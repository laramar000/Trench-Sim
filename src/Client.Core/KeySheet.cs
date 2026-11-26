namespace Client.Core;

// QoL I1: every key and mouse use of a match on one sheet (? opens it), grouped by purpose. Stage K1 (battlefield UI
// proposal): each entry is its keys apart from what they do, so the sheet can draw the keys as bold key caps. The Menu's
// "Battlefield keys" text is built from the same list, so the two cannot differ. `KeySheetTests` list the keys it must name.
public static class KeySheet
{
    public const string Title = "Keys and mouse";

    // One line of the sheet: the keys (each drawn as its own key cap) and what they do.
    public sealed record Entry(IReadOnlyList<string> Keys, string What);

    private static Entry E(string what, params string[] keys) => new(keys, what);

    public static readonly IReadOnlyList<(string Group, IReadOnlyList<Entry> Entries)> Groups =
    [
        ("Camera", [
            E("pan", "Arrows", "Right-drag"),
            E("zoom (the wheel: towards the pointer)", "+", "-", "Wheel"),
            E("turn 90°", "Q", "E"),
            E("top-down or angled", "T"),
            E("fit the table", "F"),
            E("reset the view", "Shift+F"),
            E("centre on the selected unit", "Home"),
            E("centre on it; again: zoom in", "Double-click"),
        ]),
        ("Selecting", [
            E("select a unit, target or waypoint", "Click"),
            E("point out its models", "Click a log row"),
            E("next and previous model", "N", "P"),
            E("next and previous unit that can activate", "J", "Shift+J"),
            E("next and previous objective", "O", "Shift+O"),
            E("activate the selected unit", "A"),
        ]),
        ("Actions", [
            E("place a model", "L"),
            E("move", "M"),
            E("dash", "D"),
            E("retreat", "R"),
            E("combat: choose shoot, charge or fight, then the enemy", "C"),
            E("shoot", "S"),
            E("fight", "X"),
        ]),
        ("Moving", [
            E("move the cursor (with Shift: 2\")", "Arrows"),
            E("fix a waypoint", "Enter", "Shift+click"),
            E("undo the last waypoint", "Backspace"),
            E("towers: climb a side, not a ladder (a Risky roll); again: ladders", "K"),
        ]),
        ("Targets (combat, shoot, fight)", [
            E("next and previous target", "N", "P"),
            E("in combat, first: shoot, charge, fight", "1", "2", "3"),
            E("send the attack shown (beside the target)", "Confirm"),
        ]),
        ("Confirm and cancel", [
            E("send", "Space", "Confirm"),
            E("cancel, or back to the combat menu (sends nothing)", "Esc", "Right-click"),
            E("keep an activation that is ending by itself", "Esc"),
        ]),
        ("Window", [
            E("rules in this build", "F1"),
            E("switch seat (hot-seat)", "F2"),
            E("this sheet (Esc closes it)", "?"),
            E("the scenario card (scoring, objectives now)", "G"),
            E("open or close the unit card (state, Blood Markers, profile)", "U"),
        ]),
    ];

    // Every key named on the sheet, once each.
    public static IEnumerable<string> Keys => Groups.SelectMany(g => g.Entries).SelectMany(e => e.Keys).Distinct(StringComparer.Ordinal);

    // An entry's keys as one plain phrase: "Q / E".
    public static string KeysText(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.Join(" / ", entry.Keys);
    }

    // The plain-text form (the Menu's "Battlefield keys"): each group in capitals, then "  Q / E: turn 90°".
    public static IEnumerable<string> Lines()
    {
        foreach (var (group, entries) in Groups)
        {
            yield return group.ToUpperInvariant();
            foreach (var entry in entries)
            {
                yield return $"  {KeysText(entry)}: {entry.What}";
            }
        }
    }

    public static string Text => string.Join("\n", Lines());
}
