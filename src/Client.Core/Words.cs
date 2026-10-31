using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Core;

// Plain words for the rules' enums, so the screen never shows a raw identifier where a player needs to read it.
public static class Words
{
    public static string Seat(Seat seat) => seat == Rules.Core.State.Seat.One ? "Seat One" : "Seat Two";

    // A letter that tells the seats apart without colour (accessibility: never colour alone).
    public static string SeatLetter(Seat seat) => seat == Rules.Core.State.Seat.One ? "A" : "B";

    public static string Phase(Phase phase) => phase switch
    {
        Rules.Core.State.Phase.None => "no match yet",
        Rules.Core.State.Phase.PreGame => "before the game",
        Rules.Core.State.Phase.Deployment => "Deployment",
        Rules.Core.State.Phase.Initiative => "Initiative",
        Rules.Core.State.Phase.Activation => "Activation",
        Rules.Core.State.Phase.Morale => "Morale",
        Rules.Core.State.Phase.Ended => "ended",
        _ => phase.ToString(),
    };

    public static string Status(ModelStatus status) => status switch
    {
        ModelStatus.Standing => "Standing",
        ModelStatus.Down => "Down",
        ModelStatus.OutOfAction => "Out of action",
        _ => status.ToString(),
    };

    public static string Decision(DecisionKind kind) => kind switch
    {
        DecisionKind.ChooseDeploymentZone => "choose a deployment zone",
        DecisionKind.ChooseAttacker => "choose who attacks",
        DecisionKind.ChooseFirstActivation => "choose who activates first",
        DecisionKind.ChooseMoraleOrder => "choose which warband takes its Morale Check first",
        DecisionKind.FailedMorale => "choose what a failed Morale Check means (become Shaken, or Sound Retreat)",
        DecisionKind.SpendMarkersOnSuccessRoll => "spend Blood Markers against the Success Roll, or none",
        DecisionKind.SpendMarkersOnInjuryRoll => "spend Blood Markers for the Injury Roll, or none",
        DecisionKind.FreeFight => "attack the retreating model with each engaged model, or decline",
        DecisionKind.ChooseFriendlyTarget => "choose which of its own models the shot hits",
        DecisionKind.LayMines => "set up the 12 Mine Markers and secretly pick 4 duds",
        _ => kind.ToString(),
    };

    public static string Outcome(MatchOutcome outcome, Seat? mine)
    {
        var result = outcome.Winner is not { } winner ? "Draw"
            : mine is null ? $"{Seat(winner)} won"
            : winner == mine ? "You won" : "You lost";
        return $"{result}: {Reason(outcome.Reason)}.";
    }

    // Why the match ended, in words ("victory points after the final turn").
    public static string Reason(OutcomeReason reason) => reason switch
    {
        OutcomeReason.OpponentFled => "a warband fled",
        OutcomeReason.FinalTurnComplete => "the final turn ended",
        OutcomeReason.AllEnemiesOutOfAction => "every enemy model is out of action",
        OutcomeReason.VictoryPoints => "victory points after the final turn",
        _ => reason.ToString(),
    };

    public static string MoraleChoice(MoraleFailureChoice choice) => choice switch
    {
        MoraleFailureChoice.BecomeShaken => "become Shaken",
        MoraleFailureChoice.SoundRetreat => "Sound Retreat (the warband flees)",
        _ => choice.ToString(),
    };

    public static string Actions(ActionsTaken taken)
    {
        var done = Enum.GetValues<ActionsTaken>().Where(a => a != ActionsTaken.None && taken.HasFlag(a)).Select(a => a.ToString()).ToList();
        return done.Count == 0 ? "nothing yet" : string.Join(", ", done);
    }
}
