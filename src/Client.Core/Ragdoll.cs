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
    public const double MaxSeconds = 2.0;

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
    // height the figure stands on; push is how it is thrown (2026-11-24, "the impact influences how far they ragdoll"):
    // across, away from what hit it, as hard as its length (a shot about 0.5, a blow 0.8, a charge 1, a blast up to 3;
    // zero: it crumples), and up by its height (a blast lifts it); height its standing height (gravity is scaled so it falls
    // in a person's time at this size); key gives it its own small differences (Spread). Every joint stays above the
    // ground and within reach of where it stood (further the harder it was thrown).
    //
    // hit (2026-11-24, "not just how far but the direction of the ragdoll in general"): where on the body the hit lands, 0
    // the feet to 1 the head. The push lands there, harder on the side facing it, and the rest of the body follows through
    // its joints and stiff torso: a shot to the chest knocks the top back and the body topples away; a hit at the feet
    // sweeps the legs out, so the body falls the other way; a hit from the side, struck first on the near shoulder or hip,
    // turns it as it goes; from behind, it pitches forward onto its face.
    //
    // obstacles (2026-11-24): the bases of the models on the table near it (centre X, Z and radius), which no joint can
    // enter, so a thrown body stops against a living model instead of landing on it.
    //
    // land (2026-11-25, "bodies should collide with terrain"): what it lies on round its place (RagdollGround: the table,
    // trench floors, every drawn terrain part); null, flat at ground. A joint coming down onto something lands on it; one
    // driven sideways into something taller than a small step stops against its side and slides down it, so a body
    // drapes over sandbags, drops into a trench, slides off a platform's edge and comes to rest against a wall.
    // reach: how far from its place it may lie, in its heights (null: by how hard it was thrown, as above).
    public static IReadOnlyList<Joint3[]> Fall(IReadOnlyList<Joint3> standing, double ground, Joint3 push, double height, string key, double hit = 0.6,
        IReadOnlyList<(double X, double Z, double Radius)>? obstacles = null, RagdollGround? land = null, double? reach = null)
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
        // How far from its place it can lie: a hard throw further, but never more than a body length or so.
        var far = height * (reach ?? Math.Min(2.2, 1.1 + 0.5 * push.Flat));
        var blocks = obstacles ?? [];
        land ??= RagdollGround.Flat(ground);
        var radius = Radii(height);
        // Thrown: the joints where the hit lands most, the side facing it more than the far side, each joint a little
        // differently; the stiff torso and the bones carry the rest, so where and from which side it lands decides how
        // the body goes over and turns.
        var flat = push.Flat;
        var away = flat < 1e-9 ? new Joint3(spread.Next() - 0.5, 0, spread.Next() - 0.5) : new Joint3(push.X / flat, 0, push.Z / flat);
        away = away * (1 / Math.Max(1e-9, away.Flat));
        var speed = (flat < 1e-9 ? 0.25 : 0.6 + flat) * height;
        // A hard throw carries the whole body, not only where it was hit.
        var carry = Math.Max(0, flat - 0.5) * 1.4 * height;
        var lift = Math.Max(0, push.Y) * 1.6 * height;
        hit = Math.Clamp(hit, 0, 1);
        var hips = p[(int)RagdollJoint.Hips];
        var previous = new Joint3[Joints];
        for (var i = 0; i < Joints; i++)
        {
            var up = (p[i].Y - ground) / height;   // 0 at the feet, 1 at the head
            // How near the hit it is: up the body, and across it (the side facing the hit is struck first).
            var near = Math.Exp(-Math.Pow((up - hit) / 0.3, 2));
            var facing = -((p[i].X - hips.X) * away.X + (p[i].Z - hips.Z) * away.Z) / (0.12 * height);
            var weight = near * (1 + 0.6 * Math.Clamp(facing, -1, 1));
            var kick = away * (speed * 1.9 * weight + carry) + new Joint3(0, lift * (0.6 + 0.6 * up), 0)
                + new Joint3(spread.Next() - 0.5, 0, spread.Next() - 0.5) * (0.08 * height);
            previous[i] = p[i] - kick * dt;
        }
        var shape = new Shape(rest);
        var lip = height * StepShare;
        // A joint that starts inside something (the sandbags or rubble the model stood among) is free of it until clear;
        // it keeps above the level the model stood on meanwhile.
        var free = new bool[Joints];
        for (var i = 0; i < Joints; i++)
        {
            free[i] = land.Under(p[i].X, p[i].Z, radius[i]) + radius[i] - p[i].Y > lip;
        }
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
                    shape.Hold(p);
                    for (var i = 0; i < Joints; i++)
                    {
                        // The ground and the terrain: nothing goes into them.
                        if (free[i] && (free[i] = p[i].Y < land.Under(p[i].X, p[i].Z, radius[i]) + radius[i]))
                        {
                            p[i] = p[i] with { Y = Math.Max(p[i].Y, ground + radius[i]) };
                        }
                        else
                        {
                            p[i] = OnLand(land, p[i], previous[i], radius[i], lip);
                        }
                        // Out of every other model's base (it stops against it, at its own thickness).
                        foreach (var (ox, oz, r) in blocks)
                        {
                            var dx = p[i].X - ox;
                            var dz = p[i].Z - oz;
                            var d = Math.Sqrt(dx * dx + dz * dz);
                            var clear = r + radius[i];
                            if (d < clear)
                            {
                                var (ux, uz) = d > 1e-9 ? (dx / d, dz / d) : (1.0, 0.0);
                                p[i] = new Joint3(ox + ux * clear, p[i].Y, oz + uz * clear);
                            }
                        }
                        // Within reach of where it stood (a body never sprawls onto the next model's place).
                        var off = new Joint3(p[i].X - start.X, 0, p[i].Z - start.Z);
                        if (off.Flat > far)
                        {
                            var k2 = far / off.Flat;
                            p[i] = new Joint3(start.X + off.X * k2, p[i].Y, start.Z + off.Z * k2);
                        }
                    }
                }
                // What lies on the ground slides less (friction), and does not bounce.
                for (var i = 0; i < Joints; i++)
                {
                    if (p[i].Y <= land.Under(p[i].X, p[i].Z, radius[i]) + radius[i] + 1e-6)
                    {
                        // A body thrown along the ground skids; a slow one sticks (static friction), so it comes to rest.
                        var slide = p[i] - previous[i];
                        var keep = slide.Flat < height * 0.0015 ? 0.0 : 0.82;
                        previous[i] = new Joint3(p[i].X - slide.X * keep, p[i].Y, p[i].Z - slide.Z * keep);
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

    // Taken Down (2026-11-25, "Down falls with the same ragdoll"): the same limp fall, pushed as the hit pushed it but no
    // harder than a blow, and kept to its own place: it may lie at most a little over a body length out, and slides as it
    // falls so its middle (between hips and neck) ends over the base it stood on, since a Down model keeps its base and
    // label there. Ends lying, as the battlefield then draws it until it stands up (Rise).
    public static IReadOnlyList<Joint3[]> Down(IReadOnlyList<Joint3> standing, double ground, Joint3 push, double height, string key, double hit = 0.6,
        IReadOnlyList<(double X, double Z, double Radius)>? obstacles = null, RagdollGround? land = null)
    {
        ArgumentNullException.ThrowIfNull(standing);
        var flat = push.Flat;
        var soft = flat > DownPush ? new Joint3(push.X * DownPush / flat, Math.Min(push.Y, 0.3), push.Z * DownPush / flat) : push with { Y = Math.Min(push.Y, 0.3) };
        var frames = Fall(standing, ground, soft, height, key, hit, obstacles, land, DownReach);
        land ??= RagdollGround.Flat(ground);
        var home = standing[(int)RagdollJoint.Hips];
        var end = frames[^1];
        var middle = (end[(int)RagdollJoint.Hips] + end[(int)RagdollJoint.Neck]) * 0.5;
        var shift = new Joint3(home.X - middle.X, 0, home.Z - middle.Z);
        var radius = Radii(Math.Max(0.1, height));
        var slid = new List<Joint3[]>(frames.Count);
        for (var f = 0; f < frames.Count; f++)
        {
            var t = frames.Count == 1 ? 1 : (double)f / (frames.Count - 1);
            var k = t * t * (3 - 2 * t);
            var frame = new Joint3[Joints];
            for (var i = 0; i < Joints; i++)
            {
                var q = frames[f][i] + shift * k;
                frame[i] = q with { Y = Math.Max(q.Y, land.Under(q.X, q.Z, radius[i]) + radius[i]) };
            }
            slid.Add(frame);
        }
        return slid;
    }

    // How hard a fall that takes a model Down may throw it (a blow's push), and how far out it may lie (in its heights).
    public const double DownPush = 0.8;
    public const double DownReach = 1.0;

    // Standing up (2026-11-25): from lying (a Down fall's last frame) back to standing (the figure's own standing joints),
    // in `count` frames: the legs draw in first, then the hips and shoulders come up and over the feet, the arms with them,
    // every bone kept its length and every joint above the land. Display only; the same every time.
    public static IReadOnlyList<Joint3[]> Rise(IReadOnlyList<Joint3> lying, IReadOnlyList<Joint3> standing, double height, RagdollGround? land = null, int count = 40)
    {
        ArgumentNullException.ThrowIfNull(lying);
        ArgumentNullException.ThrowIfNull(standing);
        if (lying.Count != Joints || standing.Count != Joints)
        {
            throw new ArgumentException($"a ragdoll has {Joints} joints");
        }
        height = Math.Max(0.1, height);
        var rest = standing.ToArray();
        var shape = new Shape(rest);
        var radius = Radii(height);
        land ??= RagdollGround.Flat(rest.Min(j => j.Y) - radius.Min());
        static double Ease(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return t * t * (3 - 2 * t);
        }
        // When each joint makes its way up (start, length, both shares of the rise).
        static (double From, double Span) Timing(int joint) => joint switch
        {
            (int)RagdollJoint.FootL or (int)RagdollJoint.FootR or (int)RagdollJoint.KneeL or (int)RagdollJoint.KneeR => (0, 0.55),
            (int)RagdollJoint.HandL or (int)RagdollJoint.HandR or (int)RagdollJoint.ElbowL or (int)RagdollJoint.ElbowR => (0.05, 0.7),
            (int)RagdollJoint.Hips or (int)RagdollJoint.HipL or (int)RagdollJoint.HipR => (0.2, 0.8),
            _ => (0.12, 0.88),
        };
        var frames = new List<Joint3[]>(count + 1);
        var p = lying.ToArray();
        for (var f = 0; f <= count; f++)
        {
            var t = (double)f / count;
            for (var i = 0; i < Joints; i++)
            {
                var (from, span) = Timing(i);
                var k = Ease((t - from) / span);
                p[i] = lying[i] + (rest[i] - lying[i]) * k;
            }
            if (f > 0 && f < count)
            {
                for (var n = 0; n < Iterations; n++)
                {
                    shape.Hold(p);
                    for (var i = 0; i < Joints; i++)
                    {
                        var floor = land.Under(p[i].X, p[i].Z, radius[i]) + radius[i];
                        p[i] = p[i] with { Y = Math.Max(p[i].Y, Math.Min(floor, rest[i].Y)) };
                    }
                }
            }
            frames.Add(f == count ? (Joint3[])rest.Clone() : (Joint3[])p.Clone());
        }
        return frames;
    }

    // A step a joint is lifted over rather than stopped by, as a share of the figure's height (a duckboard, a lip of rubble).
    private const double StepShare = 0.1;

    // Each joint's thickness: the trunk and head thicker than the limbs.
    private static double[] Radii(double height)
    {
        var radius = new double[Joints];
        for (var i = 0; i < Joints; i++)
        {
            radius[i] = height * (i is (int)RagdollJoint.Hips or (int)RagdollJoint.Neck or (int)RagdollJoint.HeadTop ? 0.07 : 0.04);
        }
        return radius;
    }

    // A joint at `at`, last at `was`, kept out of the land: lifted onto what it came down on (or a low step), or stopped
    // against the side of something taller that it was driven into (it keeps where it was across, and slides down).
    private static Joint3 OnLand(RagdollGround land, Joint3 at, Joint3 was, double radius, double lip)
    {
        var floor = land.Under(at.X, at.Z, radius) + radius;
        if (at.Y >= floor)
        {
            return at;
        }
        if (was.Y >= floor - lip || floor - at.Y <= lip)
        {
            return at with { Y = floor };
        }
        // Driven sideways into a side: back across to where it was (if that is clear at this height).
        var before = land.Under(was.X, was.Z, radius) + radius;
        return before <= at.Y + lip ? new Joint3(was.X, Math.Max(at.Y, before), was.Z) : at with { Y = floor };
    }

    // The body's shape from its standing joints: bone lengths, the stiff torso, the joint limits.
    private sealed class Shape
    {
        private readonly double[] _lengths;
        private readonly (int A, int B, double Length)[] _torso;
        private readonly (int A, int B, double Least)[] _least;

        public Shape(Joint3[] rest)
        {
            _lengths = [.. Bones.Select(b => (rest[(int)b.A] - rest[(int)b.B]).Length)];
            var torso = new List<(int A, int B, double Length)>();
            for (var i = 0; i < Torso.Length; i++)
            {
                for (var j = i + 1; j < Torso.Length; j++)
                {
                    torso.Add(((int)Torso[i], (int)Torso[j], (rest[(int)Torso[i]] - rest[(int)Torso[j]]).Length));
                }
            }
            _torso = [.. torso];
            _least = [.. Limits.Select(l => ((int)l.A, (int)l.B, (rest[(int)l.A] - rest[(int)l.B]).Length * l.Least))];
        }

        public void Hold(Joint3[] p)
        {
            foreach (var (a, b, length) in _torso)
            {
                Ragdoll.Hold(p, a, b, length, length);
            }
            for (var n = 0; n < Bones.Length; n++)
            {
                Ragdoll.Hold(p, (int)Bones[n].A, (int)Bones[n].B, _lengths[n], _lengths[n]);
            }
            foreach (var (a, b, min) in _least)
            {
                Ragdoll.Hold(p, a, b, min, double.MaxValue);
            }
        }
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
