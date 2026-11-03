using System.Collections.Immutable;
using Rules.Core.Dice;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Events;

// A Down model stood back up at the start of its activation; its Movement is halved for the rest of it (Core p. 20).
// Roster stage R6c (U119): Quick, its Movement is not halved (Frightening Speed); left out when false.
public sealed record ModelStoodUp(ModelId Model) : GameEvent
{
    public bool Quick { get; init; }
}

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
    Bloodbath,          // 6 markers (3 if Down) for a Bloodbath Roll: the Injury Roll keeps one more die (p. 47, U123)
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

    // Roster stage R3 (U100): the weapon that shoots, 0 or 1 (the model's second ranged weapon). Left off the wire when 0.
    public int Weapon { get => _weapon; init => _weapon = value is 0 or 1 or ModelProfile.GrenadeWeapon ? value : throw new ArgumentOutOfRangeException(nameof(Weapon), "A Ranged Attack is made with weapon 0, 1 or 2 (grenades)."); }

    private readonly int _weapon;

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

    // Roster stage R4a (U101): the extra BLOOD MARKERS the weapon's SHRAPNEL, GAS and FIRE put on the target (0 to 3),
    // added to the result's own; left out when 0.
    public int ExtraMarkers { get => _extra; init => _extra = value is >= 0 and <= 3 ? value : throw new ArgumentOutOfRangeException(nameof(ExtraMarkers), "SHRAPNEL, GAS and FIRE put 0 to 3 extra markers."); }

    private readonly int _extra;

    // Roster stage R5b (U106): TOUGH turned this Out of Action result into a Down result (p. 57); left out when false.
    public bool Tough { get => _tough; init => _tough = value && Injury.Outcome != InjuryOutcome.OutOfAction ? throw new ArgumentException("TOUGH turns only an Out of Action result into Down.", nameof(Tough)) : value; }

    private readonly bool _tough;

    // Roster stage R6c (U119): Memento Mori turned this first Out of Action result into No Effect; left out when false.
    public bool Spared { get => _spared; init => _spared = value && Injury.Outcome != InjuryOutcome.OutOfAction ? throw new ArgumentException("Memento Mori turns only an Out of Action result into No Effect.", nameof(Spared)) : value; }

    private readonly bool _spared;

    // The result the model takes, and the BLOOD MARKERS it gives (TOUGH's Down or Memento Mori's No Effect instead of
    // Out of Action).
    public InjuryOutcome Result => Spared ? InjuryOutcome.NoEffect : Tough ? InjuryOutcome.Down : Injury.Outcome;

    public int ResultMarkers => InjuryRoll.BloodMarkersFor(Result, Injury.TargetAlreadyDown);

    // Roster stage R5e (U109): the Sacrificial Lamb's blood kept off the first BLOOD MARKER placed on the model; left out
    // when false.
    public bool Warded { get; init; }

    // U123: a Bloodbath Roll (p. 47), which keeps one more die than the Injury Roll would; left out
    // when false.
    public bool Bloodbath { get; init; }

    // The BLOOD MARKERS placed on the target: the result's and the weapon's, less the one the Lamb's blood kept off.
    public int PlacedMarkers => ResultMarkers + ExtraMarkers - (Warded ? 1 : 0);
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

    // Roster stage R6a (U117): Shock Charge rolls 1 extra D6 and keeps the highest; Dropped is the lower die (0: none).
    public int Dropped { get => _dropped; init => _dropped = value is < 0 or > 6 ? throw new ArgumentOutOfRangeException(nameof(Dropped), "The dropped die is a D6.") : value; }

    private readonly int _dropped;
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

    // Roster stage R3 (U100), each left off the wire when 0 or false: the weapon that attacks (0, or 1 for the second
    // Melee Weapon); OffHand, the second weapon of a Fight, -1 DICE (p. 45); Block, the target has BLOCK and the
    // attacker made a Charge ACTION before the attack this turn, -1 DICE (p. 53).
    // Roster stage R5i (U114): ModelProfile.GrindWeapon is a Grind to Dust ACTION's attack, with no Melee Weapon.
    // Roster stage R6d (U120): ModelProfile.TrampleWeapon a Trample ACTION's.
    public int Weapon { get => _weapon; init => _weapon = value is 0 or 1 or ModelProfile.GrindWeapon or ModelProfile.TrampleWeapon ? value : throw new ArgumentOutOfRangeException(nameof(Weapon), "A model has weapon 0 or 1 (3: Grind to Dust, 4: Trample)."); }

    public bool OffHand { get; init; }

    public bool Block { get; init; }

    // Roster stage R5b (U106): the target has FEAR and the attacker neither FEAR nor NEGATE FEAR, -1 DICE (p. 54). Left
    // off the wire when false.
    public bool Fear { get; init; }

    private readonly int _weapon;

    public int PenaltyDice => AttackDice.FightPenalty(DefendedObstacleId is not null) + AttackDice.FightPenalty(OffHand) + AttackDice.FightPenalty(Block) + AttackDice.FightPenalty(Fear);
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

// Roster stage R4b (U102): a BLAST attack hit (p. 53). Target is the model it was aimed at; Hit is every model the blast
// hits, in the order their Injury Rolls are made: the target first, then the others in the setup's order. Each then
// has its own Injury Roll (and before it, the offer to spend its Blood Markers).
public sealed record BlastHit : GameEvent
{
    public BlastHit(ModelId attacker, ModelId target, ImmutableArray<ModelId> hit)
    {
        if (hit.IsDefaultOrEmpty || hit[0] != target || hit.Distinct().Count() != hit.Length)
        {
            throw new ArgumentException("A blast hits its target first, and each model once.", nameof(hit));
        }
        Attacker = attacker;
        Target = target;
        Hit = hit;
    }

    public ModelId Attacker { get; }
    public ModelId Target { get; }
    public ImmutableArray<ModelId> Hit { get; }
}

// Roster stage R4c (U103): a missed SCATTER attack lands at At, Inches (7 - the Success Roll) from the target's base
// centre (or, R5a, from the point it was aimed at; Target is then null), where the attacker's opponent put it (p. 57). Hit is every model the blast there hits, in the order their Injury
// Rolls are made (the setup's); it may be empty.
public sealed record AttackScattered : GameEvent
{
    public AttackScattered(ModelId attacker, ModelId? target, Point2 at, int inches, ImmutableArray<ModelId> hit)
    {
        if (inches < 1 || inches > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(inches), "A failed Success Roll of 2 to 6 scatters 1\" to 5\".");
        }
        if (hit.IsDefault || hit.Distinct().Count() != hit.Length)
        {
            throw new ArgumentException("A blast hits each model once.", nameof(hit));
        }
        Attacker = attacker;
        Target = target;
        At = at;
        Inches = inches;
        Hit = hit;
    }

    public ModelId Attacker { get; }
    public ModelId? Target { get; }
    public Point2 At { get; }
    public int Inches { get; }
    public ImmutableArray<ModelId> Hit { get; }
}

// Roster stage R4c (U103): a missed SCATTER attack has nowhere it could land (no point Inches from the target, on the
// battlefield, in Line of Sight of it), so it is a miss (p. 57).
public sealed record ScatterMissed : GameEvent
{
    public ScatterMissed(ModelId attacker, ModelId? target, int inches)
    {
        if (inches < 1 || inches > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(inches), "A failed Success Roll of 2 to 6 scatters 1\" to 5\".");
        }
        Attacker = attacker;
        Target = target;
        Inches = inches;
    }

    public ModelId Attacker { get; }
    public ModelId? Target { get; }
    public int Inches { get; }
}

// Roster stage R5a (U105): a Ranged Attack with a BLAST weapon aimed at a point (p. 53): the point, whether it is beyond
// half the weapon's range (Long Range, -1 DICE), and the weapon (as RangedAttackDeclared's).
public sealed record PointAttackDeclared(ModelId Attacker, Point2 At, bool LongRange) : GameEvent
{
    public int Weapon { get; init; }
}

// Roster stage R5a (U105): the Success Roll of an attack aimed at a point.
public sealed record PointSuccessRolled : GameEvent
{
    public PointSuccessRolled(ModelId attacker, Point2 at, SuccessRollResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        Attacker = attacker;
        At = at;
        Check = check;
    }

    public ModelId Attacker { get; }
    public Point2 At { get; }
    public SuccessRollResult Check { get; }
}

// Roster stage R5a (U105): an attack aimed at a point hit: every model the blast there hits, in the order their Injury
// Rolls are made (the setup's); it may be empty.
public sealed record PointBlastHit : GameEvent
{
    public PointBlastHit(ModelId attacker, Point2 at, ImmutableArray<ModelId> hit)
    {
        if (hit.IsDefault || hit.Distinct().Count() != hit.Length)
        {
            throw new ArgumentException("A blast hits each model once.", nameof(hit));
        }
        Attacker = attacker;
        At = at;
        Hit = hit;
    }

    public ModelId Attacker { get; }
    public Point2 At { get; }
    public ImmutableArray<ModelId> Hit { get; }
}

// Roster stage R5g (U112): 1 BLOOD MARKER placed next to each of Targets, the enemy models within 1" of Source, because
// Source's Crown of Hellfire's bearer ended an activation or its Damned Pact's bearer was taken Out of Action (just
// before it was removed). Warded are the targets whose Sacrificial Lamb keeps that marker off (U109), in the same order.
public sealed record BloodMarkersPlaced : GameEvent
{
    public BloodMarkersPlaced(ModelId source, ImmutableArray<ModelId> targets, ImmutableArray<ModelId> warded)
    {
        if (targets.IsDefaultOrEmpty || targets.Distinct().Count() != targets.Length || targets.Contains(source))
        {
            throw new ArgumentException("Blood Markers are placed on one or more other models, each once.", nameof(targets));
        }
        if (warded.IsDefault || warded.Distinct().Count() != warded.Length || warded.Any(w => !targets.Contains(w)))
        {
            throw new ArgumentException("Only a target can keep its marker off, once.", nameof(warded));
        }
        Source = source;
        Targets = targets;
        Warded = warded;
    }

    public ModelId Source { get; }
    public ImmutableArray<ModelId> Targets { get; }
    public ImmutableArray<ModelId> Warded { get; }
}

// Roster stage R5j (U115): Model's Martyrdom Device detonates (Warbands p. 58). Hit are the models within 3" of it (friend
// or foe, Down ones too), in the setup's order with Model itself last: each has an Injury Roll, in that order. Close are
// the other models within 1" of it, whose Injury Rolls have +1 INJURY DICE. Model's own roll is 4D6 added together.
public sealed record MartyrdomTriggered : GameEvent
{
    public MartyrdomTriggered(ModelId model, ImmutableArray<ModelId> hit, ImmutableArray<ModelId> close)
    {
        if (hit.IsDefaultOrEmpty || hit.Distinct().Count() != hit.Length || hit[^1] != model)
        {
            throw new ArgumentException("A Martyrdom Device hits each model within 3\" once, its bearer last.", nameof(hit));
        }
        if (close.IsDefault || close.Distinct().Count() != close.Length || close.Contains(model) || close.Any(c => !hit.Contains(c)))
        {
            throw new ArgumentException("The models within 1\" are other models it hits, each once.", nameof(close));
        }
        Model = model;
        Hit = hit;
        Close = close;
    }

    public ModelId Model { get; }
    public ImmutableArray<ModelId> Hit { get; }
    public ImmutableArray<ModelId> Close { get; }
}

// Roster stage R5h (U113): a Treat ACTION (Medi-kit, p. 84) is declared: its Risky Success Roll comes next (after the
// offer to spend the model's Blood Markers).
public sealed record TreatDeclared(ModelId Model, ModelId Target, TreatChoice Choice) : GameEvent;

// The Treat ACTION's Risky Success Roll. Treated: it succeeded and the choice was carried out (1 BLOOD MARKER removed
// from Target, or Target stood up); false on a Failure, or when there was nothing left to do (the markers were spent).
public sealed record TreatRolled : GameEvent
{
    public TreatRolled(ModelId model, ModelId target, TreatChoice choice, SuccessRollResult check, bool treated)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (treated && check.Outcome == SuccessOutcome.Failure)
        {
            throw new ArgumentException("A failed Treat roll treats nobody.", nameof(treated));
        }
        Model = model;
        Target = target;
        Choice = choice;
        Check = check;
        Treated = treated;
    }

    public ModelId Model { get; }
    public ModelId Target { get; }
    public TreatChoice Choice { get; }
    public SuccessRollResult Check { get; }
    public bool Treated { get; }
}

// Roster stage R5k (U116): after its failed Risky Success Roll, Model uses one of its Talismans (core rulebook pp. 81, 86):
// its activation does not end. Declining is the ActivationEnded (RiskyFailure) that would have come anyway.
public sealed record TalismanUsed(ModelId Model) : GameEvent;

// Roster stage R6b (U118): an Aim or Fortify ACTION is declared: its Risky Success Roll comes next (after the offer to
// spend the model's Blood Markers).
// Roster stage R6e (U121): Target, as on TakeSelfAction; left out when null.
public sealed record SelfActionDeclared(ModelId Model, SelfActionKind Action) : GameEvent
{
    public ModelId? Target { get; init; }
}

// The Aim or Fortify ACTION's Risky Success Roll; anything but a Failure carries it out.
public sealed record SelfActionRolled : GameEvent
{
    public SelfActionRolled(ModelId model, SelfActionKind action, SuccessRollResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        Model = model;
        Action = action;
        Check = check;
    }

    public ModelId Model { get; }
    public SelfActionKind Action { get; }
    public SuccessRollResult Check { get; }
    public bool Succeeded => Check.Outcome != SuccessOutcome.Failure;

    // Roster stage R6e (U121): a Laying on of Hands' Target and the BLOOD MARKERS it lost (1, 3 on a Critical, at most what
    // it had); the Down friends an Enforced Orthodoxy stood up. Left out when null, zero or empty.
    public ModelId? Target { get; init; }

    public int Removed { get => _removed; init => _removed = value is >= 0 and <= 3 ? value : throw new ArgumentOutOfRangeException(nameof(Removed), "A Laying on of Hands removes 0 to 3 BLOOD MARKERS."); }

    private readonly int _removed;

    public ImmutableArray<ModelId> StoodUp { get; init; } = [];
}
