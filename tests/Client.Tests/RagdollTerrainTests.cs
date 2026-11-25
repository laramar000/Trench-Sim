using Client.Core;
using Rules.Core.Geometry;

namespace Client.Tests;

// Ragdoll terrain (2026-11-25, "bodies should collide with terrain rather than pass through it"): RagdollGround is the
// height of what is drawn round a fall (table, trench floors, the dressed parts' tops); Ragdoll.Fall keeps every joint
// above it, so a body stops against a wall, drapes over sandbags, drops into a trench and comes off a platform's edge.
// Down falls stay near their base; Rise stands the body back up from lying.
public class RagdollTerrainTests
{
    private static BoardScene Table(params TerrainShape[] terrain) => new(48, 48, [], terrain, []);

    private static double Lowest(Joint3[] frame) => frame.Min(j => j.Y);

    // Every joint of every frame above the land (by its thickness, less a hair for the solver's last iteration).
    private static void AboveLand(IReadOnlyList<Joint3[]> frames, RagdollGround land, double height)
    {
        foreach (var frame in frames)
        {
            for (var i = 0; i < Ragdoll.Joints; i++)
            {
                var under = land.At(frame[i].X, frame[i].Z);
                Assert.True(frame[i].Y >= under + height * 0.02, $"joint {(RagdollJoint)i} at {frame[i].Y:0.000} under the land at {under:0.000}");
            }
        }
    }

    [Fact]
    public void TheGroundIsTheTableTrenchFloorsAndDrawnParts()
    {
        var wall = new TerrainShape("wall", new TableRect(12, 15, 13, 25), TerrainProfile.Wall, 3, false, false, "wall");
        var trench = new TerrainShape("trench", new TableRect(6, 15, 8, 25), TerrainProfile.Low, 0, false, false, "trench") { Depth = 1 };
        var land = RagdollGround.Of(Table(wall, trench), 10, 20);
        Assert.Equal(0, land.At(10, 20));
        Assert.Equal(-1, land.At(7, 20));
        // The wall's dressed parts rise up to its height inside its footprint, nothing outside it.
        Assert.InRange(Enumerable.Range(0, 20).Max(k => land.At(12.05 + k * 0.045, 20)), 1.5, 3.0001);
        Assert.Equal(0, land.At(13.3, 20));
        // Far away: the table.
        Assert.Equal(0, land.At(40, 40));
        // Undressed: the plain box at the piece's height.
        Assert.Equal(3, RagdollGround.Of(Table(wall), 10, 20, dressed: false).At(12.5, 20));
    }

    [Fact]
    public void AModelStandingAmongSandbagsFallsFromThemAndOneAgainstAWallDoesNotGoThroughIt()
    {
        // Its right foot among the ring's bags (chest high to it): they do not fling it up; it slumps against them.
        var bags = new TerrainShape("bags", new TableRect(9.85, 19, 12, 21), TerrainProfile.Cover, 0.75, false, false, "bags");
        var land = RagdollGround.Of(Table(bags), 10.3, 20);
        Assert.True(land.At(10.0, 20) > 0.5);
        var frames = Ragdoll.Fall(RagdollTests.Standing(10.3), 0, new Joint3(-0.8, 0, 0), 1.2, "b1", 0.65, null, land);
        Assert.All(frames, f => Assert.True(f[(int)RagdollJoint.Hips].Y < 0.9, "not thrown up out of the bags"));
        var end = frames[^1];
        var torso = end[(int)RagdollJoint.Neck] - end[(int)RagdollJoint.Hips];
        Assert.True(torso.Flat > 0.05, $"slumped: torso {torso}");
        Assert.True(end[(int)RagdollJoint.Neck].Y < 0.75 + 0.2, "resting on the bags, not above them");
        // Its base against a wall (touching it), thrown at it: it stays on its side.
        var wall = new TerrainShape("wall", new TableRect(10.5, 15, 11.5, 25), TerrainProfile.Wall, 3, false, false, "wall");
        var walled = RagdollGround.Of(Table(wall), 10, 20);
        var hit = Ragdoll.Fall(RagdollTests.Standing(), 0, new Joint3(2.5, 0.3, 0), 1.2, "w2", 0.65, null, walled);
        Assert.All(hit, f => Assert.All(f, j => Assert.True(j.X < 10.5 + 0.02 || j.Y > 1.5, $"joint in the wall at x {j.X:0.00}, y {j.Y:0.00}")));
        // A tower's platform under a model standing on it stays.
        var tower = new TerrainShape("tower", new TableRect(8, 18, 12, 22), TerrainProfile.Wall, 3, true, false, "tower")
        {
            Ladders = [new LadderShape("l1", new TablePoint(8, 20), Side.West)],
        };
        Assert.InRange(RagdollGround.Of(Table(tower), 10, 20).At(10, 20), 2.9, 3.0001);
    }

    [Fact]
    public void ABodyThrownAtAWallStopsAgainstItAndSlidesDown()
    {
        var wall = new TerrainShape("wall", new TableRect(10.6, 15, 11.6, 25), TerrainProfile.Wall, 3, false, false, "wall");
        var land = RagdollGround.Of(Table(wall), 10, 20);
        var frames = Ragdoll.Fall(RagdollTests.Standing(), 0, new Joint3(2.5, 0.3, 0), 1.2, "w1", 0.65, null, land);
        AboveLand(frames, land, 1.2);
        // Nothing went through the wall: no joint ever beyond its far side, and the body ends at its foot, lying.
        Assert.All(frames, f => Assert.All(f, j => Assert.True(j.X < 11.6, $"x {j.X:0.00}")));
        var end = frames[^1];
        Assert.All(end, j => Assert.True(j.X < 10.6 + 0.05 || j.Y > 1.5, $"joint inside the wall at x {j.X:0.00}, y {j.Y:0.00}"));
        Assert.True(end[(int)RagdollJoint.Hips].X < 10.6);
        // Without the wall it would have gone further.
        var open = Ragdoll.Fall(RagdollTests.Standing(), 0, new Joint3(2.5, 0.3, 0), 1.2, "w1", 0.65);
        Assert.True(open[^1].Max(j => j.X) > end.Max(j => j.X) + 0.2);
    }

    [Fact]
    public void ABodyFallsIntoATrenchAndOffAPlatformEdge()
    {
        var trench = new TerrainShape("trench", new TableRect(10.3, 15, 12.3, 25), TerrainProfile.Low, 0, false, false, "trench") { Depth = 1 };
        var land = RagdollGround.Of(Table(trench), 10, 20);
        var frames = Ragdoll.Fall(RagdollTests.Standing(), 0, new Joint3(1.5, 0, 0), 1.2, "t1", 0.65, null, land);
        AboveLand(frames, land, 1.2);
        Assert.True(Lowest(frames[^1]) < -0.5, $"lowest joint at {Lowest(frames[^1]):0.00}: it did not drop into the trench");
        // On a platform near its edge, thrown over it: it comes down off the edge onto the table.
        var platform = new TerrainShape("deck", new TableRect(5, 15, 10.4, 25), TerrainProfile.Wall, 2, true, false, "deck");
        var high = RagdollGround.Of(Table(platform), 10, 20, dressed: false);
        var off = Ragdoll.Fall(RagdollTests.Standing(ground: 2), 2, new Joint3(2, 0.4, 0), 1.2, "p1", 0.65, null, high);
        AboveLand(off, high, 1.2);
        Assert.True(Lowest(off[^1]) < 1, "it should come off the platform's edge");
        Assert.All(off[^1], j => Assert.False(j.X < 10.4 && j.Y < 1.9, $"joint inside the platform at x {j.X:0.00}, y {j.Y:0.00}"));
    }

    [Fact]
    public void ABodyDrapesOverSandbagsRatherThanThroughThem()
    {
        var bags = new TerrainShape("bags", new TableRect(10.5, 15, 11.1, 25), TerrainProfile.Cover, 0.75, false, false, "bags");
        var land = RagdollGround.Of(Table(bags), 10, 20);
        var frames = Ragdoll.Fall(RagdollTests.Standing(), 0, new Joint3(1.6, 0.2, 0), 1.2, "s1", 0.65, null, land);
        AboveLand(frames, land, 1.2);
        // Some of it rests up on the bags.
        Assert.Contains(frames[^1], j => j.X > 10.5 && j.X < 11.1 && j.Y > 0.6);
    }

    [Theory]
    [InlineData(0.5, 0, 0)]
    [InlineData(3, 1.5, 0)]
    [InlineData(0, 0, -0.8)]
    public void ADownFallLiesOverItsBase(double px, double py, double pz)
    {
        var start = RagdollTests.Standing();
        var frames = Ragdoll.Down(start, 0, new Joint3(px, py, pz), 1.2, "d1", 0.65);
        var end = frames[^1];
        var middle = (end[(int)RagdollJoint.Hips] + end[(int)RagdollJoint.Neck]) * 0.5;
        Assert.True(Math.Sqrt((middle.X - 10) * (middle.X - 10) + (middle.Z - 20) * (middle.Z - 20)) < 0.05);
        Assert.True(end[(int)RagdollJoint.Neck].Y < 0.3, "lying");
        Assert.All(end, j => Assert.True(Math.Sqrt((j.X - 10) * (j.X - 10) + (j.Z - 20) * (j.Z - 20)) < 1.2 * 1.1, "within about a body length"));
        var again = Ragdoll.Down(start, 0, new Joint3(px, py, pz), 1.2, "d1", 0.65);
        Assert.Equal(frames.Count, again.Count);
        Assert.True(frames.Zip(again).All(f => f.First.SequenceEqual(f.Second)), "the same every time");
    }

    [Fact]
    public void ARiseGoesFromLyingToStandingWithItsBonesWhole()
    {
        var start = RagdollTests.Standing();
        var lying = Ragdoll.Down(start, 0, new Joint3(0.5, 0, 0), 1.2, "r1", 0.65)[^1];
        var frames = Ragdoll.Rise(lying, start, 1.2);
        Assert.True(frames[0].SequenceEqual(lying));
        Assert.True(frames[^1].SequenceEqual(start));
        var upper = Dist(start[(int)RagdollJoint.HipL], start[(int)RagdollJoint.KneeL]);
        foreach (var frame in frames.Take(frames.Count - 1))
        {
            Assert.InRange(Dist(frame[(int)RagdollJoint.HipL], frame[(int)RagdollJoint.KneeL]), upper * 0.85, upper * 1.15);
            Assert.True(Lowest(frame) > -0.01);
        }
        // The hips come up steadily, not in a jump.
        for (var f = 1; f < frames.Count; f++)
        {
            Assert.True(Math.Abs(frames[f][(int)RagdollJoint.Hips].Y - frames[f - 1][(int)RagdollJoint.Hips].Y) < 0.1);
        }
    }

    private static double Dist(Joint3 a, Joint3 b) => (a - b).Length;

    // A figure of any size (a small one, a Giant): scaled about its feet, it still stops against the wall and lies above
    // the land, and a body thrown onto another lies over it.
    [Theory]
    [InlineData(0.6)]
    [InlineData(1.0)]
    [InlineData(1.8)]
    public void EverySizeStopsAgainstAWallAndLiesOverABodyBeforeIt(double scale)
    {
        var start = RagdollTests.Standing().Select(j => new Joint3(10 + (j.X - 10) * scale, j.Y * scale, 20 + (j.Z - 20) * scale)).ToArray();
        var height = 1.2 * scale;
        var wall = new TerrainShape("wall", new TableRect(10 + 0.6 * scale, 15, 11 + 0.6 * scale, 25), TerrainProfile.Wall, 3, false, false, "wall");
        var land = RagdollGround.Of(Table(wall), 10, 20);
        var frames = Ragdoll.Fall(start, 0, new Joint3(2.5, 0.3, 0), height, "s", 0.65, null, land);
        AboveLand(frames, land, height);
        Assert.All(frames, f => Assert.All(f, j => Assert.True(j.X < 10 + 0.6 * scale + 0.02 || j.Y > 1.5, $"joint in the wall at x {j.X:0.00}")));
        // Another body lying where this one lands: raised over it.
        var flat = RagdollGround.Of(Table(), 10, 20);
        var first = Ragdoll.Fall(start, 0, new Joint3(1.0, 0, 0), height, "f", 0.65, null, flat)[^1];
        foreach (var joint in first)
        {
            flat.Raise(joint.X, joint.Z, height * 0.06, joint.Y + height * 0.05);
        }
        var second = Ragdoll.Fall(start, 0, new Joint3(1.0, 0, 0), height, "g", 0.65, null, flat);
        AboveLand(second, flat, height);
        Assert.True(second[^1].Max(j => j.Y) > first.Max(j => j.Y) - 1e-6);
    }
}
