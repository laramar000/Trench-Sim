using Client.Core;
using Match.Protocol;
using Rules.Core.State;

namespace Client.Tests;

// M7.5 stage 5a: once a match has ended or been aborted, the screen shows the result, the final state hash and the
// event count, and they are those of the server's signed record (checked here with the existing verifier; the client
// itself never reads a record).
public sealed partial class TransportTests
{
    [Fact]
    public async Task AnAbortedMatchShowsItsResultFinalHashAndEventCountAsInItsSignedRecord()
    {
        var data = Path.Combine(_dir.FullName, "records-aborted");
        // A zero rejoin window aborts the match as soon as a seat's connection drops.
        var server = await Start(TimeSpan.Zero, data: data);
        // Seat Two rejoins only once the server has aborted the match (see ControllerPair).
        var aborting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (one, two, _) = await ControllerPair("melee", server, rejoinGate: aborting.Task);
        Assert.Null(one.Snapshot().End);
        Assert.Null(two.Snapshot().End);

        two.Session!.Abort();
        await Until(one, s => s.End is not null);
        aborting.SetResult();
        await Until(two, s => s.End is not null);

        AssertEndMatchesRecord(data, one, two, VerdictKind.Aborted);
        // Seat One received the server's abort message, so its hash is the one the server sent with it.
        Assert.Null(one.Snapshot().End!.Note);
        Assert.StartsWith("Match aborted (Disconnected).", one.Snapshot().End!.Result, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMatchEndLinesNameTheResultTheFinalHashAndTheEventCount()
    {
        var end = new MatchEnd(false, "You won: the final turn ended.", 123, new string('a', 64), null);
        Assert.Equal(["Result: You won: the final turn ended.", $"Final state hash: {new string('a', 64)}", "Events: 123"], end.Lines());
        var aborted = new MatchEnd(true, "Match aborted (Disconnected). No result was recorded as a win or loss.", 9, new string('b', 64), "A note.");
        Assert.Equal("A note.", aborted.Lines().Last());
    }

    // Both screens agree with each other and with the signed record the server wrote: the kind (completed or aborted),
    // the final state hash and the event count, and for a completed match the winner as each seat sees it.
    private static void AssertEndMatchesRecord(string data, MatchController one, MatchController two, VerdictKind kind)
    {
        var matchId = one.Session!.Welcome!.MatchId;
        var key = RecordKeys.FromPem(File.ReadAllText(Path.Combine(data, "keys", "signing.pub")))!;
        var path = Path.Combine(data, "matches", matchId + ".json");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }
        var verdict = RecordVerifier.VerifyFile(path, [key]);
        Assert.True(verdict.Kind == kind, $"{verdict.Kind}: {verdict.Detail}");
        Assert.NotNull(verdict.FinalHash);
        foreach (var (controller, seat) in new[] { (one, Seat.One), (two, Seat.Two) })
        {
            var screen = controller.Snapshot();
            var end = screen.End;
            Assert.NotNull(end);
            Assert.Equal(kind == VerdictKind.Aborted, end.Aborted);
            Assert.Equal(verdict.FinalHash, end.Hash);
            Assert.Equal(verdict.Body!.Events.Count, end.Events);
            Assert.Equal(controller.Session!.Mirror.Hash, end.Hash);
            Assert.Equal(controller.Session!.Mirror.Events, end.Events);
            Assert.Equal($"Result: {end.Result}", end.Lines().First());
            if (kind == VerdictKind.Completed)
            {
                var winner = verdict.Body.Result.Winner;
                var expected = winner is null ? "Draw" : winner == seat ? "You won" : "You lost";
                Assert.StartsWith(expected, end.Result, StringComparison.Ordinal);
                Assert.Equal(screen.View.Outcome, end.Result);
            }
            else
            {
                Assert.Contains("No result was recorded as a win or loss.", end.Result, StringComparison.Ordinal);
            }
        }
    }
}
