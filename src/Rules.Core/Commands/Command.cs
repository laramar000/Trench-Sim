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
