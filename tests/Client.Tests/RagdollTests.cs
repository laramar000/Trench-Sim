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
            Assert.All(frame, j => Assert.True(new Joint3(j.X - start[0].X, 0, j.Z - start[0].Z).Flat <= 1.2 * (1.1 + 1.2 * new Joint3(px, 0, pz).Flat) + 1e-6));
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

    [Fact]
    public void ABigFigureFallsInAboutTheSameTime()
    {
        var small = Ragdoll.Fall(Standing(), 0, new Joint3(1, 0, 0), 1.2, "a1");
        var big = Ragdoll.Fall(Standing().Select(j => new Joint3(10 + (j.X - 10) * 2, j.Y * 2, 20 + (j.Z - 20) * 2)).ToArray(), 0, new Joint3(1, 0, 0), 2.4, "a1");
        Assert.InRange(big.Count / (double)small.Count, 0.7, 1.6);
    }
}
