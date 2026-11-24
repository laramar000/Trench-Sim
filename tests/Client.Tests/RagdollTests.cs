using Client.Core;

namespace Client.Tests;

// Ragdoll (2026-11-24): a figure put Out of Action collapses from standing to lying. Its fall is the same every time for
// the same start, keeps every bone's length and the torso's shape, never goes below the ground, ends lying near where it
// stood, falls the way it is pushed, and comes to rest.
public class RagdollTests
{
    // A 1.2" figure's joints as tools/art/make_figures.py rigs the Trench Pilgrim (x right, y up, z forward).
    private static Joint3[] Standing(double x = 10, double z = 20, double ground = 0)
    {
        Joint3 J(double a, double b, double c) => new(x + a, ground + b, z + c);
        var j = new Joint3[Ragdoll.Joints];
        j[(int)RagdollJoint.Hips] = J(0, 0.48, 0);
        j[(int)RagdollJoint.Neck] = J(-0.01, 0.81, 0.06);
        j[(int)RagdollJoint.HeadTop] = J(-0.01, 0.98, 0.07);
        j[(int)RagdollJoint.ShoulderL] = J(0.09, 0.77, 0.08);
        j[(int)RagdollJoint.ShoulderR] = J(-0.11, 0.77, 0.04);
        j[(int)RagdollJoint.ElbowL] = J(0.14, 0.64, 0.11);
        j[(int)RagdollJoint.ElbowR] = J(-0.14, 0.64, 0.0);
        j[(int)RagdollJoint.HandL] = J(0.16, 0.5, 0.14);
        j[(int)RagdollJoint.HandR] = J(-0.15, 0.5, 0.02);
        j[(int)RagdollJoint.HipL] = J(0.07, 0.48, 0.01);
        j[(int)RagdollJoint.HipR] = J(-0.07, 0.48, 0.01);
        j[(int)RagdollJoint.KneeL] = J(0.08, 0.28, 0.17);
        j[(int)RagdollJoint.KneeR] = J(-0.15, 0.23, 0.03);
        j[(int)RagdollJoint.FootL] = J(0.09, 0.04, 0.1);
        j[(int)RagdollJoint.FootR] = J(-0.15, 0.04, -0.06);
        return j;
    }

    private static double Dist(Joint3 a, Joint3 b) => (a - b).Length;

    [Fact]
    public void TheSameStartFallsTheSameWay()
    {
        var a = Ragdoll.Fall(Standing(), 0, new Joint3(1, 0, 0), 1.2, "a1");
        var b = Ragdoll.Fall(Standing(), 0, new Joint3(1, 0, 0), 1.2, "a1");
        Assert.Equal(a.Count, b.Count);
        Assert.True(a.Zip(b).All(f => f.First.SequenceEqual(f.Second)));
        var other = Ragdoll.Fall(Standing(), 0, new Joint3(1, 0, 0), 1.2, "b2");
        Assert.False(other[^1].SequenceEqual(a[^1]));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(-0.7, 0, 0.7)]
    [InlineData(0, 0, 0)]
    public void ItEndsLyingNearWhereItStoodWithItsBonesWhole(double px, double py, double pz)
    {
        var start = Standing(ground: 2);
        var frames = Ragdoll.Fall(start, 2, new Joint3(px, py, pz), 1.2, "a1");
        Assert.InRange(frames.Count * Ragdoll.FrameSeconds, 0.3, Ragdoll.MaxSeconds + 0.05);
        var end = frames[^1];
        // Lying: the neck and head down near the ground, as low as the hips.
        Assert.True(end[(int)RagdollJoint.Neck].Y < 2 + 0.2, $"neck at {end[(int)RagdollJoint.Neck].Y - 2:0.00} above the ground");
        Assert.True(end[(int)RagdollJoint.HeadTop].Y < 2 + 0.25);
        foreach (var frame in frames)
        {
            Assert.All(frame, j => Assert.True(j.Y >= 2 - 1e-6, "below the ground"));
            Assert.All(frame, j => Assert.True(new Joint3(j.X - start[0].X, 0, j.Z - start[0].Z).Flat <= 1.2 * Math.Min(2.2, 1.1 + 0.5 * new Joint3(px, 0, pz).Flat) + 1e-6));
        }
        // Bones keep their lengths, the torso its shape (within 3%).
        foreach (var (a, b) in new[] { (RagdollJoint.ShoulderL, RagdollJoint.ElbowL), (RagdollJoint.KneeR, RagdollJoint.FootR), (RagdollJoint.Neck, RagdollJoint.HeadTop), (RagdollJoint.Hips, RagdollJoint.Neck), (RagdollJoint.ShoulderL, RagdollJoint.HipR) })
        {
            Assert.Equal(Dist(start[(int)a], start[(int)b]), Dist(end[(int)a], end[(int)b]), 0.03 * Dist(start[(int)a], start[(int)b]) + 1e-3);
        }
        // At rest: the last frames hardly move.
        var moved = Enumerable.Range(0, Ragdoll.Joints).Max(i => Dist(frames[^1][i], frames[^2][i]));
        Assert.True(moved < 0.01, $"still moving {moved:0.000}\" a frame");
    }

    [Fact]
    public void ItFallsTheWayItIsPushed()
    {
        foreach (var (dx, dz) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
        {
            var start = Standing();
            var end = Ragdoll.Fall(start, 0, new Joint3(dx, 0, dz), 1.2, "a1")[^1];
            var head = end[(int)RagdollJoint.HeadTop] - start[(int)RagdollJoint.Hips];
            Assert.True(head.X * dx + head.Z * dz > 0.3, $"pushed ({dx}, {dz}): head ended at ({head.X:0.00}, {head.Z:0.00}) from the hips");
        }
    }

    // 2026-11-24 ("the impact influences how far they ragdoll"): a harder hit throws the body further; a blast's lift
    // throws it up before it lands.
    [Fact]
    public void AHarderHitThrowsItFurther()
    {
        double Thrown(double strength, double lift = 0)
        {
            var start = Standing();
            var frames = Ragdoll.Fall(start, 0, new Joint3(strength, lift, 0), 1.2, "a1");
            return frames[^1][(int)RagdollJoint.Hips].X - start[(int)RagdollJoint.Hips].X;
        }
        var shot = Thrown(0.5);
        var blow = Thrown(0.8);
        var blast = Thrown(2.5, 0.6);
        Assert.True(shot < blow && blow < blast, $"hips thrown {shot:0.00}, {blow:0.00}, {blast:0.00}");
        Assert.True(blast > 1.2, $"a close blast threw the body only {blast:0.00}\"");
        var start = Standing();
        var lifted = Ragdoll.Fall(start, 0, new Joint3(1.5, 1.2, 0), 1.2, "a1");
        Assert.True(lifted.Max(f => f[(int)RagdollJoint.Hips].Y) > start[(int)RagdollJoint.Hips].Y + 0.1, "a blast's lift did not throw it up");
        Assert.True(lifted[^1][(int)RagdollJoint.Neck].Y < 0.25);
    }

    // 2026-11-24 ("the direction of the ragdoll in general"): where the hit lands and from which side decides how it goes.
    [Fact]
    public void AHighHitTopplesItAwayAndALowHitSweepsTheLegs()
    {
        var start = Standing();
        double Ahead(Joint3[] f, RagdollJoint j) => f[(int)j].X - start[(int)j].X;
        var chest = Ragdoll.Fall(start, 0, new Joint3(1, 0, 0), 1.2, "a1", hit: 0.7);
        var legs = Ragdoll.Fall(start, 0, new Joint3(1, 0, 0), 1.2, "a1", hit: 0.05);
        // Early on: hit high, the head moves away first; hit at the feet, the feet do.
        var early = 8;
        Assert.True(Ahead(chest[early], RagdollJoint.HeadTop) > Ahead(chest[early], RagdollJoint.FootL), "a chest hit moved the feet first");
        Assert.True(Ahead(legs[early], RagdollJoint.FootL) > Ahead(legs[early], RagdollJoint.HeadTop), "a hit at the feet moved the head first");
        // At rest: hit high, the head lies away from where the feet are; legs swept, the head lies back towards the hit.
        var c = chest[^1];
        var l = legs[^1];
        Assert.True(c[(int)RagdollJoint.HeadTop].X > c[(int)RagdollJoint.FootL].X, "a chest hit left the head nearer the shooter than the feet");
        Assert.True(l[(int)RagdollJoint.HeadTop].X < l[(int)RagdollJoint.FootL].X, "swept legs left the head further than the feet");
    }

    [Fact]
    public void AHitFromTheSideTurnsTheBodyMoreThanOneFromTheFront()
    {
        var start = Standing();   // facing +z: its shoulders run along x
        double Turned(Joint3 push)
        {
            var end = Ragdoll.Fall(start, 0, push, 1.2, "a1", hit: 0.7)[^1];
            static double Yaw(Joint3 a, Joint3 b) => Math.Atan2(a.Z - b.Z, a.X - b.X);
            var d = Math.Abs(Yaw(end[(int)RagdollJoint.ShoulderL], end[(int)RagdollJoint.ShoulderR]) - Yaw(start[(int)RagdollJoint.ShoulderL], start[(int)RagdollJoint.ShoulderR]));
            return Math.Min(d, 2 * Math.PI - d);
        }
        var front = Turned(new Joint3(0, 0, -1));
        var side = Turned(new Joint3(1, 0, 0));
        Assert.True(side > front + 0.15, $"turned {side:0.00} rad hit from the side, {front:0.00} from the front");
    }

    [Fact]
    public void AHitFromBehindPitchesItForwardOntoItsFace()
    {
        var start = Standing();   // facing +z
        var end = Ragdoll.Fall(start, 0, new Joint3(0, 0, 1), 1.2, "a1", hit: 0.7)[^1];
        Assert.True(end[(int)RagdollJoint.HeadTop].Z - start[(int)RagdollJoint.Hips].Z > 0.3, "not pitched forward");
    }

    // 2026-11-24: a body thrown at another model stops against its base, never on it; and a hard throw stays within about
    // a body length of where it stood.
    [Fact]
    public void AThrownBodyStopsAgainstAnotherModelsBase()
    {
        var start = Standing();
        var hips = start[(int)RagdollJoint.Hips];
        var other = (X: hips.X + 1.3, Z: hips.Z, Radius: 0.49);   // a 25 mm base 1.3" away, in the way
        var frames = Ragdoll.Fall(start, 0, new Joint3(2.8, 0.9, 0), 1.2, "a1", 0.22, [other]);
        foreach (var frame in frames)
        {
            Assert.All(frame, j => Assert.True(Math.Sqrt((j.X - other.X) * (j.X - other.X) + (j.Z - other.Z) * (j.Z - other.Z)) >= other.Radius - 1e-6, "a joint went into the other model's base"));
        }
        var free = Ragdoll.Fall(start, 0, new Joint3(2.8, 0.9, 0), 1.2, "a1", 0.22);
        Assert.True(frames[^1][(int)RagdollJoint.Hips].X < free[^1][(int)RagdollJoint.Hips].X, "the base did not stop it");
        Assert.All(free[^1], j => Assert.True(new Joint3(j.X - hips.X, 0, j.Z - hips.Z).Flat <= 1.2 * 2.2 + 1e-6));
    }

    [Fact]
    public void ABigFigureFallsInAboutTheSameTime()
    {
        var small = Ragdoll.Fall(Standing(), 0, new Joint3(1, 0, 0), 1.2, "a1");
        var big = Ragdoll.Fall(Standing().Select(j => new Joint3(10 + (j.X - 10) * 2, j.Y * 2, 20 + (j.Z - 20) * 2)).ToArray(), 0, new Joint3(1, 0, 0), 2.4, "a1");
        Assert.InRange(big.Count / (double)small.Count, 0.7, 1.6);
    }
}
