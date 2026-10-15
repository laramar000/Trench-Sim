using System.Security.Cryptography;
using Match.Cli;
using Match.Client;
using Match.Protocol;
using Rules.Core.State;

namespace Server.Tests;

// The client's saved seats (A5) and trench-cli verify (D6).
public sealed class CliM6Tests : IDisposable
{
    private const string Server = "ws://127.0.0.1:5080/match";
    private static readonly string Token = RecordFormat.Base64Url(new byte[32]);
    private static readonly string MatchId = new('c', 32);

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("trench-m6-cli-");

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(_dir.FullName, "*", SearchOption.AllDirectories).Append(_dir.FullName))
            {
                File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        _dir.Delete(recursive: true);
    }

    private SessionStore Store => new(Path.Combine(_dir.FullName, "sessions"));

    private static SavedSeat Seat2(string token) => new(Server, "CODE1", MatchId, Seat.Two, token);

    // ---- Saved seats ----

    [Fact]
    public void ASeatIsSavedOwnerOnlyAndFoundAgain()
    {
        Assert.True(Store.TrySave(Seat2(Token), out _));
        var found = Store.Find(Server, "CODE1");
        Assert.Equal(SavedSeatStatus.Found, found.Status);
        Assert.Equal(Seat2(Token), found.Seat);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Store.PathOf(MatchId, Seat.Two)));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Store.Directory));
        }

        // Replacing the token leaves exactly the new one, never a mix.
        var newer = RecordFormat.Base64Url(RandomNumberGenerator.GetBytes(32));
        Assert.True(Store.TrySave(Seat2(newer), out _));
        Assert.Equal(newer, Store.Find(Server, "CODE1").Seat!.Token);
        Assert.Single(Directory.EnumerateFiles(Store.Directory));

        Store.Delete(MatchId, Seat.Two);
        Assert.Equal(SavedSeatStatus.Missing, Store.Find(Server, "CODE1").Status);
    }

    [Fact]
    public void AMissingSeatIsReportedNotGuessed()
    {
        Assert.Equal(SavedSeatStatus.Missing, Store.Find(Server, "CODE1").Status);
        Assert.True(Store.TrySave(Seat2(Token), out _));
        Assert.Equal(SavedSeatStatus.Missing, Store.Find(Server, "OTHER").Status);
        Assert.Equal(SavedSeatStatus.Missing, Store.Find("ws://elsewhere:1/match", "CODE1").Status);
        Assert.Equal(SavedSeatStatus.Missing, Store.Find(Server, "CODE1", Seat.One).Status);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"server\":\"ws://127.0.0.1:5080/match\",\"code\":\"CODE1\"}")]
    [InlineData("{\"server\":\"ws://127.0.0.1:5080/match\",\"code\":\"CODE1\",\"matchId\":\"cccccccccccccccccccccccccccccccc\",\"seat\":\"Two\",\"token\":\"short\"}")]
    [InlineData("{\"server\":\"ws://127.0.0.1:5080/match\",\"code\":\"CODE1\",\"matchId\":\"cccccccccccccccccccccccccccccccc\",\"seat\":\"2\",\"token\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    [InlineData("{\"server\":\"ws://127.0.0.1:5080/match\",\"code\":\"CODE1\",\"matchId\":\"cccccccccccccccccccccccccccccccc\",\"seat\":\"Two\",\"token\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"extra\":1}")]
    public void ACorruptFileIsReportedAndLeftInPlace(string content)
    {
        Assert.True(Store.TrySave(Seat2(Token), out _));
        var path = Store.PathOf(MatchId, Seat.Two);
        File.WriteAllText(path, content);
        var lookup = Store.Find(Server, "CODE1");
        Assert.Equal(SavedSeatStatus.Corrupt, lookup.Status);
        Assert.Null(lookup.Seat);
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void AFileOrFolderOthersCanReadIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Assert.True(Store.TrySave(Seat2(Token), out _));
        var path = Store.PathOf(MatchId, Seat.Two);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        Assert.Equal(SavedSeatStatus.Insecure, Store.Find(Server, "CODE1").Status);

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.SetUnixFileMode(Store.Directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        Assert.Equal(SavedSeatStatus.Insecure, Store.Find(Server, "CODE1").Status);
        Assert.False(Store.TrySave(Seat2(Token), out var problem));
        Assert.Contains("other users", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnwritableFolderFailsSafelyAndSaysSoWithoutTheToken()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            // Root ignores file permissions; the safe-failure path is covered by a folder in the way instead.
            File.WriteAllText(Store.Directory, "a file where the folder should be");
            Assert.False(Store.TrySave(Seat2(Token), out var blocked));
            Assert.DoesNotContain(Token, blocked, StringComparison.Ordinal);
            return;
        }
        Directory.CreateDirectory(Store.Directory);
        File.SetUnixFileMode(Store.Directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Assert.False(Store.TrySave(Seat2(Token), out var problem));
        Assert.DoesNotContain(Token, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void BothSeatsSavedHereNeedASeatChoice()
    {
        Assert.True(Store.TrySave(Seat2(Token), out _));
        Assert.True(Store.TrySave(Seat2(Token) with { Seat = Seat.One }, out _));
        Assert.Equal(SavedSeatStatus.Ambiguous, Store.Find(Server, "CODE1").Status);
        Assert.Equal(Seat.One, Store.Find(Server, "CODE1", Seat.One).Seat!.Seat);
    }

    // ---- trench-cli verify ----

    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly Lazy<MatchRecordBody> Completed = new(() => TestRecords.Completed("ranged", 5));

    private (int Exit, string Output, string Error) Verify(byte[] record, bool withKey = true, string? content = null)
    {
        var file = Path.Combine(_dir.FullName, "record.json");
        File.WriteAllBytes(file, record);
        var keyFile = Path.Combine(_dir.FullName, "key.pub");
        File.WriteAllText(keyFile, Key.ExportSubjectPublicKeyInfoPem());
        var args = new List<string> { file };
        if (withKey)
        {
            args.AddRange(["--key", keyFile]);
        }
        if (content is not null)
        {
            args.AddRange(["--content", content]);
        }
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = Match.Cli.Verify.Run([.. args], output, error, trustedKeysFolder: Path.Combine(_dir.FullName, "trusted"));
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public void VerifyExitCodesFollowTheVerdict()
    {
        var body = Completed.Value;
        var completed = Verify(RecordFormat.Sign(body, Key), content: Path.Combine(AppContext.BaseDirectory, "content"));
        Assert.Equal(Match.Cli.Verify.Completed, completed.Exit);
        Assert.Contains("hash matches", completed.Output, StringComparison.Ordinal);
        Assert.Contains("does not prove the dice were fair", completed.Output, StringComparison.Ordinal);
        Assert.Contains("The setup matches the content", completed.Output, StringComparison.Ordinal);

        var aborted = TestRecords.With(body, body.Events.Take(20).ToList(), new RecordResult(RecordKind.Aborted, 20, body.Events[19].Hash, null, "Disconnected", Seat.Two));
        Assert.Equal(Match.Cli.Verify.Aborted, Verify(RecordFormat.Sign(aborted, Key)).Exit);

        var stub = TestRecords.With(body, [], new RecordResult(RecordKind.Unrecorded, 0, null, null, "RecordLost", null));
        var unrecorded = Verify(RecordFormat.Sign(stub, Key));
        Assert.Equal(Match.Cli.Verify.Unrecorded, unrecorded.Exit);
        Assert.Contains("not a completed match", unrecorded.Output, StringComparison.Ordinal);

        var signed = RecordFormat.Sign(body, Key);
        signed[signed.Length / 2] ^= 1;
        Assert.Equal(Match.Cli.Verify.Invalid, Verify(signed).Exit);

        Assert.Equal(Match.Cli.Verify.Unverifiable, Verify(RecordFormat.Sign(body, Key), withKey: false).Exit);

        var future = TestRecords.With(body, engine: new RecordEngine("1.1", 5, 2, 2, "later"));
        var unsupported = Verify(RecordFormat.Sign(future, Key));
        Assert.Equal(Match.Cli.Verify.Unverifiable, unsupported.Exit);
        Assert.Contains("UnsupportedRules", unsupported.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyTrustsTheTrustedKeysFolderAndNothingElse()
    {
        var trusted = Path.Combine(_dir.FullName, "trusted");
        Directory.CreateDirectory(trusted);
        File.WriteAllText(Path.Combine(trusted, "friend.pub"), Key.ExportSubjectPublicKeyInfoPem());
        Assert.Equal(Match.Cli.Verify.Completed, Verify(RecordFormat.Sign(Completed.Value, Key), withKey: false).Exit);
    }

    // After a key is retired and a new one made, the retired key's .pub in the trust folder still verifies old records.
    [Fact]
    public void ARetiredKeyStillVerifiesTheRecordsItSigned()
    {
        var trusted = Path.Combine(_dir.FullName, "trusted");
        Directory.CreateDirectory(trusted);
        using var current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(Path.Combine(trusted, "retired.pub"), Key.ExportSubjectPublicKeyInfoPem());
        File.WriteAllText(Path.Combine(trusted, "current.pub"), current.ExportSubjectPublicKeyInfoPem());
        Assert.Equal(Match.Cli.Verify.Completed, Verify(RecordFormat.Sign(Completed.Value, Key), withKey: false).Exit);
        Assert.Equal(Match.Cli.Verify.Completed, Verify(RecordFormat.Sign(Completed.Value, current), withKey: false).Exit);
    }

    [Fact]
    public void QuietPrintsOnlyTheVerdictWithTheSameExitCode()
    {
        var file = Path.Combine(_dir.FullName, "record.json");
        File.WriteAllBytes(file, RecordFormat.Sign(Completed.Value, Key));
        var keyFile = Path.Combine(_dir.FullName, "key.pub");
        File.WriteAllText(keyFile, Key.ExportSubjectPublicKeyInfoPem());
        var output = new StringWriter();
        var exit = Match.Cli.Verify.Run([file, "--key", keyFile, "--quiet"], output, new StringWriter(), Path.Combine(_dir.FullName, "trusted"));
        Assert.Equal(Match.Cli.Verify.Completed, exit);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.StartsWith("Completed", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyRefusesBadArguments()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(Match.Cli.Verify.Usage, Match.Cli.Verify.Run([], output, error));
        Assert.Equal(Match.Cli.Verify.Usage, Match.Cli.Verify.Run(["a.json", "--key"], output, error));
        var notAKey = Path.Combine(_dir.FullName, "bad.pub");
        File.WriteAllText(notAKey, "nope");
        Assert.Equal(Match.Cli.Verify.Usage, Match.Cli.Verify.Run(["a.json", "--key", notAKey], output, error));
        Assert.Equal(Match.Cli.Verify.Usage, Match.Cli.Verify.Run([Path.Combine(_dir.FullName, "missing.json")], output, error));
    }

    [Fact]
    public async Task TheRealTrenchCliVerifyExitsWithTheVerdict()
    {
        var file = Path.Combine(_dir.FullName, "record.json");
        File.WriteAllBytes(file, RecordFormat.Sign(Completed.Value, Key));
        var keyFile = Path.Combine(_dir.FullName, "key.pub");
        File.WriteAllText(keyFile, Key.ExportSubjectPublicKeyInfoPem());
        var info = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "trench-cli.dll"), "verify", file, "--key", keyFile })
        {
            info.ArgumentList.Add(arg);
        }
        info.Environment["TRENCH_SIM_HOME"] = Path.Combine(_dir.FullName, "home");
        using var process = System.Diagnostics.Process.Start(info)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("hash matches", output, StringComparison.Ordinal);
    }
}
