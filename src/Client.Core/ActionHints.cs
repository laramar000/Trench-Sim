using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Core;

// One action the player may try. A hint, never a promise: the server accepts or refuses every command (D3).
public sealed record ActionHint(string Label, Command Command);

// What the player's seat can try now, from the read-only rules query TurnSequence.LegalCommands over the server's
// state (D3). These hints are the choices that need no position on the table; placing, moving, shooting, charging
// and fighting are previewed and sent on the battlefield (M7.4).
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

    // When it is this seat's turn to do something that is done on the battlefield rather than with a hint, says how.
    public static string? NotYetOffered(MatchState state, Seat? seat)
    {
        if (seat is not { } mine || state.Pending is not null || state.Outcome is not null)
        {
            return null;
        }
        if (state.Phase == Phase.Deployment && state.Setup?.Scenario is not null && MatchView.NextDeployer(state) == mine)
        {
            return "Place your models on the battlefield: click it, press L, point with the arrows or the mouse, then Space sends the placement. N and P choose the model.";
        }
        if (state.Phase == Phase.Activation && state.ActiveModel is { } active && state.FindModel(active)?.Seat == mine)
        {
            return "Move, Dash, Retreat, Shoot, Charge and Fight are on the battlefield: click it, press M, D, R, S, C or X, choose the path or target, then Space sends it. End the activation with its button.";
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
