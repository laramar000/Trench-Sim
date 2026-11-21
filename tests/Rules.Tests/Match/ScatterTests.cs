using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R4c (U103): SCATTER (p. 57). A BLAST attack whose Success Roll fails scatters (7 - the roll)" from the
// target's base centre instead of missing: the attacker's opponent picks the point, on the battlefield and in Line of
// Sight of the target; the blast there hits as BLAST says, with no Critical Success. With nowhere to land, it misses.
public class ScatterTests
{
    private static ModelProfile Mortar(WeaponKeywords extra = WeaponKeywords.None) =>
        new(L("6"), L("0.5"), L("18")) { RangedWeaponBlast = 3, RangedWeaponKeywords = WeaponKeywords.Scatter | extra };

    private static BoardPlay Play(ModelProfile? mortar = null, Scenario? scenario = null)
    {
        var play = new BoardPlay(3, id => id.Value.StartsWith('A') ? mortar ?? Mortar() : Rifleman, scenario: scenario).ToActivation();
        return play;
    }

    private static BoardPlay Missed(BoardPlay play, params int[] roll)
    {
        play.Do(new ActivateModel(Seat.One, A(1)));
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), roll);
        Assert.Empty(result.Events.OfType<InjuryRolled>());
        return play;
    }

    [Fact]
    [Trait("Rule", "SCATTER p. 57: a failed roll scatters 7 - the roll; the opponent picks the point; the blast there hits")]
    public void AMissScattersWhereTheOpponentPutsIt()
    {
        var play = Play();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(B(2), "17", "2").Put(A(2), "18.9", "2");
        Missed(play, 2, 2); // 4: scatters 3"
        Assert.Equal(new PendingDecision(DecisionKind.ChooseScatterPoint, Seat.Two), play.State.Pending);
        Assert.Equal((RollStage.Scatter, 3), (play.State.InProgress!.Stage, play.State.InProgress.ScatterInches));

        // The compass points it may land at are offered: not those off the table's south edge, nor (12, 5) in the
        // Objective's rubble (obj-1), which hides a point on the ground. Any other possible point is accepted too.
        var offered = TurnSequence.LegalCommands(play.State).OfType<ScatterAttack>().Select(c => c.At).ToArray();
        Assert.Equal([P("15", "2"), P("14.121", "4.121"), P("9.879", "4.121"), P("9", "2")], offered);
        Assert.True(TurnSequence.CanScatterTo(play.State, P("14.121", "4.121")));

        // Point (15, 2): B1's base edge is 2.5" away, B2's 1.5"; A2 (3.4" away) is within 1" of B2. Setup order.
        var result = play.Do(new ScatterAttack(Seat.Two, P("15", "2")), 1, 1, 1, 1, 1, 1);
        var scattered = result.Events.OfType<AttackScattered>().Single();
        Assert.Equal((A(1), B(1), P("15", "2"), 3), (scattered.Attacker, scattered.Target, scattered.At, scattered.Inches));
        Assert.Equal([A(2), B(1), B(2)], scattered.Hit.ToArray());
        Assert.Equal([A(2), B(1), B(2)], result.Events.OfType<InjuryRolled>().Select(i => i.Target).ToArray());
        Assert.All(result.Events.OfType<InjuryRolled>(), i => Assert.Equal(0, i.Injury.NetDice));
        Assert.Null(play.State.InProgress);
        Assert.Null(play.State.Pending);
        Assert.Equal(A(1), play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "SCATTER p. 57: exactly that far, on the battlefield, in Line of Sight of the target; only the opponent picks")]
    public void OnlyAPossiblePointIsAccepted()
    {
        var wall = new TerrainBlock("wall", F("13", "0.5", "13.4", "3.5"), TerrainProfile.Wall, TerrainKind.Impassable);
        var play = Play(scenario: MeleePlay.WithTerrain(wall));
        play.Put(A(1), "4", "2").Put(B(1), "12", "2");
        Missed(play, 2, 2);
        Assert.Equal(ReasonCode.NotACandidate, play.Refused(new ScatterAttack(Seat.Two, P("12", "4")), ReasonCode.NotACandidate).Code); // 2"
        play.Refused(new ScatterAttack(Seat.Two, P("15", "2")), ReasonCode.NotACandidate);  // behind the wall
        play.Refused(new ScatterAttack(Seat.Two, P("12", "-1")), ReasonCode.NotACandidate); // off the battlefield
        play.Refused(new ScatterAttack(Seat.One, P("9", "2")), ReasonCode.NotYourTurn);
        Assert.DoesNotContain(TurnSequence.LegalCommands(play.State).OfType<ScatterAttack>(), c => c.At == P("15", "2"));
        // Nobody within 3" of (9, 2) but B1, whose base edge is 2.5" away.
        var result = play.Do(new ScatterAttack(Seat.Two, P("9", "2")), 1, 1);
        Assert.Equal([B(1)], result.Events.OfType<AttackScattered>().Single().Hit.ToArray());
    }

    [Fact]
    [Trait("Rule", "SCATTER p. 57: with nowhere to land the attack misses; landing on nobody hits nobody")]
    public void NowhereToLandIsAMiss()
    {
        // A1 and B1 shut in a box of walls: no point 5" from B1 is in its sight.
        TerrainBlock Wall(string id, string x1, string y1, string x2, string y2) => new(id, F(x1, y1, x2, y2), TerrainProfile.Wall, TerrainKind.Impassable);
        var box = MeleePlay.WithTerrain(Wall("w1", "9", "0.8", "13", "1"), Wall("w2", "9", "3", "13", "3.2"), Wall("w3", "8.8", "0.8", "9", "3.2"), Wall("w4", "13", "0.8", "13.2", "3.2"));
        var play = Play(scenario: box);
        play.Put(A(1), "9.8", "2").Put(B(1), "12", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1); // 2: scatters 5"
        Assert.Equal(5, result.Events.OfType<ScatterMissed>().Single().Inches);
        Assert.Null(play.State.InProgress);
        Assert.Null(play.State.Pending);

        // In the open, a point with nobody near it: the attack is over at once.
        var open = Play();
        open.Put(A(1), "4", "2").Put(B(1), "12", "2");
        Missed(open, 1, 2); // 3: scatters 4"
        var none = open.Do(new ScatterAttack(Seat.Two, P("8", "2")));
        Assert.Empty(none.Events.OfType<AttackScattered>().Single().Hit);
        Assert.Null(open.State.InProgress);
        Assert.Equal(A(1), open.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "SCATTER p. 57 with RISKY p. 56 and RELOAD p. 56: the attack scatters first, then the activation ends")]
    public void AFailedRiskyRollScattersThenEndsTheActivation()
    {
        var play = Play(Mortar(WeaponKeywords.Risky));
        play.Put(A(1), "4", "2").Put(B(1), "12", "2");
        Missed(play, 2, 3); // 5: scatters 2"
        Assert.False(play.State.MustEndActivation);
        Assert.True(play.State.InProgress!.RiskyEnds);
        var result = play.Do(new ScatterAttack(Seat.Two, P("14", "2")), 1, 1);
        Assert.Equal([B(1)], result.Events.OfType<AttackScattered>().Single().Hit.ToArray());
        Assert.Equal(ActivationEndReason.RiskyFailure, Assert.IsType<ActivationEnded>(result.Events[^1]).Reason);
        Assert.Null(play.State.ActiveModel);

        var reload = Play(Mortar(WeaponKeywords.Reload));
        reload.Put(A(1), "4", "2").Put(B(1), "12", "2");
        Missed(reload, 2, 3);
        Assert.Equal(ActivationEndReason.Reload, Assert.IsType<ActivationEnded>(reload.Do(new ScatterAttack(Seat.Two, P("14", "2")), 1, 1).Events[^1]).Reason);
    }

    [Fact]
    [Trait("Rule", "U103: a log that scatters too far, hits the wrong models or skips the choice is refused")]
    public void AForgedScatterIsRefused()
    {
        var play = Play();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(B(2), "17", "2");
        Missed(play, 2, 2);
        var state = play.State;
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new AttackScattered(A(1), B(1), P("16", "2"), 3, [B(2)])));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new AttackScattered(A(1), B(1), P("15", "2"), 3, [B(2)])));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new AttackScattered(A(1), B(1), P("15", "2"), 4, [B(1), B(2)])));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new ScatterMissed(A(1), B(1), 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScatterMissed(A(1), B(1), 6));

        // Before the choice is asked for, only the request or a miss may follow.
        var before = Play();
        before.Put(A(1), "4", "2").Put(B(1), "12", "2");
        before.Do(new ActivateModel(Seat.One, A(1)));
        var events = TurnSequence.Decide(before.State, new Shoot(Seat.One, A(1), B(1)), new FixedDice(2, 2)).Events;
        var s = before.State;
        foreach (var e in events.TakeWhile(e => e is not DecisionRequested { Kind: DecisionKind.ChooseScatterPoint }))
        {
            s = Reducer.Apply(s, e);
        }
        Assert.Equal(RollStage.Scatter, s.InProgress!.Stage);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(s, new ActivationEnded(A(1), ActivationEndReason.Voluntary)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(s, new DecisionRequested(DecisionKind.ChooseScatterPoint, Seat.One)));
    }

    [Fact]
    [Trait("Rule", "U103: the scatter points are the same on every machine: 360 directions, unit vectors in millionths")]
    public void ScatterPointsAreWholeThousandthsOnTheDistance()
    {
        var centre = P("12", "2");
        Assert.Equal(P("15", "2"), Blast.ScatterPoint(centre, 3, 0));
        Assert.Equal(P("12", "5"), Blast.ScatterPoint(centre, 3, 90));
        Assert.Equal(P("14.121", "4.121"), Blast.ScatterPoint(centre, 3, 45));
        for (var inches = 1; inches <= 5; inches++)
        {
            for (var degree = 0; degree < Blast.ScatterDirections; degree++)
            {
                Assert.True(Blast.IsScatterDistance(centre, Blast.ScatterPoint(centre, inches, degree), inches));
            }
        }
        Assert.False(Blast.IsScatterDistance(centre, P("15.003", "2"), 3));
        Assert.True(Blast.IsScatterDistance(centre, P("15.002", "2"), 3));
    }
}
