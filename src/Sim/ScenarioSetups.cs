using Rules.Core.Scenarios;
using Rules.Core.State;

namespace Sim;

// The scenarios a server offers: Claim No Man's Land with the practice warbands from the content files. The first
// warband in a file takes Seat One (models a1, a2, ...), the second Seat Two (b1, b2, ...).
public static class ScenarioSetups
{
    public static IReadOnlyDictionary<string, MatchSetup> FromFolder(string folder) => new Dictionary<string, MatchSetup>(StringComparer.Ordinal)
    {
        ["ranged"] = Setup(Path.Combine(folder, "test-warbands.json")),
        ["melee"] = Setup(Path.Combine(folder, "melee-warbands.json")),
    };

    public static MatchSetup Setup(string contentPath)
    {
        var warbands = Content.Load(contentPath);
        var models = warbands[0].Models.Select((m, i) => new SetupModel(new ModelId($"a{i + 1}"), Seat.One, m.Profile))
            .Concat(warbands[1].Models.Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, m.Profile)));
        return ClaimNoMansLand.CreateSetup(models);
    }
}
