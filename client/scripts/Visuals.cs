using Client.Core;
using Godot;

// Graphics stage G0: loads the art the catalog (client/assets/catalog.json) names, once each, and hands out copies. glTF
// models are read at run time (GltfDocument), so they work without the editor's import step; a .tscn is loaded as a
// scene. Anything missing or broken is noted in Failures and the caller draws its plain shape instead, so art can
// never stop the game from running. Art never changes what the rules see: the caller fits it to the rules' own sizes.
public sealed class Visuals
{
    public const string AssetsRoot = "res://assets/";

    private VisualCatalog _catalog = VisualCatalog.Empty;
    private readonly Dictionary<string, Node3D?> _models = [];
    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly List<string> _failures = [];

    public VisualCatalog Catalog => _catalog;

    // What could not be used, one line each ("units/x.glb: file not found").
    public IReadOnlyList<string> Failures => _failures;

    public static Visuals Load()
    {
        var visuals = new Visuals();
        var path = AssetsRoot + VisualCatalog.FileName;
        if (!Godot.FileAccess.FileExists(path))
        {
            return visuals;
        }
        if (VisualCatalog.TryParse(Godot.FileAccess.GetFileAsString(path), out var problem) is { } catalog)
        {
            visuals._catalog = catalog;
        }
        else
        {
            visuals._failures.Add($"{VisualCatalog.FileName}: {problem}");
        }
        return visuals;
    }

    // Uses another catalog (the UI check tries a broken one); everything loaded before is forgotten.
    public void Use(VisualCatalog catalog)
    {
        _catalog = catalog;
        _models.Clear();
        _textures.Clear();
        _failures.Clear();
    }

    // A new copy of the model for the first key that has an entry, or null for the plain shape.
    public Node3D? Model(params string[] keys)
    {
        if (_catalog.PathFor(keys) is not { } path)
        {
            return null;
        }
        if (!_models.TryGetValue(path, out var original))
        {
            original = LoadModel(path);
            _models[path] = original;
        }
        return original?.Duplicate() as Node3D;
    }

    // The texture for the first key that has an entry, or null for the plain colour.
    public Texture2D? Texture(params string[] keys)
    {
        if (_catalog.PathFor(keys) is not { } path)
        {
            return null;
        }
        if (!_textures.TryGetValue(path, out var texture))
        {
            texture = LoadTexture(path);
            _textures[path] = texture;
        }
        return texture;
    }

    private Node3D? LoadModel(string path)
    {
        var resource = AssetsRoot + path;
        if (!Godot.FileAccess.FileExists(resource))
        {
            _failures.Add($"{path}: file not found");
            return null;
        }
        try
        {
            if (path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
            {
                if (ResourceLoader.Load<PackedScene>(resource)?.Instantiate() is Node3D scene)
                {
                    return scene;
                }
                _failures.Add($"{path}: not a 3D scene");
                return null;
            }
            var document = new GltfDocument();
            var state = new GltfState();
            var error = document.AppendFromFile(ProjectSettings.GlobalizePath(resource), state);
            if (error != Error.Ok || document.GenerateScene(state) is not Node3D model)
            {
                _failures.Add($"{path}: could not be read ({error})");
                return null;
            }
            return model;
        }
        catch (Exception e)
        {
            _failures.Add($"{path}: {e.Message}");
            return null;
        }
    }

    private Texture2D? LoadTexture(string path)
    {
        var resource = AssetsRoot + path;
        if (!Godot.FileAccess.FileExists(resource))
        {
            _failures.Add($"{path}: file not found");
            return null;
        }
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(resource));
        if (image is null || image.IsEmpty())
        {
            _failures.Add($"{path}: could not be read");
            return null;
        }
        return ImageTexture.CreateFromImage(image);
    }

    // The box around every mesh under the node, in the node's own space.
    public static Aabb Bounds(Node3D node)
    {
        Aabb? box = null;
        void Walk(Node n, Transform3D to)
        {
            var here = n is Node3D n3 ? to * n3.Transform : to;
            if (n is MeshInstance3D { Mesh: { } mesh })
            {
                var b = here * mesh.GetAabb();
                box = box is { } a ? a.Merge(b) : b;
            }
            foreach (var child in n.GetChildren())
            {
                Walk(child, here);
            }
        }
        foreach (var child in node.GetChildren())
        {
            Walk(child, Transform3D.Identity);
        }
        return box ?? new Aabb(Vector3.Zero, Vector3.Zero);
    }

    // Graphics stage G3: the parts of a figure named "Accent..." (its sash, a pennant) are drawn with the given material,
    // so the seat colour and the activated wash are the client's, never baked into the art. Returns how many parts.
    public static int PaintAccent(Node3D figure, Material material)
    {
        var painted = 0;
        foreach (var mesh in figure.FindChildren("Accent*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            mesh.MaterialOverride = material;
            painted++;
        }
        return painted;
    }

    // Scales the model evenly to the given height, narrower still if it would be wider than the base, and stands its
    // bottom centre on the given point. The rules' base and position are unchanged; only the drawing fits them.
    public static void FitOnBase(Node3D model, Vector3 at, float radius, float height)
    {
        var box = Bounds(model);
        if (box.Size.Y <= 0.0001f)
        {
            model.Position = at;
            return;
        }
        var scale = height / box.Size.Y;
        var widest = Math.Max(box.Size.X, box.Size.Z) * scale;
        if (widest > 2 * radius)
        {
            scale *= 2 * radius / widest;
        }
        model.Scale = Vector3.One * scale;
        var bottomCentre = new Vector3(box.Position.X + box.Size.X / 2, box.Position.Y, box.Position.Z + box.Size.Z / 2) * scale;
        model.Position = at - bottomCentre;
    }
}
