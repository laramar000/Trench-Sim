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
    InvalidChoice,
    // M4
    PositionRequired,
    NoBattlefield,
    PlacementIllegal,
    TooManyWaypoints,
    MoveIllegal,
    ActionAlreadyTaken,
    ModelNotStanding,
    ModelNotOnTable,
    NoRangedWeapon,
    TargetNotEnemy,
    TargetNotOnTable,
    AttackerEngaged,
    TargetOutOfRange,
    NoLineOfSight,
    InvalidMarkerCount,
    // M4 Slice B
    AlreadyEngaged,
    NotEngaged,
    NoMeleeWeapon,
    TargetNotInMelee,
    ChargeRouteBlocked,
    RetreatEndsEngaged,
    ActionExcluded,
    NotAFreeFighter,
    NotACandidate,
    // M4 reducer hardening
    UnknownCommand,
    // Trench Warfare's Mine Markers (p. 162)
    MinesIllegal,
}

// Why a command was refused, in a form a player can read, with the rulebook reference behind it.
public sealed record Rejection(ReasonCode Code, string Text, string RuleRef);
