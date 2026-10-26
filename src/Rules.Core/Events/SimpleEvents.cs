using System.Collections.Immutable;
using Rules.Core.Dice;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Events;

// A choice is now waiting on one player.
public sealed record DecisionRequested(DecisionKind Kind, Seat Seat) : GameEvent;

// The roll-off winner chose a deployment zone; the other zone goes to the opponent (p. 152).
public sealed record DeploymentZoneChosen(Seat Seat, string ZoneId) : GameEvent;

// Who deploys first: the seat with more models, or the roll-off winner when equal (p. 152).
public sealed record DeploymentOrderSet(Seat First) : GameEvent;

// Position is set exactly when the match has a battlefield (M4): the model's base is centred there.
public sealed record ModelDeployed(Seat Seat, ModelId Model, Point2? Position = null) : GameEvent;

public sealed record PhaseEntered(Phase Phase) : GameEvent;

// A new turn begins in the Initiative Phase: activation marks, Initiative and Morale progress are cleared.
public sealed record TurnStarted(int Turn) : GameEvent;

// Standing counts as counted under the match's rules profile, and who holds the Initiative (p. 32).
public sealed record InitiativeDetermined(int CountSeatOne, int CountSeatTwo, Seat Holder) : GameEvent;

public sealed record FirstActivationChosen(Seat FirstActivator) : GameEvent;

public sealed record ActivationStarted(Seat Seat, ModelId Model) : GameEvent;

public enum ActivationEndReason
{
    Voluntary,
    RiskyFailure,
    TakenDown,
}

public sealed record ActivationEnded(ModelId Model, ActivationEndReason Reason) : GameEvent;

// First = which seat's Morale Check is made first; Chosen = the Initiative holder picked it (R5).
public sealed record MoraleOrderSet(Seat First, bool Chosen) : GameEvent;

// The roll is an ordinary Success Roll with no modifiers (provisional rule R3), kept in full.
public sealed record MoraleCheckRolled : GameEvent
{
    public MoraleCheckRolled(Seat seat, SuccessRollResult check, int modelsDownOrOut, int warbandSize, bool wasShaken)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (warbandSize < 1 || modelsDownOrOut < 0 || modelsDownOrOut > warbandSize)
        {
            throw new ArgumentOutOfRangeException(nameof(modelsDownOrOut), "Counts do not fit the warband size.");
        }
        Seat = seat;
        Check = check;
        ModelsDownOrOut = modelsDownOrOut;
        WarbandSize = warbandSize;
        WasShaken = wasShaken;
    }

    public Seat Seat { get; }
    public SuccessRollResult Check { get; }
    public int ModelsDownOrOut { get; }
    public int WarbandSize { get; }
    public bool WasShaken { get; }
}

public sealed record WarbandShaken(Seat Seat) : GameEvent;

public sealed record WarbandRecovered(Seat Seat) : GameEvent;

public enum FledReason
{
    FailedWhileShaken,
    SoundedRetreat,
}

public sealed record WarbandFled(Seat Seat, FledReason Reason) : GameEvent;

public sealed record TurnEnded(int Turn) : GameEvent;

public sealed record MatchEnded(Seat? Winner, OutcomeReason Reason) : GameEvent;

// A Glorious Deed was completed (pp. 149-150, U96): 1 Victory Point to that seat. It comes right after the event that
// completed it, which the reducer has already worked out; Model is null for a warband's deed (Hold Your Ground).
public sealed record GloriousDeedCompleted(Seat Seat, GloriousDeed Deed, ModelId? Model) : GameEvent;
