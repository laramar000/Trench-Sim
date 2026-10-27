using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Core;

// Line-of-sight stage L4: how the acting model sees each enemy, for the small icon drawn above it in the target modes.
// Clear, Partial or Blocked is the rules' own answer (Cover.Of, the function the server and the previews use), and so
// is the shield for cover. Nothing here decides anything: Shoot, Charge and Fight are still checked by the server.
public enum SightMark
{
    Clear,
    Partial,
    Blocked,
}

public sealed record TargetSight(ModelId Model, SightMark Mark, bool Cover, int Visible, int Total);

public static class TargetSights
{
    // Every enemy on the battlefield (Down ones included, Out of Action ones are off it), in id order, as seen by the
    // actor from where it stands, or from `from` (stage L6: a hovered destination) at the height the battlefield
    // gives that point. Empty when the actor is not on the battlefield.
    public static IReadOnlyList<TargetSight> Of(MatchState state, ModelId actor, Point2? from = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Setup?.Scenario is not { } scenario || state.FindModel(actor) is not { } me || (from ?? me.Position) is not { } at
            || BodyAt(state, me, at) is not { } eye)
        {
            return [];
        }
        var sights = new List<TargetSight>();
        foreach (var enemy in state.Models.Where(m => m.Seat != me.Seat && m.Position is not null).OrderBy(m => m.Id.Value, StringComparer.Ordinal))
        {
            var cover = Cover.Of(scenario.Battlefield, eye, BodyAt(state, enemy, enemy.Position!.Value)!);
            var mark = cover.Sight.Visibility switch
            {
                Visibility.Full => SightMark.Clear,
                Visibility.Partial => SightMark.Partial,
                _ => SightMark.Blocked,
            };
            sights.Add(new TargetSight(enemy.Id, mark, cover.InCover, cover.Sight.Visible, cover.Sight.Total));
        }
        return sights;
    }

    // At the elevation the battlefield gives the point, as the rules' Board.BodyAt does.
    private static ModelBody? BodyAt(MatchState state, ModelState model, Point2 at) =>
        state.Setup!.ProfileOf(model.Id) is { } profile
            ? new ModelBody(model.Id.Value, at, profile.BaseRadius, state.Setup.Scenario is { } scenario ? Towers.ElevationAt(scenario.Battlefield, at) : Length.Zero)
            : null;

    // What the icon says in words, for the panel and the UI check: "seen", "partly seen" or "hidden", and "in cover".
    public static string Words(TargetSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        var seen = sight.Mark switch
        {
            SightMark.Clear => "seen",
            SightMark.Partial => "partly seen",
            _ => "hidden",
        };
        return sight.Cover ? $"{seen}, in cover" : seen;
    }

    // Visual pass Stage 2 (2026-10-27): the short caption under the eye and shield of the target whose shot guide is
    // drawn: "Seen · No cover", "Partly seen · In cover", "Hidden". Visibility and cover stay two separate words, as the
    // rules keep them; a hidden target cannot be shot, so its cover is not given.
    public static string Caption(TargetSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        return sight.Mark switch
        {
            SightMark.Clear => $"Seen · {(sight.Cover ? "In cover" : "No cover")}",
            SightMark.Partial => $"Partly seen · {(sight.Cover ? "In cover" : "No cover")}",
            _ => "Hidden",
        };
    }
}

// The icons, 16 by 16, as text so they are original and need no image files: '#' is the light paint (bone for the eye,
// gold for the shield), 'X' the dark outline (iron), '.' clear. BoardView turns them into textures.
public static class SightIcons
{
    public const int Size = 16;

    public static readonly string[] Eye =
    [
        "................",
        "................",
        "................",
        "....XXXXXXXX....",
        "..XX########XX..",
        ".X####XXXX####X.",
        "X####XXXXXX####X",
        "X###XXX##XXX###X",
        "X####XXXXXX####X",
        ".X####XXXX####X.",
        "..XX########XX..",
        "....XXXXXXXX....",
        "................",
        "................",
        "................",
        "................",
    ];

    // The upper lid closed: part of the target is hidden.
    public static readonly string[] HalfEye =
    [
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "XXXXXXXXXXXXXXXX",
        "X###XXX##XXX###X",
        "X####XXXXXX####X",
        ".X####XXXX####X.",
        "..XX########XX..",
        "....XXXXXXXX....",
        "................",
        "................",
        "................",
        "................",
    ];

    // The eye struck through: hidden.
    public static readonly string[] StruckEye = Struck(Eye);

    public static readonly string[] Shield =
    [
        "................",
        "..XXXXXXXXXXXX..",
        "..X##########X..",
        "..X####XX####X..",
        "..X####XX####X..",
        "..X##XXXXXX##X..",
        "..X####XX####X..",
        "...X###XX###X...",
        "...X########X...",
        "....X######X....",
        ".....X####X.....",
        "......XXXX......",
        "................",
        "................",
        "................",
        "................",
    ];

    public static string[] For(SightMark mark) => mark switch
    {
        SightMark.Clear => Eye,
        SightMark.Partial => HalfEye,
        _ => StruckEye,
    };

    // A dark stroke from the lower left to the upper right, with a light edge so it reads on the dark outline.
    private static string[] Struck(string[] icon)
    {
        var rows = icon.Select(r => r.ToCharArray()).ToArray();
        for (var i = 1; i < Size - 1; i++)
        {
            var y = Size - 1 - i;
            rows[y][i] = 'X';
            rows[y][i + 1 < Size ? i + 1 : i] = 'X';
            if (y - 1 >= 0 && rows[y - 1][i] == '.')
            {
                rows[y - 1][i] = '#';
            }
        }
        return [.. rows.Select(r => new string(r))];
    }
}
