using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Core;

// One action the player may try. A hint, never a promise: the server accepts or refuses every command (D3).
public sealed record ActionHint(string Label, Command Command);

// What the player's seat can try now, from the read-only rules query TurnSequence.LegalCommands over the server's
// state (D3). These hints are the choices that need no position on the table; placing, moving, shooting, charging
// and fighting are previewed on the battlefield (M7.4 stage 4b) and sent from it in stage 4c.
public static class ActionHints
{
    public static IReadOnlyList<ActionHint> For(MatchState state, Seat? seat, ContentNames names)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(names);
        if (seat is not { } mine || state.Phase is Phase.None or Phase.Ended)
        {
            return [];
        }
        return TurnSequence.LegalCommands(state)
            .Where(c => CommandSeats.Of(c) == mine)
            .Select(c => new ActionHint(Label(c, state, names), c))
            .ToList();
    }

    // When it is this seat's turn to do something the M7.3 screen cannot offer yet, says so; otherwise null.
    public static string? NotYetOffered(MatchState state, Seat? seat)
    {
        if (seat is not { } mine || state.Pending is not null || state.Outcome is not null)
        {
            return null;
        }
        if (state.Phase == Phase.Deployment && state.Setup?.Scenario is not null && MatchView.NextDeployer(state) == mine)
        {
            return "You can preview placing your models on the battlefield (key L there). Sending a placement from it arrives in stage 4c; use trench-cli to deploy for now.";
        }
        if (state.Phase == Phase.Activation && state.ActiveModel is { } active && state.FindModel(active)?.Seat == mine)
        {
            return "You can preview Move, Dash, Retreat, Shoot, Charge and Fight on the battlefield (keys M, D, R, S, C and X there). Sending them from it arrives in stage 4c; use trench-cli for them for now.";
        }
        return null;
    }

    public static string Label(Command command, MatchState state, ContentNames names) => command switch
    {
        ChooseDeploymentZone z => $"Take the {z.ZoneId} deployment zone",
        DeployModel d => $"Deploy {names.Of(d.Model)}",
        ChooseFirstActivation f => f.First == f.Seat ? "Activate first yourself" : "Let the opponent activate first",
        ChooseMoraleOrder m => m.First == m.Seat ? "Take your Morale Check first" : "Let the opponent take its Morale Check first",
        ResolveFailedMorale r => $"Failed Morale Check: {Words.MoraleChoice(r.Choice)}",
        ActivateModel a => $"Activate {names.Of(a.Model)}",
        EndActivation => state.ActiveModel is { } active ? $"End {names.Of(active)}'s activation" : "End the activation",
        SpendMarkers s => s.Count == 0 ? "Spend no Blood Markers" : $"Spend {s.Count} Blood Marker{(s.Count == 1 ? "" : "s")}",
        FreeFight f => $"Free attack with {names.Of(f.Attacker)}",
        DeclineFreeFights => "Decline the remaining free attacks",
        ChooseFriendlyTarget t => $"The shot hits your own {names.Of(t.Victim)}",
        _ => command.GetType().Name,
    };
}
