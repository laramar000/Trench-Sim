using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Scenarios;
using Rules.Core.State;

namespace Match.Protocol;

// A match record: the server's signed account of one match, written once when the match closes. It is for
// verification only and is never loaded back into play.
//
// The file is {"format":..., "recordVersion":1, "body":{...}, "signature":{...}}. The signature covers the exact bytes
// of "body" as they appear in the file, after a prefix that names the format, the record version and the key, so
// nothing is re-serialised to check it.
public static class RecordFormat
{
    public const string Format = "trench-sim.match-record";
    public const int RecordVersion = 1;

    // Bumped whenever a change to the engine changes what a stored log means (which events replay, or to which
    // state), even if the rules version and the hash version stay the same. The golden-record tests fail until it is.
    public const int ReplayCompatibility = 1;

    public const string Algorithm = "ES256";
    public const int MaxBytes = 16 * 1024 * 1024;
    public const int MaxEvents = 100_000;

    // The protocol's depth limit plus the record's own nesting (body, events, entry).
    public const int MaxDepth = 24;

    // The rules and hash this build replays. A record made under anything else is unverifiable here.
    public static string RulesVersion => ClaimNoMansLand.RulesVersion;
    public static int StateHashVersion => StateHash.Version;

    public static JsonSerializerOptions Options { get; } = new(Wire.Options) { MaxDepth = MaxDepth };

    public static RecordEngine CurrentEngine(string build) =>
        new(RulesVersion, StateHashVersion, ReplayCompatibility, ProtocolLimits.Version, build);

    public static string ContentFingerprint(MatchSetup setup) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(setup, Wire.Options)));

    public static byte[] EncodeBody(MatchRecordBody body) => JsonSerializer.SerializeToUtf8Bytes(body, Options);

    // What the signature is over: the domain prefix, then the body bytes exactly as stored.
    public static byte[] SignedBytes(int recordVersion, string keyId, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.ASCII.GetBytes($"{Format}\n{recordVersion}\n{keyId}\n");
        var message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message.AsSpan(prefix.Length));
        return message;
    }

    // Signs a body and lays out the whole file. The key must be a P-256 private key.
    public static byte[] Sign(MatchRecordBody body, ECDsa key)
    {
        var bodyBytes = EncodeBody(body);
        var keyId = RecordKeys.KeyIdOf(key);
        var signature = key.SignData(SignedBytes(RecordVersion, keyId, bodyBytes), HashAlgorithmName.SHA256);
        return Assemble(RecordVersion, bodyBytes, new RecordSignature(Algorithm, keyId, Base64Url(signature)));
    }

    public static byte[] Assemble(int recordVersion, ReadOnlySpan<byte> body, RecordSignature signature)
    {
        using var output = new MemoryStream();
        output.Write(Encoding.UTF8.GetBytes($"{{\"format\":\"{Format}\",\"recordVersion\":{recordVersion},\"body\":"));
        output.Write(body);
        output.Write(",\n\"signature\":"u8);
        output.Write(JsonSerializer.SerializeToUtf8Bytes(signature, Options));
        output.Write("}\n"u8);
        return output.ToArray();
    }

    public static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[]? FromBase64Url(string text)
    {
        if (text.Length > 1024 || text.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            return null;
        }
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => null };
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

// Which engine wrote the record. RulesVersion, StateHashVersion and ReplayCompatibility decide whether this build can
// verify it; Build is the writer's commit, for reproducing an old verification.
public sealed record RecordEngine
{
    public RecordEngine(string rulesVersion, int stateHashVersion, int replayCompatibility, int protocolVersion, string build)
    {
        RulesVersion = Ids.Check(rulesVersion, nameof(rulesVersion));
        StateHashVersion = stateHashVersion;
        ReplayCompatibility = replayCompatibility;
        ProtocolVersion = protocolVersion;
        Build = Ids.Check(build, nameof(build));
    }

    public string RulesVersion { get; }
    public int StateHashVersion { get; }
    public int ReplayCompatibility { get; }
    public int ProtocolVersion { get; }
    public string Build { get; }
}

public sealed record RecordedEvent(int Seq, GameEvent Event, string Hash);

public enum RecordKind
{
    // The log ends with MatchEnded: the rules decided the match.
    Completed,

    // The match stopped before the rules ended it. Not a draw and not a victory.
    Aborted,

    // The match started, but its record was lost (a crash or a failed write). Only that much is attested.
    Unrecorded,
}

// How a record ends. Completed: Winner (null for a draw) and Reason, an OutcomeReason. Aborted: Reason, an
// AbortReason, and Seat when a seat caused it. Unrecorded: Reason "RecordLost", no events and no hash.
public sealed record RecordResult(RecordKind Kind, int Events, string? Hash, Seat? Winner, string Reason, Seat? Seat);

public sealed record MatchRecordBody
{
    public MatchRecordBody(
        string matchId,
        string code,
        DateTimeOffset createdAt,
        DateTimeOffset closedAt,
        RecordEngine engine,
        string scenario,
        string contentFingerprint,
        IEnumerable<RecordedEvent> events,
        RecordResult result)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(result);
        MatchId = MatchIds.Check(matchId, nameof(matchId));
        Code = Ids.Check(code, nameof(code));
        CreatedAt = createdAt;
        ClosedAt = closedAt;
        Engine = engine;
        Scenario = Ids.Check(scenario, nameof(scenario));
        ContentFingerprint = RecordKeys.IsSha256Id(contentFingerprint)
            ? contentFingerprint
            : throw new ArgumentException("A content fingerprint is sha256:<64 hex>.", nameof(contentFingerprint));
        Events = [.. events];
        if (Events.Count > RecordFormat.MaxEvents)
        {
            throw new ArgumentException("Too many events.", nameof(events));
        }
        Result = result;
    }

    public string MatchId { get; }
    public string Code { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ClosedAt { get; }
    public RecordEngine Engine { get; }
    public string Scenario { get; }
    public string ContentFingerprint { get; }
    public IReadOnlyList<RecordedEvent> Events { get; }
    public RecordResult Result { get; }
}

public sealed record RecordSignature(string Alg, string KeyId, string Value);

// Match ids: 32 lowercase hex characters (128 random bits).
public static class MatchIds
{
    public static string New() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public static string Check(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length != 32 || !value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new ArgumentException($"{name} is 32 lowercase hex characters.", name);
        }
        return value;
    }
}

// Public keys and their ids. A key id is "sha256:" and the hex SHA-256 of the key's SubjectPublicKeyInfo.
public static class RecordKeys
{
    private const string P256 = "1.2.840.10045.3.1.7";

    public static string KeyIdOf(ECDsa key) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    // "sha256:" and 64 lowercase hex characters.
    public static bool IsSha256Id(string? value) =>
        value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    public static bool IsP256(ECDsa key)
    {
        try
        {
            return key.KeySize == 256 && key.ExportParameters(false).Curve.Oid?.Value == P256;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    // A public key from PEM text, or null when it is not a P-256 public key.
    public static TrustedKey? FromPem(string pem)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(pem);
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            key.Dispose();
            return null;
        }
        if (!IsP256(key))
        {
            key.Dispose();
            return null;
        }
        return new TrustedKey(KeyIdOf(key), key);
    }
}

// A public key the verifier was told to trust. Trust only ever comes from the caller, never from a record.
public sealed record TrustedKey(string KeyId, ECDsa Key);
