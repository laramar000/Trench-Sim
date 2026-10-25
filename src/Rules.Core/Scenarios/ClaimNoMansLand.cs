using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Scenarios;

// Scenario I, "Claim No Man's Land" (Digital Rulebook 1.0.2, pp. 151-153) as a fixed battlefield.
//
// Printed: two deployment zones 24" apart; Objective markers 6" in from each zone's edge and 6" from the table's
// edge, plus one at the midpoint; the terrain piece covering a marker is the Objective; four Turns.
// NOT printed, so chosen here (provisional U32): a 36" x 36" table, 6" deep zones along the west and east edges
// (24" between them), the five Objective pieces as 4" x 4" Cover rubble centred on the marker positions, and a few
// extra non-Objective pieces. The terrain roll-off and set-up the book describes are not played; the layout is fixed.
public static class ClaimNoMansLand
{
    public const string ScenarioId = "claim-no-mans-land";
    public const string RulesVersion = "1.0.2";
    public const int TurnLimit = 4;
    public const string West = "west";
    public const string East = "east";

    // Trench-maps stage S5: the scenario apart from its map. Five Objective pieces (p. 152) and the No Man's Land
    // archetype (p. 151), deploying west then east.
    public static readonly ScenarioDefinition Definition =
        new(ScenarioId, RulesVersion, TurnLimit, [West, East], new MapNeeds([West, East], 5, Archetype.NoMansLand));

    private static TerrainBlock Piece(string id, string x1, string y1, string x2, string y2, TerrainProfile profile, TerrainKind kind = TerrainKind.Open) =>
        new(id, Rect(x1, y1, x2, y2), profile, kind);

    private static Footprint Rect(string x1, string y1, string x2, string y2) =>
        Footprint.Of(new Rect(new Point2(Length.Parse(x1), Length.Parse(y1)), new Point2(Length.Parse(x2), Length.Parse(y2))));

    public static Scenario Create()
    {
        var terrain = new[]
        {
            // The five Objective pieces: rubble you can stand in, with cover.
            Piece("obj-1", "10", "4", "14", "8", TerrainProfile.Cover),
            Piece("obj-2", "10", "28", "14", "32", TerrainProfile.Cover),
            Piece("obj-3", "22", "4", "26", "8", TerrainProfile.Cover),
            Piece("obj-4", "22", "28", "26", "32", TerrainProfile.Cover),
            Piece("obj-5", "16", "16", "20", "20", TerrainProfile.Cover),
            // Ground walls that block sight, and rough ground that costs double.
            Piece("wall-1", "14", "11", "20", "12", TerrainProfile.Wall),
            Piece("wall-2", "16", "24", "22", "25", TerrainProfile.Wall),
            Piece("rough-1", "7", "14", "11", "22", TerrainProfile.Low, TerrainKind.Difficult),
            Piece("rough-2", "25", "14", "29", "22", TerrainProfile.Low, TerrainKind.Difficult),
        };
        var zones = new[]
        {
            new DeploymentZone(West, Rect("0", "0", "6", "36")),
            new DeploymentZone(East, Rect("30", "0", "36", "36")),
        };
        var battlefield = new Battlefield(Length.FromInches(36), Length.FromInches(36), terrain, zones);
        return new Scenario(battlefield, ["obj-1", "obj-2", "obj-3", "obj-4", "obj-5"]);
    }

    public static MatchSetup CreateSetup(IEnumerable<SetupModel> models, RulesProfile? profile = null) =>
        new(RulesVersion, ScenarioId, TurnLimit, profile ?? RulesProfile.Default, [West, East], models, Create());

    // Towers proposal: the same scenario and layout with two climbable towers added on the table's north-south centre
    // line, between the Objectives, mirrored so neither side is favoured. Each is a 3" x 3" platform 2" high with a
    // ladder on its west and east sides (U66 to U74). They stand well over 1" from every Objective, zone and other piece.
    public const string TowerHeight = "2";

    //
    // Trench-maps stage S3 (Jason, 2026-10-21: towers and any vertical terrain in the default maps): the same layout
    // also has 1" deep trenches in both deployment zones, mirrored west to east (No Man's Land keeps trenches in the
    // deployment zones, p. 65). In each zone: to the south one section with a bend (an L of two parts), to the north
    // two straight sections joined end to end. Every section is 2.5" wide and 4" to 12" long (p. 62); the middle of
    // each zone, where the test matches deploy, is left open.
    public const string TrenchDepth = "1";

    public static Scenario CreateWithTowers()
    {
        var plain = Create();
        var towers = new[]
        {
            Tower("tower-s", "16.5", "5", "19.5", "8"),
            Tower("tower-n", "16.5", "28", "19.5", "31"),
        };
        var trenches = new[]
        {
            Trench("trench-sw", Part("1.5", "4", "4", "12"), Part("4", "9.5", "6", "12")),
            Trench("trench-nw", Part("1.5", "24", "4", "30")),
            Trench("trench-nw-2", Part("1.5", "30", "4", "34")),
            Trench("trench-se", Part("32", "4", "34.5", "12"), Part("30", "9.5", "32", "12")),
            Trench("trench-ne", Part("32", "24", "34.5", "30")),
            Trench("trench-ne-2", Part("32", "30", "34.5", "34")),
        };
        var field = plain.Battlefield;
        return new Scenario(new Battlefield(field.Width, field.Height, field.Terrain.Concat(towers).Concat(trenches), field.Zones), plain.ObjectiveIds);
    }

    private static Rect Part(string x1, string y1, string x2, string y2) => Rect(x1, y1, x2, y2).Bounds;

    private static TerrainBlock Trench(string id, params Rect[] parts) =>
        new(id, parts.Length == 1 ? Footprint.Of(parts[0]) : Footprint.Join(parts), TerrainProfile.Low) { Depth = Length.Parse(TrenchDepth) };

    public static MatchSetup CreateTowersSetup(IEnumerable<SetupModel> models, RulesProfile? profile = null) =>
        new(RulesVersion, ScenarioId, TurnLimit, profile ?? RulesProfile.Default, [West, East], models, CreateWithTowers());

    private static TerrainBlock Tower(string id, string x1, string y1, string x2, string y2)
    {
        var rect = Rect(x1, y1, x2, y2).Bounds;
        var midY = Length.FromThousandths((rect.Min.Y.Thousandths + rect.Max.Y.Thousandths) / 2);
        return new TerrainBlock(id, Footprint.Of(rect), TerrainProfile.Wall, surface: SurfaceLevel.High, walkable: true)
        {
            Climb = new Climb(Length.Parse(TowerHeight), [new Ladder($"{id}-west", new Point2(rect.Min.X, midY)), new Ladder($"{id}-east", new Point2(rect.Max.X, midY))]),
        };
    }
}
