using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R5e (U109): the Urn of the Bitter Ashes (-1 DICE to Ranged Attacks that target its bearer or a model
// within 3" of the bearer, Warbands p. 143) and the Sacrificial Lamb (the first BLOOD MARKER placed on the model is
// ignored, Warbands p. 70).
public class AuraAndWardTests
{
    private static readonly ModelProfile Urn = Rifleman with { SinisterWhispers = true };

    // A1 (4, 2) is activated and shoots B1 at (10, 2), short range in the open; B2 and A2 stand where given.
    private static BoardPlay Range(Func<ModelId, ModelProfile> profiles, string b2x = "30", string b2y = "30", string a2x = "2", string a2y = "30")
    {
        var play = new BoardPlay(2, profiles).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "10", "2").Put(B(2), b2x, b2y).Put(A(2), a2x, a2y);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    // The shot's Success Roll dice (all 1s: a miss, however many dice).
    private static int ShotDice(BoardPlay play) =>
        TurnSequence.Decide(play.State, new Shoot(Seat.One, A(1), B(1)), new FixedDice([1, 1, 1, 1])).Events.OfType<RangedSuccessRolled>().Single().Check.NetDice;

    [Fact]
    [Trait("Rule", "Urn of the Bitter Ashes: -1 DICE to Ranged Attacks that target the bearer or a model within 3\" of it")]
    public void TheUrnHindersShotsNearIt()
    {
        Assert.Equal(0, ShotDice(Range(_ => Rifleman)));
        Assert.Equal(-1, ShotDice(Range(id => id == B(1) ? Urn : Rifleman)));
        // A friend of the target 3" away (a 2" gap), or exactly 3" between the bases.
        Assert.Equal(-1, ShotDice(Range(id => id == B(2) ? Urn : Rifleman, "10", "5")));
        Assert.Equal(-1, ShotDice(Range(id => id == B(2) ? Urn : Rifleman, "10", "6")));
        Assert.Equal(0, ShotDice(Range(id => id == B(2) ? Urn : Rifleman, "10", "6.01")));
        // The rule names any model within 3": the shooter's own Urn near the target hinders it too.
        var own = Range(id => id == A(2) ? Urn : Rifleman, a2x: "10", a2y: "5");
        Assert.True(TurnSequence.Whispers(own.State, B(1)));
        Assert.Equal(-1, ShotDice(own));
        // A Down bearer still carries it; one Out of Action does not. Two Urns are still -1 DICE.
        var down = Range(id => id == B(2) ? Urn : Rifleman, "10", "5").SetStatus(B(2), ModelStatus.Down);
        Assert.Equal(-1, ShotDice(down));
        var gone = Range(id => id == B(2) ? Urn : Rifleman, "10", "5");
        gone.State = gone.State with { Models = [.. gone.State.Models.Select(m => m.Id == B(2) ? m with { Status = ModelStatus.OutOfAction, Position = null } : m)] };
        Assert.False(TurnSequence.Whispers(gone.State, B(1)));
        Assert.Equal(-1, ShotDice(Range(id => id == B(1) || id == B(2) ? Urn : Rifleman, "10", "5")));
        // Melee Attacks are not touched.
        var fight = Skirmish("4", "2", "5.1", "2", Brawler with { SinisterWhispers = true });
        Assert.Equal(0, fight.Do(new FightModel(Seat.One, A(1), B(1)), 1, 1).Events.OfType<MeleeSuccessRolled>().Single().Check.NetDice);
    }

    [Fact]
    [Trait("Rule", "Records: a Ranged Attack roll without the Urn's die is refused")]
    public void AShotWithoutTheUrnsDieIsRefused()
    {
        var play = Range(id => id == B(1) ? Urn : Rifleman);
        var events = TurnSequence.Decide(play.State, new Shoot(Seat.One, A(1), B(1)), new FixedDice([1, 1, 1, 1])).Events;
        var declared = Reducer.Apply(play.State, events.OfType<RangedAttackDeclared>().Single());
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new RangedSuccessRolled(A(1), B(1), SuccessRoll.Roll(new FixedDice([1, 1]), 0, 0, risky: false))));
    }

    private static BoardPlay Duel(ModelProfile target)
    {
        var play = new BoardPlay(2, id => id == B(1) ? target : Brawler).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "5.1", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "Sacrificial Lamb: the first BLOOD MARKER placed on the model is ignored, and only that one")]
    public void TheLambKeepsOffTheFirstMarker()
    {
        var warded = Brawler with { WardedByBlood = true };
        // Down (4 + 4): 1 marker, kept off.
        var play = Duel(warded);
        var first = play.Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 4, 4).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((InjuryOutcome.Down, true, 0), (first.Result, first.Warded, first.PlacedMarkers));
        Assert.Equal((0, true), (play.M(B(1)).BloodMarkers, play.M(B(1)).WardSpent));
        Assert.Contains("ward=spent", StateHash.Canonical(play.State), StringComparison.Ordinal);

        // Spent: the next marker stays. A Minor Hit (2 + 3) on an unspent model: kept off too.
        var spent = Duel(warded);
        spent.State = spent.State with { Models = [.. spent.State.Models.Select(m => m.Id == B(1) ? m with { WardSpent = true } : m)] };
        var second = spent.Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 2, 3).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((InjuryOutcome.MinorHit, false, 1), (second.Result, second.Warded, spent.M(B(1)).BloodMarkers));
        var minor = Duel(warded);
        Assert.True(minor.Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 2, 3).Events.OfType<InjuryRolled>().Single().Warded);
        Assert.Equal(0, minor.M(B(1)).BloodMarkers);
        // Without a Lamb: the marker stays.
        var plain = Duel(Brawler);
        Assert.False(plain.Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 4, 4).Events.OfType<InjuryRolled>().Single().Warded);
        Assert.Equal(1, plain.M(B(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Records: the Lamb's ward must be claimed exactly when it applies")]
    public void AForgedWardIsRefused()
    {
        foreach (var (target, claim) in new[] { (Brawler with { WardedByBlood = true }, false), (Brawler, true) })
        {
            var play = Duel(target);
            var events = TurnSequence.Decide(play.State, new FightModel(Seat.One, A(1), B(1)), new FixedDice([4, 4, 4, 4])).Events;
            var state = play.State;
            foreach (var e in events.TakeWhile(e => e is not InjuryRolled))
            {
                state = Reducer.Apply(state, e);
            }
            var rolled = events.OfType<InjuryRolled>().Single();
            Assert.Equal(!claim, rolled.Warded);
            Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, rolled with { Warded = claim }));
        }
    }

    [Fact]
    [Trait("Rule", "Records: matches with these profiles play to the end and replay; the fields are hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        var plain = StateHash.Canonical(Duel(Brawler).State);
        Assert.DoesNotContain("whispers", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("ward", plain, StringComparison.Ordinal);
        var kitted = StateHash.Canonical(Duel(Brawler with { SinisterWhispers = true, WardedByBlood = true }).State);
        Assert.Contains("whispers=yes", kitted, StringComparison.Ordinal);
        Assert.Contains("warded=yes", kitted, StringComparison.Ordinal);
        var (wards, whispered) = (0, 0);
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { SinisterWhispers = i == 0, WardedByBlood = i % 2 == 1 } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                wards += run.Log.OfType<InjuryRolled>().Count(i => i.Warded) + run.Log.OfType<MineExploded>().Count(x => x.Warded);
                whispered += run.Log.OfType<RangedSuccessRolled>().Count(r => r.Check.NetDice < 0);
            }
        }
        Assert.True(wards > 0 && whispered > 0, $"{wards} wards, {whispered} hindered shots");
    }
}
