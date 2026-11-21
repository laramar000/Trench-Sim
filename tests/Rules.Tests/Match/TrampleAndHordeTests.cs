using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R6d (U120): the Trample ACTION (Warbands pp. 88, 140): a Melee Attack without a weapon, with IGNORE ARMOUR,
// at a Down enemy; its own ACTION, once per activation, as Grind to Dust (U114). Overwhelming Horde (p. 137): its attacks
// without a weapon get +1 DICE for each other friendly model within 3".
public class TrampleAndHordeTests
{
    // A Melee Weapon with +1 DICE, +1 INJURY DICE and +2 to injure, and the model's own Melee +1 DICE.
    private static readonly ModelProfile Bull = new(L("6"), L("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true, meleeInjuryModifier: 2)
    {
        MeleeDice = 1, MeleeWeaponDice = 1, MeleeWeaponInjuryDice = 1, Tramples = true,
    };

    // An armoured target (Armour -2).
    private static readonly ModelProfile Knight = new(L("6"), L("0.5"), Rules.Core.Geometry.Length.Zero, armour: -2, hasMeleeWeapon: true);

    // A1 (4, 2) is activated beside B1 (5.1, 2), which is Down unless said otherwise.
    private static BoardPlay Near(ModelProfile attacker, ModelProfile? victim = null, ModelStatus status = ModelStatus.Down)
    {
        var play = new BoardPlay(2, id => id == A(1) ? attacker : id == B(1) ? victim ?? Brawler : Brawler).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "5.1", "2").Put(A(2), "2", "30").Put(B(2), "30", "30");
        play.SetStatus(B(1), status);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static FightModel Trample(ModelId target) => new(Seat.One, A(1), target) { Weapon = ModelProfile.TrampleWeapon };

    // Decide with more dice than needed: 5s hit without a Critical.
    private static IReadOnlyList<GameEvent> Run(BoardPlay play, Command command, int die = 5)
    {
        var result = TurnSequence.Decide(play.State, command, new FixedDice(Enumerable.Repeat(die, 12).ToArray()));
        Assert.True(result.IsAccepted, result.Rejection?.Text);
        play.State = result.State!;
        return result.Events;
    }

    [Fact]
    [Trait("Rule", "Trample pp. 88, 140: a Melee Attack without a weapon, with IGNORE ARMOUR, at a Down enemy; its own ACTION, once per activation")]
    public void TrampleAttacksWithoutAWeaponAndIgnoresArmour()
    {
        var play = Near(Bull, Knight);
        var events = Run(play, Trample(B(1)), die: 4);
        Assert.Equal(ModelProfile.TrampleWeapon, events.OfType<MeleeAttackDeclared>().Single().Weapon);
        // Only the model's own Melee die on the hit; on the injury, no weapon dice or modifier, and the Armour is ignored.
        Assert.Equal(1, events.OfType<MeleeSuccessRolled>().Single().Check.NetDice);
        var injury = events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal((1, 0), (injury.NetDice, injury.Modifier));
        Assert.Equal(ActionsTaken.Trample, play.State.Actions & (ActionsTaken.Trample | ActionsTaken.Fight | ActionsTaken.Grind));
        play.Refused(Trample(B(1)), ReasonCode.ActionAlreadyTaken);
        // A Fight is still allowed after it, with the weapon.
        Assert.NotEqual(ModelStatus.OutOfAction, play.M(B(1)).Status);
        var fight = Run(play, new FightModel(Seat.One, A(1), B(1)), die: 4);
        Assert.Equal(0, fight.OfType<MeleeAttackDeclared>().Single().Weapon);
    }

    [Fact]
    [Trait("Rule", "Trample: only by a model whose unit has it, and only at a Down enemy (any base)")]
    public void TramplesThatCannotBeTakenAreRefused()
    {
        Near(Brawler).Refused(Trample(B(1)), ReasonCode.NoSuchAction);
        Near(Bull, status: ModelStatus.Standing).Refused(Trample(B(1)), ReasonCode.TargetNotDown);
        // Grind to Dust's 32 mm limit is not Trample's.
        var large = new ModelProfile(L("6"), L("1.2"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true);
        var play = new BoardPlay(2, id => id == A(1) ? Bull : id == B(1) ? large : Brawler).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "6.0", "2").Put(A(2), "2", "30").Put(B(2), "30", "30").SetStatus(B(1), ModelStatus.Down);
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Null(TurnSequence.Check(play.State, Trample(B(1))));
        // A forged weapon 4 from a model without it is refused.
        var plain = Near(Brawler);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(plain.State, new MeleeAttackDeclared(A(1), B(1), false, null) { Weapon = ModelProfile.TrampleWeapon }));
    }

    [Fact]
    [Trait("Rule", "Overwhelming Horde p. 137: +1 DICE to its attacks without a weapon for each other friendly model within 3\"")]
    public void OverwhelmingHordeCountsFriendsWithinThreeInches()
    {
        var thrall = Brawler with { Horde = true };
        int HitDice(params (string X, string Y)[] friends)
        {
            var play = new BoardPlay(4, id => id == A(1) ? thrall : Brawler).ToActivation();
            play.Put(A(1), "4", "2").Put(B(1), "5.1", "2");
            for (var i = 0; i < friends.Length; i++)
            {
                play.Put(A(i + 2), friends[i].X, friends[i].Y);
            }
            play.Do(new ActivateModel(Seat.One, A(1)));
            Assert.Equal(friends.Count(f => f.X == "4"), TurnSequence.HordeDice(play.State, A(1), 0));
            return Run(play, new FightModel(Seat.One, A(1), B(1))).OfType<MeleeSuccessRolled>().Single().Check.NetDice;
        }
        Assert.Equal(0, HitDice());
        // Two friends within 3" (edge to edge), one far away.
        Assert.Equal(2, HitDice(("4", "4"), ("4", "5.5"), ("20", "20")));
        // Only its weapon 0, and only with the ability.
        Assert.Equal(0, TurnSequence.HordeDice(Near(thrall).State, A(1), 1));
        Assert.Equal(0, TurnSequence.HordeDice(Near(Brawler).State, A(1), 0));
    }

    [Fact]
    [Trait("Rule", "Records: matches with Trample and Overwhelming Horde play to the end and replay; the fields are hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        var plain = StateHash.Canonical(Near(Brawler).State);
        Assert.DoesNotContain("tramples", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("horde", plain, StringComparison.Ordinal);
        Assert.Contains("tramples=yes", StateHash.Canonical(Near(Bull).State), StringComparison.Ordinal);
        Assert.Contains("horde=yes", StateHash.Canonical(Near(Brawler with { Horde = true }).State), StringComparison.Ordinal);
        var tramples = 0;
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { Tramples = i % 2 == 0, Horde = i % 2 == 1 } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                tramples += run.Log.OfType<MeleeAttackDeclared>().Count(m => m.Weapon == ModelProfile.TrampleWeapon);
            }
        }
        Assert.True(tramples > 0, $"{tramples} tramples");
    }
}
