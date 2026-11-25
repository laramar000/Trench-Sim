using System.Collections.Generic;
using System.Linq;
using Client.Core;
using Godot;
using Rules.Core.State;

// Bodies (Jason, 2026-11-23: "I want bodies to lay on the ground, not disappear"): a model put Out of Action stays on the
// battlefield as a body, lying where it fell, darkened and without its base or label, until the match ends (a new match
// clears them). The server takes the model off the table, so a body is drawing only: nothing selects, blocks, sees or
// moves by it, and it is never in the scene's models (BoardScene.Fallen lists them, each at the last place the table had
// it). With animations, the casualty figure falls and becomes the body when it has settled; without (Off, a catch-up, a
// rejoin) the body is drawn lying at once.
public partial class BoardView
{
    // How dark a body is drawn (its figure's colours times this).
    public const float BodyShade = 0.55f;

    private readonly Node3D _bodiesNode = new() { Name = "Bodies" };
    private readonly Dictionary<ModelId, Node3D> _bodies = [];
    private readonly HashSet<ModelId> _hiddenForPicture = [];

    // Body polish (2026-11-23): each body's materials and base, to draw it see-through while a model stands on it.
    private readonly Dictionary<ModelId, (List<ShaderMaterial> Shaders, List<StandardMaterial3D> Paints, float Radius, Vector3 Lies)> _bodyParts = [];

    // A body under a model on the table is drawn this see-through (the figure shader's ordered dither; the paints' alpha),
    // so the living model always reads first.
    public const float BodyUnderfoot = 0.35f;

    // How long a body's blood pool takes to spread when it falls in play (drawn at once without an animation).
    public const float PoolSeconds = 1.6f;

    private static void Darken(List<ShaderMaterial> shaders, List<StandardMaterial3D> paints, IReadOnlyList<Color> colours, float shade)
    {
        foreach (var shader in shaders)
        {
            shader.SetShaderParameter("albedo_colour", new Color(shade, shade, shade, 1));
        }
        for (var i = 0; i < paints.Count && i < colours.Count; i++)
        {
            var c = colours[i];
            paints[i].AlbedoColor = new Color(c.R * shade, c.G * shade, c.B * shade, c.A);
        }
    }

    // A casualty figure that has played out: a body when its model has left the table (or the showcase hides it), else
    // (a UI check's made-up casualty on a model still there) freed as before.
    private void EndGhost(CasualtyFigure ghost)
    {
        if (!IsInstanceValid(ghost.Root) || (_scene?.Find(ghost.Model) is not null && !_hiddenForPicture.Contains(ghost.Model)))
        {
            FreeGhost(ghost);
            return;
        }
        PoseGhost(ghost, double.MaxValue);
        var radius = _lastDrawn.Find(_scene, ghost.Model) is { } shape ? (float)shape.Radius : 0.5f;
        Keep(ghost.Model, ghost.Root, ghost.Shaders, ghost.Paints, radius, spread: true);
        Underfoot();
    }

    // Lays a body to rest: its base hidden, a pool of blood under it (spreading when it has just fallen), under the bodies
    // node, the model's only body.
    private void Keep(ModelId id, Node3D root, List<ShaderMaterial> shaders, List<StandardMaterial3D> paints, float radius, bool spread)
    {
        foreach (var part in root.GetChildren().OfType<MeshInstance3D>())
        {
            part.Visible = false;
        }
        // Where it lies: a ragdoll can be thrown off its place (2026-11-24), so its hips; else its place.
        var lies = RigOf(root) is { } rig
            ? rig.Skeleton.GlobalTransform * rig.Skeleton.GetBoneGlobalPose(rig.Joint[(int)RagdollJoint.Hips]).Origin
            : root.GlobalPosition;
        _bodyParts[id] = (shaders, paints, radius, lies);
        root.GetParent()?.RemoveChild(root);
        root.Name = $"Body{id.Value}";
        if (_bodies.Remove(id, out var old) && IsInstanceValid(old))
        {
            old.GetParent()?.RemoveChild(old);
            old.QueueFree();
        }
        _bodiesNode.AddChild(root);
        // The pool goes in once the body is in its place (a tween started before the move would stop with it).
        var pool = BloodPool(id, radius);
        root.AddChild(pool);
        // On what it lies on (2026-11-25: a body over sandbags or down in a trench bleeds there).
        var level = _fallLand.TryGetValue(id, out var land) ? (float)land.At(lies.X, lies.Z) : root.GlobalPosition.Y;
        pool.GlobalPosition = new Vector3(lies.X, level + 0.03f, lies.Z);
        if (spread)
        {
            pool.Scale = new Vector3(0.15f, 1, 0.15f);
            pool.CreateTween().TweenProperty(pool, "scale", Vector3.One, PoolSeconds).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        }
        _bodies[id] = root;
        BodiesLaid++;
    }

    // Every fallen model of the scene has a body (unless its casualty figure is still playing it); a model back on the
    // table has none. A new match clears them all (NewMatch).
    private void SyncBodies()
    {
        var fallen = _scene?.Fallen ?? [];
        foreach (var gone in _bodies.Keys.Where(id => _scene?.Find(id) is not null && !_hiddenForPicture.Contains(id)).ToList())
        {
            RemoveBody(gone);
        }
        foreach (var model in fallen)
        {
            if (!_bodies.ContainsKey(model.Id) && !_ghosts.Any(g => g.Model == model.Id))
            {
                LayBody(model);
            }
        }
        Underfoot();
    }

    // A dark, uneven pool of blood on the ground under a body: three flat overlapping discs, placed from the model's id so
    // it looks the same every time it is drawn. Display only.
    private static Node3D BloodPool(ModelId id, float radius)
    {
        // Just above the deployment zones' and the mine area's slabs (0.02" and 0.025" high), under the body.
        var pool = new Node3D { Name = "Pool", Position = new Vector3(0, 0.03f, 0) };
        var paint = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.035f, 0.03f, 0.85f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.35f };
        var seed = (uint)id.Value.GetHashCode();
        foreach (var (size, step) in new[] { (0.8f, 0), (0.55f, 1), (0.45f, 2) })
        {
            var angle = (seed >> (step * 5) & 31) / 32f * Mathf.Tau;
            var off = step == 0 ? 0 : radius * 0.45f;
            pool.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = radius * size, BottomRadius = radius * size, Height = 0.008f, RadialSegments = 12 },
                Position = new Vector3(Mathf.Cos(angle) * off, step * 0.001f, Mathf.Sin(angle) * off),
                MaterialOverride = paint,
            });
        }
        return pool;
    }

    // Each body under a model on the table is drawn see-through; the rest whole.
    private void Underfoot()
    {
        foreach (var (id, root) in _bodies)
        {
            if (!IsInstanceValid(root) || !_bodyParts.TryGetValue(id, out var parts))
            {
                continue;
            }
            var at = parts.Lies;
            // (Not its own model, which the showcase hides on the table while it plays the casualty.)
            var under = _scene?.Models.Any(m => m.Id != id && !_hiddenForPicture.Contains(m.Id) && new Vector2((float)m.X - at.X, (float)m.Y - at.Z).Length() < m.Radius + parts.Radius * 0.7f) == true;
            var fade = under ? BodyUnderfoot : 1f;
            foreach (var shader in parts.Shaders)
            {
                shader.SetShaderParameter("fade", fade);
            }
            foreach (var paint in parts.Paints)
            {
                paint.Transparency = under ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
                paint.AlbedoColor = paint.AlbedoColor with { A = fade };
            }
        }
    }

    // A body drawn lying at once, as a casualty figure ends: facing as the model last faced.
    private void LayBody(ModelShape model)
    {
        var yaw = FacingNow(model.Id);
        var (root, holder, shaders, paints, _, tall, lyingScale, length) = BuildCasualty(model, new Vector3((float)model.X, (float)model.Standing, (float)model.Y), yaw, _bodiesNode);
        if (RigOf(root) is { } rig)
        {
            // Ragdoll: the same fall worked out at once (the way it fell this match, else backwards), posed at its end.
            var (push, hit) = _fallPush.TryGetValue(model.Id, out var fell) ? fell : (Backwards(yaw) * (float)RagdollImpacts.Crumple, (float)RagdollImpacts.ChestHigh);
            var blocks = _fallObstacles.TryGetValue(model.Id, out var kept) ? kept : _fallObstacles[model.Id] = Obstacles(model.Id, root.GlobalPosition);
            Simulate(rig, root.GlobalPosition, (float)model.Radius, push, tall, model.Id, hit, blocks, wait: true);
            PoseRig(rig, 1);
        }
        else
        {
            var (basis, offset) = Tipped(1, yaw, lyingScale, tall, length);
            holder.Basis = basis;
            holder.Position = new Vector3(0, tall / 2, 0) + offset;
        }
        Darken(shaders, paints, [.. paints.Select(p => p.AlbedoColor)], BodyShade);
        Keep(model.Id, root, shaders, paints, (float)model.Radius, spread: false);
    }

    private void RemoveBody(ModelId id)
    {
        _bodyParts.Remove(id);
        if (_bodies.Remove(id, out var root) && IsInstanceValid(root))
        {
            root.GetParent()?.RemoveChild(root);
            root.QueueFree();
        }
    }

    private void ClearBodies()
    {
        foreach (var id in _bodies.Keys.ToList())
        {
            RemoveBody(id);
        }
    }

    // For the UI checks and the showcase: the bodies drawn now, how many were laid, and (dev) clearing the bodies of
    // models still on the table (a staged casualty in the showcase) once its scene is over.
    public IReadOnlyCollection<ModelId> Bodies => _bodies.Keys;

    public int BodiesLaid { get; private set; }

    public (Vector3 At, float Tilt, float Shade, bool BaseShown, float Fade, bool Pool, Vector3 Lies)? BodyLook(ModelId id)
    {
        if (!_bodies.TryGetValue(id, out var root) || !IsInstanceValid(root) || root.GetChildCount() == 0)
        {
            return null;
        }
        // (A part drawn by an override, the seat colour on the Accent, keeps an unused shader under it: not that one.)
        var shader = root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(m => m.MaterialOverride is null)
            .SelectMany(m => Enumerable.Range(0, m.Mesh?.GetSurfaceCount() ?? 0).Select(i => m.GetSurfaceOverrideMaterial(i))).OfType<ShaderMaterial>().FirstOrDefault();
        var shade = shader?.GetShaderParameter("albedo_colour").AsColor().R ?? BodyShade;
        var fade = shader?.GetShaderParameter("fade").VariantType is Variant.Type.Float ? (float)shader.GetShaderParameter("fade") : 1f;
        return (root.Position, TorsoTilt(root), shade, root.GetChildren().OfType<MeshInstance3D>().Any(m => m.Visible), fade, root.GetNodeOrNull<Node3D>("Pool") is not null,
            _bodyParts.TryGetValue(id, out var parts) ? parts.Lies : root.GlobalPosition);
    }

    // For the UI check: draws every body again directly (the way Off, a catch-up or a rejoin draws them).
    public void RelayBodies()
    {
        ClearBodies();
        SyncBodies();
    }

    // Dev only (the ragdoll review, TRENCH_DEV=ragdolls, 2026-11-24; never in play): every figure file of the catalog
    // from `from` on (at most `count`), side by side from `at`, each falling from the same chest-high hit along +X, posed
    // at moment t of its fall (0 standing, 1 at rest). The figures replace the last lineup. Returns each one's file,
    // whether it has a skeleton, and how far its torso lies over.
    private Node3D? _lineup;

    public List<(string File, bool Rigged, float Tilt, float X, bool Low)> RagdollLineup(Vector3 at, int from, int count, double t)
    {
        _lineup?.GetParent()?.RemoveChild(_lineup);
        _lineup?.QueueFree();
        _lineup = new Node3D { Name = "RagdollLineup" };
        _effects.AddChild(_lineup);
        var seen = new List<(string File, bool Rigged, float Tilt, float X, bool Low)>();
        var files = Visuals.Catalog.Entries.Where(e => e.Key.StartsWith("unit:", StringComparison.Ordinal))
            .GroupBy(e => e.Value).Select(g => g.First()).OrderBy(e => e.Value, StringComparer.Ordinal).Skip(from).Take(count).ToList();
        var x = 0f;
        foreach (var (key, file) in files)
        {
            if (Visuals.Model(key) is not { } figure)
            {
                continue;
            }
            var height = (float)Visuals.Catalog.HeightFor(key);
            var root = new Node3D { Position = at + new Vector3(x, 0, 0) };
            _lineup.AddChild(root);
            root.AddChild(figure);
            Visuals.FitOnBase(figure, Vector3.Zero, Math.Max(0.5f, height * 0.42f), height);
            var tilt = 0f;
            var low = false;
            if (RigOf(root) is { } rig)
            {
                Simulate(rig, root.GlobalPosition, 0.5f, new Vector3(1, 0, 0) * (float)RagdollImpacts.Shot, height, new ModelId(System.IO.Path.GetFileNameWithoutExtension(file)), (float)RagdollImpacts.ChestHigh, [], wait: true, flat: true);
                PoseRig(rig, t);
                tilt = TorsoTilt(root);
                // Down on the ground: its hips and neck within a third of its height of it (a figure on all fours lies
                // down without its trunk turning much).
                var s = rig.Skeleton;
                var hips = (s.GlobalTransform * s.GetBoneGlobalPose(rig.Joint[(int)RagdollJoint.Hips]).Origin).Y - at.Y;
                var neck = (s.GlobalTransform * s.GetBoneGlobalPose(rig.Joint[(int)RagdollJoint.Neck]).Origin).Y - at.Y;
                low = hips < height / 3 && neck < height / 3;
            }
            seen.Add((file, RigOf(root) is not null, tilt, at.X + x, low));
            x += Math.Max(1.8f, height * 1.4f);
        }
        return seen;
    }

    // The dev step `walks` (2026-11-25): the figures named, side by side on open ground, each posed mid-stride at one moment
    // of the walk cycle (phase: 0 legs together, pi/2 the left leg forward, pi together again, 3pi/2 the right forward),
    // facing across the line. Dev only.
    public int WalkLineup(Vector3 at, IReadOnlyList<string> files, float phase)
    {
        _lineup?.GetParent()?.RemoveChild(_lineup);
        _lineup?.QueueFree();
        _lineup = new Node3D { Name = "RagdollLineup" };
        _effects.AddChild(_lineup);
        var x = 0f;
        var posed = 0;
        foreach (var file in files)
        {
            if (Visuals.Catalog.Entries.FirstOrDefault(e => e.Value == file && e.Key.StartsWith("unit:", StringComparison.Ordinal)) is not { Key: { } key }
                || Visuals.Model(key) is not { } figure)
            {
                continue;
            }
            var height = (float)Visuals.Catalog.HeightFor(key);
            // Faced to the camera's right, so the legs swing across the picture.
            var root = new Node3D { Position = at + new Vector3(x, 0, 0), Rotation = new Vector3(0, Mathf.Pi / 2, 0) };
            _lineup.AddChild(root);
            root.AddChild(figure);
            Visuals.FitOnBase(figure, Vector3.Zero, Math.Max(0.5f, height * 0.42f), height);
            if (RigOf(root) is { } rig)
            {
                PoseWalk(rig, phase, 1);
                posed++;
            }
            x += Math.Max(1.6f, height * 1.2f);
        }
        return posed;
    }

    public void ClearRagdollLineup()
    {
        _lineup?.GetParent()?.RemoveChild(_lineup);
        _lineup?.QueueFree();
        _lineup = null;
    }

    public void ClearStagedBodies()
    {
        foreach (var id in _bodies.Keys.Where(id => _scene?.Find(id) is not null).ToList())
        {
            RemoveBody(id);
        }
    }
}
