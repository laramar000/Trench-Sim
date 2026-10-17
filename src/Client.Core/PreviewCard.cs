namespace Client.Core;

// QoL stage B: the few lines drawn on the battlefield with a preview, at the end of the path or at the target. Built
// only from the preview, whose facts come from the rules' own geometry; it adds no rule and no odds. The first line
// always says it is a preview and that the server decides; what the dice decide is said to be the server's.
public static class PreviewCard
{
    public static IReadOnlyList<string> Lines(Preview preview, ModelShape? target = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var lines = new List<string> { preview.Tag };
        switch (preview.Kind)
        {
            case PreviewKind.Place:
                lines.Add($"{preview.Model.Value} at {preview.Path[0].At}");
                break;
            case PreviewKind.Move or PreviewKind.Dash or PreviewKind.Retreat:
                if (preview.Used is { } used)
                {
                    lines.Add(preview.Allowance is { } allowance
                        ? $"{BoardScene.Inch(used)} used of {BoardScene.Inch(allowance)} ({BoardScene.Inch(preview.Left!.Value)} left)"
                        : $"{BoardScene.Inch(used)} used");
                }
                if (preview.Straight is { } straight)
                {
                    lines.Add($"straight line {BoardScene.Inch(straight)}");
                }
                if (preview.Kind == PreviewKind.Dash)
                {
                    lines.Add("RISKY: the server rolls whether it moves");
                }
                if (preview.Kind == PreviewKind.Retreat)
                {
                    lines.Add("enemies within 1\" may attack first (server)");
                }
                break;
            default:
                if (target is not null)
                {
                    lines.Add($"target {target.Label}");
                }
                foreach (var fact in preview.Facts)
                {
                    // The rules' facts about the target, in their own words: distance and range, sight, cover, long
                    // range, shooting into melee, charge reach.
                    if (fact.StartsWith("Distance", StringComparison.Ordinal) || fact.StartsWith("Line of sight", StringComparison.Ordinal)
                        || fact.StartsWith("Target ", StringComparison.Ordinal) || fact.StartsWith("Long range", StringComparison.Ordinal)
                        || fact.StartsWith("Short range", StringComparison.Ordinal) || fact.StartsWith("Shooting into melee", StringComparison.Ordinal))
                    {
                        lines.Add(fact.TrimEnd('.'));
                    }
                }
                if (preview.Legal)
                {
                    if (preview.Dice is { } dice && preview.Target is { } aimed)
                    {
                        lines.AddRange(dice.Lines(preview.Model, aimed));
                    }
                    lines.Add("dice: rolled by the server");
                }
                break;
        }
        if (preview.Refusal is { } r)
        {
            lines.Add($"{r.Text} ({r.RuleRef})");
        }
        return lines;
    }
}
