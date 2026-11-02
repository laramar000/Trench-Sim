namespace Client.Core;

// What this build of the rules does not do yet (M7 design §10), shown to playtesters from the match screen.
public static class RulesGaps
{
    public const string Title = "Rules in this build";

    public static IReadOnlyList<(string Gap, string Effect)> All { get; } =
    [
        ("Some Glorious Deeds are never completed: Cast Them Down, Suicidal Bravery and Sniper (U96).", "There is no falling, a charge has one target and no model has ELITE."),
        ("Bloodbath Rolls are not offered.", "Players cannot spend 6 or 3 Blood Markers that way."),
        ("No jumping or falling; climbing only up tower sides and ladders. Towers are under 3\" high (U69).", "The +1 DICE for height never applies."),
        ("Dangerous terrain has no effect.", "Moving through it is safe."),
        ("Many unit abilities and some keywords (FLYING, INFILTRATOR, SKIRMISHER...) are named, not played; other equipment is refused.", "A roster with refused equipment cannot start a match."),
        ("Two scenarios: Claim No Man's Land and Trench Warfare (no halved ducats for the defender, no Infiltrators).", "Each is played on the maps made for it."),
        ("Charge routes are approximated (U57 to U65).", "Rarely, a charge the planner cannot find is refused with a reason."),
        ("Provisional rulings U31, U36, U40 and U32; open questions U50, U52, Q5 and Q6.", "Please note anything that looks wrong for playtest feedback."),
    ];

    public static string Text => Title + "\n" + string.Join("\n", All.Select(g => $"- {g.Gap} {g.Effect}"));
}
