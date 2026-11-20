using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Field Shrine, stage 2 (p. 82; U128): either side can attack a shrine as if it were an enemy model. A ranged attack is aimed
// at the point of its centre and a hit removes it with no Injury Roll; a blast removes every shrine it reaches and sees.
public class ShrineAttackTests
{
    private static ModelProfile Mortar() => new(L("6"), L("0.5"), L("18")) { RangedWeaponBlast = 3 };

    private static BoardPlay Play(ModelProfile attacker, params (int Id, string X, string Y)[] shrines)
    {
        var play = new BoardPlay(3, id => id.Value.StartsWith('A') ? attacker : Rifleman).ToActivation();
        play.State = play.State with { Shrines = [.. shrines.Select(s => new ShrineState(B(s.Id), Seat.Two, P(s.X, s.Y)))] };
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82: it can be attacked as if it were an enemy model; one hit removes it with no Injury Roll")]
    public void ARangedHitRemovesAShrineWithoutAnInjuryRoll()
    {
        var play = Play(Rifleman, (1, "12", "2")).Put(A(1), "4", "2");
        var result = play.Do(new ShootPoint(Seat.One, A(1), P("12", "2")), 6, 6);
        Assert.Equal(SuccessOutcome.CriticalSuccess, result.Events.OfType<PointSuccessRolled>().Single().Check.Outcome);
        Assert.Empty(result.Events.OfType<InjuryRolled>());
        Assert.Empty(play.State.Shrines);
        Assert.Null(play.State.InProgress);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Shoot));
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82: a failed attack leaves it standing")]
    public void AMissLeavesTheShrine()
    {
        var play = Play(Rifleman, (1, "12", "2")).Put(A(1), "4", "2");
        play.Do(new ShootPoint(Seat.One, A(1), P("12", "2")), 1, 1);
        Assert.Single(play.State.Shrines);
        Assert.Null(play.State.InProgress);
    }

    [Fact]
    [Trait("Rule", "BLAST p. 53: only a BLAST weapon may be aimed at a point, apart from a Field Shrine's centre")]
    public void AnOrdinaryGunCannotAimAtEmptyGround()
    {
        var play = Play(Rifleman, (1, "12", "2")).Put(A(1), "4", "2");
        play.Refused(new ShootPoint(Seat.One, A(1), P("12", "3")), ReasonCode.NotACandidate);
        play.Refused(new ShootPoint(Seat.One, A(1), P("8", "2")), ReasonCode.NotACandidate);
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82: being inside a blast removes it; BLAST p. 53: the radius, and the point must see it")]
    public void ABlastRemovesTheShrinesInsideItsRadius()
    {
        // Radius 3" around (13, 2): the shrine at (14, 2) is inside, the one at (24, 2) is far outside.
        var play = Play(Mortar(), (1, "14", "2"), (2, "24", "2")).Put(A(1), "4", "2");
        var result = play.Do(new ShootPoint(Seat.One, A(1), P("13", "2")), 6, 6);
        Assert.Empty(result.Events.OfType<InjuryRolled>());
        Assert.Equal([B(2)], play.State.Shrines.Select(h => h.Carrier).ToArray());
        Assert.Null(play.State.InProgress);
    }
}
