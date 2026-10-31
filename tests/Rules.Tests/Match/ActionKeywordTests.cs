using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R2b (U99): the weapon keywords that change what else a model may do in its activation: ASSAULT (p. 52),
// HEAVY (p. 55) and RELOAD (p. 56).
public class ActionKeywordTests
{
    // Movement 6", 1" base, an 18" gun with the given keywords and a Melee Weapon.
    private static ModelProfile Armed(WeaponKeywords gun, WeaponKeywords blade = WeaponKeywords.None) =>
        new(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true) { RangedWeaponKeywords = gun, MeleeWeaponKeywords = blade };

    private static Shoot Fire() => new(Seat.One, A(1), B(1));

    private static ChargeModel Charge() => new(Seat.One, A(1), B(1));

    [Fact]
    [Trait("Rule", "ASSAULT p. 52: a Ranged Attack with it does not prevent a Charge or Fight ACTION, in either order")]
    public void AnAssaultWeaponShootsAndCharges()
    {
        var shotFirst = Skirmish("4", "2", "12", "2", Armed(WeaponKeywords.Assault));
        shotFirst.Do(Fire(), 1, 1);               // a miss
        shotFirst.Do(Charge(), 3);                // gap 7, reach 9
        shotFirst.Do(new FightModel(Seat.One, A(1), B(1)), 1, 1);
        Assert.Equal(ActionsTaken.Shoot | ActionsTaken.Charge | ActionsTaken.Fight, shotFirst.State.Actions);

        var chargedFirst = Skirmish("4", "2", "14.5", "2", Armed(WeaponKeywords.Assault));
        chargedFirst.Do(Charge(), 1);             // short: gap 9.5, reach 7
        chargedFirst.Do(Fire(), 1, 1);
        Assert.Equal(ActionsTaken.Shoot | ActionsTaken.Charge, chargedFirst.State.Actions);

        // A melee weapon's ASSAULT does not count: the rule is about Ranged Attacks.
        var bladeOnly = Skirmish("4", "2", "12", "2", Armed(WeaponKeywords.None, WeaponKeywords.Assault));
        bladeOnly.Do(Fire(), 1, 1);
        Assert.Equal(ActionLimits.ShootAndMeleeText, bladeOnly.Refused(Charge(), ReasonCode.ActionExcluded).Text);
    }

    [Fact]
    [Trait("Rule", "HEAVY p. 55: no Charge Bonus; the charge moves Movement alone and no die is rolled")]
    public void AHeavyWeaponGetsNoChargeBonus()
    {
        var reaches = Skirmish("4", "2", "10", "2", Armed(WeaponKeywords.None, WeaponKeywords.Heavy));
        var result = reaches.Do(Charge()); // gap 5, Movement 6
        Assert.Empty(result.Events.OfType<ChargeRolled>());
        Assert.True(result.Events.OfType<ChargeMoved>().Single().Reached);

        var shortOf = Skirmish("4", "2", "13", "2", Armed(WeaponKeywords.Heavy));
        var stopped = shortOf.Do(Charge()); // gap 8: a die of 2 or more would have reached, Movement alone does not
        Assert.Empty(stopped.Events.OfType<ChargeRolled>());
        Assert.False(stopped.Events.OfType<ChargeMoved>().Single().Reached);
        Assert.True(Armed(WeaponKeywords.Heavy).NoChargeBonus);
        Assert.False(Armed(WeaponKeywords.Assault).NoChargeBonus);
    }

    [Fact]
    [Trait("Rule", "HEAVY p. 55: a HEAVY ranged weapon cannot shoot in an activation with a Move, Charge, Retreat or Dash")]
    public void AHeavyGunDoesNotShootAndMove()
    {
        var shot = Skirmish("4", "2", "12", "2", Armed(WeaponKeywords.Heavy | WeaponKeywords.Assault));
        shot.Do(Fire(), 1, 1);
        Assert.Equal(ActionLimits.HeavyText, shot.Refused(new MoveModel(Seat.One, A(1), Path(("5", "2"))), ReasonCode.ActionExcluded).Text);
        Assert.Equal(ActionLimits.HeavyText, shot.Refused(new DashModel(Seat.One, A(1), Path(("5", "2"))), ReasonCode.ActionExcluded).Text);
        Assert.Equal(ActionLimits.HeavyText, shot.Refused(Charge(), ReasonCode.ActionExcluded).Text); // even with ASSAULT

        var moved = Skirmish("4", "2", "12", "2", Armed(WeaponKeywords.Heavy));
        moved.Do(new MoveModel(Seat.One, A(1), Path(("5", "2"))));
        Assert.Equal(ActionLimits.HeavyText, moved.Refused(Fire(), ReasonCode.ActionExcluded).Text);

        var dashed = Skirmish("4", "2", "12", "2", Armed(WeaponKeywords.Heavy));
        dashed.Do(new DashModel(Seat.One, A(1), Path(("5", "2"))), 6, 5); // the Dash's Risky roll succeeds
        dashed.Refused(Fire(), ReasonCode.ActionExcluded);

        // A HEAVY melee weapon does not stop the gun.
        var blade = Skirmish("4", "2", "12", "2", Armed(WeaponKeywords.None, WeaponKeywords.Heavy));
        blade.Do(new MoveModel(Seat.One, A(1), Path(("5", "2"))));
        blade.Do(Fire(), 1, 1);
    }

    [Fact]
    [Trait("Rule", "HEAVY p. 55: the limits, including Retreat, in one place")]
    public void TheActionLimitsSayWhatIsExcluded()
    {
        var heavy = Armed(WeaponKeywords.Heavy);
        Assert.Equal(ActionLimits.HeavyText, ActionLimits.MoveExcluded(ActionsTaken.Shoot, heavy));
        Assert.Null(ActionLimits.MoveExcluded(ActionsTaken.Shoot, Gunman));
        Assert.Null(ActionLimits.MoveExcluded(ActionsTaken.Move, heavy));
        foreach (var move in new[] { ActionsTaken.Move, ActionsTaken.Charge, ActionsTaken.Retreat, ActionsTaken.Dash })
        {
            Assert.NotNull(ActionLimits.ShootExcluded(move, heavy));
        }
        Assert.Null(ActionLimits.ShootExcluded(ActionsTaken.Move | ActionsTaken.Dash, Gunman));
        Assert.Equal(ActionLimits.ShootAndMeleeText, ActionLimits.ShootExcluded(ActionsTaken.Fight, Gunman));
        Assert.Null(ActionLimits.ShootExcluded(ActionsTaken.Fight, Armed(WeaponKeywords.Assault)));
        Assert.Null(ActionLimits.MeleeExcluded(ActionsTaken.Shoot, Armed(WeaponKeywords.Assault | WeaponKeywords.Heavy), ActionsTaken.Fight));
        Assert.Equal(ActionLimits.HeavyText, ActionLimits.MeleeExcluded(ActionsTaken.Shoot, Armed(WeaponKeywords.Assault | WeaponKeywords.Heavy), ActionsTaken.Charge));
        Assert.Null(ActionLimits.MeleeExcluded(ActionsTaken.Move, Gunman, ActionsTaken.Charge));
    }

    [Fact]
    [Trait("Rule", "RELOAD p. 56: after an attack with it the activation ends once that ACTION is done, hit or miss")]
    public void AReloadWeaponEndsTheActivation()
    {
        var missed = Skirmish("4", "2", "12", "2", Armed(WeaponKeywords.Reload));
        var miss = missed.Do(Fire(), 1, 1);
        Assert.Equal(new ActivationEnded(A(1), ActivationEndReason.Reload), miss.Events[^1]);
        Assert.Null(missed.State.ActiveModel);
        Assert.False(missed.State.ReloadEnds);

        var hit = Skirmish("4", "2", "12", "2", Armed(WeaponKeywords.Reload));
        var events = hit.Do(Fire(), 4, 4, 1, 1).Events;
        Assert.IsType<InjuryRolled>(events[^2]);
        Assert.Equal(new ActivationEnded(A(1), ActivationEndReason.Reload), events[^1]);

        var fought = Skirmish("4", "2", "5.1", "2", Armed(WeaponKeywords.None, WeaponKeywords.Reload));
        var fight = fought.Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 1, 1);
        Assert.Equal(ActivationEndReason.Reload, fight.Events.OfType<ActivationEnded>().Single().Reason);

        // The gun's RELOAD does not end a melee attack's activation, and a plain gun's shot does not end one.
        var other = Skirmish("4", "2", "5.1", "2", Armed(WeaponKeywords.Reload));
        Assert.Empty(other.Do(new FightModel(Seat.One, A(1), B(1)), 1, 1).Events.OfType<ActivationEnded>());
        var plain = Skirmish("4", "2", "12", "2", Gunman);
        Assert.Empty(plain.Do(Fire(), 1, 1).Events.OfType<ActivationEnded>());
    }

    [Fact]
    [Trait("Rule", "RELOAD p. 56 with RISKY p. 55: a failed Risky roll ends the activation for being Risky")]
    public void ARiskyReloadMissEndsForRisky()
    {
        var play = Skirmish("4", "2", "12", "2", Armed(WeaponKeywords.Reload | WeaponKeywords.Risky));
        var ended = play.Do(Fire(), 1, 1).Events.OfType<ActivationEnded>().Single();
        Assert.Equal(ActivationEndReason.RiskyFailure, ended.Reason);
    }

    [Fact]
    [Trait("Rule", "RELOAD p. 56: a free attack is made in the enemy's activation, which goes on")]
    public void AFreeAttackWithAReloadWeaponEndsNothing()
    {
        var play = Skirmish("4", "2", "5.4", "2", Armed(WeaponKeywords.Reload, WeaponKeywords.Reload));
        play.Put(B(2), "30", "30");
        play.Do(new RetreatModel(Seat.One, A(1), Path(("1.5", "2"))));
        var result = play.Do(new FreeFight(Seat.Two, B(1)), 1, 1);
        Assert.Empty(result.Events.OfType<ActivationEnded>());
        Assert.False(play.State.ReloadEnds);
    }

    [Fact]
    [Trait("Rule", "Records: the RELOAD end comes next and only after a RELOAD attack; it is in the state hash")]
    public void ForgedReloadEndsAreRefused()
    {
        var play = Skirmish("4", "2", "12", "2", Armed(WeaponKeywords.Reload));
        var before = play.State;
        var events = play.Do(Fire(), 1, 1).Events;
        var afterMiss = events.Take(events.Length - 1).Aggregate(before, Reducer.Apply);
        Assert.True(afterMiss.ReloadEnds);
        Assert.NotEqual(StateHash.Compute(afterMiss), StateHash.Compute(afterMiss with { ReloadEnds = false }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(afterMiss, new ActivationEnded(A(1), ActivationEndReason.Voluntary)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(afterMiss, new ModelMoved(A(1), MoveKind.Move, Path(("5", "2")), L("1"))));

        var plain = Skirmish("4", "2", "12", "2", Gunman);
        plain.Do(Fire(), 1, 1);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(plain.State, new ActivationEnded(A(1), ActivationEndReason.Reload)));
    }

    [Fact]
    [Trait("Rule", "Records: matches with ASSAULT, HEAVY and RELOAD weapons play to the end and replay to the same state")]
    public void SimulatedMatchesWithActionKeywordsReplay()
    {
        var keywords = new[] { WeaponKeywords.Assault, WeaponKeywords.Heavy | WeaponKeywords.Reload, WeaponKeywords.Assault | WeaponKeywords.Heavy };
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var armed = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with
                {
                    Profile = new ModelProfile(m.Profile.Movement, m.Profile.BaseRadius, m.Profile.RangedRange, m.Profile.RangedInjuryModifier,
                        m.Profile.Armour, m.Profile.HasMeleeWeapon, m.Profile.MeleeInjuryModifier)
                    {
                        RangedAlsoMelee = m.Profile.RangedAlsoMelee,
                        RangedWeaponKeywords = m.Profile.HasRangedWeapon ? keywords[i % 3] : WeaponKeywords.None,
                        MeleeWeaponKeywords = m.Profile.HasMeleeWeapon ? keywords[(i + 1) % 3] : WeaponKeywords.None,
                    },
                }).ToList(),
            }).ToList();
            var reloads = 0;
            for (var seed = 1; seed <= 5; seed++)
            {
                var run = Sim.Runner.Run(seed, armed);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                reloads += run.Log.OfType<ActivationEnded>().Count(e => e.Reason == ActivationEndReason.Reload);
            }
            Assert.True(reloads > 0, path);
        }
    }
}
