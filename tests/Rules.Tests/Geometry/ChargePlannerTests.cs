using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Performance (2026-10-30): the client's Charge preview asks one ChargePlanner for the route check and for every die
// result, so the routes are searched once. These tests hold it to the promise that sharing changes nothing: every
// answer from a planner that has already been asked other questions, in any order, is the answer a fresh planner (what
// the static Charging.CheckRoute and Charging.Plan use, as the server does) gives.
public class ChargePlannerTests
{
    private static readonly string[] Reaches = ["1", "2.5", "3", "4", "5.5", "6", "7", "8", "9.5", "10", "11", "12", "30"];

    private static string Fmt(long thousandths) => (thousandths / 1000.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

    private static void AssertSame(ChargePlan expected, ChargePlan actual, string what)
    {
        Assert.True(expected.Path.SequenceEqual(actual.Path), $"{what}: the paths differ");
        Assert.Equal(expected.Cost, actual.Cost);
        Assert.Equal(expected.Reached, actual.Reached);
    }

    [Fact]
    [Trait("Rule", "Charge Moves pp. 35-37: one planner asked for every reach gives each reach's own plan")]
    public void ASharedPlannerGivesTheSameAnswersAsAFreshOneForEveryReachInAnyOrder()
    {
        var rng = new Random(4242);
        var (reached, short_, roundFriend, refused) = (0, 0, 0, 0);
        for (var i = 0; i < 120; i++)
        {
            var x = rng.Next(5500, 9000);
            var y = rng.Next(4000, 14000);
            var wall = Wall("w", Fmt(x), Fmt(y), Fmt(x + rng.Next(400, 2000)), Fmt(y + rng.Next(1500, 7000)));
            var mover = Body("m", Fmt(rng.Next(2000, 4500)), Fmt(rng.Next(8000, 16000)));
            var target = Body("t", Fmt(rng.Next(12500, 14500)), Fmt(rng.Next(8000, 16000)));
            var angle = rng.NextDouble() * 2 * Math.PI;
            var spacing = 1.0 + rng.NextDouble() * 1.3;
            var friend = Body("f", Fmt((long)Math.Round(target.Centre.X.Thousandths + spacing * 1000 * Math.Cos(angle))), Fmt((long)Math.Round(target.Centre.Y.Thousandths + spacing * 1000 * Math.Sin(angle))));
            var other = Body("e", Fmt(rng.Next(6000, 12000)), Fmt(rng.Next(2000, 22000)));
            if (wall.Footprint.CircleOverlaps(mover.Centre, L("1.3")) || wall.Footprint.CircleOverlaps(target.Centre, L("2.3"))
                || wall.Footprint.CircleOverlaps(friend.Centre, L("1.3")) || wall.Footprint.CircleOverlaps(other.Centre, L("1.3"))
                || Measure.Overlap(other, mover) || Measure.Overlap(other, target) || Measure.Overlap(other, friend))
            {
                continue;
            }
            var field = Field("36", wall);
            var others = i % 3 == 0 ? new[] { Enemy(target), Friend(friend), Enemy(other) } : new[] { Enemy(target), Friend(friend) };

            var shared = Charging.For(field, mover, target, others);
            var order = Reaches.OrderBy(_ => rng.Next()).ToList();
            // The route check asked first, last or in between, as the preview may.
            var checkAt = rng.Next(order.Count + 1);
            for (var k = 0; k <= order.Count; k++)
            {
                if (k == checkAt)
                {
                    Assert.Equal(Charging.For(field, mover, target, others).CheckRoute(), shared.CheckRoute());
                    Assert.Equal(Charging.CheckRoute(field, mover, target, others), shared.CheckRoute());
                }
                if (k == order.Count)
                {
                    break;
                }
                var reach = L(order[k]);
                var fresh = Charging.For(field, mover, target, others).Plan(reach);
                var plan = shared.Plan(reach);
                AssertSame(fresh, plan, $"scene {i}, reach {order[k]}");
                AssertSame(Charging.Plan(field, mover, target, reach, others), plan, $"scene {i}, reach {order[k]} (static)");
                // Asked twice, the same again.
                AssertSame(plan, shared.Plan(reach), $"scene {i}, reach {order[k]} (again)");
            }
            if (!shared.CheckRoute().IsLegal)
            {
                refused++;
                continue;
            }
            var far = shared.Plan(L("30"));
            if (!far.Reached)
            {
                short_++;
            }
            else if (far.Cost > Charging.For(field, mover, target, [Enemy(target)]).Plan(L("30")).Cost)
            {
                roundFriend++;
            }
            else
            {
                reached++;
            }
        }
        // The scenes cover each kind of answer.
        Assert.True(reached >= 10, $"only {reached} scenes reached directly");
        Assert.True(roundFriend >= 3, $"only {roundFriend} scenes went round the friend");
        Assert.True(short_ + refused >= 3, $"only {short_} short and {refused} refused");
    }
}
