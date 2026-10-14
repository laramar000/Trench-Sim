namespace Rules.Core;

public enum ReasonCode
{
    MatchNotCreated,
    MatchNotStarted,
    MatchEnded,
    WrongPhase,
    NotYourTurn,
    DecisionPending,
    NoDecisionPending,
    WrongDecision,
    UnknownModel,
    NotYourModel,
    ModelOutOfAction,
    ModelAlreadyActivated,
    ModelAlreadyDeployed,
    ActivationInProgress,
    NoActivationInProgress,
    UnknownZone,
}

// Why a command was refused, in a form a player can read, with the rulebook reference behind it.
public sealed record Rejection(ReasonCode Code, string Text, string RuleRef);
