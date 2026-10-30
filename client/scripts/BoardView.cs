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
// F fits the table, Home centres on the selected model, T switches between the angled and the top-down camera, N and P
// select the next and previous model. The wheel zooms towards the pointer.
// Previews: L place a model, M move, D dash, R retreat, S shoot, C charge, X fight. Then arrows move the cursor (half
// an inch, 2 inches with Shift), Enter fixes a waypoint (or sends a placement or a target), Backspace removes a
// waypoint, N and P choose the target or the model to place, Space sends the command, Esc cancels. A activates the
// selected model.
// Tab and Shift+Tab still move to the next control. Mouse: click selects (or picks the target, or fixes a waypoint),
// moving over the table moves the cursor, right or middle drag pans, the wheel zooms.
//
// Action bar (XCOM-style, mouse first): click one of your models and a small bar of buttons appears next to it with
// only what it can do now (Activate; then Move, Dash, Retreat, Shoot, Charge, Fight as the preview checks allow, and End
// activation). Clicking an action starts its preview: click points (or a target; valid targets are marked) and click the
// last one again, or press the bar's send button. A right-click (without dragging) or Esc cancels. The bar never covers
// a model. It offers what Client.Core's Interaction.Bar says; the server still decides every command. The keys above
// remain as optional shortcuts.
public partial class BoardView : SubViewportContainer
{
    private const float AngledPitch = 55f;
    private const float FitMargin = 0.05f;

    private readonly SubViewport _viewport = new() { OwnWorld3D = true, HandleInputLocally = false, Msaa3D = Viewport.Msaa.Msaa2X };
    private readonly Node3D _content = new();

    // Graphics stage G5: event effects live here, not in _content, so a redraw does not cut them short.
    private readonly Node3D _effects = new() { Name = "Effects" };
    private readonly Camera3D _camera = new() { Fov = 50, Near = 0.1f, Far = 1000 };
    private readonly WorldEnvironment _environment = new();
    private readonly Node3D _overlay = new();
    private readonly HoverThrottle _hover = new(0.08);
    private readonly List<string> _overlayTexts = [];
    private readonly HashSet<ModelId> _crossed = [];
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
    // The action bar: buttons next to the selected or activating model (never focusable, so Tab order is unchanged).
    private readonly PanelContainer _bar = new() { Visible = false, MouseFilter = MouseFilterEnum.Stop };
    private readonly HBoxContainer _barRow = new();
    private string _barShown = "";
    private ActionBar _barNow = ActionBar.Empty;

    // Battlefield UI 2, stage N5 (hot-seat): the letter of the seat shown, first on the action bar ("[A]"), and the banner
    // shown for a moment whenever the seat shown changes ("SEAT B: YOUR TURN"). It never takes a click.
    private readonly PanelContainer _turnBanner = new() { Name = "TurnBanner", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _turnBannerText = new() { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
    private double _turnBannerUntil;
    private ModelId? _lastActive;
    // A right-click that does not drag cancels; one that drags pans.
    private Vector2? _rightFrom;
    private bool _panStarted;

    // The bar was closed with a right-click; it comes back when a model is selected or the activation changes.
    private bool _barDismissed;

    // Short notes above models ("MISSED", "DOWN", "INVALID MOVE"), fading out (UnitNotes); drawn apart from the preview.
    private readonly Node3D _notes = new();
    private readonly List<(UnitNote Note, double Born, Label3D Label)> _shown = [];
    private int _lastSeq = -1;
    public const double NoteSeconds = 2.5;
    private const int NotesPerModel = 2;

    // The texts of the notes shown now (for the UI check).
    public IReadOnlyList<string> NoteTexts => [.. _shown.Select(n => $"{n.Note.Model.Value}: {n.Note.Text}")];

    public event Action? Changed;

    // The events this battlefield caught up on without showing them (after opening, rejoining or a long gap): from
    // (exclusive) to (inclusive). Their rolls, notes and moves are only in the combat log (for the UI check).
    private readonly List<(int From, int To)> _skipped = [];
    public IReadOnlyList<(int From, int To)> Skipped => _skipped;

    // The newest event this battlefield was handed as the match so far, sent again on joining or rejoining.
    public int ReplayedUpTo { get; private set; }

    // The server's dice rolls, drawn in a corner of the battlefield (visible dice rolls, QoL stage F2).
    public DiceTray Dice { get; } = new();

    // How long a box of dice stays (seconds); 0 is "Off" (the rolls stay in the combat log).
    public void SetDiceSeconds(double seconds)
    {
        Dice.Feed.Seconds = seconds;
        if (seconds <= 0)
        {
            Dice.Feed.Clear();
        }
    }

    public BoardScene? Scene => _scene;

    public ModelShape? Selected => _scene?.Find(_selected);

    // The model under the pointer (QoL H1): a thin ring and a larger id on the battlefield, and "POINTING AT" in the
    // panel. Pointing chooses nothing and sends nothing; it is worked out at most every 80 ms.
    public ModelShape? Hovered => _scene?.Find(_hovered);

    private ModelId? _hovered;
    private Vector2? _pointer;
    private double _hoverCheckedAt;
    private readonly MeshInstance3D _hoverRing = new() { Visible = false };
    // QoL I1: rings on the models a clicked combat-log row names, for a moment.
    private readonly List<(ModelId Id, MeshInstance3D Ring)> _pointRings = [];
    private double _pointUntil;
    public const double PointSeconds = 2.5;
    public const double HoverSeconds = 0.08;

    public bool TopDown => _topDown;

    public int Yaw => _yaw;

    public Camera3D Camera => _camera;

    // What the player is choosing on the battlefield, and its preview (Client.Core, tested without Godot).
    public Interaction Interaction { get; } = new();

    // The words of every preview label drawn now (for the UI check and for screen readers' sake, the same words).
    public IReadOnlyList<string> OverlayTexts => _overlayTexts;

    // Enemies marked with a grey cross as targets the chosen action cannot take (for the UI check).
    public IReadOnlyCollection<ModelId> Crossed => _crossed;

    public const string EngagedTag = "ENGAGED";

    public override void _Ready()
    {
        Stretch = true;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0, 440);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(_viewport);
        _viewport.AddChild(_environment);
        _viewport.AddChild(_sun);
        // Visual pass Stage 1: a light from the camera's side that falls on the units only (render layer 2).
        _camera.AddChild(_unitLight);
        _viewport.AddChild(_terrain);
        _viewport.AddChild(_content);
        _viewport.AddChild(_effects);
        _viewport.AddChild(_overlay);
        _viewport.AddChild(_notes);
        _viewport.AddChild(_camera);
        _viewport.AddChild(_hoverRing);
        MouseExited += () => _pointer = null;
        Interaction.NameOf = id => _scene?.Find(id)?.Name ?? id.Value;
        _camera.MakeCurrent();
        _barRow.AddThemeConstantOverride("separation", 6);
        _bar.AddChild(_barRow);
        AddChild(_bar);
        AddChild(Dice);
        _turnBanner.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.07f, 0.05f), BorderColor = new Color(0.95f, 0.8f, 0.35f), BorderWidthBottom = 2, BorderWidthTop = 2,
            BorderWidthLeft = 2, BorderWidthRight = 2, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        _turnBannerText.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.7f));
        _turnBanner.AddChild(_turnBannerText);
        AddChild(_turnBanner);
        _reason.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0.25f, 0.02f, 0.02f, 0.88f), ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 3, ContentMarginBottom = 3 });
        _reason.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.8f));
        AddChild(_reason);
        _autoEnd.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0.05f, 0.05f, 0.08f, 0.9f), BorderColor = new Color(0.95f, 0.8f, 0.3f), BorderWidthBottom = 2, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4 });
        _autoEnd.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.8f));
        AddChild(_autoEnd);
        var menuLook = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.1f, 0.94f), BorderColor = new Color(0.85f, 0.85f, 0.9f) };
        menuLook.SetBorderWidthAll(1);
        menuLook.SetContentMarginAll(6);
        _combatMenu.AddThemeStyleboxOverride("panel", menuLook);
        _combatRows.AddThemeConstantOverride("separation", 3);
        _combatMenu.AddChild(_combatRows);
        AddChild(_combatMenu);
        _confirmBox.AddThemeStyleboxOverride("panel", menuLook);
        _confirmButton.Pressed += Confirm;
        _confirmBox.AddChild(_confirmButton);
        AddChild(_confirmBox);
        Dice.ScreenOfModel = id => BodyAt(id) is { } body && ScreenOf(body.X, body.Z, body.Y) is { } p && new Rect2(Vector2.Zero, Size).HasPoint(p) ? p : null;
        Dice.Avoid = () => ScreenLabelBoxes().Select(b => b.Box).Concat(_bar.Visible ? [new Rect2(_bar.Position, _bar.Size)] : [])
            .Concat(_strip is { Visible: true } strip ? [new Rect2(strip.Position, strip.Size)] : [])
            .Concat(Reserved?.Invoke() ?? []);
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
    public void Show(BoardScene? scene, MatchState? state = null, Seat? seat = null, IReadOnlyList<LogLine>? log = null)
    {
        var first = _scene is null && scene is not null;
        // Where each model was drawn before this update: a model the server moved travels from there (improvement 3).
        var before = _scene?.Models.ToDictionary(m => m.Id, m => new Vector2((float)m.X, (float)m.Y)) ?? [];
        _scene = scene;
        if (_scene?.Find(_selected) is null)
        {
            _selected = null;
        }
        Interaction.Update(state ?? MatchState.None, seat);
        // When one of your models starts its activation, it becomes the selected model, so its action bar shows at once.
        var active = state?.ActiveModel is { } a && state.FindModel(a)?.Seat == seat ? a : (ModelId?)null;
        if (active != _lastActive)
        {
            _lastActive = active;
            if (active is not null && _scene?.Find(active) is not null)
            {
                _selected = active;
            }
            _barDismissed = false;
        }
        if (log is not null)
        {
            ShowNotes(log, before);
        }
        Rebuild();
        ApplyGlides();
        UpdateSeeThrough();
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
        Dice.SetLook(textScale, highContrast);
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
        for (var i = 0; i < 80 && (!TableInView(FitMargin) || !TableBelowStrip()); i++)
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

    // Zooms keeping the table point under the pointer where it is on screen (the wheel zooms towards the pointer).
    public void ZoomAt(float factor, Vector2 local)
    {
        var before = TableAt(local);
        Zoom(factor);
        if (before is { } a && TableAt(local) is { } b)
        {
            _target += new Vector3((float)(a.X - b.X), 0, (float)(a.Y - b.Y));
            if (_scene is not null)
            {
                _target = new Vector3(Math.Clamp(_target.X, 0, (float)_scene.Width), _target.Y, Math.Clamp(_target.Z, 0, (float)_scene.Depth));
            }
            PlaceCamera();
            Changed?.Invoke();
        }
    }

    // Where the camera looks (table inches; for the UI check).
    public Vector3 CameraTarget => _target;

    // Centres the camera on a model, keeping the current zoom unless a distance (inches) is given. Moves nothing else.
    public void CentreOn(ModelId? id, float? distance = null)
    {
        if (_scene?.Find(id) is not { } model)
        {
            return;
        }
        _autoFit = false;
        _target = new Vector3((float)model.X, (float)model.Standing, (float)model.Y);
        if (distance is { } d)
        {
            if (_topDown)
            {
                _orthoSize = Math.Clamp(d, 4, 200);
            }
            else
            {
                _distance = Math.Clamp(d, 4, 200);
            }
        }
        PlaceCamera();
        Changed?.Invoke();
    }

    // ---- QoL H2: double-click, next unit, reset ----

    public const float CloseDistance = 8f;
    public const float CloseOrtho = 10f;
    private string? _focusKey;

    // What the last double-click centred on ("model:a1" or "objective:obj-2"), for the UI check.
    public string? FocusKey => _focusKey;

    public float Zoomed => _topDown ? _orthoSize : _distance;

    // The second press of a double-click. With no action chosen: centres on the model under it (which the first press
    // selected), else on the Objective under it, keeping the zoom; a double-click on what is already centred zooms in one
    // step, down to a close view. While an action is being chosen it does nothing: the first press was the click.
    public void DoubleClickAt(Vector2 local)
    {
        if (Interaction.Mode != InteractionMode.None || _scene is null)
        {
            return;
        }
        if (ModelAt(local) is { } model)
        {
            Focus($"model:{model.Id.Value}", new Vector3((float)model.X, (float)model.Standing, (float)model.Y));
        }
        else if (ObjectiveUnder(local) is { } objective)
        {
            Focus($"objective:{objective.Id}", new Vector3((float)objective.Area.CentreX, 0, (float)objective.Area.CentreY));
        }
    }

    public TerrainShape? ObjectiveUnder(Vector2 local)
    {
        if (_scene is null)
        {
            return null;
        }
        foreach (var piece in _scene.Terrain.Where(t => t.Objective).OrderByDescending(t => t.Height))
        {
            foreach (var level in new[] { piece.Height, 0 })
            {
                if (TableAt(local, level) is { } p && piece.Area.Contains(p.X, p.Y))
                {
                    return piece;
                }
            }
        }
        return null;
    }

    private void Focus(string key, Vector3 at)
    {
        var centred = _focusKey == key && new Vector2(_target.X - at.X, _target.Z - at.Z).Length() < 0.3f;
        _autoFit = false;
        _target = at;
        if (centred)
        {
            if (_topDown)
            {
                _orthoSize = Math.Max(CloseOrtho, _orthoSize * 0.6f);
            }
            else
            {
                _distance = Math.Max(CloseDistance, _distance * 0.6f);
            }
        }
        _focusKey = key;
        PlaceCamera();
        Changed?.Invoke();
    }

    // J (Shift+J): the next (previous) of your units the rules let you choose now, chosen as a click would (nothing is
    // sent). The camera moves only when that unit is off screen, and then keeps the zoom.
    public void NextEligible(bool forward)
    {
        if (_scene?.NextEligible(_selected, Interaction.CanChoose, forward) is not { } next)
        {
            return;
        }
        ChooseUnit(next.Id);
        if (ScreenOf(next.X, next.Y, next.Standing) is not { } at || !new Rect2(Vector2.Zero, Size).HasPoint(at))
        {
            CentreOn(next.Id);
        }
        Sync();
    }

    // Shift+F or "Reset view": angled, facing north, the whole table.
    public void ResetView()
    {
        _yaw = 0;
        if (_topDown)
        {
            _topDown = false;
            Rebuild();
            DrawPreview();
        }
        Fit();
    }

    // Looks at a table point (inches) from the given distance (for the UI check).
    public void LookAtTable(Vector2 at, float distance)
    {
        _autoFit = false;
        _target = new Vector3(at.X, 0, at.Y);
        if (_topDown)
        {
            _orthoSize = distance;
        }
        else
        {
            _distance = distance;
        }
        PlaceCamera();
        Changed?.Invoke();
    }

    // Puts the angled camera at a point (Godot units: x, height, table y), keeping its facing (for the UI check).
    public void PlaceCameraAt(Vector3 at)
    {
        _autoFit = false;
        _distance = 4;
        var yaw = Mathf.DegToRad(_yaw);
        var pitch = Mathf.DegToRad(AngledPitch);
        _target = at - new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * _distance;
        PlaceCamera();
        Changed?.Invoke();
    }

    // Turns the camera to a table point (inches) at the current zoom: a move that ends off screen is followed there.
    private void CentreOnPoint(Vector2 at)
    {
        _target = new Vector3(at.X, _target.Y, at.Y);
        PlaceCamera();
        FollowedMoves++;
    }

    // How many moves the camera has followed off screen (for the UI check).
    public int FollowedMoves { get; private set; }

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

    // QoL H4: the Objective selected (by a click on it where no model is, or with O), shown highlighted and in the panel;
    // selecting a unit clears it.
    public TerrainShape? SelectedObjective => _selectedObjective is { } o ? _scene?.Objective(o) : null;

    private string? _selectedObjective;

    public void SelectObjective(string? id)
    {
        _selected = null;
        _selectedObjective = id is not null && _scene?.Objective(id) is not null ? id : null;
        Rebuild();
        UpdateBar();
        Changed?.Invoke();
    }

    // Selects the Objective and centres the camera on it, keeping the zoom (O, and a Scenario card row, proposal J3b).
    public void CentreOnObjective(string id)
    {
        if (_scene?.Objective(id) is not { } objective)
        {
            // Trench-maps stage S6: a Trench Warfare row names a trench section; centre on it, selecting nothing.
            if (_scene?.Terrain.FirstOrDefault(t => t.Id == id) is { } piece)
            {
                _autoFit = false;
                _target = new Vector3((float)piece.Area.CentreX, 0, (float)piece.Area.CentreY);
                _focusKey = $"terrain:{piece.Id}";
                PlaceCamera();
            }
            return;
        }
        SelectObjective(objective.Id);
        _autoFit = false;
        _target = new Vector3((float)objective.Area.CentreX, 0, (float)objective.Area.CentreY);
        _focusKey = $"objective:{objective.Id}";
        PlaceCamera();
    }

    // O (Shift+O): the next (previous) Objective, selected and centred, keeping the zoom.
    public void NextObjective(bool forward)
    {
        if (_scene is null)
        {
            return;
        }
        var order = _scene.Terrain.Where(t => t.Objective).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        if (order.Count == 0)
        {
            return;
        }
        var at = order.FindIndex(t => t.Id == _selectedObjective);
        var next = at < 0 ? (forward ? order[0] : order[^1]) : order[((at + (forward ? 1 : -1)) % order.Count + order.Count) % order.Count];
        CentreOnObjective(next.Id);
        Changed?.Invoke();
    }

    public void Select(ModelId? id)
    {
        if (id is not null)
        {
            _barDismissed = false;
        }
        _selectedObjective = null;
        _selected = _scene?.Find(id)?.Id;
        Rebuild();
        UpdateBar();
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

    // Towers proposal, stage T4: the table point a click or the pointer means while placing or choosing a path: a tower's
    // platform top when the pointer is over one (picked at the tower's height), else the ground.
    public (double X, double Y)? CursorAt(Vector2 local)
    {
        foreach (var tower in _scene?.Terrain.Where(t => t.Tower).OrderByDescending(t => t.Height) ?? Enumerable.Empty<TerrainShape>())
        {
            if (TableAt(local, tower.Height) is { } top && tower.Area.Contains(top.X, top.Y))
            {
                return top;
            }
        }
        // Trench-maps stage S3: over a trench, the point on its floor the pointer looks at, when the floor is seen there.
        if (_scene is { } scene && TableAt(local) is { } ground && TrenchPits.At(scene, ground.X, ground.Y) is { } trench
            && TableAt(local, -trench.Depth) is { } floor && trench.Holds(floor.X, floor.Y))
        {
            return floor;
        }
        return TableAt(local);
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

    // Stage K3: the far edge of the table (at wall height) is below the HUD strip.
    private bool TableBelowStrip()
    {
        // A strip over most of a small battlefield is not avoided (the table would shrink to nothing).
        if (_scene is null || TopInset <= 0 || TopInset > Size.Y * 0.35f)
        {
            return true;
        }
        foreach (var (x, y) in new[] { (0.0, 0.0), (_scene.Width, 0.0), (0.0, _scene.Depth), (_scene.Width, _scene.Depth) })
        {
            if (ScreenOf(x, y, DisplayHeight.Wall) is { } p && p.Y < TopInset + 4)
            {
                return false;
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
        if (started)
        {
            DismissLocalNotes();
            // Choosing an action brings back a bar closed with a right-click (it shows again once the action ends).
            _barDismissed = false;
        }
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

    // Stage K3: the HUD strip across the top of the battlefield (Main builds it). Its height is TopInset: the action bar
    // and the camera's fit keep below it.
    private Control? _strip;

    public float TopInset { get; private set; }

    // Stage N5: the seat letter on the action bar (hot-seat only; null hides it).
    public string? SeatTag { get; set; }

    public string BarSeatText => _bar.Visible && _barRow.GetChildCount() > 0 && _barRow.GetChild(0) is Label l && l.Name == "BarSeat" ? l.Text : "";

    // Stage N5: the turn banner's text while it shows, else "".
    public string TurnBannerText => _turnBanner.Visible ? _turnBannerText.Text : "";

    public Rect2? TurnBannerRect => _turnBanner.Visible ? new Rect2(_turnBanner.Position, _turnBanner.Size) : null;

    // Each banner shown: its text, when it showed and when its time ran out (null while showing or when the next one
    // replaced it), in milliseconds since the engine started. For the UI check; the newest 50.
    public List<(string Text, ulong Shown, ulong? Gone)> TurnBannerHistory { get; } = [];

    public void ShowTurnBanner(string text, double seconds)
    {
        if (TurnBannerHistory.Count >= 50)
        {
            TurnBannerHistory.RemoveAt(0);
        }
        TurnBannerHistory.Add((text, Time.GetTicksMsec(), null));
        _turnBannerText.Text = text;
        _turnBannerText.AddThemeFontSizeOverride("font_size", (int)(26 * _textScale));
        _turnBanner.Visible = true;
        _turnBanner.ResetSize();
        _turnBannerUntil = Time.GetTicksMsec() / 1000.0 + seconds;
        PlaceTurnBanner();
    }

    private void PlaceTurnBanner()
    {
        var size = _turnBanner.GetCombinedMinimumSize();
        _turnBanner.Size = size;
        _turnBanner.Position = new Vector2(Mathf.Max(0, (Size.X - size.X) / 2), Mathf.Max(TopInset + 8, Size.Y * 0.18f));
        MoveChild(_turnBanner, GetChildCount() - 1);
    }

    public Control? TopStrip => _strip;

    // Stage N3: the screen's boxes on the battlefield (unit card, log drawer, ...) that its bars and menus keep clear of.
    public Func<IEnumerable<Rect2>>? Reserved { get; set; }

    // Stage N3: Esc with nothing to cancel on the battlefield (the screen then closes the log drawer or the Menu).
    public event Action? EscapeIdle;

    public void SetTopStrip(Control strip)
    {
        _strip = strip;
        AddChild(strip);
    }

    private void PlaceStrip()
    {
        if (_strip is null || !_strip.Visible)
        {
            TopInset = 0;
            return;
        }
        var want = new Vector2(Size.X, _strip.GetCombinedMinimumSize().Y);
        if (_strip.Position != Vector2.Zero || _strip.Size != want)
        {
            _strip.Position = Vector2.Zero;
            _strip.Size = want;
        }
        var inset = _strip.Size.Y;
        if (Math.Abs(inset - TopInset) > 0.5f)
        {
            TopInset = inset;
            if (_autoFit)
            {
                CallDeferred(MethodName.Fit);
            }
        }
    }

    // Battlefield UI 2, stage N2: the note that the activation ends by itself (Esc keeps it going), centred under the HUD.
    private readonly Label _autoEnd = new() { Name = "AutoEndNote", Visible = false, MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };

    // Off for the UI check's driver (it plays its own fixed sequence), except in its own check of this.
    public static bool AutoEndActivations { get; set; } = true;

    public string? AutoEndText => _autoEnd.Visible ? _autoEnd.Text : null;

    private bool _autoEndTicking;

    private async void TickAutoEnd()
    {
        Interaction.AutoEnd = AutoEndActivations;
        if (!_autoEndTicking)
        {
            _autoEndTicking = true;
            try
            {
                if (await Interaction.TickAutoEndAsync(Time.GetTicksMsec() / 1000.0) is not null)
                {
                    Sync();
                }
            }
            finally
            {
                _autoEndTicking = false;
            }
        }
        _autoEnd.Visible = Interaction.AutoEndNote is not null;
        if (_autoEnd.Visible)
        {
            _autoEnd.Text = Interaction.AutoEndNote;
            _autoEnd.AddThemeFontSizeOverride("font_size", (int)(17 * _textScale));
            _autoEnd.ResetSize();
            _autoEnd.Position = new Vector2(Math.Max(4, (Size.X - _autoEnd.Size.X) / 2), TopInset + 8);
        }
    }

    // Stage K4: the rules' reason a placement would be refused, beside the ghost (for the UI check: its words and place).
    private readonly Label _reason = new()
    {
        Name = "PlaceReason",
        Visible = false,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private TablePoint? _reasonAt;
    private string _reasonSource = "";
    private string _reasonWords = "";
    private float _reasonWidth;

    public string? PlaceReason => _reason.Visible ? _reason.Text.Replace("\n", " ") : null;

    public Rect2? PlaceReasonRect => _reason.Visible ? _reason.GetGlobalRect() : null;

    private void PlaceReasonBox()
    {
        if (_reasonAt is not { } at || ScreenOf(at.X, at.Y, 0) is not { } p)
        {
            _reason.Visible = false;
            return;
        }
        var width = Math.Min(320 * _textScale, Math.Max(120, Size.X - 16));
        // The lines are broken here, by the font's own measure, rather than by the label's autowrap: an autowrapped
        // label can keep the height of an earlier, narrower layout (seen as a box 1,482 px tall).
        if (_reasonWords != _reasonSource || Math.Abs(_reasonWidth - width) > 0.5f)
        {
            _reasonWords = _reasonSource;
            _reasonWidth = width;
            _reason.Text = Wrap(_reasonSource, _reason.GetThemeFont("font"), _reason.GetThemeFontSize("font_size"), width - 14);
        }
        _reason.ResetSize();
        var size = _reason.Size;
        var x = Math.Clamp(p.X + 24, 4, Math.Max(4, Size.X - width - 4));
        var y = Math.Clamp(p.Y - size.Y / 2, TopInset + 4, Math.Max(TopInset + 4, Size.Y - size.Y - 4));
        _reason.Position = new Vector2(x, y);
        _reason.Visible = true;
    }

    // Words into lines no wider than the width, as the font measures them (a word longer than the width keeps its line).
    private static string Wrap(string text, Font font, int fontSize, float width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tried = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && font.GetStringSize(tried, HorizontalAlignment.Left, -1, fontSize).X > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = tried;
            }
        }
        if (line.Length > 0)
        {
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    public override void _Process(double delta)
    {
        PlaceStrip();
        if (_turnBanner.Visible)
        {
            if (Time.GetTicksMsec() / 1000.0 >= _turnBannerUntil)
            {
                _turnBanner.Visible = false;
                if (TurnBannerHistory.Count > 0)
                {
                    TurnBannerHistory[^1] = TurnBannerHistory[^1] with { Gone = Time.GetTicksMsec() };
                }
            }
            else
            {
                PlaceTurnBanner();
            }
        }
        if (Interaction.UsesCursor && _hover.Take(Time.GetTicksMsec() / 1000.0) is { } point)
        {
            Interaction.SetCursor(point.X, point.Y);
        }
        UpdateHover();
        UpdatePointOut();
        Sync();
        UpdateBar();
        UpdateCombat();
        KeepControlsOnTop();
        TickAutoEnd();
        PlaceReasonBox();
        ApplyGlides();
        UpdateMeleeLinks();
        UpdateSeeThrough();
        SeparateLabels();
        if (Interaction.ServerNotes != _serverNotesShown)
        {
            _serverNotesShown = Interaction.ServerNotes;
            if (Interaction.ServerNote is { } refused)
            {
                AddNote(refused);
            }
        }
        FadeNotes();
    }

    private int _serverNotesShown;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion pointer)
        {
            _pointer = pointer.Position;
        }
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left, DoubleClick: true } twice:
                // QoL H2: the second press of a double-click is never a click (so it never sends, decision D2): with
                // no action chosen it centres on the unit or objective under it, or zooms in on what is centred.
                GrabFocus();
                DoubleClickAt(twice.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                GrabFocus();
                Click(click.Position, click.ShiftPressed);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
                // A right-click without dragging cancels the action being chosen; dragging pans.
                if (right.Pressed)
                {
                    _rightFrom = right.Position;
                    _dragging = true;
                    _panStarted = false;
                }
                else
                {
                    if (_rightFrom is { } from && from.DistanceTo(right.Position) < 6)
                    {
                        RightClick();
                    }
                    _rightFrom = null;
                    _dragging = false;
                }
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } drag:
                _dragging = drag.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } wheel:
                ZoomAt(0.9f, wheel.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } wheel:
                ZoomAt(1.1f, wheel.Position);
                AcceptEvent();
                break;
            // A right-button drag pans only once the pointer has moved 6 pixels, so a slightly shaky right-click cancels
            // without moving the camera.
            case InputEventMouseMotion shaky when _dragging && _rightFrom is { } pressedAt && !_panStarted
                && pressedAt.DistanceTo(shaky.Position) < 6:
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                _panStarted = true;
                var inchesPerPixel = (_topDown ? _orthoSize : _distance * 0.8f) / Math.Max(1, Size.Y);
                Pan(-motion.Relative.X * inchesPerPixel, motion.Relative.Y * inchesPerPixel);
                AcceptEvent();
                break;
            case InputEventMouseMotion hover when Interaction.UsesCursor && CursorAt(hover.Position) is { } p:
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

    // A left click: picks the target in Shoot, Charge and Fight (stage N1: a second click on it sends nothing; the
    // Confirm button beside it, or Space, does). When
    // placing or moving, a click on a spot is the destination: sent at once when the preview says the server would
    // accept it, otherwise not sent and a note above the model says why. Shift+click adds a waypoint to a path.
    // Otherwise the click selects the model under it.
    // What the last left click on the battlefield did (for the UI check's reports).
    public string LastClick { get; private set; } = "";

    public void Click(Vector2 local, bool shift = false)
    {
        LastClick = $"{Interaction.Mode}, chosen {Interaction.Chosen?.Value}, cursor {(Interaction.UsesCursor ? CursorAt(local)?.ToString() ?? "off the table" : "not used")}, pending {Interaction.Pending is not null}, preview {(Interaction.Preview is { } pv ? pv.Legal.ToString() : "none")}";
        // Mines stage M3: while the defender lays the Mine Markers, a click places, makes a dud or takes one back up.
        if (Interaction.Mines is { } laying)
        {
            if (CursorAt(local) is { } spot)
            {
                laying.Click(Math.Round(spot.X * 10) / 10, Math.Round(spot.Y * 10) / 10);
                LastClick = $"Mines at ({spot.X:0.0}, {spot.Y:0.0}): {laying.Note}";
            }
            MinesChanged();
            return;
        }
        // While a unit is only chosen (not activated on the server yet), a click on another of your units that may
        // activate switches to it, whatever action was being chosen.
        if (Interaction.Chosen is { } chosen && ModelAt(local) is { Yours: true } other && other.Id != chosen && Interaction.CanChoose(other.Id))
        {
            ChooseUnit(other.Id);
            Sync();
            return;
        }
        if (Interaction.Mode == InteractionMode.Combat)
        {
            // Battlefield UI 3, stage U1: the attack is chosen first, from the Combat menu beside the bar; a click on the
            // battlefield chooses nothing until then.
        }
        else if (Interaction.IsTarget)
        {
            if (ModelAt(local) is { } model)
            {
                Interaction.SetTarget(model.Id);
            }
        }
        else if (Interaction.UsesCursor)
        {
            if (CursorAt(local) is { } p)
            {
                var result = Interaction.ClickAt(Math.Round(p.X * 10) / 10, Math.Round(p.Y * 10) / 10, shift);
                LastClick = $"{Interaction.Mode} at ({p.X:0.0}, {p.Y:0.0}): {result}";
                switch (result)
                {
                    case ClickResult.Send:
                        Confirm();
                        return;
                    case ClickResult.Refused when Interaction.RefusedNote() is { } note:
                        AddNote(note);
                        break;
                }
            }
        }
        else if (ModelAt(local) is null && ObjectiveUnder(local) is { } objective)
        {
            SelectObjective(objective.Id);
        }
        else
        {
            ChooseUnit(ModelAt(local)?.Id);
        }
        Sync();
    }

    // A mouse click on a model: selects it, and when it is one of your units that may activate now, chooses it to
    // activate (nothing is sent until its first action). A click on another of your units while your activation has
    // started on the server says why it cannot switch.
    public void ChooseUnit(ModelId? id)
    {
        Select(id);
        if (id is not { } wanted || _scene?.Find(wanted) is not { Yours: true })
        {
            return;
        }
        if (Interaction.CanChoose(wanted))
        {
            Interaction.Choose(wanted);
            UpdateBar();
        }
        else if (!Interaction.Choose(wanted) && Interaction.Notice?.Contains("cannot switch", StringComparison.Ordinal) == true)
        {
            AddNote(new UnitNote(wanted, "CAN'T SWITCH", NoteTone.Failure));
        }
        Changed?.Invoke();
    }

    // A right-click without dragging: cancels the action being chosen; with none, closes the bar and the selection.
    public void RightClick()
    {
        if (Interaction.Mode != InteractionMode.None || Interaction.Notice is not null)
        {
            CancelPreview();
            return;
        }
        if (Interaction.Pending is null)
        {
            _barDismissed = true;
            Interaction.Choose(null);
            Select(null);
        }
    }

    // The panel's Undo point (or Backspace): removes the last waypoint.
    public void UndoPoint()
    {
        Interaction.RemovePoint();
        Sync();
        UpdateBar();
    }

    // Esc, a right-click or the panel's Cancel: stops choosing; nothing is sent.
    // Stage N2: Esc while the activation is about to end by itself keeps it going; false when no such note was shown.
    public bool KeepActivation()
    {
        if (!Interaction.KeepActivation())
        {
            return false;
        }
        _autoEnd.Visible = false;
        Sync();
        return true;
    }

    public void CancelPreview(bool all = false)
    {
        Interaction.Cancel(all);
        Sync();
        UpdateBar();
    }

    private static InteractionMode? ModeKey(Key key) => key switch
    {
        Key.L => InteractionMode.Place,
        Key.M => InteractionMode.Move,
        Key.D => InteractionMode.Dash,
        Key.R => InteractionMode.Retreat,
        Key.S => InteractionMode.Shoot,
        // C opens Combat (stage U1: the menu of attacks first, then the target); in Combat, 2 is Charge.
        Key.C => InteractionMode.Combat,
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
        // Stage N2: Esc first keeps an activation that was about to end by itself.
        if (key.Keycode == Key.Escape && KeepActivation())
        {
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
            case Key.F when key.ShiftPressed: ResetView(); return true;
            case Key.F: Fit(); return true;
            case Key.J when Interaction.Mode == InteractionMode.None: NextEligible(!key.ShiftPressed); return true;
            case Key.O when Interaction.Mode == InteractionMode.None: NextObjective(!key.ShiftPressed); return true;
            case Key.Home: CentreOn(_selected); return true;
            case Key.T: ToggleTopDown(); return true;
            case Key.N: SelectNext(!key.ShiftPressed); return true;
            case Key.P: SelectNext(false); return true;
            default: return false;
        }
    }

    private bool HandlePreviewKey(InputEventKey key)
    {
        var step = (float)(key.ShiftPressed ? Interaction.BigStep : Interaction.Step);
        // In Combat, 1, 2 and 3 choose Shoot, Charge and Fight (stage U1: before any target).
        if (Interaction.Mode == InteractionMode.Combat && key.Keycode switch
            {
                Key.Key1 or Key.Kp1 => InteractionMode.Shoot,
                Key.Key2 or Key.Kp2 => InteractionMode.Charge,
                Key.Key3 or Key.Kp3 => InteractionMode.Fight,
                _ => InteractionMode.None,
            } is var attack && attack != InteractionMode.None)
        {
            PickCombat(attack, byKey: true);
            return true;
        }
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
            case Key.K:
                // Towers proposal, stage T6: switch between ladders and climbing a tower's side (a Risky roll).
                return Interaction.ToggleClimbSides();
            case Key.Escape:
                if (Interaction.Mode == InteractionMode.None && Interaction.Pending is null)
                {
                    EscapeIdle?.Invoke();
                }
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

    // An entry of the Combat menu (a click on it, or 1, 2 and 3). Stage U1: it chooses the attack, never a target and never
    // a send; the player then clicks an enemy the rules accept and Confirm beside it.
    public void PickCombat(InteractionMode mode, bool byKey = false)
    {
        Interaction.ChooseCombat(mode, byKey);
        Sync();
        UpdateCombat();
    }

    // The Combat menu (stage U1: the attacks of the acting model, beside the bar). Stage N1: the Confirm button beside the target of a Shoot,
    // Charge or Fight the preview accepts (it was for Charge only).
    private readonly PanelContainer _combatMenu = new() { Name = "CombatMenu", Visible = false, MouseFilter = MouseFilterEnum.Stop };
    private readonly VBoxContainer _combatRows = new();
    private readonly PanelContainer _confirmBox = new() { Name = "ConfirmAttackBox", Visible = false, MouseFilter = MouseFilterEnum.Stop };
    private readonly Button _confirmButton = new() { Name = "ConfirmAttack", Text = "Confirm charge", FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
    private string _combatShown = "";

    // For the UI check: the menu's entries as shown (label, enabled, the reason under a greyed one), and where to click.
    public IReadOnlyList<(string Label, bool Enabled, string? Why)> CombatMenuShown { get; private set; } = [];

    public Vector2? CombatButtonAt(InteractionMode mode) =>
        _combatMenu.Visible && _combatRows.GetChildren().OfType<Button>().FirstOrDefault(b => !b.IsQueuedForDeletion() && b.HasMeta("mode") && (int)b.GetMeta("mode") == (int)mode) is { } button
            ? button.GetGlobalRect().GetCenter()
            : null;

    public Rect2? CombatMenuRect => _combatMenu.Visible ? _combatMenu.GetGlobalRect() : null;

    // Stage U1: the menu's title, which names the friendly model that attacks (null when the menu is hidden).
    public string? CombatMenuTitle => _combatMenu.Visible && _combatRows.GetChildCount() > 0 && _combatRows.GetChild(0) is Label l && l.Name == "CombatTitle" ? l.Text : null;

    public Vector2? ConfirmChargeAt => Interaction.Mode == InteractionMode.Charge ? ConfirmAttackAt : null;

    // Stage N1: where the Confirm button beside the target is, and what it says (for the UI check); null when hidden.
    public Vector2? ConfirmAttackAt => _confirmBox.Visible ? _confirmButton.GetGlobalRect().GetCenter() : null;

    public string? ConfirmAttackText => _confirmBox.Visible ? _confirmButton.Text : null;

    private void UpdateCombat()
    {
        // Stage U1: the Combat menu lists the acting model's attacks before any target is chosen; it sits beside the bar.
        var entries = Interaction.CombatEntries();
        var actor = Interaction.Actor is { } a ? _scene?.Find(a) : null;
        var target = Interaction.Target is { } t ? _scene?.Find(t) : null;
        var key = $"{actor?.Id.Value}|{_textScale}|{string.Join("|", entries.Select(e => $"{e.Mode}:{e.Enabled}:{e.Why}:{e.Targets}"))}";
        if (key != _combatShown)
        {
            _combatShown = key;
            foreach (var child in _combatRows.GetChildren())
            {
                _combatRows.RemoveChild(child);
                child.QueueFree();
            }
            var shown = new List<(string, bool, string?)>();
            if (actor is not null && entries.Count > 0)
            {
                // Which friendly model attacks.
                var title = new Label { Name = "CombatTitle", Text = $"{UnitPanel.Heading(actor)}: choose an attack", MouseFilter = MouseFilterEnum.Ignore };
                title.AddThemeFontSizeOverride("font_size", (int)(15 * _textScale));
                title.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.7f));
                _combatRows.AddChild(title);
            }
            foreach (var entry in entries)
            {
                var button = new Button
                {
                    Text = $"{entry.Label} · {entry.Key}",
                    Disabled = !entry.Enabled,
                    TooltipText = entry.Enabled ? $"{entry.Targets} {(entry.Targets == 1 ? "enemy" : "enemies")} the rules accept as the target: choose, then click one" : entry.Why ?? "",
                    FocusMode = FocusModeEnum.None,
                    Alignment = HorizontalAlignment.Left,
                    MouseDefaultCursorShape = entry.Enabled ? CursorShape.PointingHand : CursorShape.Arrow,
                };
                button.AddThemeFontSizeOverride("font_size", (int)(16 * _textScale));
                button.SetMeta("mode", (int)entry.Mode);
                var mode = entry.Mode;
                button.Pressed += () => PickCombat(mode);
                _combatRows.AddChild(button);
                if (!entry.Enabled && entry.Why is { } why)
                {
                    // Greyed out, with the rules' reason under it in words (not by colour alone).
                    var reason = new Label { Text = why, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240 * _textScale, 0), MouseFilter = MouseFilterEnum.Ignore };
                    reason.AddThemeFontSizeOverride("font_size", (int)(13 * _textScale));
                    reason.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
                    _combatRows.AddChild(reason);
                }
                shown.Add((entry.Label, entry.Enabled, entry.Enabled ? null : entry.Why));
            }
            CombatMenuShown = shown;
            _combatMenu.ResetSize();
        }
        _combatMenu.Visible = entries.Count > 0 && actor is not null;
        if (_combatMenu.Visible)
        {
            PlaceCombatMenu(actor!);
        }
        _confirmBox.Visible = Interaction.IsAttack(Interaction.Mode) && Interaction.Pending is null && Interaction.Preview is { Legal: true } && target is not null;
        if (_confirmBox.Visible)
        {
            _confirmButton.Text = Interaction.Mode switch
            {
                InteractionMode.Shoot => "Confirm shoot",
                InteractionMode.Fight => "Confirm fight",
                _ => "Confirm charge",
            };
            _confirmBox.ResetSize();
            _confirmButton.AddThemeFontSizeOverride("font_size", (int)(17 * _textScale));
            Beside(_confirmBox, target!);
        }
    }

    // Stage U1: the Combat menu above the action bar (else below it), clear of the screen's boxes; beside the acting model
    // when the bar is hidden.
    private void PlaceCombatMenu(ModelShape actor)
    {
        if (!_bar.Visible)
        {
            Beside(_combatMenu, actor);
            return;
        }
        var size = _combatMenu.GetCombinedMinimumSize();
        var bar = new Rect2(_bar.Position, _bar.Size);
        var reserved = Reserved?.Invoke().ToList() ?? [];
        Vector2 Clamp(Vector2 at) => new(Math.Clamp(at.X, 4, Math.Max(4, Size.X - size.X - 4)), Math.Clamp(at.Y, TopInset + 4, Math.Max(TopInset + 4, Size.Y - size.Y - 4)));
        var above = Clamp(new Vector2(bar.Position.X, bar.Position.Y - size.Y - 6));
        var below = Clamp(new Vector2(bar.Position.X, bar.End.Y + 6));
        bool Clear(Vector2 p) => !new Rect2(p, size).Intersects(bar) && !reserved.Any(r => r.Intersects(new Rect2(p, size)));
        var position = Clear(above) || !Clear(below) ? above : below;
        if (_combatMenu.Position != position || _combatMenu.Size != size)
        {
            _combatMenu.Position = position;
            _combatMenu.Size = size;
        }
    }

    // Puts a small box right of a model (else left), below the HUD strip and inside the battlefield.
    private void Beside(Control box, ModelShape model)
    {
        var size = box.GetCombinedMinimumSize();
        if (ScreenOf(model.X, model.Y, model.Standing) is not { } at)
        {
            box.Visible = false;
            return;
        }
        // Stage N4: on the side of the target that keeps clear of the screen's boxes (unit card, log drawer), if either does.
        var reserved = Reserved?.Invoke().ToList() ?? [];
        Vector2 At(float x) => new(Math.Clamp(x, 4, Math.Max(4, Size.X - size.X - 4)), Math.Clamp(at.Y - size.Y / 2, TopInset + 4, Math.Max(TopInset + 4, Size.Y - size.Y - 4)));
        var right = At(at.X + 36);
        var left = At(at.X - 36 - size.X);
        bool Clear(Vector2 p) => !reserved.Any(r => r.Intersects(new Rect2(p, size)));
        var preferred = at.X + 36 + size.X <= Size.X - 4 ? right : left;
        var other = preferred == right ? left : right;
        var position = Clear(preferred) || !Clear(other) ? preferred : other;
        if (box.Position != position || box.Size != size)
        {
            box.Position = position;
            box.Size = size;
        }
    }

    // Battlefield UI 2, stage N4: the action bar, the combat menu and Confirm beside the target are never under a box the
    // screen puts on the battlefield (unit card, log drawer, Menu, decision card): when one overlaps them, they move
    // above it in the tree, so they are drawn and clicked first.
    private void KeepControlsOnTop()
    {
        foreach (var box in new Control[] { _bar, _combatMenu, _confirmBox })
        {
            if (!box.Visible || box.GetParent() != this)
            {
                continue;
            }
            var rect = new Rect2(box.Position, box.Size);
            for (var i = box.GetIndex() + 1; i < GetChildCount(); i++)
            {
                if (GetChild(i) is Control { Visible: true } over && over != _bar && over != _combatMenu && over != _confirmBox
                    && over.MouseFilter != MouseFilterEnum.Ignore && new Rect2(over.Position, over.Size).Intersects(rect))
                {
                    MoveChild(box, -1);
                    break;
                }
            }
        }
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

    // The labels of the action bar's buttons now (for the UI check), empty when it is hidden.
    // Stage K3 (for the UI check): where the action bar is, when shown.
    public Rect2? BarRect => _bar.Visible ? new Rect2(_bar.Position, _bar.Size) : null;

    // Battlefield UI 2, stage N4: the battlefield's own controls shown now (the action bar, the combat menu, Confirm beside
    // the target), in its coordinates, which the screen's Menu keeps clear of.
    public IEnumerable<Rect2> ControlRects() =>
        new Control[] { _bar, _combatMenu, _confirmBox }.Where(c => c.Visible).Select(c => new Rect2(c.Position, c.Size));

    // Stage U4: the action's numbers shown at the bar's end, or "".
    public string BarStats => _bar.Visible && _barRow.GetNodeOrNull<Label>("ActionStats") is { } l && !l.IsQueuedForDeletion() ? l.Text : "";

    public IReadOnlyList<string> BarLabels => _bar.Visible ? _barNow.Items.Select(i => i.Label).ToList() : [];

    // The texts on the bar's buttons now, keys included (for the UI check).
    public IReadOnlyList<string> BarTexts => _bar.Visible ? [.. _barRow.GetChildren().OfType<Button>().Where(b => !b.IsQueuedForDeletion()).Select(b => b.Text)] : [];

    // Where a button of the action bar is on the screen (window coordinates), for a real mouse click; null when not shown.
    public Vector2? BarButtonAt(string label) =>
        _bar.Visible && _barNow.Items.FirstOrDefault(i => i.Label == label) is { } item
            && _barRow.GetChildren().OfType<Button>().FirstOrDefault(b => b.Text == item.ButtonText && !b.IsQueuedForDeletion()) is { } button
            ? button.GetGlobalRect().GetCenter()
            : null;

    // Presses a button of the action bar, as a click on it does.
    public void PressBar(string label)
    {
        if (_bar.Visible && _barNow.Items.FirstOrDefault(i => i.Label == label && i.Enabled) is { } item)
        {
            Run(item);
        }
    }

    private async void Run(BarItem item)
    {
        switch (item.Kind)
        {
            case BarKind.Start:
                GrabFocus();
                StartPreview(item.Mode);
                break;
            case BarKind.Confirm:
                Confirm();
                break;
            case BarKind.Pick when item.Model is { } model:
                GrabFocus();
                Interaction.PickToPlace(model);
                Sync();
                break;
            case BarKind.Undo:
                Interaction.RemovePoint();
                Sync();
                break;
            case BarKind.Cancel:
                CancelPreview();
                break;
            case BarKind.Decide when item.Command is { } answer:
                await Interaction.SendCommandAsync(answer, item.Label);
                Sync();
                break;
            case BarKind.Activate or BarKind.EndActivation when item.Command is { } command:
                var name = _scene?.Find(_selected)?.Name ?? "the model";
                await Interaction.SendCommandAsync(command, item.Kind == BarKind.Activate ? $"Activate {name}" : $"End {name}'s activation");
                Sync();
                break;
        }
        UpdateBar();
    }

    private bool _commandsOnBar;

    // Proposal P1: Confirm, Undo and Cancel (and "waiting") are on the bar while the unit card is closed.
    public bool CommandButtonsOnBar
    {
        get => _commandsOnBar;
        set
        {
            if (_commandsOnBar != value)
            {
                _commandsOnBar = value;
                UpdateBar();
            }
        }
    }

    // Rebuilds the bar's buttons when what it offers changed, and keeps it next to its model without covering any model.
    private void UpdateBar()
    {
        var bar = _scene is null ? ActionBar.Empty : Interaction.Bar(_selected);
        // Proposal G: the bar next to the unit carries actions only; Confirm, Undo, Cancel and "waiting" are in the panel.
        // Proposal P1: while the unit card is closed (only its tab shows), they are on the bar instead.
        if (!_commandsOnBar)
        {
            bar = bar with { Items = [.. bar.Items.Where(i => i.Kind is not (BarKind.Confirm or BarKind.Undo or BarKind.Cancel or BarKind.Waiting))] };
        }
        if (_barDismissed && Interaction.Mode == InteractionMode.None && Interaction.Pending is null && !bar.Items.Any(i => i.Kind == BarKind.Decide))
        {
            bar = ActionBar.Empty;
        }
        var stats = bar.Items.Count > 0 ? Interaction.Stats : null;
        var shown = $"{SeatTag}|{stats}|{bar.Anchor?.Value}|{string.Join("|", bar.Items.Select(i => $"{i.Kind}:{i.Label}:{i.Enabled}:{i.Tip}"))}|{_textScale}";
        if (shown != _barShown)
        {
            _barShown = shown;
            _barNow = bar;
            foreach (var child in _barRow.GetChildren())
            {
                _barRow.RemoveChild(child);
                child.QueueFree();
            }
            if (SeatTag is { } seat && bar.Items.Count > 0)
            {
                var tag = new Label { Name = "BarSeat", Text = $"[{seat}]", VerticalAlignment = VerticalAlignment.Center, TooltipText = $"Seat {seat} is shown and plays." };
                tag.AddThemeFontSizeOverride("font_size", (int)(18 * _textScale));
                tag.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.7f));
                _barRow.AddChild(tag);
            }
            foreach (var item in bar.Items)
            {
                var button = new Button
                {
                    // QoL H5: the key that does the same is on the button ("Move · M").
                    Text = item.ButtonText,
                    TooltipText = item.Tip,
                    Disabled = !item.Enabled,
                    FocusMode = FocusModeEnum.None,
                    MouseDefaultCursorShape = CursorShape.PointingHand,
                };
                button.AddThemeFontSizeOverride("font_size", (int)(16 * _textScale));
                var captured = item;
                button.Pressed += () => Run(captured);
                _barRow.AddChild(button);
            }
            // Battlefield UI 3, stage U4: the numbers for the action being chosen, at the bar's end.
            if (stats is not null)
            {
                var line = new Label { Name = "ActionStats", Text = stats, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
                line.AddThemeFontSizeOverride("font_size", (int)(15 * _textScale));
                line.AddThemeColorOverride("font_color", _highContrast ? Colors.Yellow : new Color(0.75f, 0.9f, 1f));
                _barRow.AddChild(line);
            }
            _bar.Visible = bar.Items.Count > 0;
            _bar.ResetSize();
        }
        if (_bar.Visible)
        {
            PlaceBar();
        }
    }

    // Next to the anchor model (above it, else below, left or right), or in a corner while placing; never over a model's
    // base, so clicks on models always reach the battlefield.
    private void PlaceBar()
    {
        var size = _bar.GetCombinedMinimumSize();
        var models = _scene?.Models.Select(m => ScreenOf(m.X, m.Y, m.Standing)).OfType<Vector2>().ToList() ?? [];
        var candidates = new List<Vector2>();
        if (_scene?.Find(_barNow.Anchor) is { } anchor && ScreenOf(anchor.X, anchor.Y, anchor.Standing + 1.6) is { } top
            && ScreenOf(anchor.X, anchor.Y, anchor.Standing) is { } foot)
        {
            // Above the model, and above any notes floating over it.
            var notes = _shown.Count(n => n.Note.Model == anchor.Id);
            var above = new Vector2(top.X - size.X / 2, top.Y - size.Y - 28 - notes * 22 * _textScale);
            var below = new Vector2(foot.X - size.X / 2, foot.Y + 34);
            candidates.AddRange([above, below]);
            candidates.Add(new Vector2(foot.X + 40, foot.Y - size.Y / 2));
            candidates.Add(new Vector2(foot.X - 40 - size.X, foot.Y - size.Y / 2));
        }
        // Stage N3: and never over the unit card, the log drawer or the other boxes the screen keeps on the battlefield.
        var reserved = Reserved?.Invoke().ToList() ?? [];
        candidates.Add(new Vector2((Size.X - size.X) / 2, Size.Y - size.Y - 8));
        candidates.Add(new Vector2(8, TopInset + 8));
        candidates.Add(new Vector2(Size.X - size.X - 8, TopInset + 8));
        candidates.Add(new Vector2(8, Size.Y - size.Y - 8));
        candidates.Add(new Vector2(Size.X - size.X - 8, Size.Y - size.Y - 8));
        Vector2 Clamp(Vector2 at) => new(Math.Clamp(at.X, 4, Math.Max(4, Size.X - size.X - 4)), Math.Clamp(at.Y, TopInset + 4, Math.Max(TopInset + 4, Size.Y - size.Y - 4)));
        bool Clear(Vector2 at) => !models.Any(m => new Rect2(at, size).Grow(22).HasPoint(m)) && !reserved.Any(r => r.Intersects(new Rect2(at, size)));
        var chosen = candidates.Select(Clamp).FirstOrDefault(Clear, Clamp(candidates[0]));
        _bar.Position = chosen;
        _bar.Size = size;
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
        _crossed.Clear();
        _sightIcons.Clear();
        GuideDrawnFor = null;
        GuideOutline = null;
        GuideCaption = null;
        _guideEye = null;
        _guideCaption = null;
        AreaPoints = 0;
        GhostAt = null;
        _reasonAt = null;
        TargetLineDrawn = null;
        TargetLinesDrawn = 0;
        LongRangeTagDrawn = false;
        Climbs = 0;
        LayingDrawn = 0;
        if (_scene is not null && Interaction.Mines is { } laying)
        {
            foreach (var (id, at, dud) in laying.Shapes)
            {
                DrawMine(_overlay, at.X, at.Y, Rules.Core.Geometry.Mines.MarkerRadius.Thousandths / 1000.0, dud ? MineKnown.Dud : MineKnown.Live,
                    new MineShape(id, at.X, at.Y, 0.5, dud ? MineKnown.Dud : MineKnown.Live).Mark);
                LayingDrawn++;
            }
            var c = Interaction.Cursor;
            var level = (float)LevelAt(c.X, c.Y) + 0.1f;
            Segment(new TablePoint(c.X - 0.4, c.Y), new TablePoint(c.X + 0.4, c.Y), level, Colors.White, 0.08f);
            Segment(new TablePoint(c.X, c.Y - 0.4), new TablePoint(c.X, c.Y + 0.4), level, Colors.White, 0.08f);
            return;
        }
        if (_scene is null || Interaction.Mode == InteractionMode.None)
        {
            return;
        }
        var preview = Interaction.Preview;
        // What happened to the last confirmation (SENT, REFUSED BY THE RULES, NOT SENT ...), under the preview's own tag.
        var good = _highContrast ? Colors.White : new Color(0.35f, 1f, 0.45f);
        var bad = _highContrast ? Colors.Yellow : new Color(1f, 0.35f, 0.3f);
        var colour = preview is { Legal: false } ? bad : good;
        if (preview is null)
        {
            Hints(good);
        }
        if (Interaction.UsesCursor)
        {
            var c = Interaction.Cursor;
            var level = (float)LevelAt(c.X, c.Y) + 0.1f;
            Segment(new TablePoint(c.X - 0.4, c.Y), new TablePoint(c.X + 0.4, c.Y), level, Colors.White, 0.08f);
            Segment(new TablePoint(c.X, c.Y - 0.4), new TablePoint(c.X, c.Y + 0.4), level, Colors.White, 0.08f);
        }
        if (preview is null)
        {
            DrawSightIcons();
            return;
        }
        Hints(good);
        switch (preview.Kind)
        {
            case PreviewKind.Place:
                var at = preview.Path[0];
                // Proposal G: what the placement would do is said in the panel beside the battlefield, not over it.
                Ring(at.At, preview.Radius, colour);
                // Stage K4: a ghost of the model where a click puts it, red where the rules refuse, with their reason
                // beside it (a small box on the screen, not a label on the table).
                Ghost(at.At, preview.Model, preview.Radius, colour, tint: preview.Legal ? null : bad);
                if (preview.Refusal is { } why)
                {
                    _reasonAt = at.At;
                    _reasonSource = why.Text;
                }
                break;
            case PreviewKind.Move or PreviewKind.Dash or PreviewKind.Retreat:
                // A dashed path and a see-through "ghost" base at its end: a preview never looks like the real model,
                // which stays where it is until the server's events move it.
                // Towers proposal, stage T4: each point at its own height, and a ladder's climb drawn as a rising dashed
                // line at its foot (the rules put the climb there, U71).
                for (var i = 1; i < preview.Path.Count; i++)
                {
                    var (a, b) = (preview.Path[i - 1], preview.Path[i]);
                    if (a.Height < b.Height)
                    {
                        Climbing(a.At, a.Height, b.Height, colour);
                        Dashed(a.At, b.At, (float)b.Height + 0.12f, colour);
                    }
                    else if (a.Height > b.Height)
                    {
                        Dashed(a.At, b.At, (float)a.Height + 0.12f, colour);
                        Climbing(b.At, b.Height, a.Height, colour);
                    }
                    else
                    {
                        Dashed(a.At, b.At, (float)a.Height + 0.12f, colour);
                    }
                    if (i < preview.Path.Count - 1)
                    {
                        Overlay(BoardScene.Inch(b.Inches), b.At, (float)b.Height + 0.6f, 0.75f);
                    }
                }
                var end = preview.Path[^1];
                Ring(end.At, preview.Radius, colour, (float)end.Height);
                // Line-of-sight stage L6: the enemies the model would see from the end of the path, as the target icons.
                foreach (var sight in Interaction.DestinationSights())
                {
                    if (_scene.Find(sight.Model) is { } enemy)
                    {
                        SightIcon(enemy, sight);
                    }
                }
                Ghost(end.At, preview.Model, preview.Radius, colour, (float)end.Height);
                // Proposal G: the distances (used, left, straight line) are in the panel beside the battlefield.
                break;
            default:
                // Stage K5: one bold line from the attacker's base edge to the target's, toned by the preview: clear,
                // in cover or refused (the sight rays are in the panel's words).
                if (TargetLine.Of(Interaction.PreviewState, preview) is { } line)
                {
                    DrawTargetLine(line);
                }
                if (preview.Kind == PreviewKind.Shoot && preview.Target is { } aimed && _scene.Find(aimed) is { } aimedShape)
                {
                    DrawShotGuide(preview, aimedShape);
                }
                if (preview.Target is { } targetId && _scene.Find(targetId) is { } target)
                {
                    var spot = new TablePoint(target.X, target.Y);
                    // Proposal G: distance, sight, cover and any refusal are in the panel beside the battlefield.
                    Ring(spot, target.Radius, colour, (float)target.Standing);
                }
                for (var i = 1; i < preview.ChargePath.Count; i++)
                {
                    Segment(preview.ChargePath[i - 1].At, preview.ChargePath[i].At, 0.15f, _highContrast ? Colors.White : new Color(1f, 0.65f, 0.2f), 0.15f);
                }
                if (preview.ChargePath.Count > 1)
                {
                    var stop = preview.ChargePath[^1];
                    // Proposal G: just the roll the charge needs at the end of its path; the panel says it in words.
                    Overlay(ChargeMark(preview.ChargeRollNeeded), stop.At, 0.9f, 0.75f);
                }
                break;
        }
        // Drawn after the preview, so the chosen target's shot guide (GuideDrawnFor) is known when its eye is drawn.
        DrawSightIcons();
    }

    // Line-of-sight stage L4: an eye above every enemy (open, half closed or struck through) and a shield when it would
    // be in cover, from the rules' own sight and cover.
    private void DrawSightIcons()
    {
        foreach (var sight in Interaction.TargetSights())
        {
            if (_scene?.Find(sight.Model) is { } model)
            {
                SightIcon(model, sight);
            }
        }
    }

    // What can be chosen, marked on the battlefield: in Shoot, Charge and Fight every enemy as a valid target (a ring and
    // "valid target") or not (a thin grey ring and why not, in short); on a path, how far the model can move.
    private void Hints(Color good)
    {
        if (_scene is null)
        {
            return;
        }
        foreach (var (id, refusal) in Interaction.TargetVerdicts())
        {
            if (id == Interaction.Target || _scene.Find(id) is not { } model)
            {
                continue;
            }
            var spot = new TablePoint(model.X, model.Y);
            Ring(spot, model.Radius + 0.25, refusal is null ? good : new Color(0.55f, 0.55f, 0.55f), (float)model.Standing);
            // Proposal J2b: in Fight, a target the rules accept is one this model is engaged with; it says so under its base.
            if (refusal is null && Interaction.Mode == InteractionMode.Fight)
            {
                Overlay(EngagedTag, new TablePoint(model.X, model.Y + model.Radius + 0.45), (float)model.Standing + 0.15f, 0.45f);
            }
            // Proposal G: a target that cannot be chosen gets a grey cross as well as its grey ring (not colour alone);
            // why not is in the panel.
            if (refusal is not null)
            {
                var r = (float)model.Radius * 0.7f;
                var grey = _highContrast ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
                Segment(new TablePoint(spot.X - r, spot.Y - r), new TablePoint(spot.X + r, spot.Y + r), (float)model.Standing + 1.5f, grey, 0.08f);
                Segment(new TablePoint(spot.X - r, spot.Y + r), new TablePoint(spot.X + r, spot.Y - r), (float)model.Standing + 1.5f, grey, 0.08f);
                _crossed.Add(id);
            }
        }
        // Line-of-sight stage L5: pointing at an enemy in Shoot, before one is chosen, draws its shot guide.
        if (Interaction.Mode == InteractionMode.Shoot && Interaction.Target is null && Hovered is { Yours: false } pointed
            && Interaction.PreviewOf(pointed.Id) is { Kind: PreviewKind.Shoot } pointedShot)
        {
            DrawShotGuide(pointedShot, pointed);
        }
        if (Interaction.IsPath)
        {
            Area(Interaction.ReachArea(), _highContrast ? new Color(1, 1, 1, 0.35f) : new Color(0.45f, 0.75f, 1f, 0.28f));
        }
    }

    // Stage L5 (for the UI check): the target whose shot guide is drawn now, and the piece outlined as its cover.
    public ModelId? GuideDrawnFor { get; private set; }

    public string? GuideOutline { get; private set; }

    // The shot guide: the middle body ray from the shooter's eye, solid while clear and broken from where it is blocked,
    // and the piece that gives cover outlined in gold. Visual pass Stage 2 (2026-10-27): the 3 x 3 sight points and the
    // GUIDE tag above the target are gone; the target's own eye and shield are drawn larger with a short caption instead
    // (SightIcon). The nine points stay in words in the unit panel.
    private void DrawShotGuide(Preview preview, ModelShape target)
    {
        GuideDrawnFor = target.Id;
        var ink = _highContrast ? Colors.White : new Color(0.85f, 0.80f, 0.69f);
        foreach (var piece in ShotGuide.Line(preview.Rays, _scene!))
        {
            if (piece.Clear)
            {
                Bar(piece.From, (float)piece.FromHeight, piece.To, (float)piece.ToHeight, ink, 0.07f, 0.04f);
                continue;
            }
            var length = Math.Sqrt(Math.Pow(piece.To.X - piece.From.X, 2) + Math.Pow(piece.To.Y - piece.From.Y, 2));
            var dashes = Math.Max(2, (int)(length / 0.3));
            for (var i = 0; i < dashes; i += 2)
            {
                double At(double k, double a, double b) => a + (b - a) * k;
                var (k0, k1) = ((double)i / dashes, (double)(i + 1) / dashes);
                Bar(new TablePoint(At(k0, piece.From.X, piece.To.X), At(k0, piece.From.Y, piece.To.Y)), (float)At(k0, piece.FromHeight, piece.ToHeight),
                    new TablePoint(At(k1, piece.From.X, piece.To.X), At(k1, piece.From.Y, piece.To.Y)), (float)At(k1, piece.FromHeight, piece.ToHeight),
                    ink, 0.07f, 0.04f);
            }
        }
        if (preview.CoverId is { } coverId && _scene!.Terrain.FirstOrDefault(t => t.Id == coverId) is { } cover)
        {
            GuideOutline = coverId;
            var gold = _highContrast ? Colors.Cyan : new Color(0.79f, 0.64f, 0.15f);
            var h = (float)cover.Height + 0.03f;
            foreach (var r in cover.Parts)
            {
                Segment(new TablePoint(r.X0, r.Y0), new TablePoint(r.X1, r.Y0), h, gold, 0.08f);
                Segment(new TablePoint(r.X1, r.Y0), new TablePoint(r.X1, r.Y1), h, gold, 0.08f);
                Segment(new TablePoint(r.X1, r.Y1), new TablePoint(r.X0, r.Y1), h, gold, 0.08f);
                Segment(new TablePoint(r.X0, r.Y1), new TablePoint(r.X0, r.Y0), h, gold, 0.08f);
            }
        }
    }

    // Visual pass Stage 2 (for the UI check): the caption drawn under the guided target's eye and shield, or null.
    public string? GuideCaption { get; private set; }

    private Sprite3D? _guideEye;
    private Label3D? _guideCaption;

    // For the UI check: where the guided target's eye and its caption are drawn on screen (control coordinates).
    public Rect2? GuideEyeBox => _guideEye is { } e && IsInstanceValid(e) && e.IsInsideTree() && e.Texture is { } t && !_camera.IsPositionBehind(e.GlobalPosition)
        ? SpriteBox(e, t) : null;

    public Rect2? GuideCaptionBox => _guideCaption is { } c && IsInstanceValid(c) && c.IsInsideTree() ? LabelBox(c) : null;

    private Rect2 SpriteBox(Sprite3D sprite, Texture2D texture)
    {
        var ratio = Size.Y / Math.Max(1, _viewport.Size.Y);
        var scale = SpriteScale(sprite);
        var size = new Vector2(texture.GetWidth(), texture.GetHeight()) * scale;
        var at = _camera.UnprojectPosition(sprite.GlobalPosition) * ratio + new Vector2(sprite.Offset.X, -sprite.Offset.Y) * scale;
        return new Rect2(at - size / 2, size);
    }

    // Screen pixels (control coordinates) per pixel of a fixed-size sprite.
    private float SpriteScale(Sprite3D sprite)
    {
        var projection = _camera.GetCameraProjection();
        return sprite.PixelSize * _viewport.Size.Y / 2 * (_camera.Projection == Camera3D.ProjectionType.Orthogonal ? 1 : projection.Y.Y)
            * (Size.Y / Math.Max(1, _viewport.Size.Y));
    }

    // How much larger the guided target's eye and shield are drawn than the others'.
    public const float GuideIconScale = 1.5f;

    // Visual pass Stage 3 (for the UI check): the filled drops drawn under each model's label now (models with none
    // have no row).
    public IReadOnlyDictionary<ModelId, int> BloodDropsDrawn => _bloodDrops;

    private readonly Dictionary<ModelId, int> _bloodDrops = [];

    // Each drawn row with its offset under the label and its scale, so SeparateLabels can move it with the label.
    private readonly Dictionary<ModelId, (Sprite3D Sprite, Vector2 Offset, float Scale)> _bloodRows = [];

    // The model's Blood Markers as a row of small dark-red drops just under its label, from the server's count
    // (ModelShape.BloodMarkers is the state's ModelState.BloodMarkers); nothing at 0. Drawn through walls like the label,
    // with the battlefield's models (rebuilt whenever the state changes), and moved with the label when labels are spread.
    private void BloodRow(ModelShape model)
    {
        if (BloodDrops.Row(model.BloodMarkers) is not { } row)
        {
            return;
        }
        _bloodDrops[model.Id] = BloodDrops.Filled(row);
        var scale = BloodScale * _textScale;
        var pixel = (_topDown ? 0.0035f : 0.0012f) * scale;
        var at = new Vector3((float)model.X, LabelHeight(model), (float)model.Y);
        var blood = new Color(0.80f, 0.10f, 0.08f);
        var offset = new Vector2(0, -(19 * _textScale + 6) / scale);
        var sprite = Icon(IconTexture($"Blood{Math.Min(model.BloodMarkers, Rules.Core.State.BloodMarkers.Max)}", row, gold: false, blood), at, pixel,
            offset, dim: model.Activated && !model.Active);
        sprite.Name = $"Blood{model.Id.Value}";
        _content.AddChild(sprite);
        _bloodRows[model.Id] = (sprite, offset, scale);
    }

    // How far below its label's box a model's drop row reaches on screen, and how wide the row is (control coordinates);
    // zero with no row.
    private Vector2 BloodRowSize(ModelId model, Label3D label)
    {
        if (!_bloodRows.ContainsKey(model))
        {
            return Vector2.Zero;
        }
        var screen = LabelScale(label) * (Size.Y / Math.Max(1, _viewport.Size.Y));
        return new Vector2(BloodDrops.Width * BloodScale * _textScale, BloodDrops.Height * BloodScale * _textScale + 6) * screen;
    }

    // How many screen pixels each pixel of the drop row is drawn (times the text scale).
    private const float BloodScale = 2.5f;

    // Stage L4 (for the UI check): the sight icons drawn now, by model.
    public IReadOnlyDictionary<ModelId, TargetSight> SightIconsDrawn => _sightIcons;

    private readonly Dictionary<ModelId, TargetSight> _sightIcons = [];
    private readonly Dictionary<(string, bool, bool), ImageTexture> _iconTextures = [];

    private void SightIcon(ModelShape model, TargetSight sight)
    {
        _sightIcons[model.Id] = sight;
        // A fixed-size icon as tall as a line of label text, above the model's own label; the guided target's (Stage 2)
        // half as large again, with its caption between the icons and the label.
        var guided = model.Id == GuideDrawnFor;
        var size = guided ? GuideIconScale : 1f;
        var scale = 32 * _textScale / SightIcons.Size;
        var pixel = (_topDown ? 0.0035f : 0.0012f) * scale * size;
        var at = new Vector3((float)model.X, LabelHeight(model), (float)model.Y);
        // The guided eye sits clear above its caption (bottom at AboveLabel + 10, about 20 high at 0.45 size; the eye is
        // 48 high at 1.5 times): its middle 57 above AboveLabel, all times the text scale.
        var lift = (AboveLabel + (guided ? 57 * _textScale : 0)) / (scale * size);
        var dim = sight.Mark == SightMark.Blocked;
        var eye = Icon(IconTexture(sight.Mark.ToString(), SightIcons.For(sight.Mark), gold: false), at, pixel,
            new Vector2(sight.Cover ? -9 : 0, lift), dim);
        _overlay.AddChild(eye);
        if (guided)
        {
            _guideEye = eye;
        }
        if (sight.Cover)
        {
            _overlay.AddChild(Icon(IconTexture("Shield", SightIcons.Shield, gold: true), at, pixel, new Vector2(9, lift), dim));
        }
        if (guided)
        {
            GuideCaption = TargetSights.Caption(sight);
            var caption = MakeLabel(GuideCaption, at, 0.45f);
            Raise(caption, AboveLabel + 10 * _textScale);
            _guideCaption = caption;
            _overlay.AddChild(caption);
            _overlayTexts.Add(caption.Text);
        }
    }

    private static Sprite3D Icon(Texture2D texture, Vector3 at, float pixel, Vector2 offset, bool dim) =>
        new()
        {
            Texture = texture,
            Position = at,
            Offset = offset,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            PixelSize = pixel,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            NoDepthTest = true,
            RenderPriority = 11,
            Modulate = dim ? new Color(1, 1, 1, 0.55f) : Colors.White,
            Shaded = false,
        };

    // Bone or gold paint on an iron outline; black and white in high contrast.
    private ImageTexture IconTexture(string name, string[] rows, bool gold, Color? paint = null)
    {
        if (_iconTextures.TryGetValue((name, gold, _highContrast), out var cached))
        {
            return cached;
        }
        var light = _highContrast ? Colors.White : paint ?? (gold ? new Color(0.79f, 0.64f, 0.15f) : new Color(0.85f, 0.80f, 0.69f));
        var dark = _highContrast ? Colors.Black : new Color(0.12f, 0.11f, 0.10f);
        var image = Image.CreateEmpty(rows[0].Length, rows.Length, false, Image.Format.Rgba8);
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                image.SetPixel(x, y, rows[y][x] switch
                {
                    '#' => light,
                    'X' => dark,
                    // A faint pale outline (an empty blood drop), seen on dark ground as on light.
                    'o' => _highContrast ? new Color(1, 1, 1, 0.55f) : new Color(0.85f, 0.80f, 0.69f, 0.55f),
                    _ => new Color(0, 0, 0, 0),
                });
            }
        }
        var texture = ImageTexture.CreateFromImage(image);
        _iconTextures[(name, gold, _highContrast)] = texture;
        return texture;
    }

    // Stage K5 (for the UI check): the target line drawn now and its colour, and how many were drawn.
    public (TargetLine Line, Color Colour)? TargetLineDrawn { get; private set; }

    public int TargetLinesDrawn { get; private set; }

    public Color ToneColour(TargetTone tone) => tone switch
    {
        TargetTone.Clear => _highContrast ? Colors.White : new Color(0.3f, 1f, 0.4f),
        TargetTone.Cover => _highContrast ? Colors.Cyan : new Color(1f, 0.72f, 0.15f),
        _ => _highContrast ? Colors.Yellow : new Color(1f, 0.3f, 0.25f),
    };

    // A dark outline under a bright bar, from the attacker's eye (1.1" above its base) to the target's body point (0.7"),
    // the heights the rules' sight uses (U77); drawn under the labels.
    private void DrawTargetLine(TargetLine line)
    {
        var colour = ToneColour(line.Tone);
        var from = (_scene?.Find(line.Attacker) is { } a ? (float)a.Standing : 0f) + (float)TableRect.Inches(SightHeights.Eye);
        var to = (_scene?.Find(line.Target) is { } t ? (float)t.Standing : 0f) + (float)TableRect.Inches(SightHeights.Points[1]);
        Bar(line.From, from, line.To, to, new Color(0, 0, 0, 0.75f), 0.42f, 0.06f);
        LongRangeTagDrawn = false;
        if (!line.LongRange)
        {
            Bar(line.From, from + 0.03f, line.To, to + 0.03f, colour, 0.26f, 0.1f);
        }
        else
        {
            // Long Range (p. 43): the bright bar broken into dashes over the solid dark outline, and a small tag at the
            // middle of the line, so long range is told by shape and words as well as colour.
            var length = Math.Sqrt(Math.Pow(line.To.X - line.From.X, 2) + Math.Pow(line.To.Y - line.From.Y, 2));
            var dashes = Math.Max(3, (int)Math.Round(length / LongRangeDash) | 1);
            for (var i = 0; i < dashes; i += 2)
            {
                var (k0, k1) = ((float)i / dashes, (float)(i + 1) / dashes);
                Bar(Along(line.From, line.To, k0), Mathf.Lerp(from, to, k0) + 0.03f, Along(line.From, line.To, k1), Mathf.Lerp(from, to, k1) + 0.03f, colour, 0.26f, 0.1f);
            }
            Overlay(TargetLine.LongRangeTag, Along(line.From, line.To, 0.5), Mathf.Lerp(from, to, 0.5f) + 0.35f, 0.45f);
            LongRangeTagDrawn = true;
        }
        TargetLineDrawn = (line, colour);
        TargetLinesDrawn++;
    }

    // The length of one dash (and one gap) of a long-range line, in inches.
    private const double LongRangeDash = 0.5;

    private static TablePoint Along(TablePoint a, TablePoint b, double k) => new(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);

    // For the UI check: whether the LONG RANGE tag was drawn with the target line now.
    public bool LongRangeTagDrawn { get; private set; }

    // A flat bar between two table points at their own heights, drawn through terrain but under the labels.
    private void Bar(TablePoint a, float ha, TablePoint b, float hb, Color colour, float width, float thickness)
    {
        var from = new Vector3((float)a.X, ha, (float)a.Y);
        var to = new Vector3((float)b.X, hb, (float)b.Y);
        var length = from.DistanceTo(to);
        if (length < 0.001f)
        {
            return;
        }
        var paint = Paint(colour, noDepth: true);
        paint.RenderPriority = -1;
        var bar = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(width, thickness, length) }, MaterialOverride = paint };
        _overlay.AddChild(bar);
        bar.LookAtFromPosition((from + to) / 2, to, Mathf.Abs((to - from).Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up);
    }

    // The roll a charge needs, drawn at the end of its path: "4+", "ANY" or "SHORT" (the panel says it in words).
    private static string ChargeMark(int? needed) => needed is { } n ? n == 1 ? "ANY" : $"{n}+" : "SHORT";

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

    // Where the acting model could end its move (Interaction.ReachArea): a translucent tile per grid point.
    private void Area(IReadOnlyList<TablePoint> points, Color colour)
    {
        if (points.Count == 0)
        {
            return;
        }
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new PlaneMesh { Size = new Vector2(0.46f, 0.46f) }, InstanceCount = points.Count };
        for (var i = 0; i < points.Count; i++)
        {
            // A point on a tower's platform is drawn on the platform (towers proposal, stage T4).
            multi.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3((float)points[i].X, (float)LevelAt(points[i].X, points[i].Y) + 0.03f, (float)points[i].Y)));
        }
        _overlay.AddChild(new MultiMeshInstance3D
        {
            Multimesh = multi,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = colour,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        });
        AreaPoints = points.Count;
    }

    // How many points the movement area drawn now has (for the UI check); 0 when none is drawn.
    public int AreaPoints { get; private set; }

    // ---- notes above models ----

    // Each new event as it arrives (not those of a first look or a catch-up), for the sounds (QoL I3).
    public event Action<LogLine>? LineShown;

    // New events' notes. The first time (or after a catch-up of many events) only remembers where the log is, so a
    // rejoin does not replay old notes.
    private void ShowNotes(IReadOnlyList<LogLine> log, IReadOnlyDictionary<ModelId, Vector2> before)
    {
        var newest = log.Count == 0 ? 0 : log[^1].Seq;
        if (_lastSeq < 0 || newest < _lastSeq || newest - _lastSeq > 20)
        {
            if (newest > Math.Max(_lastSeq, 0))
            {
                _skipped.Add((Math.Max(_lastSeq, 0), newest));
            }
            _lastSeq = newest;
            _glides.Clear();
            return;
        }
        foreach (var line in log.Where(l => l.Seq > _lastSeq))
        {
            if (line.Replayed)
            {
                ReplayedUpTo = Math.Max(ReplayedUpTo, line.Seq);
            }
            Dice.Feed.Add(line, DiceTray.Now);
            LineShown?.Invoke(line);
            foreach (var note in line.Notes ?? [])
            {
                AddNote(note);
            }
            // Graphics stage G5: an event's effects, only for an event this window sees as it happens (not caught up).
            if (line.Effects.Count > 0)
            {
                EffectLines.Add((line.Seq, line.Effects.Count, line.Replayed));
                if (!line.Replayed)
                {
                    foreach (var cue in line.Effects)
                    {
                        PlayEffect(cue);
                        EffectsPlayed.Add((line.Seq, cue));
                    }
                }
            }
            if (line.Motion is { } motion && before.TryGetValue(motion.Model, out var from))
            {
                StartGlide(motion, from);
            }
        }
        _lastSeq = newest;
    }

    // ---- event effects (graphics stage G5) ----

    // Every event with effects this window took in (its number, how many effects, caught up or not), and the effects it
    // played (for the UI check: all of the live ones, none of the caught-up ones).
    public List<(int Seq, int Count, bool Replayed)> EffectLines { get; } = [];

    public List<(int Seq, EffectCue Cue)> EffectsPlayed { get; } = [];

    public int EffectsShowing => _effects.GetChildCount();

    // For the dev pictures only: show an effect without an event (never used in play).
    public void ShowEffectForPicture(EffectCue cue) => PlayEffect(cue);

    // PS1-style: a few flat, unshaded squares that face the camera, grow and fade, then go. No lights, no particles.
    private void PlayEffect(EffectCue cue)
    {
        if (_scene?.Find(cue.At) is not { } model)
        {
            return;
        }
        var at = new Vector3((float)model.X, (float)model.Standing, (float)model.Y);
        switch (cue.Kind)
        {
            case EffectKind.MuzzleFlash:
                Puff(at + new Vector3(0, 0.85f, 0), Vector3.Zero, 0.5f, 0.4f, new Color(1f, 0.9f, 0.45f), 0.35f);
                break;
            case EffectKind.HitPuff:
                for (var i = 0; i < 4; i++)
                {
                    var away = new Vector3(Mathf.Cos(i * 1.7f), 0.6f, Mathf.Sin(i * 1.7f)) * 0.35f;
                    Puff(at + new Vector3(0, 0.7f, 0), away, 0.3f, 0.4f, new Color(0.72f, 0.2f, 0.15f), 0.7f);
                }
                break;
            default:
                for (var i = 0; i < 5; i++)
                {
                    var side = new Vector3(Mathf.Cos(i * 1.3f), 0, Mathf.Sin(i * 1.3f)) * ((float)model.Radius + 0.35f);
                    Puff(at + side + new Vector3(0, 0.15f, 0), new Vector3(0, 0.6f, 0), 0.4f, 0.5f, new Color(0.58f, 0.49f, 0.37f), 0.9f);
                }
                break;
        }
    }

    private void Puff(Vector3 from, Vector3 drift, float size, float grow, Color colour, float seconds)
    {
        var paint = new StandardMaterial3D
        {
            AlbedoColor = colour,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        var puff = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(size, size) }, Position = from, MaterialOverride = paint };
        _effects.AddChild(puff);
        var tween = puff.CreateTween().SetParallel();
        tween.TweenProperty(puff, "position", from + drift, seconds);
        tween.TweenProperty(puff, "scale", Vector3.One * (1 + grow / size), seconds);
        tween.TweenProperty(paint, "albedo_color:a", 0f, seconds);
        tween.Chain().TweenCallback(Callable.From(puff.QueueFree));
    }

    // ---- see-through walls (XCOM-style UX) ----

    // In the angled view, a wall or other tall piece between the camera and a model (as drawn, also while it travels) is
    // drawn faint, in its own colour, so no model is ever hidden behind terrain; it is solid again as soon as it hides
    // nothing. Top-down nothing can hide. Display only: sight and cover come from the rules.
    public const float FadedAlpha = 0.3f;

    private readonly Dictionary<string, StandardMaterial3D> _terrainPaint = [];
    private readonly HashSet<string> _faded = [];

    // The terrain drawn see-through now, and the terrain that hides a model from the current camera (for the UI check).
    public IReadOnlySet<string> FadedTerrain => _faded;

    public IReadOnlySet<string> HidingTerrain() => _scene is not { } scene || _topDown
        ? new HashSet<string>()
        : SeeThrough.Hiding(new SeeThrough.Point3(_camera.GlobalPosition.X, _camera.GlobalPosition.Z, _camera.GlobalPosition.Y), scene.Terrain,
            _modelNodes.Values.Where(n => n.Count > 0 && IsInstanceValid(n[0].Node)).Select(n => n[0].Node.Position)
                .Select(at => new SeeThrough.Point3(at.X, at.Z, at.Y)));

    private readonly Dictionary<string, MeshInstance3D> _terrainOutlines = [];
    private (Transform3D, bool)? _seeThroughKey;

    // How often the see-through set was worked out (for the UI check: not every frame).
    public int SeeThroughUpdates { get; private set; }

    // The tall pieces whose outline is drawn now: exactly the faded ones (for the UI check).
    public IReadOnlyList<string> OutlinedTerrain => [.. _terrainOutlines.Where(o => IsInstanceValid(o.Value) && o.Value.Visible).Select(o => o.Key).Order(StringComparer.Ordinal)];

    // The twelve edges of a tall piece in a darker shade of its own colour, shown only while it is see-through, so its
    // shape stays readable (QoL H2). Display only.
    private MeshInstance3D Outline(TerrainShape piece, Color colour)
    {
        var mesh = new ImmediateMesh();
        var h = (float)piece.Height;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        // Each rectangle of the piece outlined (stage S8: joined pieces).
        foreach (var a in piece.Parts)
        {
            var corners = new[] { new Vector2((float)a.X0, (float)a.Y0), new Vector2((float)a.X1, (float)a.Y0), new Vector2((float)a.X1, (float)a.Y1), new Vector2((float)a.X0, (float)a.Y1) };
            for (var i = 0; i < 4; i++)
            {
                var p = corners[i];
                var q = corners[(i + 1) % 4];
                foreach (var y in new[] { 0.02f, h })
                {
                    mesh.SurfaceAddVertex(new Vector3(p.X, y, p.Y));
                    mesh.SurfaceAddVertex(new Vector3(q.X, y, q.Y));
                }
                mesh.SurfaceAddVertex(new Vector3(p.X, 0.02f, p.Y));
                mesh.SurfaceAddVertex(new Vector3(p.X, h, p.Y));
            }
        }
        mesh.SurfaceEnd();
        var outline = new MeshInstance3D
        {
            Mesh = mesh,
            Visible = false,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = _highContrast ? Colors.White : colour.Darkened(0.45f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        Into.AddChild(outline);
        return outline;
    }

    // The alpha a terrain piece is drawn with now (for the UI check); null when it is not a tall piece.
    public float? TerrainAlpha(string id) => _terrainPaint.TryGetValue(id, out var paint) ? paint.AlbedoColor.A : null;

    private void UpdateSeeThrough()
    {
        if (_terrainPaint.Count == 0)
        {
            return;
        }
        // QoL H2: worked out again only when the camera has moved or a model is travelling (or after a redraw), not every
        // frame.
        var key = (_camera.GlobalTransform, _topDown);
        if (!Gliding && _seeThroughKey == key)
        {
            return;
        }
        _seeThroughKey = key;
        SeeThroughUpdates++;
        var hiding = HidingTerrain();
        if (hiding.SetEquals(_faded))
        {
            return;
        }
        foreach (var (id, paint) in _terrainPaint)
        {
            var fade = hiding.Contains(id);
            paint.AlbedoColor = paint.AlbedoColor with { A = fade ? FadedAlpha : 1f };
            paint.Transparency = fade ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
            if (_terrainOutlines.TryGetValue(id, out var outline))
            {
                outline.Visible = fade;
            }
        }
        _faded.Clear();
        _faded.UnionWith(hiding);
    }

    // ---- watchable moves (XCOM-style UX, improvement 3) ----

    // A model the server moved travels its path from where it was drawn, briefly, instead of jumping. Only the drawing
    // moves: the scene, the selection, the previews and every check use the server's position throughout. When the
    // path ends off screen the camera turns to it (keeping the zoom).
    private sealed record Glide(ModelId Model, Vector2[] Points, float[] Lengths, float Total, double Start, double Seconds, float[] Heights);

    private readonly List<Glide> _glides = [];
    private readonly Dictionary<ModelId, List<(Node3D Node, Vector3 At)>> _modelNodes = [];

    public const float GlideInchesPerSecond = 24f;
    public const double GlideMinSeconds = 0.25;
    public const double GlideMaxSeconds = 0.7;

    // How many moves have been shown travelling, and the last one (for the UI check).
    public int GlidesStarted { get; private set; }

    public (ModelId Model, Vector2 From, Vector2 To, double Seconds)? LastGlide { get; private set; }

    // Whether a model is still travelling on screen (the UI check waits for this before it measures the drawing).
    public bool Gliding => _glides.Count > 0;

    // The point of a model the see-through walls keep visible (Godot units: x, height, table y; for the UI check).
    public Vector3? BodyAt(ModelId id) => _modelNodes.TryGetValue(id, out var nodes) && nodes.Count > 0 ? nodes[0].Node.Position : null;

    // Where a model is drawn now (its base centre, inches); the server's position once it has stopped.
    public Vector2? DrawnAt(ModelId id) => _modelNodes.TryGetValue(id, out var nodes) && nodes.Count > 0
        ? new Vector2(nodes[0].Node.Position.X, nodes[0].Node.Position.Z)
        : null;

    private void StartGlide(Motion motion, Vector2 from)
    {
        var points = new[] { from }.Concat(motion.Path.Select(p => new Vector2((float)p.X, (float)p.Y))).ToArray();
        var lengths = new float[points.Length];
        for (var i = 1; i < points.Length; i++)
        {
            lengths[i] = lengths[i - 1] + points[i - 1].DistanceTo(points[i]);
        }
        if (lengths[^1] < 0.01f)
        {
            return;
        }
        _glides.RemoveAll(g => g.Model == motion.Model);
        var seconds = Math.Clamp(lengths[^1] / GlideInchesPerSecond, GlideMinSeconds, GlideMaxSeconds);
        // Towers proposal, stage T4: each point's height (a tower's platform or the ground), so a climb is seen going up.
        var heights = points.Select(p => (float)LevelAt(p.X, p.Y)).ToArray();
        _glides.Add(new Glide(motion.Model, points, lengths, lengths[^1], Time.GetTicksMsec() / 1000.0, seconds, heights));
        GlidesStarted++;
        LastGlide = (motion.Model, points[0], points[^1], seconds);
        var end = points[^1];
        var standing = _scene?.Find(motion.Model)?.Standing ?? 0;
        if (!OnScreen(ScreenOf(end.X, end.Y, standing)))
        {
            CentreOnPoint(end);
        }
    }

    // The height a base centred at this table point stands at: a tower's height inside its footprint, a trench's floor
    // when the centre is in one (trench-maps stage S3), else the ground (as Rules.Core.Geometry.Towers.ElevationAt and the
    // rules' U81; display only).
    private double LevelAt(double x, double y) =>
        _scene?.Terrain.FirstOrDefault(t => t.Tower && t.Area.X0 < x && x < t.Area.X1 && t.Area.Y0 < y && y < t.Area.Y1)?.Height
        ?? (_scene is { } scene && TrenchPits.At(scene, x, y) is { } trench ? -trench.Depth : 0);

    private bool OnScreen(Vector2? at) => at is { } p && p.X >= 0 && p.Y >= 0 && p.X <= Size.X && p.Y <= Size.Y;

    // Draws each travelling model at its point along the path; a finished one is back on the server's position.
    private void ApplyGlides()
    {
        if (_glides.Count == 0)
        {
            return;
        }
        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var glide in _glides.ToList())
        {
            var t = (float)Math.Clamp((now - glide.Start) / glide.Seconds, 0, 1);
            // Eased so it starts and stops gently.
            var eased = t * t * (3 - 2 * t);
            var along = eased * glide.Total;
            var i = 1;
            while (i < glide.Points.Length - 1 && glide.Lengths[i] < along)
            {
                i++;
            }
            var leg = glide.Lengths[i] - glide.Lengths[i - 1];
            var part = leg <= 0 ? 1 : (along - glide.Lengths[i - 1]) / leg;
            var at = glide.Points[i - 1].Lerp(glide.Points[i], part);
            var offset = t >= 1 ? Vector2.Zero : at - glide.Points[^1];
            var rise = t >= 1 ? 0 : Mathf.Lerp(glide.Heights[i - 1], glide.Heights[i], part) - glide.Heights[^1];
            if (_modelNodes.TryGetValue(glide.Model, out var nodes))
            {
                foreach (var (node, rest) in nodes)
                {
                    if (IsInstanceValid(node))
                    {
                        node.Position = rest + new Vector3(offset.X, rise, offset.Y);
                    }
                }
            }
            if (t >= 1)
            {
                _glides.Remove(glide);
            }
        }
        // Labels moved: place them again next frame.
        _labelsPlaced = "";
    }

    public void AddNote(UnitNote note)
    {
        if (_scene?.Find(note.Model) is not { } model)
        {
            return;
        }
        // The same note again replaces the old one; at most NotesPerModel per model, the oldest goes first.
        foreach (var old in _shown.Where(n => n.Note.Model == note.Model && (n.Note == note || n.Note.Tone == NoteTone.Failure && note.Tone == NoteTone.Failure && n.Note.Text != note.Text && IsLocal(n.Note))).ToList())
        {
            Remove(old);
        }
        while (_shown.Count(n => n.Note.Model == note.Model) >= NotesPerModel)
        {
            Remove(_shown.First(n => n.Note.Model == note.Model));
        }
        var label = MakeLabel(note.Text, Vector3.Zero, 1.0f);
        label.Modulate = note.Tone switch
        {
            NoteTone.Success => new Color(0.55f, 1f, 0.55f),
            NoteTone.Failure => new Color(1f, 0.8f, 0.3f),
            NoteTone.Damage => new Color(1f, 0.45f, 0.4f),
            _ => new Color(0.85f, 0.9f, 1f),
        };
        _notes.AddChild(label);
        _shown.Add((note, Time.GetTicksMsec() / 1000.0, label));
        Stack(model.Id);
    }

    // The battlefield's own refusals (a click not sent) are replaced by the next one rather than piling up.
    private static bool IsLocal(UnitNote note) => note.Text is "INVALID MOVE" or "INVALID PLACEMENT" or "TOO FAR" or "TOO CLOSE";

    // A new action dismisses the battlefield's own stale refusals.
    private void DismissLocalNotes()
    {
        foreach (var old in _shown.Where(n => IsLocal(n.Note)).ToList())
        {
            Remove(old);
        }
    }

    private void Remove((UnitNote Note, double Born, Label3D Label) shown)
    {
        _shown.Remove(shown);
        shown.Label.QueueFree();
    }

    // Newest at the bottom, just above the model's id; older ones above it. A note that would cover another model's id, a
    // zone or objective label, or another model's note goes up a line at a time (proposal G: no text over text).
    private void Stack(ModelId id)
    {
        if (_scene?.Find(id) is not { } model)
        {
            return;
        }
        var mine = _shown.Where(n => n.Note.Model == id).ToList();
        _idLabels.TryGetValue(id, out var own);
        var taken = _idLabels.Values.Concat(_fixedLabels)
            .Concat(_shown.Where(n => n.Note.Model != id).Select(n => n.Label)).Select(LabelBox).OfType<Rect2>().ToList();
        // Anchored at the model's label and lifted in screen pixels, so at any zoom the notes sit above that label
        // instead of on it (and follow the id when it was nudged down).
        var lift = AboveLabel + (own is null || own.IsQueuedForDeletion() ? 0 : own.Offset.Y);
        for (var i = mine.Count - 1; i >= 0; i--)
        {
            var label = mine[i].Label;
            label.Position = new Vector3((float)model.X, LabelHeight(model), (float)model.Y);
            lift = Place(label, lift, taken);
            if (LabelBox(label) is { } placed)
            {
                taken.Add(placed);
            }
            lift += LineHeight(label) * Math.Max(1, label.Text.Split('\n').Length);
        }
    }

    // The first free spot for a note: up from its place a line at a time, then down below the model (a crowded corner,
    // such as a zone label just above); with no free spot, the one that covers the least.
    private float Place(Label3D label, float lift, List<Rect2> taken)
    {
        var step = LineHeight(label);
        var tries = Enumerable.Range(0, 16).Select(k => lift + k * step)
            .Concat(Enumerable.Range(1, 16).Select(k => lift - AboveLabel - k * step));
        var best = (Lift: lift, Covered: float.MaxValue);
        foreach (var at in tries)
        {
            Raise(label, at);
            if (LabelBox(label) is not { } box)
            {
                return at;
            }
            var covered = taken.Where(t => t.Grow(1).Intersects(box)).Sum(t => t.Grow(1).Intersection(box).Area);
            if (covered <= 0)
            {
                return at;
            }
            if (covered < best.Covered)
            {
                best = (at, covered);
            }
        }
        Raise(label, best.Lift);
        return best.Lift;
    }

    // Every short label that must not cover another, with its box on screen: model ids, zone and objective labels, and
    // the notes over models (for the UI check).
    // Visual pass Stage 3: each model's blood drop row counts as one of them.
    public IReadOnlyList<(string Text, Rect2 Box)> ScreenLabelBoxes() =>
        [.. _idLabels.Values.Concat(_fixedLabels).Concat(_shown.Select(n => n.Label)).Select(l => (l.Text, Box: LabelBox(l)))
            .Concat(_bloodRows.Where(r => GodotObject.IsInstanceValid(r.Value.Sprite) && r.Value.Sprite.IsInsideTree() && r.Value.Sprite.Texture is not null
                    && !_camera.IsPositionBehind(r.Value.Sprite.GlobalPosition))
                .Select(r => (Text: $"blood {r.Key.Value}", Box: (Rect2?)SpriteBox(r.Value.Sprite, r.Value.Sprite.Texture))))
            .Where(b => b.Box is not null).Select(b => (b.Text, b.Box!.Value))];

    private void FadeNotes()
    {
        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var shown in _shown.ToList())
        {
            var age = now - shown.Born;
            if (age >= NoteSeconds || _scene?.Find(shown.Note.Model) is null)
            {
                Remove(shown);
                continue;
            }
            var alpha = (float)Math.Clamp((NoteSeconds - age) / 0.8, 0, 1);
            shown.Label.Modulate = shown.Label.Modulate with { A = alpha };
            shown.Label.OutlineModulate = new Color(0, 0, 0, alpha);
        }
        foreach (var id in _shown.Select(n => n.Note.Model).Distinct().ToList())
        {
            Stack(id);
        }
    }

    // Where a base would stand.
    private void Ring(TablePoint at, double radius, Color colour, float standing = 0)
    {
        _overlay.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = (float)radius - 0.05f, OuterRadius = (float)radius + 0.1f },
            Position = new Vector3((float)at.X, standing + 0.08f, (float)at.Y),
            MaterialOverride = Paint(colour, noDepth: true),
        });
    }

    // A line drawn in dashes (half an inch on, a quarter off), for paths that are only a preview.
    private void Dashed(TablePoint a, TablePoint b, float height, Color colour)
    {
        var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        for (var at = 0.0; at < length; at += 0.75)
        {
            var to = Math.Min(length, at + 0.5);
            Segment(new TablePoint(a.X + (b.X - a.X) * at / length, a.Y + (b.Y - a.Y) * at / length),
                new TablePoint(a.X + (b.X - a.X) * to / length, a.Y + (b.Y - a.Y) * to / length), height, colour);
        }
    }

    // A climb up or down a ladder in a path preview: a vertical dashed line at the foot, from one height to the other.
    private void Climbing(TablePoint at, double low, double high, Color colour)
    {
        for (var y = low; y < high; y += 0.5)
        {
            var top = Math.Min(high, y + 0.3);
            _overlay.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.12f, (float)(top - y), 0.12f) },
                Position = new Vector3((float)at.X, (float)(y + top) / 2, (float)at.Y),
                MaterialOverride = Paint(colour, noDepth: true),
            });
        }
        Climbs++;
    }

    // How many climbs the path preview drawn now shows (for the UI check).
    public int Climbs { get; private set; }

    // A see-through copy of the model's base where the preview would put it, at the height it would stand at.
    private void Ghost(TablePoint at, ModelId model, double radius, Color edge, float standing = 0, Color? tint = null)
    {
        var seat = tint ?? (_scene?.Find(model) is { } shape ? ModelColour(shape) : edge);
        _overlay.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = (float)radius, BottomRadius = (float)radius, Height = 1.2f, RadialSegments = 24 },
            Position = new Vector3((float)at.X, standing + 0.6f, (float)at.Y),
            MaterialOverride = Paint(new Color(seat.R, seat.G, seat.B, 0.35f)),
        });
        GhostAt = at;
    }

    // Where the ghost of the previewed move is drawn now, if any (for the UI check).
    public TablePoint? GhostAt { get; private set; }

    private void Overlay(string text, TablePoint at, float height, float size)
    {
        _overlayTexts.Add(text);
        _overlay.AddChild(MakeLabel(text, new Vector3((float)at.X, height, (float)at.Y), size));
    }

    // Where a model's own label is drawn: a little above its body.
    private static float LabelHeight(ModelShape model) => (float)model.Standing + (model.Status == ModelStatus.Standing ? 1.2f : 0.3f) + 0.6f;

    // Label3D offsets are in the label's own pixels (the same units as its font size), so these keep their spacing on
    // screen at any distance. AboveLabel clears a selected model's label (centred on its anchor) with a gap.
    private float AboveLabel => 32 * _textScale * 1.1f + 8;

    private static float LineHeight(Label3D label) => label.FontSize * 1.25f;

    private static void Raise(Label3D label, float pixels)
    {
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.Offset = new Vector2(0, pixels);
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

    // Graphics stage G1 (art direction: cold grey overcast light, mud). PS1-style cheap lighting: one cool directional
    // light with no dynamic shadows, a flat overcast background, a cool ambient and no fog, so the battlefield stays
    // sharp and the frame costs what it did before G1 (measured: shadows and haze made a frame about 75% slower in
    // software rendering, and the haze washed out the models). Works the same in Forward+ and Compatibility. High
    // contrast keeps the old flat look on black.
    private readonly DirectionalLight3D _sun = new() { Name = "Sun", ShadowEnabled = false };

    // Visual pass Stage 1 (2026-10-27): figures are drawn on render layers 1 and 2, everything else on layer 1 only, and
    // this light reaches layer 2 alone, so the units are lit a little more than the mud they stand in. It points the
    // way the camera looks (it is the camera's child), lighting the side the player sees. No shadows, no glow.
    private const uint UnitLayer = 2;

    private readonly DirectionalLight3D _unitLight = new()
    {
        Name = "UnitLight",
        ShadowEnabled = false,
        LightCullMask = UnitLayer,
        LightEnergy = UnitPalette.UnitLightEnergy,
        LightColor = new Color(UnitPalette.UnitLightColour.R, UnitPalette.UnitLightColour.G, UnitPalette.UnitLightColour.B),
    };

    // The thin dark outline drawn around every figure (the selected one's warmer): the figure's surfaces drawn again a
    // little larger, back faces only, flat colour. Shared by all figures.
    private static readonly StandardMaterial3D FigureOutline = OutlineMaterial(UnitPalette.Outline);

    private static readonly StandardMaterial3D SelectedFigureOutline = OutlineMaterial(UnitPalette.SelectedOutline);

    private static StandardMaterial3D OutlineMaterial((float R, float G, float B, float A) c) => new()
    {
        AlbedoColor = new Color(c.R, c.G, c.B, c.A),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Front,
        Grow = true,
        GrowAmount = UnitPalette.OutlineWidth,
    };

    private void ApplyEnvironment()
    {
        if (_highContrast)
        {
            _sun.RotationDegrees = new Vector3(-60, 35, 0);
            _sun.LightColor = Colors.White;
            _sun.LightEnergy = 0.9f;
            _environment.Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.55f,
            };
            return;
        }
        _sun.RotationDegrees = new Vector3(-52, 30, 0);
        _sun.LightColor = new Color(0.86f, 0.89f, 0.95f);
        _sun.LightEnergy = 1.0f;
        _environment.Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.17f, 0.18f, 0.19f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.70f, 0.72f, 0.78f),
            AmbientLightEnergy = 0.5f,
        };
    }

    // No fog or haze over the battlefield (kept off so nothing on it is dimmed; the UI check reads it).
    public bool FogOn => _environment.Environment?.FogEnabled == true;

    // Whether the sun casts dynamic shadows (kept off: PS1-style cheap lighting).
    public bool ShadowsOn => _sun.ShadowEnabled;

    // Graphics stage G1: the colours of the battlefield (muted mud, stone, timber).
    public Color GroundColour => _highContrast ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.25f, 0.22f, 0.17f);

    private Color ZoneColour => _highContrast ? new Color(0.36f, 0.36f, 0.36f) : new Color(0.31f, 0.30f, 0.27f);

    // Performance: the terrain (ground, trench pits, decals, zone tints, terrain pieces, outlines, towers) is drawn once
    // into its own layer and kept while nothing it is drawn from changes (TerrainKey); every update redraws only what
    // the match state moves: models, links, mines, zone and objective labels.
    private readonly Node3D _terrain = new() { Name = "Terrain" };
    private bool _drawingTerrain;
    private string? _terrainKey;
    private readonly List<Label3D> _terrainLabels = [];

    private Node3D Into => _drawingTerrain ? _terrain : _content;

    // What a model is drawn from: its shape (position, state, blood markers), whether it is selected, hovered or can be
    // chosen, and the look (text size, contrast, camera, art).
    private readonly record struct ModelKey(ModelShape Shape, bool Selected, bool Hovered, bool Choosable, string Look);

    private sealed record DrawnModel(ModelKey Key, List<Node> Nodes, bool FromArt);

    private readonly Dictionary<ModelId, DrawnModel> _drawnModels = [];
    private int _artVersion;

    // How many models have been drawn (for the UI check: an update draws again only the models that changed).
    public int ModelsDrawn { get; private set; }

    // The id of the first node a model is drawn with now (for the UI check: the same id after an update means kept).
    public ulong? ModelNodeId(ModelId id) => _drawnModels.TryGetValue(id, out var drawn) && drawn.Nodes.Count > 0 && IsInstanceValid(drawn.Nodes[0])
        ? drawn.Nodes[0].GetInstanceId()
        : null;

    // How many times the terrain layer has been drawn (for the UI check: an ordinary update must not draw it again).
    public int TerrainBuilds { get; private set; }

    // Everything the terrain layer is drawn from: the table, the zones' areas, each piece's shape, and the look. The
    // match state's other parts (models, objective controllers, zone owners, mines) are drawn on every update instead.
    private string TerrainKey(BoardScene scene)
    {
        var key = new System.Text.StringBuilder();
        key.Append(System.Globalization.CultureInfo.InvariantCulture, $"{scene.Width:R}x{scene.Depth:R}|{_highContrast}|{_textScale:R}|{_topDown}");
        foreach (var zone in scene.Zones)
        {
            key.Append(System.Globalization.CultureInfo.InvariantCulture, $"|z{zone.Id}:{zone.Area}");
        }
        foreach (var piece in scene.Terrain)
        {
            key.Append(System.Globalization.CultureInfo.InvariantCulture, $"|t{piece.Id}:{piece.Area}:{piece.Profile}:{piece.Height:R}:{piece.Depth:R}:{piece.Platform}:{piece.Objective}");
            foreach (var part in piece.Parts)
            {
                key.Append(System.Globalization.CultureInfo.InvariantCulture, $":{part}");
            }
            foreach (var ladder in piece.Ladders)
            {
                key.Append(System.Globalization.CultureInfo.InvariantCulture, $":L{ladder.Id}@{ladder.At}{ladder.Side}");
            }
        }
        return key.ToString();
    }

    private void ClearTerrain()
    {
        foreach (var child in _terrain.GetChildren())
        {
            _terrain.RemoveChild(child);
            child.QueueFree();
        }
        _terrainPaint.Clear();
        _terrainOutlines.Clear();
        _towers.Clear();
        _pits.Clear();
        _groundCells.Clear();
        _terrainLabels.Clear();
        _ladders = 0;
        _decals = 0;
        _terrainFromArt = 0;
        _groundFromArt = false;
        _faded.Clear();
        _terrainKey = null;
    }

    private void Rebuild()
    {
        // Performance: a model is drawn again only when what it is drawn from changed (ModelKey); the others keep their
        // nodes. Everything else in _content (rings on objectives, zone labels, mines, links) is drawn again.
        var keys = new Dictionary<ModelId, ModelKey>();
        if (_scene is { } now)
        {
            var look = $"{_highContrast}|{_textScale}|{_topDown}|{now.Width}|{now.Depth}|{_artVersion}";
            foreach (var model in now.Models)
            {
                var selected = model.Id == _selected;
                keys[model.Id] = new ModelKey(model, selected, model.Id == _hovered, model.Yours && !selected && Interaction.CanChoose(model.Id), look);
            }
        }
        var kept = new HashSet<Node>();
        foreach (var (id, drawn) in _drawnModels.ToList())
        {
            if (keys.TryGetValue(id, out var wanted) && wanted == drawn.Key && drawn.Nodes.All(IsInstanceValid))
            {
                kept.UnionWith(drawn.Nodes);
                continue;
            }
            _drawnModels.Remove(id);
            _bodyColours.Remove(id);
            _eligibleRings.Remove(id);
            _idLabels.Remove(id);
            _bloodDrops.Remove(id);
            _bloodRows.Remove(id);
            _modelNodes.Remove(id);
        }
        // Taken out of the tree at once, so the new nodes get their names (Figurea1, BaseRima1) and not Godot's renamed ones.
        foreach (var child in _content.GetChildren())
        {
            if (!kept.Contains(child))
            {
                _content.RemoveChild(child);
                child.QueueFree();
            }
        }
        _fixedLabels.Clear();
        _meleeLinks.Clear();
        _meleeLinkNames.Clear();
        _seeThroughKey = null;
        _labelsPlaced = "";
        if (_scene is not { } scene)
        {
            ClearTerrain();
            return;
        }
        var key = TerrainKey(scene);
        if (key != _terrainKey)
        {
            ClearTerrain();
            _drawingTerrain = true;
            try
            {
                DrawTerrain(scene);
            }
            finally
            {
                _drawingTerrain = false;
            }
            _terrainKey = key;
            TerrainBuilds++;
        }
        _fixedLabels.AddRange(_terrainLabels);
        var holes = TrenchPits.Holes(scene);
        foreach (var zone in scene.Zones)
        {
            // Proposal G: "ZONE A" at the zone's outer edge, away from the models deployed in it.
            var area = zone.Area;
            var edges = new[] { (X: area.X0 + 1.2, Y: area.CentreY), (X: area.X1 - 1.2, Y: area.CentreY), (X: area.CentreX, Y: area.Y0 + 1.2), (X: area.CentreX, Y: area.Y1 - 1.2) };
            var edge = edges.OrderByDescending(e => Math.Pow(e.X - scene.Width / 2, 2) + Math.Pow(e.Y - scene.Depth / 2, 2)).First();
            _fixedLabels.Add(MakeText(zone.Short, new Vector3((float)edge.X, 0.3f, (float)edge.Y), 0.75f));
        }
        // Proposal G: only objectives carry text; the kind of terrain shows by its colour and height, and the panel's
        // Details has the legend. QoL H4: a compact mark ("◆2", with "A" or "B" for the seat the rules say controls it
        // now, in that seat's colour) and a flat ring on the piece; brighter and larger when the objective is selected.
        foreach (var piece in scene.Terrain.Where(t => t.Objective && !t.Trench))
        {
            var area = piece.Area;
            var h = (float)piece.Height;
            var chosen = piece.Id == _selectedObjective;
            var tint = piece.Controller is { } owner ? SeatColour(owner) : _highContrast ? Colors.White : new Color(0.95f, 0.85f, 0.45f);
            var mark = MakeText(piece.MarkWithController, new Vector3((float)area.CentreX, h + 0.4f, (float)area.CentreY), chosen ? 0.85f : 0.65f);
            mark.Modulate = tint;
            _fixedLabels.Add(mark);
            var radius = (float)Math.Min(1.0, Math.Min(area.Width, area.Depth) / 2 * 0.8);
            _content.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = radius - (chosen ? 0.22f : 0.1f), OuterRadius = radius },
                Position = new Vector3((float)area.CentreX, h + 0.03f, (float)area.CentreY),
                MaterialOverride = Paint(chosen ? (_highContrast ? Colors.Yellow : new Color(1f, 0.85f, 0.2f)) : tint, noDepth: true),
            });
        }
        // Mines stage M3: the Mine Placement Area shaded while the defender lays the markers, and every Mine Marker on the
        // table as a flat disc with its mark ("M3", "M3 dud" for the defender): rust where it is unknown, grey a dud, red
        // live. Drawn only; the server decides where a model halts.
        MinesDrawn = 0;
        if (scene.MineArea is { } mineArea)
        {
            foreach (var cell in TrenchPits.Around(mineArea, holes))
            {
                Box(new Vector3((float)cell.CentreX, 0.025f, (float)cell.CentreY), new Vector3((float)cell.Width, 0.02f, (float)cell.Depth),
                    _highContrast ? new Color(0.45f, 0.45f, 0.1f) : new Color(0.42f, 0.3f, 0.18f));
            }
        }
        foreach (var mine in scene.Mines)
        {
            DrawMine(_content, mine.X, mine.Y, mine.Radius, mine.Known, mine.Mark);
            MinesDrawn++;
        }
        foreach (var model in scene.Models)
        {
            if (_drawnModels.ContainsKey(model.Id))
            {
                continue;
            }
            var firstNode = _content.GetChildCount();
            var fromArt = _figuresFromArt;
            DrawModel(model, keys[model.Id].Choosable);
            var nodes = Enumerable.Range(firstNode, _content.GetChildCount() - firstNode).Select(i => _content.GetChild(i)).ToList();
            // Everything just drawn for this model (body, rings, id), so a glide can carry it along its path.
            _modelNodes[model.Id] = [.. nodes.OfType<Node3D>().Select(n => (n, n.Position))];
            _drawnModels[model.Id] = new DrawnModel(keys[model.Id], nodes, _figuresFromArt > fromArt);
            ModelsDrawn++;
        }
        _figuresFromArt = _drawnModels.Values.Count(d => d.FromArt);
        _figuresPlain = _drawnModels.Count - _figuresFromArt;
        foreach (var link in scene.Links)
        {
            DrawMeleeLink(scene.Find(link.One), scene.Find(link.Two));
        }
    }

    // The terrain layer (see TerrainKey), drawn into _terrain.
    private void DrawTerrain(BoardScene scene)
    {
        // Trench-maps stage S3: with trenches the ground is boxes around their parts, reaching below the deepest floor, so
        // their sides are the trench walls; each part gets a floor at its depth. Without trenches, the one flat box as before.
        var holes = TrenchPits.Holes(scene);
        var below = (float)TrenchPits.Deepest(scene) + 0.1f;
        var groundArt = _highContrast ? null : Visuals.Texture(VisualCatalog.GroundKey);
        foreach (var cell in TrenchPits.Around(new TableRect(0, 0, scene.Width, scene.Depth), holes))
        {
            Box(new Vector3((float)cell.CentreX, -below / 2, (float)cell.CentreY), new Vector3((float)cell.Width, below, (float)cell.Depth), GroundColour);
            _groundCells.Add(cell);
            // Graphics stage G0: the ground's texture from the art catalog, if any, tiled once per 6" (not in high contrast).
            if (groundArt is not null && _terrain.GetChild(_terrain.GetChildCount() - 1) is MeshInstance3D { MaterialOverride: StandardMaterial3D groundPaint })
            {
                Textured(groundPaint, groundArt, 6f);
                _groundFromArt = true;
            }
        }
        foreach (var trench in scene.Terrain.Where(t => t.Trench))
        {
            foreach (var part in trench.Parts)
            {
                var floor = (float)trench.Depth;
                Box(new Vector3((float)part.CentreX, -floor - (below - floor) / 2, (float)part.CentreY), new Vector3((float)part.Width, below - floor, (float)part.Depth), TrenchFloorColour);
                DrawDuckboards(part, floor);
            }
            _pits[trench.Id] = -trench.Depth;
        }
        // Graphics stage G2: flat craters and duckboards on open ground, clear of every terrain piece and zone.
        if (!_highContrast)
        {
            foreach (var spot in GroundDecor.For(scene))
            {
                if (Visuals.Texture(VisualCatalog.DecalKey(spot.Kind)) is not { } decal)
                {
                    continue;
                }
                _terrain.AddChild(new MeshInstance3D
                {
                    Name = $"Decal{_decals}",
                    Mesh = new PlaneMesh { Size = new Vector2((float)spot.Width, (float)spot.Depth) },
                    Position = new Vector3((float)spot.X, (float)GroundDecor.Lift, (float)spot.Y),
                    RotationDegrees = new Vector3(0, (float)spot.TurnDegrees, 0),
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoTexture = decal,
                        Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                        AlphaScissorThreshold = 0.5f,
                        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                    },
                });
                _decals++;
            }
        }
        foreach (var zone in scene.Zones)
        {
            // Not over a trench, so the pit stays open.
            foreach (var cell in TrenchPits.Around(zone.Area, holes))
            {
                Box(new Vector3((float)cell.CentreX, 0.01f, (float)cell.CentreY), new Vector3((float)cell.Width, 0.02f, (float)cell.Depth), ZoneColour);
            }
        }
        foreach (var piece in scene.Terrain)
        {
            // A trench is the pit drawn with the ground above.
            if (piece.Trench)
            {
                continue;
            }
            var h = (float)piece.Height;
            // Graphics stages G2 and G4: with its texture (rubble, sandbags, stone, timber), a piece is drawn as its models
            // (TerrainDress: a sandbag ring, rough ground with wire, a ruined wall, a timber tower), every part inside the
            // rules' footprint and under its height, in one mesh; without it, as the plain box.
            if (!_highContrast && Visuals.Texture(TerrainDress.TextureKey(piece)) is { } surface)
            {
                var dressed = Paint(Colors.White);
                Textured(dressed, surface, 2f);
                dressed.VertexColorUseAsAlbedo = true;
                dressed.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                _terrain.AddChild(new MeshInstance3D { Name = $"Terrain{piece.Id}", Mesh = TerrainMesh.Build(TerrainDress.For(piece)), MaterialOverride = dressed });
                _terrainFromArt++;
            }
            else
            {
                // One box per rectangle (stage S8: a ruined corner is two joined walls, not the box around them).
                foreach (var part in piece.Parts)
                {
                    Box(new Vector3((float)part.CentreX, h / 2, (float)part.CentreY), new Vector3((float)part.Width, h, (float)part.Depth), TerrainColour(piece));
                }
            }
            if (piece.Height >= SeeThrough.MinHeight && _terrain.GetChild(_terrain.GetChildCount() - 1) is MeshInstance3D { MaterialOverride: StandardMaterial3D paint })
            {
                _terrainPaint[piece.Id] = paint;
                _terrainOutlines[piece.Id] = Outline(piece, TerrainColour(piece));
            }
            if (piece.Tower)
            {
                DrawTower(piece);
            }
        }
    }

    // Mine Markers on the table (stage M3), and how many the last rebuild drew (for the UI check).
    public int MinesDrawn { get; private set; }

    // The markers being laid on this screen (stage M3), not sent yet; for the UI check.
    public int LayingDrawn { get; private set; }

    private void DrawMine(Node3D into, double x, double y, double radius, MineKnown known, string mark)
    {
        var level = (float)LevelAt(x, y);
        var colour = _highContrast
            ? known switch { MineKnown.Live => Colors.Yellow, MineKnown.Dud => new Color(0.6f, 0.6f, 0.6f), _ => Colors.White }
            : known switch { MineKnown.Live => new Color(0.8f, 0.15f, 0.1f), MineKnown.Dud => new Color(0.5f, 0.5f, 0.48f), _ => new Color(0.55f, 0.32f, 0.15f) };
        into.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = (float)radius, BottomRadius = (float)radius, Height = 0.12f, RadialSegments = 20 },
            Position = new Vector3((float)x, level + 0.06f, (float)y),
            MaterialOverride = Paint(colour),
        });
        var label = MakeLabel(mark, new Vector3((float)x, level + 0.5f, (float)y), 0.5f);
        into.AddChild(label);
        if (into == _content)
        {
            _fixedLabels.Add(label);
        }
    }

    // Redraws the markers being laid and tells the screen (the decision card) after a click or a button on it.
    public void MinesChanged()
    {
        DrawPreview();
        Changed?.Invoke();
    }

    // Towers proposal, stage T4: what is accessible on a tower, drawn plainly. The platform top is a walkable deck in a
    // lighter colour, each ladder is drawn on its side (two rails and rungs, in a colour of its own), and "TOWER 2.0\""
    // stands over the deck. The rules decide where a model can go; this only shows it.
    private void DrawTower(TerrainShape piece)
    {
        var area = piece.Area;
        var h = (float)piece.Height;
        Box(new Vector3((float)area.CentreX, h + 0.02f, (float)area.CentreY), new Vector3((float)area.Width - 0.12f, 0.04f, (float)area.Depth - 0.12f), DeckColour);
        foreach (var ladder in piece.Ladders)
        {
            var (nx, nz) = ladder.Side switch
            {
                Rules.Core.Geometry.Side.West => (-1f, 0f),
                Rules.Core.Geometry.Side.East => (1f, 0f),
                Rules.Core.Geometry.Side.South => (0f, -1f),
                _ => (0f, 1f),
            };
            // Along the side (a quarter turn from the outward direction).
            var (ax, az) = (-nz, nx);
            var foot = new Vector3((float)ladder.At.X + nx * 0.08f, 0, (float)ladder.At.Y + nz * 0.08f);
            foreach (var rail in new[] { -0.32f, 0.32f })
            {
                Box(foot + new Vector3(ax * rail, (h + 0.35f) / 2, az * rail), new Vector3(Math.Abs(ax) * 0.06f + Math.Abs(nx) * 0.06f, h + 0.35f, Math.Abs(az) * 0.06f + Math.Abs(nz) * 0.06f), LadderColour);
            }
            for (var y = 0.3f; y < h; y += 0.4f)
            {
                Box(foot + new Vector3(0, y, 0), new Vector3(Math.Abs(ax) * 0.64f + Math.Abs(nx) * 0.06f, 0.06f, Math.Abs(az) * 0.64f + Math.Abs(nz) * 0.06f), LadderColour);
            }
            _ladders++;
        }
        _terrainLabels.Add(MakeText(piece.TowerMark, new Vector3((float)area.CentreX, h + 0.5f, (float)area.CentreY), 0.6f));
        _towers.Add(piece.Id);
    }

    private Color DeckColour => _highContrast ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.52f, 0.42f, 0.29f);

    // Trench-maps stage S3: a trench's floor, darker mud than the ground (high contrast: near black), and its duckboards.
    private Color TrenchFloorColour => _highContrast ? new Color(0.06f, 0.06f, 0.06f) : new Color(0.17f, 0.14f, 0.10f);

    private Color DuckboardColour => _highContrast ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.40f, 0.31f, 0.20f);

    // Duckboards along the part's long side: slats 0.2" wide every 0.5", inset 0.3" from the walls, lying on the floor.
    // Decoration only, under 0.05" high.
    private void DrawDuckboards(TableRect part, float floor)
    {
        var along = part.Width >= part.Depth;
        var length = along ? part.Width : part.Depth;
        var across = (along ? part.Depth : part.Width) - 0.6;
        if (across <= 0.2)
        {
            return;
        }
        for (var at = 0.25; at + 0.2 <= length; at += 0.5)
        {
            var (x, y) = along ? (part.X0 + at + 0.1, part.CentreY) : (part.CentreX, part.Y0 + at + 0.1);
            var size = along ? new Vector3(0.2f, 0.03f, (float)across) : new Vector3((float)across, 0.03f, 0.2f);
            Box(new Vector3((float)x, -floor + 0.015f, (float)y), size, DuckboardColour);
        }
    }

    // Trench-maps stage S3, for the UI check: each trench drawn as a pit (its id and floor height) and the ground boxes.
    private readonly Dictionary<string, double> _pits = [];
    private readonly List<TableRect> _groundCells = [];

    public IReadOnlyDictionary<string, double> PitsDrawn => _pits;

    public IReadOnlyList<TableRect> GroundCells => _groundCells;

    private Color LadderColour => _highContrast ? Colors.Yellow : new Color(0.82f, 0.66f, 0.38f);

    // Graphics stage G0: the art (client/assets/catalog.json), and how much of the battlefield was drawn with it.
    public Visuals Visuals { get; } = Visuals.Load();

    // Graphics stage G3: the art's loaded originals are freed with the battlefield.
    public override void _ExitTree()
    {
        Visuals.Release();
        base._ExitTree();
    }
    private int _figuresFromArt;
    private int _figuresPlain;
    private bool _groundFromArt;
    private int _terrainFromArt;
    private int _decals;

    // Graphics stages G2 and G4, for the UI checks: how many terrain pieces are drawn with their models, and the ground
    // decals drawn now.
    public int TerrainFromArt => _terrainFromArt;

    // The box around a piece's drawn models (Godot units), or null when it is the plain box.
    public Aabb? TerrainDrawn(string id) => _terrain.GetNodeOrNull<MeshInstance3D>($"Terrain{id}") is { } mesh && IsInstanceValid(mesh)
        ? mesh.GlobalTransform * mesh.GetAabb()
        : null;

    public IReadOnlyList<(string Name, Aabb Box)> Decals => [.. Enumerable.Range(0, _decals)
        .Select(i => _terrain.GetNodeOrNull<MeshInstance3D>($"Decal{i}")).OfType<MeshInstance3D>()
        .Where(IsInstanceValid).Select(d => (d.Name.ToString(), d.GlobalTransform * d.GetAabb()))];

    // A PS1-style texture on a plain box: white so the texture shows its own colours, tiled in world inches (triplanar),
    // sampled as crisp pixels. The colour's alpha (the see-through fade) still works on top.
    private static void Textured(StandardMaterial3D paint, Texture2D texture, float tile)
    {
        paint.AlbedoTexture = texture;
        paint.AlbedoColor = Colors.White;
        paint.Uv1Triplanar = true;
        paint.Uv1Scale = Vector3.One / tile;
        paint.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
    }

    public (int FromArt, int Plain, bool Ground) ArtDrawn => (_figuresFromArt, _figuresPlain, _groundFromArt);

    // Graphics stage G3, for the UI check: how a model's figure is drawn now, or null when it has the plain body. Box: the
    // figure's meshes in world space (Godot units: x, height, table y); Accent: the colour its accent parts are painted;
    // Rim: the radius of the base drawn under it; Base: the colour of the base's top; Edge: the colour of its thin rim;
    // Outlined: every mesh has the outline (the selected one's when selected); UnitLit: every mesh is in the unit light.
    public (Aabb Box, Color? Accent, float? Rim, Color? Base, Color? Edge, bool Outlined, bool UnitLit)? FigureOf(ModelId id)
    {
        if (_content.GetNodeOrNull<Node3D>($"Figure{id.Value}") is not { } holder || !IsInstanceValid(holder))
        {
            return null;
        }
        Aabb? box = null;
        Color? accent = null;
        var (outlined, lit) = (true, true);
        foreach (var mesh in holder.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            outlined &= mesh.MaterialOverlay == (id == _selected ? SelectedFigureOutline : FigureOutline);
            lit &= (mesh.Layers & UnitLayer) != 0;
            var b = mesh.GlobalTransform * mesh.Mesh.GetAabb();
            box = box is { } a ? a.Merge(b) : b;
            if (mesh.Name.ToString().StartsWith("Accent", StringComparison.Ordinal) && mesh.MaterialOverride is StandardMaterial3D m)
            {
                accent = m.AlbedoColor;
            }
        }
        var rim = _content.GetNodeOrNull<MeshInstance3D>($"BaseRim{id.Value}")?.Mesh is CylinderMesh c ? c.TopRadius : (float?)null;
        var top = _content.GetNodeOrNull<MeshInstance3D>($"BaseTop{id.Value}")?.MaterialOverride is StandardMaterial3D t ? t.AlbedoColor : (Color?)null;
        var edge = _content.GetNodeOrNull<MeshInstance3D>($"BaseEdge{id.Value}")?.MaterialOverride is StandardMaterial3D e ? e.AlbedoColor : (Color?)null;
        return box is { } found ? (found, accent, rim, top, edge, outlined, lit) : null;
    }

    // Draws the battlefield again with another art catalog (the UI check tries a broken one, then puts the real one back).
    public void UseArt(VisualCatalog catalog)
    {
        Visuals.Use(catalog);
        _terrainKey = null;
        _artVersion++;
        Rebuild();
    }

    // The towers and ladders drawn now (for the UI check).
    private readonly List<string> _towers = [];
    private int _ladders;

    public IReadOnlyList<string> TowersDrawn => _towers;

    public int LaddersDrawn => _ladders;

    // QoL I5: a short bar from base edge to base edge between two enemy models the rules say are engaged (within 1"),
    // drawn through walls. Hidden while a model glides, since it joins where the models end up.
    private readonly List<MeshInstance3D> _meleeLinks = [];
    private readonly List<string> _meleeLinkNames = [];

    private void DrawMeleeLink(ModelShape? one, ModelShape? two)
    {
        if (one is null || two is null)
        {
            return;
        }
        var a = new Vector2((float)one.X, (float)one.Y);
        var b = new Vector2((float)two.X, (float)two.Y);
        var gap = a.DistanceTo(b);
        var direction = gap > 0.001f ? (b - a) / gap : Vector2.Right;
        // From just inside one base to just inside the other, so touching bases still show a visible link.
        var from = a + direction * Math.Max(0f, (float)one.Radius - 0.3f);
        var to = b - direction * Math.Max(0f, (float)two.Radius - 0.3f);
        var length = Math.Max(0.3f, from.DistanceTo(to));
        var height = (float)Math.Max(one.Standing, two.Standing) + 0.7f;
        var bar = new MeshInstance3D
        {
            Name = $"MeleeLink-{one.Id.Value}-{two.Id.Value}",
            Mesh = new BoxMesh { Size = new Vector3(0.16f, 0.06f, length) },
            Position = new Vector3((from.X + to.X) / 2, height, (from.Y + to.Y) / 2),
            Rotation = new Vector3(0, Mathf.Atan2(to.X - from.X, to.Y - from.Y), 0),
            MaterialOverride = Paint(_highContrast ? Colors.Yellow : new Color(1f, 0.45f, 0.15f), noDepth: true),
            Visible = !Gliding,
        };
        _content.AddChild(bar);
        _meleeLinks.Add(bar);
        _meleeLinkNames.Add($"{one.Id.Value}-{two.Id.Value}");
    }

    private void UpdateMeleeLinks()
    {
        foreach (var link in _meleeLinks)
        {
            if (IsInstanceValid(link))
            {
                link.Visible = !Gliding;
            }
        }
    }

    // For the UI check: the engaged pairs drawn now (visible or hidden by a glide), as "a1-b2".
    // (Godot renames a node whose name is still taken by one being freed, so the pairs are kept beside the bars.)
    public IReadOnlyList<string> MeleeLinksDrawn => [.. _meleeLinkNames.Where((_, i) => IsInstanceValid(_meleeLinks[i]))];

    private void DrawModel(ModelShape model, bool choosable)
    {
        {
            var standing = (float)model.Standing;
            var tall = model.Status == ModelStatus.Standing ? 1.2f : 0.3f;
            var colour = BodyColour(model);
            _bodyColours[model.Id] = colour;
            // Graphics stages G0 and G3: a model is drawn with its figure from the art catalog when it has one (not in high
            // contrast), fitted inside the rules' base and to the plain body's height, standing or (Down) lying; otherwise
            // with the plain body, as always. The figure's holder sits where the plain body would, so everything that
            // follows a model (glides, see-through walls) treats both alike.
            if (!_highContrast && Visuals.Model(model.Type is { } type ? VisualCatalog.UnitKey(type) : VisualCatalog.DefaultUnitKey, VisualCatalog.DefaultUnitKey) is { } figure)
            {
                var holder = new Node3D
                {
                    Name = $"Figure{model.Id.Value}",
                    Position = new Vector3((float)model.X, standing + tall / 2, (float)model.Y),
                    // Decoration only: each figure faces the middle of the battlefield.
                    Rotation = new Vector3(0, Mathf.Atan2((float)(_scene!.Width / 2 - model.X), (float)(_scene.Depth / 2 - model.Y)), 0),
                };
                Node3D shape = figure;
                if (model.Status != ModelStatus.Standing)
                {
                    // Down: the figure lies on its back across the base.
                    shape = new Node3D { Name = "Lying" };
                    figure.RotationDegrees = new Vector3(-90, 0, 0);
                    shape.AddChild(figure);
                }
                Visuals.FitOnBase(shape, new Vector3(0, -tall / 2, 0), (float)model.Radius, tall);
                // The seat colour is drawn flat (unshaded), so the side reads the same in light and shade.
                Visuals.PaintAccent(figure, new StandardMaterial3D { AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
                // Visual pass Stage 1: the unit light reaches the figure, and a thin outline keeps it apart from the
                // ground and from the next model (warmer on the selected one).
                foreach (var mesh in figure.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Append(figure as MeshInstance3D).OfType<MeshInstance3D>())
                {
                    mesh.Layers = 1 | UnitLayer;
                    mesh.MaterialOverlay = model.Id == _selected ? SelectedFigureOutline : FigureOutline;
                }
                holder.AddChild(shape);
                _content.AddChild(holder);
                FigureBase(model, standing, colour);
                _figuresFromArt++;
            }
            else
            {
                var body = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = (float)model.Radius, BottomRadius = (float)model.Radius, Height = tall, RadialSegments = 24 },
                    Position = new Vector3((float)model.X, standing + tall / 2, (float)model.Y),
                    MaterialOverride = Paint(colour),
                };
                _content.AddChild(body);
                _figuresPlain++;
            }
            var selected = model.Id == _selected;
            // Your units the rules let you activate now: a thin, faint ring (the units you can click to choose).
            if (choosable)
            {
                _eligibleRings.Add(model.Id);
                BaseRing(model, standing, 0.05f, 0.18f, _highContrast ? new Color(1, 1, 1, 0.8f) : new Color(0.85f, 1f, 0.85f, 0.55f));
            }
            // The model activating now: a cyan ring outside the selection ring.
            if (model.Look == UnitLook.Active)
            {
                BaseRing(model, standing, 0.4f, 0.6f, _highContrast ? Colors.White : new Color(0.3f, 0.95f, 1f));
            }
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
            // Proposal G: the short id ("A2", with ✓ once activated and ↓ when Down); the selected one larger, an activated
            // one dimmed. The full name and state are in the panel beside the battlefield.
            var id = MakeText(model.ShortLabel(), new Vector3((float)model.X, LabelHeight(model), (float)model.Y), selected || model.Id == _hovered ? 1.15f : 0.9f);
            if (model.Activated && !model.Active)
            {
                id.Modulate = new Color(1, 1, 1, 0.6f);
            }
            _idLabels[model.Id] = id;
            BloodRow(model);
        }
    }

    private Color TerrainColour(TerrainShape piece) => _highContrast
        ? piece.Profile switch { TerrainProfile.Wall => new Color(0.75f, 0.75f, 0.75f), TerrainProfile.Cover => new Color(0.5f, 0.5f, 0.5f), _ => new Color(0.3f, 0.3f, 0.3f) }
        : piece.Profile switch { TerrainProfile.Wall => new Color(0.47f, 0.46f, 0.44f), TerrainProfile.Cover => new Color(0.50f, 0.42f, 0.29f), _ => new Color(0.36f, 0.32f, 0.25f) };

    // The body colour: the seat's colour, with a light grey wash once the model has activated this turn (from the server's
    // state, ModelShape.Activated). Kept opaque and about 60% of the seat colour, so the seat still reads at a glance.
    public const float ActivatedWash = 0.4f;

    // Under a figure, its base at the rules' exact radius. Visual pass Stage 1 (2026-10-27): the top is a pale, dull
    // sandstone (UnitPalette.BaseTop), lit like the ground, and only the rim at the edge carries the body colour (the
    // seat, washed grey once activated), muted; before, the whole top was the bright seat colour and outshone the figure. A dark
    // edge outside the rim keeps the base apart from the ground and the rings.
    private void FigureBase(ModelShape model, float standing, Color colour)
    {
        var r = (float)model.Radius;
        _content.AddChild(new MeshInstance3D
        {
            Name = $"BaseRim{model.Id.Value}",
            Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.05f, RadialSegments = 16 },
            Position = new Vector3((float)model.X, standing + 0.025f, (float)model.Y),
            MaterialOverride = Paint(new Color(0.10f, 0.10f, 0.11f)),
        });
        var edge = UnitPalette.Rim((colour.R, colour.G, colour.B));
        _content.AddChild(new MeshInstance3D
        {
            Name = $"BaseEdge{model.Id.Value}",
            Mesh = new CylinderMesh { TopRadius = r - 0.03f, BottomRadius = r - 0.03f, Height = 0.055f, RadialSegments = 16 },
            Position = new Vector3((float)model.X, standing + 0.0275f, (float)model.Y),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(edge.R, edge.G, edge.B), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        });
        var top = UnitPalette.BaseTop;
        _content.AddChild(new MeshInstance3D
        {
            Name = $"BaseTop{model.Id.Value}",
            Mesh = new CylinderMesh { TopRadius = r - UnitPalette.RimWidth, BottomRadius = r - UnitPalette.RimWidth, Height = 0.06f, RadialSegments = 16 },
            Position = new Vector3((float)model.X, standing + 0.03f, (float)model.Y),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(top.R, top.G, top.B), Roughness = 1f },
        });
    }

    private Color BodyColour(ModelShape model)
    {
        var seat = ModelColour(model);
        return model.Look == UnitLook.Activated ? seat.Lerp(new Color(0.5f, 0.5f, 0.5f), ActivatedWash) : seat;
    }

    // The colour each model's body was last drawn with, and the models drawn with an eligible ring (for the UI check).
    private readonly Dictionary<ModelId, Color> _bodyColours = [];
    private readonly HashSet<ModelId> _eligibleRings = [];

    public Color? BodyColourOf(ModelId id) => _bodyColours.TryGetValue(id, out var c) ? c : null;

    public Color SeatColourOf(ModelShape model) => ModelColour(model);

    public IReadOnlyCollection<ModelId> EligibleRings => _eligibleRings;

    // QoL H1: which model is under the pointer (not while panning, and not when the battlefield ignores the mouse); its id
    // is drawn larger and a thin white ring follows its base as drawn.
    private void UpdateHover()
    {
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _hoverCheckedAt >= HoverSeconds || _pointer is null || MouseFilter == MouseFilterEnum.Ignore)
        {
            _hoverCheckedAt = now;
            var over = _pointer is { } at && MouseFilter != MouseFilterEnum.Ignore && !_dragging ? ModelAt(at)?.Id : null;
            if (over != _hovered)
            {
                SetHovered(over);
            }
        }
        if (_hovered is { } id && Hovered is { } model && BodyAt(id) is { } body)
        {
            _hoverRing.Visible = true;
            _hoverRing.Position = new Vector3(body.X, (float)model.Standing + 0.06f, body.Z);
        }
        else
        {
            _hoverRing.Visible = false;
        }
    }

    // QoL I1: rings the models (a clicked combat-log row's), for PointSeconds; the camera centres on them only when one
    // is off screen, keeping the zoom. Nothing is selected and nothing is sent.
    public void PointOut(IReadOnlyList<ModelId> ids)
    {
        foreach (var (_, ring) in _pointRings)
        {
            ring.QueueFree();
        }
        _pointRings.Clear();
        var models = ids.Select(id => _scene?.Find(id)).OfType<ModelShape>().ToList();
        foreach (var model in models)
        {
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = (float)model.Radius + 0.16f, OuterRadius = (float)model.Radius + 0.34f },
                MaterialOverride = Paint(_highContrast ? Colors.Yellow : new Color(1f, 0.95f, 0.6f, 0.95f), noDepth: true),
            };
            _viewport.AddChild(ring);
            _pointRings.Add((model.Id, ring));
        }
        _pointUntil = Time.GetTicksMsec() / 1000.0 + PointSeconds;
        if (models.Count > 0 && models.Any(m => !InView(ScreenOf(m.X, m.Y, m.Standing), 0.05f)))
        {
            _autoFit = false;
            _target = new Vector3((float)models.Average(m => m.X), (float)models.Max(m => m.Standing), (float)models.Average(m => m.Y));
            _focusKey = null;
            PlaceCamera();
            Changed?.Invoke();
        }
        UpdatePointOut();
    }

    private void UpdatePointOut()
    {
        if (_pointRings.Count == 0)
        {
            return;
        }
        if (Time.GetTicksMsec() / 1000.0 > _pointUntil)
        {
            foreach (var (_, ring) in _pointRings)
            {
                ring.QueueFree();
            }
            _pointRings.Clear();
            return;
        }
        foreach (var (id, ring) in _pointRings)
        {
            if (_scene?.Find(id) is { } model && BodyAt(id) is { } body)
            {
                ring.Visible = true;
                ring.Position = new Vector3(body.X, (float)model.Standing + 0.05f, body.Z);
            }
            else
            {
                ring.Visible = false;
            }
        }
    }

    // The models pointed out now (for the UI check).
    public IReadOnlyList<ModelId> PointedOut => [.. _pointRings.Where(r => r.Ring.Visible).Select(r => r.Id)];

    // Whether the hover ring is drawn, and a model id label's font size now (for the UI check).
    public bool HoverRingShown => _hoverRing.Visible;

    public int? IdFontSize(ModelId id) => _idLabels.TryGetValue(id, out var label) && IsInstanceValid(label) ? label.FontSize : null;

    public void SetHovered(ModelId? id)
    {
        if (id == _hovered)
        {
            return;
        }
        var before = _hovered;
        _hovered = id;
        // Stage L5: the shot guide follows the pointer, so the preview is drawn again.
        if (Interaction.IsTarget)
        {
            _drawnVersion = -1;
        }
        foreach (var changed in new[] { before, id })
        {
            if (changed is { } m && _idLabels.TryGetValue(m, out var label) && IsInstanceValid(label))
            {
                label.FontSize = (int)(32 * (m == _selected || m == _hovered ? 1.15f : 0.9f) * _textScale);
            }
        }
        if (Hovered is { } model)
        {
            _hoverRing.Mesh = new TorusMesh { InnerRadius = (float)model.Radius + 0.12f, OuterRadius = (float)model.Radius + 0.24f };
            _hoverRing.MaterialOverride = Paint(_highContrast ? Colors.White : new Color(1, 1, 1, 0.85f), noDepth: true);
        }
        Changed?.Invoke();
    }

    private void BaseRing(ModelShape model, float standing, float inner, float outer, Color colour)
    {
        _content.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = (float)model.Radius + inner, OuterRadius = (float)model.Radius + outer },
            Position = new Vector3((float)model.X, standing + 0.04f, (float)model.Y),
            MaterialOverride = Paint(colour, noDepth: true),
        });
    }

    // Seats differ by colour and by the [A]/[B] letter in every label; colour is never the only sign.
    private Color ModelColour(ModelShape model) => SeatColour(model.Seat);

    public Color SeatColourFor(Seat seat) => SeatColour(seat);

    private Color SeatColour(Seat seat) => _highContrast
        ? seat == Seat.One ? Colors.White : new Color(1f, 0.9f, 0f)
        : seat == Seat.One ? new Color(0.36f, 0.56f, 0.92f) : new Color(0.86f, 0.30f, 0.26f);

    private void Box(Vector3 centre, Vector3 size, Color colour)
    {
        Into.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = centre, MaterialOverride = Paint(colour) });
    }

    // Labels keep their size on screen at any distance, follow the text size setting and show through walls.
    private void Text(string text, Vector3 at, float size) => Into.AddChild(MakeLabel(text, at, size));

    private Label3D MakeText(string text, Vector3 at, float size)
    {
        var label = MakeLabel(text, at, size);
        Into.AddChild(label);
        return label;
    }

    // Proposal G: the model ids, and the fixed labels (zones, objectives) they must not cover.
    private readonly Dictionary<ModelId, Label3D> _idLabels = [];
    private readonly List<Label3D> _fixedLabels = [];
    private string _labelsPlaced = "";

    // Screen pixels per Label3D pixel for a fixed-size label (Godot scales it by the depth in perspective, so its size
    // on screen depends only on the pixel size, the view's height and the projection).
    private float LabelScale(Label3D label)
    {
        var height = _viewport.Size.Y;
        var projection = _camera.GetCameraProjection();
        return label.PixelSize * height / 2 * (_camera.Projection == Camera3D.ProjectionType.Orthogonal ? 1 : projection.Y.Y);
    }

    // Where a label is drawn on the battlefield's screen (control coordinates), as a box; null when off screen.
    public Rect2? LabelBox(Label3D label)
    {
        if (label.IsQueuedForDeletion() || !label.Visible || _camera.IsPositionBehind(label.GlobalPosition))
        {
            return null;
        }
        var scale = LabelScale(label) * (Size.Y / Math.Max(1, _viewport.Size.Y));
        var font = label.Font ?? ThemeDB.FallbackFont;
        var lines = label.Text.Split('\n');
        var width = lines.Max(l => font.GetStringSize(l, HorizontalAlignment.Left, -1, label.FontSize).X) * scale;
        var height = font.GetHeight(label.FontSize) * lines.Length * scale;
        var at = _camera.UnprojectPosition(label.GlobalPosition) * (Size.Y / Math.Max(1, _viewport.Size.Y));
        // Offset is in the label's pixels, x to the right and y up; the screen's y goes down.
        at += new Vector2(label.Offset.X, -label.Offset.Y) * scale;
        var top = label.VerticalAlignment switch
        {
            VerticalAlignment.Bottom => at.Y - height,
            VerticalAlignment.Top => at.Y,
            _ => at.Y - height / 2,
        };
        return new Rect2(at.X - width / 2, top, width, height);
    }

    // Every short label over the battlefield (ids, zones, objectives, notes, path inches) with its box on screen.
    public IReadOnlyList<(string Text, Rect2 Box)> LabelBoxes() =>
        [.. All(this).OfType<Label3D>().Select(l => (l.Text, Box: LabelBox(l))).Where(b => b.Box is not null).Select(b => (b.Text, b.Box!.Value))];

    // The short label drawn on a model ("A2", "A2 ✓"), for the UI check.
    public string? IdLabelOf(ModelId id) => _idLabels.TryGetValue(id, out var label) && !label.IsQueuedForDeletion() ? label.Text : null;

    private static IEnumerable<Node> All(Node node) => node.GetChildren().SelectMany(c => All(c).Prepend(c));

    // Moves model ids that would overlap another id or a zone or objective label down, a line at a time, so no two
    // labels cover each other; worked out again whenever the camera or the models change.
    private void SeparateLabels()
    {
        var key = $"{_camera.GlobalTransform}|{_camera.Size}|{Size}|{_idLabels.Count}|{_scene?.GetHashCode()}";
        if (key == _labelsPlaced)
        {
            return;
        }
        _labelsPlaced = key;
        var taken = _fixedLabels.Select(LabelBox).OfType<Rect2>().ToList();
        foreach (var (model, label) in _idLabels.OrderBy(l => _camera.UnprojectPosition(l.Value.GlobalPosition).Y).ThenBy(l => _camera.UnprojectPosition(l.Value.GlobalPosition).X))
        {
            label.Offset = Vector2.Zero;
            // Visual pass Stage 3: a label's box takes in its drop row below it (as wide as the row where that is wider), so
            // no other label or row covers the drops.
            var row = BloodRowSize(model, label);
            Rect2? Box() => LabelBox(label) is { } b
                ? new Rect2(b.GetCenter().X - Math.Max(b.Size.X, row.X) / 2, b.Position.Y, Math.Max(b.Size.X, row.X), b.Size.Y + row.Y)
                : null;
            if (Box() is not { } box)
            {
                MoveBloodRow(model, label);
                continue;
            }
            var scale = LabelScale(label) * (Size.Y / Math.Max(1, _viewport.Size.Y));
            for (var tries = 0; tries < 6 && taken.Any(t => t.Grow(1).Intersects(box)); tries++)
            {
                // Down by the height of the box it hits, then try again.
                var step = box.Size.Y + 2;
                label.Offset -= new Vector2(0, step / Math.Max(0.001f, scale));
                box = Box() ?? box;
            }
            taken.Add(box);
            MoveBloodRow(model, label);
        }
    }

    // Keeps a model's drop row just under its label after the label was moved (the label's offset is in its own pixels,
    // the row's in its pixels, which are Scale times larger).
    private void MoveBloodRow(ModelId model, Label3D label)
    {
        if (!_bloodRows.TryGetValue(model, out var row) || !GodotObject.IsInstanceValid(row.Sprite))
        {
            return;
        }
        row.Sprite.Offset = row.Offset + label.Offset / row.Scale;
        // On screen, the row's top 2 pixels under the label's box, so the two never cover each other at any text size.
        if (LabelBox(label) is { } box && row.Sprite.Texture is { } texture && !_camera.IsPositionBehind(row.Sprite.GlobalPosition))
        {
            var scale = SpriteScale(row.Sprite);
            var anchor = _camera.UnprojectPosition(row.Sprite.GlobalPosition) * (Size.Y / Math.Max(1, _viewport.Size.Y));
            var middle = box.End.Y + 2 + texture.GetHeight() * scale / 2;
            row.Sprite.Offset = new Vector2(0, -(middle - anchor.Y) / Math.Max(0.0001f, scale));
        }
    }

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
        Transparency = colour.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        NoDepthTest = noDepth,
        ShadingMode = noDepth ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
    };
}
