using System.Collections.Immutable;
using Rules.Core.Dice;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Events;

// A Down model stood back up at the start of its activation; its Movement is halved for the rest of it (Core p. 20).
public sealed record ModelStoodUp(ModelId Model) : GameEvent;

public enum MoveKind
{
    Move,
    Dash,
}

// The active model moved along a checked path (pp. 34-35). Cost is what the path cost, recorded so a replay can check it.
public sealed record ModelMoved : GameEvent
{
    public const int MaxWaypoints = 16;

    public ModelMoved(ModelId model, MoveKind kind, ImmutableArray<Point2> path, Length cost)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Unknown move kind.", nameof(kind));
        }
        if (path.IsDefault || path.IsEmpty || path.Length > MaxWaypoints)
        {
            throw new ArgumentException($"A move has 1 to {MaxWaypoints} waypoints.", nameof(path));
        }
        Model = model;
        Kind = kind;
        Path = path;
        Cost = cost;
    }

    public ModelId Model { get; }
    public MoveKind Kind { get; }
    public ImmutableArray<Point2> Path { get; }
    public Length Cost { get; }
}

// A Dash was chosen: the Risky Success Roll comes next, the move only if it succeeds.
public sealed record DashDeclared : GameEvent
{
    public DashDeclared(ModelId model, ImmutableArray<Point2> path)
    {
        if (path.IsDefault || path.IsEmpty || path.Length > ModelMoved.MaxWaypoints)
        {
            throw new ArgumentException($"A move has 1 to {ModelMoved.MaxWaypoints} waypoints.", nameof(path));
        }
        Model = model;
        Path = path;
    }

    public ModelId Model { get; }
    public ImmutableArray<Point2> Path { get; }
}

public sealed record DashRolled : GameEvent
{
    public DashRolled(ModelId model, SuccessRollResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        Model = model;
        Check = check;
    }

    public ModelId Model { get; }
    public SuccessRollResult Check { get; }
}

// A Move or Dash climbs a tower's side away from its ladders (towers proposal, stage T6; p. 39, U75): the Risky Success
// Roll comes next. On a Success the model makes the whole move; on a Failure it moves only to where the climb starts
// and its activation ends. Path is the whole move as declared.
public sealed record SideClimbDeclared : GameEvent
{
    public SideClimbDeclared(ModelId model, MoveKind kind, ImmutableArray<Point2> path)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Unknown move kind.", nameof(kind));
        }
        if (path.IsDefault || path.IsEmpty || path.Length > ModelMoved.MaxWaypoints)
        {
            throw new ArgumentException($"A move has 1 to {ModelMoved.MaxWaypoints} waypoints.", nameof(path));
        }
        Model = model;
        Kind = kind;
        Path = path;
    }

    public ModelId Model { get; }
    public MoveKind Kind { get; }
    public ImmutableArray<Point2> Path { get; }
}

public sealed record SideClimbRolled : GameEvent
{
    public SideClimbRolled(ModelId model, SuccessRollResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        Model = model;
        Check = check;
    }

    public ModelId Model { get; }
    public SuccessRollResult Check { get; }
}

public enum MarkerSpendPurpose
{
    ReduceSuccessRoll,  // -1 DICE per marker on a Success Roll for the model holding them
    RaiseInjuryRoll,    // +1 INJURY DICE per marker on an Injury Roll against the model holding them
}

// Blood Markers spent (p. 28). Spender is the player who chose to; Model holds the markers. Count 0 is "none".
public sealed record MarkersSpent : GameEvent
{
    public MarkersSpent(Seat spender, ModelId model, int count, MarkerSpendPurpose purpose)
    {
        if (count < 0 || count > BloodMarkers.Max || !Enum.IsDefined(purpose) || !Enum.IsDefined(spender))
        {
            throw new ArgumentException("Invalid marker spend.");
        }
        Spender = spender;
        Model = model;
        Count = count;
        Purpose = purpose;
    }

    public Seat Spender { get; }
    public ModelId Model { get; }
    public int Count { get; }
    public MarkerSpendPurpose Purpose { get; }
}

// A Ranged Attack was declared with every fact the modifiers depend on (pp. 42-44). Elevated (stage L3, p. 43) is the
// +1 DICE for standing 3" or more above the target. Bonus and penalty are the dice for the Success Roll before any
// Blood Markers are spent.
public sealed record RangedAttackDeclared : GameEvent
{
    public RangedAttackDeclared(
        ModelId attacker,
        ModelId target,
        bool longRange,
        bool inCover,
        string? coverTerrainId,
        ModelLevel attackerLevel,
        ModelLevel targetLevel,
        int penaltyDice)
    {
        if (inCover != (coverTerrainId is not null) || !Enum.IsDefined(attackerLevel) || !Enum.IsDefined(targetLevel))
        {
            throw new ArgumentException("Inconsistent attack declaration.");
        }
        if (penaltyDice != AttackDice.ShotPenalty(longRange, inCover))
        {
            throw new ArgumentException("Penalty dice must be the Long Range and Cover modifiers.", nameof(penaltyDice));
        }
        Attacker = attacker;
        Target = target;
        LongRange = longRange;
        InCover = inCover;
        CoverTerrainId = coverTerrainId;
        AttackerLevel = attackerLevel;
        TargetLevel = targetLevel;
        PenaltyDice = penaltyDice;
    }

    public ModelId Attacker { get; }
    public ModelId Target { get; }
    public bool LongRange { get; }
    public bool InCover { get; }
    public string? CoverTerrainId { get; }
    public ModelLevel AttackerLevel { get; }
    public ModelLevel TargetLevel { get; }
    public int PenaltyDice { get; }

    // The target is within 1" of one of the shooter's own models: a D6 decides who is shot (p. 42).
    public bool IntoMelee { get; init; }

    // Elevated position (p. 43, line-of-sight stage L3): the attacker stands 3" or more above the target, +1 DICE. Left
    // off the wire when false, so every earlier record reads and writes as before.
    public bool Elevated { get; init; }

    public int BonusDice => AttackDice.ShotBonus(Elevated);
}

public sealed record RangedSuccessRolled : GameEvent
{
    public RangedSuccessRolled(ModelId attacker, ModelId target, SuccessRollResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        Attacker = attacker;
        Target = target;
        Check = check;
    }

    public ModelId Attacker { get; }
    public ModelId Target { get; }
    public SuccessRollResult Check { get; }
}

// The Injury Roll of a hit and its consequences: Down, Out of Action, and the Blood Markers placed (cap 6).
// What the roll does to the target is worked out from the roll by the reducer, never stored separately.
public sealed record InjuryRolled : GameEvent
{
    public InjuryRolled(ModelId attacker, ModelId target, InjuryRollResult injury)
    {
        ArgumentNullException.ThrowIfNull(injury);
        Attacker = attacker;
        Target = target;
        Injury = injury;
    }

    public ModelId Attacker { get; }
    public ModelId Target { get; }
    public InjuryRollResult Injury { get; }
}

public sealed record ObjectiveResult(string ObjectiveId, Seat? Controller, int Points);

// Victory Points for the Objectives each seat controlled at the end of a Turn (Scenario I, p. 153).
public sealed record ObjectivesScored : GameEvent
{
    public ObjectivesScored(int turn, IEnumerable<ObjectiveResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        Turn = turn;
        Results = [.. results];
    }

    public int Turn { get; }
    public ImmutableArray<ObjectiveResult> Results { get; }
}

// ---- M4 Slice B ----

// A Charge ACTION was taken against this target (pp. 36-37). The Charge Bonus is rolled next.
public sealed record ChargeDeclared(ModelId Model, ModelId Target) : GameEvent;

public sealed record ChargeRolled : GameEvent
{
    public ChargeRolled(ModelId model, ModelId target, int die)
    {
        if (die is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(die), "The Charge Bonus is a D6.");
        }
        Model = model;
        Target = target;
        Die = die;
    }

    public ModelId Model { get; }
    public ModelId Target { get; }
    public int Die { get; }
}

// The charge move. Path is empty when the model could not move at all; Reached says it finished within 1" of its
// target. The reducer plans the same move from the state and the die and requires the same path and cost.
public sealed record ChargeMoved : GameEvent
{
    public ChargeMoved(ModelId model, ModelId target, ImmutableArray<Point2> path, Length cost, bool reached)
    {
        if (path.IsDefault || path.Length > Charging.MaxLegs || (path.IsEmpty && (reached || cost != Length.Zero)))
        {
            throw new ArgumentException($"A charge move has at most {Charging.MaxLegs} legs; no legs cost nothing and do not reach.", nameof(path));
        }
        Model = model;
        Target = target;
        Path = path;
        Cost = cost;
        Reached = reached;
    }

    public ModelId Model { get; }
    public ModelId Target { get; }
    public ImmutableArray<Point2> Path { get; }
    public Length Cost { get; }
    public bool Reached { get; }
}

// A Retreat ACTION was taken. Nothing moves yet: the opponent's free attacks come first (p. 37).
public sealed record RetreatDeclared : GameEvent
{
    public RetreatDeclared(ModelId model, ImmutableArray<Point2> path)
    {
        if (path.IsDefault || path.IsEmpty || path.Length > ModelMoved.MaxWaypoints)
        {
            throw new ArgumentException($"A move has 1 to {ModelMoved.MaxWaypoints} waypoints.", nameof(path));
        }
        Model = model;
        Path = path;
    }

    public ModelId Model { get; }
    public ImmutableArray<Point2> Path { get; }
}

// The opponent gave up its remaining free attacks.
public sealed record FreeFightsDeclined(Seat Seat) : GameEvent;

// The end of a Retreat: the model moved along its path, unless it was taken Down or Out of Action by the free
// attacks, in which case Path is empty and the model stays where it was (p. 37).
public sealed record RetreatResolved : GameEvent
{
    public RetreatResolved(ModelId model, ImmutableArray<Point2> path, Length cost)
    {
        if (path.IsDefault || path.Length > ModelMoved.MaxWaypoints || (path.IsEmpty && cost != Length.Zero))
        {
            throw new ArgumentException("A retreat that did not move costs nothing.", nameof(path));
        }
        Model = model;
        Path = path;
        Cost = cost;
    }

    public ModelId Model { get; }
    public ImmutableArray<Point2> Path { get; }
    public Length Cost { get; }
    public bool Moved => !Path.IsEmpty;
}

// A Melee Attack was declared with the facts its modifiers depend on (pp. 45-46). FreeFight marks an attack made
// by the opponent of a retreating model. Penalty dice are the Defended Obstacle modifier before any markers are spent.
public sealed record MeleeAttackDeclared : GameEvent
{
    public MeleeAttackDeclared(ModelId attacker, ModelId target, bool freeFight, string? defendedObstacleId)
    {
        Attacker = attacker;
        Target = target;
        FreeFight = freeFight;
        DefendedObstacleId = defendedObstacleId;
    }

    public ModelId Attacker { get; }
    public ModelId Target { get; }
    public bool FreeFight { get; }
    public string? DefendedObstacleId { get; }
    public int PenaltyDice => AttackDice.FightPenalty(DefendedObstacleId is not null);
}

public sealed record MeleeSuccessRolled : GameEvent
{
    public MeleeSuccessRolled(ModelId attacker, ModelId target, SuccessRollResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        Attacker = attacker;
        Target = target;
        Check = check;
    }

    public ModelId Attacker { get; }
    public ModelId Target { get; }
    public SuccessRollResult Check { get; }
}

// The D6 for a Ranged Attack at a target within 1" of one of the shooter's own models (p. 42): 1-3 the shooter must
// target one of its own models, 4-6 it may target the enemy model.
public sealed record ShootingTargetRolled : GameEvent
{
    public ShootingTargetRolled(ModelId attacker, ModelId target, int die)
    {
        if (die is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(die), "A D6 is 1 to 6.");
        }
        Attacker = attacker;
        Target = target;
        Die = die;
    }

    public ModelId Attacker { get; }
    public ModelId Target { get; }
    public int Die { get; }
    public bool HitsFriend => Die <= 3;
}

// The shooter chose which of its own models the shot is aimed at, with the modifiers that model's position gives.
public sealed record FriendlyTargetChosen : GameEvent
{
    public FriendlyTargetChosen(ModelId attacker, ModelId victim, bool longRange, bool inCover, string? coverTerrainId)
    {
        if (inCover != (coverTerrainId is not null))
        {
            throw new ArgumentException("Inconsistent cover.");
        }
        Attacker = attacker;
        Victim = victim;
        LongRange = longRange;
        InCover = inCover;
        CoverTerrainId = coverTerrainId;
    }

    public ModelId Attacker { get; }
    public ModelId Victim { get; }
    public bool LongRange { get; }
    public bool InCover { get; }
    public string? CoverTerrainId { get; }

    // Elevated position above the model shot at (p. 43, stage L3); left off the wire when false.
    public bool Elevated { get; init; }
}

// ---- Trench Warfare (trench-maps stage S6, Scenario IV, pp. 161-163) ----

// The roles roll-off winner chose who attacks. The defender takes the zone holding the trench lines (U88).
public sealed record AttackerChosen(Seat Seat, Seat Attacker) : GameEvent;

// One trench section at the end of a Turn: the defending and attacking models in it, and what each side scores.
public sealed record TrenchResult(string TrenchId, int Defenders, int Attackers, int DefenderPoints, int AttackerPoints);

// Victory Points for the trench sections at the end of a Turn (p. 163).
public sealed record TrenchesScored : GameEvent
{
    public TrenchesScored(int turn, IEnumerable<TrenchResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        Turn = turn;
        Results = [.. results];
    }

    public int Turn { get; }
    public ImmutableArray<TrenchResult> Results { get; }
}

// The attacker's game-length D6 at the end of Turn 4 (p. 162): 1-2, the game ends now (LastTurn 4); 3+, after Turn 5.
public sealed record GameLengthRolled(Seat Seat, int Die, int LastTurn) : GameEvent;
