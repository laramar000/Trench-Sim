using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R5d (U108): battlekit for Risky rolls and Cover. A Pontiff's Crucifix keeps the activation going after a
// failed Risky roll (Warbands p. 36), a Mountaineer Kit gives +1 DICE to Risky climb rolls (p. 84), a Musical Instrument
// gives +1 DICE to the Dash rolls of friendly models within 4" (p. 85), and a Cloak of the Mountain makes Cover -2 DICE
// against its wearer (Warbands p. 77).
public class RiskyBattlekitTests
{
    private static readonly Scenario Towers = ClaimNoMansLand.CreateWithTowers();

    private static readonly ModelProfile Blessed = Rifleman with { KeepsGoingAfterRisky = true };

    // A1 at (2, 3) and activated; A2 is put where given; the profiles are the given ones.
    private static BoardPlay Active(Func<ModelId, ModelProfile> profiles, string a2x = "2", string a2y = "11")
    {
        var play = new BoardPlay(2, profiles).ToActivation();
        play.Put(A(2), a2x, a2y);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static DashModel Dash => new(Seat.One, A(1), Path(("8", "3")));

    [Fact]
    [Trait("Rule", "Pontiff's Crucifix (Warbands p. 36): a failed Dash roll does not end the activation")]
    public void TheCrucifixKeepsTheActivationAfterAFailedDash()
    {
        var play = Active(id => id == A(1) ? Blessed : Rifleman);
        var result = play.Do(Dash, 3, 3);
        Assert.Equal(SuccessOutcome.Failure, result.Events.OfType<DashRolled>().Single().Check.Outcome);
        Assert.DoesNotContain(result.Events, e => e is ActivationEnded or ModelMoved);
        Assert.Equal((A(1), false), (play.State.ActiveModel, play.State.MustEndActivation));
        Assert.Equal(P("2", "3"), play.M(A(1)).Position);
        // The Dash is spent; a Move is still allowed.
        play.Refused(Dash, ReasonCode.ActionAlreadyTaken);
        play.Do(new MoveModel(Seat.One, A(1), Path(("6", "3"))));
        Assert.Equal(P("6", "3"), play.M(A(1)).Position);
        // A forged end for the failure is refused.
        var again = Active(id => id == A(1) ? Blessed : Rifleman);
        again.Do(Dash, 3, 3);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(again.State, new ActivationEnded(A(1), ActivationEndReason.RiskyFailure)));
        // Without it the activation ends.
        var plain = Active(_ => Rifleman);
        Assert.Contains(plain.Do(Dash, 3, 3).Events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
        Assert.True(TurnSequence.RiskyEnds(plain.State, A(1)));
        Assert.False(TurnSequence.RiskyEnds(again.State, A(1)));
    }

    [Fact]
    [Trait("Rule", "Pontiff's Crucifix: a failed climb roll stops the model but does not end its activation")]
    public void TheCrucifixKeepsTheActivationAfterAFailedClimb()
    {
        var play = SkirmishWith(Towers, "15", "5.8", "30", "30", Blessed);
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("16", "5.8"), ("17", "5.8"), ("18", "5.8"))), 3, 3);
        Assert.Equal([typeof(SideClimbDeclared), typeof(SideClimbRolled), typeof(ModelMoved)], result.Events.Select(e => e.GetType()));
        Assert.Equal(P("16", "5.8"), play.M(A(1)).Position);
        Assert.Equal((A(1), false), (play.State.ActiveModel, play.State.MustEndActivation));
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Move));

        // From the side itself nothing moves, and the activation goes on.
        var side = SkirmishWith(Towers, "16", "5.8", "30", "30", Blessed);
        Assert.Equal([typeof(SideClimbDeclared), typeof(SideClimbRolled)], side.Do(new MoveModel(Seat.One, A(1), Path(("17", "5.8"), ("18", "5.8"))), 3, 3).Events.Select(e => e.GetType()));
        Assert.Equal(A(1), side.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Pontiff's Crucifix: a failed RISKY attack does not end the activation; RELOAD still does")]
    public void TheCrucifixKeepsTheActivationAfterAFailedRiskyAttack()
    {
        var gun = Blessed with { RangedWeaponKeywords = WeaponKeywords.Risky };
        var shot = Skirmish("4", "2", "12", "2", gun);
        var missed = shot.Do(new Shoot(Seat.One, A(1), B(1)), 2, 2);
        Assert.True(missed.Events.OfType<RangedSuccessRolled>().Single().Check.RiskyFailure);
        Assert.DoesNotContain(missed.Events, e => e is ActivationEnded);
        Assert.Equal(A(1), shot.State.ActiveModel);

        var reload = Skirmish("4", "2", "12", "2", gun with { RangedWeaponKeywords = WeaponKeywords.Risky | WeaponKeywords.Reload });
        Assert.Equal(new ActivationEnded(A(1), ActivationEndReason.Reload), reload.Do(new Shoot(Seat.One, A(1), B(1)), 2, 2).Events[^1]);

        var blade = Brawler with { KeepsGoingAfterRisky = true, MeleeWeaponKeywords = WeaponKeywords.Risky };
        var fight = Skirmish("4", "2", "5.1", "2", blade);
        var swung = fight.Do(new FightModel(Seat.One, A(1), B(1)), 1, 1);
        Assert.True(swung.Events.OfType<MeleeSuccessRolled>().Single().Check.RiskyFailure);
        Assert.DoesNotContain(swung.Events, e => e is ActivationEnded);
        Assert.Equal(A(1), fight.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Mountaineer Kit p. 84: +1 DICE to Risky Success Rolls to climb a sheer surface")]
    public void TheMountaineerKitAddsADieToClimbs()
    {
        var play = SkirmishWith(Towers, "15", "5.8", "30", "30", Brawler with { ClimbDice = 1 });
        Assert.Equal(1, TurnSequence.ClimbDice(play.State, A(1)));
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("16", "5.8"), ("17", "5.8"), ("18", "5.8"))), 3, 1, 4);
        var check = result.Events.OfType<SideClimbRolled>().Single().Check;
        Assert.Equal((1, SuccessOutcome.Success), (check.NetDice, check.Outcome));
        Assert.Equal(P("18", "5.8"), play.M(A(1)).Position);
        // Its Dash rolls are not touched.
        var dash = Active(id => id == A(1) ? Rifleman with { ClimbDice = 1 } : Rifleman);
        Assert.Equal(0, dash.Do(Dash, 4, 3).Events.OfType<DashRolled>().Single().Check.NetDice);
        Assert.Throws<ArgumentOutOfRangeException>(() => Brawler with { ClimbDice = -1 });
    }

    [Fact]
    [Trait("Rule", "Musical Instrument p. 85: +1 DICE to Dash rolls of friendly models within 4\" of a model with one")]
    public void AMusicalInstrumentHelpsDashesWithinFourInches()
    {
        var player = Rifleman with { Fanfare = true };
        // A2 with the instrument 3" away (a 2" gap): +1 DICE.
        var near = Active(id => id == A(2) ? player : Rifleman, "2", "6");
        Assert.Equal(1, TurnSequence.DashDice(near.State, A(1)));
        var check = near.Do(Dash, 3, 1, 4).Events.OfType<DashRolled>().Single().Check;
        Assert.Equal((1, SuccessOutcome.Success), (check.NetDice, check.Outcome));

        // Exactly 4" between the bases still counts; beyond it does not.
        Assert.Equal(1, TurnSequence.DashDice(Active(id => id == A(2) ? player : Rifleman, "2", "8").State, A(1)));
        Assert.Equal(0, TurnSequence.DashDice(Active(id => id == A(2) ? player : Rifleman, "2", "8.01").State, A(1)));
        // The player itself, but not an enemy with one.
        Assert.Equal(1, TurnSequence.DashDice(Active(id => id == A(1) ? player : Rifleman).State, A(1)));
        var enemy = new BoardPlay(2, id => id == B(1) ? player : Rifleman).ToActivation();
        enemy.Put(B(1), "3", "6");
        enemy.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(0, TurnSequence.DashDice(enemy.State, A(1)));

        // A forged Dash roll without the die is refused.
        var forged = Active(id => id == A(2) ? player : Rifleman, "2", "6");
        var events = TurnSequence.Decide(forged.State, Dash, new FixedDice([3, 1, 4])).Events;
        var declared = Reducer.Apply(forged.State, events[0]);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new DashRolled(A(1), SuccessRoll.Roll(new FixedDice([4, 3]), 0, 0, risky: true))));
    }

    [Fact]
    [Trait("Rule", "Cloak of the Mountain (Warbands p. 77): Cover gives -2 DICE instead of -1 against Ranged Attacks at the wearer")]
    public void TheCloakDoublesCover()
    {
        Assert.Equal((0, 0, 2, 0), AttackDice.ShotModifiers(WeaponKeywords.None, false, true, false, blendsIn: true));
        Assert.Equal((0, 0, 0, 0), AttackDice.ShotModifiers(WeaponKeywords.None, false, false, false, blendsIn: true));
        Assert.Equal((0, 0, 0, 0), AttackDice.ShotModifiers(WeaponKeywords.IgnoreCover, false, true, false, blendsIn: true));

        // B1 against the south edge of the rubble obj-1 (as in RangedAttackTests): in Cover.
        var cloaked = Rifleman with { BlendsIn = true };
        var play = new BoardPlay(2, id => id == B(1) ? cloaked : Rifleman).ToActivation();
        play.Put(A(1), "4", "6").Put(B(1), "12", "3.6");
        play.Do(new ActivateModel(Seat.One, A(1)));
        var before = play.State;
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 6, 6, 6, 1, 1, 1);
        var declared = result.Events.OfType<RangedAttackDeclared>().Single();
        Assert.True(declared.InCover);
        Assert.Equal(1, declared.PenaltyDice); // the event names Cover; the roll has its -2
        Assert.Equal(2, AttackDice.Shot(Rifleman, false, true, false, cloaked).Penalty);
        Assert.Equal(-2, result.Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
        // A forged Success Roll with Cover's usual one penalty die is refused.
        var aimed = Reducer.Apply(before, declared);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(aimed, new RangedSuccessRolled(A(1), B(1), SuccessRoll.Roll(new FixedDice([6, 6, 1]), 0, 1, risky: false))));
    }

    [Fact]
    [Trait("Rule", "Records: matches with these profiles play to the end and replay; the fields are hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        var plain = StateHash.Canonical(Skirmish("4", "2", "12", "2").State);
        foreach (var field in new[] { "keepsGoing", "climbDice", "fanfare", "blendsIn" })
        {
            Assert.DoesNotContain(field, plain, StringComparison.Ordinal);
        }
        var kitted = StateHash.Canonical(Skirmish("4", "2", "12", "2", Brawler with { KeepsGoingAfterRisky = true, ClimbDice = 1, Fanfare = true, BlendsIn = true }).State);
        foreach (var field in new[] { "keepsGoing=yes", "climbDice=1", "fanfare=yes", "blendsIn=yes" })
        {
            Assert.Contains(field, kitted, StringComparison.Ordinal);
        }
        var (dashes, kept) = (0, 0);
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with
                {
                    Profile = m.Profile with { KeepsGoingAfterRisky = i % 2 == 0, ClimbDice = i % 3 == 0 ? 1 : 0, Fanfare = i == 1, BlendsIn = i % 2 == 1 },
                }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                dashes += run.Log.OfType<DashRolled>().Count(d => d.Check.NetDice > 0);
                kept += run.Log.Select((e, i) => (e, i)).Count(x => x.e is DashRolled { Check.Outcome: SuccessOutcome.Failure }
                    && !(x.i + 1 < run.Log.Count && run.Log[x.i + 1] is ActivationEnded));
            }
        }
        Assert.True(dashes > 0 && kept > 0, $"{dashes} helped dashes, {kept} failures kept going");
    }
}
