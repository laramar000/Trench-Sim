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
