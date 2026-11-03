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
        DecisionKind.ChooseScatterPoint => "choose where the missed attack scatters to",
        DecisionKind.UseTalisman => "use a Talisman so the activation goes on, or let it end",
        DecisionKind.CarryOnCharge => "carry the charge on round the target to within 1\" of another enemy, or stop in contact",
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
        var done = Enum.GetValues<ActionsTaken>().Where(a => a != ActionsTaken.None && taken.HasFlag(a)).Select(a => a switch
        {
            // Roster stage R6e (U121): the two names that are not one word.
            ActionsTaken.Orthodoxy => "Enforced Orthodoxy",
            ActionsTaken.LayingOnHands => "Laying on of Hands",
            _ => a.ToString(),
        }).ToList();
        return done.Count == 0 ? "nothing yet" : string.Join(", ", done);
    }

    // Roster stage R6b/R6e (U118, U121): the ACTIONS of TakeSelfAction, for the log, the dice popup and the notes.
    public static string SelfAction(SelfActionKind action) => action switch
    {
        SelfActionKind.Aim => "Aim",
        SelfActionKind.Fortify => "Fortify",
        SelfActionKind.EnforcedOrthodoxy => "Enforced Orthodoxy",
        _ => "Laying on of Hands",
    };

    public static string SelfActionWithArticle(SelfActionKind action) => action switch
    {
        SelfActionKind.Aim => "an Aim",
        SelfActionKind.EnforcedOrthodoxy => "an Enforced Orthodoxy",
        _ => $"a {SelfAction(action)}",
    };

    public static string SelfActionVerb(SelfActionKind action) => action switch
    {
        SelfActionKind.Aim => "AIMS",
        SelfActionKind.Fortify => "FORTIFIES",
        SelfActionKind.EnforcedOrthodoxy => "ENFORCES ORTHODOXY",
        _ => "LAYS ON HANDS",
    };

    public static (string Pass, string Fail) SelfActionVerdicts(SelfActionKind action) => action switch
    {
        SelfActionKind.Aim => ("AIMED", "AIM FAILED"),
        SelfActionKind.Fortify => ("FORTIFIED", "FORTIFY FAILED"),
        SelfActionKind.EnforcedOrthodoxy => ("ORTHODOXY ENFORCED", "ORTHODOXY FAILED"),
        _ => ("HANDS LAID", "HANDS FAILED"),
    };

    // What a successful roll did.
    public static string SelfActionResult(Rules.Core.Events.SelfActionRolled rolled) => rolled.Action switch
    {
        SelfActionKind.Aim => "its Ranged Attacks get +2 DICE this activation",
        SelfActionKind.Fortify => "it has COVER until it moves",
        SelfActionKind.EnforcedOrthodoxy => rolled.StoodUp.IsEmpty ? "no Down friend within 8\" to stand up" : $"{string.Join(", ", rolled.StoodUp)} stand{(rolled.StoodUp.Length == 1 ? "s" : "")} up",
        _ => rolled.Removed == 0 ? $"{rolled.Target} has no BLOOD MARKER left to remove" : $"{rolled.Removed} BLOOD MARKER{(rolled.Removed == 1 ? "" : "S")} removed from {rolled.Target}",
    };
}
