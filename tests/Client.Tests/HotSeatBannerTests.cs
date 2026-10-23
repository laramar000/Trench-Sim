using Client.Core;
using Rules.Core.State;

namespace Client.Tests;

// Battlefield UI 2, stage N5: the hot-seat banner says which seat acts now, from the server's state alone: the seat's
// pending decision in the rules' words when the rules wait for it, else its turn.
public class HotSeatBannerTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    [Fact]
    public void TheBannerNamesTheSeatAndThePendingDecisionOrItsTurn()
    {
        var kinds = new HashSet<DecisionKind>();
        foreach (var state in States)
        {
            foreach (var seat in new[] { Seat.One, Seat.Two })
            {
                var banner = HotSeat.TurnBanner(state, seat);
                var letter = Words.SeatLetter(seat);
                if (state.Pending is { } pending && pending.Seat == seat)
                {
                    Assert.Equal($"SEAT {letter} DECIDES: {Words.Decision(pending.Kind)}", banner);
                    kinds.Add(pending.Kind);
                }
                else
                {
                    Assert.Equal($"SEAT {letter}: YOUR TURN", banner);
                }
            }
        }
        // Decisions of the seat not acting are among them (Blood Markers on the other seat's roll).
        Assert.Contains(DecisionKind.ChooseDeploymentZone, kinds);
        Assert.Contains(kinds, k => k is DecisionKind.SpendMarkersOnSuccessRoll or DecisionKind.SpendMarkersOnInjuryRoll or DecisionKind.FreeFight);
        Assert.Equal(1.5, HotSeat.BannerSeconds);
    }
}
