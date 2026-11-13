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

// Roster stage R5c (U107): battlekit that changes Injury Rolls: -1 INJURY DICE on attacks that hit a model with Holy Smoke
// or Zealot's Draught, and +1 INJURY MODIFIER per Grovelling Devotee on the bearer's Melee Attacks.
public class InjuryBattlekitTests
{
    private static BoardPlay Duel(ModelProfile a, ModelProfile b, string bx = "5.1")
    {
        var play = new BoardPlay(2, id => id == A(1) ? a : id == B(1) ? b : Brawler).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), bx, "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "Holy Smoke / Zealot's Draught: -1 INJURY DICE is added to Injury Rolls for attacks that hit the model")]
    public void SmokedModelsAreHarderToHurt()
    {
        var smoked = Brawler with { InjuryDiceTaken = -1 };
        // Melee: the hit (4, 4), then three dice for the Injury Roll, the lowest two kept: 6, 6, 1 is 7, Down.
        var fought = Duel(Brawler, smoked).Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 6, 6, 1).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((-1, 7, InjuryOutcome.Down), (fought.Injury.NetDice, fought.Injury.Total, fought.Injury.Outcome));

        // Ranged: the same.
        var gunman = Gunman;
        var shot = Duel(gunman, Gunman with { InjuryDiceTaken = -1 }, "12").Do(new Shoot(Seat.One, A(1), B(1)), 4, 4, 6, 6, 1).Events.OfType<InjuryRolled>().Single();
        Assert.Equal(-1, shot.Injury.NetDice);

        // A forged Injury Roll with the usual two dice is refused.
        var play = Duel(Brawler, smoked);
        var events = TurnSequence.Decide(play.State, new FightModel(Seat.One, A(1), B(1)), new FixedDice([4, 4, 6, 6, 1])).Events;
        var state = play.State;
        foreach (var e in events.TakeWhile(e => e is not InjuryRolled))
        {
            state = Reducer.Apply(state, e);
        }
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new InjuryRolled(A(1), B(1), InjuryRoll.Roll(new FixedDice([6, 6])))));
        Assert.Equal((0, 1), AttackDice.InjuryDice(Brawler, AttackKind.Melee, false, false, 0, smoked)); // one penalty die
        var critical = AttackDice.InjuryDice(Brawler, AttackKind.Melee, true, false, 0, smoked);
        Assert.Equal(0, critical.Bonus - critical.Penalty);
    }

    [Fact]
    [Trait("Rule", "Grovelling Devotee: +1 INJURY MODIFIER to the bearer's Melee Attacks for each, as a modifier of its own (p. 48)")]
    public void DevoteesAddToMeleeInjuries()
    {
        var follower = Brawler with { MeleeInjuryBonus = 2 };
        var armoured = new ModelProfile(L("6"), L("0.5"), Length.Zero, armour: -3, hasMeleeWeapon: true);
        Assert.Equal([0, -3, 2], AttackDice.InjuryModifiers(follower, armoured, AttackKind.Melee));
        Assert.Equal([0, -3], AttackDice.InjuryModifiers(Gunman with { MeleeInjuryBonus = 2 }, armoured, AttackKind.Ranged));
        // Negative modifiers floor at -3 on their own; the Devotees' +2 is added after (CombineModifiers).
        var weak = new ModelProfile(L("6"), L("0.5"), Length.Zero, hasMeleeWeapon: true, meleeInjuryModifier: -1) { MeleeInjuryBonus = 1 };
        Assert.Equal(-2, InjuryRoll.CombineModifiers(AttackDice.InjuryModifiers(weak, armoured, AttackKind.Melee)));

        var rolled = Duel(follower, Brawler).Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 3, 3).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((2, 8, InjuryOutcome.Down), (rolled.Injury.Modifier, rolled.Injury.Total, rolled.Injury.Outcome));
        Assert.Throws<ArgumentOutOfRangeException>(() => Brawler with { MeleeInjuryBonus = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => Brawler with { MeleeInjuryBonus = ModelProfile.MaxModifier + 1 });
        Assert.Equal(2, (follower with { SecondMeleeWeapon = new Weapon(Length.Zero) }).Using(AttackKind.Melee, 1).MeleeInjuryBonus);
    }

    [Fact]
    [Trait("Rule", "Records: matches with these profiles play to the end and replay; the fields are hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        Assert.DoesNotContain("injuryDiceTaken", StateHash.Canonical(Duel(Brawler, Brawler).State), StringComparison.Ordinal);
        Assert.Contains("injuryDiceTaken=-1", StateHash.Canonical(Duel(Brawler, Brawler with { InjuryDiceTaken = -1 }).State), StringComparison.Ordinal);
        Assert.Contains("meleeInjuryBonus=1", StateHash.Canonical(Duel(Brawler with { MeleeInjuryBonus = 1 }, Brawler).State), StringComparison.Ordinal);
        var rolls = 0;
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { InjuryDiceTaken = i % 2 == 0 ? -1 : 0, MeleeInjuryBonus = i % 3 } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                rolls += run.Log.OfType<InjuryRolled>().Count();
            }
        }
        Assert.True(rolls > 0);
    }
}
