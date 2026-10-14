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
    ModelLevel TargetLevel = ModelLevel.Ground)
{
    public bool IsLegal => Refusal is null;
}

// Questions about positions in a state. Pure functions that delegate every geometric decision to the M3 geometry
// (Movement, Placement, Measure, Cover, LineOfSight, Elevation). Nothing here measures anything itself.
internal static class Board
{
    public const string RefAttack = "1.0.2 pp. 42-44";
    public const string RefMove = "1.0.2 pp. 34-35";
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
        if (OnTable(state).Any(o => o.Seat == attacker.Seat && o.Id != attacker.Id && Measure.IsWithin(to, BodyOf(state, o)!, EngagementRange)))
        {
            return Refuse(ReasonCode.ShootingIntoMeleeUnsupported, "Shooting at a model within 1\" of a friendly model is not supported yet.");
        }

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
            TargetLevel: cover.Sight.Target.Level);
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
