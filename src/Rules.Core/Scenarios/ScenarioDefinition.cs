using System.Collections.Immutable;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Scenarios;

// Trench-maps stage S5: what a scenario needs from a map (U87). The deployment zones it names, how many Objective
// pieces, and the battlefield archetype the book gives it. A map that names a different archetype does not fit; a
// map that names none is a test layout and fits if the rest does.
public sealed record MapNeeds(ImmutableArray<string> ZoneIds, int Objectives, Archetype Archetype)
{
    // Trench-maps stage S6 (Trench Warfare, pp. 161-162, U88): at least this many trench sections 8" or longer, all in
    // one deployment zone (the defender's), and no other terrain in either zone. Zero: no such need.
    public int TrenchLines { get; init; }
}

// Trench-maps stage S5: a scenario's rules, apart from any map: its id, rules version, turn limit, the zones in seat
// order, and what it needs from a map. A match setup is a definition, a map that fits it, and the models.
public sealed record ScenarioDefinition(string Id, string RulesVersion, int TurnLimit, ImmutableArray<string> ZoneIds, MapNeeds Needs)
{
    // Why the map does not fit this scenario; empty when it does.
    public ImmutableArray<string> Unmet(Scenario map, Archetype? archetype)
    {
        ArgumentNullException.ThrowIfNull(map);
        var unmet = new List<string>();
        var zones = map.Battlefield.Zones.Select(z => z.ZoneId).Order(StringComparer.Ordinal).ToList();
        var wanted = Needs.ZoneIds.Order(StringComparer.Ordinal).ToList();
        if (!zones.SequenceEqual(wanted, StringComparer.Ordinal))
        {
            unmet.Add($"{Id} needs deployment zones {string.Join(" and ", wanted)}; the map has {string.Join(" and ", zones)}.");
        }
        if (map.ObjectiveIds.Length != Needs.Objectives)
        {
            unmet.Add($"{Id} needs {Needs.Objectives} Objective pieces; the map has {map.ObjectiveIds.Length}.");
        }
        if (Needs.TrenchLines > 0)
        {
            var field = map.Battlefield;
            var sections = TrenchWarfare.ScoringSections(field);
            var defender = TrenchWarfare.DefenderZone(field);
            if (sections.Length < Needs.TrenchLines || defender is null)
            {
                unmet.Add($"{Id} needs {Needs.TrenchLines} trench sections at least {TrenchWarfare.ScoringLength} long, all in one deployment zone; the map has {sections.Length}{(sections.IsEmpty || defender is not null ? "" : " in more than one zone")}.");
            }
            else
            {
                var zone = field.Zones.First(z => z.ZoneId == defender).Area.Bounds;
                foreach (var piece in field.Terrain.Where(t => field.Zones.Any(z => t.Footprint.Parts.Any(p => Overlaps(p, z.Area.Bounds)))))
                {
                    if (!piece.IsTrench || !piece.Footprint.Parts.All(p => TrenchWarfare.Within(p, zone)))
                    {
                        unmet.Add($"{Id} sets up no terrain in the deployment zones but the defender's trenches; '{piece.Id}' is in one.");
                    }
                }
            }
        }
        if (archetype is { } a && a != Needs.Archetype)
        {
            unmet.Add($"{Id} is played on the {Needs.Archetype} archetype; the map is {a}.");
        }
        return [.. unmet];
    }

    private static bool Overlaps(Rect a, Rect b) =>
        a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y;

    // The setup for these models on this map. Throws when the map does not fit.
    public MatchSetup Setup(Scenario map, Archetype? archetype, IEnumerable<SetupModel> models, RulesProfile? profile = null)
    {
        var unmet = Unmet(map, archetype);
        if (!unmet.IsEmpty)
        {
            throw new ArgumentException(string.Join(" ", unmet), nameof(map));
        }
        return new MatchSetup(RulesVersion, Id, TurnLimit, profile ?? RulesProfile.Default, ZoneIds, models, map);
    }
}
