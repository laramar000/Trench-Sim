using Rules.Core.Scenarios;
using Rules.Core.State;

namespace Rules.Core;

// Read-only, for the client (proposal J, stage J3a, approved by Jason on 2026-10-20): what the scenario being played
// says about scoring and how the match ends, in words, built from the numbers the rules use (Scoring, the setup's turn
// limit and objective ids, Reducer.TerminalReason's three endings). Only scenarios the rules implement have one; any
// other gives null, so the client never shows a made-up or generic text. Nothing here changes a rule or a state.
public sealed record ScenarioRules(
    string Id,
    string Name,
    string Pages,
    IReadOnlyList<string> ObjectiveIds,
    int PointsPerObjective,
    int TurnLimit,
    IReadOnlyList<string> Scoring,
    IReadOnlyList<string> MatchEnds,
    IReadOnlyList<string> Provisional)
{
    public static ScenarioRules? Of(MatchSetup? setup)
    {
        if (setup?.Scenario is not { } scenario || setup.ScenarioId != ClaimNoMansLand.ScenarioId)
        {
            return null;
        }
        var points = Rules.Core.Scoring.PointsPerObjective;
        return new ScenarioRules(
            ClaimNoMansLand.ScenarioId,
            "Claim No Man's Land (Scenario I)",
            "1.0.2 pp. 151-153",
            [.. scenario.ObjectiveIds],
            points,
            setup.TurnLimit,
            [
                $"At the end of each Turn, each Objective a warband controls scores {points} Victory Points.",
                "A warband controls an Objective when more of its models than enemy models are on, in or within 1\" of the piece; if only one side has models on the piece, that side controls it. Down models count; Out of Action models do not.",
            ],
            [
                $"After Turn {setup.TurnLimit}: the warband with more Victory Points wins; equal points are a draw.",
                "A warband with no model left on the battlefield loses at once.",
                "A warband that flees (a failed Morale test while Shaken, or choosing to Sound the Retreat) loses at once (p. 50).",
            ],
            [
                "The table, deployment zones and terrain are a fixed layout: the book does not print them (U32).",
                "What counts as \"on\" an Objective is an interpretation (U36).",
                "Equal Victory Points being a draw is an interpretation: the book is silent on a tie (U40).",
            ]);
    }
}
