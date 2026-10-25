using System.Collections.Immutable;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Commands;

// A player's (or the host's) intent. A command is only a request: TurnSequence.Decide checks it
// against the state and either rejects it or turns it into events.
public abstract record Command;

public sealed record StartMatch : Command;

public sealed record ChooseDeploymentZone(Seat Seat, string ZoneId) : Command;

public sealed record DeployModel(Seat Seat, ModelId Model) : Command;

public sealed record ChooseFirstActivation(Seat Seat, Seat First) : Command;

public sealed record ActivateModel(Seat Seat, ModelId Model) : Command;

public sealed record EndActivation(Seat Seat) : Command;

public enum MoraleFailureChoice
{
    BecomeShaken,
    SoundRetreat,
}

public sealed record ResolveFailedMorale(Seat Seat, MoraleFailureChoice Choice) : Command;

public sealed record ChooseMoraleOrder(Seat Seat, Seat First) : Command;

// Trench Warfare (p. 161): the roles roll-off winner chooses which seat attacks; the other defends.
public sealed record ChooseAttacker(Seat Seat, Seat Attacker) : Command;

// ---- M4: commands that need a battlefield (a MatchSetup with a Scenario) ----

// Sets a model up at a point inside its deployment zone (Scenario I, p. 152). Replaces DeployModel when there is a battlefield.
public sealed record PlaceModel(Seat Seat, ModelId Model, Point2 At) : Command;

// A Move ACTION (p. 34): the active model's centre follows the waypoints in order. At most MaxWaypoints of them.
public sealed record MoveModel(Seat Seat, ModelId Model, ImmutableArray<Point2> Path) : Command;

// A Dash ACTION: a Risky Success Roll first, then a move like an ordinary Move (p. 34).
public sealed record DashModel(Seat Seat, ModelId Model, ImmutableArray<Point2> Path) : Command;

// A Shoot ACTION: a Ranged Attack by the active model against one enemy model (pp. 42-44).
public sealed record Shoot(Seat Seat, ModelId Attacker, ModelId Target) : Command;

// Answers a pending Blood Marker decision (p. 28): spend this many markers, or none (Count 0).
public sealed record SpendMarkers(Seat Seat, int Count) : Command;

// ---- M4 Slice B: Charge, Retreat, Fight and the choices around them ----

// A Charge ACTION (pp. 34, 36-37): declare the target; the Charge Bonus D6 and the charge move follow.
public sealed record ChargeModel(Seat Seat, ModelId Model, ModelId Target) : Command;

// A Retreat ACTION (pp. 35, 37): the path is fixed now, the move happens after the opponent's free attacks.
public sealed record RetreatModel(Seat Seat, ModelId Model, ImmutableArray<Point2> Path) : Command;

// A Fight ACTION (pp. 34, 45-46): one Melee Attack with the model's Melee Weapon against one enemy within 1".
public sealed record FightModel(Seat Seat, ModelId Model, ModelId Target) : Command;

// The opponent of a retreating model attacks it with one engaged model (p. 37). Answers the FreeFight decision.
public sealed record FreeFight(Seat Seat, ModelId Attacker) : Command;

// The opponent declines the remaining free attacks (p. 37). Answers the FreeFight decision.
public sealed record DeclineFreeFights(Seat Seat) : Command;

// A shot into melee rolled 1-3: the shooter picks which of its own models within 1" of the target is shot (p. 42).
public sealed record ChooseFriendlyTarget(Seat Seat, ModelId Victim) : Command;
