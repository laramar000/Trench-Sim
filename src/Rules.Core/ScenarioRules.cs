using Rules.Core.Geometry;
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
        if (setup?.Scenario is { } trenches && TrenchWarfare.Is(setup))
        {
            return TrenchWarfareRules(setup, trenches);
        }
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

    // Trench-maps stage S6: Scenario IV.
    private static ScenarioRules TrenchWarfareRules(MatchSetup setup, Scenario scenario)
    {
        var sections = TrenchWarfare.ScoringSections(scenario.Battlefield);
        return new ScenarioRules(
            TrenchWarfare.ScenarioId,
            "Trench Warfare (Scenario IV)",
            "1.0.2 pp. 161-163",
            [],
            0,
            setup.TurnLimit,
            [
                $"At the end of each Turn, each trench section at least {TrenchWarfare.ScoringLength} long ({string.Join(", ", sections.Select(t => t.Id))}) scores:",
                $"the defender {TrenchWarfare.DefenderHeld} VP if a defending model is in it, {TrenchWarfare.DefenderAlone} VP if no attacking model is;",
                $"the attacker {TrenchWarfare.AttackerHeld} VP if an attacking model is in it, {TrenchWarfare.AttackerAlone} VP if no defending model is.",
                "A model is in a trench when more than half its base is in the trench network (U81). Down models count; Out of Action models do not.",
            ],
            [
                $"At the end of Turn {TrenchWarfare.LengthRollTurn} the attacker rolls a D6: on 1-{TrenchWarfare.EndsOnOrBelow} the game ends, otherwise it ends after Turn {TrenchWarfare.TurnLimit}. The warband with more Victory Points wins; equal points are a draw.",
                "A warband with no model left on the battlefield loses at once.",
                "A warband that flees (a failed Morale test while Shaken, or choosing to Sound the Retreat) loses at once (p. 50).",
            ],
            [
                "A roll-off decides the roles: its winner chooses who attacks (a one-off game, p. 161). The attacker deploys first, then the players alternate (p. 162).",
                "The defender takes the deployment zone with the map's trench lines and the attacker the other: the zone roll-off is not played (U88).",
                "A trench section's length is the longer side of the rectangle around it (U88).",
                $"Before deployment the defender sets up {Mines.Count} Mine Markers (1\" discs) in the {Mines.PlacementDepth} strip of its zone facing the attacker, {Mines.Duds} of them secret duds known only to the defender and the server (p. 162, U93).",
                $"A model whose base comes within {Mines.TriggerRange} of a marker halts there and the marker is revealed: a live mine makes an Injury Roll (no Blood Markers) and adds 1 SHRAPNEL marker; a dud does nothing. Then the move goes on unless the model is taken Down.",
                "Not built yet: Glorious Deeds, the defender's halved ducats and Infiltrators.",
                "Equal Victory Points being a draw is an interpretation: the book is silent on a tie (U40).",
            ]);
    }
}
