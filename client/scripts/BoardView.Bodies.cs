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
        Keep(ghost.Model, ghost.Root);
    }

    // Lays a body to rest: its base hidden, under the bodies node, the model's only body.
    private void Keep(ModelId id, Node3D root)
    {
        foreach (var part in root.GetChildren().OfType<MeshInstance3D>())
        {
            part.Visible = false;
        }
        root.GetParent()?.RemoveChild(root);
        root.Name = $"Body{id.Value}";
        if (_bodies.Remove(id, out var old) && IsInstanceValid(old))
        {
            old.GetParent()?.RemoveChild(old);
            old.QueueFree();
        }
        _bodiesNode.AddChild(root);
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
    }

    // A body drawn lying at once, as a casualty figure ends: facing as the model last faced.
    private void LayBody(ModelShape model)
    {
        var yaw = FacingNow(model.Id);
        var (root, holder, shaders, paints, _, tall, lyingScale, length) = BuildCasualty(model, new Vector3((float)model.X, (float)model.Standing, (float)model.Y), yaw, _bodiesNode);
        var (basis, offset) = Tipped(1, yaw, lyingScale, tall, length);
        holder.Basis = basis;
        holder.Position = new Vector3(0, tall / 2, 0) + offset;
        Darken(shaders, paints, [.. paints.Select(p => p.AlbedoColor)], BodyShade);
        Keep(model.Id, root);
    }

    private void RemoveBody(ModelId id)
    {
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

    public (Vector3 At, float Tilt, float Shade, bool BaseShown)? BodyLook(ModelId id)
    {
        if (!_bodies.TryGetValue(id, out var root) || !IsInstanceValid(root) || root.GetChildCount() == 0)
        {
            return null;
        }
        var holder = root.GetChildren().OfType<Node3D>().FirstOrDefault(n => n is not MeshInstance3D);
        var shader = root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
            .SelectMany(m => Enumerable.Range(0, m.Mesh?.GetSurfaceCount() ?? 0).Select(i => m.GetSurfaceOverrideMaterial(i))).OfType<ShaderMaterial>().FirstOrDefault();
        var shade = shader?.GetShaderParameter("albedo_colour").AsColor().R ?? BodyShade;
        var up = holder is null ? Vector3.Up : holder.Basis.Y.Normalized();
        return (root.Position, up.AngleTo(Vector3.Up), shade, root.GetChildren().OfType<MeshInstance3D>().Any(m => m.Visible));
    }

    // For the UI check: draws every body again directly (the way Off, a catch-up or a rejoin draws them).
    public void RelayBodies()
    {
        ClearBodies();
        SyncBodies();
    }

    public void ClearStagedBodies()
    {
        foreach (var id in _bodies.Keys.Where(id => _scene?.Find(id) is not null).ToList())
        {
            RemoveBody(id);
        }
    }
}
