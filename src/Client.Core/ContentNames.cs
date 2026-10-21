using System.Reflection;
using System.Text.Json;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Core;

// Model names from the content files (D5). The server builds the ids a1, a2, ... for the first warband of the
// scenario's file and b1, b2, ... for the second, in file order (Sim.ScenarioSetups). The names are used only if every
// mapped model's profile equals the one in the match's own setup; otherwise the client shows ids and says why, rather
// than ever showing a wrong name. Reading the file is a copy of Sim.Content.Parse (M7.3, S2), checked by a test.
public sealed class ContentNames
{
    private readonly IReadOnlyDictionary<string, string> _names;

    private ContentNames(IReadOnlyDictionary<string, string> names, string? warbandOne, string? warbandTwo, string? problem)
    {
        _names = names;
        WarbandOne = warbandOne;
        WarbandTwo = warbandTwo;
        Problem = problem;
    }

    public static ContentNames None { get; } = new(new Dictionary<string, string>(), null, null, null);

    public string? WarbandOne { get; }
    public string? WarbandTwo { get; }

    // Why names are not shown, in words; null when they are.
    public string? Problem { get; }

    public string WarbandOf(Seat seat) => (seat == Seat.One ? WarbandOne : WarbandTwo) ?? Words.Seat(seat);

    // "Sharpshooter (a1)", or just "a1" when names are not known.
    public string Of(ModelId id) => _names.TryGetValue(id.Value, out var name) ? $"{name} ({id.Value})" : id.Value;

    // The scenario names the server offers, and the content file each uses (as Sim.ScenarioSetups does).
    // A test size ("ranged-2v2") uses the first models of the same file.
    public static string? FileFor(string scenario) => BaseOf(scenario) switch
    {
        "ranged" => "test-warbands.json",
        "melee" => "melee-warbands.json",
        _ => null,
    };

    // The scenarios a player can choose when creating a match: the normal rosters, then the small test sizes.
    // The tower layouts ("ranged-towers", towers proposal stage T3) come last, so the earlier choices keep their places.
    public static IReadOnlyList<string> Choices { get; } =
        ["ranged", "melee", "ranged-1v1", "ranged-2v2", "ranged-3v3", "melee-1v1", "melee-2v2", "melee-3v3", "ranged-towers", "melee-towers",
            "ranged-1v1-towers", "ranged-2v2-towers", "ranged-3v3-towers", "melee-1v1-towers", "melee-2v2-towers", "melee-3v3-towers"];

    public const string TowersSuffix = "-towers";

    // The words for a scenario in the create-match list: "ranged (normal roster)", "melee, 2 v 2 (test roster)",
    // "ranged with towers (normal roster)", "melee with towers, 1 v 1 (test roster)".
    public static string Label(string scenario)
    {
        var towers = scenario.EndsWith(TowersSuffix, StringComparison.Ordinal);
        var name = towers ? scenario[..^TowersSuffix.Length] : scenario;
        var map = towers ? " with towers" : "";
        return name is "ranged" or "melee"
            ? $"{name}{map} (normal roster)"
            : $"{name[..^4]}{map}, {name[^3]} v {name[^1]} (test roster)";
    }

    private static string BaseOf(string scenario) =>
        scenario.EndsWith(TowersSuffix, StringComparison.Ordinal) ? BaseOf(scenario[..^TowersSuffix.Length])
        : scenario.Length > 4 && scenario[^4] == '-' && scenario[^2] == 'v' && char.IsAsciiDigit(scenario[^3]) && scenario[^3] == scenario[^1]
            ? scenario[..^4]
            : scenario;

    public static ContentNames For(string scenario, MatchSetup? setup)
    {
        if (setup is null)
        {
            return None;
        }
        if (FileFor(scenario) is not { } file)
        {
            return Ids($"This client has no content for the scenario \"{scenario}\".");
        }
        IReadOnlyList<ContentWarband> warbands;
        try
        {
            warbands = Parse(Read(file));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException)
        {
            return Ids($"This client's content file {file} could not be read.");
        }
        var sized = scenario.EndsWith(TowersSuffix, StringComparison.Ordinal) ? scenario[..^TowersSuffix.Length] : scenario;
        if (sized != BaseOf(sized))
        {
            // A test size: the first models of each warband, as Sim.ScenarioSetups builds it.
            var size = sized[^1] - '0';
            warbands = [.. warbands.Select(w => w with { Models = [.. w.Models.Take(size)] })];
        }
        return Match(warbands, setup);
    }

    public static ContentNames Match(IReadOnlyList<ContentWarband> warbands, MatchSetup setup)
    {
        if (warbands.Count < 2)
        {
            return Ids("This client's content does not match the server's.");
        }
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var expected = warbands[0].Models.Select((m, i) => ($"a{i + 1}", Seat.One, m))
            .Concat(warbands[1].Models.Select((m, i) => ($"b{i + 1}", Seat.Two, m)))
            .ToList();
        if (expected.Count != setup.Models.Length)
        {
            return Ids("This client's content does not match the server's.");
        }
        foreach (var (id, seat, model) in expected)
        {
            var actual = setup.Models.FirstOrDefault(m => m.Id.Value == id);
            if (actual is null || actual.Seat != seat || actual.Profile != model.Profile)
            {
                return Ids("This client's content does not match the server's.");
            }
            names[id] = model.Name;
        }
        return new ContentNames(names, warbands[0].Name, warbands[1].Name, null);
    }

    private static ContentNames Ids(string problem) => new(new Dictionary<string, string>(), null, null, problem + " Models are shown by id.");

    public static string Read(string file)
    {
        using var stream = typeof(ContentNames).Assembly.GetManifestResourceStream("content/" + file)
            ?? throw new InvalidOperationException($"No embedded content {file}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // A copy of Sim.Content.Parse.
    public static IReadOnlyList<ContentWarband> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var warbands = new List<ContentWarband>();
        foreach (var band in doc.RootElement.GetProperty("warbands").EnumerateArray())
        {
            var models = new List<ContentModel>();
            foreach (var m in band.GetProperty("models").EnumerateArray())
            {
                var diameter = Length.FromMillimetres(m.GetProperty("baseMm").GetInt32());
                var radius = Length.FromThousandths(diameter.Thousandths / 2);
                models.Add(new ContentModel(
                    m.GetProperty("name").GetString()!,
                    new ModelProfile(
                        Length.Parse(m.GetProperty("movementInches").GetString()!),
                        radius,
                        Length.Parse(m.GetProperty("rangeInches").GetString()!),
                        m.GetProperty("weaponInjuryModifier").GetInt32(),
                        m.GetProperty("armour").GetInt32(),
                        m.TryGetProperty("meleeInjuryModifier", out var melee),
                        melee.ValueKind == JsonValueKind.Number ? melee.GetInt32() : 0)));
            }
            warbands.Add(new ContentWarband(band.GetProperty("id").GetString()!, band.GetProperty("name").GetString()!, models));
        }
        return warbands;
    }
}

public sealed record ContentModel(string Name, ModelProfile Profile);

public sealed record ContentWarband(string Id, string Name, IReadOnlyList<ContentModel> Models);
