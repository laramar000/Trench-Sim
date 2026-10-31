using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Line-of-sight review 2026-10-25 (Stage 1, tests only): a Melee Attack across a trench's rim, through the rules, as it
// is today. Defended Obstacle (p. 45) reads the trench-edge cover of U83; a target the attacker cannot see is refused.
// The deep-trench case is open question T1 and is pinned here, not decided.
public class TrenchMeleeSightTests
{
    private static readonly ModelProfile Fighter = new(L("6"), L("0.5"), Length.Zero, hasMeleeWeapon: true);

    // A 36" table like BoardPlay.TrenchField, with one trench at the given depth: x 31.5 to 34, y 14 to 22.
    private static BoardPlay Rim(string depth)
    {
        var zones = new[] { new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36")) };
        var trench = new TerrainBlock("trench", F("31.5", "14", "34", "22"), TerrainProfile.Low) { Depth = L(depth) };
        var play = new BoardPlay(1, _ => Fighter, scenario: new Scenario(Field("36", [trench], zones), [])).ToActivation();
        // The attacker on the ground touching the west edge; the target inside, against the near (west) wall.
        play.Put(A(1), "31", "18").Put(B(1), "32.1", "18");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    public void AtOneInchTheTargetAgainstTheNearWallIsSeenInFullAndHasNoDefendedObstacle()
    {
        var declared = Rim("1").Do(new FightModel(Seat.One, A(1), B(1)), 1, 1).Events.OfType<MeleeAttackDeclared>().Single();
        Assert.Null(declared.DefendedObstacleId);
    }

    [Fact]
    public void AtTwoInchesTheNearWallHidesPartOfTheTargetAndGivesDefendedObstacle()
    {
        // -1 DICE: three dice are rolled and the lowest two kept (p. 45).
        var declared = Rim("2").Do(new FightModel(Seat.One, A(1), B(1)), 1, 1, 1).Events.OfType<MeleeAttackDeclared>().Single();
        Assert.Equal("trench", declared.DefendedObstacleId);
    }

    [Fact]
    [Trait("Rule", "OPEN QUESTION T1 (review 2026-10-25): not decided; this pins today's result only")]
    public void T1_AtNearlyThreeInchesTheTargetWithinAnInchCannotBeSeenSoTheAttackIsRefused()
    {
        Rim("2.9").Refused(new FightModel(Seat.One, A(1), B(1)), ReasonCode.NoLineOfSight);
    }
}
