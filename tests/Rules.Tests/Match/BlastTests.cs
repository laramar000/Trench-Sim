using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R4b (U102): BLAST (X") (p. 53). On a hit, every model within X" of the target's base centre that has Line
// of Sight to the target is hit, and so are the attacker's own models within 1" of an enemy model the blast hit; each
// has its own Injury Roll, and only the target's gets a Critical Success's extra dice.
public class BlastTests
{
    private static ModelProfile Bomber(int blast) => new(L("6"), L("0.5"), L("18")) { RangedWeaponBlast = blast };

    private static BoardPlay Play(int blast, Scenario? scenario = null)
    {
        var play = new BoardPlay(3, id => id.Value.StartsWith('A') ? Bomber(blast) : Rifleman, scenario: scenario).ToActivation();
        return play;
    }

    private static BoardPlay Active(BoardPlay play)
    {
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "BLAST p. 53: models within X\" that see the target are hit, then friends within 1\" of a hit enemy")]
    public void ABlastHitsEveryoneInItsRadiusAndFriendsBesideHitEnemies()
    {
        var play = Play(2);
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(B(2), "13.5", "2").Put(A(2), "15.2", "2");
        Active(play);
        // A Critical Success (6, 6): the target's Injury Roll has 3 dice, the others 2. A2 is 2.7" from the target but
        // 0.7" from B2, which the blast hit.
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 6, 6, 1, 1, 1, 1, 1, 1, 1);
        Assert.Equal([B(1), A(2), B(2)], result.Events.OfType<BlastHit>().Single().Hit.ToArray());
        var injuries = result.Events.OfType<InjuryRolled>().ToList();
        Assert.Equal([B(1), A(2), B(2)], injuries.Select(i => i.Target).ToArray());
        Assert.Equal([1, 0, 0], injuries.Select(i => i.Injury.NetDice).ToArray());
        Assert.All([B(1), A(2), B(2)], id => Assert.Equal(1, play.M(id).BloodMarkers));
        Assert.Equal(0, play.M(B(3)).BloodMarkers);
        Assert.Null(play.State.InProgress);

        // A miss hits nobody.
        var miss = Active(Play(2).Put(A(1), "4", "2").Put(B(1), "12", "2").Put(B(2), "13.5", "2"));
        Assert.Empty(miss.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1).Events.OfType<BlastHit>());
    }

    [Fact]
    [Trait("Rule", "BLAST p. 53: a model that cannot see the target is not hit")]
    public void AModelThatCannotSeeTheTargetIsNotHit()
    {
        var wall = new TerrainBlock("wall", F("10", "2.8", "14", "3.4"), TerrainProfile.Wall, TerrainKind.Impassable);
        var play = Play(2, MeleePlay.WithTerrain(wall));
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(B(2), "12", "4.2").Put(B(3), "13.5", "2");
        Active(play);
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 4, 1, 1, 1, 1);
        Assert.Equal([B(1), B(3)], result.Events.OfType<BlastHit>().Single().Hit.ToArray());
        Assert.Equal(0, play.M(B(2)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "BLAST p. 53: the radius counts height too; it reaches the closest point of the base")]
    public void TheRadiusIsMeasuredToTheBaseAndUpwards()
    {
        var base1 = new ModelBody("m", P("3.5", "0"), L("0.5"));
        Assert.True(Blast.Reaches(P("0", "0"), Length.Zero, base1, Length.Zero, L("3")));    // gap exactly 3"
        Assert.False(Blast.Reaches(P("0", "0"), Length.Zero, base1, L("0.1"), L("3")));      // 0.1" up: just beyond
        Assert.True(Blast.Reaches(P("0", "0"), Length.Zero, new ModelBody("m", P("0.4", "0"), L("0.5")), L("2.9"), L("3"))); // over the base
        Assert.False(Blast.Reaches(P("0", "0"), Length.Zero, new ModelBody("m", P("0.4", "0"), L("0.5")), L("3.1"), L("3")));
        // 3-4-5: a base edge 2.4" across and 1.8" up is exactly 3" away.
        Assert.True(Blast.Reaches(P("0", "0"), Length.Zero, new ModelBody("m", P("2.9", "0"), L("0.5")), L("1.8"), L("3")));
        Assert.False(Blast.Reaches(P("0", "0"), Length.Zero, new ModelBody("m", P("2.902", "0"), L("0.5")), L("1.8"), L("3")));
    }

    [Fact]
    [Trait("Rule", "p. 28: the attacker may spend each hit enemy's Blood Markers on its own Injury Roll")]
    public void MarkersAreOfferedBeforeEachInjuryRoll()
    {
        var play = Play(2);
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(B(2), "13.5", "2").Mark(B(2), 2);
        Active(play);
        play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 4, 1, 1);
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnInjuryRoll, Seat.One), play.State.Pending);
        Assert.Equal(B(2), play.State.InProgress!.Target);
        var injury = play.Do(new SpendMarkers(Seat.One, 1), 1, 1, 1).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((B(2), 1), (injury.Target, injury.Injury.NetDice));
        Assert.Null(play.State.InProgress);
    }

    [Fact]
    [Trait("Rule", "U102: a log naming other models hit by the blast is refused")]
    public void AForgedBlastIsRefused()
    {
        var play = Play(2);
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(B(2), "13.5", "2");
        Active(play);
        var events = TurnSequence.Decide(play.State, new Shoot(Seat.One, A(1), B(1)), new FixedDice(4, 4, 1, 1, 1, 1)).Events;
        var state = play.State;
        foreach (var e in events.TakeWhile(e => e is not BlastHit))
        {
            state = Reducer.Apply(state, e);
        }
        var blast = events.OfType<BlastHit>().Single();
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new BlastHit(A(1), B(1), [B(1)])));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new BlastHit(A(1), B(1), [B(1), B(2), B(3)])));
        Assert.Throws<ArgumentException>(() => new BlastHit(A(1), B(1), [B(2), B(1)]));
        // Skipping the blast and rolling for the target at once is refused too.
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, events.OfType<InjuryRolled>().First()));
        var after = Reducer.Apply(state, blast);
        Assert.Equal(RollStage.InjuryRoll, after.InProgress!.Stage);
    }

    [Fact]
    [Trait("Rule", "BLAST p. 53 with p. 49: the attacker in its own blast is hit, and taken Down its activation ends")]
    public void TheAttackerInItsOwnBlastIsHit()
    {
        var play = Play(3);
        play.Put(A(1), "4", "2").Put(B(1), "6.5", "2"); // gap 1.5": the attacker is within 3" of the target's centre
        Active(play);
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 4, 1, 1, 4, 4);
        Assert.Equal([B(1), A(1)], result.Events.OfType<BlastHit>().Single().Hit.ToArray());
        Assert.Equal(ModelStatus.Down, play.M(A(1)).Status);
        Assert.Equal(ActivationEndReason.TakenDown, Assert.IsType<ActivationEnded>(result.Events[^1]).Reason);
        Assert.Null(play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "U102: BLAST travels with the weapon; a second gun and grenades have their own")]
    public void TheWeaponUsedHasTheBlast()
    {
        var profile = new ModelProfile(L("6"), L("0.5"), L("18"))
        {
            SecondRangedWeapon = new Weapon(L("18")) { Blast = 2 },
            Grenade = new Weapon(L("8")) { Blast = 3 },
        };
        Assert.Equal((0, 2, 3), (profile.RangedWeaponBlast, profile.Using(AttackKind.Ranged, 1).RangedWeaponBlast, profile.Using(AttackKind.Ranged, 2).RangedWeaponBlast));
        Assert.Throws<ArgumentException>(() => new Weapon(Length.Zero) { Blast = 2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), L("0.5"), L("18")) { RangedWeaponBlast = 7 });

        var play = new BoardPlay(2, id => id.Value.StartsWith('A') ? profile : Rifleman).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(B(2), "13.5", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Empty(TurnSequence.BlastHits(play.State, A(1), B(1)));
        Assert.Equal([B(1), B(2)], TurnSequence.BlastHits(play.State, A(1), B(1), 1).ToArray());
        Assert.Equal([B(1), B(2)], play.Do(new Shoot(Seat.One, A(1), B(1)) { Weapon = 1 }, 4, 4, 1, 1, 1, 1).Events.OfType<BlastHit>().Single().Hit.ToArray());
    }
}
