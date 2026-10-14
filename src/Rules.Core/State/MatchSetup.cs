using System.Collections.Immutable;

namespace Rules.Core.State;

public sealed record SetupModel(ModelId Id, Seat Seat);

// Everything fixed when a match is created. Zone ids are opaque names; their geometry belongs to M3.
public sealed record MatchSetup
{
    public MatchSetup(
        string rulesVersion,
        string scenarioId,
        int turnLimit,
        RulesProfile profile,
        IEnumerable<string> zoneIds,
        IEnumerable<SetupModel> models)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(zoneIds);
        ArgumentNullException.ThrowIfNull(models);
        if (turnLimit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(turnLimit), "A match lasts at least one turn.");
        }
        if (!Enum.IsDefined(profile.InitiativeCount) || !Enum.IsDefined(profile.MoraleOrder))
        {
            throw new ArgumentException("Unknown rules profile value.", nameof(profile));
        }

        RulesVersion = IdRules.Check(rulesVersion, nameof(rulesVersion));
        ScenarioId = IdRules.Check(scenarioId, nameof(scenarioId));
        TurnLimit = turnLimit;
        Profile = profile;

        var zones = zoneIds.Select(z => IdRules.Check(z, "zoneId")).ToImmutableArray();
        if (zones.Length != 2 || zones[0] == zones[1])
        {
            throw new ArgumentException("A scenario has exactly two different deployment zones.", nameof(zoneIds));
        }
        ZoneIds = zones;

        var list = models.ToImmutableArray();
        if (list.Any(m => m is null || !Enum.IsDefined(m.Seat)))
        {
            throw new ArgumentException("Every model needs a seat.", nameof(models));
        }
        if (list.Select(m => m.Id.Value).Distinct(StringComparer.Ordinal).Count() != list.Length)
        {
            throw new ArgumentException("Model ids must be unique.", nameof(models));
        }
        if (!list.Any(m => m.Seat == Seat.One) || !list.Any(m => m.Seat == Seat.Two))
        {
            throw new ArgumentException("Each seat needs at least one model.", nameof(models));
        }
        Models = list.OrderBy(m => m.Id.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    public string RulesVersion { get; }
    public string ScenarioId { get; }
    public int TurnLimit { get; }
    public RulesProfile Profile { get; }
    public ImmutableArray<string> ZoneIds { get; }
    public ImmutableArray<SetupModel> Models { get; }
}
