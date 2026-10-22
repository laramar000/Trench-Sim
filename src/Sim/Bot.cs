using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Sim;

// A deliberately simple player. It proposes commands in order of preference and the runner offers each to the rules
// until one is accepted; the rules, not the bot, decide what is legal. All of its randomness comes from one seeded
// generator and all of its geometry is integer arithmetic, so a seed always plays the same match.
public sealed class Bot(Random rng)
{
    // Sixteen directions in 22.5-degree steps as integer vectors (thousandths of a unit).
    private static readonly (int X, int Y)[] Directions =
    [
        (1000, 0), (924, 383), (707, 707), (383, 924), (0, 1000), (-383, 924), (-707, 707), (-924, 383),
        (-1000, 0), (-924, -383), (-707, -707), (-383, -924), (0, -1000), (383, -924), (707, -707), (924, -383),
    ];

    // Slightly under the allowance so a diagonal vector (a hair longer than one unit) still fits.
    private static readonly int[] Fractions = [990, 750, 500, 250];

    public IEnumerable<Command> Propose(MatchState state)
    {
        if (state.Pending is { } pending)
        {
            return Decide(state, pending);
        }
        return state.Phase switch
        {
            Phase.PreGame => [new StartMatch()],
            Phase.Deployment => Deploy(state),
            Phase.Activation when state.ActiveModel is { } active => Act(state, state.FindModel(active)!),
            Phase.Activation => TurnSequence.LegalCommands(state).OrderBy(_ => rng.Next()),
            _ => [],
        };
    }

    // ---- decisions ----

    private IEnumerable<Command> Decide(MatchState state, PendingDecision pending)
    {
        var legal = TurnSequence.LegalCommands(state);
        return pending.Kind switch
        {
            // Retreating ends the match in a loss, so do it only now and then.
            DecisionKind.FailedMorale => rng.Next(100) < 25
                ? legal.OrderByDescending(c => c is ResolveFailedMorale { Choice: MoraleFailureChoice.SoundRetreat })
                : legal.OrderByDescending(c => c is ResolveFailedMorale { Choice: MoraleFailureChoice.BecomeShaken }),
            // Most opponents take their free attacks on a retreating model.
            DecisionKind.FreeFight => rng.Next(100) < 85
                ? legal.OrderBy(c => c is FreeFight ? 0 : 1)
                : legal.OrderBy(_ => rng.Next()),
            _ => legal.OrderBy(_ => rng.Next()),
        };
    }

    // ---- deployment ----

    private IEnumerable<Command> Deploy(MatchState state)
    {
        var scenario = state.Setup!.Scenario!;
        var seat = NextDeployer(state);
        var model = state.Models.First(m => m.Seat == seat && !m.Deployed);
        var zone = scenario.Battlefield.Zone(state.SeatFor(seat).ZoneId!)!.Area.Bounds;
        var radius = state.Setup.ProfileOf(model.Id)!.BaseRadius.Thousandths;
        var lowX = zone.Min.X.Thousandths + radius;
        var highX = zone.Max.X.Thousandths - radius;
        var lowY = zone.Min.Y.Thousandths + radius;
        var highY = zone.Max.Y.Thousandths - radius;
        for (var i = 0; i < 60; i++)
        {
            yield return new PlaceModel(seat, model.Id, At(rng.NextInt64(lowX, highX + 1), rng.NextInt64(lowY, highY + 1)));
        }
        // A grid sweep that cannot miss a free spot while one exists.
        for (var y = lowY; y <= highY; y += 500)
        {
            for (var x = lowX; x <= highX; x += 500)
            {
                yield return new PlaceModel(seat, model.Id, At(x, y));
            }
        }
    }

    private static Point2 At(long x, long y) => new(Length.FromThousandths(x), Length.FromThousandths(y));

    private static Seat NextDeployer(MatchState state)
    {
        bool Has(Seat s) => state.Models.Any(m => m.Seat == s && !m.Deployed);
        var seat = state.LastDeployer is { } last ? last.Opponent() : state.DeploymentFirst!.Value;
        return Has(seat) ? seat : seat.Opponent();
    }

    // ---- an activation ----

    private IEnumerable<Command> Act(MatchState state, ModelState model)
    {
        var seat = model.Seat;
        var kinds = new List<string>();
        var melee = state.Setup!.ProfileOf(model.Id)!.CanMakeMeleeAttack;
        var engaged = melee && NearestEnemy(state, model) is { } nearest && Gap(state, model, nearest) <= Length.Inch;
        // Melee actions are only ever proposed for a model that has a Melee Weapon, so a ranged-only warband plays
        // exactly as it did before Slice B.
        if (melee && engaged && !state.Actions.HasFlag(ActionsTaken.Fight) && rng.Next(100) < 85)
        {
            kinds.Add("fight");
        }
        if (melee && engaged && (state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) == 0 && rng.Next(100) < 35)
        {
            kinds.Add("retreat");
        }
        if (melee && !engaged && (state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat | ActionsTaken.Shoot)) == 0 && rng.Next(100) < 70)
        {
            kinds.Add("charge");
        }
        if (!state.Actions.HasFlag(ActionsTaken.Dash) && rng.Next(100) < 30)
        {
            kinds.Add("dash");
        }
        if (!state.Actions.HasFlag(ActionsTaken.Move))
        {
            kinds.Add("move");
        }
        if (!state.Actions.HasFlag(ActionsTaken.Shoot))
        {
            kinds.Add("shoot");
        }
        // Order the actions randomly, and sometimes stop after the first (the model simply ends its activation).
        var order = kinds.OrderBy(_ => rng.Next()).ToList();
        if (rng.Next(100) < 10)
        {
            order.Clear();
        }
        foreach (var kind in order)
        {
            switch (kind)
            {
                case "dash":
                    foreach (var path in MovePaths(state, model))
                    {
                        yield return new DashModel(seat, model.Id, path);
                    }
                    break;
                case "move":
                    foreach (var path in MovePaths(state, model))
                    {
                        yield return new MoveModel(seat, model.Id, path);
                    }
                    break;
                case "fight":
                    foreach (var target in Targets(state, model).Take(3))
                    {
                        yield return new FightModel(seat, model.Id, target.Id);
                    }
                    break;
                case "charge":
                    foreach (var target in Targets(state, model).Take(3))
                    {
                        yield return new ChargeModel(seat, model.Id, target.Id);
                    }
                    break;
                case "retreat":
                    foreach (var path in RetreatPaths(state, model))
                    {
                        yield return new RetreatModel(seat, model.Id, path);
                    }
                    break;
                default:
                    foreach (var target in Targets(state, model))
                    {
                        yield return new Shoot(seat, model.Id, target.Id);
                    }
                    break;
            }
        }
        yield return new EndActivation(seat);
    }

    private IEnumerable<ModelState> Targets(MatchState state, ModelState model)
    {
        var from = model.Position!.Value;
        return state.Models
            .Where(m => m.Seat != model.Seat && m.Position is not null)
            .OrderBy(m => Distance2(from, m.Position!.Value))
            .ThenBy(m => m.Id.Value, StringComparer.Ordinal);
    }

    private ModelState? NearestEnemy(MatchState state, ModelState model) => Targets(state, model).FirstOrDefault();

    private static Length Gap(MatchState state, ModelState a, ModelState b) =>
        Measure.Gap(
            new ModelBody(a.Id.Value, a.Position!.Value, state.Setup!.ProfileOf(a.Id)!.BaseRadius, ElevationAt(state, a.Position!.Value)),
            new ModelBody(b.Id.Value, b.Position!.Value, state.Setup.ProfileOf(b.Id)!.BaseRadius, ElevationAt(state, b.Position!.Value)));

    // Retreat paths: the legal candidates that end farthest from the nearest enemy, best first.
    private IEnumerable<ImmutableArray<Point2>> RetreatPaths(MatchState state, ModelState model)
    {
        var scenario = state.Setup!.Scenario!;
        var profile = state.Setup.ProfileOf(model.Id)!;
        var allowance = state.MovementHalved ? Length.FromThousandths(profile.Movement.Thousandths / 2) : profile.Movement;
        var body = new ModelBody(model.Id.Value, model.Position!.Value, profile.BaseRadius, ElevationAt(state, model.Position!.Value));
        var others = state.Models
            .Where(m => m.Id != model.Id && m.Position is not null)
            .Select(m => new Obstacle(
                new ModelBody(m.Id.Value, m.Position!.Value, state.Setup.ProfileOf(m.Id)!.BaseRadius, ElevationAt(state, m.Position!.Value)),
                m.Seat == model.Seat ? Relation.Friendly : Relation.Enemy))
            .ToList();
        var from = model.Position!.Value;
        var enemy = NearestEnemy(state, model)!.Position!.Value;
        var candidates = new List<ImmutableArray<Point2>>();
        foreach (var (dx, dy) in Directions)
        {
            foreach (var fraction in Fractions)
            {
                var length = allowance.Thousandths * fraction / 1000;
                candidates.Add([At(from.X.Thousandths + length * dx / 1000, from.Y.Thousandths + length * dy / 1000)]);
            }
        }
        return candidates
            .OrderByDescending(path => Distance2(path[^1], enemy))
            .Where(path => Movement.CheckPath(scenario.Battlefield, body, path, allowance, others, retreat: true).IsLegal)
            .Take(6)
            .ToList();
    }

    private static long Distance2(Point2 a, Point2 b)
    {
        var dx = a.X.Thousandths - b.X.Thousandths;
        var dy = a.Y.Thousandths - b.Y.Thousandths;
        return dx * dx + dy * dy;
    }

    // Where this model would like to be: an Objective piece, or the nearest enemy.
    private Point2 Goal(MatchState state, ModelState model)
    {
        var scenario = state.Setup!.Scenario!;
        var enemies = Targets(state, model).ToList();
        if (enemies.Count > 0 && ((state.Setup.ProfileOf(model.Id)!.HasRangedWeapon || state.Setup.ProfileOf(model.Id)!.CanMakeMeleeAttack) && rng.Next(100) < 40))
        {
            return enemies[0].Position!.Value;
        }
        var id = scenario.ObjectiveIds[rng.Next(scenario.ObjectiveIds.Length)];
        var bounds = scenario.Battlefield.Terrain.First(t => t.Id == id).Footprint.Bounds;
        return At((bounds.Min.X.Thousandths + bounds.Max.X.Thousandths) / 2, (bounds.Min.Y.Thousandths + bounds.Max.Y.Thousandths) / 2);
    }

    // Legal single- and two-leg paths toward the goal, best first. Legality is asked of Movement.CheckPath; the rules
    // check it again when the command is decided.
    private IEnumerable<ImmutableArray<Point2>> MovePaths(MatchState state, ModelState model)
    {
        var scenario = state.Setup!.Scenario!;
        var profile = state.Setup.ProfileOf(model.Id)!;
        var allowance = state.MovementHalved ? Length.FromThousandths(profile.Movement.Thousandths / 2) : profile.Movement;
        var body = new ModelBody(model.Id.Value, model.Position!.Value, profile.BaseRadius, ElevationAt(state, model.Position!.Value));
        var others = state.Models
            .Where(m => m.Id != model.Id && m.Position is not null)
            .Select(m => new Obstacle(
                new ModelBody(m.Id.Value, m.Position!.Value, state.Setup.ProfileOf(m.Id)!.BaseRadius, ElevationAt(state, m.Position!.Value)),
                m.Seat == model.Seat ? Relation.Friendly : Relation.Enemy))
            .ToList();
        var goal = Goal(state, model);
        var from = model.Position!.Value;

        var candidates = new List<ImmutableArray<Point2>>();
        foreach (var (dx, dy) in Directions)
        {
            foreach (var fraction in Fractions)
            {
                var length = allowance.Thousandths * fraction / 1000;
                candidates.Add([At(from.X.Thousandths + length * dx / 1000, from.Y.Thousandths + length * dy / 1000)]);
            }
        }
        if (rng.Next(100) < 30)
        {
            for (var i = 0; i < 24; i++)
            {
                var a = Directions[rng.Next(Directions.Length)];
                var b = Directions[rng.Next(Directions.Length)];
                var leg = allowance.Thousandths * 450 / 1000;
                var mid = At(from.X.Thousandths + leg * a.X / 1000, from.Y.Thousandths + leg * a.Y / 1000);
                candidates.Add([mid, At(mid.X.Thousandths + leg * b.X / 1000, mid.Y.Thousandths + leg * b.Y / 1000)]);
            }
        }

        bool Legal(ImmutableArray<Point2> path) => Movement.CheckPath(scenario.Battlefield, body, path, allowance, others).IsLegal;
        // On a battlefield with towers (towers proposal, stage T3), half the time head for the nearest ladder and climb
        // it (or come down), so simulated tower matches use the towers. Without towers no die is drawn here, so those
        // matches play exactly as before.
        if (scenario.Battlefield.Terrain.Any(t => t.IsTower) && rng.Next(100) < 50)
        {
            var climbs = ClimbPaths(scenario.Battlefield, from, profile.BaseRadius).ToList();
            var ladder = climbs[0][0];
            return climbs.Where(Legal).Take(3)
                .Concat(candidates.OrderBy(path => Distance2(path[^1], ladder)).Where(Legal).Take(3))
                .ToList();
        }
        return candidates
            .OrderBy(path => Distance2(path[^1], goal))
            .Where(Legal)
            .Take(6)
            .ToList();
    }

    // Paths up or down a ladder: from the ground to a ladder's foot, up to its top and one step on; or from a platform
    // to a ladder's top, down to its foot and one step away.
    private static IEnumerable<ImmutableArray<Point2>> ClimbPaths(Battlefield field, Point2 from, Length radius)
    {
        var on = Towers.TowerAt(field, from);
        var paths = new List<ImmutableArray<Point2>>();
        foreach (var tower in field.Terrain.Where(t => t.IsTower && (on is null || t == on)))
        {
            var rect = tower.Footprint.Bounds;
            foreach (var ladder in tower.Climb!.Ladders)
            {
                var foot = Climb.Foot(rect, ladder, radius);
                var top = Climb.Top(rect, ladder, radius);
                var inward = new Point2(top.X + (top.X - foot.X), top.Y + (top.Y - foot.Y));
                var outward = new Point2(foot.X + (foot.X - top.X), foot.Y + (foot.Y - top.Y));
                paths.Add(on is null ? [foot, top, inward] : [top, foot, outward]);
            }
        }
        // Nearest first (ties keep id order); a path that starts where the model stands drops that point.
        return paths.OrderBy(path => Distance2(from, path[0])).Select(path => path[0] == from ? path[1..] : path);
    }

    // How high a base centred here stands (a tower's platform or the ground), as the rules measure it.
    private static Length ElevationAt(MatchState state, Point2 at) =>
        state.Setup?.Scenario is { } scenario ? Towers.ElevationAt(scenario.Battlefield, at) : Length.Zero;
}
