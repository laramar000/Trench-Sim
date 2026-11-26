using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R6a (U117): New Antioch unit abilities. Absolute Faith (Warbands p. 28), Expert Medic and Finish the Fallen
// (p. 34), Shock Charge (p. 32) and Onward Christian Soldiers! (p. 29).
public class NewAntiochAbilityTests
{
    private static Shoot Shot => new(Seat.One, A(1), B(1));

    private static FightModel Fight => new(Seat.One, A(1), B(1));

    private static ChargeModel Charge => new(Seat.One, A(1), B(1));

    [Fact]
    [Trait("Rule", "Absolute Faith p. 28: the opponent cannot spend BLOOD MARKERS to add -DICE to its Ranged Attacks")]
    public void AbsoluteFaithShotsTakeNoMarkers()
    {
        var priest = Rifleman with { ShotsIgnoreMarkers = true };
        var play = new BoardPlay(2, id => id == A(1) ? priest : Rifleman).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Mark(A(1), 2);
        play.Do(new ActivateModel(Seat.One, A(1)));
        var before = play.State;
        var shot = play.Do(Shot, 1, 1);
        Assert.DoesNotContain(shot.Events, e => e is DecisionRequested);
        Assert.Equal(0, shot.Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
        // A forged offer is refused.
        var declared = Reducer.Apply(before, shot.Events.OfType<RangedAttackDeclared>().Single());
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, Seat.Two)));

        // Without it the opponent is asked; and the priest's Dash rolls still are.
        var plain = new BoardPlay(2, _ => Rifleman).ToActivation();
        plain.Put(A(1), "4", "2").Put(B(1), "12", "2").Mark(A(1), 2);
        plain.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, Seat.Two), plain.Do(Shot).Events[^1]);
        var dash = new BoardPlay(2, id => id == A(1) ? priest : Rifleman).ToActivation();
        dash.Put(A(1), "4", "2").Mark(A(1), 1);
        dash.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, Seat.Two), dash.Do(new DashModel(Seat.One, A(1), Path(("8", "2")))).Events[^1]);
    }

    [Fact]
    [Trait("Rule", "Expert Medic p. 34: +2 DICE to the Risky Success Roll of its Treat ACTION; Holy Water adds its +1")]
    public void AnExpertMedicTreatsWithTwoMoreDice()
    {
        foreach (var (medic, dice) in new[] { (Rifleman with { Treats = true, ExpertMedic = true }, 2), (Rifleman with { Treats = true, ExpertMedic = true, HealingWaters = true }, 3) })
        {
            var play = new BoardPlay(2, id => id == A(1) ? medic : Rifleman).ToActivation();
            play.Put(A(1), "4", "2").Put(A(2), "4", "4").Put(B(1), "30", "30").Put(B(2), "30", "20").Mark(A(2), 1);
            play.Do(new ActivateModel(Seat.One, A(1)));
            Assert.Equal(dice, TurnSequence.TreatDice(play.State, A(1)));
            var rolled = play.Do(new TreatModel(Seat.One, A(1), A(2), TreatChoice.RemoveMarker), [.. Enumerable.Repeat(1, dice), 4, 4]).Events.OfType<TreatRolled>().Single();
            Assert.Equal((dice, true), (rolled.Check.NetDice, rolled.Treated));
        }
    }

    [Fact]
    [Trait("Rule", "Finish the Fallen p. 34: +1 INJURY DICE to its Melee Attacks' Injury Rolls at a Down target that is neither CULT nor DEMONIC")]
    public void FinishTheFallenAddsAnInjuryDie()
    {
        var medic = Brawler with { FinishesTheFallen = true };
        int InjuryDice(ModelProfile attacker, ModelProfile target, ModelStatus status, int expected)
        {
            var play = new BoardPlay(2, id => id == A(1) ? attacker : id == B(1) ? target : Brawler).ToActivation();
            play.Put(A(1), "4", "2").Put(B(1), "5.1", "2").SetStatus(B(1), status);
            play.Do(new ActivateModel(Seat.One, A(1)));
            var events = play.Do(Fight, [4, 4, .. Enumerable.Repeat(1, 2 + expected)]).Events;
            return events.OfType<InjuryRolled>().Single().Injury.NetDice;
        }
        Assert.Equal(2, InjuryDice(medic, Brawler, ModelStatus.Down, 2));
        Assert.Equal(1, InjuryDice(Brawler, Brawler, ModelStatus.Down, 1));
        Assert.Equal(0, InjuryDice(medic, Brawler, ModelStatus.Standing, 0));
        Assert.Equal(1, InjuryDice(medic, Brawler with { Traits = ModelTraits.Cult }, ModelStatus.Down, 1));
        Assert.Equal(1, InjuryDice(medic, Brawler with { Traits = ModelTraits.Demonic }, ModelStatus.Down, 1));
        Assert.Equal(2, InjuryDice(medic, Brawler with { Traits = ModelTraits.Principality }, ModelStatus.Down, 2));
    }

    [Fact]
    [Trait("Rule", "Shock Charge p. 32: roll 1 extra D6 for the Charge Bonus and use the highest")]
    public void AShockChargeKeepsTheHigherDie()
    {
        var trooper = Brawler with { ExtraChargeDie = true };
        var play = Skirmish("4", "2", "12", "2", trooper);
        var before = play.State;
        var rolled = play.Do(Charge, 1, 3).Events.OfType<ChargeRolled>().Single();
        Assert.Equal((3, 1), (rolled.Die, rolled.Dropped));
        var other = Skirmish("4", "2", "12", "2", trooper).Do(Charge, 5, 2).Events.OfType<ChargeRolled>().Single();
        Assert.Equal((5, 2), (other.Die, other.Dropped));

        // The log must carry both dice, the kept one the higher; a plain charge has one.
        var declared = Reducer.Apply(before, new ChargeDeclared(A(1), B(1)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new ChargeRolled(A(1), B(1), 3)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new ChargeRolled(A(1), B(1), 2) { Dropped = 4 }));
        Reducer.Apply(declared, new ChargeRolled(A(1), B(1), 4) { Dropped = 4 });
        var plain = Skirmish("4", "2", "12", "2");
        var plainDeclared = Reducer.Apply(plain.State, new ChargeDeclared(A(1), B(1)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(plainDeclared, new ChargeRolled(A(1), B(1), 4) { Dropped = 2 }));
        Assert.Equal(0, plain.Do(Charge, 3).Events.OfType<ChargeRolled>().Single().Dropped);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChargeRolled(A(1), B(1), 4) { Dropped = 7 });
    }

    [Fact]
    [Trait("Rule", "Onward Christian Soldiers! p. 29: friendly PRINCIPALITY models within 8\" of a Trench Cleric have NEGATE FEAR")]
    public void ATrenchClericLiftsFear()
    {
        var soldier = Brawler with { Traits = ModelTraits.Principality };
        var cleric = Brawler with { Traits = ModelTraits.Principality, InspiresFaithful = true };
        var fearsome = Brawler with { Traits = ModelTraits.Fear };
        bool Feared(ModelProfile attacker, ModelProfile friend, string friendX)
        {
            var play = new BoardPlay(2, id => id == A(1) ? attacker : id == A(2) ? friend : id == B(1) ? fearsome : Brawler).ToActivation();
            play.Put(A(1), "4", "2").Put(B(1), "5.1", "2").Put(A(2), friendX, "2").Put(B(2), "30", "30");
            play.Do(new ActivateModel(Seat.One, A(1)));
            var fear = TurnSequence.Fears(play.State, A(1), B(1));
            Assert.Equal(fear, play.Do(Fight, [.. Enumerable.Repeat(1, fear ? 3 : 2)]).Events.OfType<MeleeAttackDeclared>().Single().Fear);
            return fear;
        }
        // The cleric 7" away (an edge gap of 6"), and 9.5" away (8.5" gap).
        Assert.False(Feared(soldier, cleric, "-3"));
        Assert.True(Feared(soldier, cleric, "-5.5"));
        Assert.True(Feared(Brawler, cleric, "-3"));   // not PRINCIPALITY
        Assert.True(Feared(soldier, soldier, "-3"));  // no cleric
        Assert.False(Feared(cleric, Brawler, "-5.5")); // the cleric itself
        Assert.True(TurnSequence.Inspired(new BoardPlay(1, _ => cleric).ToActivation().Put(A(1), "4", "2").State, A(1)));
    }

    [Fact]
    [Trait("Rule", "Records: the abilities are in the canonical hash only when present")]
    public void TheHashShowsTheAbilitiesOnlyWhenPresent()
    {
        var plain = StateHash.Canonical(Skirmish("4", "2", "12", "2").State);
        var fields = new[] { "shotsIgnoreMarkers=yes", "expertMedic=yes", "finishesTheFallen=yes", "extraChargeDie=yes", "inspires=yes" };
        Assert.All(fields, f => Assert.DoesNotContain(f, plain, StringComparison.Ordinal));
        var kitted = StateHash.Canonical(Skirmish("4", "2", "12", "2", Brawler with
        {
            ShotsIgnoreMarkers = true, ExpertMedic = true, FinishesTheFallen = true, ExtraChargeDie = true, InspiresFaithful = true,
            Traits = ModelTraits.Principality | ModelTraits.Cult | ModelTraits.Demonic,
        }).State);
        Assert.All(fields, f => Assert.Contains(f, kitted, StringComparison.Ordinal));
        Assert.Contains("traits=Principality, Cult, Demonic", kitted, StringComparison.Ordinal);
    }
}
