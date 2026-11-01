using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R5i (U114): Piston Legs give the Grind to Dust ACTION (Warbands p. 66): a Melee Attack that does not use a
// Melee Weapon, at an enemy model within 1" that is Down and on a base of 32 mm or smaller. It is its own ACTION, once
// per activation, besides a Fight ACTION.
public class GrindTests
{
    // A Melee Weapon with +1 DICE, +1 INJURY DICE and +2 to injure, and the model's own Melee +1 DICE.
    private static readonly ModelProfile Walker = new(L("6"), L("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true, meleeInjuryModifier: 2)
    {
        MeleeDice = 1, MeleeWeaponDice = 1, MeleeWeaponInjuryDice = 1, GrindsToDust = true,
    };

    // A1 (4, 2) is activated beside B1 (5.1, 2), which is Down; B1's profile is given.
    private static BoardPlay Near(ModelProfile attacker, ModelProfile? victim = null, string bx = "5.1")
    {
        var play = new BoardPlay(2, id => id == A(1) ? attacker : id == B(1) ? victim ?? Brawler : Brawler).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), bx, "2").Put(A(2), "2", "30").Put(B(2), "30", "30");
        play.SetStatus(B(1), ModelStatus.Down);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static FightModel Grind(ModelId target) => new(Seat.One, A(1), target) { Weapon = ModelProfile.GrindWeapon };

    // Decide with more dice than needed: 5s hit without a Critical.
    private static IReadOnlyList<GameEvent> Run(BoardPlay play, Command command, int die = 5)
    {
        var result = TurnSequence.Decide(play.State, command, new FixedDice(Enumerable.Repeat(die, 12).ToArray()));
        Assert.True(result.IsAccepted, result.Rejection?.Text);
        play.State = result.State!;
        return result.Events;
    }

    [Fact]
    [Trait("Rule", "Grind to Dust (Warbands p. 66): a Melee Attack without a Melee Weapon at a Down enemy; its own ACTION, once per activation")]
    public void TheGrindAttacksWithoutAWeapon()
    {
        var play = Near(Walker);
        var events = Run(play, Grind(B(1)));
        var declared = events.OfType<MeleeAttackDeclared>().Single();
        Assert.Equal((ModelProfile.GrindWeapon, false, false), (declared.Weapon, declared.OffHand, declared.FreeFight));
        // Only the model's own Melee die: not the weapon's DICE, INJURY DICE or modifier.
        var grindHit = events.OfType<MeleeSuccessRolled>().Single().Check;
        var grindInjury = events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(1, grindHit.NetDice);
        var bare = Near(Brawler with { MeleeDice = 1 });
        var bareInjury = Run(bare, new FightModel(Seat.One, A(1), B(1))).OfType<InjuryRolled>().Single().Injury;
        Assert.Equal((bareInjury.NetDice, bareInjury.Modifier), (grindInjury.NetDice, grindInjury.Modifier));
        Assert.Equal(ActionsTaken.Grind, play.State.Actions & (ActionsTaken.Grind | ActionsTaken.Fight));

        // With the weapon, a Fight is still allowed after it, and it uses the weapon.
        var again = Near(Walker);
        Run(again, Grind(B(1)));
        again.Refused(Grind(B(1)), ReasonCode.ActionAlreadyTaken);
        var missed = Near(Walker);
        Run(missed, Grind(B(1)), die: 1);
        Assert.Equal(2, Run(missed, new FightModel(Seat.One, A(1), B(1))).OfType<MeleeSuccessRolled>().Single().Check.NetDice);

        // And a Grind after a Fight that missed (all 1s).
        var after = Near(Walker);
        Assert.Empty(Run(after, new FightModel(Seat.One, A(1), B(1)), die: 1).OfType<InjuryRolled>());
        Assert.Equal((ModelStatus.Down, (ModelId?)A(1)), (after.M(B(1)).Status, after.State.ActiveModel));
        Assert.Contains(Run(after, Grind(B(1))), e => e is MeleeAttackDeclared { Weapon: ModelProfile.GrindWeapon });
    }

    [Fact]
    [Trait("Rule", "Grind to Dust: only with Piston Legs, at a Down enemy within 1\" on a base of 32 mm or smaller")]
    public void GrindsThatCannotBeTakenAreRefused()
    {
        Near(Brawler).Refused(Grind(B(1)), ReasonCode.NoSuchAction);
        var standing = Near(Walker);
        standing.SetStatus(B(1), ModelStatus.Standing);
        standing.Refused(Grind(B(1)), ReasonCode.TargetNotDown);
        // A 32 mm base (radius 0.63") may be ground; a larger one may not.
        var small = new ModelProfile(L("6"), L("0.63"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true);
        Assert.Null(TurnSequence.Check(Near(Walker, small, "5.13").State, Grind(B(1))));
        var large = new ModelProfile(L("6"), L("0.631"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true);
        Near(Walker, large, "5.131").Refused(Grind(B(1)), ReasonCode.TargetTooLarge);
        Near(Walker, bx: "6.01").Refused(Grind(B(1)), ReasonCode.TargetNotInMelee);
        var friend = Near(Walker);
        friend.Put(A(2), "4", "3.1").SetStatus(A(2), ModelStatus.Down);
        friend.Refused(Grind(A(2)), ReasonCode.TargetNotEnemy);
    }

    [Fact]
    [Trait("Rule", "Records: a forged Grind to Dust attack is refused")]
    public void ForgedGrindsAreRefused()
    {
        var declared = new MeleeAttackDeclared(A(1), B(1), false, null) { Weapon = ModelProfile.GrindWeapon };
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(Near(Brawler).State, declared));
        var standing = Near(Walker);
        standing.SetStatus(B(1), ModelStatus.Standing);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(standing.State, declared));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(Near(Walker).State, declared with { OffHand = true }));
        var play = Near(Walker);
        Run(play, Grind(B(1)), die: 1);
        Assert.Equal(ModelStatus.Down, play.M(B(1)).Status);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, declared));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeAttackDeclared(A(1), B(1), false, null) { Weapon = 2 });
    }

    [Fact]
    [Trait("Rule", "Records: matches with Piston Legs play to the end and replay; the field is hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        Assert.DoesNotContain("grinds", StateHash.Canonical(Near(Brawler).State), StringComparison.Ordinal);
        Assert.Contains("grinds=yes", StateHash.Canonical(Near(Walker).State), StringComparison.Ordinal);
        var grinds = 0;
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select(m => m with { Profile = m.Profile with { GrindsToDust = true } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                grinds += run.Log.OfType<MeleeAttackDeclared>().Count(m => m.Weapon == ModelProfile.GrindWeapon);
            }
        }
        Assert.True(grinds > 0, $"{grinds} grinds");
    }
}
