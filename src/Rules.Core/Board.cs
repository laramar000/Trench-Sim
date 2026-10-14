using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core;

// What a ranged attack looks like from the current positions: either why it cannot be made, or the facts that
// set its modifiers. Computed the same way when a command is decided and when its event is checked.
internal sealed record AttackAssessment(
    ReasonCode? Refusal,
    string Text,
    string RuleRef,
    bool LongRange = false,
    bool InCover = false,
    string? CoverTerrainId = null,
    ModelLevel AttackerLevel = ModelLevel.Ground,
    ModelLevel TargetLevel = ModelLevel.Ground,
    bool IntoMelee = false)
{
    public bool IsLegal => Refusal is null;
}

// Questions about positions in a state. Pure functions that delegate every geometric decision to the M3 geometry
// (Movement, Placement, Measure, Cover, LineOfSight, Elevation). Nothing here measures anything itself.
internal static class Board
{
    public const string RefAttack = "1.0.2 pp. 42-44";
    public const string RefMove = "1.0.2 pp. 34-35";
    public const string RefCharge = "1.0.2 pp. 34, 36-37";
    public const string RefRetreat = "1.0.2 pp. 35, 37";
    public const string RefMelee = "1.0.2 pp. 45-46, 49";
    public const string RefIntoMelee = "1.0.2 p. 42";
    public const string RefObjectives = "1.0.2 pp. 152-153";
    public static readonly Length EngagementRange = Length.Inch;

    public static Scenario? ScenarioOf(MatchState state) => state.Setup?.Scenario;

    public static ModelBody? BodyOf(MatchState state, ModelState model) =>
        model.Position is { } at && state.Setup!.ProfileOf(model.Id) is { } profile
            ? new ModelBody(model.Id.Value, at, profile.BaseRadius)
            : null;

    // Every model that is on the table, Down ones included (a Down model still has a base, p. 36).
    public static IEnumerable<ModelState> OnTable(MatchState state) => state.Models.Where(m => m.Position is not null);

    public static List<ModelBody> AllBodies(MatchState state, ModelId? except = null) =>
        OnTable(state).Where(m => m.Id != except).Select(m => BodyOf(state, m)!).ToList();

    // Movement for this activation: the profile's Movement, halved after standing up (Core p. 20, U30).
    public static Length Allowance(MatchState state, ModelState model)
    {
        var movement = state.Setup!.ProfileOf(model.Id)!.Movement;
        return state.MovementHalved ? Length.FromThousandths(movement.Thousandths / 2) : movement;
    }

    public static MoveResult CheckMove(MatchState state, ModelState mover, IReadOnlyList<Point2> path)
    {
        var scenario = ScenarioOf(state)!;
        var others = OnTable(state)
            .Where(m => m.Id != mover.Id)
            .Select(m => new Obstacle(BodyOf(state, m)!, m.Seat == mover.Seat ? Relation.Friendly : Relation.Enemy));
        return Movement.CheckPath(scenario.Battlefield, BodyOf(state, mover)!, path, Allowance(state, mover), others);
    }

    public static PlacementResult CheckPlacement(MatchState state, Seat seat, ModelState model, Point2 at)
    {
        var scenario = ScenarioOf(state)!;
        var body = new ModelBody(model.Id.Value, at, state.Setup!.ProfileOf(model.Id)!.BaseRadius);
        return Placement.Check(scenario.Battlefield, body, AllBodies(state, model.Id), state.SeatFor(seat).ZoneId);
    }

    public static bool IsEngaged(MatchState state, ModelState model)
    {
        var body = BodyOf(state, model);
        return body is not null && OnTable(state).Any(o => o.Seat != model.Seat && Measure.IsWithin(body, BodyOf(state, o)!, EngagementRange));
    }

    public static AttackAssessment Assess(MatchState state, ModelState attacker, ModelState target)
    {
        var profile = state.Setup!.ProfileOf(attacker.Id)!;
        var scenario = ScenarioOf(state)!;
        var from = BodyOf(state, attacker);
        if (from is null || attacker.Status == ModelStatus.OutOfAction)
        {
            return Refuse(ReasonCode.ModelNotOnTable, "The attacking model is not on the battlefield.");
        }
        if (attacker.Status != ModelStatus.Standing)
        {
            return Refuse(ReasonCode.ModelNotStanding, "A Down model cannot shoot.");
        }
        if (!profile.HasRangedWeapon)
        {
            return Refuse(ReasonCode.NoRangedWeapon, "That model has no Ranged Weapon.");
        }
        if (target.Seat == attacker.Seat)
        {
            return Refuse(ReasonCode.TargetNotEnemy, "The target must be an enemy model.");
        }
        var to = BodyOf(state, target);
        if (to is null || target.Status == ModelStatus.OutOfAction)
        {
            return Refuse(ReasonCode.TargetNotOnTable, "The target is not on the battlefield.");
        }
        if (IsEngaged(state, attacker))
        {
            return Refuse(ReasonCode.AttackerEngaged, "A model within 1\" of an enemy cannot make a Ranged Attack.");
        }
        var intoMelee = FriendsNear(state, attacker, to).Count > 0;

        var cover = Cover.Of(scenario.Battlefield, from, to);
        if (cover.Sight.Visibility == Visibility.None)
        {
            return Refuse(ReasonCode.NoLineOfSight, "The target is not in the attacker's Line of Sight.", "1.0.2 p. 30");
        }
        if (!Measure.IsWithin(from, to, profile.RangedRange))
        {
            return Refuse(ReasonCode.TargetOutOfRange, "The target is beyond the weapon's range.");
        }
        var half = Length.FromThousandths(profile.RangedRange.Thousandths / 2);
        return new AttackAssessment(
            null,
            "",
            RefAttack,
            LongRange: !Measure.IsWithin(from, to, half),
            InCover: cover.InCover,
            CoverTerrainId: cover.TerrainId,
            AttackerLevel: cover.Sight.Attacker.Level,
            TargetLevel: cover.Sight.Target.Level,
            IntoMelee: intoMelee);
    }

    // The attacker's own models (on the table, Down ones included) within 1" of a target: the models a shot at that
    // target might hit instead (p. 42), in id order.
    public static List<ModelState> FriendsNear(MatchState state, ModelState attacker, ModelBody target) =>
        OnTable(state)
            .Where(o => o.Seat == attacker.Seat && o.Id != attacker.Id && Measure.IsWithin(target, BodyOf(state, o)!, EngagementRange))
            .OrderBy(o => o.Id.Value, StringComparer.Ordinal)
            .ToList();

    // Long Range and Cover for a shot at a model the shooter was forced to target (p. 42): the modifiers follow the
    // positions as for any shot, but Line of Sight and range no longer decide whether the shot may be made (U51).
    public static (bool LongRange, bool InCover, string? CoverTerrainId) ForcedShotFacts(MatchState state, ModelState attacker, ModelState victim)
    {
        var from = BodyOf(state, attacker)!;
        var to = BodyOf(state, victim)!;
        var profile = state.Setup!.ProfileOf(attacker.Id)!;
        var cover = Cover.Of(ScenarioOf(state)!.Battlefield, from, to);
        var half = Length.FromThousandths(profile.RangedRange.Thousandths / 2);
        var inCover = cover.InCover && cover.Sight.Visibility != Visibility.None;
        return (!Measure.IsWithin(from, to, half), inCover, inCover ? cover.TerrainId : null);
    }

    // ---- Charge (pp. 34, 36-37) ----

    public static List<Obstacle> ObstaclesFor(MatchState state, ModelState mover) =>
        OnTable(state)
            .Where(m => m.Id != mover.Id)
            .Select(m => new Obstacle(BodyOf(state, m)!, m.Seat == mover.Seat ? Relation.Friendly : Relation.Enemy))
            .ToList();

    // Declaring a charge: everything that can be decided before the Charge Bonus is rolled.
    public static Rejection? AssessCharge(MatchState state, ModelState charger, ModelState target)
    {
        var from = BodyOf(state, charger);
        if (from is null || charger.Status == ModelStatus.OutOfAction)
        {
            return new(ReasonCode.ModelNotOnTable, "The charging model is not on the battlefield.", RefCharge);
        }
        if (charger.Status != ModelStatus.Standing)
        {
            return new(ReasonCode.ModelNotStanding, "A Down model cannot charge.", RefCharge);
        }
        if (target.Seat == charger.Seat)
        {
            return new(ReasonCode.TargetNotEnemy, "The target of a charge must be an enemy model.", RefCharge);
        }
        var to = BodyOf(state, target);
        if (to is null || target.Status == ModelStatus.OutOfAction)
        {
            return new(ReasonCode.TargetNotOnTable, "The target is not on the battlefield.", RefCharge);
        }
        if (IsEngaged(state, charger))
        {
            return new(ReasonCode.AlreadyEngaged, "A model already within 1\" of an enemy cannot take a Charge ACTION.", "1.0.2 p. 34");
        }
        var cover = Cover.Of(ScenarioOf(state)!.Battlefield, from, to);
        if (cover.Sight.Visibility == Visibility.None)
        {
            return new(ReasonCode.NoLineOfSight, "The target of a charge must be visible to the charging model.", "1.0.2 pp. 30, 36");
        }
        if (!Measure.IsWithin(from, to, ChargeRange))
        {
            return new(ReasonCode.TargetOutOfRange, "The target of a charge must be within 12\".", "1.0.2 p. 36");
        }
        var route = Charging.CheckRoute(ScenarioOf(state)!.Battlefield, from, to, ObstaclesFor(state, charger));
        return route.IsLegal ? null : new(ReasonCode.ChargeRouteBlocked, route.Text, route.RuleRef);
    }

    public static readonly Length ChargeRange = Length.FromInches(12);

    // Movement for a charge: Movement plus the Charge Bonus, at most 12", all halved after standing up (pp. 36, 49).
    public static Length ChargeReach(MatchState state, ModelState model, int bonus)
    {
        var movement = state.Setup!.ProfileOf(model.Id)!.Movement;
        var total = Length.FromThousandths(Math.Min(movement.Thousandths + bonus * 1000L, Charging.MaxMovement.Thousandths));
        return state.MovementHalved ? Length.FromThousandths(total.Thousandths / 2) : total;
    }

    public static ChargePlan PlanCharge(MatchState state, ModelState charger, ModelState target, int bonus) =>
        Charging.Plan(ScenarioOf(state)!.Battlefield, BodyOf(state, charger)!, BodyOf(state, target)!, ChargeReach(state, charger, bonus), ObstaclesFor(state, charger));

    // ---- Retreat (pp. 35, 37) ----

    public static Rejection? CheckRetreat(MatchState state, ModelState model, IReadOnlyList<Point2> path)
    {
        if (!IsEngaged(state, model))
        {
            return new(ReasonCode.NotEngaged, "Only a model within 1\" of an enemy can Retreat.", RefRetreat);
        }
        var result = Movement.CheckPath(ScenarioOf(state)!.Battlefield, BodyOf(state, model)!, path, Allowance(state, model), ObstaclesFor(state, model), retreat: true);
        if (!result.IsLegal)
        {
            return new(ReasonCode.MoveIllegal, result.Text, result.RuleRef);
        }
        var end = BodyOf(state, model)!.At(path[^1]);
        return OnTable(state).Any(o => o.Seat != model.Seat && Measure.IsWithin(end, BodyOf(state, o)!, EngagementRange))
            ? new(ReasonCode.RetreatEndsEngaged, "A retreating model must end its move more than 1\" from all enemy models.", "1.0.2 p. 37")
            : null;
    }

    public static Length RetreatCost(MatchState state, ModelState model, IReadOnlyList<Point2> path) =>
        Movement.CheckPath(ScenarioOf(state)!.Battlefield, BodyOf(state, model)!, path, Allowance(state, model), ObstaclesFor(state, model), retreat: true).Cost;

    // The enemy models that may still attack a retreating model before it moves: standing, within 1" of it, not yet used.
    public static List<ModelState> FreeFighters(MatchState state, RetreatInProgress retreat)
    {
        var retreater = state.FindModel(retreat.Model);
        if (retreater?.Position is null || retreater.Status == ModelStatus.OutOfAction)
        {
            return [];
        }
        var body = BodyOf(state, retreater)!;
        return OnTable(state)
            .Where(o => o.Seat != retreater.Seat && o.Status == ModelStatus.Standing && !retreat.Fought.Contains(o.Id)
                && state.Setup!.ProfileOf(o.Id)!.HasMeleeWeapon && Measure.IsWithin(body, BodyOf(state, o)!, EngagementRange))
            .OrderBy(o => o.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    // ---- Melee Attacks (pp. 45-46) ----

    public sealed record MeleeAssessment(Rejection? Refusal, bool InCover = false, string? CoverTerrainId = null);

    public static MeleeAssessment AssessMelee(MatchState state, ModelState attacker, ModelState target)
    {
        MeleeAssessment Refuse(ReasonCode code, string text) => new(new Rejection(code, text, RefMelee));
        var from = BodyOf(state, attacker);
        if (from is null || attacker.Status == ModelStatus.OutOfAction)
        {
            return Refuse(ReasonCode.ModelNotOnTable, "The attacking model is not on the battlefield.");
        }
        if (attacker.Status != ModelStatus.Standing)
        {
            return Refuse(ReasonCode.ModelNotStanding, "A Down model cannot fight.");
        }
        if (!state.Setup!.ProfileOf(attacker.Id)!.HasMeleeWeapon)
        {
            return Refuse(ReasonCode.NoMeleeWeapon, "That model has no Melee Weapon.");
        }
        if (target.Seat == attacker.Seat)
        {
            return Refuse(ReasonCode.TargetNotEnemy, "The target must be an enemy model.");
        }
        var to = BodyOf(state, target);
        if (to is null || target.Status == ModelStatus.OutOfAction)
        {
            return Refuse(ReasonCode.TargetNotOnTable, "The target is not on the battlefield.");
        }
        if (!Measure.IsWithin(from, to, EngagementRange))
        {
            return Refuse(ReasonCode.TargetNotInMelee, "The target of a Melee Attack must be within 1\".");
        }
        var cover = Cover.Of(ScenarioOf(state)!.Battlefield, from, to);
        if (cover.Sight.Visibility == Visibility.None)
        {
            return Refuse(ReasonCode.NoLineOfSight, "The target must be in the attacker's Line of Sight.");
        }
        return new MeleeAssessment(null, cover.InCover, cover.InCover ? cover.TerrainId : null);
    }

    private static AttackAssessment Refuse(ReasonCode code, string text, string rule = RefAttack) => new(code, text, rule);

    // ---- Objectives (Scenario I, p. 152) ----

    // The seat that controls the Objective piece, if any: more friendly models on, in or within 1" than enemy models;
    // but if exactly one side has models on the piece, that side controls it. "On" is read as more than half the base
    // on the piece (U36); Down models count, Out of Action ones are off the table.
    public static Seat? Controller(MatchState state, string objectiveId)
    {
        var piece = ScenarioOf(state)!.Battlefield.Terrain.First(t => t.Id == objectiveId);
        var near = new int[2];
        var on = new int[2];
        foreach (var model in OnTable(state))
        {
            var body = BodyOf(state, model)!;
            var isOn = piece.Footprint.MoreThanHalfInside(body.Centre, body.Radius) == AreaDecision.MoreThanHalf;
            if (isOn || piece.Footprint.CircleWithin(body.Centre, body.Radius, EngagementRange))
            {
                near[(int)model.Seat]++;
            }
            if (isOn)
            {
                on[(int)model.Seat]++;
            }
        }
        if ((on[0] > 0) != (on[1] > 0))
        {
            return on[0] > 0 ? Seat.One : Seat.Two;
        }
        return near[0] == near[1] ? null : near[0] > near[1] ? Seat.One : Seat.Two;
    }
}
