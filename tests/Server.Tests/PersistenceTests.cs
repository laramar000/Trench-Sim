using System.Security.Cryptography;
using System.Text;
using Match.Protocol;
using Match.Server;
using Rules.Core.State;

namespace Server.Tests;

// Fails before one step of a durable write: with an IOException (a write failure, once or every time), or with a
// crash (any other exception: the writer stops where it is and cleans nothing up).
internal sealed class Faults : IWriteFaults
{
    public WriteStep? FailAt { get; set; }
    public bool Permanent { get; set; }
    public WriteStep? CrashAt { get; set; }

    // Runs instead of failing: lets a test damage the file just written (before the folder flush).
    public Action<string>? Tamper { get; set; }

    public int Failures { get; private set; }

    public void Before(WriteStep step, string path)
    {
        if (step == WriteStep.SyncDirectory && Tamper is { } tamper && File.Exists(path))
        {
            tamper(path);
        }
        if (step == CrashAt)
        {
            throw new SimulatedCrash();
        }
        if (step == FailAt && (Permanent || Failures == 0))
        {
            Failures++;
            throw new IOException($"simulated failure before {step}");
        }
    }
}

internal sealed class SimulatedCrash : Exception;

public sealed class PersistenceTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("trench-m6-");
    private readonly List<string> _log = [];

    public void Dispose() => _dir.Delete(recursive: true);

    private string Data => _dir.FullName;

    private (SigningKey Key, RecordStore Store) Open(IWriteFaults? faults = null)
    {
        var key = SigningKey.Load(Data);
        return (key, new RecordStore(Data, key, faults, _log.Add));
    }

    private static OpenMatch MarkerFor(MatchRecordBody body) =>
        new(body.MatchId, body.Code, body.CreatedAt, body.Scenario, body.ContentFingerprint, body.Engine);

    private static RecordVerdict VerifyWith(SigningKey key, string path) => RecordVerifier.VerifyFile(path, [key.Trusted]);

    // ---- One durable write ----

    [Fact]
    public void ADurableWriteNeverOverwritesAndLeavesNoTemporaryFile()
    {
        var path = Path.Combine(Data, "a.json");
        DurableFile.Write(path, "first"u8);
        Assert.Throws<IOException>(() => DurableFile.Write(path, "second"u8));
        Assert.Equal("first", File.ReadAllText(path));
        Assert.Single(Directory.EnumerateFiles(Data));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Theory]
    [InlineData(WriteStep.CreateTemp)]
    [InlineData(WriteStep.Write)]
    [InlineData(WriteStep.Flush)]
    [InlineData(WriteStep.Rename)]
    public void AFailedWriteLeavesNothing(WriteStep step)
    {
        var path = Path.Combine(Data, "a.json");
        Assert.Throws<IOException>(() => DurableFile.Write(path, "x"u8, new Faults { FailAt = step }));
        Assert.Empty(Directory.EnumerateFiles(Data));
    }

    // ---- Records ----

    [Fact]
    public void ARecordIsWrittenVerifiedAndItsMarkerRemoved()
    {
        var (key, store) = Open();
        var body = TestRecords.Completed();
        store.WriteMarker(MarkerFor(body));
        Assert.True(File.Exists(store.MarkerPath(body.MatchId)));

        Assert.Equal(RecordWriteOutcome.Written, store.WriteRecord(body));
        Assert.False(File.Exists(store.MarkerPath(body.MatchId)));
        Assert.Equal(VerdictKind.Completed, VerifyWith(key, store.RecordPath(body.MatchId)).Kind);
        Assert.Contains(_log, l => l.Contains("written and verified (Completed)", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecordIsNeverOverwritten()
    {
        var (_, store) = Open();
        var body = TestRecords.Completed();
        Assert.Equal(RecordWriteOutcome.Written, store.WriteRecord(body));
        var before = File.ReadAllBytes(store.RecordPath(body.MatchId));
        var aborted = TestRecords.With(body, result: new RecordResult(RecordKind.Aborted, body.Events.Count, body.Events[^1].Hash, null, "Faulted", null));
        Assert.Equal(RecordWriteOutcome.Failed, store.WriteRecord(aborted));
        Assert.Equal(before, File.ReadAllBytes(store.RecordPath(body.MatchId)));
    }

    // A transient failure (once) and a permanent one (every time), at each step: never reported as written, never a
    // Completed record, and the next start records the match as Unrecorded.
    [Theory]
    [InlineData(WriteStep.CreateTemp, false)]
    [InlineData(WriteStep.Write, false)]
    [InlineData(WriteStep.Flush, false)]
    [InlineData(WriteStep.Rename, false)]
    [InlineData(WriteStep.CreateTemp, true)]
    [InlineData(WriteStep.Write, true)]
    [InlineData(WriteStep.Flush, true)]
    [InlineData(WriteStep.Rename, true)]
    public void AFailedRecordWriteIsNeverReportedAndRecoversAsUnrecorded(WriteStep step, bool permanent)
    {
        var body = TestRecords.Completed();
        var faults = new Faults { Permanent = permanent };
        var (key, store) = Open(faults);
        store.WriteMarker(MarkerFor(body));
        faults.FailAt = step;

        Assert.Equal(RecordWriteOutcome.Failed, store.WriteRecord(body));
        Assert.False(File.Exists(store.RecordPath(body.MatchId)));
        Assert.True(File.Exists(store.MarkerPath(body.MatchId)));
        Assert.DoesNotContain(_log, l => l.Contains("written and verified", StringComparison.Ordinal));
        Assert.Contains(_log, l => l.Contains("NOT written", StringComparison.Ordinal));
        key.Dispose();

        // The next start (with a working disk).
        var (key2, store2) = Open();
        var report = store2.Recover();
        Assert.Equal([body.MatchId], report.Unrecorded);
        var verdict = VerifyWith(key2, store2.RecordPath(body.MatchId));
        Assert.Equal(VerdictKind.Unrecorded, verdict.Kind);
        Assert.False(verdict.IsCompletedMatch);
        Assert.False(File.Exists(store2.MarkerPath(body.MatchId)));
    }

    [Fact]
    public void RecoveryThatCannotWriteTheStubStopsTheServerRatherThanForget()
    {
        var body = TestRecords.Completed();
        var (_, store) = Open();
        store.WriteMarker(MarkerFor(body));
        var (_, broken) = Open(new Faults { FailAt = WriteStep.Write, Permanent = true });
        Assert.Throws<ServerDataException>(() => broken.Recover());
        Assert.True(File.Exists(store.MarkerPath(body.MatchId)));
    }

    // A crash at each step of the write (the process stops; nothing is cleaned up).
    [Theory]
    [InlineData(WriteStep.CreateTemp, false)]
    [InlineData(WriteStep.Write, false)]
    [InlineData(WriteStep.Flush, false)]
    [InlineData(WriteStep.Rename, false)]
    [InlineData(WriteStep.SyncDirectory, true)]
    public void ACrashAtAnyStepRecoversToTheRecordOrToUnrecorded(WriteStep step, bool recordSurvives)
    {
        var body = TestRecords.Completed();
        var (_, store) = Open(new Faults { CrashAt = step });
        DurableFile.Write(store.MarkerPath(body.MatchId), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(MarkerFor(body), RecordFormat.Options));
        Assert.Throws<SimulatedCrash>(() => store.WriteRecord(body));

        var (key, after) = Open();
        var report = after.Recover();
        var verdict = VerifyWith(key, after.RecordPath(body.MatchId));
        if (recordSurvives)
        {
            Assert.Empty(report.Unrecorded);
            Assert.Equal(1, report.MarkersCleared);
            Assert.Equal(VerdictKind.Completed, verdict.Kind);
        }
        else
        {
            Assert.Equal([body.MatchId], report.Unrecorded);
            Assert.Equal(VerdictKind.Unrecorded, verdict.Kind);
        }
        Assert.Empty(Directory.EnumerateFiles(after.OpenDirectory));
        Assert.DoesNotContain(Directory.EnumerateFiles(after.MatchesDirectory), DurableFile.IsTemp);
    }

    [Fact]
    public void ARecordThatDoesNotVerifyOnReadBackIsNotReportedAndRecoveryReplacesIt()
    {
        var body = TestRecords.Completed();
        var faults = new Faults();
        var (key, store) = Open(faults);
        store.WriteMarker(MarkerFor(body));
        faults.Tamper = path => File.WriteAllText(path, "{\"damaged\":true}");
        Assert.Equal(RecordWriteOutcome.Failed, store.WriteRecord(body));
        Assert.True(File.Exists(store.MarkerPath(body.MatchId)));
        Assert.Contains(_log, l => l.Contains("did NOT verify", StringComparison.Ordinal));

        var report = new RecordStore(Data, key, null, _log.Add).Recover();
        Assert.Equal([body.MatchId], report.BadRecords);
        Assert.Equal([body.MatchId], report.Unrecorded);
        Assert.True(File.Exists(store.RecordPath(body.MatchId) + ".bad"));
        Assert.Equal(VerdictKind.Unrecorded, VerifyWith(key, store.RecordPath(body.MatchId)).Kind);
    }

    [Fact]
    public void RecoveryRemovesOrphansAndSetsAsideABadMarker()
    {
        var (_, store) = Open();
        File.WriteAllText(Path.Combine(store.MatchesDirectory, ".x.json.0011.tmp"), "half");
        File.WriteAllText(Path.Combine(store.OpenDirectory, ".y.json.0022.tmp"), "half");
        File.WriteAllText(Path.Combine(store.OpenDirectory, new string('a', 32) + ".json"), "not json");
        var report = store.Recover();
        Assert.Equal(2, report.TempFilesRemoved);
        Assert.Equal(1, report.BadMarkers);
        Assert.Empty(report.Unrecorded);
        Assert.True(File.Exists(Path.Combine(store.OpenDirectory, new string('a', 32) + ".json.bad")));
    }

    [Fact]
    public void RecordsAndMarkersHoldNoSecrets()
    {
        var recorder = new CapturingRecorder();
        var harness = new Harness(1, recorder: recorder);
        var (one, two) = harness.Pair();
        Play.ToEnd(one, two, 1);
        var (key, store) = Open();
        store.WriteMarker(recorder.Started[0]);
        store.WriteRecord(recorder.Closed[0]);
        var text = string.Concat(Directory.EnumerateFiles(Data, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".key", StringComparison.Ordinal)).Select(File.ReadAllText));
        Assert.DoesNotContain(one.Token!, text, StringComparison.Ordinal);
        Assert.DoesNotContain(two.Token!, text, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", text, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat(_log), one.Token!, StringComparison.Ordinal);
        Assert.Equal(VerdictKind.Completed, VerifyWith(key, store.RecordPath(recorder.Closed[0].MatchId)).Kind);
    }

    // ---- The signing key ----

    [Fact]
    public void TheKeyIsCreatedOnceThenLoaded()
    {
        string id;
        using (var first = SigningKey.Load(Data))
        {
            Assert.True(first.Created);
            id = first.KeyId;
        }
        using var second = SigningKey.Load(Data);
        Assert.False(second.Created);
        Assert.Equal(id, second.KeyId);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(Data, "keys", "signing.key")));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(Data, "keys")));
        }
    }

    // Writes a key file owner-only, as the server does, so a test fails for the reason it is about.
    private static void WriteOwnerOnly(string path, string text)
    {
        File.Delete(path);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        using var stream = new FileStream(path, options);
        stream.Write(Encoding.ASCII.GetBytes(text));
    }

    private ServerDataException Refused(bool createNew = false) =>
        Assert.Throws<ServerDataException>(() => SigningKey.Load(Data, createNew));

    // Diagnostics name paths and modes, never the key: no PEM armour and no line of the key's base64.
    private static void AssertNoKeyIn(string message, string privatePem)
    {
        Assert.DoesNotContain("PRIVATE KEY", message, StringComparison.Ordinal);
        foreach (var line in privatePem.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith("-----", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(line, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AMissingCorruptOrMismatchedKeyStopsTheServer()
    {
        SigningKey.Load(Data).Dispose();
        var keys = Path.Combine(Data, "keys");
        var keyPath = Path.Combine(keys, "signing.key");
        var privatePem = File.ReadAllText(keyPath);

        File.Move(keyPath, Path.Combine(keys, "elsewhere"));
        var missing = Refused();
        Assert.Contains("is missing", missing.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(keyPath)); // no replacement key was made

        const string corruptPem = "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n";
        WriteOwnerOnly(keyPath, corruptPem);
        var corrupt = Refused();
        Assert.Contains("not a readable PKCS#8 PEM key", corrupt.Message, StringComparison.Ordinal);
        Assert.Null(corrupt.InnerException);
        AssertNoKeyIn(corrupt.Message, corruptPem);

        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        WriteOwnerOnly(keyPath, p384.ExportPkcs8PrivateKeyPem());
        Assert.Contains("not a P-256 key", Refused().Message, StringComparison.Ordinal);

        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherPem = other.ExportPkcs8PrivateKeyPem();
        WriteOwnerOnly(keyPath, otherPem);
        var mismatched = Refused();
        Assert.Contains("does not match", mismatched.Message, StringComparison.Ordinal);
        AssertNoKeyIn(mismatched.Message, otherPem);

        WriteOwnerOnly(keyPath, privatePem);
        using var restored = SigningKey.Load(Data);
        Assert.False(restored.Created);
        Assert.Equal(privatePem, File.ReadAllText(keyPath));
    }

    // Owner-only modes, including stricter ones than the server creates, are accepted.
    [Theory]
    [InlineData("600", "700")]
    [InlineData("400", "700")]
    [InlineData("600", "500")]
    [InlineData("400", "500")]
    public void AnOwnerOnlyKeyLoads(string fileMode, string folderMode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string id;
        using (var first = SigningKey.Load(Data))
        {
            id = first.KeyId;
        }
        var keys = Path.Combine(Data, "keys");
        File.SetUnixFileMode(Path.Combine(keys, "signing.key"), (UnixFileMode)Convert.ToInt32(fileMode, 8));
        File.SetUnixFileMode(keys, (UnixFileMode)Convert.ToInt32(folderMode, 8));
        try
        {
            using var loaded = SigningKey.Load(Data);
            Assert.False(loaded.Created);
            Assert.Equal(id, loaded.KeyId);
        }
        finally
        {
            File.SetUnixFileMode(keys, (UnixFileMode)Convert.ToInt32("700", 8));
        }
    }

    // Any group or other permission on the key stops the server. Nothing is repaired or replaced: the mode and the key
    // are left as they were, and once the owner fixes the mode the same key loads.
    [Theory]
    [InlineData("640")]
    [InlineData("644")]
    [InlineData("604")]
    [InlineData("620")]
    [InlineData("602")]
    [InlineData("610")]
    [InlineData("660")]
    [InlineData("666")]
    public void AKeyOthersCanReachStopsTheServerAndIsLeftAlone(string fileMode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string id;
        using (var first = SigningKey.Load(Data))
        {
            id = first.KeyId;
        }
        var keyPath = Path.Combine(Data, "keys", "signing.key");
        var privatePem = File.ReadAllText(keyPath);
        var mode = (UnixFileMode)Convert.ToInt32(fileMode, 8);
        File.SetUnixFileMode(keyPath, mode);

        var refused = Refused();
        Assert.Contains(keyPath, refused.Message, StringComparison.Ordinal);
        Assert.Contains($"mode 0{fileMode}", refused.Message, StringComparison.Ordinal);
        Assert.Contains("chmod 600", refused.Message, StringComparison.Ordinal);
        AssertNoKeyIn(refused.Message, privatePem);
        // --new-signing-key does not get round it either.
        Assert.Throws<ServerDataException>(() => SigningKey.Load(Data, createNew: true));
        Assert.Throws<ServerDataException>(() => ServerData.Open(Data));

        Assert.Equal(mode, File.GetUnixFileMode(keyPath));
        Assert.Equal(privatePem, File.ReadAllText(keyPath));
        Assert.Equal(["signing.key", "signing.pub"], Directory.EnumerateFiles(Path.Combine(Data, "keys")).Select(Path.GetFileName).Order());

        File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var fixedKey = SigningKey.Load(Data);
        Assert.Equal(id, fixedKey.KeyId);
    }

    [Theory]
    [InlineData("750")]
    [InlineData("755")]
    [InlineData("705")]
    [InlineData("701")]
    [InlineData("710")]
    [InlineData("770")]
    [InlineData("777")]
    public void AKeysFolderOthersCanReachStopsTheServer(string folderMode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        SigningKey.Load(Data).Dispose();
        var keys = Path.Combine(Data, "keys");
        var mode = (UnixFileMode)Convert.ToInt32(folderMode, 8);
        File.SetUnixFileMode(keys, mode);
        try
        {
            var refused = Refused();
            Assert.Contains($"{keys} has mode 0{folderMode}", refused.Message, StringComparison.Ordinal);
            Assert.Contains("chmod 700", refused.Message, StringComparison.Ordinal);
            Assert.Equal(mode, File.GetUnixFileMode(keys));

            // Retiring the key does not let a new one be made in a folder others can reach.
            File.Move(Path.Combine(keys, "signing.key"), Path.Combine(Data, "retired.key"));
            File.Move(Path.Combine(keys, "signing.pub"), Path.Combine(Data, "retired.pub"));
            Assert.Throws<ServerDataException>(() => SigningKey.Load(Data, createNew: true));
            Assert.False(File.Exists(Path.Combine(keys, "signing.key")));
        }
        finally
        {
            File.SetUnixFileMode(keys, (UnixFileMode)Convert.ToInt32("700", 8));
        }
    }

    // A link could hand the server a key, or a folder, that someone else controls. Every link is refused, even one to a
    // valid owner-only key, and a dangling link is not taken for a first start.
    [Fact]
    public void LinksToTheKeyOrItsFolderAreRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var real = Path.Combine(_dir.FullName, "real");
        Directory.CreateDirectory(real);
        string id;
        using (var created = SigningKey.Load(real))
        {
            id = created.KeyId;
        }
        var realKeys = Path.Combine(real, "keys");

        // keys/ is a link to a valid, owner-only keys folder.
        var linkedFolder = Path.Combine(_dir.FullName, "linked-folder");
        Directory.CreateDirectory(linkedFolder);
        Directory.CreateSymbolicLink(Path.Combine(linkedFolder, "keys"), realKeys);
        Assert.Contains("symbolic link", Assert.Throws<ServerDataException>(() => SigningKey.Load(linkedFolder)).Message, StringComparison.Ordinal);

        // signing.key is a link to a valid, owner-only key.
        var linkedFile = Path.Combine(_dir.FullName, "linked-file");
        var linkedKeys = Path.Combine(linkedFile, "keys");
        Directory.CreateDirectory(linkedKeys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.Copy(Path.Combine(realKeys, "signing.pub"), Path.Combine(linkedKeys, "signing.pub"));
        File.CreateSymbolicLink(Path.Combine(linkedKeys, "signing.key"), Path.Combine(realKeys, "signing.key"));
        Assert.Contains("symbolic link", Assert.Throws<ServerDataException>(() => SigningKey.Load(linkedFile)).Message, StringComparison.Ordinal);

        // A dangling signing.key link is refused, not reported as missing (and not replaced).
        File.Delete(Path.Combine(linkedKeys, "signing.key"));
        File.CreateSymbolicLink(Path.Combine(linkedKeys, "signing.key"), Path.Combine(_dir.FullName, "nowhere.key"));
        Assert.Contains("symbolic link", Assert.Throws<ServerDataException>(() => SigningKey.Load(linkedFile, createNew: true)).Message, StringComparison.Ordinal);

        // A dangling keys/ link is refused, not taken as a first start that would make a new identity.
        var dangling = Path.Combine(_dir.FullName, "dangling");
        Directory.CreateDirectory(dangling);
        Directory.CreateSymbolicLink(Path.Combine(dangling, "keys"), Path.Combine(_dir.FullName, "no-such-folder"));
        Assert.Contains("symbolic link", Assert.Throws<ServerDataException>(() => SigningKey.Load(dangling)).Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_dir.FullName, "no-such-folder")));

        // The real key is untouched.
        using var reloaded = SigningKey.Load(real);
        Assert.Equal(id, reloaded.KeyId);
    }

    // The identity survives restarts: the same key is loaded, the file is not rewritten, and no other key appears.
    [Fact]
    public void TheSigningKeyIsStableAcrossRestarts()
    {
        string id;
        using (var first = ServerData.Open(Data))
        {
            Assert.True(first.Key.Created);
            id = first.Key.KeyId;
        }
        var keyPath = Path.Combine(Data, "keys", "signing.key");
        var bytes = File.ReadAllBytes(keyPath);
        var written = File.GetLastWriteTimeUtc(keyPath);
        for (var restart = 0; restart < 3; restart++)
        {
            using var again = ServerData.Open(Data);
            Assert.False(again.Key.Created);
            Assert.Equal(id, again.Key.KeyId);
        }
        Assert.Equal(bytes, File.ReadAllBytes(keyPath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(keyPath));
        Assert.Equal(["signing.key", "signing.pub"], Directory.EnumerateFiles(Path.Combine(Data, "keys")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void ANewKeyIsMadeOnlyAfterTheOldOneIsRetired()
    {
        string oldId;
        using (var old = SigningKey.Load(Data))
        {
            oldId = old.KeyId;
        }
        var keys = Path.Combine(Data, "keys");
        Assert.Throws<ServerDataException>(() => SigningKey.Load(Data, createNew: true));

        Directory.CreateDirectory(Path.Combine(keys, "retired"));
        File.Move(Path.Combine(keys, "signing.pub"), Path.Combine(keys, "retired", "old.pub"));
        File.Delete(Path.Combine(keys, "signing.key"));
        using var fresh = SigningKey.Load(Data, createNew: true);
        Assert.True(fresh.Created);
        Assert.NotEqual(oldId, fresh.KeyId);
    }

    [Fact]
    public void OnlyOneServerUsesADataFolder()
    {
        using var first = ServerData.Open(Data);
        Assert.Throws<ServerDataException>(() => ServerData.Open(Data));
    }

    // ---- Through the Lobby ----

    [Fact]
    public async Task TheLobbyRecordsFinishedAbortedAndUnstartedMatchesCorrectly()
    {
        using var data = ServerData.Open(Data);
        var harness = new Harness(3, recorder: data.Writer);

        var (one, two) = harness.Pair("melee");
        Play.ToEnd(one, two, 3);
        var finished = one.Welcome!.MatchId;

        var (three, four) = harness.Pair();
        Play.Commands(three, four, 3, 5);
        harness.Lobby.Disconnect(four.Id);
        var aborted = three.Welcome!.MatchId;

        var lonely = harness.Connect();
        lonely.Send(new CreateMatch("ranged"));
        harness.Lobby.Disconnect(lonely.Id);
        var unstarted = lonely.Welcome!.MatchId;

        Assert.True(await data.Writer.DrainAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(VerdictKind.Completed, VerifyWith(data.Key, data.Store.RecordPath(finished)).Kind);
        var abortedVerdict = VerifyWith(data.Key, data.Store.RecordPath(aborted));
        Assert.Equal(VerdictKind.Aborted, abortedVerdict.Kind);
        Assert.Equal(nameof(AbortReason.Disconnected), abortedVerdict.Body!.Result.Reason);
        Assert.Equal(Seat.Two, abortedVerdict.Body.Result.Seat);
        Assert.False(File.Exists(data.Store.RecordPath(unstarted)));
        Assert.Empty(Directory.EnumerateFiles(data.Store.OpenDirectory));
        Assert.All(data.Writer.Results, r => Assert.Equal(RecordWriteOutcome.Written, r.Outcome));
    }

    [Fact]
    public async Task AMatchInterruptedWithoutARecordIsUnrecordedAtTheNextStart()
    {
        string matchId;
        using (var data = ServerData.Open(Data))
        {
            var harness = new Harness(3, recorder: data.Writer);
            var (one, two) = harness.Pair();
            Play.Commands(one, two, 3, 8);
            matchId = one.Welcome!.MatchId;
            Assert.True(await data.Writer.DrainAsync(TimeSpan.FromSeconds(30)));
            Assert.True(File.Exists(data.Store.MarkerPath(matchId)));
            // The process dies here: no abort, no record.
        }

        using var restarted = ServerData.Open(Data);
        Assert.Equal([matchId], restarted.Recovery.Unrecorded);
        var verdict = VerifyWith(restarted.Key, restarted.Store.RecordPath(matchId));
        Assert.Equal(VerdictKind.Unrecorded, verdict.Kind);
        Assert.False(verdict.IsCompletedMatch);
    }

    [Fact]
    public void TheMarkerHoldsOnlyPublicFacts()
    {
        var recorder = new CapturingRecorder();
        var harness = new Harness(1, recorder: recorder);
        var (one, _) = harness.Pair();
        var marker = Encoding.UTF8.GetString(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(recorder.Started[0], RecordFormat.Options));
        Assert.DoesNotContain(one.Token!, marker, StringComparison.Ordinal);
        Assert.Contains(one.Welcome!.MatchId, marker, StringComparison.Ordinal);
    }
}
