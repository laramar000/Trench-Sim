using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core;

// A stable fingerprint of a match state. The state is written out as plain text in a fixed field order
// (models sorted by id, enums by name, integers in invariant form), and the hash is SHA-256 of that
// text. Two machines with the same state always get the same text and the same hash, so a client can
// compare hashes after every event. Any change to the layout must bump Version.
public static class StateHash
{
    public const int Version = 3;

    public static string Canonical(MatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sb = new StringBuilder();
        Line(sb, "trench-state", Version);

        if (state.Setup is { } setup)
        {
            Line(sb, "rules", setup.RulesVersion);
            Line(sb, "scenario", setup.ScenarioId);
            Line(sb, "turnLimit", setup.TurnLimit);
            Line(sb, "initiativeCount", setup.Profile.InitiativeCount);
            Line(sb, "moraleOrder", setup.Profile.MoraleOrder);
            Line(sb, "zones", string.Join(",", setup.ZoneIds));
            if (setup.Scenario is { } scenario)
            {
                WriteScenario(sb, scenario);
                foreach (var m in setup.Models)
                {
                    var prof = m.Profile!;
                    Line(sb, "profile", $"{m.Id.Value},move={prof.Movement.Thousandths},radius={prof.BaseRadius.Thousandths},range={prof.RangedRange.Thousandths},weapon={prof.RangedInjuryModifier},armour={prof.Armour},melee={Bool(prof.HasMeleeWeapon)},meleeMod={prof.MeleeInjuryModifier}");
                }
            }
        }

        Line(sb, "phase", state.Phase);
        Line(sb, "turn", state.Turn);
        Line(sb, "turnEnded", state.TurnEnded);
        Line(sb, "deploymentFirst", state.DeploymentFirst);
        Line(sb, "lastDeployer", state.LastDeployer);
        Line(sb, "initiativeHolder", state.InitiativeHolder);
        Line(sb, "firstActivator", state.FirstActivator);
        Line(sb, "lastActivator", state.LastActivator);
        Line(sb, "activeModel", state.ActiveModel?.Value);
        Line(sb, "moraleFirst", state.MoraleFirst);
        Line(sb, "pending", state.Pending is { } p ? $"{p.Kind}:{p.Seat}" : null);
        Line(sb, "actions", (int)state.Actions);
        Line(sb, "movementHalved", state.MovementHalved);
        Line(sb, "scoredTurn", state.ScoredTurn);
        Line(sb, "inProgress", state.InProgress is { } r
            ? $"{r.Stage}:{r.Actor.Value}:{r.Target?.Value ?? "-"}:{r.BonusDice}:{r.PenaltyDice}:{Bool(r.Critical)}:{Bool(r.MarkersAsked)}:{r.Kind}:{Bool(r.FreeFight)}:{Bool(r.FriendlyFire)}:{r.ChargeDie}:{(r.DashPath.IsDefaultOrEmpty ? "-" : Path(r.DashPath))}"
            : null);
        Line(sb, "retreat", state.Retreat is { } t
            ? $"{t.Model.Value}:{Path(t.Path)}:{string.Join(";", t.Fought.Select(f => f.Value))}:{Bool(t.Closed)}"
            : null);
        Line(sb, "outcome", state.Outcome is { } o ? $"{(o.Winner?.ToString() ?? "-")}:{o.Reason}" : null);

        foreach (var seat in new[] { state.One, state.Two })
        {
            Line(sb, $"seat.{seat.Seat}", string.Join(
                ",",
                $"zone={seat.ZoneId ?? "-"}",
                $"shaken={Bool(seat.Shaken)}",
                $"fled={Bool(seat.Fled)}",
                $"checked={Bool(seat.MoraleChecked)}",
                $"outcome={seat.MoraleOutcome?.ToString() ?? "-"}",
                $"vp={seat.VictoryPoints}"));
        }

        foreach (var m in state.Models.OrderBy(m => m.Id.Value, StringComparer.Ordinal))
        {
            Line(sb, "model", string.Join(
                ",",
                m.Id.Value,
                m.Seat,
                m.Status,
                $"deployed={Bool(m.Deployed)}",
                $"activated={Bool(m.ActivatedThisTurn)}",
                $"at={(m.Position is { } at ? Point(at) : "-")}",
                $"markers={m.BloodMarkers}"));
        }
        return sb.ToString();
    }

    public static string Compute(MatchState state) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(state))));

    private static string Point(Point2 p) => $"{p.X.Thousandths}/{p.Y.Thousandths}";

    private static string Path(IEnumerable<Point2> path) => string.Join(";", path.Select(Point));

    private static string Rectangle(Footprint f) => $"{Point(f.Bounds.Min)}-{Point(f.Bounds.Max)}";

    // Terrain and zones are written in id order (the Battlefield already sorts them).
    private static void WriteScenario(StringBuilder sb, Scenario scenario)
    {
        var field = scenario.Battlefield;
        Line(sb, "table", $"{field.Width.Thousandths}x{field.Height.Thousandths}");
        foreach (var z in field.Zones)
        {
            Line(sb, "zone", $"{z.ZoneId},{Rectangle(z.Area)}");
        }
        foreach (var t in field.Terrain)
        {
            Line(sb, "terrain", $"{t.Id},{Rectangle(t.Footprint)},{t.Profile},{(int)t.Kind},{t.Surface},walkable={Bool(t.IsWalkable)}");
        }
        Line(sb, "objectives", string.Join(",", scenario.ObjectiveIds));
    }

    private static string Bool(bool value) => value ? "1" : "0";

    private static void Line(StringBuilder sb, string key, object? value)
    {
        sb.Append(key).Append('=');
        sb.Append(value switch
        {
            null => "-",
            bool b => Bool(b),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        });
        sb.Append('\n');
    }
}
