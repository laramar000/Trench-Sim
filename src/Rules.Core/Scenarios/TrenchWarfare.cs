using System.Collections.Immutable;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Scenarios;

// Scenario IV, "Trench Warfare" (Digital Rulebook 1.0.2, pp. 161-163), trench-maps stage S6.
//
// Built: the attacker and defender roll-off, whose winner chooses who attacks (p. 161, a one-off game); the defender
// takes the deployment zone holding the map's trench lines and the attacker the other (U88); the attacker deploys
// first and the players alternate (p. 162); at the end of each Turn each trench section at least 8" long scores (p. 163);
// at the end of Turn 4 the attacker rolls a D6 and on 1-2 the game ends, otherwise after Turn 5 (p. 162); the defender's
// 12 Mine Markers with 4 secret duds (p. 162, U93; Geometry/Mines.cs).
// Not built (listed on the scenario card): the defender's halved ducats, Infiltrators. Glorious Deeds are in Deeds (U96).
public static class TrenchWarfare
{
    public const string ScenarioId = "trench-warfare";
    public const string RulesVersion = ClaimNoMansLand.RulesVersion;

    // The most Turns a game can last; the game-length roll at the end of LengthRollTurn may end it sooner.
    public const int TurnLimit = 5;
    public const int LengthRollTurn = 4;

    // On this D6 or lower the game ends after LengthRollTurn.
    public const int EndsOnOrBelow = 2;

    public const string West = ClaimNoMansLand.West;
    public const string East = ClaimNoMansLand.East;

    // A trench section that scores is at least this long (p. 163), and the defender sets up at least three (p. 162).
    public static readonly Length ScoringLength = Length.FromInches(8);
    public const int DefenderSections = 3;

    public const int DefenderHeld = 1;
    public const int DefenderAlone = 2;
    public const int AttackerHeld = 2;
    public const int AttackerAlone = 3;

    public static readonly ScenarioDefinition Definition =
        new(ScenarioId, RulesVersion, TurnLimit, [West, East], new MapNeeds([West, East], 0, Archetype.NoMansLand) { TrenchLines = DefenderSections });

    public static bool Is(MatchSetup? setup) => setup?.ScenarioId == ScenarioId;

    // A section's length: the longer side of the rectangle around it (U88).
    public static Length LengthOf(TerrainBlock trench)
    {
        ArgumentNullException.ThrowIfNull(trench);
        var b = trench.Footprint.Bounds;
        return b.Width > b.Height ? b.Width : b.Height;
    }

    // The trench sections that score, in id order.
    public static ImmutableArray<TerrainBlock> ScoringSections(Battlefield battlefield)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        return [.. battlefield.Terrain.Where(t => t.IsTrench && LengthOf(t) >= ScoringLength).OrderBy(t => t.Id, StringComparer.Ordinal)];
    }

    // The deployment zone that wholly holds every scoring section, or null when no single zone does (U88).
    public static string? DefenderZone(Battlefield battlefield)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        var sections = ScoringSections(battlefield);
        if (sections.IsEmpty)
        {
            return null;
        }
        return battlefield.Zones
            .Where(z => sections.All(t => t.Footprint.Parts.All(p => Within(p, z.Area.Bounds))))
            .Select(z => z.ZoneId)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // The points a section gives, from how many defending and attacking models are in it (p. 163).
    public static (int Defender, int Attacker) Points(int defenders, int attackers) =>
        (defenders == 0 ? 0 : attackers == 0 ? DefenderAlone : DefenderHeld, attackers == 0 ? 0 : defenders == 0 ? AttackerAlone : AttackerHeld);

    internal static bool Within(Rect part, Rect zone) =>
        part.Min.X >= zone.Min.X && part.Max.X <= zone.Max.X && part.Min.Y >= zone.Min.Y && part.Max.Y <= zone.Max.Y;
}
