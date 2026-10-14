using System.Collections.Immutable;
using Rules.Core.Dice;

namespace Rules.Core.State;

public enum DecisionKind
{
    ChooseDeploymentZone,
    ChooseFirstActivation,
    ChooseMoraleOrder,
    FailedMorale,
}

// A choice only one player can make. While one is pending, nothing else may happen.
// Blood Marker spending (M4) will use the same slot.
public sealed record PendingDecision(DecisionKind Kind, Seat Seat);

public enum OutcomeReason
{
    OpponentFled,
    FinalTurnComplete,
}

// Winner is null when the final turn ended and the scenario's scoring (M4) still has to decide.
public sealed record MatchOutcome(Seat? Winner, OutcomeReason Reason);

public sealed record SeatState(
    Seat Seat,
    string? ZoneId = null,
    bool Shaken = false,
    bool Fled = false,
    bool MoraleChecked = false,
    SuccessOutcome? MoraleOutcome = null);

// The whole match at one moment. Immutable: only Reducer.Apply produces a new one, one event at a time.
// ImmutableArray fields compare by reference, so compare states by StateHash, not by ==.
public sealed record MatchState
{
    public static MatchState None { get; } = new();

    public MatchSetup? Setup { get; init; }
    public Phase Phase { get; init; } = Phase.None;
    public int Turn { get; init; }
    public bool TurnEnded { get; init; }
    public ImmutableArray<ModelState> Models { get; init; } = [];
    public SeatState One { get; init; } = new(Seat.One);
    public SeatState Two { get; init; } = new(Seat.Two);
    public Seat? DeploymentFirst { get; init; }
    public Seat? LastDeployer { get; init; }
    public Seat? InitiativeHolder { get; init; }
    public Seat? FirstActivator { get; init; }
    public Seat? LastActivator { get; init; }
    public ModelId? ActiveModel { get; init; }
    public Seat? MoraleFirst { get; init; }
    public PendingDecision? Pending { get; init; }
    public MatchOutcome? Outcome { get; init; }

    public SeatState SeatFor(Seat seat) => seat == Seat.One ? One : Two;

    public ModelState? FindModel(ModelId id)
    {
        foreach (var model in Models)
        {
            if (model.Id == id)
            {
                return model;
            }
        }
        return null;
    }

    // Success Rolls for a Shaken warband are Risky (p. 50); M4 reads this when it rolls for a model.
    public bool IsShaken(Seat seat) => SeatFor(seat).Shaken;
}
