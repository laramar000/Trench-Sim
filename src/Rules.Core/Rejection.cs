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
    // Roster stage R3: a Fight's next attack is with another weapon
    WrongWeapon,
    // Roster stage R5h: an ACTION the model's kit does not give it, and a Treat with nothing to treat
    NoSuchAction,
    NothingToTreat,
    // Roster stage R5i: a Grind to Dust target that is not Down, or on a base larger than 32 mm
    TargetNotDown,
    TargetTooLarge,
    // Roster stage R7c: a hidden model cannot be shot or charged
    TargetHidden,
    // Roster stage R8d: turning an oval base is refused (a round base, the same axis, no room, or it would change who it is engaged with)
    BaseTurnIllegal,
}

// Why a command was refused, in a form a player can read, with the rulebook reference behind it.
public sealed record Rejection(ReasonCode Code, string Text, string RuleRef);
