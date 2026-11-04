using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Trench-maps stage S4: hand-made layouts that break one set-up rule each, and the same layouts fixed.
public class MapLayoutTests
{
    private static readonly Length Base = L("0.63");

    private static DeploymentZone Zone(string id, string x1, string y1, string x2, string y2) => new(id, F(x1, y1, x2, y2));

    private static readonly DeploymentZone[] Zones = [Zone("west", "0", "0", "6", "36"), Zone("east", "30", "0", "36", "36")];

    private static TerrainBlock Cover(string id, string x1, string y1, string x2, string y2) =>
        new(id, F(x1, y1, x2, y2), TerrainProfile.Cover);

    private static TerrainBlock Block(string id, string x1, string y1, string x2, string y2) =>
        new(id, F(x1, y1, x2, y2), TerrainProfile.Wall, TerrainKind.Impassable);

    private static TerrainBlock Trench(string id, params Rect[] parts) =>
        new(id, parts.Length == 1 ? Footprint.Of(parts[0]) : Footprint.Join(parts), TerrainProfile.Low) { Depth = L("1") };

    private static Battlefield Table(params TerrainBlock[] terrain) => new(L("36"), L("36"), terrain, Zones);

    private static List<LayoutProblemKind> Kinds(Battlefield field, string[] objectives, Archetype? archetype = null, string[]? ruins = null) =>
        [.. MapLayout.Check(field, objectives, Base, archetype, ruins).Select(p => p.Kind)];

    [Fact]
    public void AnOpenTableHasNoProblems() =>
        Assert.Empty(MapLayout.Check(Table(Cover("obj", "16", "16", "20", "20")), ["obj"], Base, Archetype.NoMansLand));

    [Fact]
    public void AZoneWalledInIsRefused()
    {
        // A wall across the whole width in front of the west zone, with no way round.
        var walled = Table(Block("wall", "6", "0", "7", "36"), Cover("obj", "16", "16", "20", "20"));
        var problems = MapLayout.Check(walled, ["obj"], Base);
        Assert.Contains(problems, p => p.Kind == LayoutProblemKind.ObjectiveOutOfReach && p.Ids.SequenceEqual(["west", "obj"]));
        // A gap a base can pass through fixes it.
        var gap = Table(Block("wall-s", "6", "0", "7", "17"), Block("wall-n", "6", "18.5", "7", "36"), Cover("obj", "16", "16", "20", "20"));
        Assert.Empty(MapLayout.Check(gap, ["obj"], Base));
        // Too narrow for the base: refused again.
        var narrow = Table(Block("wall-s", "6", "0", "7", "17"), Block("wall-n", "6", "18", "7", "36"), Cover("obj", "16", "16", "20", "20"));
        Assert.Contains(LayoutProblemKind.ObjectiveOutOfReach, Kinds(narrow, ["obj"]));
    }

    [Fact]
    public void APocketOfAZoneThatCannotGetOutIsRefused()
    {
        // A box of impassable terrain inside the west zone with room for a base inside it.
        var pocket = Table(
            Block("s", "0", "10", "5", "11"), Block("n", "0", "14", "5", "15"), Block("e", "4", "11", "5", "14"),
            Cover("obj", "16", "16", "20", "20"));
        Assert.Equal([LayoutProblemKind.ZoneBlockedIn, LayoutProblemKind.ObjectiveOutOfReach], Kinds(pocket, ["obj"]));
    }

    [Fact]
    public void AZoneFullOfTerrainHasNoRoom()
    {
        var full = Table(Block("w", "0", "0", "6", "36"), Cover("obj", "16", "16", "20", "20"));
        Assert.Equal([LayoutProblemKind.ZoneHasNoRoom], Kinds(full, ["obj"]));
    }

    [Fact]
    public void AnObjectiveRingedByImpassableTerrainIsOutOfReach()
    {
        var ringed = Table(
            Cover("obj", "16", "16", "20", "20"),
            Block("s", "14", "14", "22", "15"), Block("n", "14", "21", "22", "22"), Block("w", "14", "15", "15", "21"), Block("e", "21", "15", "22", "21"));
        Assert.Equal([LayoutProblemKind.ObjectiveOutOfReach, LayoutProblemKind.ObjectiveOutOfReach], Kinds(ringed, ["obj"]));
    }

    [Fact]
    public void ATowerBlocksTheWayLikeAWall()
    {
        var tower = new TerrainBlock("tower", F("6", "0", "8", "36"), TerrainProfile.Wall, surface: SurfaceLevel.High, walkable: true)
        {
            Climb = new Climb(L("2"), [new Ladder("tower-w", P("6", "18"))]),
        };
        Assert.Contains(LayoutProblemKind.ObjectiveOutOfReach, Kinds(Table(tower, Cover("obj", "16", "16", "20", "20")), ["obj"]));
    }

    [Fact]
    public void NoMansLandKeepsPiecesThreeInchesApartAndTrenchesInTheZones()
    {
        var close = Table(Cover("a", "10", "10", "12", "12"), Cover("b", "14.999", "10", "16", "12"));
        Assert.Equal([LayoutProblemKind.TooClose], Kinds(close, [], Archetype.NoMansLand));
        Assert.Empty(Kinds(Table(Cover("a", "10", "10", "12", "12"), Cover("b", "15", "10", "16", "12")), [], Archetype.NoMansLand));
        // Diagonal: 2.1" and 2.1" apart is 2.97", too close.
        Assert.Equal([LayoutProblemKind.TooClose], Kinds(Table(Cover("a", "10", "10", "12", "12"), Cover("b", "14.1", "14.1", "16", "16")), [], Archetype.NoMansLand));

        // Two sections of one trench touch; a trench reaching out of its zone is refused.
        var network = Table(Trench("t1", R("1", "4", "3.5", "12")), Trench("t2", R("1", "12", "3.5", "16")));
        Assert.Empty(Kinds(network, [], Archetype.NoMansLand));
        var outside = Table(Trench("t1", R("1", "4", "8", "6.5")));
        Assert.Equal([LayoutProblemKind.TrenchOutsideZone], Kinds(outside, [], Archetype.NoMansLand));
        // Two trenches that do not touch keep the spacing.
        var apart = Table(Trench("t1", R("1", "4", "3.5", "12")), Trench("t2", R("1", "13", "3.5", "16")));
        Assert.Equal([LayoutProblemKind.TooClose], Kinds(apart, [], Archetype.NoMansLand));
        // Spacing is only checked when the map names its archetype.
        Assert.Empty(Kinds(close, []));
    }

    [Fact]
    public void DecimatedRuinsChainRuinsWithinNineInchesAndRefusesTrenches()
    {
        var chain = Table(Cover("r1", "8", "8", "12", "12"), Cover("r2", "16", "8", "20", "12"), Cover("r3", "24", "8", "28", "12"));
        Assert.Empty(Kinds(chain, [], Archetype.DecimatedRuins, ["r1", "r2", "r3"]));
        // r3 12" from r2 and 20" from r1: not set up by the rules.
        var far = Table(Cover("r1", "8", "8", "12", "12"), Cover("r2", "16", "8", "20", "12"), Cover("r3", "32", "20", "34", "24"));
        Assert.Equal([LayoutProblemKind.RuinTooFar], Kinds(far, [], Archetype.DecimatedRuins, ["r1", "r2", "r3"]));
        Assert.Equal([LayoutProblemKind.UnknownRuin], Kinds(chain, [], Archetype.DecimatedRuins, ["r1", "r2", "r3", "r9"]));
        Assert.Equal([LayoutProblemKind.TrenchNotAllowed], Kinds(Table(Trench("t", R("1", "4", "3.5", "12"))), [], Archetype.DecimatedRuins));
    }

    [Fact]
    public void TrenchLinesRunEdgeToEdgeWithOtherTerrainOneInchFromThem()
    {
        // West edge to east edge, with a bend.
        TerrainBlock[] line = [Trench("t1", R("0", "16", "20", "18.5"), R("17.5", "18.5", "20", "24")), Trench("t2", R("20", "21.5", "36", "24"))];
        Assert.Empty(Kinds(Table(line), [], Archetype.TrenchLines));
        Assert.Equal([LayoutProblemKind.NoTrenchAcross], Kinds(Table(line[0]), [], Archetype.TrenchLines));
        // 1" from a trench is fine, closer is not; other pieces keep 3".
        Assert.Empty(Kinds(Table([.. line, Cover("c", "4", "10", "8", "15")]), [], Archetype.TrenchLines));
        Assert.Equal([LayoutProblemKind.TooClose], Kinds(Table([.. line, Cover("c", "4", "10", "8", "15.5")]), [], Archetype.TrenchLines));
        Assert.Equal([LayoutProblemKind.TooClose], Kinds(Table([.. line, Cover("c", "4", "10", "8", "15"), Cover("d", "10", "10", "12", "15")]), [], Archetype.TrenchLines));
    }

    [Fact]
    public void TheGapIsMeasuredEdgeToEdge()
    {
        Assert.Equal(0, MapLayout.GapSquared(F("0", "0", "1", "1"), F("1", "0", "2", "1")));
        Assert.Equal(3000L * 3000, MapLayout.GapSquared(F("0", "0", "1", "1"), F("4", "0", "5", "1")));
        Assert.Equal(3000L * 3000 + 4000L * 4000, MapLayout.GapSquared(F("0", "0", "1", "1"), F("4", "5", "5", "6")));
        // The nearest part of a joined footprint counts.
        Assert.Equal(1000L * 1000, MapLayout.GapSquared(Footprint.Join([R("0", "0", "4", "1"), R("3", "1", "4", "5")]), F("5", "3", "6", "4")));
    }
}
