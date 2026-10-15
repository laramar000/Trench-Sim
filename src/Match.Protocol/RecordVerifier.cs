using System.Security.Cryptography;
using System.Text.Json;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Match.Protocol;

public enum VerdictKind
{
    // Signed by a trusted key, every event legal and hashed as stored, and ended by the rules with MatchEnded.
    Completed,

    // Signed and consistent, but the match stopped before the rules ended it. Not a finished match.
    Aborted,

    // Signed stub: the match started on that server and its record was lost. Not a finished match.
    Unrecorded,

    // Provably wrong: malformed, a bad signature, an illegal event, a hash or result that does not match.
    Invalid,

    // This verifier cannot judge it: an untrusted key, or a format, algorithm or rules version it does not support.
    Unverifiable,
}

public enum VerdictReason
{
    None,
    Malformed,
    UnsupportedRecordVersion,
    UnsupportedAlgorithm,
    UntrustedKey,
    BadSignature,
    UnsupportedRules,
    BadSetup,
    BadSequence,
    IllegalEvent,
    HashMismatch,
    NotCompleted,
    ResultMismatch,
}

public sealed record RecordVerdict(
    VerdictKind Kind,
    VerdictReason Reason,
    string Detail,
    MatchRecordBody? Body = null,
    string? FinalHash = null)
{
    // The only verdict that means a finished match. Aborted, Unrecorded, Invalid and Unverifiable never do.
    public bool IsCompletedMatch => Kind == VerdictKind.Completed;
}

// Checks a saved record. It reads the bytes only: it never contacts a server and never puts a record into play. It
// never throws for anything in the file; every problem is a verdict with one reason.
//
// What a Completed verdict proves: a trusted key signed exactly this record, and every event in it is one the rules
// could have produced from the recorded dice, with the stored hashes and result. It does not prove the dice were rolled
// fairly; the server that holds the key chose them. See docs/records.md.
public static class RecordVerifier
{
    public static RecordVerdict Verify(ReadOnlySpan<byte> file, IReadOnlyCollection<TrustedKey> trusted)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        try
        {
            return VerifyCore(file, trusted);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A last guard: nothing in a file may crash the verifier.
            return Invalid(VerdictReason.Malformed, "The record could not be read.");
        }
    }

    public static RecordVerdict VerifyFile(string path, IReadOnlyCollection<TrustedKey> trusted)
    {
        var info = new FileInfo(path);
        if (info.Length > RecordFormat.MaxBytes)
        {
            return Invalid(VerdictReason.Malformed, $"A record is at most {RecordFormat.MaxBytes} bytes.");
        }
        return Verify(File.ReadAllBytes(path), trusted);
    }

    private static RecordVerdict VerifyCore(ReadOnlySpan<byte> file, IReadOnlyCollection<TrustedKey> trusted)
    {
        // 1. Size, UTF-8, JSON, exactly the four top-level fields.
        if (file.Length > RecordFormat.MaxBytes)
        {
            return Invalid(VerdictReason.Malformed, $"A record is at most {RecordFormat.MaxBytes} bytes.");
        }
        if (!System.Text.Unicode.Utf8.IsValid(file))
        {
            return Invalid(VerdictReason.Malformed, "A record is UTF-8 text.");
        }
        if (!TryReadTop(file, out var top, out var problem))
        {
            return Invalid(VerdictReason.Malformed, problem);
        }

        // 2. Format and version.
        if (top.Format != RecordFormat.Format || top.RecordVersion != RecordFormat.RecordVersion)
        {
            return Unverifiable(VerdictReason.UnsupportedRecordVersion,
                $"This verifier reads {RecordFormat.Format} version {RecordFormat.RecordVersion}.");
        }

        // 3. Signature shape and algorithm.
        var signature = top.Signature;
        if (signature.Alg != RecordFormat.Algorithm)
        {
            return Unverifiable(VerdictReason.UnsupportedAlgorithm, $"This verifier checks {RecordFormat.Algorithm} signatures only.");
        }
        var signatureBytes = RecordFormat.FromBase64Url(signature.Value);
        if (!RecordKeys.IsSha256Id(signature.KeyId) || signatureBytes is null || signatureBytes.Length != 64)
        {
            return Invalid(VerdictReason.Malformed, "The signature is not a well-formed ES256 signature.");
        }

        // 4. A key the caller trusts.
        var key = trusted.FirstOrDefault(k => k.KeyId == signature.KeyId);
        if (key is null)
        {
            return Unverifiable(VerdictReason.UntrustedKey, $"No trusted key has id {signature.KeyId}.");
        }

        // 5. The signature over the stored bytes.
        var body = file[top.BodyStart..top.BodyEnd];
        var signed = RecordFormat.SignedBytes(top.RecordVersion, signature.KeyId, body);
        bool good;
        try
        {
            good = key.Key.VerifyData(signed, signatureBytes, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            good = false;
        }
        if (!good)
        {
            return Invalid(VerdictReason.BadSignature, "The signature does not match the record.");
        }

        // 6. The body, strictly.
        MatchRecordBody record;
        try
        {
            record = JsonSerializer.Deserialize<MatchRecordBody>(body, RecordFormat.Options)
                ?? throw new JsonException("The body is null.");
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return Invalid(VerdictReason.Malformed, "The body is not a well-formed record body.");
        }

        // 7. The rules this build replays. Never verify a record under rules other than its own.
        var engine = record.Engine;
        if (engine.RulesVersion != RecordFormat.RulesVersion
            || engine.StateHashVersion != RecordFormat.StateHashVersion
            || engine.ReplayCompatibility != RecordFormat.ReplayCompatibility)
        {
            return Unverifiable(VerdictReason.UnsupportedRules,
                $"The record was made under rules {engine.RulesVersion}, hash version {engine.StateHashVersion}, replay version {engine.ReplayCompatibility}; " +
                $"this verifier replays rules {RecordFormat.RulesVersion}, hash version {RecordFormat.StateHashVersion}, replay version {RecordFormat.ReplayCompatibility}. " +
                $"Use the build that wrote it ({engine.Build}).", record);
        }

        var result = record.Result;

        // 8. A stub for a lost record: nothing but the fact that the match started.
        if (result.Kind == RecordKind.Unrecorded)
        {
            return record.Events.Count == 0 && result.Events == 0 && result.Hash is null && result.Winner is null
                   && result.Seat is null && result.Reason == "RecordLost"
                ? new RecordVerdict(VerdictKind.Unrecorded, VerdictReason.None, "The match started, but its record was lost. It is not a completed match.", record)
                : Invalid(VerdictReason.ResultMismatch, "An unrecorded stub has no events and no result.", record);
        }

        // 9. The setup.
        if (record.Events.Count == 0 || record.Events[0].Event is not MatchCreated created)
        {
            return Invalid(VerdictReason.BadSetup, "The first event is not MatchCreated.", record);
        }
        if (record.Events.Skip(1).Any(e => e.Event is MatchCreated))
        {
            return Invalid(VerdictReason.BadSetup, "A match has exactly one MatchCreated, first.", record);
        }
        if (RecordFormat.ContentFingerprint(created.Setup) != record.ContentFingerprint)
        {
            return Invalid(VerdictReason.BadSetup, "The content fingerprint does not match the setup.", record);
        }

        // 10. Every event, in order, through the reducer, against its stored hash.
        var state = MatchState.None;
        for (var i = 0; i < record.Events.Count; i++)
        {
            var entry = record.Events[i];
            if (entry.Seq != i + 1)
            {
                return Invalid(VerdictReason.BadSequence, $"Entry {i + 1} is numbered {entry.Seq}.", record);
            }
            try
            {
                state = Reducer.Apply(state, entry.Event);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException)
            {
                return Invalid(VerdictReason.IllegalEvent, $"Event {entry.Seq} ({entry.Event.GetType().Name}) does not apply.", record);
            }
            if (StateHash.Compute(state) != entry.Hash)
            {
                return Invalid(VerdictReason.HashMismatch, $"The state after event {entry.Seq} does not have the stored hash.", record);
            }
        }

        // 11. The result agrees with the replayed end.
        var last = record.Events[^1];
        if (result.Events != record.Events.Count || result.Hash != last.Hash)
        {
            return Invalid(VerdictReason.ResultMismatch, "The result's event count or hash is not the last event's.", record);
        }
        var ended = last.Event is MatchEnded;
        switch (result.Kind)
        {
            case RecordKind.Completed:
                if (!ended || state.Phase != Phase.Ended || state.Outcome is null)
                {
                    return Invalid(VerdictReason.NotCompleted, "The record claims a completed match, but its log does not end with MatchEnded.", record);
                }
                if (result.Winner != state.Outcome.Winner || result.Reason != state.Outcome.Reason.ToString() || result.Seat is not null)
                {
                    return Invalid(VerdictReason.ResultMismatch, "The stated result is not the outcome the log ends with.", record);
                }
                return new RecordVerdict(VerdictKind.Completed, VerdictReason.None,
                    $"Completed: {(state.Outcome.Winner is { } w ? $"Seat {w} won" : "a draw")} ({state.Outcome.Reason}).", record, last.Hash);
            case RecordKind.Aborted:
                if (ended || state.Phase == Phase.Ended || state.Outcome is not null)
                {
                    return Invalid(VerdictReason.ResultMismatch, "An aborted record cannot end with MatchEnded.", record);
                }
                if (result.Winner is not null || !Enum.TryParse<AbortReason>(result.Reason, ignoreCase: false, out var reason)
                    || !Enum.IsDefined(reason) || result.Reason != reason.ToString())
                {
                    return Invalid(VerdictReason.ResultMismatch, "An aborted record has an abort reason and no winner.", record);
                }
                return new RecordVerdict(VerdictKind.Aborted, VerdictReason.None,
                    $"Aborted ({reason}) after {result.Events} events. It is not a completed match: no winner and no draw.", record, last.Hash);
            default:
                return Invalid(VerdictReason.Malformed, "Unknown result kind.", record);
        }
    }

    private sealed record Top(string Format, int RecordVersion, int BodyStart, int BodyEnd, RecordSignature Signature);

    // Reads the outer object without re-serialising anything: the body is located by its byte positions.
    private static bool TryReadTop(ReadOnlySpan<byte> file, out Top top, out string problem)
    {
        top = null!;
        problem = "The record is not well-formed JSON with exactly format, recordVersion, body and signature.";
        try
        {
            var reader = new Utf8JsonReader(file, new JsonReaderOptions
            {
                MaxDepth = RecordFormat.MaxDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }
            string? format = null;
            int? version = null;
            int bodyStart = -1, bodyEnd = -1;
            RecordSignature? signature = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (!reader.Read())
                {
                    return false;
                }
                switch (name)
                {
                    case "format" when format is null && reader.TokenType == JsonTokenType.String:
                        format = reader.GetString();
                        break;
                    case "recordVersion" when version is null && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var v):
                        version = v;
                        break;
                    case "body" when bodyStart < 0 && reader.TokenType == JsonTokenType.StartObject:
                        bodyStart = checked((int)reader.TokenStartIndex);
                        reader.Skip();
                        bodyEnd = checked((int)reader.BytesConsumed);
                        break;
                    case "signature" when signature is null && reader.TokenType == JsonTokenType.StartObject:
                        signature = JsonSerializer.Deserialize<RecordSignature>(ref reader, RecordFormat.Options);
                        break;
                    default:
                        return false;
                }
            }
            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                return false;
            }
            if (format is null || version is null || bodyStart < 0 || signature is null)
            {
                return false;
            }
            top = new Top(format, version.Value, bodyStart, bodyEnd, signature);
            return true;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    private static RecordVerdict Invalid(VerdictReason reason, string detail, MatchRecordBody? body = null) =>
        new(VerdictKind.Invalid, reason, detail, body);

    private static RecordVerdict Unverifiable(VerdictReason reason, string detail, MatchRecordBody? body = null) =>
        new(VerdictKind.Unverifiable, reason, detail, body);
}
