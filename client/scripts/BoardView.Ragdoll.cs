using System;
using System.Collections.Generic;
using System.Linq;
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
    }

    // Which way each model fell this match (away from what hit it), so a body drawn again directly falls the same way.
    private readonly Dictionary<ModelId, Vector3> _fallPush = [];

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

    // The fall from the figure as it stands now, in the world: on the ground at groundY, pushed along push.
    private static void Simulate(RagdollRig rig, float groundY, Vector3 push, float height, ModelId id)
    {
        var to = rig.Skeleton.GlobalTransform;
        var standing = rig.Joint.Select(b => to * rig.Skeleton.GetBoneGlobalPose(b).Origin).Select(v => new Joint3(v.X, v.Y, v.Z)).ToArray();
        rig.Frames = Ragdoll.Fall(standing, groundY, new Joint3(push.X, push.Y, push.Z), height, id.Value);
    }

    // Poses the skeleton at the fall's moment t (0 standing .. 1 at rest).
    private static void PoseRig(RagdollRig rig, double t)
    {
        if (rig.Frames is not { Count: > 0 } frames)
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
}
