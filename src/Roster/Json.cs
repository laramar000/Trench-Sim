using System.Text.Json;

namespace Roster;

// Small strict readers that turn a malformed field into a readable problem instead of an exception.
internal static class Json
{
    public const int Format = 1;
    public const string RulesVersion = "1.0.2";

    public static bool TryParse(string json, List<string> problems, out JsonDocument? doc)
    {
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            return true;
        }
        catch (JsonException e)
        {
            problems.Add($"not valid JSON: {e.Message}");
            doc = null;
            return false;
        }
    }

    public static void CheckHeader(JsonElement root, string what, List<string> problems)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"the {what} must be a JSON object");
            return;
        }
        var format = Whole(root, "format", what, problems);
        if (format is not null && format != Format)
            problems.Add($"the {what} has format {format}; this version reads format {Format} only");
        var version = Text(root, "rulesVersion", what, problems);
        if (version is not null && version != RulesVersion)
            problems.Add($"the {what} is for rules version {version}; this version supports {RulesVersion} only");
    }

    public static IEnumerable<(JsonElement Item, string At)> Items(JsonElement root, string name, string what, List<string> problems)
    {
        if (root.ValueKind != JsonValueKind.Object)
            yield break;
        if (!root.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"the {what} has no \"{name}\" list");
            yield break;
        }
        var i = 0;
        foreach (var item in list.EnumerateArray())
        {
            i++;
            var at = $"{name} item {i}";
            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{at}: must be a JSON object");
                continue;
            }
            yield return (item, at);
        }
    }

    public static string? Text(JsonElement e, string name, string at, List<string> problems, bool required = true)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (required)
                problems.Add($"{at}: \"{name}\" is missing");
            return null;
        }
        if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
        {
            problems.Add($"{at}: \"{name}\" must be non-empty text");
            return null;
        }
        return v.GetString();
    }

    public static int? Whole(JsonElement e, string name, string at, List<string> problems, bool required = true)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (required)
                problems.Add($"{at}: \"{name}\" is missing");
            return null;
        }
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n))
        {
            problems.Add($"{at}: \"{name}\" must be a whole number");
            return null;
        }
        return n;
    }

    public static bool? Flag(JsonElement e, string name, string at, List<string> problems)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            problems.Add($"{at}: \"{name}\" must be true or false");
            return null;
        }
        return v.GetBoolean();
    }

    // Enum fields are written in camelCase ("ranged", "glory"); anything else is a problem, never a default.
    public static T? Choice<T>(JsonElement e, string name, string at, List<string> problems, bool required = true) where T : struct, Enum
    {
        var text = Text(e, name, at, problems, required);
        if (text is null)
            return null;
        foreach (var value in Enum.GetValues<T>())
            if (CamelCase(value.ToString()) == text)
                return value;
        problems.Add($"{at}: \"{name}\" is \"{text}\"; it must be one of {string.Join(", ", Enum.GetValues<T>().Select(v => CamelCase(v.ToString())))}");
        return null;
    }

    public static string CamelCase(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    public static List<string> Strings(JsonElement e, string name, string at, List<string> problems)
    {
        var list = new List<string>();
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            return list;
        if (v.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{at}: \"{name}\" must be a list of text");
            return list;
        }
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                problems.Add($"{at}: every item of \"{name}\" must be non-empty text");
            else
                list.Add(item.GetString()!);
        }
        return list;
    }

    public static IEnumerable<JsonElement> Objects(JsonElement e, string name, string at, List<string> problems)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            yield break;
        if (v.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{at}: \"{name}\" must be a list");
            yield break;
        }
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                problems.Add($"{at}: every item of \"{name}\" must be a JSON object");
            else
                yield return item;
        }
    }
}
