using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Match.Protocol;

namespace Match.Server;

// A match that has started and has no durable record yet. Written when the second player joins, removed once the
// match's record is durable and verified. One left behind at start-up means the record was lost.
public sealed record OpenMatch(
    string MatchId,
    string Code,
    DateTimeOffset CreatedAt,
    string Scenario,
    string ContentFingerprint,
    RecordEngine Engine);

// Why the server could not start with its data folder. It never works around these.
public sealed class ServerDataException(string message, Exception? inner = null) : Exception(message, inner);

// The server's signing key, in <data>/keys. It is created only on the first start (no keys folder yet) or when asked
// for explicitly; a missing or damaged key otherwise stops the server, so it never quietly takes on a new identity.
//
// Permissions (where Unix file modes exist; guaranteed and tested on Linux, best effort on macOS, not checked on
// Windows): keys/ and keys/signing.key must grant no group or other permission (0700 and 0600 as created, or
// stricter), and neither may be a symbolic link. Anything else stops the server; it never repairs the modes, and the
// message names the path and mode only, never the key.
public sealed class SigningKey : IDisposable
{
    private const UnixFileMode GroupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private SigningKey(ECDsa key)
    {
        Key = key;
        KeyId = RecordKeys.KeyIdOf(key);
        PublicPem = key.ExportSubjectPublicKeyInfoPem();
    }

    public ECDsa Key { get; }
    public string KeyId { get; }
    public string PublicPem { get; }
    public bool Created { get; private init; }

    public TrustedKey Trusted => new(KeyId, Key);

    public static SigningKey Load(string dataDirectory, bool createNew = false, IWriteFaults? faults = null)
    {
        var keys = Path.Combine(dataDirectory, "keys");
        var privatePath = Path.Combine(keys, "signing.key");
        var publicPath = Path.Combine(keys, "signing.pub");
        RefuseLink(keys);
        var firstStart = !Directory.Exists(keys);
        if (!firstStart)
        {
            if (!OperatingSystem.IsWindows())
            {
                RefuseShared(keys, File.GetUnixFileMode(keys));
            }
            RefuseLink(privatePath);
        }
        if (firstStart || (createNew && !File.Exists(privatePath)))
        {
            if (!firstStart && File.Exists(publicPath))
            {
                throw new ServerDataException($"{publicPath} still exists. Move it to keys/retired/ before creating a new signing key.");
            }
            CreateDirectory(keys);
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            DurableFile.Write(privatePath, System.Text.Encoding.ASCII.GetBytes(key.ExportPkcs8PrivateKeyPem()), faults);
            DurableFile.Write(publicPath, System.Text.Encoding.ASCII.GetBytes(key.ExportSubjectPublicKeyInfoPem()), faults,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            return new SigningKey(key) { Created = true };
        }
        if (createNew)
        {
            throw new ServerDataException($"{privatePath} exists. A new signing key is created only after the old one is retired.");
        }
        if (!File.Exists(privatePath))
        {
            throw new ServerDataException($"The signing key {privatePath} is missing. Restore it, or retire the old key and start with --new-signing-key.");
        }

        string pem;
        try
        {
            using var handle = File.OpenHandle(privatePath, FileMode.Open, FileAccess.Read);
            if (!OperatingSystem.IsWindows())
            {
                // Checked on the open file, so the mode is the mode of what is read.
                RefuseShared(privatePath, File.GetUnixFileMode(handle));
            }
            using var stream = new FileStream(handle, FileAccess.Read);
            using var reader = new StreamReader(stream, System.Text.Encoding.ASCII);
            pem = reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ServerDataException($"The signing key {privatePath} cannot be read ({e.GetType().Name}).");
        }

        var loaded = ECDsa.Create();
        try
        {
            loaded.ImportFromPem(pem);
            _ = loaded.ExportParameters(includePrivateParameters: true);
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException or IOException or UnauthorizedAccessException)
        {
            loaded.Dispose();
            // The parser's message is not passed on: it is about the key's contents.
            throw new ServerDataException($"The signing key {privatePath} is not a readable PKCS#8 PEM key ({e.GetType().Name}).");
        }
        if (!RecordKeys.IsP256(loaded))
        {
            loaded.Dispose();
            throw new ServerDataException($"The signing key {privatePath} is not a P-256 key.");
        }
        var signing = new SigningKey(loaded);
        var published = File.Exists(publicPath) ? RecordKeys.FromPem(File.ReadAllText(publicPath)) : null;
        if (published?.KeyId != signing.KeyId)
        {
            loaded.Dispose();
            throw new ServerDataException($"{publicPath} is missing or does not match the signing key.");
        }
        return signing;
    }

    // A link could point the server at a key, or a folder, that someone else controls.
    private static void RefuseLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null)
        {
            throw new ServerDataException($"{path} is a symbolic link. The signing key and its folder must be real files; replace the link with the file it points to.");
        }
    }

    private static void RefuseShared(string path, UnixFileMode mode)
    {
        if ((mode & GroupOrOther) != 0)
        {
            throw new ServerDataException(
                $"{path} has mode {Convert.ToString((int)mode, 8).PadLeft(4, '0')}, which lets other users reach the signing key. " +
                $"It must grant no group or other permission (chmod {(Directory.Exists(path) ? "700" : "600")}). The server does not change it for you.");
        }
    }

    internal static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public void Dispose() => Key.Dispose();
}

public enum RecordWriteOutcome
{
    // Durable, read back and verified as the kind that was written. Only now is the record reported as written.
    Written,

    // Not written, or not verified on read-back. The open-match marker stays, so the next start records the match as
    // Unrecorded.
    Failed,
}

// What start-up recovery did.
public sealed record RecoveryReport(int TempFilesRemoved, int MarkersCleared, IReadOnlyList<string> Unrecorded, IReadOnlyList<string> BadRecords, int BadMarkers);

// Match records and open-match markers in <data>/matches. Not thread-safe: the RecordWriter calls it from one task.
public sealed class RecordStore
{
    private readonly SigningKey _key;
    private readonly IWriteFaults? _faults;
    private readonly Action<string> _log;
    private readonly TimeProvider _time;

    public RecordStore(string dataDirectory, SigningKey key, IWriteFaults? faults = null, Action<string>? log = null, TimeProvider? time = null)
    {
        _key = key;
        _faults = faults;
        _log = log ?? (_ => { });
        _time = time ?? TimeProvider.System;
        MatchesDirectory = Path.Combine(dataDirectory, "matches");
        OpenDirectory = Path.Combine(MatchesDirectory, "open");
        SigningKey.CreateDirectory(MatchesDirectory);
        SigningKey.CreateDirectory(OpenDirectory);
    }

    public string MatchesDirectory { get; }
    public string OpenDirectory { get; }

    public static string Build { get; } = ReadBuild();

    public string RecordPath(string matchId) => Path.Combine(MatchesDirectory, MatchIds.Check(matchId, nameof(matchId)) + ".json");

    public string MarkerPath(string matchId) => Path.Combine(OpenDirectory, MatchIds.Check(matchId, nameof(matchId)) + ".json");

    public void WriteMarker(OpenMatch match)
    {
        DurableFile.Write(MarkerPath(match.MatchId), JsonSerializer.SerializeToUtf8Bytes(match, RecordFormat.Options), _faults);
    }

    // Signs and writes a record, reads it back and verifies it with the server's own key, and only then removes the
    // marker. Fails closed: any problem leaves the marker for recovery and reports Failed.
    public RecordWriteOutcome WriteRecord(MatchRecordBody body)
    {
        var path = RecordPath(body.MatchId);
        try
        {
            DurableFile.Write(path, RecordFormat.Sign(body, _key.Key), _faults);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log($"record {body.MatchId}: NOT written ({e.GetType().Name}); it will be recorded as Unrecorded at the next start");
            return RecordWriteOutcome.Failed;
        }
        var expected = body.Result.Kind switch
        {
            RecordKind.Completed => VerdictKind.Completed,
            RecordKind.Aborted => VerdictKind.Aborted,
            _ => VerdictKind.Unrecorded,
        };
        if (!VerifiesAs(path, body.MatchId, expected, out var verdict))
        {
            _log($"record {body.MatchId}: written but did NOT verify on read-back ({verdict}); the marker stays");
            return RecordWriteOutcome.Failed;
        }
        try
        {
            if (File.Exists(MarkerPath(body.MatchId)))
            {
                DurableFile.DeleteDurably(MarkerPath(body.MatchId));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The record is durable and verified; a stale marker is cleared at the next start.
            _log($"record {body.MatchId}: written and verified; the marker could not be removed ({e.GetType().Name})");
        }
        _log($"record {body.MatchId}: written and verified ({expected})");
        return RecordWriteOutcome.Written;
    }

    // Start-up: removes leftover temporary files, then settles every open-match marker. A marker whose record exists and
    // verifies is cleared; otherwise the record (if any) is set aside as .bad and a signed Unrecorded stub takes its place.
    public RecoveryReport Recover()
    {
        var temps = 0;
        foreach (var dir in new[] { MatchesDirectory, OpenDirectory })
        {
            foreach (var file in Directory.EnumerateFiles(dir).Where(DurableFile.IsTemp).ToList())
            {
                File.Delete(file);
                temps++;
            }
        }

        var cleared = 0;
        var badMarkers = 0;
        var unrecorded = new List<string>();
        var badRecords = new List<string>();
        foreach (var markerPath in Directory.EnumerateFiles(OpenDirectory, "*.json").Order(StringComparer.Ordinal).ToList())
        {
            OpenMatch? marker;
            try
            {
                marker = JsonSerializer.Deserialize<OpenMatch>(File.ReadAllBytes(markerPath), RecordFormat.Options);
                if (marker is null || Path.GetFileNameWithoutExtension(markerPath) != marker.MatchId)
                {
                    throw new JsonException("The marker does not name its own match.");
                }
                MatchIds.Check(marker.MatchId, nameof(marker.MatchId));
            }
            catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException)
            {
                File.Move(markerPath, markerPath + ".bad", overwrite: true);
                _log($"recovery: unreadable marker {Path.GetFileName(markerPath)} set aside");
                badMarkers++;
                continue;
            }

            var recordPath = RecordPath(marker.MatchId);
            if (File.Exists(recordPath))
            {
                if (VerifiesAs(recordPath, marker.MatchId, null, out _))
                {
                    DurableFile.DeleteDurably(markerPath);
                    cleared++;
                    continue;
                }
                File.Move(recordPath, recordPath + ".bad", overwrite: true);
                DurableFile.SyncDirectory(MatchesDirectory);
                badRecords.Add(marker.MatchId);
                _log($"recovery: record {marker.MatchId} did not verify; set aside as .bad");
            }

            var stub = new MatchRecordBody(marker.MatchId, marker.Code, marker.CreatedAt, _time.GetUtcNow(), marker.Engine,
                marker.Scenario, marker.ContentFingerprint, [], new RecordResult(RecordKind.Unrecorded, 0, null, null, "RecordLost", null));
            if (WriteRecord(stub) != RecordWriteOutcome.Written)
            {
                throw new ServerDataException($"Could not record the lost match {marker.MatchId} as Unrecorded.");
            }
            unrecorded.Add(marker.MatchId);
            _log($"recovery: match {marker.MatchId} started but has no record; recorded as Unrecorded");
        }
        return new RecoveryReport(temps, cleared, unrecorded, badRecords, badMarkers);
    }

    // True when the file verifies with this server's key, for this match, as the expected kind (or as any kind of
    // genuine record when expected is null).
    private bool VerifiesAs(string path, string matchId, VerdictKind? expected, out VerdictKind verdict)
    {
        RecordVerdict result;
        try
        {
            result = RecordVerifier.VerifyFile(path, [_key.Trusted]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            verdict = VerdictKind.Invalid;
            return false;
        }
        verdict = result.Kind;
        var genuine = result.Kind is VerdictKind.Completed or VerdictKind.Aborted or VerdictKind.Unrecorded;
        return genuine && result.Body?.MatchId == matchId && (expected is null || expected == result.Kind);
    }

    private static string ReadBuild()
    {
        var version = typeof(RecordStore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        var build = plus >= 0 ? version[(plus + 1)..] : "unknown";
        return build.Length is >= 1 and <= ProtocolLimits.MaxIdLength && build.All(c => c is > ' ' and <= '~') ? build : "unknown";
    }
}
