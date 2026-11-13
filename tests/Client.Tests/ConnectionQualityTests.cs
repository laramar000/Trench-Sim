using System.Diagnostics;
using Client.Core;
using Rules.Core.State;

namespace Client.Tests;

// Battlefield UI 3, stage U3 (decision D1-A): the connection indicator. Its bars and words come from the connection state
// and the only latency the client can measure, a command's round trip to the server's answer; nothing before that.
public class ConnectionQualityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryStateHasBarsOrACrossAndWords()
    {
        foreach (var state in Enum.GetValues<ConnectionState>())
        {
            var bars = ConnectionQuality.Bars(state, RoundTrips.None);
            var expected = state switch
            {
                ConnectionState.Connected or ConnectionState.Finished => 3,
                ConnectionState.Reconnecting => 1,
                _ => (int?)null,
            };
            Assert.Equal(expected, bars);
            Assert.False(string.IsNullOrWhiteSpace(ConnectionQuality.StateWord(state)));
        }
        Assert.Equal(Enum.GetValues<ConnectionState>().Length, Enum.GetValues<ConnectionState>().Select(ConnectionQuality.StateWord).Distinct().Count());
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(149, 3)]
    [InlineData(150, 2)]
    [InlineData(399, 2)]
    [InlineData(400, 1)]
    [InlineData(3000, 1)]
    public void ConnectedBarsFollowTheLastRoundTrip(int ms, int bars)
    {
        var trips = RoundTrips.Of([TimeSpan.FromMilliseconds(ms)], Now);
        Assert.Equal(bars, ConnectionQuality.Bars(ConnectionState.Connected, trips));
        // Only a connected seat's bars follow the timing.
        Assert.Equal(1, ConnectionQuality.Bars(ConnectionState.Reconnecting, trips));
        Assert.Null(ConnectionQuality.Bars(ConnectionState.GaveUp, trips));
    }

    [Fact]
    public void TheWordsSayNotMeasuredUntilACommandIsAnsweredThenTheLastAndTheMedian()
    {
        Assert.Equal("Command round trip: not measured yet (it is measured when a command is answered).", ConnectionQuality.Timing(RoundTrips.None, Now));
        var trips = RoundTrips.Of([.. new[] { 30, 10, 20, 500 }.Select(ms => TimeSpan.FromMilliseconds(ms))], Now.AddSeconds(-5));
        Assert.Equal(TimeSpan.FromMilliseconds(500), trips.Last);
        Assert.Equal(TimeSpan.FromMilliseconds(25), trips.Median);
        Assert.Equal("Command round trip: last 500 ms (5 s ago), median of the last 4: 25 ms.", ConnectionQuality.Timing(trips, Now));
    }
}

public sealed partial class TransportTests
{
    // The controller's measured round trip is the time from sending a command to the server's answer: none before the
    // first answer, then one per answered command, each no longer than the whole send as the test timed it.
    [Fact]
    public async Task TheRoundTripIsMeasuredFromEachCommandToItsAnswer()
    {
        var (one, two, _) = await ControllerPair("melee");
        Assert.Equal(RoundTrips.None, one.Snapshot().RoundTrips);
        Assert.Equal(RoundTrips.None, two.Snapshot().RoundTrips);
        var mover = one.Snapshot().Hints.Count > 0 ? one : two;
        var hint = mover.Snapshot().Hints[0];
        var timer = Stopwatch.StartNew();
        var result = await mover.SendAsync(hint.Command, hint.Label, Token);
        timer.Stop();
        Assert.Equal(FeedbackKind.Accepted, result.Kind);
        var trips = mover.Snapshot().RoundTrips!;
        Assert.Equal(1, trips.Count);
        Assert.NotNull(trips.Last);
        Assert.InRange(trips.Last!.Value, TimeSpan.Zero, timer.Elapsed);
        Assert.Equal(trips.Last, trips.Median);
        // The other seat sent nothing, so it measured nothing.
        Assert.Equal(RoundTrips.None, (mover == one ? two : one).Snapshot().RoundTrips);
    }
}
