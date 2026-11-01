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

// Roster stage R5b (U106): the model keywords FEAR (p. 54), NEGATE FEAR and NEGATE HEAVY (p. 56; STRONG has it, p. 57),
// TOUGH (p. 57) and LEADER (p. 55).
public class ModelTraitTests
{
    private static ModelProfile With(ModelProfile profile, ModelTraits traits) => profile with { Traits = traits };

    // A1 at (4, 2) and activated, B1 at (5.1, 2) in reach; A1 and B1 take the profiles given, the others are Brawlers.
    private static BoardPlay Duel(ModelProfile a, ModelProfile b)
    {
        var play = new BoardPlay(2, id => id == A(1) ? a : id == B(1) ? b : Brawler).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "5.1", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static FightModel Fight => new(Seat.One, A(1), B(1));

    private static void Replace(BoardPlay play, ModelState model) =>
        play.State = play.State with { Models = [.. play.State.Models.Select(m => m.Id == model.Id ? model : m)] };

    [Fact]
    [Trait("Rule", "FEAR p. 54: -1 DICE to a Melee Attack that targets a FEAR model; FEAR and NEGATE FEAR models are immune")]
    public void FearHindersMeleeAttacksOnTheModel()
    {
        var fearsome = With(Brawler, ModelTraits.Fear);
        var hit = Duel(Brawler, fearsome).Do(Fight, 1, 1, 1).Events.OfType<MeleeAttackDeclared>().Single(); // 1 penalty die
        Assert.Equal((true, 1), (hit.Fear, hit.PenaltyDice));
        Assert.True(TurnSequence.Fears(Duel(Brawler, fearsome).State, A(1), B(1)));

        foreach (var immune in new[] { fearsome, With(Brawler, ModelTraits.NegateFear), With(Brawler, ModelTraits.Fear | ModelTraits.NegateFear) })
        {
            var calm = Duel(immune, fearsome).Do(Fight, 1, 1).Events.OfType<MeleeAttackDeclared>().Single();
            Assert.Equal((false, 0), (calm.Fear, calm.PenaltyDice));
        }
        // A FEAR attacker on a plain target: no penalty. Ranged Attacks are not touched.
        Assert.False(Duel(fearsome, Brawler).Do(Fight, 1, 1).Events.OfType<MeleeAttackDeclared>().Single().Fear);
        Assert.Equal(0, AttackDice.OwnDice(fearsome, AttackKind.Ranged));
    }

    [Fact]
    [Trait("Rule", "Records: a forged FEAR in a Melee Attack is refused")]
    public void ForgedFearIsRefused()
    {
        var fearsome = With(Brawler, ModelTraits.Fear);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(Duel(Brawler, Brawler).State, new MeleeAttackDeclared(A(1), B(1), false, null) { Fear = true }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(Duel(Brawler, fearsome).State, new MeleeAttackDeclared(A(1), B(1), false, null)));
        Reducer.Apply(Duel(Brawler, fearsome).State, new MeleeAttackDeclared(A(1), B(1), false, null) { Fear = true });
    }

    [Fact]
    [Trait("Rule", "TOUGH p. 57: the first Out of Action result on the Injury Table is a Down result instead; the next one is not")]
    public void ToughTurnsTheFirstOutOfActionIntoDown()
    {
        var tough = With(Brawler, ModelTraits.Tough);
        var play = Duel(Brawler, tough);
        var result = play.Do(Fight, 4, 4, 5, 5);
        var rolled = result.Events.OfType<InjuryRolled>().Single();
        Assert.Equal((InjuryOutcome.OutOfAction, true, InjuryOutcome.Down, 1), (rolled.Injury.Outcome, rolled.Tough, rolled.Result, rolled.ResultMarkers));
        Assert.Equal((ModelStatus.Down, true, 1), (play.M(B(1)).Status, play.M(B(1)).ToughSpent, play.M(B(1)).BloodMarkers));
        Assert.NotNull(play.M(B(1)).Position);
        Assert.Contains("tough=spent", StateHash.Canonical(play.State), StringComparison.Ordinal);

        // Spent: the next Out of Action result stands (a Melee Attack on a Down model has +1 INJURY DICE).
        var again = Duel(Brawler, tough);
        Replace(again, again.M(B(1)) with { Status = ModelStatus.Down, ToughSpent = true });
        var second = again.Do(Fight, 4, 4, 5, 5, 1).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((InjuryOutcome.OutOfAction, false), (second.Result, second.Tough));
        Assert.Equal(ModelStatus.OutOfAction, again.M(B(1)).Status);

        // Not TOUGH: Out of Action. A Down result on a TOUGH model leaves it unspent.
        var plain = Duel(Brawler, Brawler);
        Assert.False(plain.Do(Fight, 4, 4, 5, 5).Events.OfType<InjuryRolled>().Single().Tough);
        Assert.Equal(ModelStatus.OutOfAction, plain.M(B(1)).Status);
        var downed = Duel(Brawler, tough);
        Assert.False(downed.Do(Fight, 4, 4, 4, 4).Events.OfType<InjuryRolled>().Single().Tough);
        Assert.Equal((ModelStatus.Down, false), (downed.M(B(1)).Status, downed.M(B(1)).ToughSpent));
    }

    [Fact]
    [Trait("Rule", "Records: TOUGH must be claimed exactly when it applies; the event cannot claim it on another result")]
    public void ForgedToughIsRefused()
    {
        var tough = With(Brawler, ModelTraits.Tough);
        foreach (var (target, claim) in new[] { (tough, false), (Brawler, true) })
        {
            var play = Duel(Brawler, target);
            var events = TurnSequence.Decide(play.State, Fight, new FixedDice([4, 4, 5, 5])).Events;
            var state = play.State;
            foreach (var e in events.TakeWhile(e => e is not InjuryRolled))
            {
                state = Reducer.Apply(state, e);
            }
            var rolled = events.OfType<InjuryRolled>().Single();
            Assert.Equal(!claim, rolled.Tough);
            Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, rolled with { Tough = claim }));
        }
        var minor = InjuryRoll.Roll(new FixedDice([1, 2]));
        Assert.Throws<ArgumentException>(() => new InjuryRolled(A(1), B(1), minor) { Tough = true });
    }

    [Fact]
    [Trait("Rule", "NEGATE HEAVY p. 56 (STRONG p. 57): HEAVY limits neither the charge bonus nor the activation")]
    public void NegateHeavyLiftsHeavysLimits()
    {
        var heavy = new ModelProfile(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true) { RangedWeaponKeywords = WeaponKeywords.Heavy };
        Assert.True(heavy.NoChargeBonus);
        Assert.False(With(heavy, ModelTraits.NegateHeavy).NoChargeBonus);

        var limited = Duel(heavy, Brawler);
        limited.Put(B(1), "12", "2");
        limited.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1);
        Assert.NotNull(ActionLimits.MoveExcluded(limited.State, A(1)));
        var strong = Duel(With(heavy, ModelTraits.NegateHeavy), Brawler);
        strong.Put(B(1), "12", "2");
        strong.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1);
        Assert.Null(ActionLimits.MoveExcluded(strong.State, A(1)));
        strong.Do(new MoveModel(Seat.One, A(1), Path(("4", "4"))));
    }

    [Fact]
    [Trait("Rule", "LEADER p. 55: +1 DICE to Morale Checks while a LEADER model is on the battlefield, neither Down nor Out of Action")]
    public void LeaderAddsADieToMoraleChecks()
    {
        var play = new BoardPlay(2, id => id == A(1) ? With(Brawler, ModelTraits.Leader) : Brawler).ToActivation();
        Assert.Equal(1, TurnRules.LeaderDice(play.State, Seat.One));
        Assert.Equal(0, TurnRules.LeaderDice(play.State, Seat.Two));
        Assert.Equal(0, TurnRules.LeaderDice(play.SetStatus(A(1), ModelStatus.Down).State, Seat.One));
        Assert.Equal(0, TurnRules.LeaderDice(play.SetStatus(A(1), ModelStatus.OutOfAction).State, Seat.One));
    }

    // Warbands whose models have the traits in turn (every third model a LEADER, TOUGH and FEAR spread around).
    private static List<Sim.Warband> Traited(string path) => Sim.Content.Load(path).Select(w => w with
    {
        Models = w.Models.Select((m, i) => m with
        {
            Profile = m.Profile with
            {
                Traits = (i % 3 == 0 ? ModelTraits.Leader : ModelTraits.None) | (i % 2 == 0 ? ModelTraits.Tough : ModelTraits.None)
                    | (i % 3 == 1 ? ModelTraits.Fear : ModelTraits.None) | (i % 4 == 2 ? ModelTraits.NegateFear : ModelTraits.None),
            },
        }).ToList(),
    }).ToList();

    [Fact]
    [Trait("Rule", "Records: matches with FEAR, TOUGH and LEADER play to the end and replay; a forged Morale Check die is refused")]
    public void SimulatedMatchesWithTraitsReplay()
    {
        var (fears, toughs, leaders) = (0, 0, 0);
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Traited(path);
            for (var seed = 1; seed <= 10; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                fears += run.Log.OfType<MeleeAttackDeclared>().Count(e => e.Fear);
                toughs += run.Log.OfType<InjuryRolled>().Count(e => e.Tough) + run.Log.OfType<MineExploded>().Count(e => e.Tough);
                var state = MatchState.None;
                foreach (var e in run.Log)
                {
                    if (e is MoraleCheckRolled m)
                    {
                        Assert.Equal(TurnRules.LeaderDice(state, m.Seat), m.Check.NetDice);
                        leaders += m.Check.NetDice;
                        var other = SuccessRoll.Roll(new FixedDice([3, 3, 3]), 1 - m.Check.NetDice);
                        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new MoraleCheckRolled(m.Seat, other, m.ModelsDownOrOut, m.WarbandSize, m.WasShaken)));
                    }
                    state = Reducer.Apply(state, e);
                }
            }
        }
        Assert.True(fears > 0 && toughs > 0 && leaders > 0, $"FEAR {fears}, TOUGH {toughs}, LEADER {leaders}");
    }

    [Fact]
    [Trait("Rule", "Profiles: traits are checked, hashed only when set, and kept by a profile's second-weapon copy")]
    public void TraitsAreCheckedAndHashed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Brawler with { Traits = (ModelTraits)64 });
        var play = Duel(Brawler, Brawler);
        Assert.DoesNotContain("traits=", StateHash.Canonical(play.State), StringComparison.Ordinal);
        Assert.DoesNotContain("tough=", StateHash.Canonical(play.State), StringComparison.Ordinal);
        var traited = Duel(With(Brawler, ModelTraits.Leader), Brawler);
        Assert.Contains("traits=", StateHash.Canonical(traited.State), StringComparison.Ordinal);
        var two = With(Brawler, ModelTraits.Fear) with { SecondMeleeWeapon = new Weapon(Length.Zero) };
        Assert.Equal(ModelTraits.Fear, two.Using(AttackKind.Melee, 1).Traits);
    }
}
