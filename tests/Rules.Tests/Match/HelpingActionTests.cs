using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R6e (U121): Enforced Orthodoxy ACTION (Warbands p. 55): a Risky roll at +1 DICE; on a Success every Down
// friend within 8" stands up. Laying on of Hands ACTION (p. 53): a Success Roll; on a Success 1 BLOOD MARKER (3 on a
// Critical) comes off a friend within 6", the model itself included.
public class HelpingActionTests
{
    private static readonly ModelProfile Castigator = Rifleman with { EnforcesOrthodoxy = true };
    private static readonly ModelProfile Prophet = Rifleman with { LaysOnHands = true };

    // A1 (4, 2) is activated; A2 and A3 stand where given; the enemies are far away.
    private static BoardPlay Ready(ModelProfile a1, string a2x = "4", string a2y = "6", string a3x = "4", string a3y = "20")
    {
        var play = new BoardPlay(3, id => id == A(1) ? a1 : Rifleman).ToActivation();
        play.Put(A(1), "4", "2").Put(A(2), a2x, a2y).Put(A(3), a3x, a3y).Put(B(1), "30", "30").Put(B(2), "30", "26").Put(B(3), "30", "22");
        return play;
    }

    private static TakeSelfAction Orthodoxy => new(Seat.One, A(1), SelfActionKind.EnforcedOrthodoxy);

    private static TakeSelfAction Hands(ModelId target) => new(Seat.One, A(1), SelfActionKind.LayingOnHands) { Target = target };

    [Fact]
    [Trait("Rule", "Enforced Orthodoxy p. 55: a Risky roll at +1 DICE; on a Success every Down friend within 8\" stands up")]
    public void EnforcedOrthodoxyStandsUpDownFriends()
    {
        var play = Ready(Castigator).SetStatus(A(2), ModelStatus.Down).SetStatus(A(3), ModelStatus.Down);
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal([A(2)], TurnSequence.OrthodoxyStands(play.State, A(1)));
        var rolled = play.Do(Orthodoxy, 4, 4, 1).Events.OfType<SelfActionRolled>().Single();
        Assert.Equal((true, true, 1), (rolled.Succeeded, rolled.Check.Risky, rolled.Check.NetDice));
        Assert.Equal([A(2)], rolled.StoodUp.ToArray());
        Assert.Equal((ModelStatus.Standing, ModelStatus.Down), (play.M(A(2)).Status, play.M(A(3)).Status));
        play.Refused(Orthodoxy, ReasonCode.ActionAlreadyTaken);

        // A Failure stands nobody up and ends the activation.
        var failed = Ready(Castigator).SetStatus(A(2), ModelStatus.Down);
        failed.Do(new ActivateModel(Seat.One, A(1)));
        var events = failed.Do(Orthodoxy, 3, 3, 1).Events;
        Assert.True(events.OfType<SelfActionRolled>().Single().StoodUp.IsEmpty);
        Assert.Contains(events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
        Assert.Equal(ModelStatus.Down, failed.M(A(2)).Status);
    }

    [Fact]
    [Trait("Rule", "Laying on of Hands p. 53: a Success Roll removes 1 BLOOD MARKER, 3 on a Critical, from a friend within 6\" or itself")]
    public void LayingOnOfHandsRemovesMarkers()
    {
        var play = Ready(Prophet).Mark(A(2), 4);
        play.Do(new ActivateModel(Seat.One, A(1)));
        var rolled = play.Do(Hands(A(2)), 4, 4).Events.OfType<SelfActionRolled>().Single();
        Assert.Equal((false, 1, (ModelId?)A(2)), (rolled.Check.Risky, rolled.Removed, rolled.Target));
        Assert.Equal(3, play.M(A(2)).BloodMarkers);
        play.Refused(Hands(A(2)), ReasonCode.ActionAlreadyTaken);

        var critical = Ready(Prophet).Mark(A(2), 2);
        critical.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(2, critical.Do(Hands(A(2)), 6, 6).Events.OfType<SelfActionRolled>().Single().Removed);
        Assert.Equal(0, critical.M(A(2)).BloodMarkers);

        // Not Risky: a Failure does not end the activation.
        var missed = Ready(Prophet).Mark(A(2), 1);
        missed.Do(new ActivateModel(Seat.One, A(1)));
        var events = missed.Do(Hands(A(2)), 1, 2).Events;
        Assert.Equal(0, events.OfType<SelfActionRolled>().Single().Removed);
        Assert.DoesNotContain(events, e => e is ActivationEnded);
        Assert.Equal(1, missed.M(A(2)).BloodMarkers);

        // With Holy Water of Lalibela: +1 DICE.
        var watered = Ready(Prophet with { HealingWaters = true }).Mark(A(2), 1);
        watered.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(1, watered.Do(Hands(A(2)), 1, 4, 4).Events.OfType<SelfActionRolled>().Single().Check.NetDice);
    }

    [Fact]
    [Trait("Rule", "Laying on of Hands: only the model itself or a friend within 6\" with a BLOOD MARKER; the other ACTIONS have no target")]
    public void TargetsThatCannotBeTakenAreRefused()
    {
        var play = Ready(Prophet, "4", "9.01").Mark(A(1), 1).Mark(A(2), 1).Mark(B(1), 1);
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Refused(Hands(A(2)), ReasonCode.NothingToTreat);   // just beyond 6"
        play.Refused(Hands(A(3)), ReasonCode.NothingToTreat);   // far, and no marker
        play.Refused(Hands(B(1)), ReasonCode.NothingToTreat);   // an enemy
        play.Refused(new TakeSelfAction(Seat.One, A(1), SelfActionKind.LayingOnHands), ReasonCode.UnknownModel);
        Assert.Null(TurnSequence.Check(play.State, Hands(A(1))));
        var castigator = Ready(Castigator);
        castigator.Do(new ActivateModel(Seat.One, A(1)));
        castigator.Refused(Orthodoxy with { Target = A(2) }, ReasonCode.InvalidChoice);
        castigator.Refused(Hands(A(1)), ReasonCode.NoSuchAction);
    }

    [Fact]
    [Trait("Rule", "Records: a forged Enforced Orthodoxy or Laying on of Hands is refused")]
    public void ForgedActionsAreRefused()
    {
        var play = Ready(Prophet).Mark(A(2), 2);
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new SelfActionDeclared(A(1), SelfActionKind.LayingOnHands)));
        var declared = Reducer.Apply(play.State, new SelfActionDeclared(A(1), SelfActionKind.LayingOnHands) { Target = A(2) });
        var check = SuccessRoll.Roll(new FixedDice(4, 4), 0, 0, risky: false);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new SelfActionRolled(A(1), SelfActionKind.LayingOnHands, check) { Target = A(2), Removed = 2 }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new SelfActionRolled(A(1), SelfActionKind.LayingOnHands, SuccessRoll.Roll(new FixedDice(4, 4), 0, 0, risky: true)) { Target = A(2), Removed = 1 }));
        Assert.Equal(1, Reducer.Apply(declared, new SelfActionRolled(A(1), SelfActionKind.LayingOnHands, check) { Target = A(2), Removed = 1 }).FindModel(A(2))!.BloodMarkers);

        var castigator = Ready(Castigator).SetStatus(A(2), ModelStatus.Down);
        castigator.Do(new ActivateModel(Seat.One, A(1)));
        var ordered = Reducer.Apply(castigator.State, new SelfActionDeclared(A(1), SelfActionKind.EnforcedOrthodoxy));
        var risky = SuccessRoll.Roll(new FixedDice(4, 4, 1), 1, 0, risky: true);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(ordered, new SelfActionRolled(A(1), SelfActionKind.EnforcedOrthodoxy, risky)));
        Assert.Equal(ModelStatus.Standing, Reducer.Apply(ordered, new SelfActionRolled(A(1), SelfActionKind.EnforcedOrthodoxy, risky) { StoodUp = [A(2)] }).FindModel(A(2))!.Status);
    }

    [Fact]
    [Trait("Rule", "Records: matches with these ACTIONS play to the end and replay; the fields are hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        var plain = StateHash.Canonical(Ready(Rifleman).State);
        Assert.DoesNotContain("orthodoxy", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("laysOnHands", plain, StringComparison.Ordinal);
        Assert.Contains("orthodoxy=yes", StateHash.Canonical(Ready(Castigator).State), StringComparison.Ordinal);
        Assert.Contains("laysOnHands=yes", StateHash.Canonical(Ready(Prophet).State), StringComparison.Ordinal);
        var (stood, removed) = (0, 0);
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { EnforcesOrthodoxy = i % 2 == 0, LaysOnHands = i % 2 == 1 } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                stood += run.Log.OfType<SelfActionRolled>().Sum(s => s.StoodUp.Length);
                removed += run.Log.OfType<SelfActionRolled>().Sum(s => s.Removed);
            }
        }
        Assert.True(stood > 0 && removed > 0, $"{stood} stood up, {removed} markers removed");
    }
}
