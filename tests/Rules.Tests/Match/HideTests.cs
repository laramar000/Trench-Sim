using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Hide ACTION (Warbands p. 110, U126): a model touching terrain at least 1/2" high takes a Risky roll at +1 DICE; on a
// Success it cannot be the target of Ranged Attacks or Charges until it moves, charges, retreats or makes a Ranged
// Attack, or an enemy moves within 1.5" of it. A BLAST still catches it.
public class HideTests
{
    private static readonly ModelProfile Commando = Rifleman with { Hides = true };

    private static TakeSelfAction Hide => new(Seat.One, A(1), SelfActionKind.Hide);

    // A1 (4, 2) with a piece of terrain just behind it (west, 0.06" from its base): a 3/4" wall, or a 1/4" low piece.
    private static BoardPlay Ready(bool tall = true, bool touching = true)
    {
        var piece = new TerrainBlock("wall", touching ? F("2.5", "0.5", "3.45", "3.5") : F("1.5", "0.5", "2.5", "3.5"), tall ? TerrainProfile.Cover : TerrainProfile.Low);
        var play = new BoardPlay(2, id => id == A(1) ? Commando : Rifleman, scenario: MeleePlay.WithTerrain(piece)).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(A(2), "4", "30").Put(B(2), "30", "30");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    // A1 hides (6, 6, 1: a Success), ends its activation; B1 activates.
    private static BoardPlay Hidden()
    {
        var play = Ready();
        var rolled = play.Do(Hide, 6, 6, 1).Events.OfType<SelfActionRolled>().Single();
        Assert.Equal((true, true, 1), (rolled.Succeeded, rolled.Check.Risky, rolled.Check.NetDice));
        Assert.True(play.M(A(1)).Hidden);
        play.Do(new EndActivation(Seat.One));
        play.Do(new ActivateModel(Seat.Two, B(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "Hide p. 110: a Risky roll at +1 DICE; on a Success no Ranged Attack or Charge may target it")]
    public void AHiddenModelCannotBeShotOrCharged()
    {
        var play = Hidden();
        play.Refused(new Shoot(Seat.Two, B(1), A(1)), ReasonCode.TargetHidden);
        play.Refused(new ChargeModel(Seat.Two, B(1), A(1)), ReasonCode.TargetHidden);
    }

    [Fact]
    [Trait("Rule", "Hide p. 110: only in contact with terrain at least 1/2\" high")]
    public void HideNeedsTallTerrainInContact()
    {
        Ready(tall: false).Refused(Hide, ReasonCode.NotAgainstTerrain);
        Ready(touching: false).Refused(Hide, ReasonCode.NotAgainstTerrain);
        Assert.Null(TurnSequence.Check(Ready().State, Hide));
    }

    [Fact]
    [Trait("Rule", "Hide p. 110: a failed Risky roll gives nothing and ends the activation")]
    public void AFailedHideEndsTheActivation()
    {
        var play = Ready();
        var events = play.Do(Hide, 3, 3, 1).Events;
        Assert.False(events.OfType<SelfActionRolled>().Single().Succeeded);
        Assert.False(play.M(A(1)).Hidden);
        Assert.True(play.State.MustEndActivation || play.State.ActiveModel is null);
    }

    [Fact]
    [Trait("Rule", "Hide p. 110: it ends when an enemy moves within 1.5\" of it")]
    public void AnEnemyComingCloseFindsIt()
    {
        var play = Hidden();
        // B1 moves from (12, 2) to (7.5, 2): its base ends 3.5 - 2 x 0.49 = 2.52" from A1's, so A1 stays hidden.
        play.Do(new MoveModel(Seat.Two, B(1), Path(("7.5", "2"))));
        Assert.True(play.M(A(1)).Hidden);

        // B1 walks from (6.5, 0.6) to (5.5, 4.6): it starts and ends over 1.5" away, but passes about 1.2" from A1's base.
        var near = Hidden();
        near.Put(B(1), "6.5", "0.6");
        near.Do(new MoveModel(Seat.Two, B(1), Path(("5.5", "4.6"))));
        Assert.False(near.M(A(1)).Hidden);
    }

    [Fact]
    [Trait("Rule", "Hide p. 110: it ends when the model itself moves or makes a Ranged Attack")]
    public void ItsOwnMoveOrShotEndsIt()
    {
        var moved = Ready();
        moved.Do(Hide, 6, 6, 1);
        moved.Do(new MoveModel(Seat.One, A(1), Path(("4", "2.5"))));
        Assert.False(moved.M(A(1)).Hidden);

        var shot = Ready();
        shot.Do(Hide, 6, 6, 1);
        shot.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1);
        Assert.False(shot.M(A(1)).Hidden);
    }

    [Fact]
    [Trait("Rule", "Hide p. 110: only a model with the ability")]
    public void AModelWithoutHideCannot()
    {
        var play = new BoardPlay(2).ToActivation();
        play.Put(A(1), "4", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Refused(Hide, ReasonCode.NoSuchAction);
    }
}
