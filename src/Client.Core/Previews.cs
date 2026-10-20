using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Core;

// M7.4 stage 4b: what a positional command would probably do, worked out from the server's state with the rules' own
// read-only geometry (Placement, Movement, Cover and LineOfSight, Measure, Charging, Elevation). A preview is advice:
// it decides nothing, rolls nothing and changes nothing. The server checks every command again and its answer, a
// refusal with its reason included, is what counts.
//
// The checks run in the same order and with the same words as the server's validation, so a preview refuses for the
// same reason the server would. Rules.Core keeps its own helpers internal; the few lines repeated here are kept equal
// by the agreement tests, which compare previews with the server's decisions over whole simulated matches.
public enum PreviewKind
{
    Place,
    Move,
    Dash,
    Retreat,
    Shoot,
    Charge,
    Fight,
}

// A point on the table, in inches.
public readonly record struct TablePoint(double X, double Y)
{
    public static TablePoint Of(Point2 p) => new(TableRect.Inches(p.X), TableRect.Inches(p.Y));

    public override string ToString() => $"({BoardScene.Inch(X)}, {BoardScene.Inch(Y)})";
}

// One of the rules' sight rays (SightResult.Rays): from the attacker's base centre to a point on the target's base.
// BlockedBy names the terrain piece that blocks it, or is null when the ray is clear.
public sealed record PreviewRay(TablePoint From, TablePoint To, string? BlockedBy)
{
    public bool Clear => BlockedBy is null;
}

// A point along a path with the distance used to get there, in inches (difficult terrain counts double, a ladder's climb
// counts its height), and the height it stands at (a tower's platform, else 0; towers proposal stage T4).
public sealed record PathMark(TablePoint At, double Inches, double Height = 0);

public sealed record Preview(
    PreviewKind Kind,
    ModelId Model,
    Command Command,
    Rejection? Refusal,
    IReadOnlyList<string> Facts,
    IReadOnlyList<PathMark> Path,
    double Radius,
    ModelId? Target,
    IReadOnlyList<PreviewRay> Rays,
    IReadOnlyList<PathMark> ChargePath,
    int? ChargeRollNeeded,
    double? Allowance = null,
    DicePreview? Dice = null)
{
    // For a Move, Dash or Retreat: inches used by the path (the rules' path cost), and inches of the allowance left after it.
    public double? Used => Kind is PreviewKind.Move or PreviewKind.Dash or PreviewKind.Retreat && Path.Count > 0 ? Path[^1].Inches : null;

    public double? Left => Allowance is { } a && Used is { } u ? Math.Max(0, a - u) : null;

    // The straight-line distance from the model's centre to the end of the path (the path may be longer).
    public double? Straight => Used is not null && Path.Count > 1
        ? Math.Sqrt(Math.Pow(Path[^1].At.X - Path[0].At.X, 2) + Math.Pow(Path[^1].At.Y - Path[0].At.Y, 2))
        : null;

    // Shown with every preview, in words, on the battlefield and beside it.
    public const string Caveat = "preview; the server decides";

    public bool Legal => Refusal is null;

    public string Verb => Kind switch
    {
        PreviewKind.Place => "Place",
        PreviewKind.Move => "Move",
        PreviewKind.Dash => "Dash",
        PreviewKind.Retreat => "Retreat",
        PreviewKind.Shoot => "Shoot",
        PreviewKind.Charge => "Charge",
        _ => "Fight",
    };

    // One line: what the server would probably say, and the rule it would cite.
    public string Headline => Refusal is { } r
        ? $"{Verb}: refused. {r.Text} ({r.RuleRef}; {Caveat})"
        : $"{Verb}: looks legal ({Caveat})";

    // The words drawn on the battlefield at the end of the path or at the target.
    public string Tag => $"{Verb.ToUpperInvariant()} {(Refusal is null ? "LOOKS LEGAL" : "REFUSED")} ({Caveat})";

    public IEnumerable<string> Lines()
    {
        yield return Headline;
        foreach (var fact in Facts)
        {
            yield return fact;
        }
    }
}

public static class Previews
{
    private const string RefTurn = "1.0.2 p. 32";
    private const string RefDeployment = "1.0.2 p. 152";
    private const string RefActions = "1.0.2 p. 34";
    private const string RefAttack = "1.0.2 pp. 42-44";
    private const string RefMove = "1.0.2 pp. 34-35";
    private const string RefCharge = "1.0.2 pp. 34, 36-37";
    private const string RefRetreat = "1.0.2 pp. 35, 37";
    private const string RefMelee = "1.0.2 pp. 45-46, 49";
    private const string RefExcluded = "1.0.2 pp. 34, 42";
    private const string OneOfThree = "A model can take a Move or a Charge or a Retreat ACTION, but not more than one.";
    private const string ShootAndMelee = "A model cannot take a Shoot ACTION and a Charge or Fight ACTION in the same activation (no ASSAULT weapons yet).";
    private static readonly Length Engagement = Length.Inch;
    private static readonly Length ChargeRange = Length.FromInches(12);
    private const int ChargeDieFaces = 6;

    // ---- placing a model during Deployment ----

    public static Preview Place(MatchState state, Seat seat, ModelId model, Point2 at)
    {
        ArgumentNullException.ThrowIfNull(state);
        var command = new PlaceModel(seat, model, at);
        var radius = RadiusOf(state, model);
        var facts = new List<string> { $"At {TablePoint.Of(at)}." };
        var refusal = Opening(state) ?? CheckPlace(state, seat, model, at, facts);
        return new Preview(PreviewKind.Place, model, command, refusal, facts, [new PathMark(TablePoint.Of(at), 0)], radius, null, [], [], null);
    }

    private static Rejection? CheckPlace(MatchState state, Seat seat, ModelId id, Point2 at, List<string> facts)
    {
        if (state.Setup!.Scenario is not { } scenario)
        {
            return new(ReasonCode.NoBattlefield, "This match has no battlefield; use DeployModel.", RefDeployment);
        }
        if (state.Phase != Phase.Deployment)
        {
            return new(ReasonCode.WrongPhase, "Models are set up only during Deployment.", RefDeployment);
        }
        if (MatchView.NextDeployer(state) != seat)
        {
            return new(ReasonCode.NotYourTurn, "It is the other player's turn to deploy.", RefDeployment);
        }
        var model = state.FindModel(id);
        if (model is null)
        {
            return new(ReasonCode.UnknownModel, "There is no such model.", RefDeployment);
        }
        if (model.Seat != seat)
        {
            return new(ReasonCode.NotYourModel, "That model belongs to the other player.", RefDeployment);
        }
        if (model.Deployed)
        {
            return new(ReasonCode.ModelAlreadyDeployed, "That model is already deployed.", RefDeployment);
        }
        var zone = state.SeatFor(seat).ZoneId;
        if (zone is not null)
        {
            facts.Add($"Your deployment zone: {zone}.");
        }
        var body = new ModelBody(id.Value, at, state.Setup.ProfileOf(id)!.BaseRadius);
        var result = Placement.Check(scenario.Battlefield, body, Bodies(state, except: id), zone);
        return result.IsLegal ? null : new(ReasonCode.PlacementIllegal, result.Text, result.RuleRef);
    }

    // ---- Move, Dash and Retreat: a path of waypoints ----

    public static Preview Path(MatchState state, Seat seat, PreviewKind kind, ModelId model, IReadOnlyList<Point2> waypoints)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(waypoints);
        var path = waypoints.ToImmutableArray();
        Command command = kind switch
        {
            PreviewKind.Move => new MoveModel(seat, model, path),
            PreviewKind.Dash => new DashModel(seat, model, path),
            PreviewKind.Retreat => new RetreatModel(seat, model, path),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "Move, Dash or Retreat."),
        };
        var facts = new List<string>();
        var refusal = Opening(state) ?? (kind == PreviewKind.Retreat ? CheckRetreat(state, seat, model, path, facts) : CheckMove(state, seat, model, path, kind, facts));
        double? allowance = state.FindModel(model) is { } mover && state.Setup?.ProfileOf(model) is not null ? TableRect.Inches(Allowance(state, mover)) : null;
        var marks = Marks(state, model, path);
        // Towers proposal, stage T4: a path that goes up or down a ladder says so, and what the climb costs (U66).
        var climbs = marks.Zip(marks.Skip(1)).Where(m => m.First.Height != m.Second.Height).Select(m => Math.Abs(m.Second.Height - m.First.Height)).ToList();
        if (climbs.Count > 0)
        {
            facts.Add($"{(climbs.Count == 1 ? "One ladder climb" : $"{climbs.Count} ladder climbs")}: each counts its {BoardScene.Inch(climbs[0])} height in the distance (U66).");
        }
        return new Preview(kind, model, command, refusal, facts, marks, RadiusOf(state, model), null, [], [], null, allowance);
    }

    private static Rejection? CheckMove(MatchState state, Seat seat, ModelId id, ImmutableArray<Point2> path, PreviewKind kind, List<string> facts)
    {
        if (Acting(state, seat, id, out var model) is { } rejection)
        {
            return rejection;
        }
        var action = kind == PreviewKind.Move ? ActionsTaken.Move : ActionsTaken.Dash;
        if (state.Actions.HasFlag(action))
        {
            return new(ReasonCode.ActionAlreadyTaken, $"A {action} ACTION can be taken only once per activation.", RefActions);
        }
        if (action == ActionsTaken.Move && (state.Actions & (ActionsTaken.Charge | ActionsTaken.Retreat)) != 0)
        {
            return new(ReasonCode.ActionAlreadyTaken, OneOfThree, RefActions);
        }
        if (path.Length > Rules.Core.Events.ModelMoved.MaxWaypoints)
        {
            return new(ReasonCode.TooManyWaypoints, $"A move has at most {Rules.Core.Events.ModelMoved.MaxWaypoints} waypoints.", RefMove);
        }
        var result = Movement.CheckPath(state.Setup!.Scenario!.Battlefield, BodyOf(state, model!)!, path, Allowance(state, model!), Obstacles(state, model!));
        facts.Add($"Distance {Inch(result.Cost)} of {Inch(Allowance(state, model!))} allowed{(state.MovementHalved ? " (halved after standing up)" : "")}.");
        if (kind == PreviewKind.Dash)
        {
            facts.Add("A Dash is a Risky Success Roll: the dice decide whether the model moves (server).");
        }
        return result.IsLegal ? null : new(ReasonCode.MoveIllegal, result.Text, result.RuleRef);
    }

    // A legal Fight target is engaged with the attacker: within 1" (pp. 45-46).
    public const string EngagedFact = "Engaged: within 1\" of the attacker.";

    // Proposal J2b (decision D2): who could make a free attack if the model retreated now, by the rules' own test.
    public static string FreeAttackFact(IReadOnlyList<ModelId> attackers) => attackers.Count == 0
        ? "No enemy can make a free attack now."
        : $"Free attacks possible from: {string.Join(", ", attackers.Select(a => a.Value.ToUpperInvariant()))} (each may attack before the model moves; the server decides).";

    private static Rejection? CheckRetreat(MatchState state, Seat seat, ModelId id, ImmutableArray<Point2> path, List<string> facts)
    {
        if (Acting(state, seat, id, out var model) is { } rejection)
        {
            return rejection;
        }
        if ((state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) != 0)
        {
            return new(ReasonCode.ActionAlreadyTaken, OneOfThree, RefActions);
        }
        if (path.IsEmpty)
        {
            return new(ReasonCode.MoveIllegal, "A retreat needs at least one point to move to.", RefRetreat);
        }
        if (path.Length > Rules.Core.Events.ModelMoved.MaxWaypoints)
        {
            return new(ReasonCode.TooManyWaypoints, $"A move has at most {Rules.Core.Events.ModelMoved.MaxWaypoints} waypoints.", RefMove);
        }
        if (!IsEngaged(state, model!))
        {
            return new(ReasonCode.NotEngaged, "Only a model within 1\" of an enemy can Retreat.", RefRetreat);
        }
        var result = Movement.CheckPath(state.Setup!.Scenario!.Battlefield, BodyOf(state, model!)!, path, Allowance(state, model!), Obstacles(state, model!), retreat: true);
        facts.Add($"Distance {Inch(result.Cost)} of {Inch(Allowance(state, model!))} allowed.");
        facts.Add(FreeAttackFact(Rules.Core.Engagement.FreeAttackersIfRetreating(state, id)));
        if (!result.IsLegal)
        {
            return new(ReasonCode.MoveIllegal, result.Text, result.RuleRef);
        }
        var end = BodyAt(state, model!, path[^1])!;
        return OnTable(state).Any(o => o.Seat != model!.Seat && Measure.IsWithin(end, BodyOf(state, o)!, Engagement))
            ? new(ReasonCode.RetreatEndsEngaged, "A retreating model must end its move more than 1\" from all enemy models.", "1.0.2 p. 37")
            : null;
    }

    // ---- Shoot, Charge and Fight: a target ----

    public static Preview Target(MatchState state, Seat seat, PreviewKind kind, ModelId model, ModelId target)
    {
        ArgumentNullException.ThrowIfNull(state);
        Command command = kind switch
        {
            PreviewKind.Shoot => new Shoot(seat, model, target),
            PreviewKind.Charge => new ChargeModel(seat, model, target),
            PreviewKind.Fight => new FightModel(seat, model, target),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "Shoot, Charge or Fight."),
        };
        var facts = new List<string>();
        var rays = new List<PreviewRay>();
        var charge = new List<PathMark>();
        int? needed = null;
        DicePreview? dice = null;
        var refusal = Opening(state) ?? kind switch
        {
            PreviewKind.Shoot => CheckShoot(state, seat, model, target, facts, rays, ref dice),
            PreviewKind.Charge => CheckCharge(state, seat, model, target, facts, rays, charge, ref needed),
            _ => CheckFight(state, seat, model, target, facts, rays, ref dice),
        };
        // Proposal J2b (decision D1): whether a Fight target can be chosen is the rules' own answer (TurnSequence.Check,
        // the check Decide makes); the facts and dice above only describe it.
        if (kind == PreviewKind.Fight && Opening(state) is null)
        {
            refusal = TurnSequence.Check(state, command);
            if (refusal is null)
            {
                facts.Add(EngagedFact);
            }
        }
        var from = state.FindModel(model) is { } m && BodyOf(state, m) is { } b ? [new PathMark(TablePoint.Of(b.Centre), 0)] : Array.Empty<PathMark>();
        return new Preview(kind, model, command, refusal, facts, from, RadiusOf(state, model), target, rays, charge, needed, Dice: refusal is null ? dice : null);
    }

    private static Rejection? CheckShoot(MatchState state, Seat seat, ModelId id, ModelId targetId, List<string> facts, List<PreviewRay> rays, ref DicePreview? dice)
    {
        if (Acting(state, seat, id, out var attacker) is { } rejection)
        {
            return rejection;
        }
        if (state.Actions.HasFlag(ActionsTaken.Shoot))
        {
            return new(ReasonCode.ActionAlreadyTaken, "A Shoot ACTION can be taken only once per activation.", RefActions);
        }
        if ((state.Actions & (ActionsTaken.Charge | ActionsTaken.Fight)) != 0)
        {
            return new(ReasonCode.ActionExcluded, ShootAndMelee, RefExcluded);
        }
        if (state.FindModel(targetId) is not { } target)
        {
            return new(ReasonCode.UnknownModel, "There is no such target.", RefAttack);
        }
        var profile = state.Setup!.ProfileOf(attacker!.Id)!;
        var from = BodyOf(state, attacker)!;
        if (!profile.HasRangedWeapon)
        {
            return new(ReasonCode.NoRangedWeapon, "That model has no Ranged Weapon.", RefAttack);
        }
        if (target.Seat == attacker.Seat)
        {
            return new(ReasonCode.TargetNotEnemy, "The target must be an enemy model.", RefAttack);
        }
        var to = BodyOf(state, target);
        if (to is null || target.Status == ModelStatus.OutOfAction)
        {
            return new(ReasonCode.TargetNotOnTable, "The target is not on the battlefield.", RefAttack);
        }
        facts.Add($"Distance {Inch(Measure.Gap(from, to))}, weapon range {Inch(profile.RangedRange)}.");
        if (IsEngaged(state, attacker))
        {
            return new(ReasonCode.AttackerEngaged, "A model within 1\" of an enemy cannot make a Ranged Attack.", RefAttack);
        }
        var cover = Sight(state, from, to, facts, rays);
        if (cover.Sight.Visibility == Visibility.None)
        {
            return new(ReasonCode.NoLineOfSight, "The target is not in the attacker's Line of Sight.", "1.0.2 p. 30");
        }
        if (!Measure.IsWithin(from, to, profile.RangedRange))
        {
            return new(ReasonCode.TargetOutOfRange, "The target is beyond the weapon's range.", RefAttack);
        }
        var longRange = !Measure.IsWithin(from, to, Length.FromThousandths(profile.RangedRange.Thousandths / 2));
        facts.Add(longRange ? "Long range: beyond half the weapon's range (1 penalty die)." : "Short range: within half the weapon's range.");
        var intoMelee = OnTable(state).Any(o => o.Seat == attacker.Seat && o.Id != attacker.Id && Measure.IsWithin(to, BodyOf(state, o)!, Engagement));
        if (intoMelee)
        {
            facts.Add("Shooting into melee: one of your models is within 1\" of the target, so the shot may hit it (the server rolls).");
        }
        dice = DicePreview.Of(state, attacker, target, AttackKind.Ranged, AttackDice.ShotPenalty(longRange, cover.InCover),
            [.. new[] { longRange ? "long range" : null, cover.InCover ? "cover" : null }.OfType<string>()]);
        return null;
    }

    private static Rejection? CheckCharge(MatchState state, Seat seat, ModelId id, ModelId targetId, List<string> facts, List<PreviewRay> rays, List<PathMark> charge, ref int? needed)
    {
        if (Acting(state, seat, id, out var charger) is { } rejection)
        {
            return rejection;
        }
        if ((state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) != 0)
        {
            return new(ReasonCode.ActionAlreadyTaken, OneOfThree, RefActions);
        }
        if (state.Actions.HasFlag(ActionsTaken.Shoot))
        {
            return new(ReasonCode.ActionExcluded, ShootAndMelee, RefExcluded);
        }
        if (state.FindModel(targetId) is not { } target)
        {
            return new(ReasonCode.UnknownModel, "There is no such target.", RefCharge);
        }
        var from = BodyOf(state, charger!)!;
        if (target.Seat == charger!.Seat)
        {
            return new(ReasonCode.TargetNotEnemy, "The target of a charge must be an enemy model.", RefCharge);
        }
        var to = BodyOf(state, target);
        if (to is null || target.Status == ModelStatus.OutOfAction)
        {
            return new(ReasonCode.TargetNotOnTable, "The target is not on the battlefield.", RefCharge);
        }
        facts.Add($"Distance {Inch(Measure.Gap(from, to))}; a charge target must be within 12\".");
        if (IsEngaged(state, charger))
        {
            return new(ReasonCode.AlreadyEngaged, "A model already within 1\" of an enemy cannot take a Charge ACTION.", "1.0.2 p. 34");
        }
        var field = state.Setup!.Scenario!.Battlefield;
        var cover = Sight(state, from, to, facts, rays);
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
        var others = Obstacles(state, charger).Where(o => o.Body.Elevation == from.Elevation).ToList();
        var route = Charging.CheckRoute(field, from, to, others);
        if (!route.IsLegal)
        {
            return new(ReasonCode.ChargeRouteBlocked, route.Text, route.RuleRef);
        }
        // Charge reach: Movement plus the Charge Bonus die, at most 12", halved after standing up (pp. 36, 49). The die
        // is the server's to roll; the preview only says which results would reach.
        ChargePlan? plan = null;
        for (var die = 1; die <= ChargeDieFaces; die++)
        {
            plan = Charging.Plan(field, from, to, ChargeReach(state, charger, die), others);
            if (plan.Reached)
            {
                needed = die;
                break;
            }
        }
        var movement = state.Setup.ProfileOf(charger.Id)!.Movement;
        facts.Add($"Charge reach: Movement {Inch(movement)} plus a D6, at most 12\"{(state.MovementHalved ? ", halved after standing up" : "")}.");
        facts.Add(needed is { } n
            ? n == 1 ? "Reaches base contact on any Charge roll." : $"Reaches base contact on a Charge roll of {n} or more; less stops short."
            : "Would not reach base contact even on a 6; the model would stop short.");
        if (plan is not null)
        {
            charge.AddRange(Marks(state, charger.Id, plan.Path));
        }
        return null;
    }

    private static Rejection? CheckFight(MatchState state, Seat seat, ModelId id, ModelId targetId, List<string> facts, List<PreviewRay> rays, ref DicePreview? dice)
    {
        if (Acting(state, seat, id, out var attacker) is { } rejection)
        {
            return rejection;
        }
        if (state.Actions.HasFlag(ActionsTaken.Fight))
        {
            return new(ReasonCode.ActionAlreadyTaken, "A Fight ACTION can be taken only once per activation.", RefActions);
        }
        if (state.Actions.HasFlag(ActionsTaken.Shoot))
        {
            return new(ReasonCode.ActionExcluded, ShootAndMelee, RefExcluded);
        }
        if (state.FindModel(targetId) is not { } target)
        {
            return new(ReasonCode.UnknownModel, "There is no such target.", RefMelee);
        }
        var from = BodyOf(state, attacker!)!;
        if (!state.Setup!.ProfileOf(attacker!.Id)!.HasMeleeWeapon)
        {
            return new(ReasonCode.NoMeleeWeapon, "That model has no Melee Weapon.", RefMelee);
        }
        if (target.Seat == attacker.Seat)
        {
            return new(ReasonCode.TargetNotEnemy, "The target must be an enemy model.", RefMelee);
        }
        var to = BodyOf(state, target);
        if (to is null || target.Status == ModelStatus.OutOfAction)
        {
            return new(ReasonCode.TargetNotOnTable, "The target is not on the battlefield.", RefMelee);
        }
        facts.Add($"Distance {Inch(Measure.Gap(from, to))}; a Melee Attack needs the target within 1\".");
        if (!Measure.IsWithin(from, to, Engagement))
        {
            return new(ReasonCode.TargetNotInMelee, "The target of a Melee Attack must be within 1\".", RefMelee);
        }
        var cover = Sight(state, from, to, facts, rays);
        if (cover.Sight.Visibility == Visibility.None)
        {
            return new(ReasonCode.NoLineOfSight, "The target must be in the attacker's Line of Sight.", RefMelee);
        }
        dice = DicePreview.Of(state, attacker, target, AttackKind.Melee, AttackDice.FightPenalty(cover.InCover),
            cover.InCover ? ["defended obstacle"] : []);
        return null;
    }

    // ---- which actions to offer (the action bar) ----

    // Whether the acting model can take this kind of action at all now, before a path or a target is chosen: the same
    // checks the previews make (and so the server), in the same words. For Shoot, Charge and Fight that means at least
    // one enemy on the table the preview would accept; the refusal is then the nearest enemy's. Advisory only: it
    // decides which buttons the action bar shows, never what the server accepts.
    public static Rejection? ActionOpen(MatchState state, Seat seat, ModelId model, PreviewKind kind)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (Opening(state) is { } closed)
        {
            return closed;
        }
        if (Acting(state, seat, model, out var acting) is { } rejection)
        {
            return rejection;
        }
        switch (kind)
        {
            case PreviewKind.Move:
                if (state.Actions.HasFlag(ActionsTaken.Move))
                {
                    return new(ReasonCode.ActionAlreadyTaken, "A Move ACTION can be taken only once per activation.", RefActions);
                }
                return (state.Actions & (ActionsTaken.Charge | ActionsTaken.Retreat)) != 0 ? new(ReasonCode.ActionAlreadyTaken, OneOfThree, RefActions) : null;
            case PreviewKind.Dash:
                return state.Actions.HasFlag(ActionsTaken.Dash) ? new(ReasonCode.ActionAlreadyTaken, "A Dash ACTION can be taken only once per activation.", RefActions) : null;
            case PreviewKind.Retreat:
                if ((state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) != 0)
                {
                    return new(ReasonCode.ActionAlreadyTaken, OneOfThree, RefActions);
                }
                return IsEngaged(state, acting!) ? null : new(ReasonCode.NotEngaged, "Only a model within 1\" of an enemy can Retreat.", RefRetreat);
            case PreviewKind.Shoot or PreviewKind.Charge or PreviewKind.Fight:
                Rejection? nearest = null;
                foreach (var enemy in EnemiesByDistance(state, acting!))
                {
                    if (Target(state, seat, kind, model, enemy.Id).Refusal is not { } refusal)
                    {
                        return null;
                    }
                    nearest ??= refusal;
                }
                return nearest ?? new(ReasonCode.TargetNotOnTable, "No enemy model is on the battlefield.", RefAttack);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), "Move, Dash, Retreat, Shoot, Charge or Fight.");
        }
    }

    // How far the acting model may move in a Move, Dash or Retreat (inches), to draw its reach; null when it cannot act.
    public static double? Reach(MatchState state, Seat seat, ModelId model)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Opening(state) is null && Acting(state, seat, model, out var acting) is null ? TableRect.Inches(Allowance(state, acting!)) : null;
    }

    // The state as the server would hold it just after ActivateModel(seat, model), for previewing a unit the player has
    // only chosen (nothing is sent until its first action). A preview-only copy: never sent, never shown as the match.
    // Null when the rules do not offer that activation now (TurnSequence.LegalCommands). A test checks it has the same
    // state hash as the server's state after TurnSequence.Decide(ActivateModel) for every activation it offers.
    public static MatchState? AsIfActivated(MatchState state, Seat seat, ModelId model)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ActiveModel is not null || state.Pending is not null || state.FindModel(model) is not { } found
            || !TurnSequence.LegalCommands(state).Contains(new ActivateModel(seat, model)))
        {
            return null;
        }
        // ActivationStarted: the model is activated, it is the active model and its seat activated last (Reducer).
        var standsUp = state.Setup?.Scenario is not null && found.Status == ModelStatus.Down;
        var activated = found with { ActivatedThisTurn = true, Status = standsUp ? ModelStatus.Standing : found.Status };
        var models = state.Models.Select(m => m.Id == model ? activated : m).ToImmutableArray();
        // ModelStoodUp (U30): a Down model stands up as its activation starts, with its Movement halved.
        return state with { Models = models, ActiveModel = model, LastActivator = seat, Actions = ActionsTaken.None, MovementHalved = standsUp };
    }

    // Towers proposal, stage T4: the path a click's points make for this model, with a ladder's foot and top put in
    // wherever the points change level (TowerRoute). The points themselves on a battlefield without towers.
    public static List<Point2> Route(MatchState state, ModelId model, IReadOnlyList<Point2> points)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(points);
        return state.Setup?.Scenario is { } scenario && state.FindModel(model) is { Position: { } at } && state.Setup.ProfileOf(model) is { } profile
            ? TowerRoute.Expand(scenario.Battlefield, profile.BaseRadius, at, points)
            : [.. points];
    }

    // Where the acting model could end a Move, Dash or Retreat with one more straight leg after the fixed waypoints: every
    // point of a grid (step inches apart) whose path the preview checks accept, so the area follows the rules' own path
    // check (allowance after the waypoints already used, terrain, other models, a Retreat's end more than 1" from enemies)
    // and agrees with the server as Path does. Empty when the action is not open.
    public static IReadOnlyList<TablePoint> Reachable(MatchState state, Seat seat, PreviewKind kind, ModelId model, IReadOnlyList<Point2> waypoints, double step = 0.5)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(waypoints);
        if (kind is not (PreviewKind.Move or PreviewKind.Dash or PreviewKind.Retreat) || ActionOpen(state, seat, model, kind) is not null
            || Reach(state, seat, model) is not { } reach || state.FindModel(model)?.Position is not { } at || state.Setup?.Scenario is not { } scenario)
        {
            return [];
        }
        var from = waypoints.Count > 0 ? TablePoint.Of(waypoints[^1]) : TablePoint.Of(at);
        // On a battlefield with towers each point is reached as a click would reach it, by a ladder if it is on another
        // level (Route); the area then shows the platform tops a click can reach.
        var used = waypoints.Count > 0 ? Path(state, seat, kind, model, Route(state, model, waypoints)).Path[^1].Inches : 0;
        // A path never costs less than its straight length, so nothing beyond the allowance left can be reached.
        var left = reach - used;
        var width = TableRect.Inches(scenario.Battlefield.Width);
        var depth = TableRect.Inches(scenario.Battlefield.Height);
        var points = new List<TablePoint>();
        if (left <= 0 || waypoints.Count >= Rules.Core.Events.ModelMoved.MaxWaypoints)
        {
            return points;
        }
        var path = new List<Point2>(waypoints) { default };
        for (var x = Math.Max(0, Math.Ceiling((from.X - left) / step) * step); x <= Math.Min(width, from.X + left); x += step)
        {
            for (var y = Math.Max(0, Math.Ceiling((from.Y - left) / step) * step); y <= Math.Min(depth, from.Y + left); y += step)
            {
                var (dx, dy) = (x - from.X, y - from.Y);
                if (dx * dx + dy * dy > left * left || (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9))
                {
                    continue;
                }
                path[^1] = PointAt(x, y);
                if (Path(state, seat, kind, model, Route(state, model, path)).Legal)
                {
                    points.Add(new TablePoint(Math.Round(x, 3), Math.Round(y, 3)));
                }
            }
        }
        return points;
    }

    // Enemy models on the table that are not Out of Action, nearest first (the order targets are offered in).
    public static IReadOnlyList<ModelState> EnemiesByDistance(MatchState state, ModelState from)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(from);
        if (from.Position is not { } at)
        {
            return [];
        }
        return [.. OnTable(state)
            .Where(m => m.Seat != from.Seat && m.Status != ModelStatus.OutOfAction)
            .OrderBy(m => Squared(at, m.Position!.Value))
            .ThenBy(m => m.Id.Value, StringComparer.Ordinal)];
    }

    private static double Squared(Point2 a, Point2 b)
    {
        var dx = TableRect.Inches(a.X) - TableRect.Inches(b.X);
        var dy = TableRect.Inches(a.Y) - TableRect.Inches(b.Y);
        return dx * dx + dy * dy;
    }

    // ---- shared checks ----

    // What the server refuses before it looks at the command itself: no match, an ended match, a pending decision.
    private static Rejection? Opening(MatchState state) => state.Phase switch
    {
        Phase.None => new(ReasonCode.MatchNotCreated, "No match has been created.", RefTurn),
        Phase.Ended => new(ReasonCode.MatchEnded, "The match has ended.", "1.0.2 p. 51"),
        Phase.PreGame => new(ReasonCode.MatchNotStarted, "The match has not been started.", RefTurn),
        _ when state.Pending is { } waiting => new(ReasonCode.DecisionPending, $"A decision must be made first ({waiting.Kind}).", RefTurn),
        _ => null,
    };

    // The checks every action of the active model shares.
    private static Rejection? Acting(MatchState state, Seat seat, ModelId id, out ModelState? model)
    {
        model = null;
        if (state.Setup!.Scenario is null)
        {
            return new(ReasonCode.NoBattlefield, "This match has no battlefield.", RefActions);
        }
        if (state.Phase != Phase.Activation)
        {
            return new(ReasonCode.WrongPhase, "Actions are taken only during the Activation Phase.", RefActions);
        }
        if (state.ActiveModel is not { } active)
        {
            return new(ReasonCode.NoActivationInProgress, "No model is activating.", RefActions);
        }
        var found = state.FindModel(id);
        if (found is null)
        {
            return new(ReasonCode.UnknownModel, "There is no such model.", RefActions);
        }
        if (found.Seat != seat)
        {
            return new(ReasonCode.NotYourModel, "That model belongs to the other player.", RefActions);
        }
        if (active != id)
        {
            return new(ReasonCode.ActivationInProgress, "Only the activating model can take actions.", RefActions);
        }
        if (found.Status != ModelStatus.Standing)
        {
            return new(ReasonCode.ModelNotStanding, "A Down model cannot take that action.", RefActions);
        }
        if (found.Position is null)
        {
            return new(ReasonCode.ModelNotOnTable, "The model is not on the battlefield.", RefActions);
        }
        model = found;
        return null;
    }

    // Line of sight and cover from the rules (Cover.Of, which asks LineOfSight.Between): the facts in words and the rays
    // to draw. Never a 3D raycast: the picture's heights play no part (M7.4 Q4).
    private static CoverResult Sight(MatchState state, ModelBody from, ModelBody to, List<string> facts, List<PreviewRay> rays)
    {
        var cover = Cover.Of(state.Setup!.Scenario!.Battlefield, from, to);
        rays.AddRange(cover.Sight.Rays.Select(r => new PreviewRay(TablePoint.Of(r.From), TablePoint.Of(r.To), r.BlockedBy)));
        var seeing = cover.Sight.Visibility switch
        {
            Visibility.None => "not visible",
            Visibility.Partial => "partly visible",
            _ => "fully visible",
        };
        var blockers = cover.Sight.Rays.Where(r => r.BlockedBy is not null).Select(r => r.BlockedBy!).Distinct().ToList();
        facts.Add($"Line of sight: {seeing}, {cover.Sight.Visible} of {cover.Sight.Total} sight rays clear{(blockers.Count > 0 ? $", blocked by {string.Join(", ", blockers)}" : "")}.");
        facts.Add(cover.InCover ? $"Target in cover ({cover.TerrainId})." : "Target not in cover.");
        if (cover.Sight.Attacker.Level == ModelLevel.High)
        {
            facts.Add("Attacker on high ground: it sees over ground-level walls.");
        }
        return cover;
    }

    // The rules' distance between two models on the table, base edge to base edge (as the attack previews measure it), in
    // words ("7.4\""); null when either is not on the table.
    public static string? Gap(MatchState state, ModelId a, ModelId b)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.FindModel(a) is { } first && state.FindModel(b) is { } second && first.Status != ModelStatus.OutOfAction
            && second.Status != ModelStatus.OutOfAction && BodyOf(state, first) is { } x && BodyOf(state, second) is { } y
            ? Inch(Measure.Gap(x, y))
            : null;
    }

    private static IEnumerable<ModelState> OnTable(MatchState state) => state.Models.Where(m => m.Position is not null);

    private static ModelBody? BodyOf(MatchState state, ModelState model) =>
        model.Position is { } at ? BodyAt(state, model, at) : null;

    // At the elevation the battlefield gives the point (a tower's platform or the ground), as Board.BodyAt does.
    private static ModelBody? BodyAt(MatchState state, ModelState model, Point2 at) =>
        state.Setup!.ProfileOf(model.Id) is { } profile
            ? new ModelBody(model.Id.Value, at, profile.BaseRadius, state.Setup.Scenario is { } scenario ? Towers.ElevationAt(scenario.Battlefield, at) : Length.Zero)
            : null;

    private static List<ModelBody> Bodies(MatchState state, ModelId except) =>
        OnTable(state).Where(m => m.Id != except).Select(m => BodyOf(state, m)!).ToList();

    private static List<Obstacle> Obstacles(MatchState state, ModelState mover) =>
        OnTable(state).Where(m => m.Id != mover.Id)
            .Select(m => new Obstacle(BodyOf(state, m)!, m.Seat == mover.Seat ? Relation.Friendly : Relation.Enemy))
            .ToList();

    private static bool IsEngaged(MatchState state, ModelState model) =>
        BodyOf(state, model) is { } body && OnTable(state).Any(o => o.Seat != model.Seat && Measure.IsWithin(body, BodyOf(state, o)!, Engagement));

    private static Length Allowance(MatchState state, ModelState model)
    {
        var movement = state.Setup!.ProfileOf(model.Id)!.Movement;
        return state.MovementHalved ? Length.FromThousandths(movement.Thousandths / 2) : movement;
    }

    private static Length ChargeReach(MatchState state, ModelState model, int bonus)
    {
        var movement = state.Setup!.ProfileOf(model.Id)!.Movement;
        var total = Length.FromThousandths(Math.Min(movement.Thousandths + bonus * 1000L, Charging.MaxMovement.Thousandths));
        return state.MovementHalved ? Length.FromThousandths(total.Thousandths / 2) : total;
    }

    private static double RadiusOf(MatchState state, ModelId id) =>
        state.Setup?.ProfileOf(id) is { } profile ? TableRect.Inches(profile.BaseRadius) : 0.5;

    // The path from the model's centre through each waypoint, with the distance used so far at each point.
    private static List<PathMark> Marks(MatchState state, ModelId id, IReadOnlyList<Point2> waypoints)
    {
        var marks = new List<PathMark>();
        if (state.FindModel(id) is not { } model || BodyOf(state, model) is not { } body || state.Setup?.Scenario is not { } scenario)
        {
            return marks;
        }
        var field = scenario.Battlefield;
        marks.Add(new PathMark(TablePoint.Of(body.Centre), 0, TableRect.Inches(body.Elevation)));
        for (var i = 1; i <= waypoints.Count; i++)
        {
            marks.Add(new PathMark(TablePoint.Of(waypoints[i - 1]), TableRect.Inches(Movement.Cost(field, body, waypoints.Take(i).ToList())),
                TableRect.Inches(Towers.ElevationAt(field, waypoints[i - 1]))));
        }
        return marks;
    }

    private static string Inch(Length length) => BoardScene.Inch(TableRect.Inches(length));

    // Converts inches to the rules' units, rounded to the nearest thousandth.
    public static Point2 PointAt(double x, double y) =>
        new(Length.FromThousandths((long)Math.Round(x * 1000, MidpointRounding.AwayFromZero)), Length.FromThousandths((long)Math.Round(y * 1000, MidpointRounding.AwayFromZero)));
}
