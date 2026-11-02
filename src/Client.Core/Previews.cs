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

    // Roster stage R5a (U105): a Ranged Attack with a BLAST weapon aimed at a point (ShootPoint).
    Point,

    // Roster stage R5h (U113): a Medi-kit's Treat ACTION on the model itself or a friend within 1" (TreatModel).
    Treat,

    // Roster stage R5j (U115): a Trigger Martyrdom Device ACTION (TriggerMartyrdom); its "target" is the model itself.
    Detonate,

    // Roster stage R6b (U118): an Aim or Fortify ACTION (TakeSelfAction); its "target" is the model itself.
    Aim,
    Fortify,
}

// A point on the table, in inches.
public readonly record struct TablePoint(double X, double Y)
{
    public static TablePoint Of(Point2 p) => new(TableRect.Inches(p.X), TableRect.Inches(p.Y));

    public override string ToString() => $"({BoardScene.Inch(X)}, {BoardScene.Inch(Y)})";
}

// One of the rules' sight rays (SightResult.Rays): from the attacker's eye above its base centre to one of the nine
// points on the target, each at its height above the table in inches (line-of-sight stage L2). BlockedBy names the
// terrain piece that blocks it, or is null when the ray is clear.
public sealed record PreviewRay(TablePoint From, TablePoint To, string? BlockedBy, double FromHeight = 0, double ToHeight = 0)
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
    DicePreview? Dice = null,
    bool? LongRange = null)
{
    // LongRange: for a Shoot the preview accepts, whether the target is beyond half the weapon's range (Long Range, -1 DICE,
    // p. 43); null for any other preview or a refused shot. It is the same value the "Long range" or "Short range" fact
    // and the dice were made from, so the battlefield's long-range line always agrees with the panel.

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

    // The terrain piece the rules say gives the target cover (stage L5 outlines it), read back from the cover fact
    // Previews.Sight writes; null when the target is not in cover.
    public string? CoverId => Facts.FirstOrDefault(f => f.StartsWith(Previews.CoverFact + " (", StringComparison.Ordinal)) is { } fact
        ? fact[(Previews.CoverFact.Length + 2)..^2]
        : null;

    public string Verb => Kind switch
    {
        PreviewKind.Place => "Place",
        PreviewKind.Move => "Move",
        PreviewKind.Dash => "Dash",
        PreviewKind.Retreat => "Retreat",
        PreviewKind.Shoot => "Shoot",
        PreviewKind.Charge => "Charge",
        PreviewKind.Point => "Blast",
        PreviewKind.Treat => "Treat",
        PreviewKind.Detonate => "Detonate",
        PreviewKind.Aim => "Aim",
        PreviewKind.Fortify => "Fortify",
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
    // Roster stage R2b: a HEAVY ranged weapon that has shot rules out a Move, Dash or Retreat (ActionLimits, p. 55).
    private static Rejection? Heavy(MatchState state, ModelId id) =>
        ActionLimits.MoveExcluded(state, id) is { } text ? new(ReasonCode.ActionExcluded, text, ActionLimits.Ref) : null;
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
            // Stage T6: one of them may be up or down a side rather than a ladder; it costs the same (U75).
            var side = facts.Contains(SideClimbFact);
            var what = side ? (climbs.Count == 1 ? "One climb up or down the side" : $"{climbs.Count} climbs (one up or down the side)")
                : climbs.Count == 1 ? "One ladder climb" : $"{climbs.Count} ladder climbs";
            facts.Add($"{what}: each counts its {BoardScene.Inch(climbs[0])} height in the distance (U66{(side ? ", U75" : "")}).");
        }
        if (refusal is null && state.FindModel(model) is { } walker && MineFact(state, walker, path) is { } mined)
        {
            facts.Add(mined);
        }
        return new Preview(kind, model, command, refusal, facts, marks, RadiusOf(state, model), null, [], [], null, allowance);
    }

    // Trench Warfare (p. 162): the Mine Markers a path would halt at, in order, by the rules' own test (Mines.Stops).
    // Null when it passes none.
    public static string? MineFact(MatchState state, ModelState mover, IReadOnlyList<Point2> path)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(path);
        if (state.Mines.IsEmpty || path.Count == 0 || state.Setup?.Scenario is not { } scenario || BodyOf(state, mover) is not { } body)
        {
            return null;
        }
        var stops = Mines.Stops(scenario.Battlefield, body, path, state.Mines, Bodies(state, mover.Id));
        return stops.IsEmpty ? null : MineFactStart + $" {(stops.Length == 1 ? "a Mine Marker" : $"{stops.Length} Mine Markers")} on the way ({string.Join(", ", stops.Select(s => s.Marker))}): " +
            "each is revealed, and a live one makes an Injury Roll; a model taken Down stops there (p. 162).";
    }

    public const string MineFactStart = "MINES: halts at";

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
        if (Heavy(state, id) is { } heavy)
        {
            return heavy;
        }
        if (path.Length > Rules.Core.Events.ModelMoved.MaxWaypoints)
        {
            return new(ReasonCode.TooManyWaypoints, $"A move has at most {Rules.Core.Events.ModelMoved.MaxWaypoints} waypoints.", RefMove);
        }
        var result = Movement.CheckPath(state.Setup!.Scenario!.Battlefield, BodyOf(state, model!)!, path, Allowance(state, model!), Obstacles(state, model!), sideClimb: true);
        facts.Add($"Distance {Inch(result.Cost)} of {Inch(Allowance(state, model!))} allowed{(state.MovementHalved ? " (halved after standing up)" : "")}.");
        if (kind == PreviewKind.Dash)
        {
            facts.Add("A Dash is a Risky Success Roll: the dice decide whether the model moves (server).");
            // Roster stage R5d (U108).
            if (TurnSequence.FanfareDice(state, id) > 0)
            {
                facts.Add("+1 DICE on the Dash roll: a Musical Instrument within 4\" (p. 85).");
            }
            // Roster stage R6c (U119).
            if (state.Setup!.ProfileOf(id) is { } dasher && (dasher.Agile || dasher.SwiftDash))
            {
                facts.Add($"+{(dasher.Agile ? 1 : 0) + (dasher.SwiftDash ? 1 : 0)} DICE on the Dash roll: {string.Join(" and ", new[] { dasher.Agile ? "Agile" : null, dasher.SwiftDash ? "its Dash ability" : null }.OfType<string>())}.");
            }
        }
        if (result.SideClimb is not null)
        {
            facts.Add(SideClimbFact);
            if (state.Setup!.ProfileOf(id)!.ClimbDice is var climb and > 0)
            {
                facts.Add($"+{climb} DICE on the climb roll: Mountaineer Kit (p. 84).");
            }
            if (state.Setup!.ProfileOf(id)!.Agile)
            {
                facts.Add("+1 DICE on the climb roll: Agile.");
            }
        }
        if ((kind == PreviewKind.Dash || result.SideClimb is not null) && !TurnSequence.RiskyEnds(state, id))
        {
            facts.Add("A failed Risky roll does not end this model's activation (its Crucifix).");
        }
        else if ((kind == PreviewKind.Dash || result.SideClimb is not null) && TurnSequence.TalismanReady(state, id))
        {
            facts.Add(TalismanFact);
        }
        return result.IsLegal ? null : new(ReasonCode.MoveIllegal, result.Text, result.RuleRef);
    }

    // Towers proposal, stage T6 (p. 39, U75).
    // Roster stage R5f (U110).
    public const string KeywordCoverFact = "Its COVER keyword (a shield, or dug in with a Shovel) gives it the Cover and Defended Obstacle modifiers (p. 54).";

    // Roster stage R5k (U116).
    public const string TalismanFact = "A failed Risky roll: you may use one of this model's Talismans so its activation does not end (once per game each).";
    public const string SideClimbFact = "Climbs the tower's side: a Risky Success Roll when the model reaches it. On a Success it makes the whole move; on a Failure it stops where the climb starts and its activation ends (server).";

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
        if (Heavy(state, id) is { } heavy)
        {
            return heavy;
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

    // Roster stage R3: weapon 1 is the model's second Ranged or Melee Weapon. A Fight with attacks still to come (an
    // Off-Hand weapon, CLEAVE) goes on with the weapon the rules name, whatever is asked (TurnSequence.NextFightAttack).
    public static Preview Target(MatchState state, Seat seat, PreviewKind kind, ModelId model, ModelId target, int weapon = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (kind == PreviewKind.Treat)
        {
            return Treat(state, seat, model, target);
        }
        if (kind is PreviewKind.Aim or PreviewKind.Fortify)
        {
            return SelfAction(state, seat, model, kind == PreviewKind.Aim ? SelfActionKind.Aim : SelfActionKind.Fortify);
        }
        if (kind == PreviewKind.Detonate)
        {
            return Detonate(state, seat, model);
        }
        // Roster stage R5i: a Grind to Dust ACTION (GrindWeapon) is its own ACTION, not the Fight's next attack.
        var next = kind == PreviewKind.Fight && weapon != ModelProfile.GrindWeapon ? TurnSequence.NextFightAttack(state, model) : null;
        if (next is not null)
        {
            weapon = next.Weapon;
        }
        Command command = kind switch
        {
            PreviewKind.Shoot => new Shoot(seat, model, target) { Weapon = weapon },
            PreviewKind.Charge => new ChargeModel(seat, model, target),
            PreviewKind.Fight => new FightModel(seat, model, target) { Weapon = weapon },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "Shoot, Charge or Fight."),
        };
        var facts = new List<string>();
        var rays = new List<PreviewRay>();
        var charge = new List<PathMark>();
        int? needed = null;
        DicePreview? dice = null;
        bool? longRange = null;
        var refusal = Opening(state) ?? kind switch
        {
            PreviewKind.Shoot => CheckShoot(state, seat, model, target, facts, rays, ref dice, ref longRange, weapon),
            PreviewKind.Charge => CheckCharge(state, seat, model, target, facts, rays, charge, ref needed),
            _ => CheckFight(state, seat, model, target, facts, rays, ref dice, weapon, next),
        };
        // Battlefield UI, stage K7: a charger without a Melee Weapon may still charge (1.0.2 p. 37: a charge does not of
        // itself allow a Fight), but it cannot Fight after it (p. 45); the preview says so before the charge is sent.
        if (kind == PreviewKind.Charge && state.Setup?.ProfileOf(model) is { CanMakeMeleeAttack: false })
        {
            facts.Add(NoMeleeWeaponFact);
        }
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
        return new Preview(kind, model, command, refusal, facts, from, RadiusOf(state, model), target, rays, charge, needed, Dice: refusal is null ? dice : null,
            LongRange: refusal is null ? longRange : null);
    }

    private static Rejection? CheckShoot(MatchState state, Seat seat, ModelId id, ModelId targetId, List<string> facts, List<PreviewRay> rays, ref DicePreview? dice, ref bool? isLong, int weapon = 0)
    {
        if (Acting(state, seat, id, out var attacker) is { } rejection)
        {
            return rejection;
        }
        if (state.Actions.HasFlag(ActionsTaken.Shoot))
        {
            return new(ReasonCode.ActionAlreadyTaken, "A Shoot ACTION can be taken only once per activation.", RefActions);
        }
        if (ActionLimits.ShootExcluded(state, id, weapon) is { } excluded)
        {
            return new(ReasonCode.ActionExcluded, excluded, ActionLimits.Ref);
        }
        if (state.FindModel(targetId) is not { } target)
        {
            return new(ReasonCode.UnknownModel, "There is no such target.", RefAttack);
        }
        var owned = state.Setup!.ProfileOf(attacker!.Id)!;
        var from = BodyOf(state, attacker)!;
        if (!owned.Has(AttackKind.Ranged, weapon))
        {
            return new(ReasonCode.NoRangedWeapon, TurnSequence.NoSuchGun(weapon), RefAttack);
        }
        var profile = owned.Using(AttackKind.Ranged, weapon);
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
        isLong = longRange;
        facts.Add(longRange ? "Long range: beyond half the weapon's range (1 penalty die)." : "Short range: within half the weapon's range.");
        var intoMelee = OnTable(state).Any(o => o.Seat == attacker.Seat && o.Id != attacker.Id && Measure.IsWithin(to, BodyOf(state, o)!, Engagement));
        if (intoMelee)
        {
            facts.Add("Shooting into melee: one of your models is within 1\" of the target, so the shot may hit it (the server rolls).");
        }
        var elevated = SightHeights.IsElevated(state.Setup!.Scenario!.Battlefield, from, to);
        if (elevated)
        {
            facts.Add("Elevated position: 3\" or more above the target (+1 DICE, p. 43).");
        }
        // The weapon's keywords decide which of these apply (roster stage R2: IGNORE ..., SHOTGUN).
        // Roster stage R5d (U108): a target with a Cloak of the Mountain makes Cover -2 DICE.
        var cloaked = state.Setup.ProfileOf(target.Id)?.BlendsIn == true;
        // Roster stage R5f (U110): a target with the COVER keyword (a shield, or dug in) has the Cover modifier.
        var keyword = !cover.InCover && TurnSequence.HasCover(state, target.Id);
        if (keyword)
        {
            facts.Add(KeywordCoverFact);
        }
        var applied = AttackDice.ShotModifiers(profile.RangedWeaponKeywords, longRange, cover.InCover || keyword, elevated, cloaked);
        // Roster stage R5e (U109): the Urn of the Bitter Ashes' bearer or a model within 3" of it: -1 DICE.
        var whispers = TurnSequence.Whispers(state, target.Id);
        // Roster stage R6c (U119): a target with a Stealth Generator: -1 DICE.
        var stealthy = state.Setup.ProfileOf(target.Id)!.Stealthy;
        dice = DicePreview.Of(state, attacker, target, AttackKind.Ranged, applied.LongRange + applied.Cover + (whispers ? 1 : 0) + (stealthy ? 1 : 0),
            [.. new[] { applied.LongRange > 0 ? "Long Range, p. 43" : null, applied.Cover > 0 ? (cloaked ? "Cover -2 for its Cloak of the Mountain, pp. 38, 43" : keyword ? "Cover from its COVER keyword, p. 54" : "Cover, pp. 38, 43") : null,
                whispers ? "Urn of the Bitter Ashes within 3\", Warbands p. 143" : null, stealthy ? "Stealth Generator, Warbands p. 110" : null }.OfType<string>()], applied.Elevated,
            applied.ShotgunInjury, weapon);
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
        if (ActionLimits.MeleeExcluded(state, id, ActionsTaken.Charge) is { } excluded)
        {
            return new(ReasonCode.ActionExcluded, excluded, ActionLimits.Ref);
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
        // Roster stage R6c (U119): a Charge as the first ACTION starts a Counter-Charge.
        if (state.Actions == ActionsTaken.None && state.Setup!.ProfileOf(id)!.CounterCharge)
        {
            facts.Add(CounterChargeFact);
        }
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
        // Performance: one planner for the declaration's route check and every die result below, so the routes are
        // searched once (each answer is the same as Charging.CheckRoute and Charging.Plan on their own).
        var planner = Charging.For(field, from, to, others);
        var route = planner.CheckRoute();
        if (!route.IsLegal)
        {
            return new(ReasonCode.ChargeRouteBlocked, route.Text, route.RuleRef);
        }
        // Charge reach: Movement plus the Charge Bonus die, at most 12", halved after standing up (pp. 36, 49). The die
        // is the server's to roll; the preview only says which results would reach.
        ChargePlan? plan = null;
        var movement = state.Setup.ProfileOf(charger.Id)!.Movement;
        var halved = state.MovementHalved ? ", halved after standing up" : "";
        var heavy = state.Setup.ProfileOf(charger.Id)!.NoChargeBonus;
        if (heavy)
        {
            // HEAVY (p. 55, U99): no Charge Bonus die, the reach is the Movement alone.
            plan = planner.Plan(ChargeReach(state, charger, 0));
            facts.Add($"Charge reach: Movement {Inch(movement)}, no Charge Bonus (HEAVY, {ActionLimits.Ref}){halved}.");
            facts.Add(plan.Reached ? "Reaches base contact." : "Would not reach base contact; the model would stop short.");
            needed = plan.Reached ? 1 : null; // reaches whatever happens, like "any Charge roll"
        }
        for (var die = 1; !heavy && die <= ChargeDieFaces; die++)
        {
            plan = planner.Plan(ChargeReach(state, charger, die));
            if (plan.Reached)
            {
                needed = die;
                break;
            }
        }
        if (!heavy)
        {
            // Roster stage R6a (U117): Shock Charge keeps the highest of 2D6.
            var die = state.Setup.ProfileOf(charger.Id)!.ExtraChargeDie ? "the highest of 2D6 (Shock Charge)" : "a D6";
            facts.Add($"Charge reach: Movement {Inch(movement)} plus {die}, at most 12\"{halved}.");
            facts.Add(needed is { } n
                ? n == 1 ? "Reaches base contact on any Charge roll." : $"Reaches base contact on a Charge roll of {n} or more; less stops short."
                : "Would not reach base contact even on a 6; the model would stop short.");
        }
        if (plan is not null)
        {
            charge.AddRange(Marks(state, charger.Id, plan.Path));
            if (MineFact(state, charger, plan.Path) is { } mined)
            {
                facts.Add(mined);
            }
        }
        return null;
    }

    private static Rejection? CheckFight(MatchState state, Seat seat, ModelId id, ModelId targetId, List<string> facts, List<PreviewRay> rays, ref DicePreview? dice, int weapon = 0, MeleeAttackSlot? next = null)
    {
        if (Acting(state, seat, id, out var attacker) is { } rejection)
        {
            return rejection;
        }
        var grind = weapon == ModelProfile.GrindWeapon;
        // Roster stage R6d (U120): a Trample ACTION is the same kind of ACTION attack.
        var trample = weapon == ModelProfile.TrampleWeapon;
        var action = grind || trample;
        if (trample && state.Setup?.ProfileOf(id) is not { Tramples: true })
        {
            return new(ReasonCode.NoSuchAction, "Only a model whose unit has it can take a Trample ACTION.", RefTrample);
        }
        if (trample && state.Actions.HasFlag(ActionsTaken.Trample))
        {
            return new(ReasonCode.ActionAlreadyTaken, "A Trample ACTION can be taken only once per activation.", RefActions);
        }
        if (grind && state.Setup?.ProfileOf(id) is not { GrindsToDust: true })
        {
            return new(ReasonCode.NoSuchAction, "Only a model with Piston Legs can take a Grind to Dust ACTION.", RefGrind);
        }
        if (grind && state.Actions.HasFlag(ActionsTaken.Grind))
        {
            return new(ReasonCode.ActionAlreadyTaken, "A Grind to Dust ACTION can be taken only once per activation.", RefActions);
        }
        if (!action && next is null && state.Actions.HasFlag(ActionsTaken.Fight))
        {
            return new(ReasonCode.ActionAlreadyTaken, "A Fight ACTION can be taken only once per activation.", RefActions);
        }
        if (!action && next is null && ActionLimits.MeleeExcluded(state, id, ActionsTaken.Fight) is { } excluded)
        {
            return new(ReasonCode.ActionExcluded, excluded, ActionLimits.Ref);
        }
        if (state.FindModel(targetId) is not { } target)
        {
            return new(ReasonCode.UnknownModel, "There is no such target.", RefMelee);
        }
        var from = BodyOf(state, attacker!)!;
        if (!state.Setup!.ProfileOf(attacker!.Id)!.CanMakeMeleeAttack && !action)
        {
            return new(ReasonCode.NoMeleeWeapon, "That model has no Melee Weapon.", RefMelee);
        }
        if (!action && weapon != 0 && (weapon < 0 || state.Setup.ProfileOf(attacker.Id)!.Weapons(AttackKind.Melee) <= weapon))
        {
            return new(ReasonCode.NoMeleeWeapon, "That model has no second Melee Weapon.", RefMelee);
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
        if (grind)
        {
            facts.Add(GrindFact);
        }
        if (trample)
        {
            facts.Add(TrampleFact);
        }
        // Roster stage R3 (U100): an Off-Hand weapon's attacks and BLOCK after a charge are a penalty die each.
        var offHand = next?.OffHand ?? false;
        var block = TurnSequence.Blocks(state, attacker.Id, target.Id);
        // Roster stage R5b (U106): a target with FEAR, unless the attacker has FEAR or NEGATE FEAR.
        var fear = TurnSequence.Fears(state, attacker.Id, target.Id);
        // Roster stage R6a (U117): a Trench Cleric's aura lifts FEAR.
        if (!fear && TurnSequence.Inspired(state, attacker.Id) && state.Setup!.ProfileOf(target.Id)!.Traits.HasFlag(ModelTraits.Fear) && !state.Setup.ProfileOf(attacker.Id)!.IgnoresFear)
        {
            facts.Add(InspiredFact);
        }
        // Roster stage R5f (U110): a target with the COVER keyword has the Defended Obstacle modifier.
        var keyword = !cover.InCover && TurnSequence.HasCover(state, target.Id);
        if (keyword)
        {
            facts.Add(KeywordCoverFact);
        }
        var defended = cover.InCover || keyword;
        var reasons = new[] { cover.InCover ? "Defended Obstacle, p. 45" : keyword ? "Defended Obstacle from its COVER keyword, p. 54" : null, offHand ? "Off-Hand weapon, p. 45" : null, block ? "BLOCK after a charge, p. 53" : null, fear ? "FEAR, p. 54" : null }.OfType<string>().ToList();
        dice = DicePreview.Of(state, attacker, target, AttackKind.Melee,
            AttackDice.FightPenalty(defended) + AttackDice.FightPenalty(offHand) + AttackDice.FightPenalty(block) + AttackDice.FightPenalty(fear), reasons, weapon: weapon);
        if (next is not null)
        {
            facts.Add(offHand ? "The Fight goes on with the Off-Hand weapon (p. 45)." : "The Fight goes on with the weapon's next CLEAVE attack (p. 53).");
        }
        return null;
    }

    // Roster stage R5h (U113): a Treat ACTION on the model itself or a friend: a Down friend is stood up, any other model
    // has 1 BLOOD MARKER removed. The rules' own check (TurnSequence.Check) decides; the facts say what the roll does.
    public static TreatChoice TreatChoiceFor(MatchState state, ModelId model, ModelId target) =>
        target != model && state.FindModel(target) is { Status: ModelStatus.Down } ? TreatChoice.StandUp : TreatChoice.RemoveMarker;

    // Roster stage R5i (U114): Piston Legs' Grind to Dust ACTION.
    // Roster stage R6d (U120).
    public const string TrampleFact = "Trample ACTION (Warbands pp. 88, 140): a Melee Attack with no Melee Weapon, with IGNORE ARMOUR, at a Down enemy.";

    public const string GrindFact = "Grind to Dust (Warbands p. 66): a Melee Attack with no Melee Weapon, at a Down enemy on a base of 32 mm or smaller.";
    public const string RefGrind = "Warbands 1.0.2 p. 66";

    public const string RefTrample = "Warbands 1.0.2 pp. 88, 140";

    public const string TreatFact = "A Treat is a Risky Success Roll (p. 84): on a Success it is carried out; a Failure ends the activation.";
    public const string HolyWaterFact = "+1 DICE on the Treat roll: Holy Water of Lalibela (Warbands p. 44).";

    // Roster stage R6a (U117).
    public const string InspiredFact = "NEGATE FEAR within 8\" of a friendly Trench Cleric: the target's FEAR does not apply (Onward Christian Soldiers!, Warbands p. 29).";
    public const string ExpertMedicFact = "+2 DICE on the Treat roll: Expert Medic (Warbands p. 34).";

    // Roster stage R5j (U115): a Trigger Martyrdom Device ACTION: the rules' own check, who it would hit and who is within 1".
    public const string MartyrdomFact = "Martyrdom Device (Warbands p. 58), once per game: an Injury Roll for every model within 3\", friend or foe, itself included; its own roll is 4D6 added together.";

    public static Preview Detonate(MatchState state, Seat seat, ModelId model)
    {
        ArgumentNullException.ThrowIfNull(state);
        var command = new TriggerMartyrdom(seat, model);
        var facts = new List<string>();
        var refusal = Opening(state) ?? Acting(state, seat, model, out _) ?? TurnSequence.Check(state, command);
        if (refusal is null)
        {
            var (hit, close) = TurnSequence.MartyrdomHits(state, model);
            facts.Add(MartyrdomFact);
            facts.Add($"Hits {string.Join(", ", hit.Select(h => h.Value))}.");
            if (close.Count > 0)
            {
                facts.Add($"+1 INJURY DICE within 1\": {string.Join(", ", close.Select(c => c.Value))}.");
            }
        }
        var from = state.FindModel(model) is { } m && BodyOf(state, m) is { } b ? [new PathMark(TablePoint.Of(b.Centre), 0)] : Array.Empty<PathMark>();
        return new Preview(PreviewKind.Detonate, model, command, refusal, facts, from, RadiusOf(state, model), model, [], [], null);
    }

    // Roster stage R6b (U118): an Aim or Fortify ACTION: the rules' own check and what a Success gives.
    // Roster stage R6c (U119).
    public const string CounterChargeFact = "Counter-Charge (Warbands p. 87): a Charge as its first ACTION gives +1 DICE to its Melee Attacks for the rest of this activation.";

    public const string AimFact = "Aim ACTION (Warbands p. 28): a Risky roll at +2 DICE; on a Success the model's Ranged Attacks get +2 DICE for the rest of this activation; a Failure ends the activation.";
    public const string FortifyFact = "Fortify ACTION (Warbands pp. 33, 85): a Risky roll; on a Success the model has COVER until it moves; a Failure ends the activation.";

    public static Preview SelfAction(MatchState state, Seat seat, ModelId model, SelfActionKind action)
    {
        ArgumentNullException.ThrowIfNull(state);
        var command = new TakeSelfAction(seat, model, action);
        var refusal = Opening(state) ?? Acting(state, seat, model, out _) ?? TurnSequence.Check(state, command);
        IReadOnlyList<string> facts = refusal is null ? [action == SelfActionKind.Aim ? AimFact : FortifyFact] : [];
        var from = state.FindModel(model) is { } m && BodyOf(state, m) is { } b ? [new PathMark(TablePoint.Of(b.Centre), 0)] : Array.Empty<PathMark>();
        return new Preview(action == SelfActionKind.Aim ? PreviewKind.Aim : PreviewKind.Fortify, model, command, refusal, facts, from, RadiusOf(state, model), model, [], [], null);
    }

    public static Preview Treat(MatchState state, Seat seat, ModelId model, ModelId target)
    {
        ArgumentNullException.ThrowIfNull(state);
        var choice = TreatChoiceFor(state, model, target);
        var command = new TreatModel(seat, model, target, choice);
        var facts = new List<string>();
        var refusal = Opening(state) ?? Acting(state, seat, model, out _) ?? TurnSequence.Check(state, command);
        if (refusal is null)
        {
            facts.Add(choice == TreatChoice.StandUp ? $"Stands {target.Value} up." : $"Removes 1 Blood Marker from {target.Value}.");
            facts.Add(TreatFact);
            if (state.Setup!.ProfileOf(model)!.HealingWaters)
            {
                facts.Add(HolyWaterFact);
            }
            if (state.Setup!.ProfileOf(model)!.ExpertMedic)
            {
                facts.Add(ExpertMedicFact);
            }
            if (!TurnSequence.RiskyEnds(state, model))
            {
                facts.Add("A failed Risky roll does not end this model's activation (its Crucifix).");
            }
            else if (TurnSequence.TalismanReady(state, model))
            {
                facts.Add(TalismanFact);
            }
        }
        var from = state.FindModel(model) is { } m && BodyOf(state, m) is { } b ? [new PathMark(TablePoint.Of(b.Centre), 0)] : Array.Empty<PathMark>();
        return new Preview(PreviewKind.Treat, model, command, refusal, facts, from, RadiusOf(state, model), target, [], [], null);
    }

    // ---- which actions to offer (the action bar) ----

    // Whether the acting model can take this kind of action at all now, before a path or a target is chosen: the same
    // checks the previews make (and so the server), in the same words. For Shoot, Charge and Fight that means at least
    // one enemy on the table the preview would accept; the refusal is then the nearest enemy's. Advisory only: it
    // decides which buttons the action bar shows, never what the server accepts.
    // Roster stage R3: with no weapon named, Shoot and Fight are open when any of the model's weapons of that kind is.
    public static Rejection? ActionOpen(MatchState state, Seat seat, ModelId model, PreviewKind kind, int? weapon = null)
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
                return (state.Actions & (ActionsTaken.Charge | ActionsTaken.Retreat)) != 0 ? new(ReasonCode.ActionAlreadyTaken, OneOfThree, RefActions) : Heavy(state, model);
            case PreviewKind.Dash:
                return state.Actions.HasFlag(ActionsTaken.Dash) ? new(ReasonCode.ActionAlreadyTaken, "A Dash ACTION can be taken only once per activation.", RefActions) : Heavy(state, model);
            case PreviewKind.Retreat:
                if ((state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) != 0)
                {
                    return new(ReasonCode.ActionAlreadyTaken, OneOfThree, RefActions);
                }
                if (Heavy(state, model) is { } heavy)
                {
                    return heavy;
                }
                return IsEngaged(state, acting!) ? null : new(ReasonCode.NotEngaged, "Only a model within 1\" of an enemy can Retreat.", RefRetreat);
            case PreviewKind.Shoot or PreviewKind.Charge or PreviewKind.Fight:
                Rejection? nearest = null;
                var weapons = weapon is { } w ? [w] : WeaponsOf(state, model, kind);
                foreach (var enemy in EnemiesByDistance(state, acting!))
                {
                    foreach (var each in weapons)
                    {
                        if (Target(state, seat, kind, model, enemy.Id, each).Refusal is not { } refusal)
                        {
                            return null;
                        }
                        nearest ??= refusal;
                    }
                }
                return nearest ?? new(ReasonCode.TargetNotOnTable, "No enemy model is on the battlefield.", RefAttack);
            case PreviewKind.Aim or PreviewKind.Fortify:
                return SelfAction(state, seat, model, kind == PreviewKind.Aim ? SelfActionKind.Aim : SelfActionKind.Fortify).Refusal;
            case PreviewKind.Detonate:
                return Detonate(state, seat, model).Refusal;
            case PreviewKind.Treat:
            {
                // Roster stage R5h: open when the rules would take a Treat on the model itself or some friend.
                Rejection? firstRefusal = null;
                foreach (var friend in state.Models.Where(m => m.Seat == acting!.Seat && m.Position is not null && m.Status != ModelStatus.OutOfAction))
                {
                    if (Treat(state, seat, model, friend.Id).Refusal is not { } refusal)
                    {
                        return null;
                    }
                    // The kit or the once-per-activation reason before any target's.
                    if (refusal.Code is ReasonCode.NoSuchAction or ReasonCode.ActionAlreadyTaken)
                    {
                        return refusal;
                    }
                    firstRefusal ??= refusal;
                }
                return firstRefusal;
            }
            case PreviewKind.Point:
                // Roster stage R5a: open when the rules would take a BLAST attack with that weapon at some point; the
                // model's own spot is always in its sight and range, so it stands for every point.
                var blasts = weapon is { } chosen ? [chosen] : BlastWeaponsOf(state, model);
                Rejection? first = null;
                foreach (var each in blasts)
                {
                    if (TurnSequence.Check(state, new ShootPoint(seat, model, acting!.Position!.Value) { Weapon = each }) is not { } refusal)
                    {
                        return null;
                    }
                    first ??= refusal;
                }
                return first ?? new(ReasonCode.NotACandidate, "Only a BLAST weapon may be aimed at a point.", RefBlast);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), "Move, Dash, Retreat, Shoot, Charge, Fight or Point.");
        }
    }

    // The weapons an attack of this kind could use: 0, and 1 when the model has a second one (a Fight going on with its
    // next attack uses the weapon the rules name, so 0 alone is asked).
    public static int[] WeaponsOf(MatchState state, ModelId model, PreviewKind kind)
    {
        var profile = state.Setup?.ProfileOf(model);
        return kind switch
        {
            PreviewKind.Shoot when profile is not null => [.. new[] { 0, 1, ModelProfile.GrenadeWeapon }.Where(w => w == 0 || profile.Has(AttackKind.Ranged, w))],
            PreviewKind.Fight when profile?.Weapons(AttackKind.Melee) == 2 && TurnSequence.NextFightAttack(state, model) is null => [0, 1],
            _ => [0],
        };
    }

    // Roster stage R5a: the model's ranged weapons with BLAST (0, 1 and the grenade), which may be aimed at a point.
    public static int[] BlastWeaponsOf(MatchState state, ModelId model)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Setup?.ProfileOf(model) is { } profile
            ? [.. new[] { 0, 1, ModelProfile.GrenadeWeapon }.Where(w => profile.Has(AttackKind.Ranged, w) && profile.Using(AttackKind.Ranged, w).RangedWeaponBlast > 0)]
            : [];
    }

    public const string RefBlast = "1.0.2 p. 53";

    // Roster stage R5a (U105): a BLAST attack aimed at a point: the rules' own check (TurnSequence.Check), Long Range,
    // and who the blast there would hit (TurnSequence.BlastHitsAt). Path holds the point; Radius is the blast's radius.
    public static Preview Point(MatchState state, Seat seat, ModelId model, Point2 at, int weapon = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        var command = new ShootPoint(seat, model, at) { Weapon = weapon };
        var profile = state.Setup?.ProfileOf(model) is { } owned && owned.Has(AttackKind.Ranged, weapon) ? owned.Using(AttackKind.Ranged, weapon) : null;
        var blast = profile?.RangedWeaponBlast ?? 0;
        var facts = new List<string> { $"Aimed at {TablePoint.Of(at)}." };
        var refusal = Opening(state) ?? TurnSequence.Check(state, command);
        bool? longRange = null;
        if (refusal is null)
        {
            longRange = TurnSequence.CheckPoint(state, model, at, weapon).LongRange;
            facts.Add(longRange == true ? "Long range: -1 DICE." : "Short range.");
            var hit = TurnSequence.BlastHitsAt(state, model, at, weapon);
            var mine = state.FindModel(model)?.Seat;
            facts.Add($"BLAST {blast}\": hits {(hit.Count == 0 ? "nobody" : string.Join(", ", hit.Select(h => h.Value + (state.FindModel(h)?.Seat == mine ? " (yours)" : ""))))}.");
            facts.Add("No model is the target, so a Critical Success adds no INJURY DICE.");
            if (profile is not null && (profile.RangedWeaponKeywords & WeaponKeywords.Scatter) != 0)
            {
                facts.Add("SCATTER: a miss lands (7 - the roll)\" from the point, where your opponent puts it.");
            }
        }
        return new Preview(PreviewKind.Point, model, command, refusal, facts, [new PathMark(TablePoint.Of(at), 0)], blast, null, [], [], null, LongRange: longRange);
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
        // What the Glorious Deeds keep for an activation starts afresh, and notes a model that began it Down (U96).
        var deeds = Rules.Core.Deeds.For(state.Setup).IsEmpty ? state.Deeds
            : state.Deeds with { ActiveBeganDown = standsUp, RetreatingFrom = [], RetreatedFrom = [], ChargedInTrench = null };
        // ModelStoodUp (U30): a Down model stands up as its activation starts, with its Movement halved.
        return state with { Models = models, ActiveModel = model, LastActivator = seat, Actions = ActionsTaken.None, MovementHalved = standsUp, Deeds = deeds };
    }

    // Towers proposal, stage T4: the path a click's points make for this model, with a ladder's foot and top put in
    // wherever the points change level (TowerRoute). The points themselves on a battlefield without towers.
    public static List<Point2> Route(MatchState state, ModelId model, IReadOnlyList<Point2> points, bool sides = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(points);
        return state.Setup?.Scenario is { } scenario && state.FindModel(model) is { Position: { } at } && state.Setup.ProfileOf(model) is { } profile
            ? TowerRoute.Expand(scenario.Battlefield, profile.BaseRadius, at, points, sides)
            : [.. points];
    }

    // Where the acting model could end a Move, Dash or Retreat with one more straight leg after the fixed waypoints: every
    // point of a grid (step inches apart) whose path the preview checks accept, so the area follows the rules' own path
    // check (allowance after the waypoints already used, terrain, other models, a Retreat's end more than 1" from enemies)
    // and agrees with the server as Path does. Empty when the action is not open.
    public static IReadOnlyList<TablePoint> Reachable(MatchState state, Seat seat, PreviewKind kind, ModelId model, IReadOnlyList<Point2> waypoints, double step = 0.5, bool sides = false)
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
        var used = waypoints.Count > 0 ? Path(state, seat, kind, model, Route(state, model, waypoints, sides)).Path[^1].Inches : 0;
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
                if (Path(state, seat, kind, model, Route(state, model, path, sides)).Legal)
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

    // Line of sight and cover from the rules (Cover.Of, which asks HeightSight.Between): the facts in words and the rays
    // to draw. Never a Godot raycast: the rules' heights decide, and the picture is drawn at the same heights.
    private static CoverResult Sight(MatchState state, ModelBody from, ModelBody to, List<string> facts, List<PreviewRay> rays)
    {
        var cover = Cover.Of(state.Setup!.Scenario!.Battlefield, from, to);
        rays.AddRange(cover.Sight.Rays.Select(r => new PreviewRay(TablePoint.Of(r.From), TablePoint.Of(r.To), r.BlockedBy, TableRect.Inches(r.FromHeight), TableRect.Inches(r.ToHeight))));
        var seeing = cover.Sight.Visibility switch
        {
            Visibility.None => "not visible",
            Visibility.Partial => "partly visible",
            _ => "fully visible",
        };
        var blockers = cover.Sight.Rays.Where(r => r.BlockedBy is not null).Select(r => r.BlockedBy!).Distinct().ToList();
        facts.Add($"Line of sight: {seeing}, {cover.Sight.Visible} of {cover.Sight.Total} sight rays clear{(blockers.Count > 0 ? $", blocked by {string.Join(", ", blockers)}" : "")}.");
        facts.Add(cover.InCover ? $"{CoverFact} ({cover.TerrainId})." : "Target not in cover.");
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

    // Stage K7: said by a Charge preview when the charger has no Melee Weapon.
    public const string NoMeleeWeaponFact = "It can charge but cannot Fight: it has no Melee Weapon (" + RefMelee + ").";

    // The fact a preview states when the target is in cover (stage K5 colours the target line by it).
    public const string CoverFact = "Target in cover";

    internal static double RadiusOf(MatchState state, ModelId id) =>
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
