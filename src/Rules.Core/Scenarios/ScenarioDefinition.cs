using System.Collections.Immutable;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Scenarios;

// Trench-maps stage S5: what a scenario needs from a map (U87). The deployment zones it names, how many Objective
// pieces, and the battlefield archetype the book gives it. A map that names a different archetype does not fit; a
// map that names none is a test layout and fits if the rest does.
public sealed record MapNeeds(ImmutableArray<string> ZoneIds, int Objectives, Archetype Archetype);

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
        if (archetype is { } a && a != Needs.Archetype)
        {
            unmet.Add($"{Id} is played on the {Needs.Archetype} archetype; the map is {a}.");
        }
        return [.. unmet];
    }

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
