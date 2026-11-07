using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Roster stage R8b (U132): charges with oval bases, as mover or target. The charge goes straight at the target's centre, stops
// as soon as the bases touch, and routes round what is in the way with the whole base.
public class OvalChargeTests
{
    private static ModelBody Oval(string id, string x, string y, BaseAxis axis = BaseAxis.Y) =>
        new(id, P(x, y), L("0.5"), default, L("0.5"), axis);

    private static ChargePlan Plan(Battlefield field, ModelBody mover, ModelBody target, string reach = "12", IReadOnlyList<Obstacle>? others = null, bool carryOn = false) =>
        Charging.Plan(field, mover, target, L(reach), [.. (others ?? []).Append(Enemy(target))], carryOn);

    private static Point2 End(ChargePlan plan) => plan.Path[^1];

    [Fact]
    public void AnOvalStopsWhenItsEndTouchesTheTarget()
    {
        var field = Field("24");
        // Along its long axis the core's end meets the target: the centre stops 1.5" short of it.
        var along = Plan(field, Oval("m", "5", "5"), Body("t", "5", "12"));
        Assert.True(along.Reached);
        Assert.Equal(L("10.5"), End(along).Y);
        // Across its width only the radius counts: the centre stops 1" short.
        var across = Plan(field, Oval("m", "5", "5", BaseAxis.X), Body("t", "5", "12"));
        Assert.Equal(L("11"), End(across).Y);
    }

    [Fact]
    public void ARoundChargerStopsAtAnOvalTargetsCore()
    {
        var field = Field("24");
        // From the side the target's core is on the line: stop 1" from its centre.
        var side = Plan(field, Body("m", "5", "5"), Oval("t", "12", "5"));
        Assert.True(side.Reached);
        Assert.Equal(L("11"), End(side).X);
        // From the end of the long axis the core's end is nearer.
        var end = Plan(field, Body("m", "5", "5"), Oval("t", "5", "12"));
        Assert.Equal(L("10.5"), End(end).Y);
    }

    [Fact]
    public void AnOvalGoesRoundAWallWithItsWholeBase()
    {
        var field = Field("24", Wall("w", "8", "2", "9", "8.2"));
        var oval = Oval("m", "6", "6");
        var plan = Plan(field, oval, Body("t", "11", "6"));
        Assert.True(plan.Reached);
        // The core's top edge plus the radius must clear the wall's top: the centre goes above 9.2".
        Assert.True(plan.Path.Max(p => p.Y.Thousandths) >= 9200);
        Assert.True(Movement.CheckPath(field, oval, plan.Path, L("12"), [Enemy(Body("t", "11", "6"))], "t").IsLegal);
        // Lying along X the oval needs only 8.7".
        var low = Plan(field, Oval("m", "6", "6", BaseAxis.X), Body("t", "11", "6"));
        Assert.True(low.Path.Max(p => p.Y.Thousandths) is >= 8700 and < 9200);
    }

    [Fact]
    public void AFriendInTheWayIsGoneRoundByTheWholeBase()
    {
        var field = Field("24");
        var friend = new Obstacle(Oval("f", "8", "6"), Relation.Friendly);
        var mover = Oval("m", "5", "6");
        var plan = Plan(field, mover, Body("t", "13", "6"), "12", [friend]);
        Assert.True(plan.Reached);
        Assert.True(Movement.CheckPath(field, mover, plan.Path, L("12"), [friend, Enemy(Body("t", "13", "6"))], "t").IsLegal);
    }

    // In the open every charge within reach ends in contact and legal, whatever the shapes and axes; with a wall or a second
    // enemy about, whatever the planner returns is legal and a charge that reaches ends within 1" without overlapping.
    [Fact]
    public void RandomChargesAreLegalAndTouch()
    {
        var rng = new Random(23);
        var reached = 0;
        var total = 0;
        for (var scene = 0; scene < 250; scene++)
        {
            Length Pick(int lo, int hi) => Length.FromThousandths(rng.Next(lo, hi));
            ModelBody Make(string id, Point2 at) => new(id, at, Pick(250, 700), default, rng.Next(2) == 0 ? Length.Zero : Pick(200, 900), rng.Next(2) == 0 ? BaseAxis.X : BaseAxis.Y);
            var start = new Point2(Pick(8000, 16000), Pick(8000, 16000));
            var mover = Make("m", start);
            var angle = rng.NextDouble() * Math.PI * 2;
            var distance = rng.Next(3000, 9000);
            var target = Make("t", new Point2(
                Length.FromThousandths(start.X.Thousandths + (long)(Math.Cos(angle) * distance)),
                Length.FromThousandths(start.Y.Thousandths + (long)(Math.Sin(angle) * distance))));
            if (Measure.Overlap(mover, target))
            {
                continue;
            }
            var wall = scene % 3 == 0;
            var terrain = wall
                ? [Wall("w", ((start.X.Thousandths + target.Centre.X.Thousandths) / 2000.0 - 0.5).ToString("0.000"), ((start.Y.Thousandths + target.Centre.Y.Thousandths) / 2000.0 - 1.5).ToString("0.000"),
                    ((start.X.Thousandths + target.Centre.X.Thousandths) / 2000.0 + 0.5).ToString("0.000"), ((start.Y.Thousandths + target.Centre.Y.Thousandths) / 2000.0 + 1.5).ToString("0.000"))]
                : Array.Empty<TerrainBlock>();
            var field = Field("40", terrain);
            if (wall && (Measure.Overlap(mover, target) || terrain[0].Footprint.Overlaps(mover) || terrain[0].Footprint.Overlaps(target)))
            {
                continue;
            }
            var plan = Charging.Plan(field, mover, target, L("12"), [Enemy(target)]);
            total++;
            if (plan.Path.IsEmpty)
            {
                continue;
            }
            Assert.True(Movement.CheckPath(field, mover, plan.Path, L("12"), [Enemy(target)], "t").IsLegal, $"scene {scene}: the plan is not a legal charge move");
            var last = mover.At(End(plan));
            Assert.False(Measure.Overlap(last, target), $"scene {scene}: the charge ends overlapping the target");
            if (plan.Reached)
            {
                reached++;
                Assert.True(Measure.IsWithin(last, target, Length.Inch), $"scene {scene}: reached but not within 1\"");
                if (!wall)
                {
                    // Stops as soon as the bases touch: no more than a hair away.
                    Assert.True(Measure.IsWithin(last, target, L("0.02")), $"scene {scene}: stopped short of contact in the open");
                }
            }
            else if (!wall)
            {
                Assert.Fail($"scene {scene}: a charge within 9\" in the open did not reach");
            }
        }
        Assert.True(total > 150 && reached > total * 0.6, $"{reached} of {total} reached");
    }
}
