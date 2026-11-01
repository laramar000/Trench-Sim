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
    ChooseAttacker,            // Trench Warfare: the roles roll-off winner chooses who attacks (p. 161)
    LayMines,                  // Trench Warfare: the defender sets up the Mine Markers and secretly picks the duds (p. 162)
    ChooseScatterPoint,        // a SCATTER attack missed: the attacker's opponent picks where it lands (p. 57, roster stage R4c)
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

// MustFlee: the warband failed its Morale Check while already Shaken, so it flees next (p. 50). The state cannot
// show this otherwise: a warband that chose to become Shaken after failing looks the same once it has chosen.
public sealed record SeatState(
    Seat Seat,
    string? ZoneId = null,
    bool Shaken = false,
    bool Fled = false,
    bool MoraleChecked = false,
    SuccessOutcome? MoraleOutcome = null,
    int VictoryPoints = 0,
    bool MustFlee = false);

// The result of a roll-off, kept until the event it decides has used it: the zone choice, the deployment order or
// the Initiative. The winner is worked out from the recorded dice, never taken from the event.
public sealed record RollOffResult(Events.RollOffPurpose Purpose, Seat Winner);

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
    ClimbRoll,   // the Risky Success Roll of a climb up or down a tower's side (towers stage T6)
    ClimbMove,   // that roll succeeded: the whole move follows
    ClimbStop,   // it failed: the model moves only to where the climb starts, then its activation ends
    Blast,       // a BLAST attack hit: who the blast hits follows, then an Injury Roll for each (roster stage R4b)
    Scatter,     // a SCATTER attack missed: the opponent picks where it lands, or it is a miss after all (roster stage R4c)
}

public enum AttackKind
{
    Ranged,
    Melee,
}

// A Dash or Ranged Attack that is part-way through, between the player's command and its last die. It exists
// only while a Blood Marker decision is pending or inside one Decide call; no dice are rolled early.
// BonusDice and PenaltyDice are the dice for the next roll (Success or Injury), including spent markers.
// A climb up a tower's side (Climb* stages) keeps its whole path in DashPath and its action (Move or Dash) in ClimbAction.
// InjuryDiceLater: injury dice decided when the attack was declared, added at the Injury Roll (SHOTGUN at Long Range, R2).
// Roster stage R4b (U102): a BLAST attack that hit keeps the model it was aimed at in Aimed (only its Injury Roll gets a
// Critical Success's extra dice) and the models still to roll for in BlastQueue, in order; Target is the one now.
// Roster stage R4c (U103): a SCATTER attack that missed keeps how far it scatters (ScatterInches, 7 - the roll), where it
// landed once chosen (BlastAt), and whether its roll was a failed Risky one, so the activation ends once it is over
// (RiskyEnds).
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
    int ChargeDie = 0,
    ActionsTaken ClimbAction = ActionsTaken.None,
    int InjuryDiceLater = 0,
    int Weapon = 0,
    bool OffHand = false,
    ModelId? Aimed = null,
    ImmutableArray<ModelId> BlastQueue = default,
    int ScatterInches = 0,
    Point2? BlastAt = null,
    bool RiskyEnds = false);

// A Retreat that is part-way through: the opponent's free attacks come before the move (p. 37). Fought lists the
// models that have attacked; Closed means the opponent declined the rest.
// Roster stage R3 (U100): the Melee Attacks still to come of one Fight ACTION (Free false: the active model's) or one
// free attack (Free true), in order: each with its weapon (0, or 1 for the second Melee Weapon) and whether it is the
// Off-Hand weapon (-1 DICE, p. 45). The active model's are optional: anything else it does ends them. A free attack's
// CLEAVE attacks follow one after another against the retreating model.
public sealed record MeleeAttacksLeft(ModelId Attacker, bool Free, ImmutableArray<MeleeAttackSlot> Next);

public sealed record MeleeAttackSlot(int Weapon, bool OffHand);

// Roster stage R5g (U112): 1 BLOOD MARKER owed to each of Targets (enemy models within 1" of Source, in the setup's order).
public sealed record OwedMarkers(ModelId Source, ImmutableArray<ModelId> Targets);

public sealed record RetreatInProgress(ModelId Model, ImmutableArray<Point2> Path, ImmutableArray<ModelId> Fought, bool Closed = false);

// A move halted by a Mine Marker (Trench Warfare, p. 162; U93), between the move event and the rest of the move. Path is
// the move's whole path, Stops every halt on it in order, Index the halt the model is at. Revealed: the marker there has
// been revealed; Live: it was live and its Injury Roll is still to come. EndsActivation: the move ends the activation
// once it is finished (a failed side climb, U75).
public sealed record MineStop(
    ModelId Model,
    ImmutableArray<Point2> Path,
    ImmutableArray<MineStopPoint> Stops,
    int Index = 0,
    bool Revealed = false,
    bool Live = false,
    bool EndsActivation = false)
{
    public MineStopPoint Current => Stops[Index];
}

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

    // Trench Warfare (trench-maps stage S6): the attacking seat once chosen, and the last Turn once the game-length
    // roll has decided it. Null in every other scenario.
    public Seat? Attacker { get; init; }
    public int? LastTurn { get; init; }

    // Trench Warfare's Mine Markers (p. 162): those still on the table, whether they have been set up, and the duds. The
    // duds are secret: a view that does not know them (the attacker's) has DudsKnown false and Duds empty, and the state
    // hash leaves both out, so every view hashes the same.
    public ImmutableArray<MineMarker> Mines { get; init; } = [];
    public bool MinesLaid { get; init; }
    public ImmutableArray<string> Duds { get; init; } = [];
    public bool DudsKnown { get; init; }
    public MineStop? MineStop { get; init; }

    // Glorious Deeds (U96): those completed, those just completed and still to be announced, and what they need to know.
    public DeedState Deeds { get; init; } = new();

    // The Turn after which the game ends: the game-length roll's answer, else the setup's turn limit.
    public int FinalTurn => LastTurn ?? Setup?.TurnLimit ?? 0;

    // A roll-off that has been rolled but not yet used (see RollOffResult).
    public RollOffResult? RollOff { get; init; }

    // A Risky roll of the active model failed (a Dash, or a Shaken warband's attack): its activation ends next.
    public bool MustEndActivation { get; init; }

    // Roster stage R2b: the active model attacked with a RELOAD weapon and that ACTION is done, so its activation ends
    // next (p. 56). Hashed only when set.
    public bool ReloadEnds { get; init; }

    // Roster stage R3 (U100), each hashed only when set: the weapon the active model's Shoot ACTION used (ASSAULT and
    // HEAVY are the shooting weapon's, pp. 52, 55); the Melee Attacks still to come of a Fight (a second weapon, CLEAVE)
    // or of a free attack (CLEAVE); and, only in matches with a BLOCK model, the models that made a Charge ACTION this
    // turn (p. 53).
    public int ShotWeapon { get; init; }

    public MeleeAttacksLeft? AttacksLeft { get; init; }

    public ImmutableArray<ModelId> ChargedThisTurn { get; init; } = [];

    // Roster stage R5g (U112): BLOOD MARKERS the last event owes nearby enemies (a Crown of Hellfire's bearer ended its
    // activation, a Damned Pact's bearer was taken Out of Action), each placed by the next event (BloodMarkersPlaced),
    // in order. Hashed only when set.
    public ImmutableArray<OwedMarkers> MarkersOwed { get; init; } = [];

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
