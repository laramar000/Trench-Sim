using System.Collections.Immutable;
using Rules.Core.Dice;
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
    bool IntoMelee = false,
    bool Elevated = false)
{
    public bool IsLegal => Refusal is null;
}

// Questions about positions in a state. Pure functions that delegate every geometric decision to the M3 geometry
// (Movement, Placement, Measure, Cover, LineOfSight, Elevation). Nothing here measures anything itself.
internal static class Board
{
    // Roster stage R5f (U110): the cover piece an event names when the target's COVER keyword gives it the Cover or
    // Defended Obstacle modifier (p. 54) and no piece of terrain does.
    public const string CoverKeyword = "COVER";

    // Roster stage R5f (U110): the model has the COVER keyword: always (a shield with COVER), or dug in with a Shovel
    // while it stands where it started the game.
    // Roster stage R6b (U118): or fortified by a Fortify ACTION where it stands; DugInAt holds that spot too.
    public static bool HasCover(MatchState state, ModelState model) =>
        state.Setup?.ProfileOf(model.Id) is { } profile && model.Position is { } at
        && (profile.HasCover || model.DugInAt == at);

    // Roster stage R5f (U110): a model starts the game on Open terrain when its centre is on no Difficult or Dangerous
    // piece.
    public static bool OnOpenTerrain(MatchState state, Point2 at) =>
        ScenarioOf(state)?.Battlefield is not { } field
        || !field.Terrain.Any(t => (t.Kind & (TerrainKind.Difficult | TerrainKind.Dangerous)) != 0 && t.Footprint.Parts.Any(r => r.Contains(at)));

    public const string RefAttack = "1.0.2 pp. 42-44";
    public const string RefMove = "1.0.2 pp. 34-35";
    public const string RefCharge = "1.0.2 pp. 34, 36-37";
    public const string RefRetreat = "1.0.2 pp. 35, 37";
    public const string RefMelee = "1.0.2 pp. 45-46, 49";
    public const string RefIntoMelee = "1.0.2 p. 42";
    public const string RefScatter = "1.0.2 p. 57";
    public const string RefBlast = "1.0.2 p. 53";
    public const string RefObjectives = "1.0.2 pp. 152-153";
    public static readonly Length EngagementRange = Length.Inch;

    public static Scenario? ScenarioOf(MatchState state) => state.Setup?.Scenario;

    // The model's base where it stands, at its elevation (a tower's height on its platform, else the ground).
    public static ModelBody? BodyOf(MatchState state, ModelState model) =>
        model.Position is { } at ? BodyAt(state, model, at) : null;

    // The model's base if it stood at this point, at the elevation the battlefield gives that point.
    public static ModelBody? BodyAt(MatchState state, ModelState model, Point2 at) =>
        state.Setup!.ProfileOf(model.Id) is { } profile
            ? new ModelBody(model.Id.Value, at, profile.BaseRadius, ScenarioOf(state) is { } scenario ? Towers.ElevationAt(scenario.Battlefield, at) : Length.Zero)
            : null;

    // Every model that is on the table, Down ones included (a Down model still has a base, p. 36).
    public static IEnumerable<ModelState> OnTable(MatchState state) => state.Models.Where(m => m.Position is not null);

    public static List<ModelBody> AllBodies(MatchState state, ModelId? except = null) =>
        OnTable(state).Where(m => m.Id != except).Select(m => BodyOf(state, m)!).Concat(ShrineBodies(state)).ToList();

    // Field Shrines (Battlekit p. 82; U127): each is an Impassable base on the table, not a model.
    public static List<ModelBody> ShrineBodies(MatchState state) =>
        state.Shrines.IsDefaultOrEmpty
            ? []
            : [.. state.Shrines.Select(h => new ModelBody("shrine-" + h.Carrier.Value, h.At, ShrineState.Radius,
                ScenarioOf(state) is { } scenario ? Towers.ElevationAt(scenario.Battlefield, h.At) : Length.Zero))];

    public static IEnumerable<Obstacle> ShrineObstacles(MatchState state) => ShrineBodies(state).Select(b => new Obstacle(b, Relation.Impassable));

    // The model whose Field Shrine its player has yet to deploy or decline: the first deployed one of the seat with a shrine
    // that is not settled. Null if none.
    public static ModelState? ShrineCarrierWaiting(MatchState state, Seat seat) =>
        state.Models.FirstOrDefault(m => m.Seat == seat && m.Deployed && !state.ShrineSettled.Contains(m.Id)
            && state.Setup?.ProfileOf(m.Id) is { FieldShrine: true });

    // Why this centre cannot hold the carrier's Field Shrine: wholly within the seat's zone, not on a base, another shrine
    // or Impassable terrain. Null when it can.
    public static PlacementResult CheckShrine(MatchState state, Seat seat, Point2 at) =>
        Placement.Check(ScenarioOf(state)!.Battlefield, new ModelBody("shrine", at, ShrineState.Radius), AllBodies(state), state.SeatFor(seat).ZoneId);

    // One legal spot for the seat's Field Shrine, nearest the zone's centre on a 1" grid; null if the zone has none. The
    // rules accept any other legal spot too (U127).
    public static Point2? SuggestedShrine(MatchState state, Seat seat)
    {
        if (ScenarioOf(state)?.Battlefield is not { } field || state.SeatFor(seat).ZoneId is not { } zoneId || field.Zone(zoneId) is not { } zone)
        {
            return null;
        }
        var b = zone.Area.Bounds;
        var (cx, cy) = ((b.Min.X.Thousandths + b.Max.X.Thousandths) / 2, (b.Min.Y.Thousandths + b.Max.Y.Thousandths) / 2);
        var r = ShrineState.Radius.Thousandths;
        var found = new List<(long D, long X, long Y)>();
        for (var x = b.Min.X.Thousandths + r; x <= b.Max.X.Thousandths - r; x += 1000)
        {
            for (var y = b.Min.Y.Thousandths + r; y <= b.Max.Y.Thousandths - r; y += 1000)
            {
                found.Add(((x - cx) * (x - cx) + (y - cy) * (y - cy), x, y));
            }
        }
        foreach (var (_, x, y) in found.OrderBy(f => f.D).ThenBy(f => f.X).ThenBy(f => f.Y))
        {
            var at = new Point2(Length.FromThousandths(x), Length.FromThousandths(y));
            if (CheckShrine(state, seat, at).IsLegal)
            {
                return at;
            }
        }
        return null;
    }

    // The Morale half test counts each shrine of the seat as 3 more models that are not Down or Out of Action, up to 9 (p. 82).
    public static int ShrineMoraleBonus(MatchState state, Seat seat) =>
        Math.Min(ShrineState.MaxBonus, ShrineState.MoraleBonus * state.Shrines.Count(h => h.Seat == seat));

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
            .Select(m => new Obstacle(BodyOf(state, m)!, m.Seat == mover.Seat ? Relation.Friendly : Relation.Enemy))
            .Concat(ShrineObstacles(state));
        return Movement.CheckPath(scenario.Battlefield, BodyOf(state, mover)!, path, Allowance(state, mover), others, sideClimb: true);
    }

    // Trench Warfare: the Mine Placement Area, the 12" of No Man's Land next to the defender's zone (p. 161, U93). Null
    // before the roles are chosen or when the zones do not face each other.
    public static Rect? MinePlacementArea(MatchState state)
    {
        if (ScenarioOf(state) is not { } scenario || state.Attacker is not { } attacker
            || state.SeatFor(attacker.Opponent()).ZoneId is not { } defending || state.SeatFor(attacker).ZoneId is not { } attacking)
        {
            return null;
        }
        return Mines.PlacementArea(scenario.Battlefield, defending, attacking);
    }

    // Where a move along this path from the model's place would halt for Mine Markers still on the table (p. 162).
    public static System.Collections.Immutable.ImmutableArray<MineStopPoint> MineStops(MatchState state, ModelState mover, IReadOnlyList<Point2> path) =>
        state.Mines.IsEmpty || ScenarioOf(state) is not { } scenario || BodyOf(state, mover) is not { } body
            ? []
            : Mines.Stops(scenario.Battlefield, body, path, state.Mines, AllBodies(state, mover.Id));

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

    // Roster stage R3: weapon 1 is the model's second ranged weapon (R4a: 2 its grenades); one it does not have is
    // refused as no weapon.
    public static AttackAssessment Assess(MatchState state, ModelState attacker, ModelState target, int weapon = 0)
    {
        var owned = state.Setup!.ProfileOf(attacker.Id)!;
        var profile = owned.Has(AttackKind.Ranged, weapon) ? owned.Using(AttackKind.Ranged, weapon) : owned;
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
        if (!owned.Has(AttackKind.Ranged, weapon))
        {
            return Refuse(ReasonCode.NoRangedWeapon, NoSuchGun(weapon));
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
            InCover: cover.InCover || HasCover(state, target),
            CoverTerrainId: cover.InCover ? cover.TerrainId : HasCover(state, target) ? CoverKeyword : null,
            AttackerLevel: cover.Sight.Attacker.Level,
            TargetLevel: cover.Sight.Target.Level,
            IntoMelee: intoMelee,
            Elevated: SightHeights.IsElevated(scenario.Battlefield, from, to));
    }

    // Roster stage R4b (U102): the models a BLAST attack at this target hits if it hits (p. 53): the target first, then,
    // in the setup's order, every other model on the table (Down ones included, either side, the attacker too) within
    // the blast radius of the target's base centre (Geometry.Blast, including vertically) that has Line of Sight to
    // the target, then the attacker's own models within 1" of an enemy model the blast hit. Empty for a weapon
    // without BLAST.
    public static List<ModelId> BlastHits(MatchState state, ModelState attacker, ModelState target, int weapon)
    {
        var radius = BlastRadius(state, attacker, weapon);
        if (radius == 0 || ScenarioOf(state) is not { } scenario || BodyOf(state, target) is not { } centre)
        {
            return [];
        }
        var battlefield = scenario.Battlefield;
        var height = SightHeights.StandingHeight(battlefield, centre, Elevation.Of(battlefield, centre));
        return Caught(state, attacker, centre.Centre, height, radius, target, body => Cover.Of(battlefield, body, centre).Sight.Visibility != Visibility.None).Prepend(target.Id).ToList();
    }

    // Roster stage R4c (U103): the models a scattered BLAST attack hits around the point it scattered to (p. 57): every
    // model on the table within the radius of the point (from the ground, platform or trench floor under it) that has
    // Line of Sight to the point, then the attacker's own models within 1" of an enemy model it hit; in the setup's
    // order. It may hit nobody.
    public static List<ModelId> BlastHitsAt(MatchState state, ModelState attacker, Point2 at, int weapon)
    {
        var radius = BlastRadius(state, attacker, weapon);
        if (radius == 0 || ScenarioOf(state) is not { } scenario)
        {
            return [];
        }
        var battlefield = scenario.Battlefield;
        var point = new ModelBody("point", at, HeightSight.PointRadius);
        var height = SightHeights.StandingHeight(battlefield, point, Elevation.Of(battlefield, point));
        return Caught(state, attacker, at, height, radius, null, body => HeightSight.SeesPoint(battlefield, body, at));
    }

    private static int BlastRadius(MatchState state, ModelState attacker, int weapon)
    {
        var owned = state.Setup!.ProfileOf(attacker.Id)!;
        return owned.Has(AttackKind.Ranged, weapon) ? owned.Using(AttackKind.Ranged, weapon).RangedWeaponBlast : 0;
    }

    // The models a blast centred there catches, besides the target (already hit, and first), in the setup's order.
    private static List<ModelId> Caught(MatchState state, ModelState attacker, Point2 centre, Length height, int radius, ModelState? target, Func<ModelBody, bool> sees)
    {
        var battlefield = ScenarioOf(state)!.Battlefield;
        var reach = Length.FromInches(radius);
        var others = OnTable(state).Where(m => m.Id != target?.Id).ToList();
        var hit = target is null ? new List<ModelState>() : new List<ModelState> { target };
        foreach (var other in others)
        {
            var body = BodyOf(state, other)!;
            var standing = SightHeights.StandingHeight(battlefield, body, Elevation.Of(battlefield, body));
            if (Geometry.Blast.Reaches(centre, height, body, standing, reach) && sees(body))
            {
                hit.Add(other);
            }
        }
        var enemies = hit.Where(h => h.Seat != attacker.Seat).Select(h => BodyOf(state, h)!).ToList();
        var near = others.Where(o => o.Seat == attacker.Seat && !hit.Contains(o)
            && enemies.Any(e => Measure.IsWithin(e, BodyOf(state, o)!, EngagementRange))).ToList();
        var order = state.Models.Select(m => m.Id).ToList();
        return hit.Where(h => h.Id != target?.Id).Concat(near).OrderBy(m => order.IndexOf(m.Id)).Select(m => m.Id).ToList();
    }

    // Roster stage R4c (U103): whether a scattering attack may land at this point: exactly the scatter distance from the
    // target's base centre (Geometry.Blast.IsScatterDistance), on the battlefield, and with Line of Sight between the
    // point and the target (HeightSight.SeesPoint, from the target). R5a (U105): an attack aimed at a point scatters
    // from that point, and the sight is from 1.1" above it, as from a model standing there.
    public static bool CanScatterTo(MatchState state, RollInProgress progress, Point2 at)
    {
        if (ScenarioOf(state) is not { } scenario || ScatterOrigin(state, progress) is not { } origin)
        {
            return false;
        }
        var battlefield = scenario.Battlefield;
        return OnBattlefield(battlefield, at)
            && Geometry.Blast.IsScatterDistance(origin.Centre, at, progress.ScatterInches)
            && HeightSight.SeesPoint(battlefield, origin, at);
    }

    private static ModelBody? ScatterOrigin(MatchState state, RollInProgress progress) =>
        progress.Target is { } aimed ? BodyOf(state, state.FindModel(aimed)!)
        : progress.BlastAt is { } point ? new ModelBody("point", point, HeightSight.PointRadius)
        : null;

    public static Point2? ScatterCentre(MatchState state, RollInProgress progress) => ScatterOrigin(state, progress)?.Centre;

    private static bool OnBattlefield(Battlefield battlefield, Point2 at) =>
        at.X >= Length.Zero && at.Y >= Length.Zero && at.X <= battlefield.Width && at.Y <= battlefield.Height;

    // The points, of the 360 one-degree directions (Geometry.Blast.ScatterPoint), where a scattering attack may land, in
    // direction order. None: the attack is a miss (p. 57).
    public static List<(int Degree, Point2 At)> ScatterPoints(MatchState state, RollInProgress progress)
    {
        var found = new List<(int, Point2)>();
        if (ScatterOrigin(state, progress) is not { } origin)
        {
            return found;
        }
        for (var degree = 0; degree < Geometry.Blast.ScatterDirections; degree++)
        {
            var at = Geometry.Blast.ScatterPoint(origin.Centre, progress.ScatterInches, degree);
            if (CanScatterTo(state, progress, at))
            {
                found.Add((degree, at));
            }
        }
        return found;
    }

    // Roster stage R5a (U105): a Ranged Attack with a BLAST weapon at a point (p. 53): the attacker as for any Ranged
    // Attack (on the table, standing, not within 1" of an enemy, with that weapon), the weapon with BLAST, the point on
    // the battlefield, in the attacker's Line of Sight (HeightSight.SeesPoint) and within the weapon's range of the
    // attacker's base (its centre within range plus the base radius). Long Range is beyond half that range. A point has
    // no cover and no height to shoot down at, so neither modifier applies.
    public static AttackAssessment AssessPoint(MatchState state, ModelState attacker, Point2 at, int weapon)
    {
        var owned = state.Setup!.ProfileOf(attacker.Id)!;
        if (ScenarioOf(state) is not { } scenario || BodyOf(state, attacker) is not { } from || attacker.Status == ModelStatus.OutOfAction)
        {
            return Refuse(ReasonCode.ModelNotOnTable, "The attacking model is not on the battlefield.");
        }
        if (attacker.Status != ModelStatus.Standing)
        {
            return Refuse(ReasonCode.ModelNotStanding, "A Down model cannot shoot.");
        }
        if (!owned.Has(AttackKind.Ranged, weapon))
        {
            return Refuse(ReasonCode.NoRangedWeapon, NoSuchGun(weapon));
        }
        var profile = owned.Using(AttackKind.Ranged, weapon);
        if (profile.RangedWeaponBlast == 0)
        {
            return Refuse(ReasonCode.NotACandidate, "Only a BLAST weapon may be aimed at a point.", RefBlast);
        }
        if (IsEngaged(state, attacker))
        {
            return Refuse(ReasonCode.AttackerEngaged, "A model within 1\" of an enemy cannot make a Ranged Attack.");
        }
        var battlefield = scenario.Battlefield;
        if (!OnBattlefield(battlefield, at))
        {
            return Refuse(ReasonCode.NotACandidate, "The point must be on the battlefield.", RefBlast);
        }
        if (!HeightSight.SeesPoint(battlefield, from, at))
        {
            return Refuse(ReasonCode.NoLineOfSight, "The point is not in the attacker's Line of Sight.", RefBlast);
        }
        if (!Reaches(from, at, profile.RangedRange))
        {
            return Refuse(ReasonCode.TargetOutOfRange, "The point is beyond the weapon's range.");
        }
        return new AttackAssessment(null, "", RefBlast, LongRange: !Reaches(from, at, Length.FromThousandths(profile.RangedRange.Thousandths / 2)),
            InCover: false, CoverTerrainId: null, AttackerLevel: Elevation.Of(battlefield, from).Level, TargetLevel: ModelLevel.Ground, IntoMelee: false, Elevated: false);
    }

    // Whether a point is within that distance of a base: its centre within the distance plus the radius. Exact.
    private static bool Reaches(ModelBody from, Point2 at, Length distance)
    {
        var dx = (Int128)(at.X.Thousandths - from.Centre.X.Thousandths);
        var dy = (Int128)(at.Y.Thousandths - from.Centre.Y.Thousandths);
        var reach = (Int128)(distance.Thousandths + from.Radius.Thousandths);
        return dx * dx + dy * dy <= reach * reach;
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
    public static (bool LongRange, bool InCover, string? CoverTerrainId, bool Elevated) ForcedShotFacts(MatchState state, ModelState attacker, ModelState victim, int weapon = 0)
    {
        var from = BodyOf(state, attacker)!;
        var to = BodyOf(state, victim)!;
        var profile = state.Setup!.ProfileOf(attacker.Id)!.Using(AttackKind.Ranged, weapon);
        var cover = Cover.Of(ScenarioOf(state)!.Battlefield, from, to);
        var half = Length.FromThousandths(profile.RangedRange.Thousandths / 2);
        var inCover = cover.InCover && cover.Sight.Visibility != Visibility.None;
        var keyword = !inCover && HasCover(state, victim);
        return (!Measure.IsWithin(from, to, half), inCover || keyword, inCover ? cover.TerrainId : keyword ? CoverKeyword : null, SightHeights.IsElevated(ScenarioOf(state)!.Battlefield, from, to));
    }

    // ---- Charge (pp. 34, 36-37) ----

    public static List<Obstacle> ObstaclesFor(MatchState state, ModelState mover) =>
        OnTable(state)
            .Where(m => m.Id != mover.Id)
            .Select(m => new Obstacle(BodyOf(state, m)!, m.Seat == mover.Seat ? Relation.Friendly : Relation.Enemy))
            .Concat(ShrineObstacles(state))
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
        if (from.Elevation != to.Elevation)
        {
            return new(ReasonCode.ChargeRouteBlocked, Charging.CrossLevelText, Charging.RefCrossLevel);
        }
        var route = Charging.CheckRoute(ScenarioOf(state)!.Battlefield, from, to, ChargeObstaclesFor(state, charger, from));
        return route.IsLegal ? null : new(ReasonCode.ChargeRouteBlocked, route.Text, route.RuleRef);
    }

    // The models a charge at this height can meet: those at the charger's elevation (all of them without towers).
    private static List<Obstacle> ChargeObstaclesFor(MatchState state, ModelState charger, ModelBody from) =>
        ObstaclesFor(state, charger).Where(o => o.Body.Elevation == from.Elevation).ToList();

    public static readonly Length ChargeRange = Length.FromInches(12);

    // Movement for a charge: Movement plus the Charge Bonus, at most 12", all halved after standing up (pp. 36, 49).
    public static Length ChargeReach(MatchState state, ModelState model, int bonus)
    {
        var movement = state.Setup!.ProfileOf(model.Id)!.Movement;
        var total = Length.FromThousandths(Math.Min(movement.Thousandths + bonus * 1000L, Charging.MaxMovement.Thousandths));
        return state.MovementHalved ? Length.FromThousandths(total.Thousandths / 2) : total;
    }

    public static ChargePlan PlanCharge(MatchState state, ModelState charger, ModelState target, int bonus, bool carryOn = false) =>
        Charging.Plan(ScenarioOf(state)!.Battlefield, BodyOf(state, charger)!, BodyOf(state, target)!, ChargeReach(state, charger, bonus), ChargeObstaclesFor(state, charger, BodyOf(state, charger)!), carryOn);

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
        var end = BodyAt(state, model, path[^1])!;
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
                && state.Setup!.ProfileOf(o.Id)!.CanMakeMeleeAttack && Measure.IsWithin(body, BodyOf(state, o)!, EngagementRange))
            .OrderBy(o => o.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    // ---- Melee Attacks (pp. 45-46) ----

    // Roster stage R3 (U100): BLOCK gives -1 DICE to a Melee Attack against its model when the attacker made a Charge
    // ACTION before the attack this turn (p. 53). Charges are only tracked in matches with a BLOCK model.
    public static bool Blocks(MatchState state, ModelId attacker, ModelId target) =>
        state.Setup?.ProfileOf(target) is { HasBlock: true } && !state.ChargedThisTurn.IsDefaultOrEmpty && state.ChargedThisTurn.Contains(attacker);

    // Roster stage R5b (U106): FEAR on the target and not on the attacker, nor NEGATE FEAR (pp. 54, 56).
    // Roster stage R6a (U117): a PRINCIPALITY attacker within 8" of a friendly Trench Cleric (Onward Christian Soldiers!)
    // has NEGATE FEAR.
    public static bool Fears(MatchState state, ModelId attacker, ModelId target) =>
        state.Setup?.ProfileOf(attacker) is { } a && state.Setup.ProfileOf(target) is { } t && AttackDice.Fears(a, t) && !Inspired(state, attacker);

    // A friendly model with Onward Christian Soldiers! on the battlefield within 8" of this PRINCIPALITY model (itself
    // included), Down or not.
    public static bool Inspired(MatchState state, ModelId model) =>
        state.Setup?.ProfileOf(model) is { } p && p.Traits.HasFlag(ModelTraits.Principality) && state.FindModel(model) is { } m && BodyOf(state, m) is { } body
        && state.Models.Any(c => c.Seat == m.Seat && state.Setup.ProfileOf(c.Id)!.InspiresFaithful
            && (c.Id == m.Id || BodyOf(state, c) is { } cleric && Measure.IsWithin(body, cleric, InspireReach)));

    private static readonly Length InspireReach = Length.FromInches(8);

    // Why a Shoot ACTION with that weapon is refused when the model does not have it.
    public static string NoSuchGun(int weapon) => weapon switch
    {
        1 => "That model has no second ranged weapon.",
        ModelProfile.GrenadeWeapon => "That model has no grenades.",
        _ => "That model has no Ranged Weapon.",
    };

    public static bool TracksCharges(MatchSetup setup) => setup.Models.Any(m => m.Profile?.HasBlock == true);

    public sealed record MeleeAssessment(Rejection? Refusal, bool InCover = false, string? CoverTerrainId = null);

    public static MeleeAssessment AssessMelee(MatchState state, ModelState attacker, ModelState target, int weapon = 0)
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
        var grind = weapon == ModelProfile.GrindWeapon;
        // Roster stage R6d (U120): a Trample ACTION is the same kind of ACTION attack, at any Down enemy.
        var trample = weapon == ModelProfile.TrampleWeapon;
        if (grind && !state.Setup!.ProfileOf(attacker.Id)!.GrindsToDust)
        {
            return new MeleeAssessment(new Rejection(ReasonCode.NoSuchAction, "Only a model with Piston Legs can take a Grind to Dust ACTION.", RefGrind));
        }
        if (trample && !state.Setup!.ProfileOf(attacker.Id)!.Tramples)
        {
            return new MeleeAssessment(new Rejection(ReasonCode.NoSuchAction, "Only a model whose unit has it can take a Trample ACTION.", RefTrample));
        }
        if (!grind && !trample && !state.Setup!.ProfileOf(attacker.Id)!.CanMakeMeleeAttack)
        {
            return Refuse(ReasonCode.NoMeleeWeapon, "That model has no Melee Weapon.");
        }
        if (!grind && !trample && weapon != 0 && (weapon < 0 || state.Setup!.ProfileOf(attacker.Id)!.Weapons(AttackKind.Melee) <= weapon))
        {
            return Refuse(ReasonCode.NoMeleeWeapon, "That model has no second Melee Weapon.");
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
        // Roster stage R5i (U114): Grind to Dust targets a Down enemy on a base of 32 mm or smaller (Warbands p. 66).
        if (grind && target.Status != ModelStatus.Down)
        {
            return new MeleeAssessment(new Rejection(ReasonCode.TargetNotDown, "A Grind to Dust ACTION must target a Down enemy model.", RefGrind));
        }
        if (trample && target.Status != ModelStatus.Down)
        {
            return new MeleeAssessment(new Rejection(ReasonCode.TargetNotDown, "A Trample ACTION must target a Down enemy model.", RefTrample));
        }
        if (grind && state.Setup!.ProfileOf(target.Id)!.BaseRadius > GrindBaseRadius)
        {
            return new MeleeAssessment(new Rejection(ReasonCode.TargetTooLarge, "A Grind to Dust ACTION must target a model on a base of 32 mm or smaller.", RefGrind));
        }
        var cover = Cover.Of(ScenarioOf(state)!.Battlefield, from, to);
        if (cover.Sight.Visibility == Visibility.None)
        {
            return Refuse(ReasonCode.NoLineOfSight, "The target must be in the attacker's Line of Sight.");
        }
        // Roster stage R5f (U110): a target with the COVER keyword has the Defended Obstacle modifier (p. 54).
        return cover.InCover ? new MeleeAssessment(null, true, cover.TerrainId)
            : HasCover(state, target) ? new MeleeAssessment(null, true, CoverKeyword)
            : new MeleeAssessment(null);
    }

    private static AttackAssessment Refuse(ReasonCode code, string text, string rule = RefAttack) => new(code, text, rule);

    public const string RefGrind = "Warbands 1.0.2 p. 66";

    public const string RefTrample = "Warbands 1.0.2 pp. 88, 140";

    // Roster stage R5j (U115): the models a Martyrdom Device hits (friend or foe on the battlefield within 3", Down ones
    // too, in the setup's order, the bearer last) and the other models within 1" of the bearer (Warbands p. 58).
    public static (ImmutableArray<ModelId> Hit, ImmutableArray<ModelId> Close) MartyrdomHits(MatchState state, ModelState bearer)
    {
        if (BodyOf(state, bearer) is not { } from)
        {
            return (ImmutableArray<ModelId>.Empty, ImmutableArray<ModelId>.Empty);
        }
        var near = state.Models
            .Where(m => m.Id != bearer.Id && m.Status != ModelStatus.OutOfAction && BodyOf(state, m) is { } to && Measure.IsWithin(from, to, MartyrdomReach))
            .ToList();
        var close = near.Where(m => Measure.IsWithin(from, BodyOf(state, m)!, EngagementRange)).Select(m => m.Id).ToImmutableArray();
        return (near.Select(m => m.Id).Append(bearer.Id).ToImmutableArray(), close);
    }

    private static readonly Length MartyrdomReach = Length.FromInches(3);
    public const string RefMartyrdom = "Warbands 1.0.2 p. 58";

    // A 32 mm base's radius, as content reads one (half the diameter in thousandths of an inch, rounded down).
    private static readonly Length GrindBaseRadius = Length.FromThousandths(Length.FromMillimetres(32).Thousandths / 2);

    // ---- Objectives (Scenario I, p. 152) ----

    // Trench Warfare (p. 163): each scoring trench section, with the defending and attacking models in it (U81: more
    // than half the base in the trench network, in the section the trench test names; Down models count, as they do
    // for Objectives) and the points each side scores for it.
    public static IReadOnlyList<Events.TrenchResult> TrenchResults(MatchState state)
    {
        var field = ScenarioOf(state)!.Battlefield;
        var attacker = state.Attacker!.Value;
        var defenders = new Dictionary<string, int>(StringComparer.Ordinal);
        var attackers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var model in OnTable(state))
        {
            if (Trenches.Of(field, BodyOf(state, model)!) is { } trench)
            {
                var counts = model.Seat == attacker ? attackers : defenders;
                counts[trench.Id] = counts.GetValueOrDefault(trench.Id) + 1;
            }
        }
        return [.. Scenarios.TrenchWarfare.ScoringSections(field).Select(t =>
        {
            var d = defenders.GetValueOrDefault(t.Id);
            var a = attackers.GetValueOrDefault(t.Id);
            var (dp, ap) = Scenarios.TrenchWarfare.Points(d, a);
            return new Events.TrenchResult(t.Id, d, a, dp, ap);
        })];
    }

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
            // A model on a tower is 2" above the ground piece, never on it or within 1" of it (U74).
            if (body.Elevation != Length.Zero)
            {
                continue;
            }
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
