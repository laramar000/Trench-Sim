using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R6b (U118): the Aim ACTION (Warbands p. 28): a Risky Success Roll at +2 DICE; on a Success the model's
// Ranged Attacks get +2 DICE for the rest of the activation. The Fortify ACTION (pp. 33, 85): a Risky Success Roll; on a
// Success the model has COVER until it moves.
public class SelfActionTests
{
    private static readonly ModelProfile Marksman = Rifleman with { Aims = true };
    private static readonly ModelProfile Engineer = Rifleman with { Fortifies = true };

    private static TakeSelfAction Aim => new(Seat.One, A(1), SelfActionKind.Aim);

    private static TakeSelfAction Fortify => new(Seat.One, A(1), SelfActionKind.Fortify);

    private static Shoot Shot => new(Seat.One, A(1), B(1));

    // A1 (4, 2) is activated; B1 stands 8" away in the open.
    private static BoardPlay Ready(ModelProfile a1)
    {
        var play = new BoardPlay(2, id => id == A(1) ? a1 : Rifleman).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "Aim p. 28: a Risky roll at +2 DICE; on a Success its Ranged Attacks get +2 DICE for the rest of the activation")]
    public void ASuccessfulAimAddsTwoDiceToItsShots()
    {
        var play = Ready(Marksman);
        var rolled = play.Do(Aim, 6, 6, 1, 1).Events.OfType<SelfActionRolled>().Single();
        Assert.Equal((true, true, 2), (rolled.Succeeded, rolled.Check.Risky, rolled.Check.NetDice));
        Assert.Equal((true, ActionsTaken.Aim), (play.State.Aiming, play.State.Actions & ActionsTaken.Aim));
        Assert.Equal(2, TurnSequence.AimedDice(play.State, A(1)));
        Assert.Equal(2, play.Do(Shot, 1, 1, 1, 1).Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
        play.Refused(Aim, ReasonCode.ActionAlreadyTaken);

        // It lasts only for the activation.
        play.Do(new EndActivation(Seat.One));
        Assert.False(play.State.Aiming);
        Assert.Equal(0, TurnSequence.AimedDice(play.State, A(1)));

        // Without it a shot keeps its own dice.
        Assert.Equal(0, Ready(Marksman).Do(Shot, 1, 1).Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
    }

    [Fact]
    [Trait("Rule", "Aim and Fortify: Risky; a Failure gives nothing and ends the activation (not with a Pontiff's Crucifix)")]
    public void AFailedRollEndsTheActivation()
    {
        var play = Ready(Marksman);
        var events = play.Do(Aim, 3, 3, 1, 1).Events;
        Assert.False(events.OfType<SelfActionRolled>().Single().Succeeded);
        Assert.Contains(events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
        Assert.False(play.State.Aiming);

        var fort = Ready(Engineer);
        Assert.Contains(fort.Do(Fortify, 3, 3).Events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
        Assert.False(TurnSequence.HasCover(fort.State, A(1)));

        var blessed = Ready(Marksman with { KeepsGoingAfterRisky = true });
        Assert.DoesNotContain(blessed.Do(Aim, 3, 3, 1, 1).Events, e => e is ActivationEnded);
        Assert.Equal(((ModelId?)A(1), false), (blessed.State.ActiveModel, blessed.State.Aiming));
    }

    [Fact]
    [Trait("Rule", "Fortify pp. 33, 85: a Risky roll; on a Success the model has COVER until it moves")]
    public void FortifyGivesCoverUntilItMoves()
    {
        var play = Ready(Engineer);
        Assert.False(TurnSequence.HasCover(play.State, A(1)));
        var rolled = play.Do(Fortify, 4, 4).Events.OfType<SelfActionRolled>().Single();
        Assert.Equal((true, 0), (rolled.Succeeded, rolled.Check.NetDice));
        Assert.True(TurnSequence.HasCover(play.State, A(1)));
        play.Refused(Fortify, ReasonCode.ActionAlreadyTaken);
        // It stays through the end of the activation, then a move ends it.
        play.Do(new EndActivation(Seat.One));
        Assert.True(TurnSequence.HasCover(play.State, A(1)));
        var moved = Ready(Engineer);
        moved.Do(Fortify, 4, 4);
        moved.Do(new MoveModel(Seat.One, A(1), Path(("6", "2"))));
        Assert.False(TurnSequence.HasCover(moved.State, A(1)));
    }

    [Fact]
    [Trait("Rule", "Aim and Fortify: the opponent may spend the model's markers as -DICE on the roll")]
    public void SpentMarkersArePenaltyDice()
    {
        var play = Ready(Marksman).Mark(A(1), 1);
        Assert.Equal(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, Seat.Two), play.Do(Aim).Events[^1]);
        var rolled = play.Do(new SpendMarkers(Seat.Two, 1), 6, 6, 1).Events.OfType<SelfActionRolled>().Single();
        Assert.Equal(1, rolled.Check.NetDice);
    }

    [Fact]
    [Trait("Rule", "Aim and Fortify: only a model whose unit has the ACTION may take it")]
    public void ActionsTheModelLacksAreRefused()
    {
        Ready(Rifleman).Refused(Aim, ReasonCode.NoSuchAction);
        Ready(Marksman).Refused(Fortify, ReasonCode.NoSuchAction);
        Ready(Engineer).Refused(Aim, ReasonCode.NoSuchAction);
        Ready(Marksman).Refused(Aim with { Action = (SelfActionKind)7 }, ReasonCode.InvalidChoice);
    }

    [Fact]
    [Trait("Rule", "Records: a forged Aim or Fortify is refused")]
    public void ForgedActionsAreRefused()
    {
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(Ready(Rifleman).State, new SelfActionDeclared(A(1), SelfActionKind.Aim)));
        var play = Ready(Marksman);
        var check = SuccessRoll.Roll(new FixedDice(6, 6, 1, 1), 2, 0, risky: true);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new SelfActionRolled(A(1), SelfActionKind.Aim, check)));
        var declared = Reducer.Apply(play.State, new SelfActionDeclared(A(1), SelfActionKind.Aim));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new SelfActionRolled(A(1), SelfActionKind.Fortify, check)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new SelfActionRolled(A(1), SelfActionKind.Aim, SuccessRoll.Roll(new FixedDice(6, 6), 0, 0, risky: true))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new SelfActionRolled(A(1), SelfActionKind.Aim, SuccessRoll.Roll(new FixedDice(6, 6, 1, 1), 2, 0, risky: false))));
        Assert.True(Reducer.Apply(declared, new SelfActionRolled(A(1), SelfActionKind.Aim, check)).Aiming);
    }

    [Fact]
    [Trait("Rule", "Records: matches with Aim and Fortify play to the end and replay; the fields are hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        var plain = StateHash.Canonical(Ready(Rifleman).State);
        Assert.DoesNotContain("aims", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("fortifies", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("aiming", plain, StringComparison.Ordinal);
        var aimed = Ready(Marksman);
        aimed.Do(Aim, 6, 6, 1, 1);
        var hashed = StateHash.Canonical(aimed.State);
        Assert.Contains("aims=yes", hashed, StringComparison.Ordinal);
        Assert.Contains("aiming", hashed, StringComparison.Ordinal);
        Assert.Contains("fortifies=yes", StateHash.Canonical(Ready(Engineer).State), StringComparison.Ordinal);
        var (aims, fortified) = (0, 0);
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { Aims = i % 2 == 0, Fortifies = i % 3 == 0 } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                aims += run.Log.OfType<SelfActionRolled>().Count(s => s.Action == SelfActionKind.Aim && s.Succeeded);
                fortified += run.Log.OfType<SelfActionRolled>().Count(s => s.Action == SelfActionKind.Fortify && s.Succeeded);
            }
        }
        Assert.True(aims > 0 && fortified > 0, $"{aims} aims, {fortified} fortified");
    }
}
