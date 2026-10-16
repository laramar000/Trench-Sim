using Client.Core;
using Rules.Core.State;

namespace Client.Tests;

// The test match options (tools/TestCenter): a client started without them behaves as before, and bad options are
// reported rather than guessed.
public class TestMatchTests
{
    [Fact]
    public void WithoutTestMatchOptionsTheClientStartsAsUsual()
    {
        Assert.Equal((null, null), TestMatch.Parse([]));
        Assert.Equal((null, null), TestMatch.Parse(["--ui-check", "host", "--server", "ws://127.0.0.1:5080/match"]));
    }

    [Fact]
    public void CreateJoinAndRejoinAreRead()
    {
        var (create, problem) = TestMatch.Parse(["--test-match", "create", "--server", "ws://127.0.0.1:5000/match", "--scenario", "melee",
            "--test-window", "A", "--test-report", "a.txt"]);
        Assert.Null(problem);
        Assert.Equal(new TestMatch("create", "ws://127.0.0.1:5000/match", "melee", null, "A", "a.txt"), create);

        var (join, _) = TestMatch.Parse(["--test-match", "join", "--server", "ws://127.0.0.1:5000/match", "--code", " abc123 "]);
        Assert.Equal(new TestMatch("join", "ws://127.0.0.1:5000/match", "ranged", "ABC123", null, null), join);

        var (rejoin, _) = TestMatch.Parse(["--test-match", "rejoin", "--server", "ws://127.0.0.1:5000/match", "--code", "ABC123"]);
        Assert.Equal("rejoin", rejoin!.Mode);
    }

    [Theory]
    [InlineData(new[] { "--test-match", "watch", "--server", "ws://127.0.0.1:5000/match" }, "create, join or rejoin")]
    [InlineData(new[] { "--test-match", "create" }, "needs --server")]
    [InlineData(new[] { "--test-match", "create", "--server", "not a url" }, "needs --server")]
    [InlineData(new[] { "--test-match", "join", "--server", "ws://127.0.0.1:5000/match" }, "needs --code")]
    [InlineData(new[] { "--test-match", "rejoin", "--server", "ws://127.0.0.1:5000/match", "--code", "" }, "needs --code")]
    [InlineData(new[] { "--test-match", "create", "--server", "ws://127.0.0.1:5000/match", "--scenario", "siege" }, "ranged or melee")]
    public void UnusableOptionsAreReported(string[] args, string problem)
    {
        var (test, said) = TestMatch.Parse(args);
        Assert.Null(test);
        Assert.Contains(problem, said);
    }

    [Fact]
    public void TheTitleNamesTheWindowAndSeat()
    {
        var test = new TestMatch("join", "ws://x/match", "ranged", "ABC", "B", null);
        Assert.Equal("Trench Sim test match: window B (Seat Two)", test.Title(Seat.Two));
        Assert.Equal("Trench Sim test match: window B", test.Title(null));
    }

    [Fact]
    public void TheReportHoldsWhatTheEndOfMatchSectionShows()
    {
        var end = new MatchEnd(false, "Seat One won (VictoryPoints).", 185, new string('a', 64), null);
        Assert.Equal($"code=ABC123\nseat=One\naborted=no\nresult=Seat One won (VictoryPoints).\nevents=185\nfinal_hash={new string('a', 64)}\n",
            TestMatch.ReportText("ABC123", Seat.One, end));
    }

    [Fact]
    public void TheReportIsWrittenWhole()
    {
        var folder = Path.Combine(Path.GetTempPath(), "trench-test-match-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "nested", "a.txt");
            TestMatch.WriteReport(path, "one\n");
            TestMatch.WriteReport(path, "two\n");
            Assert.Equal("two\n", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
