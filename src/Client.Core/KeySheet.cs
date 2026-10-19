namespace Client.Core;

// QoL I1: every key and mouse use of a match on one sheet (? opens it), grouped. The Menu's "Battlefield keys" text
// is built from the same list, so the two cannot differ. No key here is new; `KeySheetTests` list the keys it must name.
public static class KeySheet
{
    public const string Title = "Keys and mouse";

    public static readonly IReadOnlyList<(string Group, IReadOnlyList<string> Lines)> Groups =
    [
        ("Camera", [
            "Arrows: pan · + and -: zoom · wheel: zoom towards the pointer · right-drag: pan",
            "Q and E: turn 90° · T: top-down or angled",
            "F: fit the table · Shift+F: reset the view · Home: centre on the selected unit",
            "Double-click a unit or objective: centre on it; again: zoom in",
        ]),
        ("Choosing", [
            "Click: select a unit (or pick the target or a waypoint) · click a combat-log row: point out its models",
            "N and P: next and previous model · J and Shift+J: next and previous unit that can activate",
            "O and Shift+O: next and previous objective · A: activate the selected unit",
        ]),
        ("Actions", [
            "L place · M move · D dash · R retreat · S shoot · C charge · X fight",
            "Moving: arrows move the cursor (Shift: 2\"), Enter or Shift+click fixes a waypoint",
            "Shoot, Charge, Fight: N and P next and previous target",
        ]),
        ("Confirm and cancel", [
            "Space or Confirm: send · Backspace: undo the last waypoint",
            "Esc or right-click: cancel (sends nothing)",
        ]),
        ("Window", [
            "F1: rules in this build · F2: switch seat (hot-seat) · ?: this sheet (Esc closes it)",
        ]),
    ];

    public static IEnumerable<string> Lines()
    {
        foreach (var (group, lines) in Groups)
        {
            yield return group.ToUpperInvariant();
            foreach (var line in lines)
            {
                yield return "  " + line;
            }
        }
    }

    public static string Text => string.Join("\n", Lines());
}
