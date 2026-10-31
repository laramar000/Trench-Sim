namespace Client.Core;

// What this build of the rules does not do yet (M7 design §10), shown to playtesters from the match screen.
public static class RulesGaps
{
    public const string Title = "Rules in this build";

    public static IReadOnlyList<(string Gap, string Effect)> All { get; } =
    [
        ("Glorious Deeds are not scored; victory points come from Objectives only.", "Can change the winner of a close game."),
        ("Bloodbath Rolls are not offered.", "Players cannot spend 6 or 3 Blood Markers that way."),
        ("No elevation: no +1 DICE for height, climbing, jumping or falling.", "All terrain is effectively ground level for attacks."),
        ("Dangerous terrain has no effect.", "Moving through it is safe."),
        ("No keywords or equipment; no CRITICAL or DEADLY.", "Only the two invented test warbands are playable."),
        ("One scenario (Claim No Man's Land) on a fixed battlefield (U32).", "Every game uses the same table."),
        ("Charge routes are approximated (U57 to U65).", "Rarely, a charge the planner cannot find is refused with a reason."),
        ("Provisional rulings U31, U36, U40 and U32; open questions U50, U52, Q5 and Q6.", "Please note anything that looks wrong for playtest feedback."),
    ];

    public static string Text => Title + "\n" + string.Join("\n", All.Select(g => $"- {g.Gap} {g.Effect}"));
}
