using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Core;
using Godot;
using Rules.Core.State;

// Ragdoll (Jason, 2026-11-24, "Implement ragdoll"): a figure built with a skeleton (tools/art/make_figures.py, rig_figure)
// goes limp when it is put Out of Action: its joints fall as Client.Core.Ragdoll works them out (pushed away from what
// hit it, onto the ground, to rest) and its bones follow them, the torso turning as one stiff piece and each limb bone
// along its joints. The fall is worked out once when it starts and played back on the casualty's beat, so it is the same
// every time; a body drawn without its fall (Animations Off, a catch-up, a rejoin) is posed at the fall's end at once.
// A figure without a skeleton tips over as before. Display only: nothing here moves a model or decides anything.
public partial class BoardView
{
    // The skeleton's bone for each ragdoll joint (Client.Core.RagdollJoint's order).
    private static readonly string[] JointBones =
    [
        "pelvis", "head", "headtip", "upperarm.L", "upperarm.R", "lowerarm.L", "lowerarm.R", "hand.L", "hand.R",
        "upperleg.L", "upperleg.R", "lowerleg.L", "lowerleg.R", "foot.L", "foot.R",
    ];

    private sealed class RagdollRig
    {
        public required Skeleton3D Skeleton { get; init; }

        // Bone index of each joint, of the chest, and every bone parents first.
        public required int[] Joint { get; init; }

        public required int Chest { get; init; }

        public required int[] Order { get; init; }

        public IReadOnlyList<Joint3[]>? Frames { get; set; }

        // A fall being worked out off the main thread (2026-11-25: a fall costs a few milliseconds; several at once would
        // stall a frame); Frames once it is done.
        public Task<IReadOnlyList<Joint3[]>>? Pending { get; set; }

        // Are the frames there (taking them from a finished worker)?
        public bool Ready
        {
            get
            {
                if (Frames is null && Pending is { IsCompleted: true } done)
                {
                    Frames = done.IsCompletedSuccessfully ? done.Result : [];
                }
                return Frames is not null;
            }
        }

        // The frames, waiting for the worker if it is still at them (a body drawn at once).
        public IReadOnlyList<Joint3[]> Wait()
        {
            if (!Ready && Pending is { } working)
            {
                working.Wait();
                _ = Ready;
            }
            return Frames ?? [];
        }
    }

    // A Down model drawn by its ragdoll (2026-11-25, "Down falls with the same ragdoll"): its fall's frames (world inches),
    // from the place it fell at and the facing its figure was drawn with, so it is drawn lying in the very pose its fall
    // ended in, and stands up from it.
    private sealed class DownFall
    {
        public required Vector3 Place { get; init; }

        public required float Yaw { get; init; }

        public required RagdollRig Fall { get; init; }
    }

    private readonly Dictionary<ModelId, DownFall> _downFalls = [];

    // The terrain round each fall (RagdollGround), kept for the body's blood pool.
    private readonly Dictionary<ModelId, RagdollGround> _fallLand = [];

    // Which way each model fell this match (away from what hit it), so a body drawn again directly falls the same way.
    private readonly Dictionary<ModelId, (Vector3 Push, float Hit)> _fallPush = [];

    // The bases each fall had to keep out of (the models on the table near it then), so the same body drawn again lies
    // the same even when models have moved since.
    private readonly Dictionary<ModelId, (double X, double Z, double Radius)[]> _fallObstacles = [];

    // The bases of the models on the table within 6" of a point (not the model falling, not one the showcase hides).
    private (double X, double Z, double Radius)[] Obstacles(ModelId falling, Vector3 near) =>
        [.. (_scene?.Models ?? []).Where(m => m.Id != falling && !_hiddenForPicture.Contains(m.Id)
                && new Vector2((float)m.X - near.X, (float)m.Y - near.Z).Length() < 6)
            .Select(m => (m.X, m.Y, m.Radius))];

    // Each model's last hit (Client.Core.RagdollImpacts) and when it lands in the playback.
    private readonly Dictionary<ModelId, (double At, RagdollImpact Hit)> _recentImpact = [];

    // For the UI check: how many falls were played as a ragdoll.
    public int RagdollFalls { get; private set; }

    private static RagdollRig? RigOf(Node3D? figure)
    {
        if (figure?.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault() is not { } skeleton)
        {
            return null;
        }
        int Find(string name) => skeleton.FindBone(name) is var i and >= 0 ? i : skeleton.FindBone(name.Replace('.', '_'));
        var joints = JointBones.Select(Find).ToArray();
        var chest = Find("chest");
        if (joints.Any(i => i < 0) || chest < 0)
        {
            return null;
        }
        int Depth(int b)
        {
            var d = 0;
            for (var p = skeleton.GetBoneParent(b); p >= 0; p = skeleton.GetBoneParent(p))
            {
                d++;
            }
            return d;
        }
        return new RagdollRig
        {
            Skeleton = skeleton,
            Joint = joints,
            Chest = chest,
            Order = [.. Enumerable.Range(0, skeleton.GetBoneCount()).OrderBy(Depth)],
        };
    }

    // The figure's joints standing at rest, in the world, as it is placed now.
    private static Joint3[] StandingJoints(RagdollRig rig)
    {
        var to = rig.Skeleton.GlobalTransform;
        return [.. rig.Joint.Select(b => to * rig.Skeleton.GetBoneGlobalRest(b).Origin).Select(v => new Joint3(v.X, v.Y, v.Z))];
    }

    // The ground round a place (2026-11-25: bodies collide with the terrain drawn there), the same every time for a model.
    private RagdollGround LandAt(ModelId id, Vector3 at, float radius)
    {
        if (_fallLand.TryGetValue(id, out var kept))
        {
            return kept;
        }
        var land = _scene is { } scene ? RagdollGround.Of(scene, at.X, at.Z, dressed: !_highContrast) : RagdollGround.Flat(at.Y);
        // The bodies already lying near (each joint a small mound), so a body thrown onto one lies over it.
        foreach (var (other, root) in _bodies)
        {
            if (other == id || !IsInstanceValid(root) || new Vector2(root.GlobalPosition.X - at.X, root.GlobalPosition.Z - at.Z).Length() > RagdollGround.Window)
            {
                continue;
            }
            if (CachedRig(root) is { } rig)
            {
                // (Its size from its skeleton standing at rest.)
                var standing = StandingJoints(rig);
                var tall = standing[(int)RagdollJoint.HeadTop].Y - standing[(int)RagdollJoint.FootL].Y;
                var to = rig.Skeleton.GlobalTransform;
                foreach (var bone in rig.Joint)
                {
                    var joint = to * rig.Skeleton.GetBoneGlobalPose(bone).Origin;
                    var r = Math.Max(0.04, Math.Abs(tall) * 0.06);
                    land.Raise(joint.X, joint.Z, r, joint.Y + r * 0.8);
                }
            }
        }
        return _fallLand[id] = land;
    }

    // The fall from the figure as it stands at `at` (its place on the ground, base radius), pushed along push, on the
    // terrain there: a model put Out of Action (Ragdoll.Fall) or taken Down (Ragdoll.Down, kept over its base). Worked out
    // off the main thread (rig.Ready says when it is there), or at once with wait.
    private void Simulate(RagdollRig rig, Vector3 at, float radius, Vector3 push, float height, ModelId id, float hit, (double X, double Z, double Radius)[] obstacles,
        bool down = false, bool wait = false, bool flat = false)
    {
        var standing = StandingJoints(rig);
        // (flat: the dev lineup, every figure on open ground so their falls compare.)
        var land = flat ? RagdollGround.Flat(at.Y) : LandAt(id, at, radius);
        var thrown = new Joint3(push.X, push.Y, push.Z);
        var ground = (double)at.Y;
        var key = id.Value;
        IReadOnlyList<Joint3[]> Work() => down
            ? Ragdoll.Down(standing, ground, thrown, height, key, hit, obstacles, land)
            : Ragdoll.Fall(standing, ground, thrown, height, key, hit, obstacles, land);
        rig.Frames = null;
        if (wait)
        {
            rig.Pending = null;
            rig.Frames = Work();
        }
        else
        {
            rig.Pending = Task.Run(Work);
        }
    }

    // A Down model's lying pose: its fall's frames when it fell here this match, else (drawn Down at once: Off, a
    // catch-up, a rejoin, a model Down before) a fall worked out now, crumpling backwards from its facing.
    private DownFall DownPoseOf(ModelId id, RagdollRig rig, Vector3 place, float radius, float height, float yaw)
    {
        if (_downFalls.TryGetValue(id, out var fell) && fell.Place.DistanceTo(place) < 0.01f)
        {
            return fell;
        }
        var fall = new RagdollRig { Skeleton = rig.Skeleton, Joint = rig.Joint, Chest = rig.Chest, Order = rig.Order };
        Simulate(fall, place, radius, Backwards(yaw) * (float)RagdollImpacts.Crumple, height, id, (float)RagdollImpacts.ChestHigh, Obstacles(id, place), down: true, wait: true);
        return _downFalls[id] = new DownFall { Place = place, Yaw = yaw, Fall = fall };
    }

    // Poses the skeleton at the fall's moment t (0 standing .. 1 at rest).
    private static void PoseRig(RagdollRig rig, double t) => PoseRig(rig, rig.Frames, t);

    // Poses the skeleton at moment t of these frames (another figure's fall, as for a Down model drawn again).
    private static void PoseRig(RagdollRig rig, IReadOnlyList<Joint3[]>? frames, double t)
    {
        if (frames is not { Count: > 0 })
        {
            return;
        }
        var at = Math.Clamp(t, 0, 1) * (frames.Count - 1);
        var i0 = (int)Math.Floor(at);
        var i1 = Math.Min(frames.Count - 1, i0 + 1);
        var k = (float)(at - i0);
        var into = rig.Skeleton.GlobalTransform.AffineInverse();
        var p = new Vector3[Ragdoll.Joints];
        for (var j = 0; j < Ragdoll.Joints; j++)
        {
            var a = frames[i0][j];
            var b = frames[i1][j];
            p[j] = into * new Vector3((float)a.X, (float)a.Y, (float)a.Z).Lerp(new Vector3((float)b.X, (float)b.Y, (float)b.Z), k);
        }
        var s = rig.Skeleton;
        var rest = new Transform3D[s.GetBoneCount()];
        for (var b = 0; b < rest.Length; b++)
        {
            rest[b] = s.GetBoneGlobalRest(b);
        }
        Vector3 R(RagdollJoint j) => rest[rig.Joint[(int)j]].Origin;
        Vector3 P(RagdollJoint j) => p[(int)j];
        // The torso turns as one piece: its frame from up (hips to neck) and across (shoulder to shoulder).
        static Basis Frame(Vector3 up, Vector3 side)
        {
            var u = up.Normalized();
            var x = (side - u * side.Dot(u)).Normalized();
            return new Basis(x, u, x.Cross(u));
        }
        var torso = Frame(P(RagdollJoint.Neck) - P(RagdollJoint.Hips), P(RagdollJoint.ShoulderL) - P(RagdollJoint.ShoulderR))
            * Frame(R(RagdollJoint.Neck) - R(RagdollJoint.Hips), R(RagdollJoint.ShoulderL) - R(RagdollJoint.ShoulderR)).Inverse();
        var pose = new Transform3D?[rest.Length];
        void Set(int bone, Basis turn, Vector3 origin) => pose[bone] = new Transform3D(turn * rest[bone].Basis, origin);
        // A limb bone: its parent's turn, then the shortest turn that lays it along its joints.
        Basis Along(Basis parent, RagdollJoint from, RagdollJoint to, Vector3 origin, int bone)
        {
            var turn = Arc(parent * (R(to) - R(from)), P(to) - P(from)) * parent;
            Set(bone, turn, origin);
            return turn;
        }
        Set(rig.Joint[(int)RagdollJoint.Hips], torso, P(RagdollJoint.Hips));
        Set(rig.Chest, torso, P(RagdollJoint.Hips) + torso * (rest[rig.Chest].Origin - R(RagdollJoint.Hips)));
        var head = Along(torso, RagdollJoint.Neck, RagdollJoint.HeadTop, P(RagdollJoint.Neck), rig.Joint[(int)RagdollJoint.Neck]);
        Set(rig.Joint[(int)RagdollJoint.HeadTop], head, P(RagdollJoint.HeadTop));
        foreach (var (shoulder, elbow, hand) in new[] { (RagdollJoint.ShoulderL, RagdollJoint.ElbowL, RagdollJoint.HandL), (RagdollJoint.ShoulderR, RagdollJoint.ElbowR, RagdollJoint.HandR),
                     (RagdollJoint.HipL, RagdollJoint.KneeL, RagdollJoint.FootL), (RagdollJoint.HipR, RagdollJoint.KneeR, RagdollJoint.FootR) })
        {
            var upper = Along(torso, shoulder, elbow, P(shoulder), rig.Joint[(int)shoulder]);
            var lower = Along(upper, elbow, hand, P(elbow), rig.Joint[(int)elbow]);
            Set(rig.Joint[(int)hand], lower, P(hand));
        }
        foreach (var bone in rig.Order)
        {
            if (pose[bone] is { } global)
            {
                s.SetBoneGlobalPose(bone, global);
            }
        }
    }

    // The shortest turn taking direction a to direction b.
    private static Basis Arc(Vector3 a, Vector3 b)
    {
        if (a.LengthSquared() < 1e-12f || b.LengthSquared() < 1e-12f)
        {
            return Basis.Identity;
        }
        a = a.Normalized();
        b = b.Normalized();
        var axis = a.Cross(b);
        var dot = Mathf.Clamp(a.Dot(b), -1f, 1f);
        if (axis.LengthSquared() < 1e-10f)
        {
            if (dot > 0)
            {
                return Basis.Identity;
            }
            axis = Mathf.Abs(a.X) < 0.9f ? a.Cross(Vector3.Right) : a.Cross(Vector3.Up);
        }
        return new Basis(axis.Normalized(), Mathf.Acos(dot));
    }

    // The way a model falls when nothing else says: away from its own facing (backwards).
    private static Vector3 Backwards(float yaw) => -new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));

    // How far a body lies over: the angle of its torso (hips to neck) from its standing pose, or of its figure from upright
    // when it has no rig.
    private static float TorsoTilt(Node3D root)
    {
        if (RigOf(root) is { } rig)
        {
            var s = rig.Skeleton;
            // From its own standing pose (a figure that stoops or crouches leans already): how far it has gone over.
            var hips = s.GlobalTransform * s.GetBoneGlobalPose(rig.Joint[(int)RagdollJoint.Hips]).Origin;
            var neck = s.GlobalTransform * s.GetBoneGlobalPose(rig.Joint[(int)RagdollJoint.Neck]).Origin;
            var standing = s.GlobalTransform.Basis * (s.GetBoneGlobalRest(rig.Joint[(int)RagdollJoint.Neck]).Origin - s.GetBoneGlobalRest(rig.Joint[(int)RagdollJoint.Hips]).Origin);
            return (neck - hips).AngleTo(standing);
        }
        var holder = root.GetChildren().OfType<Node3D>().FirstOrDefault(n => n is not MeshInstance3D && n.Name != "Pool");
        return holder is null ? 0 : holder.Basis.Y.Normalized().AngleTo(Vector3.Up);
    }

    // Each figure's rig, found once per figure drawn (a redraw makes a new figure).
    private readonly Dictionary<ulong, RagdollRig?> _rigs = [];

    private RagdollRig? CachedRig(Node3D figure)
    {
        var key = figure.GetInstanceId();
        if (!_rigs.TryGetValue(key, out var rig))
        {
            if (_rigs.Count > 512)
            {
                _rigs.Clear();
            }
            rig = _rigs[key] = RigOf(figure);
        }
        return rig;
    }

    // Walk cycle (Jason, 2026-11-25: "Is there movement animations?", then "1": legs stride and the body sways while models
    // move): each figure with a skeleton gliding along its path is posed every frame from its own standing pose (a
    // crouching, charging or kneeling figure walks from that pose): each leg swings from the hip about the figure's
    // side-to-side axis, opposite to the other, its knee bending as it comes forward, and the chest (with the arms, the
    // weapon and the head) twists a little against the stride. The phase is the distance travelled, so the feet keep
    // pace with the ground; it fades in and out with the glide and ends exactly at rest. Display only.
    private readonly Dictionary<ModelId, (float Phase, float Weight)> _stride = [];
    private readonly HashSet<ModelId> _walking = [];

    public const float WalkSwingDegrees = 26f;
    public const float WalkKneeDegrees = 38f;
    public const float WalkSwayDegrees = 6f;

    // For the UI check: frames a figure was posed walking, and the largest leg swing seen (degrees).
    public int WalkFrames { get; private set; }

    public float WalkSwingSeen { get; private set; }

    private void ApplyWalk()
    {
        foreach (var id in _walking.Where(id => !_stride.ContainsKey(id)).ToList())
        {
            if (WalkingRig(id) is { } done)
            {
                done.Skeleton.ResetBonePoses();
            }
            _walking.Remove(id);
        }
        foreach (var (id, (phase, weight)) in _stride)
        {
            if (WalkingRig(id) is not { } rig || _falls.Any(f => f.Model == id) || _scene?.Find(id) is { Status: not ModelStatus.Standing })
            {
                continue;
            }
            PoseWalk(rig, phase, weight);
            _walking.Add(id);
            WalkFrames++;
        }
    }

    private RagdollRig? WalkingRig(ModelId id) =>
        _figures.TryGetValue(id, out var holder) && IsInstanceValid(holder) && holder.GetChildCount() > 0 && holder.GetChild(0) is Node3D art
            ? CachedRig(art)
            : null;

    private void PoseWalk(RagdollRig rig, float phase, float weight)
    {
        var s = rig.Skeleton;
        var count = s.GetBoneCount();
        var pose = new Transform3D[count];
        for (var b = 0; b < count; b++)
        {
            pose[b] = s.GetBoneGlobalRest(b);
        }
        int Bone(RagdollJoint j) => rig.Joint[(int)j];
        var side = pose[Bone(RagdollJoint.HipL)].Origin - pose[Bone(RagdollJoint.HipR)].Origin;
        if (side.LengthSquared() < 1e-10f)
        {
            return;
        }
        side = side.Normalized();
        bool Under(int bone, int top)
        {
            for (var b = bone; b >= 0; b = s.GetBoneParent(b))
            {
                if (b == top)
                {
                    return true;
                }
            }
            return false;
        }
        // Turns a bone and everything below it about a point.
        void Turn(int bone, Vector3 at, Basis q)
        {
            for (var b = 0; b < count; b++)
            {
                if (Under(b, bone))
                {
                    pose[b] = new Transform3D(q * pose[b].Basis, at + q * (pose[b].Origin - at));
                }
            }
        }
        foreach (var (hip, knee, offset) in new[] { (RagdollJoint.HipL, RagdollJoint.KneeL, 0f), (RagdollJoint.HipR, RagdollJoint.KneeR, Mathf.Pi) })
        {
            var p = phase + offset;
            // Forward (towards the figure's front, +Z) is a turn about the side axis by minus the swing.
            var swing = Mathf.DegToRad(WalkSwingDegrees) * weight * Mathf.Sin(p);
            var bend = Mathf.DegToRad(WalkKneeDegrees) * weight * Mathf.Max(0, Mathf.Cos(p));
            Turn(Bone(hip), pose[Bone(hip)].Origin, new Basis(side, -swing));
            Turn(Bone(knee), pose[Bone(knee)].Origin, new Basis(side, bend));
            WalkSwingSeen = Math.Max(WalkSwingSeen, Mathf.RadToDeg(Mathf.Abs(swing)));
        }
        // The chest twists against the stride (the left leg forward, the right shoulder comes forward).
        Turn(rig.Chest, pose[rig.Chest].Origin, new Basis(Vector3.Up, Mathf.DegToRad(WalkSwayDegrees) * weight * Mathf.Sin(phase)));
        foreach (var bone in rig.Order)
        {
            s.SetBoneGlobalPose(bone, pose[bone]);
        }
    }

    // Hit reactions (Jason, 2026-11-26: "an animation for when a unit takes a hit but doesn't die ... nothing too over the
    // top"): a model that takes a BLOOD MARKER and stays standing reels from the blow on its skeleton: the trunk snaps back
    // away from whoever hurt it (the shoulders turning a little with it), the head jerks further, then it recovers its
    // own stance; quick out (the first sixth), slower back. Without a skeleton the figure is
    // knocked back a little instead. Display only.
    private sealed record Wound(ModelId Model, double Start, double Seconds, Vector3 Away, HitPower Power, float Gain = 1f);

    // How far a hit's recoil (the flinch on impact) reels the body against a wound's reel.
    public const float FlinchRecoil = 0.6f;

    private readonly List<Wound> _wounds = [];

    public const float WoundTrunkDegrees = 16f;
    public const float WoundHeadDegrees = 12f;
    public const float WoundTwistDegrees = 7f;

    // Hit power (2026-11-26, "a rocket launcher or an explosive would send them a lot farther than a pistol or a blunt
    // weapon"): how far each power reels the trunk, jerks the head and turns the shoulders (degrees). The body only: the
    // model stays on its place (2026-11-26, "I dont want the entire model to move just have them recoil").
    // A pistol or a club jolts; a rifle rocks; a shotgun or a heavy weapon rocks it hard; a blast reels it furthest.
    public static (float Trunk, float Head, float Twist) WoundOf(HitPower power) => power switch
    {
        HitPower.Light => (9f, 7f, 4f),
        HitPower.Heavy => (23f, 15f, 9f),
        HitPower.Explosive => (30f, 18f, 12f),
        _ => (WoundTrunkDegrees, WoundHeadDegrees, WoundTwistDegrees),
    };

    // For the UI check: wounds started, and the most the trunk was seen reeling (degrees).
    public int WoundsStarted { get; private set; }

    public float WoundReelSeen { get; private set; }

    public HitPower? LastWoundPower { get; private set; }

    private void StartWound(Beat beat, double start)
    {
        if (!Where(beat.Model, out var at, out _))
        {
            return;
        }
        // Away from whoever hurt it; else backwards from its facing.
        var away = Backwards(FacingNow(beat.Model));
        if (beat.Other is { } by && Where(by, out var from, out _) && new Vector2(at.X - from.X, at.Z - from.Z).Length() > 0.01f)
        {
            away = new Vector3(at.X - from.X, 0, at.Z - from.Z).Normalized();
        }
        var power = Choreography.PowerOf(beat);
        if (WalkingRig(beat.Model) is null)
        {
            // No skeleton: a small knock back and in again, as hard as the hit.
            _knocks.Add(new Knock(beat.Model, start, beat.Seconds, away * FlinchInches * 0.6f * (float)HitPowers.Scale(power), 0.2f));
        }
        else
        {
            _wounds.Add(new Wound(beat.Model, start, beat.Seconds, away, power));
        }
        WoundsStarted++;
        LastWoundPower = power;
    }

    // How far into the reel at t (0 to 1 of its time): out fast, back slowly, exactly 0 at both ends.
    public static float WoundCurve(float t)
    {
        if (t is <= 0 or >= 1)
        {
            return 0;
        }
        const float peak = 0.16f;
        if (t < peak)
        {
            var u = t / peak;
            return u * (2 - u);
        }
        var v = (t - peak) / (1 - peak);
        return 1 - v * v * (3 - 2 * v);
    }

    private void ApplyWounds(double now)
    {
        for (var i = 0; i < _wounds.Count; i++)
        {
            var wound = _wounds[i];
            var t = (float)((now - wound.Start) / wound.Seconds);
            var rig = WalkingRig(wound.Model);
            // (Taken Down or out meanwhile, or ended: its own pose takes over.)
            if (t >= 1 || rig is null || _scene?.Find(wound.Model) is not { Status: ModelStatus.Standing } || _falls.Any(f => f.Model == wound.Model))
            {
                _wounds.RemoveAt(i--);
                if (rig is not null && !_walking.Contains(wound.Model) && _scene?.Find(wound.Model) is { Status: ModelStatus.Standing } && !_falls.Any(f => f.Model == wound.Model))
                {
                    rig.Skeleton.ResetBonePoses();
                }
                continue;
            }
            if (t > 0 && !_walking.Contains(wound.Model))
            {
                PoseWound(rig, wound.Away, WoundCurve(t) * wound.Gain, WoundOf(wound.Power));
            }
        }
    }

    private void EndWounds()
    {
        foreach (var wound in _wounds)
        {
            if (WalkingRig(wound.Model) is { } rig && _scene?.Find(wound.Model) is { Status: ModelStatus.Standing })
            {
                rig.Skeleton.ResetBonePoses();
            }
        }
        _wounds.Clear();
    }

    private void PoseWound(RagdollRig rig, Vector3 away, float k, (float Trunk, float Head, float Twist) size)
    {
        var s = rig.Skeleton;
        var count = s.GetBoneCount();
        var pose = new Transform3D[count];
        for (var b = 0; b < count; b++)
        {
            pose[b] = s.GetBoneGlobalRest(b);
        }
        // The way it reels, in the skeleton's own space, level.
        var local = s.GlobalTransform.Basis.Inverse() * away;
        local.Y = 0;
        if (local.LengthSquared() < 1e-8f)
        {
            return;
        }
        local = local.Normalized();
        var axis = Vector3.Up.Cross(local).Normalized();
        bool Under(int bone, int top)
        {
            for (var b = bone; b >= 0; b = s.GetBoneParent(b))
            {
                if (b == top)
                {
                    return true;
                }
            }
            return false;
        }
        void Turn(int bone, Vector3 at, Basis q)
        {
            for (var b = 0; b < count; b++)
            {
                if (Under(b, bone))
                {
                    pose[b] = new Transform3D(q * pose[b].Basis, at + q * (pose[b].Origin - at));
                }
            }
        }
        var reel = Mathf.DegToRad(size.Trunk) * k;
        // The trunk from the chest up snaps back (the top moves away), the shoulders turn a little with it.
        Turn(rig.Chest, pose[rig.Chest].Origin, new Basis(axis, reel) * new Basis(Vector3.Up, Mathf.DegToRad(size.Twist) * k));
        // The head jerks further.
        var neck = rig.Joint[(int)RagdollJoint.Neck];
        Turn(neck, pose[neck].Origin, new Basis(axis, Mathf.DegToRad(size.Head) * k));
        foreach (var bone in rig.Order)
        {
            s.SetBoneGlobalPose(bone, pose[bone]);
        }
        WoundReelSeen = Math.Max(WoundReelSeen, Mathf.RadToDeg(reel));
    }
}

