using Client.Core;
using Godot;
using Rules.Core.Geometry;
using Rules.Core.State;

// M7.4: the battlefield in 3D. It draws Client.Core's BoardScene with placeholder shapes (1 scene unit = 1 inch; table x
// is scene X, table y is scene Z) and lets the player move the camera and select a model (stage 4a). Stage 4b adds
// previews: the player picks a placement point, a path or a target, Client.Core's Interaction and Previews work out
// what the server would probably say, and this view draws it. Stage 4c sends a confirmed command through the screen's
// MatchController (Space; Enter for a placement or a target; A activates the selected model). It shows the server's state and decides nothing:
// heights are display conventions (DisplayHeight), line of sight and cover come from the rules geometry (the sight
// rays drawn are the rules' own), and every preview says "preview; the server decides". Only the server's answer says
// whether a command was taken.
//
// Keys while the battlefield has focus (Tab to it, or click it): arrows pan, + and - zoom, Q and E turn 90 degrees,
// F fits the table, T switches between the angled and the top-down camera, N and P select the next and previous model.
// Previews: L place a model, M move, D dash, R retreat, S shoot, C charge, X fight. Then arrows move the cursor (half
// an inch, 2 inches with Shift), Enter fixes a waypoint (or sends a placement or a target), Backspace removes a
// waypoint, N and P choose the target or the model to place, Space sends the command, Esc cancels. A activates the
// selected model.
// Tab and Shift+Tab still move to the next control. Mouse: click selects (or picks the target, or fixes a waypoint),
// moving over the table moves the cursor, right or middle drag pans, the wheel zooms.
public partial class BoardView : SubViewportContainer
{
    private const float AngledPitch = 55f;
    private const float FitMargin = 0.05f;

    private readonly SubViewport _viewport = new() { OwnWorld3D = true, HandleInputLocally = false, Msaa3D = Viewport.Msaa.Msaa2X };
    private readonly Node3D _content = new();
    private readonly Camera3D _camera = new() { Fov = 50, Near = 0.1f, Far = 1000 };
    private readonly WorldEnvironment _environment = new();
    private readonly Node3D _overlay = new();
    private readonly HoverThrottle _hover = new(0.08);
    private readonly List<string> _overlayTexts = [];
    private long _drawnVersion = -1;
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

    // What the player is choosing on the battlefield, and its preview (Client.Core, tested without Godot).
    public Interaction Interaction { get; } = new();

    // The words of every preview label drawn now (for the UI check and for screen readers' sake, the same words).
    public IReadOnlyList<string> OverlayTexts => _overlayTexts;

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
        _viewport.AddChild(_overlay);
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

    // Redraws the battlefield from the latest state. The selection is kept while that model is still on the table; a
    // preview is kept while it still fits the match (Interaction.Update).
    public void Show(BoardScene? scene, MatchState? state = null, Seat? seat = null)
    {
        var first = _scene is null && scene is not null;
        _scene = scene;
        if (_scene?.Find(_selected) is null)
        {
            _selected = null;
        }
        Interaction.Update(state ?? MatchState.None, seat);
        Rebuild();
        DrawPreview();
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
        DrawPreview();
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
        DrawPreview();
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

    // Starts a preview: the same as the L, M, D, R, S, C and X keys.
    public bool StartPreview(InteractionMode mode)
    {
        var started = Interaction.Start(mode);
        Sync();
        return started;
    }

    // Moves the preview cursor by inches relative to the camera (right, and away from the viewer), as the arrows do.
    public void MoveCursor(float right, float forward)
    {
        var yaw = Mathf.DegToRad(_yaw);
        var dx = Mathf.Cos(yaw) * right - Mathf.Sin(yaw) * forward;
        var dy = -Mathf.Sin(yaw) * right - Mathf.Cos(yaw) * forward;
        Interaction.MoveCursor(Math.Round(dx, 3), Math.Round(dy, 3));
        Sync();
    }

    public override void _Process(double delta)
    {
        if (Interaction.UsesCursor && _hover.Take(Time.GetTicksMsec() / 1000.0) is { } point)
        {
            Interaction.SetCursor(point.X, point.Y);
        }
        Sync();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                GrabFocus();
                Click(click.Position);
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
            case InputEventMouseMotion hover when Interaction.UsesCursor && TableAt(hover.Position) is { } p:
                // Throttled: the preview is worked out at most every 80 ms while the mouse moves (_Process).
                _hover.Offer(new TablePoint(Math.Round(p.X * 10) / 10, Math.Round(p.Y * 10) / 10));
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

    // A left click: picks the target in Shoot, Charge and Fight; puts the cursor there (and fixes a waypoint on a path)
    // when placing or moving; otherwise selects the model under it.
    public void Click(Vector2 local)
    {
        if (Interaction.IsTarget)
        {
            if (ModelAt(local) is { } model)
            {
                Interaction.SetTarget(model.Id);
            }
        }
        else if (Interaction.UsesCursor)
        {
            if (TableAt(local) is { } p)
            {
                Interaction.SetCursor(Math.Round(p.X * 10) / 10, Math.Round(p.Y * 10) / 10);
                if (Interaction.IsPath)
                {
                    Interaction.AddPoint();
                }
            }
        }
        else
        {
            Select(ModelAt(local)?.Id);
        }
        Sync();
    }

    private static InteractionMode? ModeKey(Key key) => key switch
    {
        Key.L => InteractionMode.Place,
        Key.M => InteractionMode.Move,
        Key.D => InteractionMode.Dash,
        Key.R => InteractionMode.Retreat,
        Key.S => InteractionMode.Shoot,
        Key.C => InteractionMode.Charge,
        Key.X => InteractionMode.Fight,
        _ => null,
    };

    private bool HandleKey(InputEventKey key)
    {
        if (ModeKey(key.Keycode) is { } mode)
        {
            StartPreview(mode);
            return true;
        }
        if (Interaction.Mode != InteractionMode.None && HandlePreviewKey(key))
        {
            Sync();
            return true;
        }
        if (key.Keycode == Key.A && Interaction.Mode == InteractionMode.None)
        {
            ActivateSelected();
            return true;
        }
        if (key.Keycode == Key.Escape && Interaction.Notice is not null)
        {
            Interaction.Cancel();
            Sync();
            return true;
        }
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

    private bool HandlePreviewKey(InputEventKey key)
    {
        var step = (float)(key.ShiftPressed ? Interaction.BigStep : Interaction.Step);
        switch (key.Keycode)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down when Interaction.UsesCursor:
                MoveCursor(key.Keycode == Key.Left ? -step : key.Keycode == Key.Right ? step : 0, key.Keycode == Key.Up ? step : key.Keycode == Key.Down ? -step : 0);
                return true;
            case Key.Enter or Key.KpEnter:
                if (Interaction.IsPath)
                {
                    Interaction.AddPoint();
                }
                else
                {
                    Confirm();
                }
                return true;
            case Key.Space:
                Confirm();
                return true;
            case Key.Backspace:
                Interaction.RemovePoint();
                return true;
            case Key.Escape:
                Interaction.Cancel();
                return true;
            case Key.N:
                Interaction.NextChoice(!key.ShiftPressed);
                return true;
            case Key.P:
                Interaction.NextChoice(false);
                return true;
            default:
                return false;
        }
    }

    // Sends the previewed command (an explicit key press: Space, or Enter for a placement or a target). The answer comes
    // back through the same MatchController as the hint buttons; _Process redraws when it arrives.
    public async void Confirm()
    {
        await Interaction.ConfirmAsync();
        Sync();
    }

    // A: activate the selected model (yours), as its hint button does.
    public async void ActivateSelected()
    {
        if (Selected is { Yours: true } model)
        {
            await Interaction.ActivateAsync(model.Id);
            Sync();
        }
    }

    // Redraws the preview when the interaction changed since it was last drawn, and tells the screen.
    private void Sync()
    {
        if (Interaction.Version == _drawnVersion)
        {
            return;
        }
        DrawPreview();
        Changed?.Invoke();
    }

    // The preview, drawn on top of everything (never hidden by a wall), in words as well as lines: legal or refused is
    // written on it, not shown by colour alone.
    private void DrawPreview()
    {
        _drawnVersion = Interaction.Version;
        foreach (var child in _overlay.GetChildren())
        {
            child.QueueFree();
        }
        _overlayTexts.Clear();
        if (_scene is null || Interaction.Mode == InteractionMode.None)
        {
            return;
        }
        var preview = Interaction.Preview;
        // What happened to the last confirmation (SENT, REFUSED BY THE RULES, NOT SENT ...), under the preview's own tag.
        var state = Interaction.Notice is { } notice ? $"\n{notice}" : "";
        var good = _highContrast ? Colors.White : new Color(0.35f, 1f, 0.45f);
        var bad = _highContrast ? Colors.Yellow : new Color(1f, 0.35f, 0.3f);
        var colour = preview is { Legal: false } ? bad : good;
        if (Interaction.UsesCursor)
        {
            var c = Interaction.Cursor;
            Segment(new TablePoint(c.X - 0.4, c.Y), new TablePoint(c.X + 0.4, c.Y), 0.1f, Colors.White, 0.08f);
            Segment(new TablePoint(c.X, c.Y - 0.4), new TablePoint(c.X, c.Y + 0.4), 0.1f, Colors.White, 0.08f);
        }
        if (preview is null)
        {
            if (Interaction.UsesCursor)
            {
                Overlay($"cursor {Interaction.Cursor}", Interaction.Cursor, 1.2f, 0.8f);
            }
            return;
        }
        var refusal = preview.Refusal is { } r ? $"\n{r.Text}" : "";
        switch (preview.Kind)
        {
            case PreviewKind.Place:
                var at = preview.Path[0];
                Ring(at.At, preview.Radius, colour);
                Overlay($"{preview.Tag}\n{preview.Model.Value} at {at.At}{refusal}{state}", at.At, 2.8f, 0.95f);
                break;
            case PreviewKind.Move or PreviewKind.Dash or PreviewKind.Retreat:
                for (var i = 1; i < preview.Path.Count; i++)
                {
                    Segment(preview.Path[i - 1].At, preview.Path[i].At, 0.12f, colour);
                    if (i < preview.Path.Count - 1)
                    {
                        Overlay(BoardScene.Inch(preview.Path[i].Inches), preview.Path[i].At, 0.6f, 0.75f);
                    }
                }
                var end = preview.Path[^1];
                Ring(end.At, preview.Radius, colour);
                Overlay($"{preview.Tag}\n{BoardScene.Inch(end.Inches)} used{refusal}{state}", end.At, 2.8f, 0.95f);
                break;
            default:
                foreach (var ray in preview.Rays)
                {
                    Segment(ray.From, ray.To, 0.6f, ray.Clear ? good : new Color(0.55f, 0.55f, 0.55f), 0.08f);
                }
                if (preview.Target is { } targetId && _scene.Find(targetId) is { } target)
                {
                    var spot = new TablePoint(target.X, target.Y);
                    Ring(spot, target.Radius, colour);
                    var sight = preview.Facts.FirstOrDefault(f => f.StartsWith("Line of sight", StringComparison.Ordinal));
                    var distance = preview.Facts.FirstOrDefault(f => f.StartsWith("Distance", StringComparison.Ordinal));
                    Overlay($"{preview.Tag}\n{target.Label}{(distance is null ? "" : "\n" + distance)}{(sight is null ? "" : "\n" + sight)}{refusal}{state}", spot, 3.6f, 0.95f);
                }
                for (var i = 1; i < preview.ChargePath.Count; i++)
                {
                    Segment(preview.ChargePath[i - 1].At, preview.ChargePath[i].At, 0.15f, _highContrast ? Colors.White : new Color(1f, 0.65f, 0.2f), 0.15f);
                }
                if (preview.ChargePath.Count > 1)
                {
                    var stop = preview.ChargePath[^1];
                    var roll = preview.ChargeRollNeeded is { } n ? n == 1 ? "any Charge roll" : $"a Charge roll of {n}+" : "no roll (stops short)";
                    Overlay($"Charge path {BoardScene.Inch(stop.Inches)}: reaches on {roll}", stop.At, 0.9f, 0.75f);
                }
                break;
        }
    }

    // A flat bar between two table points, at a height, drawn through everything.
    private void Segment(TablePoint a, TablePoint b, float height, Color colour, float width = 0.12f)
    {
        var from = new Vector3((float)a.X, height, (float)a.Y);
        var to = new Vector3((float)b.X, height, (float)b.Y);
        var length = from.DistanceTo(to);
        if (length < 0.001f)
        {
            return;
        }
        _overlay.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(width, 0.04f, length) },
            Position = (from + to) / 2,
            Rotation = new Vector3(0, Mathf.Atan2(to.X - from.X, to.Z - from.Z), 0),
            MaterialOverride = Paint(colour, noDepth: true),
        });
    }

    // Where a base would stand.
    private void Ring(TablePoint at, double radius, Color colour)
    {
        _overlay.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = (float)radius - 0.05f, OuterRadius = (float)radius + 0.1f },
            Position = new Vector3((float)at.X, 0.08f, (float)at.Y),
            MaterialOverride = Paint(colour, noDepth: true),
        });
    }

    private void Overlay(string text, TablePoint at, float height, float size)
    {
        _overlayTexts.Add(text);
        _overlay.AddChild(MakeLabel(text, new Vector3((float)at.X, height, (float)at.Y), size));
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
    private void Text(string text, Vector3 at, float size) => _content.AddChild(MakeLabel(text, at, size));

    private Label3D MakeLabel(string text, Vector3 at, float size) =>
        new()
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
        };

    private static StandardMaterial3D Paint(Color colour, bool noDepth = false) => new()
    {
        AlbedoColor = colour,
        NoDepthTest = noDepth,
        ShadingMode = noDepth ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
    };
}
