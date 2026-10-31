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
        // The Mine Markers are set up on the battlefield (MineLaying), not from the rules' one suggested layout.
        return TurnSequence.LegalCommands(state)
            .Where(c => CommandSeats.Of(c) == mine && c is not LayMines)
            .Select(c => new ActionHint(Label(c, state, names), c))
            .ToList();
    }

    // When it is this seat's turn to do something that is done on the battlefield rather than with a hint, says how.
    public static string? NotYetOffered(MatchState state, Seat? seat)
    {
        if (seat is { } layer && state.Pending is { Kind: DecisionKind.LayMines } lay && lay.Seat == layer)
        {
            return "Set up your 12 Mine Markers on the battlefield: click in the shaded strip in front of your zone to place one, click a marker to make it a dud (4 of them; only you see which), then Lay the mines on the card. Or let the card fill them in at random.";
        }
        if (seat is not { } mine || state.Pending is not null || state.Outcome is not null)
        {
            return null;
        }
        if (state.Phase == Phase.Deployment && state.Setup?.Scenario is not null && MatchView.NextDeployer(state) == mine)
        {
            return "Place your models on the battlefield: click a spot in your zone; the bar shows which model is next, and a click on another chip (or N and P) chooses it. (Keys: L, arrows, Space.)";
        }
        if (state.Phase == Phase.Activation && state.ActiveModel is { } active && state.FindModel(active)?.Seat == mine)
        {
            return "Your activating model's actions are on its bar on the battlefield: click one, then click where to go (it moves at once), or click a target and then Confirm beside it. (Keys: M, D, R, S, C, X, then Space.) End the activation on the bar; when nothing is left it ends by itself.";
        }
        if (state.Phase == Phase.Activation && state.ActiveModel is null && state.Setup?.Scenario is not null && TurnSequence.NextActivator(state) == mine)
        {
            return "Click one of your units on the battlefield to choose it and see its actions. Nothing is sent until its first action, so you can click another unit to switch. (An Activate choice here starts an activation at once.)";
        }
        return null;
    }

    // Battlefield UI 2, stage N3: the choices that belong to no model on the battlefield (the deployment zone, who
    // activates or takes the Morale Check first, a failed Morale Check, a deployment without a battlefield, starting the
    // match). They are shown on a card over the battlefield; the others are on the battlefield's bars, next to a model.
    public static bool IsDecision(Command command) =>
        command is StartMatch or ChooseDeploymentZone or ChooseAttacker or ChooseFirstActivation or ChooseMoraleOrder or ResolveFailedMorale or DeployModel;

    // The card's heading for those choices.
    public static string DecisionTitle(IEnumerable<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        return commands.FirstOrDefault(IsDecision) switch
        {
            ChooseDeploymentZone => "YOUR CHOICE: which deployment zone you take",
            ChooseAttacker => "YOUR CHOICE: who attacks the trenches",
            ChooseFirstActivation => "YOUR CHOICE: who activates first",
            ChooseMoraleOrder => "YOUR CHOICE: who takes the Morale Check first",
            ResolveFailedMorale => "YOUR CHOICE: your warband failed its Morale Check",
            DeployModel => "YOUR CHOICE: deploy a model",
            StartMatch => "YOUR CHOICE: start the match",
            _ => "YOUR CHOICE",
        };
    }

    public static string Label(Command command, MatchState state, ContentNames names) => command switch
    {
        ChooseDeploymentZone z => $"Take the {z.ZoneId} deployment zone",
        ChooseAttacker a => a.Attacker == a.Seat ? "Attack: the opponent defends the trenches" : "Defend the trenches: the opponent attacks",
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
