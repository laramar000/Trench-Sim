using System.Collections.Immutable;
using Rules.Core.Dice;
using Rules.Core.Geometry;

namespace Rules.Core.State;

public enum DecisionKind
{
    ChooseDeploymentZone,
    ChooseFirstActivation,
    ChooseMoraleOrder,
    FailedMorale,
    SpendMarkersOnSuccessRoll, // the rolling model's opponent may spend its Blood Markers for -1 DICE each (p. 28)
    SpendMarkersOnInjuryRoll,  // the attacker may spend the target's Blood Markers for +1 INJURY DICE each (p. 28)
    FreeFight,                 // a model retreats: its opponent may attack it with each engaged model (p. 37)
    ChooseFriendlyTarget,      // a shot into melee rolled 1-3: the shooter picks which of its own models is the target (p. 42)
}

// A choice only one player can make. While one is pending, nothing else may happen.
// Blood Marker spending uses the same slot.
public sealed record PendingDecision(DecisionKind Kind, Seat Seat);

public enum OutcomeReason
{
    OpponentFled,
    FinalTurnComplete,       // M2 setups without a battlefield: nothing scores, so there is no winner
    AllEnemiesOutOfAction,   // no enemy model is left on the battlefield (Scenario I, p. 153)
    VictoryPoints,           // the last turn ended; a null winner is a draw on equal points (provisional)
}

// Winner is null for a draw, and for FinalTurnComplete where no scoring exists.
public sealed record MatchOutcome(Seat? Winner, OutcomeReason Reason);

public sealed record SeatState(
    Seat Seat,
    string? ZoneId = null,
    bool Shaken = false,
    bool Fled = false,
    bool MoraleChecked = false,
    SuccessOutcome? MoraleOutcome = null,
    int VictoryPoints = 0);

[Flags]
public enum ActionsTaken
{
    None = 0,
    Move = 1,
    Dash = 2,
    Shoot = 4,
    Charge = 8,
    Retreat = 16,
    Fight = 32,
}

public enum RollStage
{
    DashRoll,    // the Risky Success Roll of a Dash, then the move itself
    DashMove,    // the roll succeeded; the move follows
    ShotRoll,    // the Success Roll of a Ranged Attack
    InjuryRoll,  // the attack hit; the Injury Roll follows
    ChargeRoll,  // the Charge Bonus D6
    ChargeMove,  // the charge move itself
    TargetRoll,  // the D6 of a Ranged Attack at a target within 1" of a friendly model (p. 42)
    TargetChoice, // that D6 was 1-3: the shooter chooses which friendly model is hit
    FightRoll,   // the Success Roll of a Melee Attack
}

public enum AttackKind
{
    Ranged,
    Melee,
}

// A Dash or Ranged Attack that is part-way through, between the player's command and its last die. It exists
// only while a Blood Marker decision is pending or inside one Decide call; no dice are rolled early.
// BonusDice and PenaltyDice are the dice for the next roll (Success or Injury), including spent markers.
public sealed record RollInProgress(
    RollStage Stage,
    ModelId Actor,
    ModelId? Target = null,
    int BonusDice = 0,
    int PenaltyDice = 0,
    bool Critical = false,
    ImmutableArray<Point2> DashPath = default,
    bool MarkersAsked = false,
    AttackKind Kind = AttackKind.Ranged,
    bool FreeFight = false,
    bool FriendlyFire = false,
    int ChargeDie = 0);

// A Retreat that is part-way through: the opponent's free attacks come before the move (p. 37). Fought lists the
// models that have attacked; Closed means the opponent declined the rest.
public sealed record RetreatInProgress(ModelId Model, ImmutableArray<Point2> Path, ImmutableArray<ModelId> Fought, bool Closed = false);

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
    public ActionsTaken Actions { get; init; } = ActionsTaken.None;
    public bool MovementHalved { get; init; }
    public RollInProgress? InProgress { get; init; }
    public RetreatInProgress? Retreat { get; init; }
    public int ScoredTurn { get; init; }
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
