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

// A Ranged Attack was declared with every fact the modifiers depend on (pp. 42-44). Level fields are logged for
// the elevation bonus that Gate 1 does not apply (U35). Bonus and penalty are the dice for the Success Roll
// before any Blood Markers are spent.
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
        if (penaltyDice != (longRange ? 1 : 0) + (inCover ? 1 : 0))
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
