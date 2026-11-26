using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R5a (U105): a BLAST weapon aimed at a point (p. 53). The point must be on the battlefield, in the
// attacker's Line of Sight and within the weapon's range; beyond half the range is Long Range. On a hit the blast there
// hits as BLAST says, with no Critical Success extra dice (no model was the target); a SCATTER miss scatters from the
// point. DEADLY (U104): the Injury Roll keeps three dice.
public class PointBlastTests
{
    private static ModelProfile Mortar(WeaponKeywords keywords = WeaponKeywords.None) =>
        new(L("6"), L("0.5"), L("18")) { RangedWeaponBlast = 3, RangedWeaponKeywords = keywords };

    private static BoardPlay Play(ModelProfile? attacker = null, Scenario? scenario = null) =>
        new BoardPlay(3, id => id.Value.StartsWith('A') ? attacker ?? Mortar() : Rifleman, scenario: scenario).ToActivation();

    // The state once the events before the first Injury Roll are applied.
    private static MatchState BeforeInjury(MatchState state, IEnumerable<GameEvent> events)
    {
        foreach (var e in events.TakeWhile(e => e is not InjuryRolled))
        {
            state = Reducer.Apply(state, e);
        }
        return state;
    }

    private static BoardPlay Active(BoardPlay play)
    {
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "BLAST p. 53: an attack aimed at a point hits the models around the point; no Critical Success dice")]
    public void AnAttackAtAPointHitsAroundIt()
    {
        var play = Active(Play().Put(A(1), "4", "2").Put(B(1), "12", "2").Put(B(2), "17", "2"));
        // (13, 2): 9" from A1's centre, within half the 18" range plus the base; B1's base edge is 0.5" away, B2's 3.5".
        Assert.Equal((null, false), TurnSequence.CheckPoint(play.State, A(1), P("13", "2")));
        var result = play.Do(new ShootPoint(Seat.One, A(1), P("13", "2")), 6, 6, 1, 1);
        Assert.Equal(new PointAttackDeclared(A(1), P("13", "2"), false), result.Events.OfType<PointAttackDeclared>().Single());
        Assert.Equal(SuccessOutcome.CriticalSuccess, result.Events.OfType<PointSuccessRolled>().Single().Check.Outcome);
        Assert.Equal([B(1)], result.Events.OfType<PointBlastHit>().Single().Hit.ToArray());
        var injury = result.Events.OfType<InjuryRolled>().Single();
        Assert.Equal((B(1), 0), (injury.Target, injury.Injury.NetDice));
        Assert.Null(play.State.InProgress);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Shoot));
        Assert.Equal(A(1), play.State.ActiveModel);
        play.Refused(new ShootPoint(Seat.One, A(1), P("13", "2")), ReasonCode.ActionAlreadyTaken);
        play.Refused(new Shoot(Seat.One, A(1), B(1)), ReasonCode.ActionAlreadyTaken);

        // A point with nobody near it: the attack is over at once. A miss without SCATTER is a miss.
        var empty = Active(Play().Put(A(1), "4", "2").Put(B(1), "12", "2"));
        var none = empty.Do(new ShootPoint(Seat.One, A(1), P("8", "7")), 4, 4);
        Assert.Empty(none.Events.OfType<PointBlastHit>().Single().Hit);
        Assert.Empty(none.Events.OfType<InjuryRolled>());
        Assert.Null(empty.State.InProgress);
        var miss = Active(Play().Put(A(1), "4", "2").Put(B(1), "12", "2"));
        var missed = miss.Do(new ShootPoint(Seat.One, A(1), P("13", "2")), 1, 1);
        Assert.Empty(missed.Events.OfType<PointBlastHit>());
        Assert.Null(miss.State.InProgress);
    }

    [Fact]
    [Trait("Rule", "BLAST p. 53 with p. 43: beyond half the range is Long Range, -1 DICE; there is no cover at a point")]
    public void BeyondHalfTheRangeIsLongRange()
    {
        var play = Active(Play().Put(A(1), "4", "2").Put(B(1), "16", "2"));
        Assert.True(TurnSequence.CheckPoint(play.State, A(1), P("14", "2")).LongRange);
        var result = play.Do(new ShootPoint(Seat.One, A(1), P("14", "2")), 6, 6, 1, 1, 1);
        Assert.True(result.Events.OfType<PointAttackDeclared>().Single().LongRange);
        Assert.Equal(-1, result.Events.OfType<PointSuccessRolled>().Single().Check.NetDice);
    }

    [Fact]
    [Trait("Rule", "BLAST p. 53: only a BLAST weapon, a point on the battlefield, in sight and in range")]
    public void OnlyAPointInSightAndRangeMayBeAimedAt()
    {
        var wall = new TerrainBlock("wall", F("8", "0.5", "8.4", "3.5"), TerrainProfile.Wall, TerrainKind.Impassable);
        var play = Active(Play(scenario: MeleePlay.WithTerrain(wall)).Put(A(1), "4", "2").Put(B(1), "12", "6"));
        play.Refused(new ShootPoint(Seat.One, A(1), P("12", "2")), ReasonCode.NoLineOfSight);
        play.Refused(new ShootPoint(Seat.One, A(1), P("4", "-1")), ReasonCode.NotACandidate);
        play.Refused(new ShootPoint(Seat.One, A(1), P("4", "20.6")), ReasonCode.TargetOutOfRange);
        play.Refused(new ShootPoint(Seat.One, A(1), P("4", "8")) { Weapon = 1 }, ReasonCode.NoRangedWeapon);
        play.Refused(new ShootPoint(Seat.Two, A(1), P("4", "8")), ReasonCode.NotYourModel);
        Assert.Null(TurnSequence.Check(play.State, new ShootPoint(Seat.One, A(1), P("4", "20.4"))));

        var rifle = Active(Play(Rifleman).Put(A(1), "4", "2").Put(B(1), "12", "6"));
        rifle.Refused(new ShootPoint(Seat.One, A(1), P("4", "8")), ReasonCode.NotACandidate);

        var engaged = Active(Play().Put(A(1), "4", "2").Put(B(1), "5.5", "2"));
        engaged.Refused(new ShootPoint(Seat.One, A(1), P("4", "8")), ReasonCode.AttackerEngaged);
    }

    [Fact]
    [Trait("Rule", "SCATTER p. 57 with BLAST p. 53: a miss aimed at a point scatters from the point")]
    public void AMissAtAPointScattersFromThePoint()
    {
        var play = Active(Play(Mortar(WeaponKeywords.Scatter)).Put(A(1), "4", "2").Put(B(1), "12", "2"));
        var result = play.Do(new ShootPoint(Seat.One, A(1), P("13", "2")), 2, 2); // 4: scatters 3"
        Assert.Empty(result.Events.OfType<PointBlastHit>());
        Assert.Equal(new PendingDecision(DecisionKind.ChooseScatterPoint, Seat.Two), play.State.Pending);
        Assert.Equal(P("13", "2"), TurnSequence.ScatterOrigin(play.State));
        Assert.True(TurnSequence.CanScatterTo(play.State, P("16", "2")));
        Assert.False(TurnSequence.CanScatterTo(play.State, P("15", "2")));
        Assert.Contains(new ScatterAttack(Seat.Two, P("10", "2")), TurnSequence.LegalCommands(play.State));

        // (10, 2): B1's base edge is 1.5" away; A1's 5.5".
        var landed = play.Do(new ScatterAttack(Seat.Two, P("10", "2")), 1, 1);
        var scattered = landed.Events.OfType<AttackScattered>().Single();
        Assert.Equal((A(1), (ModelId?)null, P("10", "2"), 3), (scattered.Attacker, scattered.Target, scattered.At, scattered.Inches));
        Assert.Equal([B(1)], scattered.Hit.ToArray());
        Assert.Equal(0, landed.Events.OfType<InjuryRolled>().Single().Injury.NetDice);
        Assert.Null(play.State.InProgress);
        Assert.Null(play.State.Pending);
    }

    [Fact]
    [Trait("Rule", "U105: a log with the wrong Long Range, point or hits is refused")]
    public void AForgedPointAttackIsRefused()
    {
        var play = Active(Play().Put(A(1), "4", "2").Put(B(1), "12", "2").Put(B(2), "17", "2"));
        var start = play.State;
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(start, new PointAttackDeclared(A(1), P("13", "2"), true)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(start, new PointAttackDeclared(A(1), P("4", "21"), false)));
        var declared = Reducer.Apply(start, new PointAttackDeclared(A(1), P("13", "2"), false));
        var roll = SuccessRoll.Roll(new FixedDice(4, 4), 0, 0, risky: false);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new PointSuccessRolled(A(1), P("13", "3"), roll)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new RangedSuccessRolled(A(1), B(1), roll)));
        var hit = Reducer.Apply(declared, new PointSuccessRolled(A(1), P("13", "2"), roll));
        Assert.Equal(RollStage.Blast, hit.InProgress!.Stage);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(hit, new PointBlastHit(A(1), P("13", "2"), [B(1), B(2)])));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(hit, new PointBlastHit(A(1), P("13", "2"), [])));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(hit, new BlastHit(A(1), B(1), [B(1)])));
        Assert.Equal(RollStage.InjuryRoll, Reducer.Apply(hit, new PointBlastHit(A(1), P("13", "2"), [B(1)])).InProgress!.Stage);
        Assert.Throws<ArgumentException>(() => new PointBlastHit(A(1), P("13", "2"), [B(1), B(1)]));
    }

    [Fact]
    [Trait("Rule", "DEADLY: the Injury Roll is 3D6, INJURY DICE adding or taking away dice as usual")]
    public void ADeadlyWeaponRollsThreeInjuryDice()
    {
        var deadly = new ModelProfile(L("6"), L("0.5"), L("18")) { RangedWeaponKeywords = WeaponKeywords.Deadly };
        var play = Active(Play(deadly).Put(A(1), "4", "2").Put(B(1), "12", "2"));
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 4, 1, 2, 3);
        var injury = result.Events.OfType<InjuryRolled>().Single().Injury;
        Assert.True(injury.Deadly);
        Assert.Equal(3, injury.Dice.Kept.Count);
        Assert.Equal(6, injury.Dice.Total);

        // A critical hit adds a die: four rolled, the three highest kept.
        var critical = Active(Play(deadly).Put(A(1), "4", "2").Put(B(1), "12", "2"));
        var crit = critical.Do(new Shoot(Seat.One, A(1), B(1)), 6, 6, 1, 2, 3, 4).Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal((4, 3, 9), (crit.Dice.Rolled.Count, crit.Dice.Kept.Count, crit.Dice.Total));

        // The same roll with two dice kept is refused for a DEADLY weapon, and three for any other.
        var plain = Active(Play(Rifleman).Put(A(1), "4", "2").Put(B(1), "12", "2"));
        var plainStart = plain.State;
        var plainShot = plain.Do(new Shoot(Seat.One, A(1), B(1)), 4, 4, 1, 2).Events;
        var twoDice = plainShot.OfType<InjuryRolled>().Single();
        Assert.False(twoDice.Injury.Deadly);
        var deadlyStart = Active(Play(deadly).Put(A(1), "4", "2").Put(B(1), "12", "2")).State;
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(BeforeInjury(deadlyStart, result.Events), twoDice));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(BeforeInjury(plainStart, plainShot), result.Events.OfType<InjuryRolled>().Single()));
    }
}
