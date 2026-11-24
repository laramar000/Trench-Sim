namespace Client.Core;

// Ragdoll (Jason, 2026-11-24, "Implement ragdoll"): how a figure with a skeleton collapses when it is put Out of Action.
// A small, fixed-step point-and-stick solver (Verlet integration with distance constraints): the joints are points, the
// bones keep their lengths, the torso is held stiff, elbows, knees and the neck cannot fold flat, gravity pulls, the
// ground stops and slows them, and a push sends the upper body away from the attacker. Display only: it never decides
// anything, and it is the same every time for the same start (no chance; any variation comes from Spread, a fixed hash
// of the model), so a body seen fall and the same body drawn again later (Animations Off, a catch-up, a rejoin) lie
// alike. Units are the battlefield's inches, Y up.
public readonly record struct Joint3(double X, double Y, double Z)
{
    public static Joint3 operator +(Joint3 a, Joint3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Joint3 operator -(Joint3 a, Joint3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Joint3 operator *(Joint3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public double Flat => Math.Sqrt(X * X + Z * Z);
}

// The points a ragdoll moves: the figure's skeleton joints (tools/art/make_figures.py, rig_figure).
public enum RagdollJoint
{
    Hips,
    Neck,
    HeadTop,
    ShoulderL,
    ShoulderR,
    ElbowL,
    ElbowR,
    HandL,
    HandR,
    HipL,
    HipR,
    KneeL,
    KneeR,
    FootL,
    FootR,
}

public static class Ragdoll
{
    public const int Joints = 15;

    // Frames are recorded at this rate; the solver steps four times between frames.
    public const double FrameSeconds = 1.0 / 60;

    // At most this long; a fall that has come to rest sooner stops there.
    public const double MaxSeconds = 1.4;

    private const int Substeps = 4;
    private const int Iterations = 10;

    private static readonly (RagdollJoint A, RagdollJoint B)[] Bones =
    [
        (RagdollJoint.Neck, RagdollJoint.HeadTop),
        (RagdollJoint.ShoulderL, RagdollJoint.ElbowL), (RagdollJoint.ElbowL, RagdollJoint.HandL),
        (RagdollJoint.ShoulderR, RagdollJoint.ElbowR), (RagdollJoint.ElbowR, RagdollJoint.HandR),
        (RagdollJoint.HipL, RagdollJoint.KneeL), (RagdollJoint.KneeL, RagdollJoint.FootL),
        (RagdollJoint.HipR, RagdollJoint.KneeR), (RagdollJoint.KneeR, RagdollJoint.FootR),
    ];

    private static readonly RagdollJoint[] Torso =
    [
        RagdollJoint.Hips, RagdollJoint.Neck, RagdollJoint.ShoulderL, RagdollJoint.ShoulderR, RagdollJoint.HipL, RagdollJoint.HipR,
    ];

    // Each pair can never come closer than this share of its standing distance: a limb cannot fold flat, the head cannot
    // drop into the chest.
    private static readonly (RagdollJoint A, RagdollJoint B, double Least)[] Limits =
    [
        (RagdollJoint.ShoulderL, RagdollJoint.HandL, 0.45), (RagdollJoint.ShoulderR, RagdollJoint.HandR, 0.45),
        (RagdollJoint.HipL, RagdollJoint.FootL, 0.6), (RagdollJoint.HipR, RagdollJoint.FootR, 0.6),
        (RagdollJoint.HeadTop, RagdollJoint.ShoulderL, 0.85), (RagdollJoint.HeadTop, RagdollJoint.ShoulderR, 0.85),
        (RagdollJoint.HeadTop, RagdollJoint.Hips, 0.9),
    ];

    // The fall: frames of every joint (indexed by RagdollJoint), from the standing joints at rest to lying. ground is the
    // height the figure stands on; push is the way it is thrown (horizontal, away from what hit it; zero: it crumples);
    // height its standing height (gravity is scaled so it falls in a person's time at this size); key gives it its own
    // small differences (Spread). Every joint stays above the ground and within reach of where it stood.
    public static IReadOnlyList<Joint3[]> Fall(IReadOnlyList<Joint3> standing, double ground, Joint3 push, double height, string key)
    {
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(key);
        if (standing.Count != Joints)
        {
            throw new ArgumentException($"a ragdoll has {Joints} joints", nameof(standing));
        }
        height = Math.Max(0.1, height);
        var spread = new Spread("ragdoll:" + key);
        var p = standing.ToArray();
        var rest = (Joint3[])p.Clone();
        var dt = FrameSeconds / Substeps;
        // A person is about 1.75 m; the figure is `height` inches: gravity in inches per second squared at that scale.
        var gravity = 9.81 * height / 1.75;
        var start = p[(int)RagdollJoint.Hips];
        var reach = height * 1.1;
        var radius = new double[Joints];
        for (var i = 0; i < Joints; i++)
        {
            radius[i] = height * (i is (int)RagdollJoint.Hips or (int)RagdollJoint.Neck or (int)RagdollJoint.HeadTop ? 0.07 : 0.04);
        }
        // Thrown: the upper body away from the attacker, the feet a little the other way, each joint a little differently.
        var flat = push.Flat;
        var away = flat < 1e-9 ? new Joint3(spread.Next() - 0.5, 0, spread.Next() - 0.5) : new Joint3(push.X / flat, 0, push.Z / flat);
        var speed = (flat < 1e-9 ? 0.25 : 1.2) * height;
        var previous = new Joint3[Joints];
        for (var i = 0; i < Joints; i++)
        {
            var up = (p[i].Y - ground) / height;   // 0 at the feet, 1 at the head
            var kick = away * (speed * (up - 0.3)) + new Joint3(spread.Next() - 0.5, 0, spread.Next() - 0.5) * (0.08 * height);
            previous[i] = p[i] - kick * dt;
        }
        var lengths = Bones.Select(b => (rest[(int)b.A] - rest[(int)b.B]).Length).ToArray();
        var torsoPairs = new List<(int A, int B, double Length)>();
        for (var i = 0; i < Torso.Length; i++)
        {
            for (var j = i + 1; j < Torso.Length; j++)
            {
                torsoPairs.Add(((int)Torso[i], (int)Torso[j], (rest[(int)Torso[i]] - rest[(int)Torso[j]]).Length));
            }
        }
        var least = Limits.Select(l => (A: (int)l.A, B: (int)l.B, Least: (rest[(int)l.A] - rest[(int)l.B]).Length * l.Least)).ToArray();
        var frames = new List<Joint3[]> { (Joint3[])p.Clone() };
        var still = 0;
        var maxFrames = (int)Math.Round(MaxSeconds / FrameSeconds);
        for (var f = 0; f < maxFrames && still < 12; f++)
        {
            for (var s = 0; s < Substeps; s++)
            {
                for (var i = 0; i < Joints; i++)
                {
                    var v = (p[i] - previous[i]) * 0.995;
                    previous[i] = p[i];
                    p[i] = p[i] + v + new Joint3(0, -gravity * dt * dt, 0);
                }
                for (var k = 0; k < Iterations; k++)
                {
                    foreach (var (a, b, length) in torsoPairs)
                    {
                        Hold(p, a, b, length, length);
                    }
                    for (var n = 0; n < Bones.Length; n++)
                    {
                        Hold(p, (int)Bones[n].A, (int)Bones[n].B, lengths[n], lengths[n]);
                    }
                    foreach (var (a, b, min) in least)
                    {
                        Hold(p, a, b, min, double.MaxValue);
                    }
                    for (var i = 0; i < Joints; i++)
                    {
                        // The ground: nothing goes below it.
                        p[i] = p[i] with { Y = Math.Max(p[i].Y, ground + radius[i]) };
                        // Within reach of where it stood (a body never sprawls onto the next model's place).
                        var off = new Joint3(p[i].X - start.X, 0, p[i].Z - start.Z);
                        if (off.Flat > reach)
                        {
                            var k2 = reach / off.Flat;
                            p[i] = new Joint3(start.X + off.X * k2, p[i].Y, start.Z + off.Z * k2);
                        }
                    }
                }
                // What lies on the ground slides less (friction), and does not bounce.
                for (var i = 0; i < Joints; i++)
                {
                    if (p[i].Y <= ground + radius[i] + 1e-6)
                    {
                        var slide = p[i] - previous[i];
                        previous[i] = new Joint3(p[i].X - slide.X * 0.6, p[i].Y, p[i].Z - slide.Z * 0.6);
                    }
                }
            }
            var moved = 0.0;
            var last = frames[^1];
            for (var i = 0; i < Joints; i++)
            {
                moved = Math.Max(moved, (p[i] - last[i]).Length);
            }
            frames.Add((Joint3[])p.Clone());
            // At rest: nothing moved more than a hair for a fifth of a second (after the first third of a second).
            still = f > 20 && moved < height * 0.0008 ? still + 1 : 0;
        }
        return frames;
    }

    // Keeps two joints between min and max apart, moving both halfway (the hips and neck, heavier, a little less).
    private static void Hold(Joint3[] p, int a, int b, double min, double max)
    {
        var d = p[b] - p[a];
        var length = d.Length;
        if (length < 1e-9)
        {
            return;
        }
        var want = Math.Clamp(length, min, max);
        if (Math.Abs(want - length) < 1e-12)
        {
            return;
        }
        var heavyA = a is (int)RagdollJoint.Hips or (int)RagdollJoint.Neck;
        var heavyB = b is (int)RagdollJoint.Hips or (int)RagdollJoint.Neck;
        var shareA = heavyA == heavyB ? 0.5 : heavyA ? 0.3 : 0.7;
        var fix = d * ((length - want) / length);
        p[a] = p[a] + fix * shareA;
        p[b] = p[b] - fix * (1 - shareA);
    }
}
