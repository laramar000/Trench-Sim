using Client.Core;
using Godot;
using Rules.Core.Geometry;
using Rules.Core.State;

// M7.4 stage 4a: the battlefield in 3D, read-only. It draws Client.Core's BoardScene with placeholder shapes (1 scene
// unit = 1 inch; table x is scene X, table y is scene Z) and lets the player move the camera and select a model. It
// shows the server's state and decides nothing: heights are display conventions (DisplayHeight), and line of sight and
// cover will come from the rules geometry, never from this picture.
//
// Keys while the battlefield has focus (Tab to it, or click it): arrows pan, + and - zoom, Q and E turn 90 degrees,
// F fits the table, T switches between the angled and the top-down camera, N and P select the next and previous model.
// Tab and Shift+Tab still move to the next control. Mouse: click selects, right or middle drag pans, the wheel zooms.
public partial class BoardView : SubViewportContainer
{
    private const float AngledPitch = 55f;
    private const float FitMargin = 0.05f;

    private readonly SubViewport _viewport = new() { OwnWorld3D = true, HandleInputLocally = false, Msaa3D = Viewport.Msaa.Msaa2X };
    private readonly Node3D _content = new();
    private readonly Camera3D _camera = new() { Fov = 50, Near = 0.1f, Far = 1000 };
    private readonly WorldEnvironment _environment = new();
    private BoardScene? _scene;
    private ModelId? _selected;
    private Vector3 _target;
    private float _distance = 50;
    private float _orthoSize = 40;
    private int _yaw;
    private bool _topDown;
    private bool _autoFit = true;
    private bool _dragging;
    private float _textScale = 1f;
    private bool _highContrast;

    public event Action? Changed;

    public BoardScene? Scene => _scene;

    public ModelShape? Selected => _scene?.Find(_selected);

    public bool TopDown => _topDown;

    public int Yaw => _yaw;

    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        Stretch = true;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0, 440);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(_viewport);
        _viewport.AddChild(_environment);
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-60, 35, 0), LightEnergy = 0.9f });
        _viewport.AddChild(_content);
        _viewport.AddChild(_camera);
        _camera.MakeCurrent();
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
        Resized += () =>
        {
            if (_autoFit)
            {
                CallDeferred(MethodName.Fit);
            }
        };
        ApplyEnvironment();
    }

    // Redraws the battlefield from the latest state. The selection is kept while that model is still on the table.
    public void Show(BoardScene? scene)
    {
        var first = _scene is null && scene is not null;
        _scene = scene;
        if (_scene?.Find(_selected) is null)
        {
            _selected = null;
        }
        Rebuild();
        if (first)
        {
            _target = new Vector3((float)scene!.Width / 2, 0, (float)scene.Depth / 2);
            CallDeferred(MethodName.Fit);
        }
        Changed?.Invoke();
    }

    public void SetLook(float textScale, bool highContrast)
    {
        _textScale = textScale;
        _highContrast = highContrast;
        ApplyEnvironment();
        Rebuild();
    }

    // Brings the whole table (and the tops of walls) into view for the current camera.
    public void Fit()
    {
        if (_scene is null || Size.X < 10 || Size.Y < 10)
        {
            return;
        }
        _autoFit = true;
        _target = new Vector3((float)_scene.Width / 2, 0, (float)_scene.Depth / 2);
        if (_topDown)
        {
            var turned = _yaw % 180 != 0;
            var across = (float)(turned ? _scene.Depth : _scene.Width);
            var along = (float)(turned ? _scene.Width : _scene.Depth);
            _orthoSize = Math.Max(along, across * Size.Y / Size.X) * (1 + 2 * FitMargin);
            PlaceCamera();
            Changed?.Invoke();
            return;
        }
        // Move the camera back until every corner of the table, on the ground and at wall height, is on screen.
        _distance = 5;
        PlaceCamera();
        for (var i = 0; i < 80 && !TableInView(FitMargin); i++)
        {
            _distance *= 1.06f;
            PlaceCamera();
        }
        Changed?.Invoke();
    }

    public void ToggleTopDown()
    {
        _topDown = !_topDown;
        Rebuild();
        Fit();
    }

    public void Turn(int steps)
    {
        _yaw = ((_yaw + steps * 90) % 360 + 360) % 360;
        Fit();
    }

    public void Zoom(float factor)
    {
        _autoFit = false;
        if (_topDown)
        {
            _orthoSize = Math.Clamp(_orthoSize * factor, 4, 200);
        }
        else
        {
            _distance = Math.Clamp(_distance * factor, 4, 200);
        }
        PlaceCamera();
        Changed?.Invoke();
    }

    // Pans by a distance in inches, relative to the way the camera faces (right, and away from the viewer).
    public void Pan(float right, float forward)
    {
        _autoFit = false;
        var yaw = Mathf.DegToRad(_yaw);
        var r = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
        var f = new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));
        _target += r * right + f * forward;
        if (_scene is not null)
        {
            _target = new Vector3(Math.Clamp(_target.X, 0, (float)_scene.Width), 0, Math.Clamp(_target.Z, 0, (float)_scene.Depth));
        }
        PlaceCamera();
        Changed?.Invoke();
    }

    public void SelectNext(bool forward)
    {
        if (_scene?.Next(_selected, forward) is { } next)
        {
            Select(next.Id);
        }
    }

    public void Select(ModelId? id)
    {
        _selected = _scene?.Find(id)?.Id;
        Rebuild();
        Changed?.Invoke();
    }

    // Where a table point (inches, at a height) appears, in this control's own coordinates; null if behind the camera.
    public Vector2? ScreenOf(double x, double y, double height = 0)
    {
        var point = new Vector3((float)x, (float)height, (float)y);
        if (_camera.IsPositionBehind(point))
        {
            return null;
        }
        return _camera.UnprojectPosition(point) * (Size / _viewport.Size);
    }

    // The table point under a position in this control (inches), at the given height; null if the ray misses that level.
    public (double X, double Y)? TableAt(Vector2 local, double height = 0)
    {
        var at = local * (_viewport.Size / Size);
        var hit = new Plane(Vector3.Up, (float)height).IntersectsRay(_camera.ProjectRayOrigin(at), _camera.ProjectRayNormal(at));
        return hit is { } p ? (p.X, p.Z) : null;
    }

    // The model under a position in this control: platform tops first, then the ground.
    public ModelShape? ModelAt(Vector2 local)
    {
        if (_scene is null)
        {
            return null;
        }
        foreach (var level in _scene.StandingLevels)
        {
            if (TableAt(local, level) is { } p && _scene.ModelAt(p.X, p.Y, level) is { } model)
            {
                return model;
            }
        }
        return null;
    }

    // Is every corner of the table, on the ground and at wall height, inside the view (with a margin)?
    public bool TableInView(float margin)
    {
        if (_scene is null)
        {
            return false;
        }
        foreach (var (x, y) in new[] { (0.0, 0.0), (_scene.Width, 0.0), (0.0, _scene.Depth), (_scene.Width, _scene.Depth) })
        {
            foreach (var h in new[] { 0.0, DisplayHeight.Wall })
            {
                if (!InView(ScreenOf(x, y, h), margin))
                {
                    return false;
                }
            }
        }
        return true;
    }

    public bool InView(Vector2? point, float margin = 0) =>
        point is { } p && p.X >= Size.X * margin && p.X <= Size.X * (1 - margin) && p.Y >= Size.Y * margin && p.Y <= Size.Y * (1 - margin);

    public string CameraText => $"{(_topDown ? "top-down" : "angled")}, facing {Facing()}";

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                GrabFocus();
                Select(ModelAt(click.Position)?.Id);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } drag:
                _dragging = drag.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Zoom(0.9f);
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Zoom(1.1f);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                var inchesPerPixel = (_topDown ? _orthoSize : _distance * 0.8f) / Math.Max(1, Size.Y);
                Pan(-motion.Relative.X * inchesPerPixel, motion.Relative.Y * inchesPerPixel);
                AcceptEvent();
                break;
            case InputEventKey { Pressed: true } key when HandleKey(key):
                AcceptEvent();
                break;
        }
    }

    public override void _Draw()
    {
        if (HasFocus())
        {
            DrawRect(new Rect2(Vector2.Zero, Size), _highContrast ? Colors.Yellow : Colors.White, filled: false, width: 3);
        }
    }

    private bool HandleKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Left: Pan(-2, 0); return true;
            case Key.Right: Pan(2, 0); return true;
            case Key.Up: Pan(0, 2); return true;
            case Key.Down: Pan(0, -2); return true;
            case Key.Equal or Key.Plus or Key.KpAdd: Zoom(0.85f); return true;
            case Key.Minus or Key.KpSubtract: Zoom(1.15f); return true;
            case Key.Q: Turn(-1); return true;
            case Key.E: Turn(1); return true;
            case Key.F: Fit(); return true;
            case Key.T: ToggleTopDown(); return true;
            case Key.N: SelectNext(!key.ShiftPressed); return true;
            case Key.P: SelectNext(false); return true;
            default: return false;
        }
    }

    private string Facing() => _yaw switch
    {
        0 => "north",
        90 => "west",
        180 => "south",
        _ => "east",
    };

    private void PlaceCamera()
    {
        var yaw = Mathf.DegToRad(_yaw);
        if (_topDown)
        {
            _camera.Projection = Camera3D.ProjectionType.Orthogonal;
            _camera.Size = _orthoSize;
            _camera.Position = _target + new Vector3(0, 100, 0);
            _camera.Rotation = new Vector3(-Mathf.Pi / 2, yaw, 0);
            return;
        }
        _camera.Projection = Camera3D.ProjectionType.Perspective;
        var pitch = Mathf.DegToRad(AngledPitch);
        var back = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
        _camera.Position = _target + back * _distance;
        _camera.Rotation = new Vector3(-pitch, yaw, 0);
    }

    private void ApplyEnvironment()
    {
        _environment.Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = _highContrast ? Colors.Black : new Color(0.16f, 0.16f, 0.18f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
            AmbientLightEnergy = 0.55f,
        };
    }

    private void Rebuild()
    {
        foreach (var child in _content.GetChildren())
        {
            child.QueueFree();
        }
        if (_scene is not { } scene)
        {
            return;
        }
        Box(new Vector3((float)scene.Width / 2, -0.05f, (float)scene.Depth / 2), new Vector3((float)scene.Width, 0.1f, (float)scene.Depth),
            _highContrast ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.33f, 0.36f, 0.28f));
        foreach (var zone in scene.Zones)
        {
            var area = zone.Area;
            Box(new Vector3((float)area.CentreX, 0.01f, (float)area.CentreY), new Vector3((float)area.Width, 0.02f, (float)area.Depth),
                _highContrast ? new Color(0.36f, 0.36f, 0.36f) : new Color(0.38f, 0.42f, 0.5f));
            Text(zone.Label, new Vector3((float)area.CentreX, 0.3f, (float)area.CentreY), 0.8f);
        }
        foreach (var piece in scene.Terrain)
        {
            var area = piece.Area;
            var h = (float)piece.Height;
            Box(new Vector3((float)area.CentreX, h / 2, (float)area.CentreY), new Vector3((float)area.Width, h, (float)area.Depth), TerrainColour(piece));
            Text(piece.Label, new Vector3((float)area.CentreX, h + 0.4f, (float)area.CentreY), 0.6f);
        }
        foreach (var model in scene.Models)
        {
            var standing = (float)model.Standing;
            var tall = model.Status == ModelStatus.Standing ? 1.2f : 0.3f;
            var body = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = (float)model.Radius, BottomRadius = (float)model.Radius, Height = tall, RadialSegments = 24 },
                Position = new Vector3((float)model.X, standing + tall / 2, (float)model.Y),
                MaterialOverride = Paint(ModelColour(model)),
            };
            _content.AddChild(body);
            var selected = model.Id == _selected;
            if (selected)
            {
                // A ring on the ground around the selected base, drawn through walls so it is never hidden.
                var ring = new MeshInstance3D
                {
                    Mesh = new TorusMesh { InnerRadius = (float)model.Radius + 0.1f, OuterRadius = (float)model.Radius + 0.35f },
                    Position = new Vector3((float)model.X, standing + 0.05f, (float)model.Y),
                    MaterialOverride = Paint(_highContrast ? Colors.Yellow : new Color(1f, 0.85f, 0.2f), noDepth: true),
                };
                _content.AddChild(ring);
            }
            Text((selected ? "SELECTED " : "") + model.Label, new Vector3((float)model.X, standing + tall + 0.6f, (float)model.Y), selected ? 1f : 0.85f);
        }
    }

    private Color TerrainColour(TerrainShape piece) => _highContrast
        ? piece.Profile switch { TerrainProfile.Wall => new Color(0.75f, 0.75f, 0.75f), TerrainProfile.Cover => new Color(0.5f, 0.5f, 0.5f), _ => new Color(0.3f, 0.3f, 0.3f) }
        : piece.Profile switch { TerrainProfile.Wall => new Color(0.55f, 0.53f, 0.5f), TerrainProfile.Cover => new Color(0.6f, 0.48f, 0.3f), _ => new Color(0.42f, 0.4f, 0.28f) };

    // Seats differ by colour and by the [A]/[B] letter in every label; colour is never the only sign.
    private Color ModelColour(ModelShape model) => _highContrast
        ? model.Seat == Seat.One ? Colors.White : new Color(1f, 0.9f, 0f)
        : model.Seat == Seat.One ? new Color(0.35f, 0.55f, 0.95f) : new Color(0.9f, 0.35f, 0.3f);

    private void Box(Vector3 centre, Vector3 size, Color colour)
    {
        _content.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = centre, MaterialOverride = Paint(colour) });
    }

    // Labels keep their size on screen at any distance, follow the text size setting and show through walls.
    private void Text(string text, Vector3 at, float size)
    {
        _content.AddChild(new Label3D
        {
            Text = text,
            Position = at,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            // A fixed-size label is smaller under the orthographic top-down camera; scale it up there.
            PixelSize = _topDown ? 0.0035f : 0.0012f,
            FontSize = (int)(32 * size * _textScale),
            OutlineSize = 10,
            Modulate = Colors.White,
            OutlineModulate = Colors.Black,
            NoDepthTest = true,
            RenderPriority = 10,
            OutlineRenderPriority = 9,
        });
    }

    private static StandardMaterial3D Paint(Color colour, bool noDepth = false) => new()
    {
        AlbedoColor = colour,
        NoDepthTest = noDepth,
        ShadingMode = noDepth ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
    };
}
