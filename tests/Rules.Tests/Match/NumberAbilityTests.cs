using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R6c (U119): unit abilities that change a number. Agile (Warbands pp. 57, 86), Frightening Speed (p. 138),
// Loping Dash (p. 113), Mad Dash (p. 58), Undead Fortitude (p. 134), Abiotic Life (p. 116), Stealth Generator (p. 110),
// Mubarizun (p. 81), Counter-Charge (p. 87), Memento Mori (p. 53); Corpulent and Artificial Life are INJURY DICE taken.
public class NumberAbilityTests
{
    private static FightModel Fight => new(Seat.One, A(1), B(1));

    private static ChargeModel Charge => new(Seat.One, A(1), B(1));

    [Fact]
    [Trait("Rule", "Agile, Frightening Speed, Loping Dash, Mad Dash: +1 DICE to the Dash roll; Agile also to a climb roll")]
    public void DashAbilitiesAddDice()
    {
        int DashDice(ModelProfile profile)
        {
            var play = new BoardPlay(2, id => id == A(1) ? profile : Rifleman).ToActivation();
            play.Put(A(1), "4", "2").Put(B(1), "30", "30");
            play.Do(new ActivateModel(Seat.One, A(1)));
            return play.Do(new DashModel(Seat.One, A(1), Path(("8", "2"))), [.. Enumerable.Repeat(6, 2 + (profile.Agile ? 1 : 0) + (profile.SwiftDash ? 1 : 0))]).Events.OfType<DashRolled>().Single().Check.NetDice;
        }
        Assert.Equal(0, DashDice(Rifleman));
        Assert.Equal(1, DashDice(Rifleman with { SwiftDash = true }));
        Assert.Equal(1, DashDice(Rifleman with { Agile = true }));
        Assert.Equal(2, DashDice(Rifleman with { Agile = true, SwiftDash = true }));
        var agile = new BoardPlay(2, id => id == A(1) ? Rifleman with { Agile = true, ClimbDice = 1 } : Rifleman).ToActivation();
        Assert.Equal(2, TurnSequence.ClimbDice(agile.State, A(1)));
        Assert.Equal(0, TurnSequence.FanfareDice(agile.Put(A(1), "4", "2").State, A(1)));
    }

    [Fact]
    [Trait("Rule", "Frightening Speed p. 138: Movement is not halved when it stands up")]
    public void FrighteningSpeedStandsUpAtFullMovement()
    {
        BoardPlay Down(ModelProfile profile)
        {
            var play = new BoardPlay(2, id => id == A(1) ? profile : Rifleman).ToActivation();
            play.Put(A(1), "4", "2").Put(B(1), "30", "30").SetStatus(A(1), ModelStatus.Down);
            return play;
        }
        var quick = Down(Rifleman with { QuickToRise = true });
        var before = quick.State;
        var stood = quick.Do(new ActivateModel(Seat.One, A(1))).Events.OfType<ModelStoodUp>().Single();
        Assert.Equal((true, false, ModelStatus.Standing), (stood.Quick, quick.State.MovementHalved, quick.M(A(1)).Status));
        var plain = Down(Rifleman);
        Assert.False(plain.Do(new ActivateModel(Seat.One, A(1))).Events.OfType<ModelStoodUp>().Single().Quick);
        Assert.True(plain.State.MovementHalved);
        // A forged claim either way is refused.
        var started = Reducer.Apply(before, new ActivationStarted(Seat.One, A(1)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(started, new ModelStoodUp(A(1))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(Reducer.Apply(Down(Rifleman).State, new ActivationStarted(Seat.One, A(1))), new ModelStoodUp(A(1)) { Quick = true }));
    }

    [Fact]
    [Trait("Rule", "Undead Fortitude -1 unless FIRE, Abiotic Life -1 from GAS, Mubarizun +1 against TOUGH; Corpulent and Artificial Life as INJURY DICE taken")]
    public void InjuryDiceAbilities()
    {
        var undead = Brawler with { UndeadFortitude = true };
        var abiotic = Brawler with { AbioticLife = true };
        Assert.Equal(-1, AttackDice.TakenDice(undead, WeaponKeywords.None));
        Assert.Equal(0, AttackDice.TakenDice(undead, WeaponKeywords.Fire));
        Assert.Equal(0, AttackDice.TakenDice(abiotic, WeaponKeywords.None));
        Assert.Equal(-1, AttackDice.TakenDice(abiotic, WeaponKeywords.Gas));
        Assert.Equal(-3, AttackDice.TakenDice(undead with { InjuryDiceTaken = -2 }, WeaponKeywords.Gas));
        var hunter = Brawler with { HuntsTheTough = true };
        Assert.Equal(1, AttackDice.HuntDice(hunter, Brawler with { Traits = ModelTraits.Tough }));
        Assert.Equal(0, AttackDice.HuntDice(hunter, Brawler));
        Assert.Equal(0, AttackDice.HuntDice(Brawler, Brawler with { Traits = ModelTraits.Tough }));

        // In a fight: the Injury Roll's dice.
        int InjuryDice(ModelProfile attacker, ModelProfile target, int dice)
        {
            var play = new BoardPlay(2, id => id == A(1) ? attacker : id == B(1) ? target : Brawler).ToActivation();
            play.Put(A(1), "4", "2").Put(B(1), "5.1", "2");
            play.Do(new ActivateModel(Seat.One, A(1)));
            return play.Do(Fight, [4, 4, .. Enumerable.Repeat(1, dice)]).Events.OfType<InjuryRolled>().Single().Injury.NetDice;
        }
        Assert.Equal(-1, InjuryDice(Brawler, undead, 3));
        Assert.Equal(1, InjuryDice(hunter, Brawler with { Traits = ModelTraits.Tough }, 3));
        Assert.Equal(0, InjuryDice(hunter, undead with { Traits = ModelTraits.Tough }, 2));
    }

    [Fact]
    [Trait("Rule", "Stealth Generator p. 110: -1 DICE to Ranged Attacks that target it")]
    public void StealthGeneratorCostsAShotADie()
    {
        int ShotDice(ModelProfile target, params int[] dice)
        {
            var play = new BoardPlay(2, id => id == B(1) ? target : Rifleman).ToActivation();
            play.Put(A(1), "4", "2").Put(B(1), "12", "2");
            play.Do(new ActivateModel(Seat.One, A(1)));
            return play.Do(new Shoot(Seat.One, A(1), B(1)), dice).Events.OfType<RangedSuccessRolled>().Single().Check.NetDice;
        }
        Assert.Equal(0, ShotDice(Rifleman, 1, 1));
        Assert.Equal(-1, ShotDice(Rifleman with { Stealthy = true }, 1, 1, 1));
    }

    [Fact]
    [Trait("Rule", "Counter-Charge p. 87: a Charge as its first ACTION gives +1 DICE to its Melee Attacks for the rest of the activation")]
    public void CounterChargeAddsAMeleeDie()
    {
        var janissary = Brawler with { CounterCharge = true };
        var play = Skirmish("4", "2", "8", "2", janissary);
        play.Do(Charge, 6);
        Assert.True(play.State.CounterCharging);
        Assert.Contains("counterCharging", StateHash.Canonical(play.State), StringComparison.Ordinal);
        Assert.Equal(1, TurnSequence.CounterDice(play.State, A(1)));
        Assert.Equal(1, play.Do(Fight, 1, 1, 1).Events.OfType<MeleeSuccessRolled>().Single().Check.NetDice);
        play.Do(new EndActivation(Seat.One));
        Assert.False(play.State.CounterCharging);

        // Not after another ACTION, and not without the ability.
        var moved = Skirmish("4", "2", "8", "2", janissary with { Fortifies = true });
        moved.Do(new TakeSelfAction(Seat.One, A(1), SelfActionKind.Fortify), 4, 4);
        moved.Do(Charge, 6);
        Assert.False(moved.State.CounterCharging);
        var plain = Skirmish("4", "2", "8", "2");
        plain.Do(Charge, 6);
        Assert.False(plain.State.CounterCharging);
        Assert.DoesNotContain("counterCharging", StateHash.Canonical(plain.State), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "Memento Mori p. 53: its first Out of Action result counts as No Effect; it is never TOUGH")]
    public void MementoMoriTurnsTheFirstOutOfActionIntoNoEffect()
    {
        var prophet = Brawler with { MementoMori = true, Traits = ModelTraits.Tough };
        var play = new BoardPlay(2, id => id == B(1) ? prophet : Brawler).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "5.1", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        var before = play.State;
        var rolled = play.Do(Fight, 4, 4, 6, 6).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((InjuryOutcome.OutOfAction, true, false, InjuryOutcome.NoEffect), (rolled.Injury.Outcome, rolled.Spared, rolled.Tough, rolled.Result));
        Assert.Equal((ModelStatus.Standing, true, 0), (play.M(B(1)).Status, play.M(B(1)).ToughSpent, play.M(B(1)).BloodMarkers));
        Assert.Throws<ArgumentException>(() => new InjuryRolled(A(1), B(1), InjuryRoll.Roll(new FixedDice(1, 1), 0, 0, [], false)) { Spared = true });

        // A forged claim either way is refused.
        var declared = before;
        foreach (var e in TurnSequence.Decide(before, Fight, new FixedDice([4, 4, 6, 6])).Events.TakeWhile(e => e is not InjuryRolled))
        {
            declared = Reducer.Apply(declared, e);
        }
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new InjuryRolled(rolled.Attacker, rolled.Target, rolled.Injury)));

        // Once spent (ToughSpent records it), the next Out of Action result takes the model out.
        var spent = declared with { Models = [.. declared.Models.Select(m => m.Id == B(1) ? m with { ToughSpent = true } : m)] };
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(spent, rolled));
        Assert.Equal(ModelStatus.OutOfAction, Reducer.Apply(spent, new InjuryRolled(rolled.Attacker, rolled.Target, rolled.Injury)).FindModel(B(1))!.Status);
    }

    [Fact]
    [Trait("Rule", "Records: matches with these abilities play to the end and replay; the fields are hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        var fields = new[] { "agile", "swiftDash", "quickToRise", "undeadFortitude", "abioticLife", "stealthy", "huntsTheTough", "counterCharge", "mementoMori" };
        var plain = StateHash.Canonical(Skirmish("4", "2", "12", "2").State);
        Assert.All(fields, f => Assert.DoesNotContain(f, plain, StringComparison.Ordinal));
        var all = Brawler with
        {
            Agile = true, SwiftDash = true, QuickToRise = true, UndeadFortitude = true, AbioticLife = true, Stealthy = true,
            HuntsTheTough = true, CounterCharge = true, MementoMori = true,
        };
        var kitted = StateHash.Canonical(Skirmish("4", "2", "12", "2", all).State);
        Assert.All(fields, f => Assert.Contains($"{f}=yes", kitted, StringComparison.Ordinal));
        var spared = 0;
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with
                {
                    Profile = m.Profile with
                    {
                        Agile = i % 2 == 0, SwiftDash = i % 3 == 0, QuickToRise = i % 2 == 1, UndeadFortitude = i % 2 == 0, AbioticLife = i % 3 == 1,
                        Stealthy = i % 2 == 1, HuntsTheTough = true, CounterCharge = true, MementoMori = i % 2 == 0, Traits = m.Profile.Traits | (i % 3 == 2 ? ModelTraits.Tough : 0),
                    },
                }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                spared += run.Log.OfType<InjuryRolled>().Count(r => r.Spared) + run.Log.OfType<MineExploded>().Count(r => r.Spared);
            }
        }
        Assert.True(spared > 0, $"{spared} spared");
    }
}
