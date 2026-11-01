using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using Rules.Tests;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R5h (U113): a Medi-kit gives the Treat ACTION (p. 84): once per activation, a Risky Success Roll; on a
// success the model itself or a friendly model within 1" loses 1 BLOOD MARKER, or a Down friend stands up. Holy Water of
// Lalibela gives +1 DICE to it (Warbands p. 44).
public class TreatTests
{
    private static readonly ModelProfile Medic = Rifleman with { Treats = true };
    private static readonly ModelProfile Watered = Medic with { HealingWaters = true };

    // A1 (4, 2) is activated; A2 (a friend) stands where given; B1 is far away.
    private static BoardPlay Near(ModelProfile medic, string a2x = "4", string a2y = "4")
    {
        var play = new BoardPlay(2, id => id == A(1) ? medic : Rifleman).ToActivation();
        play.Put(A(1), "4", "2").Put(A(2), a2x, a2y).Put(B(1), "30", "30").Put(B(2), "30", "20");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static TreatModel Treat(ModelId target, TreatChoice choice = TreatChoice.RemoveMarker) => new(Seat.One, A(1), target, choice);

    [Fact]
    [Trait("Rule", "Treat (p. 84): on a success, 1 BLOOD MARKER comes off the model itself or a friend within 1\"; once per activation")]
    public void ATreatRemovesOneMarker()
    {
        // A2 is exactly 1" away (centres 2" apart).
        var play = Near(Medic).Mark(A(2), 2);
        var events = play.Do(Treat(A(2)), 4, 4).Events;
        Assert.Equal(A(2), events.OfType<TreatDeclared>().Single().Target);
        var rolled = events.OfType<TreatRolled>().Single();
        Assert.Equal((true, true, 0), (rolled.Treated, rolled.Check.Risky, rolled.Check.NetDice));
        Assert.DoesNotContain(events, e => e is DecisionRequested or ActivationEnded);
        Assert.Equal((1, (ModelId?)A(1), ActionsTaken.Treat), (play.M(A(2)).BloodMarkers, play.State.ActiveModel, play.State.Actions & ActionsTaken.Treat));
        Assert.Null(play.State.InProgress);
        play.Refused(Treat(A(2)), ReasonCode.ActionAlreadyTaken);
        // Other actions go on.
        play.Do(new MoveModel(Seat.One, A(1), Path(("6", "2"))));

        // The model itself.
        var self = Near(Medic).Mark(A(1), 1);
        var asked = self.Do(Treat(A(1)));
        Assert.Equal(Seat.Two, asked.Events.OfType<DecisionRequested>().Single().Seat);
        Assert.True(self.Do(new SpendMarkers(Seat.Two, 0), 5, 5).Events.OfType<TreatRolled>().Single().Treated);
        Assert.Equal(0, self.M(A(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Treat (p. 84): a Down friend within 1\" stands up on a success")]
    public void ATreatStandsUpADownFriend()
    {
        var play = Near(Medic).SetStatus(A(2), ModelStatus.Down);
        Assert.True(play.Do(Treat(A(2), TreatChoice.StandUp), 6, 1).Events.OfType<TreatRolled>().Single().Treated);
        Assert.Equal(ModelStatus.Standing, play.M(A(2)).Status);
    }

    [Fact]
    [Trait("Rule", "Treat (p. 84): a Risky roll; a Failure treats nobody and ends the activation (not with a Pontiff's Crucifix)")]
    public void AFailedTreatEndsTheActivation()
    {
        var play = Near(Medic).Mark(A(2), 1);
        var events = play.Do(Treat(A(2)), 3, 3).Events;
        Assert.False(events.OfType<TreatRolled>().Single().Treated);
        Assert.Contains(events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
        Assert.Equal(1, play.M(A(2)).BloodMarkers);

        var blessed = Near(Medic with { KeepsGoingAfterRisky = true }).Mark(A(2), 1);
        Assert.DoesNotContain(blessed.Do(Treat(A(2)), 3, 3).Events, e => e is ActivationEnded);
        Assert.Equal(((ModelId?)A(1), false), (blessed.State.ActiveModel, blessed.State.MustEndActivation));
        Assert.Throws<ArgumentException>(() => new TreatRolled(A(1), A(2), TreatChoice.RemoveMarker, SuccessRoll.Roll(new FixedDice(3, 3), 0, 0, risky: true), true));
    }

    [Fact]
    [Trait("Rule", "Treat: the opponent may spend the model's markers as -DICE; a spent last marker leaves nothing to remove")]
    public void SpentMarkersArePenaltyDice()
    {
        var play = Near(Medic).Mark(A(1), 1);
        play.Do(Treat(A(1)));
        var rolled = play.Do(new SpendMarkers(Seat.Two, 1), 6, 6, 6).Events.OfType<TreatRolled>().Single();
        Assert.Equal((-1, false), (rolled.Check.NetDice, rolled.Treated));
        Assert.Equal(0, play.M(A(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Holy Water of Lalibela (Warbands p. 44): +1 DICE to the Treat roll")]
    public void HolyWaterAddsADie()
    {
        var play = Near(Watered).Mark(A(2), 1);
        var rolled = play.Do(Treat(A(2)), 1, 6, 6).Events.OfType<TreatRolled>().Single();
        Assert.Equal((1, true), (rolled.Check.NetDice, rolled.Treated));
        Assert.Equal(1, TurnSequence.TreatDice(Near(Watered).State, A(1)));
        Assert.Equal(0, TurnSequence.TreatDice(Near(Medic).State, A(1)));
    }

    [Fact]
    [Trait("Rule", "Treat: only with a Medi-kit, only the model itself or a friend within 1\", and only when there is something to treat")]
    public void TreatsThatCannotBeTakenAreRefused()
    {
        Near(Rifleman).Mark(A(2), 1).Refused(Treat(A(2)), ReasonCode.NoSuchAction);
        var play = Near(Medic, "4", "4.01").Mark(A(2), 1).Mark(B(1), 1).Mark(B(2), 1);
        play.Refused(Treat(A(2)), ReasonCode.NothingToTreat);         // just beyond 1"
        play.Refused(Treat(A(1)), ReasonCode.NothingToTreat);         // no markers
        play.Refused(Treat(A(1), TreatChoice.StandUp), ReasonCode.NothingToTreat);
        play.Refused(Treat(B(1)), ReasonCode.NothingToTreat);         // an enemy
        Near(Medic).Refused(Treat(A(2), TreatChoice.StandUp), ReasonCode.NothingToTreat); // standing
        Near(Medic).Refused(Treat(A(2), (TreatChoice)7), ReasonCode.InvalidChoice);
    }

    [Fact]
    [Trait("Rule", "Records: a forged Treat is refused")]
    public void ForgedTreatsAreRefused()
    {
        var plain = Near(Rifleman).Mark(A(2), 1);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(plain.State, new TreatDeclared(A(1), A(2), TreatChoice.RemoveMarker)));
        var play = Near(Medic).Mark(A(2), 1);
        var check = SuccessRoll.Roll(new FixedDice(4, 4), 0, 0, risky: true);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new TreatRolled(A(1), A(2), TreatChoice.RemoveMarker, check, true)));
        var declared = Reducer.Apply(play.State, new TreatDeclared(A(1), A(2), TreatChoice.RemoveMarker));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new TreatRolled(A(1), A(2), TreatChoice.RemoveMarker, check, false)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new TreatRolled(A(1), A(2), TreatChoice.StandUp, check, false)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new TreatRolled(A(1), A(2), TreatChoice.RemoveMarker, SuccessRoll.Roll(new FixedDice(4, 4, 4), 1, 0, risky: true), true)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new TreatDeclared(A(1), A(2), TreatChoice.RemoveMarker)));
        Assert.Equal(0, Reducer.Apply(declared, new TreatRolled(A(1), A(2), TreatChoice.RemoveMarker, check, true)).FindModel(A(2))!.BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Records: matches with Medi-kits play to the end and replay; the fields are hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        var plain = StateHash.Canonical(Near(Rifleman).State);
        Assert.DoesNotContain("treats", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("healingWaters", plain, StringComparison.Ordinal);
        var kitted = StateHash.Canonical(Near(Watered).State);
        Assert.Contains("treats=yes", kitted, StringComparison.Ordinal);
        Assert.Contains("healingWaters=yes", kitted, StringComparison.Ordinal);
        var treats = 0;
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { Treats = true, HealingWaters = i % 2 == 0 } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                treats += run.Log.OfType<TreatRolled>().Count(t => t.Treated);
            }
        }
        Assert.True(treats > 0, $"{treats} treats");
    }
}
