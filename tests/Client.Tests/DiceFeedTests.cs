using Client.Core;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// Visible dice rolls (QoL stage F2): the boxes the dice display holds. Every roll handed over is shown once, in event
// order; the rolls of one action share a box; a box goes after its time, or at once on a click; "Off" shows nothing.
public class DiceFeedTests
{
    private static readonly ModelId A1 = new("a1");
    private static readonly ModelId B2 = new("b2");

    private static LogLine Line(int seq, GameEvent e) => new(seq, EventText.Line(e), EventText.Dice(e), Step: DiceRolls.Step(e));

    private static SuccessRollResult Check(int a, int b) => new(new RollRecord([a, b], [a, b]), 0, risky: false);

    [Fact]
    public void EveryRollOfAWholeMatchIsShownOnceInOrderAndMatchesItsEvent()
    {
        foreach (var (_, log) in ClientCoreTests.Matches.Value.Take(20))
        {
            var feed = new DiceFeed { Seconds = 1e9 };
            var expected = new List<(int Seq, IReadOnlyList<int> Faces)>();
            for (var i = 0; i < log.Count; i++)
            {
                feed.Add(Line(i + 1, log[i]), i);
                if (DiceRolls.Of(log[i]) is { } roll)
                {
                    expected.Add((i + 1, roll.Faces));
                }
                Assert.True(feed.Boxes.Count <= DiceFeed.MaxBoxes);
                Assert.All(feed.Boxes, b => Assert.InRange(b.Rows.Count, 1, DiceFeed.MaxRows));
            }
            Assert.Equal(expected.Select(x => x.Seq), feed.Shown.Select(r => r.Seq));
            Assert.Equal(expected.Select(x => string.Join(",", x.Faces)), feed.Shown.Select(r => string.Join(",", r.Roll.Faces)));
        }
    }

    [Fact]
    public void TheRollsOfOneShotShareABoxAndTheNextActionStartsANewOne()
    {
        var feed = new DiceFeed();
        feed.Add(Line(10, new ShootingTargetRolled(A1, B2, 5)), 0);
        feed.Add(Line(11, new RangedSuccessRolled(A1, B2, Check(4, 4))), 0.1);
        feed.Add(Line(12, new MarkersSpent(Seat.One, B2, 1, MarkerSpendPurpose.RaiseInjuryRoll)), 0.2);
        feed.Add(Line(13, new InjuryRolled(A1, B2, new InjuryRollResult(new RollRecord([5, 3], [5, 3]), 0, 0, false))), 0.3);
        var shot = Assert.Single(feed.Boxes);
        Assert.Equal([10, 11, 13], shot.Rows.Select(r => r.Seq));
        Assert.Equal(10, shot.Id);
        Assert.Equal(A1, shot.Model);
        // The last row is the final outcome of the action.
        Assert.Equal("✓ DOWN 8, +1 Blood Marker", shot.Rows[^1].Roll.Result);
        feed.Add(Line(14, new ActivationEnded(A1, ActivationEndReason.Voluntary)), 0.4);
        feed.Add(Line(15, new ChargeDeclared(B2, A1)), 0.5);
        feed.Add(Line(16, new ChargeRolled(B2, A1, 2)), 0.6);
        feed.Add(Line(17, new ChargeMoved(B2, A1, [], Length.Zero, false)), 0.7);
        Assert.Equal(2, feed.Boxes.Count);
        var charge = feed.Boxes[^1];
        Assert.Equal([16], charge.Rows.Select(r => r.Seq));
        Assert.Equal("✗ FAILED", charge.Rows[0].FollowUp);
        Assert.Equal(RollVerdict.Fail, charge.Rows[0].FollowUpVerdict);
        Assert.Equal("✗ FAILED", feed.Shown[^1].FollowUp);
    }

    [Fact]
    public void MoraleAndRollOffsHaveABoxOfTheirOwn()
    {
        var feed = new DiceFeed();
        feed.Add(Line(1, new MeleeSuccessRolled(A1, B2, Check(3, 3))), 0);
        feed.Add(Line(2, new MoraleCheckRolled(Seat.Two, Check(5, 5), 2, 4, false)), 0);
        feed.Add(Line(3, new RollOffResolved(RollOffPurpose.Initiative, [new RollOffRound(6, 1)], Seat.One)), 0);
        feed.Add(Line(4, new MeleeSuccessRolled(A1, B2, Check(6, 6))), 0);
        Assert.Equal(3, feed.Boxes.Count);
        Assert.All(feed.Boxes, b => Assert.Single(b.Rows));
        Assert.Equal([2, 3, 4], feed.Boxes.Select(b => b.Id));
        Assert.Null(feed.Boxes[0].Model);
    }

    [Fact]
    public void ABoxFadesAfterItsTimeAndAClickClosesItAtOnce()
    {
        var feed = new DiceFeed { Seconds = 2 };
        feed.Add(Line(1, new MeleeSuccessRolled(A1, B2, Check(3, 4))), 10);
        Assert.True(feed.TakeChanged());
        var box = Assert.Single(feed.Boxes);
        Assert.Equal(1, feed.Alpha(box, 11));
        Assert.InRange(feed.Alpha(box, 11.7), 0.4, 0.6);
        feed.Expire(11.9);
        Assert.Single(feed.Boxes);
        feed.Expire(12);
        Assert.Empty(feed.Boxes);
        Assert.True(feed.TakeChanged());
        feed.Add(Line(2, new MeleeSuccessRolled(A1, B2, Check(3, 4))), 20);
        feed.Dismiss(2);
        Assert.Empty(feed.Boxes);
        // Closing does not undo what was shown.
        Assert.Equal([1, 2], feed.Shown.Select(r => r.Seq));
    }

    [Fact]
    public void OffShowsNothingAndRollsStayInTheLog()
    {
        var feed = new DiceFeed { Seconds = 0 };
        var line = Line(1, new MeleeSuccessRolled(A1, B2, Check(3, 4)));
        feed.Add(line, 0);
        Assert.Empty(feed.Boxes);
        Assert.Empty(feed.Shown);
        Assert.False(feed.TakeChanged());
        Assert.NotNull(line.Dice);
    }

    [Fact]
    public void EventsWithoutRollsShowNothingAndNothingIsShownOnSending()
    {
        // Only server events reach the feed; a declaration alone (what follows a command) adds no box.
        var feed = new DiceFeed();
        feed.Add(Line(1, new ChargeDeclared(A1, B2)), 0);
        feed.Add(Line(2, new TurnStarted(2)), 0);
        feed.Add(new LogLine(3, "x", null), 0);
        Assert.Empty(feed.Boxes);
        Assert.Empty(feed.Shown);
    }

    [Fact]
    public void TheMatchSoFarSentAgainOnRejoiningIsNotShown()
    {
        var feed = new DiceFeed();
        feed.Add(Line(5, new MeleeSuccessRolled(A1, B2, Check(3, 4))) with { Replayed = true }, 0);
        Assert.Empty(feed.Boxes);
        Assert.Empty(feed.Shown);
        feed.Add(Line(6, new MeleeSuccessRolled(A1, B2, Check(3, 4))), 0);
        Assert.Equal([6], feed.Shown.Select(r => r.Seq));
    }

    [Fact]
    public void ALongActionStartsAnotherBoxAfterFourRows()
    {
        var feed = new DiceFeed();
        for (var i = 1; i <= 5; i++)
        {
            feed.Add(Line(i, new MeleeSuccessRolled(A1, B2, Check(2, 2))), i);
        }
        Assert.Equal([4, 1], feed.Boxes.Select(b => b.Rows.Count));
    }
}
