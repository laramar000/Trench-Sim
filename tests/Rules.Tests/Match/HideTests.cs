using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R7c (U130): the Hide ACTION (Warbands p. 110): touching terrain at least 1/2" high, a Risky roll at +1
// DICE; on a Success the model cannot be targeted by Ranged Attacks or Charges until it acts or an enemy comes within 1.5".
public class HideTests
{
    private static readonly ModelProfile Commando = Rifleman with { Hides = true };

    private static TakeSelfAction Hide => new(Seat.One, A(1), SelfActionKind.Hide);

    // A1 (4, 2) beside a barricade at x 5-6; B1 stands at (12, 2), B2 far; A2 far. Seat One activates A1.
    private static BoardPlay Ready(string ax = "4.5")
    {
        var scenario = new Scenario(Field("36", [Barricade("b", "5", "0", "6", "6")], [new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36"))]), []);
        var play = new BoardPlay(2, id => id == A(1) ? Commando : Rifleman, scenario: scenario).ToActivation();
        play.Put(A(1), ax, "2").Put(A(2), "2", "30").Put(B(1), "12", "2").Put(B(2), "30", "30");
        return play;
    }

    [Fact]
    [Trait("Rule", "Hide p. 110: a Risky roll at +1 DICE touching terrain at least 1/2\" high; a Success hides the model")]
    public void ASuccessHidesTheModel()
    {
        var play = Ready();
        play.Do(new ActivateModel(Seat.One, A(1)));
        var rolled = play.Do(Hide, 4, 4, 1).Events.OfType<SelfActionRolled>().Single();
        Assert.Equal((true, true, 1), (rolled.Succeeded, rolled.Check.Risky, rolled.Check.NetDice));
        Assert.True(play.M(A(1)).Hidden);
        play.Refused(Hide, ReasonCode.ActionAlreadyTaken);
    }

    [Fact]
    [Trait("Rule", "Hide p. 110: needs terrain 1/2\" high or more to touch; a Failure ends the activation")]
    public void NeedsCoverToTouch()
    {
        var away = Ready("2");
        away.Do(new ActivateModel(Seat.One, A(1)));
        away.Refused(Hide, ReasonCode.NoSuchAction);

        var failed = Ready();
        failed.Do(new ActivateModel(Seat.One, A(1)));
        var events = failed.Do(Hide, 1, 1, 1).Events;
        Assert.False(failed.M(A(1)).Hidden);
        Assert.Contains(events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });

        // Only a model whose unit has it.
        var plain = Ready();
        plain.Do(new ActivateModel(Seat.One, A(1)));
        plain.Do(new EndActivation(Seat.One));
        plain.Do(new ActivateModel(Seat.Two, B(1)));
        plain.Refused(new TakeSelfAction(Seat.Two, B(1), SelfActionKind.Hide), ReasonCode.NoSuchAction);
    }

    [Fact]
    [Trait("Rule", "Hide p. 110: not a target of Ranged Attacks or Charges until it acts or an enemy comes within 1.5\"")]
    public void AHiddenModelCannotBeShotOrCharged()
    {
        var play = Ready();
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Do(Hide, 4, 4, 1);
        play.Do(new EndActivation(Seat.One));
        play.Do(new ActivateModel(Seat.Two, B(1)));
        play.Refused(new Shoot(Seat.Two, B(1), A(1)), ReasonCode.TargetHidden);
        play.Refused(new ChargeModel(Seat.Two, B(1), A(1)), ReasonCode.TargetHidden);

        // An enemy within 1.5" ends the cover: B1 moves up beside it.
        play.Put(B(1), "5.9", "2");
        Assert.True(TurnSequence.Check(play.State, new Shoot(Seat.Two, B(1), A(1))) is null or { Code: not ReasonCode.TargetHidden });
        // And its own next activation clears it.
        play.Put(B(1), "12", "2");
        play.Do(new EndActivation(Seat.Two));
        play.State = TurnRules.WithModel(play.State, play.M(A(1)) with { ActivatedThisTurn = false });
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.False(play.M(A(1)).Hidden);
    }
}
