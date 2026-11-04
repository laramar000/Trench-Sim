using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Line-of-sight stage 2b-2 (U95), through the rules: a target that shows only through a slit narrower than the space
// between the sample points can now be shot; with no slit it still cannot.
public class GapShotTests
{
    private static readonly ModelProfile Gunman = new(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true);

    // A 36" table; the shooter at (20, 12), the target at (28, 12), and a 3" wall 0.1" thick 1" in front of the target,
    // with a slit at y 12.15 to 12.25 (between the target's middle and left sample points) unless closed.
    private static BoardPlay Range(bool slit)
    {
        var zones = new[] { new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36")) };
        TerrainBlock[] walls = slit
            ? [Wall("south", "26.9", "0", "27", "12.15"), Wall("north", "26.9", "12.25", "27", "36")]
            : [Wall("wall", "26.9", "0", "27", "36")];
        var play = new BoardPlay(1, _ => Gunman, scenario: new Scenario(Field("36", walls, zones), [])).ToActivation();
        play.Put(A(1), "20", "12").Put(B(1), "28", "12");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    public void ATargetSeenOnlyThroughANarrowSlitCanBeShot()
    {
        var play = Range(slit: true);
        Assert.Null(TurnSequence.Check(play.State, new Shoot(Seat.One, A(1), B(1))));
        // The wall is 1" from the target's base, so it gives no cover (U15).
        var facts = Board.Assess(play.State, play.M(A(1)), play.M(B(1)));
        Assert.True(facts.IsLegal);
        Assert.False(facts.InCover);
    }

    [Fact]
    public void WithNoSlitTheTargetStillCannotBeShot()
    {
        Range(slit: false).Refused(new Shoot(Seat.One, A(1), B(1)), ReasonCode.NoLineOfSight);
    }
}
