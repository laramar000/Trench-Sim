using Client.Core;
using Godot;
using Match.Client;
using Match.Protocol;
using Rules.Core.Scenarios;

// M7.3: the connect screen and a text-only match screen. Everything shown comes from Client.Core's MatchController,
// which reads the server's state; this script only lays it out and passes button presses back. It never decides,
// rolls or applies anything. The battlefield, camera, units and previews arrive in M7.4.
// Headless checks: godot --headless --path client -- --smoke-server ws://127.0.0.1:5080/match
//                  godot --headless --path client -- --smoke-view ws://127.0.0.1:5080/match
public partial class Main : Control
{
    private const string Hints = "Tab / Shift+Tab: move between controls · Enter or Space: press · F1: rules in this build · Esc: close a dialog";

    private readonly Theme _theme = new();
    private ColorRect _background = null!;
    private VBoxContainer _root = null!;
    private Label _status = null!;
    private MatchController? _controller;
    private MatchController? _opponent;
    // Single-player (hot-seat) mode: both seats of one match, on a local server this window started; null otherwise.
    private HotSeat? _hotSeat;
    private bool _turnBannerDue;

    // Stage N5: how many turn banners were shown (for the UI check).
    public int TurnBannersShown { get; private set; }

    // The server's state of the seat shown, as last drawn (for the UI check).
    public Rules.Core.State.MatchState? ShownState => _lastScreen?.State;
    private int _changed;
    private long _shown = -1;
    private bool _gapsShown;
    private float _scale = 1f;
    private bool _highContrast;
    private bool _followLog = true;
    // Started by the test center (tools/TestCenter) for a test match; null for a normal start.
    private TestMatch? _test;
    private bool _testReported;

    // Connect screen.
    private VBoxContainer _connect = null!;
    private LineEdit _server = null!;
    private SpinBox _window = null!;
    private OptionButton _scenario = null!;
    private LineEdit _code = null!;
    private VBoxContainer _saved = null!;
    private Label _connectMessage = null!;

    // Match screen.
    private VBoxContainer _match = null!;
    private Label _banner = null!;
    private Label _hotLine = null!;
    private HFlowContainer _bannerButtons = null!;
    private Label _code2 = null!;
    private VBoxContainer _endSection = null!;
    private PanelContainer _resultBox = null!;
    private ScrollContainer _resultScroll = null!;
    private Label _summary = null!;
    private Button _rematch = null!;
    private Label _rematchNote = null!;
    private Label _end = null!;
    private string? _finalHash;
    private Label _state = null!;
    private Label _models = null!;
    private VBoxContainer _boardSection = null!;
    private BoardView _board = null!;
    private Label _boardInfo = null!;
    private Label _hud = null!;
    private RichTextLabel _hudCentre = null!;
    private Label _hudStatus = null!;
    private SignalIcon _signal = null!;
    private PanelContainer _signalCard = null!;
    private Label _signalDetails = null!;
    private PanelContainer _hudStrip = null!;
    private HBoxContainer _icons = null!;
    private IconButton _logButton = null!;
    private PanelContainer _logDrawer = null!;
    private bool _drawerOpen;
    private PanelContainer _decision = null!;
    private Label _decisionTitle = null!;
    private VBoxContainer _decisionButtons = null!;
    private double _feedbackSince;
    private string _feedbackShown = "";
    private Button _follow = null!;
    private VBoxContainer _menuBox = null!;
    private VBoxContainer _matchMenu = null!;
    private PanelContainer _menuPanel = null!;
    private ScrollContainer _page = null!;
    private IconButton _menuButton = null!;
    private bool _menuOpen;
    private BoxContainer _split = null!;
    private VBoxContainer _logBox = null!;
    private VBoxContainer _allChoices = null!;
    private ScreenState? _lastScreen;
    private VBoxContainer _hints = null!;
    private VBoxContainer _actions = null!;
    private Label _notYet = null!;
    private Label _feedback = null!;
    private VBoxContainer _log = null!;
    // One combat log row: a marker for the newest, the compact line (bold, coloured verb) and the event it shows.
    private sealed record LogRow(int Seq, Control Box, Label Marker, RichTextLabel Line, LogLine Source, Button? Toggle = null, Label? Details = null);

    // Proposal J1b: the actions whose combat-log row is expanded (by the first event of the action).
    private readonly HashSet<int> _logOpen = [];

    private readonly List<LogRow> _logRows = [];
    private ScrollContainer _logScroll = null!;
    private AcceptDialog _gaps = null!;
    private AcceptDialog _keySheet = null!;
    private AcceptDialog _scenarioCard = null!;
    private Label _scenarioText = null!;
    private VBoxContainer _scenarioRows = null!;
    private Label _scenarioPreview = null!;
    private ScenarioCardView? _scenarioView;
    private readonly Dictionary<string, Button> _scenarioButtons = [];
    private ConfirmationDialog _takeBack = null!;

    public override async void _Ready()
    {
        Theme = _theme;
        _baseTitle = GetWindow().Title;
        AddChild(_sounds);
        _background = new ColorRect { Color = new Color(0.12f, 0.12f, 0.14f) };
        _background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_background);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _page = scroll;
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.OffsetLeft = 16;
        scroll.OffsetTop = 12;
        scroll.OffsetRight = -16;
        scroll.OffsetBottom = -12;
        AddChild(scroll);
        _root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _root.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_root);

        // Version, settings and keys: shown on the connect screen; in a match they fold into the HUD's Menu (proposal G3).
        _menuBox = new VBoxContainer { Name = "MenuItems" };
        _menuBox.AddThemeConstantOverride("separation", 10);
        _menuBox.AddChild(Text($"Trench Sim client (M7.4: battlefield, in progress). Godot {Engine.GetVersionInfo()["string"]}, " +
            $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}. Rules version {ClaimNoMansLand.RulesVersion}; protocol {ClientSession.SubProtocol}."));
        _menuBox.AddChild(Settings());
        _menuBox.AddChild(Text(Hints));
        _root.AddChild(_menuBox);
        _status = Text("");
        _root.AddChild(_status);
        BuildConnect();
        BuildMatch();
        ApplyTheme();
        ShowConnect();

        var args = OS.GetCmdlineUserArgs();
        _settingsPath = Arg(args, "--ui-check") is { } checking
            ? System.IO.Path.GetFullPath(System.IO.Path.Combine(Arg(args, "--out") ?? "ui-check", $"settings-{checking}.cfg"))
            : Arg(args, "--smoke-server") is null && Arg(args, "--smoke-view") is null ? "user://settings.cfg" : null;
        LoadSettings();
        var (test, testProblem) = TestMatch.Parse(args);
        if (testProblem is not null)
        {
            _connectMessage.Text = $"Test match not started: {testProblem}";
        }
        if (test is not null)
        {
            StartTestMatch(test);
        }
        if (Arg(args, "--ui-check") is { } role)
        {
            // Verification only (tools/UiCheck): drives this screen with key presses and reports what it shows.
            AddChild(new UiCheck(this, role, Arg(args, "--out") ?? "ui-check", Arg(args, "--server") ?? "ws://127.0.0.1:5080/match",
                Arg(args, "--code"), Arg(args, "--scenario") ?? "ranged"));
        }
        else if (Arg(args, "--smoke-server") is { } smoke)
        {
            Print(await Smoke(new Uri(smoke)));
            GetTree().Quit();
        }
        else if (Arg(args, "--smoke-view") is { } view)
        {
            Print(await SmokeView(new Uri(view)));
            if (Arg(args, "--screenshot") is { } shot)
            {
                // Let the layout settle, then save what the window shows (needs a display; not for --headless).
                for (var i = 0; i < 5; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                GetViewport().GetTexture().GetImage().SavePng(shot);
            }
            GetTree().Quit();
        }
    }

    // The controller raises Changed on its own threads; the screen is redrawn here, on Godot's main thread.
    public override void _Process(double delta)
    {
        if (_controller is not null && System.Threading.Interlocked.Exchange(ref _changed, 0) == 1)
        {
            // Hot-seat: show the seat that must act when the shown one has nothing to do (unless switching is by hand).
            _hotSeat?.FollowIfNeeded();
            Render(_controller.Snapshot());
        }
        // The line above the HUD only takes room when it says something.
        _status.Visible = _status.Text.Length > 0;
        FitBattlefield();
        UpdateScaleNote();
    }

    // QoL I2: when the window is too small for the chosen interface scale, Confirm and Cancel end up below the first
    // screen; a note over the battlefield's top-left corner says so (drawn on top, so it moves nothing).
    public const string ScaleNoteText = "Confirm and Cancel are below: scroll down, or choose a smaller Scale in the Menu, or make the window larger.";

    private Label? _scaleNote;

    public bool ScaleNoteShown => _scaleNote?.Visible == true;

    private void UpdateScaleNote()
    {
        if (_scaleNote is null)
        {
            return;
        }
        var height = GetViewportRect().Size.Y;
        var below = _match.Visible && _boardSection.Visible && _pConfirm.IsVisibleInTree() && PageY(_pConfirm) + _pConfirm.Size.Y > height - 2 * PageMargin.Y + 2;
        _scaleNote.Visible = below;
        if (below)
        {
            var board = _board.GetGlobalRect();
            _scaleNote.Position = board.Position + new Vector2(8, _board.TopInset + 8);
            _scaleNote.Size = new Vector2(Math.Max(120, Math.Min(620, board.Size.X - 16)), 0);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.F1 })
        {
            _gaps.PopupCentered();
        }
        else if (@event is InputEventKey { Pressed: true } question && question.Unicode == '?')
        {
            ToggleKeySheet();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.G, CtrlPressed: false, AltPressed: false } && _board is not null && _match.Visible)
        {
            ToggleScenario();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.F2 } && _hotSeat is not null)
        {
            SwitchSeat();
        }
        // Stage N2: Esc keeps an activation that was about to end by itself, also when the board does not have the keyboard.
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape } && _board is not null && _board.KeepActivation())
        {
            GetViewport().SetInputAsHandled();
        }
        // Esc cancels the action being chosen also when the board does not have the keyboard (after a click on the side
        // panel or the action bar). It only cancels; it never sends anything.
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape } && _board is not null
            && _board.Interaction.Mode != InteractionMode.None && _board.Interaction.Pending is null)
        {
            _board.CancelPreview();
            GetViewport().SetInputAsHandled();
        }
        // Stage N3: Esc with nothing being chosen closes the log drawer, then the Menu, wherever the keyboard is.
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape } && _match.Visible && (_drawerOpen || _menuOpen || _keySheet.Visible || _scenarioCard.Visible))
        {
            CloseOverlays();
            GetViewport().SetInputAsHandled();
        }
    }

    // Closing the window (or quitting) stops the hot-seat server at once, so no server process is left behind.
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusIn || what == NotificationWMWindowFocusOut)
        {
            _focused = what == NotificationWMWindowFocusIn;
            if (_lastScreen is not null)
            {
                UpdateTurnAlert();
            }
        }
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
        {
            _hotSeat?.StopServerNow();
        }
    }

    public override void _ExitTree() => _hotSeat?.StopServerNow();

    // ---- connect screen ----

    private void BuildConnect()
    {
        _connect = new VBoxContainer();
        _connect.AddThemeConstantOverride("separation", 8);
        _server = new LineEdit { Text = "ws://127.0.0.1:5080/match", CustomMinimumSize = new Vector2(420, 0) };
        _connect.AddChild(Row(Text("Server address:"), _server));
        _window = new SpinBox { MinValue = 10, MaxValue = 86_400, Step = 10, Value = ReconnectPolicy.DefaultGiveUpAfter.TotalSeconds };
        _connect.AddChild(Row(Text("Server rejoin window (seconds):"), _window));
        _connect.AddChild(Text("Set this to the server's --rejoin-window (300 by default). The client cannot find it out: it decides how long " +
            "reconnecting keeps trying on its own before offering Rejoin now."));
        // The normal rosters, then the small test sizes (each side's first 1 to 3 models) for trying mechanics quickly.
        _scenario = new OptionButton();
        foreach (var choice in ContentNames.Choices)
        {
            _scenario.AddItem(ScenarioLabel(choice));
            _scenario.SetItemMetadata(_scenario.ItemCount - 1, choice);
        }
        _connect.AddChild(Row(Text("Scenario:"), _scenario, Button("Create match", () => Create(ChosenScenario()))));
        _connect.AddChild(Text("The 1 v 1, 2 v 2 and 3 v 3 choices are small test rosters (each side's first models) for trying mechanics quickly; " +
            "the normal roster is the full warbands."));
        _connect.AddChild(Row(Text("Single player:"), Button("Hot-seat: play both sides in this window", StartHotSeat)));
        _connect.AddChild(Text("Hot-seat starts a local server on this PC (no server address needed) and seats you as Seat One and Seat Two of a " +
            "match in the scenario above. F2 switches the side you control; by default the window follows the turn."));
        _code = new LineEdit { PlaceholderText = "match code", CustomMinimumSize = new Vector2(160, 0) };
        _connect.AddChild(Row(Text("Join with a code:"), _code, Button("Join match", () =>
        {
            _createdScenario = null;
            Start(c => c.JoinAsync(_code.Text.Trim().ToUpperInvariant()));
        })));
        _connect.AddChild(Text("Saved seats on this server (Rejoin):"));
        _saved = new VBoxContainer();
        _connect.AddChild(_saved);
        _connect.AddChild(Button("Refresh saved seats", RefreshSaved));
        _connectMessage = Text("");
        _connect.AddChild(_connectMessage);
        _root.AddChild(_connect);
    }

    private void ShowConnect()
    {
        _connect.Visible = true;
        _match.Visible = false;
        _page.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        MoveMenu(_root, 0, true);
        _status.Text = "Connect to a server: create a match, join one with its code, or rejoin a saved seat.";
        RefreshSaved();
        // Keyboard users start in the first field rather than with nothing focused.
        _server.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void RefreshSaved()
    {
        foreach (var child in _saved.GetChildren())
        {
            child.QueueFree();
        }
        var (seats, problem) = SavedSeats.For(new SessionStore(SessionStore.DefaultDirectory()), _server.Text.Trim());
        foreach (var seat in seats)
        {
            var saved = seat;
            _saved.AddChild(Button($"Rejoin match {saved.Code} as {Words.Seat(saved.Seat)}", () => Start(c => c.RejoinSavedAsync(saved))));
        }
        if (seats.Count == 0)
        {
            _saved.AddChild(Text("None."));
        }
        if (problem is not null)
        {
            _saved.AddChild(Text(problem));
        }
    }

    private async void Start(Func<MatchController, Task<ServerMessage>> request)
    {
        if (!Uri.TryCreate(_server.Text.Trim(), UriKind.Absolute, out var endpoint))
        {
            _connectMessage.Text = "That server address is not valid.";
            return;
        }
        var controller = NewController(endpoint);
        _connectMessage.Text = "Connecting…";
        try
        {
            var answer = await request(controller);
            if (answer is not Welcome)
            {
                _connectMessage.Text = $"Refused: {(answer as Rejected)?.Text ?? answer.GetType().Name}";
                await controller.DisposeAsync();
                return;
            }
        }
        catch (Exception e)
        {
            _connectMessage.Text = $"Could not connect: {e.Message}";
            await controller.DisposeAsync();
            return;
        }
        _connectMessage.Text = "";
        Attach(controller);
    }

    private MatchController NewController(Uri endpoint) => new(new MatchControllerOptions
    {
        Endpoint = endpoint,
        Store = new SessionStore(SessionStore.DefaultDirectory()),
        Policy = ReconnectPolicy.ForRejoinWindow(TimeSpan.FromSeconds(_window.Value)),
    });

    private void Attach(MatchController controller)
    {
        _controller = controller;
        controller.Changed += () => System.Threading.Interlocked.Exchange(ref _changed, 1);
        _connect.Visible = false;
        _match.Visible = true;
        // Stage K2: in a match the page never scrolls; everything is laid out in the window (FitBattlefield).
        _page.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _page.ScrollVertical = 0;
        MoveMenu(_matchMenu, 0, true);
        SetMenu(false);
        _shown = -1;
        Render(controller.Snapshot());
        FocusDefault();
        if (!_gapsShown)
        {
            // Playtesters see the rules gaps once per session at the first match, and any time with F1.
            _gapsShown = true;
            _gaps.PopupCentered();
        }
    }

    private async void Leave() => await LeaveAsync();

    // QoL I4: the window that created the match (or a hot-seat) can start the same match again: same scenario and side
    // size, a new match code. A window that joined waits for the other's new code.
    private string? _createdScenario;

    private void Create(string scenario)
    {
        _createdScenario = scenario;
        Start(c => c.CreateAsync(scenario));
    }

    // For the UI check.
    public string SummaryText => _summary.Visible ? _summary.Text : "";

    public bool RematchShown => _rematch.IsVisibleInTree();

    public string RematchNoteText => _rematchNote.Visible ? _rematchNote.Text : "";

    public string? MatchCode => _lastScreen?.Code;

    public Rules.Core.State.Seat? Seat => _lastScreen?.Seat;

    public void LeaveMatch() => Leave();

    public bool CanRematch => _createdScenario is not null && _test is null && _lastScreen?.End is { Aborted: false };

    public async void Rematch()
    {
        if (!CanRematch || _createdScenario is not { } scenario)
        {
            return;
        }
        var hot = _hotSeat is not null;
        await LeaveAsync();
        for (var i = 0; i < _scenario.ItemCount; i++)
        {
            if (_scenario.GetItemMetadata(i).AsString() == scenario)
            {
                _scenario.Select(i);
            }
        }
        if (hot)
        {
            StartHotSeat();
        }
        else
        {
            Create(scenario);
        }
    }

    private async Task LeaveAsync()
    {
        if (_hotSeat is { } hot)
        {
            // Both seats and the local server end together.
            _hotSeat = null;
            _controller = null;
            await hot.DisposeAsync();
        }
        else if (_controller is { } controller)
        {
            _controller = null;
            await controller.DisposeAsync();
        }
        ShowConnect();
    }

    // Starts the local server, creates a match as Seat One and joins it as Seat Two, then shows Seat One.
    private async void StartHotSeat()
    {
        if (_hotSeat is not null || _controller is not null)
        {
            return;
        }
        var repository = LocalServer.FindRepository(ProjectSettings.GlobalizePath("res://"));
        if (repository is null)
        {
            _connectMessage.Text = "Hot-seat starts the server from the Trench Sim source folder (TrenchSim.sln above client/), which was not found.";
            return;
        }
        var scenario = ChosenScenario();
        _createdScenario = scenario;
        try
        {
            var server = await LocalServer.StartAsync(repository, LocalServer.DefaultDataDirectory(),
                text => Callable.From(() => _connectMessage.Text = text).CallDeferred());
            _connectMessage.Text = "Creating the match and seating both sides…";
            var hot = await HotSeat.StartAsync(() => new MatchController(new MatchControllerOptions
            {
                Endpoint = server.Endpoint,
                // The local server runs with its default --rejoin-window of 300 seconds.
                Policy = ReconnectPolicy.ForRejoinWindow(TimeSpan.FromSeconds(300)),
            }), scenario, server);
            _hotSeat = hot;
            hot.Busy = () => _board.Interaction.Pending is not null;
            hot.Other.Changed += () => System.Threading.Interlocked.Exchange(ref _changed, 1);
            hot.Switched += () =>
            {
                _controller = hot.Controller;
                _shown = -1;
                _turnBannerDue = true;
                System.Threading.Interlocked.Exchange(ref _changed, 1);
            };
            _connectMessage.Text = "";
            Attach(hot.Controller);
        }
        catch (Exception e)
        {
            _connectMessage.Text = $"Could not start hot-seat: {e.Message}";
        }
    }

    // Test match: lay the window out beside the other one and press create, join or rejoin for the player, exactly as the
    // buttons on this screen would. The server answers as for any player.
    private void StartTestMatch(TestMatch test)
    {
        _test = test;
        DisplayServer.WindowSetTitle(test.Title(null));
        if (test.Window is "A" or "B")
        {
            var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            var size = new Vector2I(screen.Size.X / 2, screen.Size.Y);
            DisplayServer.WindowSetSize(size);
            DisplayServer.WindowSetPosition(screen.Position + new Vector2I(test.Window == "A" ? 0 : size.X, 0));
        }
        _server.Text = test.Server;
        _scenario.Select(Math.Max(0, ContentNames.Choices.ToList().IndexOf(test.Scenario)));
        _code.Text = test.Code ?? "";
        RefreshSaved();
        switch (test.Mode)
        {
            case "create":
                Start(c => c.CreateAsync(test.Scenario));
                break;
            case "join":
                Start(c => c.JoinAsync(test.Code!));
                break;
            default:
                var (seats, _) = SavedSeats.For(new SessionStore(SessionStore.DefaultDirectory()), test.Server);
                if (seats.FirstOrDefault(s => s.Code == test.Code) is { } saved)
                {
                    Start(c => c.RejoinSavedAsync(saved));
                }
                else
                {
                    _connectMessage.Text = $"Test match: no saved seat for match {test.Code} on {test.Server}.";
                }
                break;
        }
    }

    // F2 or the banner button: show the other seat (not while a command waits for its answer).
    private void SwitchSeat()
    {
        if (_hotSeat is not { } hot)
        {
            return;
        }
        if (!hot.Switch())
        {
            _status.Text = hot.SwitchBlocked() ?? "";
        }
    }

    // ---- match screen ----

    private void BuildMatch()
    {
        _match = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _match.AddThemeConstantOverride("separation", 8);
        _board = new BoardView();
        _board.LineShown += line =>
        {
            if (!line.Replayed && line.Sound is { } cue)
            {
                _sounds.Play(cue);
            }
        };
        // Proposal G3: one line on top (the HUD): this window's seat, whose move, turn, phase, VP, units left to activate,
        // the objectives of the last scoring; then the camera buttons and the Menu.
        // Battlefield UI 2, stage N3: the HUD is words over the battlefield with a dark outline, no panel behind them:
        // top-left where the match stands, top-centre what happens now (TopHud.Split; together they are the line the HUD
        // always had), and the corner icons at the top-right.
        _hud = Text("");
        _hud.Name = "TopHud";
        // Battlefield UI 3, stage U2: the centre is whose move and the scores only, with colour on those words; what
        // happens now (placing prompt, model activating, a command waiting) is a smaller line under the top-left block.
        _hudCentre = new RichTextLabel
        {
            Name = "TopHudCentre", BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _hudCentre.AddThemeConstantOverride("outline_size", 6);
        _hudCentre.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _hudCentre.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        _hudCentre.AddThemeConstantOverride("shadow_offset_x", 1);
        _hudCentre.AddThemeConstantOverride("shadow_offset_y", 2);
        _hudStatus = Text("");
        _hudStatus.Name = "TopHudStatus";
        _menuButton = new IconButton(IconButton.Symbol.Menu, "Menu: all choices, the match code, version, text size, high contrast and the rules in this build.");
        _menuButton.Name = "MenuButton";
        _menuButton.Pressed += () => SetMenu(!_menuOpen);
        _logButton = new IconButton(IconButton.Symbol.Log, "Combat log: what the server confirmed, newest at the bottom. Esc or this button closes it.");
        _logButton.Name = "LogButton";
        _logButton.Pressed += () => SetLog(!_drawerOpen);
        var keys = new IconButton(IconButton.Symbol.Keys, "Keys (?): every key and mouse use.");
        keys.Name = "KeysButton";
        keys.Pressed += ToggleKeySheet;
        // Proposal J3b: the scenario's scoring, endings and objectives (G).
        var scenario = new IconButton(IconButton.Symbol.Scenario, "Scenario (G): how Victory Points are scored, how the match ends, and who controls each objective now.");
        scenario.Name = "ScenarioButton";
        scenario.Pressed += ToggleScenario;
        var centre = new IconButton(IconButton.Symbol.Centre, "Centre (Home): centre the camera on the selected unit and keep the zoom.");
        centre.Name = "CentreButton";
        centre.Pressed += () => _board.CentreOn(_board.Selected?.Id);
        var fit = new IconButton(IconButton.Symbol.Fit, "Fit (F): show the whole table.");
        fit.Name = "FitButton";
        fit.Pressed += () => _board.Fit();
        _icons = new HBoxContainer { Name = "CornerIcons", SizeFlagsHorizontal = SizeFlags.ShrinkEnd, MouseFilter = Control.MouseFilterEnum.Ignore };
        _icons.AddThemeConstantOverride("separation", 4);
        foreach (var button in new Control[] { centre, fit, ResetViewButton(), scenario, keys, _logButton, _menuButton })
        {
            _icons.AddChild(button);
        }
        foreach (var label in new[] { _hud, _hudStatus })
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.SizeFlagsStretchRatio = 3;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            Outline(label);
        }
        var hudLeft = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3, MouseFilter = Control.MouseFilterEnum.Ignore };
        hudLeft.AddThemeConstantOverride("separation", 2);
        // Battlefield UI 3, stage U3: the connection indicator in the corner, before the HUD line; a click opens its
        // details card under it (the tooltip says the same).
        _signal = new SignalIcon();
        _signal.Pressed += () => SetSignalCard(!_signalCard.Visible);
        var hudTop = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        hudTop.AddThemeConstantOverride("separation", 6);
        hudTop.AddChild(_signal);
        hudTop.AddChild(_hud);
        hudLeft.AddChild(hudTop);
        _signalDetails = Text("");
        _signalDetails.Name = "ConnectionDetails";
        _signalCard = new PanelContainer { Name = "ConnectionCard", Visible = false, SizeFlagsHorizontal = SizeFlags.ShrinkBegin, CustomMinimumSize = new Vector2(320, 0) };
        _signalCard.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.09f, 0.95f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        _signalCard.AddChild(_signalDetails);
        hudLeft.AddChild(_hudStatus);
        var hudRight = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkEnd, MouseFilter = Control.MouseFilterEnum.Ignore };
        hudRight.AddThemeConstantOverride("separation", 4);
        hudRight.AddChild(_icons);
        var hudRow = Row(hudLeft, _hudCentre, hudRight);
        hudRow.MouseFilter = Control.MouseFilterEnum.Ignore;
        _hudStrip = new PanelContainer { Name = "HudStrip", MouseFilter = Control.MouseFilterEnum.Ignore };
        _hudStrip.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 4 });
        var stripRows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        stripRows.AddThemeConstantOverride("separation", 2);
        stripRows.AddChild(hudRow);
        _hudStrip.AddChild(stripRows);
        _board.SetTopStrip(_hudStrip);
        // What the Menu holds (folded by default): the version, settings and keys (moved here from the top), the match
        // code, the rules in this build, and the battlefield's camera and keys.
        // Stage K2: the Menu opens over the battlefield (under the HUD, at the right) instead of pushing it down; what does
        // not fit scrolls inside the Menu.
        _menuPanel = new PanelContainer { Name = "MenuPanel", Visible = false };
        var menuLook = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.12f, 1f), BorderColor = new Color(0.6f, 0.6f, 0.65f) };
        menuLook.SetBorderWidthAll(1);
        menuLook.SetContentMarginAll(10);
        _menuPanel.AddThemeStyleboxOverride("panel", menuLook);
        var menuScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _menuPanel.AddChild(menuScroll);
        _matchMenu = new VBoxContainer { Name = "MatchMenu", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _matchMenu.AddThemeConstantOverride("separation", 8);
        menuScroll.AddChild(_matchMenu);
        // Over the battlefield, as its child (not a top-level control, which Tab would skip).
        _board.AddChild(_menuPanel);
        _code2 = Text("");
        _matchMenu.AddChild(Row(_code2, Button("Copy code", () =>
        {
            if (_controller?.Snapshot().Code is { } code)
            {
                DisplayServer.ClipboardSet(code);
            }
        }), Button("Rules in this build (F1)", () => _gaps.PopupCentered()), Button("Keys (?)", ToggleKeySheet)));
        _boardInfo = Text("");
        _matchMenu.AddChild(Section("Battlefield keys", _boardInfo));
        // Hot-seat only: which seat this window controls, whose move it is, and the local server. Stage K2: in the Menu
        // (the HUD names the seat this window plays; Switch seat is a button beside the connection line).
        _hotLine = Text("");
        _matchMenu.AddChild(_hotLine);
        _matchMenu.MoveChild(_hotLine, 0);
        _follow = Button("Follow the turn: on", () =>
        {
            if (_hotSeat is { } hot)
            {
                hot.FollowTurn = !hot.FollowTurn;
                _shown = -1;
                System.Threading.Interlocked.Exchange(ref _changed, 1);
            }
        });
        _follow.Name = "FollowTurn";
        _follow.TooltipText = "Hot-seat: show the seat that must act next by itself.";
        _follow.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _follow.Visible = false;
        _matchMenu.AddChild(_follow);
        _matchMenu.MoveChild(_follow, 1);
        // Connection problems need attention, so they stay out of the Menu. Stage K2: the connection line and its buttons
        // share one row.
        _banner = Text("");
        _banner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _banner.SizeFlagsStretchRatio = 3;
        _banner.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        _bannerButtons = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2, Alignment = FlowContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
        _bannerButtons.AddThemeConstantOverride("h_separation", 6);
        _bannerButtons.AddThemeConstantOverride("v_separation", 4);
        // Stage K3: in the strip over the battlefield: the line under the HUD line, its buttons under the corner icons.
        Outline(_banner);
        _banner.Modulate = new Color(1, 1, 1, 0.85f);
        hudLeft.AddChild(_banner);
        hudLeft.AddChild(_signalCard);
        hudRight.AddChild(_bannerButtons);
        // Once the match has ended or been aborted: the result, the final state hash and the event count (M7.5). The
        // hash can be compared with the one `trench-cli verify` prints for the server's signed record.
        _end = Text("");
        var endBox = new VBoxContainer();
        endBox.AddChild(_end);
        // QoL I4: the match in numbers (from the server's events and final state), and a rematch.
        _summary = Text("");
        _summary.Name = "MatchSummary";
        endBox.AddChild(_summary);
        _rematch = Button("Rematch (same scenario, new code)", Rematch);
        _rematch.Name = "Rematch";
        _rematchNote = Text("");
        endBox.AddChild(Row(Button("Copy final hash", () =>
        {
            if (_finalHash is { } hash)
            {
                DisplayServer.ClipboardSet(hash);
            }
        }), _rematch));
        endBox.AddChild(_rematchNote);
        _endSection = Section("Result of the match", endBox);
        _endSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // Stage K2: the result is a box over the battlefield, under the HUD strip (not a section that pushes the
        // battlefield and the combat log down). It scrolls inside itself when the battlefield is too short for it.
        _resultBox = new PanelContainer { Name = "MatchResult", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _resultBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.04f, 0.04f, 0.06f, 0.94f), BorderColor = new Color(0.95f, 0.8f, 0.3f), BorderWidthBottom = 2, BorderWidthTop = 2, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 8 });
        _resultScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true, FocusMode = FocusModeEnum.None };
        _resultScroll.AddChild(_endSection);
        _resultBox.AddChild(_resultScroll);
        _board.AddChild(_resultBox);
        // Battlefield UI 2, stage N3: the battlefield fills the window. On it: the unit card at the bottom-left, the last
        // command's answer over it, a card for the choices that belong to no model in the middle, and the combat log as a
        // drawer on the right edge (the log icon). The full list of choices is in the Menu, as "All choices".
        BuildLog();
        _split = new BoxContainer { Name = "BattlefieldAndPanel", Vertical = false };
        _board.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _board.SizeFlagsVertical = SizeFlags.ExpandFill;
        _split.AddChild(_board);
        _board.AddChild(BuildPanel());
        _hints = new VBoxContainer();
        _hints.AddThemeConstantOverride("separation", 2);
        _notYet = Text("");
        var actions = new VBoxContainer { Name = "AllChoicesList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        actions.AddChild(_hints);
        actions.AddChild(_notYet);
        _actions = actions;
        _allChoices = Section("All choices", actions);
        _allChoices.Name = "AllChoices";
        _feedback = Text("");
        _feedback.Name = "LastCommand";
        // At most two lines; the whole answer is on its tooltip. It fades a few seconds after it changes.
        _feedback.MaxLinesVisible = 2;
        _feedback.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _feedback.MouseFilter = Control.MouseFilterEnum.Pass;
        _feedback.SizeFlagsHorizontal = SizeFlags.Fill;
        Outline(_feedback);
        _board.AddChild(_feedback);
        _decision = new PanelContainer { Name = "DecisionCard", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        var decisionLook = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.1f, 0.96f), BorderColor = new Color(0.95f, 0.8f, 0.3f) };
        decisionLook.SetBorderWidthAll(2);
        decisionLook.SetContentMarginAll(12);
        _decision.AddThemeStyleboxOverride("panel", decisionLook);
        var decisionColumn = new VBoxContainer();
        decisionColumn.AddThemeConstantOverride("separation", 6);
        _decisionTitle = Text("");
        _decisionTitle.Name = "DecisionTitle";
        _decisionTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _decisionButtons = new VBoxContainer { Name = "DecisionButtons" };
        _decisionButtons.AddThemeConstantOverride("separation", 4);
        decisionColumn.AddChild(_decisionTitle);
        decisionColumn.AddChild(_decisionButtons);
        _decision.AddChild(decisionColumn);
        _board.AddChild(_decision);
        var logBox = Section("Combat log", _logScroll);
        _logScroll.TooltipText = "What the server confirmed, newest at the bottom. Tab here, then arrows or Page Up/Down to scroll; End returns to the newest. Hover a row for its full line.";
        logBox.Name = "CombatLog";
        _logBox = logBox;
        logBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        logBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _logDrawer = new PanelContainer { Name = "LogDrawer", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        var drawerLook = new StyleBoxFlat { BgColor = new Color(0.05f, 0.05f, 0.07f, 0.86f), BorderColor = new Color(0.5f, 0.5f, 0.55f), BorderWidthLeft = 1 };
        drawerLook.SetContentMarginAll(8);
        _logDrawer.AddThemeStyleboxOverride("panel", drawerLook);
        _logDrawer.AddChild(logBox);
        _board.AddChild(_logDrawer);
        _board.Reserved = Reserved;
        _board.EscapeIdle += CloseOverlays;
        var boardBox = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _split.SizeFlagsVertical = SizeFlags.ExpandFill;
        boardBox.AddChild(_split);
        // All choices first in the Menu, so Tab reaches them first when it opens.
        _matchMenu.AddChild(_allChoices);
        _board.Changed += ShowBoardInfo;
        // Stage K4: the HUD says what placing does now; it follows the battlefield's choice (the model, or Esc).
        _board.Changed += () =>
        {
            if (_board.Interaction.Prompt != _shownPrompt && _lastScreen is not null)
            {
                ShowStrip();
            }
        };
        // QoL I2: the camera's angled or top-down choice is kept between launches too.
        _board.Changed += () =>
        {
            if (_board.TopDown != _settings.TopDown)
            {
                Remember(_settings with { TopDown = _board.TopDown });
            }
        };
        // A confirmed battlefield command goes the same way as a hint button: one at a time, answered by the server.
        _board.Interaction.Send = (command, label) => _controller is { } controller
            ? controller.SendAsync(command, label)
            : Task.FromResult(new Feedback(FeedbackKind.ProtocolRefused, "Not sent: not in a match."));
        _boardSection = boardBox;
        _match.AddChild(_boardSection);
        // Stage K2: the match in words and the list of models are in the Menu (the HUD and the panel say what the next
        // decision needs); nothing is below the battlefield.
        _state = Text("");
        _matchMenu.AddChild(Section("Match", _state));
        _models = Text("");
        _matchMenu.AddChild(Section("Models ([A] Seat One, [B] Seat Two)", _models));
        _matchMenu.MoveChild(_allChoices, 0);
        _root.AddChild(_match);
        _scaleNote = new Label
        {
            Name = "ScaleNote",
            Text = ScaleNoteText,
            TopLevel = true,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _scaleNote.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.8f), ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 3, ContentMarginBottom = 3 });
        _scaleNote.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.4f));
        _match.AddChild(_scaleNote);

        _gaps = new AcceptDialog { Title = RulesGaps.Title, DialogText = RulesGaps.Text, DialogAutowrap = true, Size = new Vector2I(760, 480) };
        AddChild(_gaps);
        // QoL I1: every key on one sheet; ? opens and closes it, Esc or OK closes it.
        // Stage K1: each key is a bold key cap apart from what it does, grouped by purpose in two columns (BuildKeySheet).
        _keySheet = new AcceptDialog { Title = KeySheet.Title };
        _keySheet.Name = "KeySheet";
        AddChild(_keySheet);
        // Proposal J3b: the Scenario card. Its words are the rules' own description (Rules.Core.ScenarioRules); a row per
        // objective names who controls it now and centres the camera on it when clicked; nothing is sent.
        _scenarioCard = new AcceptDialog { Title = ScenarioCard.Title, Size = new Vector2I(860, 600) };
        _scenarioCard.Name = "ScenarioCard";
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var scenarioColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scenarioColumn.AddThemeConstantOverride("separation", 6);
        _scenarioText = Text("");
        _scenarioRows = new VBoxContainer { Name = "ScenarioRows" };
        _scenarioPreview = Text("");
        _scenarioPreview.Name = "ScenarioPreview";
        _scenarioPreview.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 1f));
        scenarioColumn.AddChild(_scenarioText);
        scenarioColumn.AddChild(_scenarioRows);
        scenarioColumn.AddChild(_scenarioPreview);
        scroll.AddChild(scenarioColumn);
        _scenarioCard.AddChild(scroll);
        AddChild(_scenarioCard);
        _takeBack = new ConfirmationDialog
        {
            Title = "Take the seat back?",
            DialogText = "This seat was opened from another window. Taking it back closes it there. Take it back?",
            DialogAutowrap = true,
        };
        _takeBack.Confirmed += () => RejoinNow(true);
        AddChild(_takeBack);
    }

    private void BuildLog()
    {
        // One row per event (QoL stage D): a coloured tag naming its kind, then its line, word for word as before. The
        // newest row is marked, and so is the current turn's header.
        _log = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _log.AddThemeConstantOverride("separation", 0);
        // The event list takes focus (Tab), so it can be scrolled with the keyboard: arrows, Page Up/Down, Home, End.
        // The scroll bar is always shown: a bar that comes and goes changes the rows' width, which rewraps them, which can
        // bring the bar back, over and over (a layout loop that filled Godot's message queue once).
        _logScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 60), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways, FocusMode = FocusModeEnum.All };
        _logScroll.AddChild(_log);
        _log.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _logScroll.GuiInput += ScrollLogWithKeys;
        // New events scroll into view unless the player has scrolled up to read (End or scrolling to the bottom resumes).
        _logScroll.GetVScrollBar().Changed += () =>
        {
            if (_followLog)
            {
                _logScroll.ScrollVertical = (int)_logScroll.GetVScrollBar().MaxValue;
            }
        };
        // Dragging the bar: away from the bottom stops following, back at the bottom follows again (as the wheel does).
        _logScroll.GetVScrollBar().Scrolling += () =>
        {
            var bar = _logScroll.GetVScrollBar();
            _followLog = bar.Value + bar.Page >= bar.MaxValue - 2;
        };
    }

    private void Render(ScreenState s)
    {
        if (s.Version == _shown)
        {
            return;
        }
        _shown = s.Version;
        _lastScreen = s;
        ShowStrip();
        ShowPanel();
        var view = s.View;
        // The HUD says whose move it is; the line above stays for messages (a blocked hot-seat switch, a problem).
        _status.Text = "";
        _hotLine.Visible = _hotSeat is not null;
        _hotLine.Text = _hotSeat is { } h
            ? $"{h.Banner()}\nLocal server {h.Server?.Endpoint} (process {h.Server?.ProcessId}); it stops when you leave or close this window."
            : "";
        // Stage U3: the indicator in the corner says the connection; the line under the HUD keeps only what needs
        // attention: a connection that is not simply up (or the match ended), the opponent away, and a warning.
        var problem = s.Connection is ConnectionState.Connected or ConnectionState.Finished ? null : $"Connection: {s.ConnectionText}";
        _banner.Text = string.Join("\n", new[] { problem, s.OpponentAway, s.Warning is null ? null : "Warning: " + s.Warning }.Where(l => l is not null));
        _banner.Visible = _banner.Text.Length > 0;
        ShowSignal(s);

        // Buttons are rebuilt on every change; keep the keyboard focus on the same button (or a sensible one).
        var focused = GetViewport().GuiGetFocusOwner();
        var refocus = focused is Button b && (_bannerButtons.IsAncestorOf(b) || _hints.IsAncestorOf(b) || _decisionButtons.IsAncestorOf(b)) ? b.Text : null;
        var refocusDecision = focused is Button d && _decisionButtons.IsAncestorOf(d);
        foreach (var child in _bannerButtons.GetChildren())
        {
            child.QueueFree();
        }
        if (s.CanRejoinNow)
        {
            _bannerButtons.AddChild(s.RejoinNeedsConfirmation
                ? Button("Take the seat back…", () => _takeBack.PopupCentered())
                : Button("Rejoin now", () => RejoinNow(false)));
        }
        if (s.CanResync)
        {
            _bannerButtons.AddChild(Button("Resync (reconnect to fetch the latest events)", Resync));
        }
        if (_hotSeat is { } hot)
        {
            // Stage K3: short, so the strip over the battlefield stays small; "Follow the turn" is in the Menu.
            var switchSeat = Button("Switch seat (F2)", SwitchSeat);
            switchSeat.TooltipText = $"Show and play {Words.Seat(hot.OtherSeat)}.";
            _bannerButtons.AddChild(switchSeat);
        }
        _follow.Visible = _hotSeat is not null;
        _follow.Text = _hotSeat?.FollowTurn == false ? "Follow the turn: off" : "Follow the turn: on";
        _bannerButtons.AddChild(Button("Leave", Leave));

        _code2.Text = s.Code is null ? "" : $"Match code {s.Code}. You are {(s.Seat is { } seat ? $"{Words.Seat(seat)} [{Words.SeatLetter(seat)}]" : "not seated")}." +
            (_hotSeat is null && (view.Phase == Words.Phase(Rules.Core.State.Phase.PreGame) || view.Models.Count == 0) ? " Give the code to your opponent." : "");
        _resultBox.Visible = s.End is not null;
        _finalHash = s.End?.Hash;
        _end.Text = s.End is { } end ? string.Join("\n", end.Lines()) : "";
        _summary.Text = s.End?.Summary is { } summary ? string.Join("\n", summary.Lines()) : "";
        _summary.Visible = _summary.Text.Length > 0;
        _rematch.Visible = CanRematch;
        _rematchNote.Text = s.End is { Aborted: false } && _createdScenario is null && _test is null
            ? "Rematch: the window that created this match can start one and give you its code." : "";
        _rematchNote.Visible = _rematchNote.Text.Length > 0;
        _title = _test is { } titled ? titled.Title(s.Seat) : _baseTitle;
        UpdateTurnAlert();
        if (_test is { } test)
        {
            WindowTitle = _turnAlert.Title(test.Title(s.Seat));
            DisplayServer.WindowSetTitle(WindowTitle);
            if (test.Report is { } report && s.End is { } ended && !_testReported)
            {
                _testReported = true;
                try
                {
                    TestMatch.WriteReport(report, TestMatch.ReportText(s.Code, s.Seat, ended));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Print($"Test match: could not write {report}: {e.Message}");
                }
            }
        }
        _boardSection.Visible = s.Board is not null;
        _board.SeatTag = _hotSeat is { } seated ? Words.SeatLetter(seated.Active) : null;
        _board.Show(s.Board, s.State, s.Seat, s.Log);
        // Stage N5: whenever the seat shown changes, a banner says who acts now (from the server's state of that seat).
        if (_turnBannerDue && _hotSeat is { } turned && s.Seat == turned.Active)
        {
            _turnBannerDue = false;
            TurnBannersShown++;
            _board.ShowTurnBanner(HotSeat.TurnBanner(s.State, turned.Active), HotSeat.BannerSeconds);
        }
        _state.Text = string.Join("\n", view.Lines());
        _models.Text = view.Models.Count == 0 ? "No models yet." : string.Join("\n", view.Models.Select(m => m.Text));

        foreach (var child in _hints.GetChildren().Concat(_decisionButtons.GetChildren()))
        {
            child.QueueFree();
        }
        // Stage N3: the choices that belong to no model also on a card over the battlefield (the same buttons).
        var decisions = s.Hints.Where(h => ActionHints.IsDecision(h.Command)).ToList();
        _decision.Visible = decisions.Count > 0;
        _decisionTitle.Text = ActionHints.DecisionTitle(decisions.Select(h => h.Command));
        foreach (var hint in decisions)
        {
            var chosen = hint;
            var button = Button(chosen.Label, () => Send(chosen));
            button.TooltipText = $"{chosen.Label}\nA choice the rules offer now; the server accepts or refuses it.";
            button.SetMeta("hint", true);
            button.SetMeta("decision", true);
            _decisionButtons.AddChild(button);
        }
        foreach (var hint in s.Hints)
        {
            var chosen = hint;
            // A choice made in the Menu's list closes the Menu (the battlefield shows what follows).
            var button = Button(chosen.Label, () =>
            {
                Send(chosen);
                SetMenu(false);
            });
            // Stage K2: short words; the tooltip says where the choice comes from.
            button.TooltipText = $"{chosen.Label}\nA choice the rules offer now; the server accepts or refuses it.";
            button.SetMeta("hint", true);
            // Stage K2: one line each; a long label ends in "…" (its tooltip has the whole of it), so the buttons never
            // push anything off the window.
            button.ClipText = true;
            button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            button.Alignment = HorizontalAlignment.Left;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _hints.AddChild(button);
        }
        if (s.Hints.Count == 0)
        {
            _hints.AddChild(Text(s.InputLocked is { } locked ? $"Nothing to send: {locked}" : view.YourMove ? "No choice to make here." : "Nothing to do until the opponent moves."));
        }
        _notYet.Text = s.NotYetOffered ?? "";
        _notYet.Visible = _notYet.Text.Length > 0;
        _feedback.Text = s.Feedback is { } f ? $"{FeedbackWord(f.Kind)}: {f.Text}{(f.RuleRef is null ? "" : $" (rule: {f.RuleRef})")}" : "None yet.";
        _feedback.TooltipText = _feedback.Text;
        if (_feedback.Text != _feedbackShown)
        {
            _feedbackShown = _feedback.Text;
            _feedbackSince = Time.GetTicksMsec() / 1000.0;
        }
        ShowLog(s.Log);
        if (refocus is not null)
        {
            var same = (refocusDecision ? _decisionButtons.GetChildren() : _bannerButtons.GetChildren().Concat(_hints.GetChildren())).OfType<Button>().FirstOrDefault(n => !n.IsQueuedForDeletion() && n.Text == refocus);
            if (same is not null)
            {
                same.GrabFocus();
            }
            else
            {
                FocusDefault();
            }
        }
    }

    private string? _shownPrompt;

    private void ShowStrip()
    {
        _shownPrompt = _board.Interaction.Prompt;
        RefreshScenario();
        var waiting = _lastScreen?.Feedback is { Kind: FeedbackKind.Sending } || _board.Interaction.Pending is not null;
        _hudText = _lastScreen is { } s ? TopHud.Of(s.View, _board.Scene, s.Log, s.Seat, waiting, _board.Interaction.Prompt) : "";
        var places = _lastScreen is { } t ? TopHud.Split(t.View, _board.Scene, t.Log, t.Seat, waiting, _board.Interaction.Prompt) : new TopHud.Places("", "", "");
        var plain = _highContrast ? Colors.White : new Color(0.92f, 0.92f, 0.9f);
        _hud.Text = places.Left;
        _hud.AddThemeColorOverride("font_color", plain);
        _hudStatus.Text = places.Status;
        _hudStatus.Visible = places.Status.Length > 0;
        _hudStatus.AddThemeColorOverride("font_color", _highContrast ? Colors.White : new Color(0.8f, 0.8f, 0.78f));
        _hudStatus.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(StatusFontSize()));
        _hudCentreText = places.Centre;
        _hudCentre.Text = CentreBbcode(places.Centre, plain);
    }

    // Stage U3: the indicator's bars, tooltip and details card from the controller's screen (the shown seat's, in
    // hot-seat).
    private void ShowSignal(ScreenState s)
    {
        _signal.Bars = ConnectionQuality.Bars(s.Connection, s.RoundTrips ?? RoundTrips.None);
        _signal.HighContrast = _highContrast;
        _signal.Visible = true;
        var details = _controller is { } c ? ConnectionQuality.Details(s, c.Endpoint.ToString(), c.Now) : [];
        _signalDetailsText = string.Join("\n", details);
        _signal.TooltipText = _signalDetailsText + "\nClick for these details on a card.";
        _signalDetails.Text = _signalDetailsText;
    }

    private string _signalDetailsText = "";

    private void SetSignalCard(bool open)
    {
        _signalCard.Visible = open;
        if (open && _lastScreen is { } s)
        {
            ShowSignal(s);
        }
    }

    public SignalIcon ConnectionIcon => _signal;

    public Control ConnectionCard => _signalCard;

    public string ConnectionDetailsText => _signalDetailsText;

    public string ConnectionLine => _banner.Visible ? _banner.Text : "";

    // The status line is a step smaller than the HUD's own text.
    private float StatusFontSize() => (_theme.DefaultFontSize > 0 ? _theme.DefaultFontSize : 16) * 0.85f;

    // Stage U2: colour on the parts that matter, the words unchanged: whose move (green when yours, amber when the
    // opponent's) and each seat's letter in its seat colour.
    private string CentreBbcode(string centre, Color plain)
    {
        static string Esc(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);
        static string Paint(string text, Color colour) => $"[color=#{colour.ToHtml(false)}]{Esc(text)}[/color]";
        var parts = new List<string>();
        foreach (var piece in centre.Length == 0 ? [] : centre.Split(" · "))
        {
            if (piece == "YOUR MOVE")
            {
                parts.Add(Paint(piece, _highContrast ? Colors.Yellow : new Color(0.6f, 1f, 0.65f)));
            }
            else if (piece == "OPPONENT'S MOVE")
            {
                parts.Add(Paint(piece, _highContrast ? Colors.White : new Color(1f, 0.75f, 0.3f)));
            }
            else if (piece.StartsWith("VP ", StringComparison.Ordinal))
            {
                // "VP A 3 – B 1": the letters in their seat colours.
                var words = piece.Split(' ');
                parts.Add(string.Join(" ", words.Select(w => w switch
                {
                    "A" => Paint(w, _board.SeatColourFor(Rules.Core.State.Seat.One)),
                    "B" => Paint(w, _board.SeatColourFor(Rules.Core.State.Seat.Two)),
                    _ => Paint(w, plain),
                })));
            }
            else
            {
                parts.Add(Paint(piece, plain));
            }
        }
        return $"[center][b]{string.Join(Paint("  ·  ", plain), parts)}[/b][/center]";
    }

    private string _hudCentreText = "";

    // The centre's words (stage U2), without the colour marks.
    public string HudCentreText => _hudCentreText;

    public string HudStatusText => _hudStatus.Visible ? _hudStatus.Text : "";

    // ---- the Menu (proposal G3) ----

    private void SetMenu(bool open)
    {
        _menuOpen = open;
        // Closing the Menu never leaves the keyboard focus on a hidden control: it goes back to the Menu button.
        if (!open && GetViewport()?.GuiGetFocusOwner() is { } focused && _matchMenu.IsAncestorOf(focused))
        {
            _menuButton.GrabFocus();
        }
        if (open)
        {
            // Stage N4: one of the Menu, the log drawer, the key sheet and the scenario card open at a time.
            CloseOthers(_menuPanel);
        }
        _menuPanel.Visible = open;
        if (open)
        {
            // Drawn over the dice, and placed before it shows (clear of the battlefield's controls, stage N4).
            _menuPanel.MoveToFront();
            PlaceMenu(GetViewportRect().Size);
            // Stage N3: opened from its button (mouse or keyboard), the keyboard focus goes to the Menu's first control
            // (the first of All choices), so Tab walks the Menu from its top.
            if (GetViewport()?.GuiGetFocusOwner() is not { } owner || owner == _menuButton || !owner.IsVisibleInTree())
            {
                FirstFocusable(_matchMenu)?.GrabFocus();
            }
        }
        _menuButton.Lit = open;
    }

    private static Control? FirstFocusable(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Control { Visible: false } || child.IsQueuedForDeletion())
            {
                continue;
            }
            if (child is Control { FocusMode: not FocusModeEnum.None } control)
            {
                return control;
            }
            if (FirstFocusable(child) is { } inner)
            {
                return inner;
            }
        }
        return null;
    }

    // Stage N3 (for the UI check): a control in the Menu while it is closed, the Menu's choices, and opening it as its
    // button does.
    public bool InClosedMenu(Node node) => !_menuOpen && InMatchMenu(node);

    public IReadOnlyList<Button> MenuChoices => [.. _hints.GetChildren().OfType<Button>().Where(b => !b.IsQueuedForDeletion())];

    private void MoveMenu(Node parent, int index, bool visible)
    {
        if (_menuBox.GetParent() != parent)
        {
            _menuBox.GetParent()?.RemoveChild(_menuBox);
            parent.AddChild(_menuBox);
        }
        parent.MoveChild(_menuBox, index);
        _menuBox.Visible = visible;
    }

    public bool MenuOpen => _menuOpen;

    public Button MenuButton => _menuButton;

    // Whether a control sits in the match screen's Menu (shown or folded), for the UI check.
    public bool InMatchMenu(Node node) => _match.IsVisibleInTree() && _matchMenu.IsAncestorOf(node);

    public string BoardInfoText => _boardInfo.Text;

    // The layout (for the UI check): the battlefield and panel row, the combat log, and how far down the page the row starts.
    public Control BattlefieldRow => _split;

    public Control CombatLog => _logBox;

    public float BattlefieldTop => _boardSection.GlobalPosition.Y - _root.GlobalPosition.Y;

    // The battlefield fills the window below the HUD (and any banner); the rest of the page is below the fold. Under
    // StackBelow pixels of width (proposal G5) the panel and the log move under the battlefield, side by side, so the
    // battlefield keeps the full width and Confirm and Cancel stay on screen.
    private void FitBattlefield()
    {
        // Stage N3: in a match the page has no margin, so the battlefield reaches the window's edges.
        var margin = PageMargin;
        if (!Mathf.IsEqualApprox(_page.OffsetTop, margin.Y) || !Mathf.IsEqualApprox(_page.OffsetLeft, margin.X))
        {
            _page.OffsetLeft = margin.X;
            _page.OffsetTop = margin.Y;
            _page.OffsetRight = -margin.X;
            _page.OffsetBottom = -margin.Y;
        }
        if (!_match.Visible || _split is null)
        {
            return;
        }
        var size = GetViewportRect().Size;
        // Stage K2: the battlefield takes exactly the window's height under anything above it (no minimum that would push
        // anything below the window; the page does not scroll in a match). Stage N3: and the whole width.
        var top = _boardSection.GlobalPosition.Y - _root.GlobalPosition.Y;
        var want = Math.Max(0f, size.Y - 2 * margin.Y - top);
        if (Math.Abs(_split.CustomMinimumSize.Y - want) > 1)
        {
            _split.CustomMinimumSize = new Vector2(0, want);
        }
        PlaceOverlays();
        PlaceResult();
        PlaceMenu(size);
    }

    // Stage N3: what lies on the battlefield, in its own coordinates. The unit card at the bottom-left (as tall as its
    // words, at most a little over half the battlefield; more scrolls inside it, Confirm and Cancel stay at its foot),
    // the last command's answer just over it, the log drawer along the right edge under the HUD, and the card of
    // choices in the middle.
    private void PlaceOverlays()
    {
        var b = _board.Size;
        if (b.X < 10 || b.Y < 10)
        {
            return;
        }
        var inset = _board.TopInset;
        var cardWidth = Math.Min(380 * _scale, Math.Max(220, b.X * 0.42f));
        var natural = _pColumn.GetCombinedMinimumSize().Y + _pBody.GetCombinedMinimumSize().Y + 24;
        var room = Math.Max(120, b.Y - inset - 16);
        var cardHeight = Math.Min(natural, Math.Max(_pColumn.GetCombinedMinimumSize().Y + 60 * _scale, room * 0.58f));
        cardHeight = Math.Min(cardHeight, room);
        Put(_panel, new Rect2(8, b.Y - cardHeight - 8, cardWidth, cardHeight));
        var drawerWidth = Math.Min(440 * _scale, Math.Max(240, b.X * 0.4f));
        if (_logDrawer.Visible)
        {
            Put(_logDrawer, new Rect2(b.X - drawerWidth - 8, inset + 4, drawerWidth, Math.Max(80, b.Y - inset - 12)));
        }
        _board.Dice.RightInset = _logDrawer.Visible ? drawerWidth + 8 : 0;
        // The last command: just over the card, as wide as it; it fades a few seconds after it changes.
        _feedback.Size = new Vector2(cardWidth, 0);
        _feedback.ResetSize();
        _feedback.Size = new Vector2(cardWidth, _feedback.GetCombinedMinimumSize().Y);
        _feedback.Position = new Vector2(8, Math.Max(inset + 4, b.Y - cardHeight - 12 - _feedback.Size.Y));
        var age = Time.GetTicksMsec() / 1000.0 - _feedbackSince;
        _feedback.Modulate = new Color(1, 1, 1, (float)Math.Clamp(1 - (age - FeedbackSeconds) / 1.5, 0.0, 1.0));
        if (_decision.Visible)
        {
            // The title wraps at the card's width (fixed first: a wrapping label's height depends on its width, so the
            // card is never measured narrow and tall).
            // Centred in the room the log drawer leaves, when it is open.
            var free = _logDrawer.Visible ? Math.Max(120, b.X - drawerWidth - 16) : b.X;
            var width = Math.Min(460 * _scale, free - 16);
            var titleWidth = Math.Max(40, width - 30 * _scale);
            if (!Mathf.IsEqualApprox(_decisionTitle.CustomMinimumSize.X, titleWidth))
            {
                _decisionTitle.CustomMinimumSize = new Vector2(titleWidth, 0);
            }
            var d = _decision.GetCombinedMinimumSize();
            width = Math.Min(Math.Max(width, d.X), free - 16);
            var height = Math.Min(d.Y, Math.Max(60, b.Y - inset - 16));
            Put(_decision, new Rect2(Math.Max(8, (free - width) / 2), Math.Max(inset + 8, (b.Y - height) / 2 - 40), width, height));
        }
    }

    // How long the last command's line stays before it fades (it comes back when the answer changes).
    public const double FeedbackSeconds = 6;

    private static void Put(Control control, Rect2 rect)
    {
        if (control.Position != rect.Position || control.Size != rect.Size)
        {
            control.Position = rect.Position;
            control.Size = rect.Size;
        }
    }

    // Stage N3: where the battlefield's own bars and boxes must not go (in its coordinates): the unit card, the last
    // command, the choices card, the log drawer and the result.
    private IEnumerable<Rect2> Reserved()
    {
        foreach (var control in new Control[] { _panel, _feedback, _decision, _logDrawer, _resultBox })
        {
            if (control.Visible && control.GetParent() == _board)
            {
                yield return new Rect2(control.Position, control.Size);
            }
        }
    }

    // Stage N3: the combat log drawer, opened and closed by its icon (and Esc).
    private void SetLog(bool open)
    {
        if (open)
        {
            CloseOthers(_logDrawer);
        }
        _drawerOpen = open;
        if (!open && GetViewport()?.GuiGetFocusOwner() is { } focused && _logDrawer.IsAncestorOf(focused))
        {
            _logButton.GrabFocus();
        }
        _logDrawer.Visible = open;
        _logButton.Lit = open;
        if (open)
        {
            _logDrawer.MoveToFront();
            PlaceOverlays();
        }
    }

    public bool LogOpen => _drawerOpen;

    public void OpenLog(bool open) => SetLog(open);

    public IconButton LogButton => _logButton;

    public Control LogDrawer => _logDrawer;

    public Control DecisionCard => _decision;

    public HBoxContainer CornerIcons => _icons;

    public Control HudCentre => _hudCentre;

    // Esc with nothing being chosen on the battlefield closes the key sheet or scenario card, then the log drawer, then
    // the Menu.
    private void CloseOverlays()
    {
        // The key sheet or the scenario card first (Esc closes them also when the keyboard is on the main window).
        if (_keySheet.Visible || _scenarioCard.Visible)
        {
            _keySheet.Hide();
            _scenarioCard.Hide();
        }
        else if (_drawerOpen)
        {
            SetLog(false);
        }
        else if (_menuOpen)
        {
            SetMenu(false);
        }
    }

    // Stage N4: opening one of the Menu, the log drawer, the key sheet and the scenario card closes the others.
    private void CloseOthers(GodotObject opening)
    {
        if (opening != _menuPanel && _menuOpen)
        {
            SetMenu(false);
        }
        if (opening != _logDrawer && _drawerOpen)
        {
            SetLog(false);
        }
        if (opening != _keySheet && _keySheet.Visible)
        {
            _keySheet.Hide();
        }
        if (opening != _scenarioCard && _scenarioCard.Visible)
        {
            _scenarioCard.Hide();
        }
    }

    // Stage N4 (for the UI check): how many of the Menu, the log drawer, the key sheet and the scenario card are open.
    public int OpenPanels => (_menuOpen ? 1 : 0) + (_drawerOpen ? 1 : 0) + (_keySheet.Visible ? 1 : 0) + (_scenarioCard.Visible ? 1 : 0);

    // Words over the battlefield stay readable on any ground: a dark outline and shadow around the letters.
    private static void Outline(Label label)
    {
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
    }

    // The Menu, when open, under the HUD row, as tall as its contents or the room left. Stage N4: it never covers the
    // battlefield's controls (the action bar, the combat menu, Confirm beside a target), the unit card or the decision
    // card: it takes the right-hand column under the corner icons, else the left-hand or the middle one, in the tallest
    // free space; what does not fit scrolls inside it. A place still free is kept, so it does not jump while open.
    private void PlaceMenu(Vector2 size)
    {
        if (!_menuPanel.Visible)
        {
            return;
        }
        var b = _board.Size;
        var width = Math.Min(680 * _scale, b.X - 16);
        var top = _menuButton.GetGlobalRect().End.Y + 4 - _board.GlobalPosition.Y;
        var bottom = Math.Min(b.Y, size.Y - _board.GlobalPosition.Y) - 8;
        var want = _matchMenu.GetCombinedMinimumSize().Y + 24;
        var avoid = MenuAvoids().ToList();
        // The first column whose tallest free span holds the Menu (or 160 px of it), else the tallest span of all.
        Rect2? best = null;
        foreach (var x in new[] { b.X - 8 - width, 8f, (b.X - width) / 2 })
        {
            var gaps = FreeGaps(x, width, top, bottom, avoid).ToList();
            if (gaps.Count == 0)
            {
                continue;
            }
            var gap = gaps[0];
            var rect = new Rect2(x, gap.From, width, Math.Min(want, gap.To - gap.From));
            if (rect.Size.Y >= Math.Min(want, 160 * _scale))
            {
                best = rect;
                break;
            }
            if (best is not { } tallest || rect.Size.Y > tallest.Size.Y)
            {
                best = rect;
            }
        }
        var place = best ?? new Rect2(b.X - 8 - width, top, width, Math.Max(80, bottom - top));
        // Keep the place it has while it is still free and still as tall.
        var now = new Rect2(_menuPanel.Position, _menuPanel.Size);
        if (Mathf.IsEqualApprox(now.Size.X, place.Size.X) && Mathf.IsEqualApprox(now.Size.Y, place.Size.Y) && now.Position.Y >= top - 0.5f && now.End.Y <= bottom + 0.5f
            && !avoid.Any(r => r.Intersects(now)))
        {
            return;
        }
        Put(_menuPanel, place);
    }

    // What the Menu keeps clear of, in the battlefield's coordinates.
    private IEnumerable<Rect2> MenuAvoids()
    {
        foreach (var rect in _board.ControlRects())
        {
            yield return rect.Grow(4);
        }
        foreach (var control in new Control[] { _panel, _decision })
        {
            if (control.Visible && control.GetParent() == _board)
            {
                yield return new Rect2(control.Position, control.Size).Grow(4);
            }
        }
    }

    // The free spans of a column from top to bottom between the rectangles that cross it, tallest first.
    private static IEnumerable<(float From, float To)> FreeGaps(float x, float width, float top, float bottom, IReadOnlyList<Rect2> avoid)
    {
        var blocks = avoid.Where(r => r.Position.X < x + width && r.End.X > x && r.End.Y > top && r.Position.Y < bottom)
            .Select(r => (From: Math.Max(top, r.Position.Y), To: Math.Min(bottom, r.End.Y))).OrderBy(r => r.From).ToList();
        var gaps = new List<(float From, float To)>();
        var at = top;
        foreach (var (from, to) in blocks)
        {
            if (from > at)
            {
                gaps.Add((at, from));
            }
            at = Math.Max(at, to);
        }
        if (bottom > at)
        {
            gaps.Add((at, bottom));
        }
        return gaps.Where(g => g.To - g.From >= 40).OrderByDescending(g => g.To - g.From);
    }

    // Stage N4 (for the UI check): what the open Menu must not cover.
    public IReadOnlyList<(string Name, Rect2 Rect)> MenuMustNotCover()
    {
        var list = new List<(string, Rect2)>();
        foreach (var (name, control) in new (string, Control)[] { ("unit card", _panel), ("decision card", _decision), ("Confirm", _pConfirm), ("Cancel", _pCancel) })
        {
            if (control.IsVisibleInTree())
            {
                list.Add((name, control.GetGlobalRect()));
            }
        }
        foreach (var rect in _board.ControlRects())
        {
            list.Add(("battlefield control", new Rect2(rect.Position + _board.GlobalPosition, rect.Size)));
        }
        return list;
    }

    public Control MenuPanel => _menuPanel;


    // How far down the page a control starts (whatever the page's scroll), for the UI check.
    public float PageY(Control control) => control.GlobalPosition.Y - _root.GlobalPosition.Y;


    // Stage K2 (for the UI check): how tall the match screen needs to be, and the last command's line.
    public float MatchHeight => _match.GlobalPosition.Y + _match.GetCombinedMinimumSize().Y + PageMargin.Y;

    // The page's margin around the screen: none in a match (stage N3), 16 by 12 pixels elsewhere.
    public Vector2 PageMargin => _match is not null && _match.Visible ? Vector2.Zero : new Vector2(16, 12);

    public Control LastCommandLabel => _feedback;
    public string LastCommandText => _feedback.Text;

    public bool InMatch => _match.IsVisibleInTree();

    public float CurrentScale => _scale;

    // Stage K2: the result box over the battlefield, under the HUD strip, as tall as its words or the space left. Stage
    // N3: compact, at the top-centre.
    private void PlaceResult()
    {
        if (!_resultBox.Visible)
        {
            return;
        }
        var width = Math.Min(Math.Max(120f, _board.Size.X - 16), 620 * _scale);
        // Stage N3: compact, at most 60% of the room under the HUD (at least 160 px); the rest scrolls inside it.
        var room = Math.Min(Math.Max(60f, _board.Size.Y - _board.TopInset - 16), Math.Max(160 * _scale, (_board.Size.Y - _board.TopInset - 16) * 0.6f));
        _resultScroll.CustomMinimumSize = new Vector2(0, Math.Min(_endSection.GetCombinedMinimumSize().Y, room - 14));
        var size = new Vector2(width, Math.Min(_resultBox.GetCombinedMinimumSize().Y, room));
        var at = new Vector2((_board.Size.X - width) / 2, _board.TopInset + 8);
        if (_resultBox.Position != at || _resultBox.Size != size)
        {
            _resultBox.Position = at;
            _resultBox.Size = size;
        }
    }

    public Control ResultBox => _resultBox;

    // Stage N3: the list of all choices, in the Menu (for the UI check).
    public Control ActionsBox => _allChoices;

    // ---- the selected-unit and action panel (proposal G, step G1) ----

    private PanelContainer _panel = null!;
    private VBoxContainer _pColumn = null!;
    private VBoxContainer _pBody = null!;
    private Label _pTitle = null!;
    private Label _pUnit = null!;
    private Label _pUnavailable = null!;
    private Label _pActionTitle = null!;
    private Label _pAction = null!;
    private Label _pRefusal = null!;
    private Label _pNotice = null!;
    private Label _pPointing = null!;
    private Label _pKeys = null!;
    private Button _pDetailsButton = null!;
    private Label _pDetails = null!;
    private Button _pConfirm = null!;
    private Button _pCancel = null!;
    private Button _pUndo = null!;
    private bool _detailsOpen;
    private UnitPanelView? _panelView;

    private Control BuildPanel()
    {
        // Stage N3: the unit card, over the battlefield's bottom-left corner.
        _panel = new PanelContainer { Name = "UnitPanel", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.12f, 0.15f, 0.93f),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(column);
        _pColumn = column;
        // The words scroll on their own; Confirm and Cancel stay at the bottom, always in the same place.
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways, SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(body);
        _pBody = body;
        _pTitle = Text("");
        _pTitle.Name = "PanelTitle";
        _pUnit = Text("");
        _pUnavailable = Text("");
        _pActionTitle = Text("");
        _pAction = Text("");
        _pRefusal = Text("");
        _pNotice = Text("");
        _pPointing = Text("");
        _pPointing.Name = "PanelPointing";
        _pDetails = Text("");
        _pDetails.Visible = false;
        _pDetailsButton = Button("Details ▸", () =>
        {
            // Opening or closing the details changes nothing else: not the selection, the action or its preview.
            _detailsOpen = !_detailsOpen;
            ShowPanel();
        });
        _pDetailsButton.Alignment = HorizontalAlignment.Left;
        foreach (var control in new Control[] { _pTitle, _pUnit, _pUnavailable, _pActionTitle, _pAction, _pRefusal, _pPointing, _pNotice, _pDetails })
        {
            body.AddChild(control);
        }
        _pConfirm = Button("Confirm", () => _board.Confirm());
        _pConfirm.Name = "PanelConfirm";
        _pCancel = Button("Cancel", () => _board.CancelPreview(all: true));
        _pCancel.Name = "PanelCancel";
        _pConfirm.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _pCancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _pUndo = Button("Undo", () => _board.UndoPoint());
        _pUndo.Name = "PanelUndo";
        _pUndo.TooltipText = "Remove the last waypoint (key Backspace). Shift+click adds one.";
        // Buttons whose size never depends on their text (Confirm twice as wide), so Confirm and Cancel never move.
        foreach (var button in new[] { _pConfirm, _pUndo, _pCancel })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.ClipText = true;
        }
        _pConfirm.SizeFlagsStretchRatio = 2;
        // Details ▸ stays in view above the buttons however long the panel's words are; what it opens scrolls with them.
        column.AddChild(_pDetailsButton);
        column.AddChild(Row(_pConfirm, _pUndo, _pCancel));
        // QoL H5: the same keys for every action, under its buttons.
        _pKeys = Text(UnitPanelView.KeysLine);
        _pKeys.Name = "PanelKeys";
        _pKeys.Modulate = new Color(1, 1, 1, 0.7f);
        column.AddChild(_pKeys);
        return _panel;
    }

    private void ShowPanel()
    {
        if (_controller is null || _lastScreen is null)
        {
            return;
        }
        var view = UnitPanel.Of(_controller.Snapshot().State, _board.Scene, _board.Interaction, _lastScreen.Seat, _board.Selected, _board.Hovered,
            _board.SelectedObjective, TopHud.LastScoring(_lastScreen.Log));
        _panelView = view;
        var scale = _scale;
        _pTitle.Text = view.Title;
        _pTitle.AddThemeFontSizeOverride("font_size", (int)(22 * scale));
        _pUnit.Text = string.Join("\n", view.Unit);
        _pUnavailable.Text = view.Unavailable ?? "";
        _pUnavailable.Visible = view.Unavailable is not null;
        _pUnavailable.Modulate = new Color(1, 1, 1, 0.7f);
        _pActionTitle.Text = view.ActionTitle ?? "";
        _pActionTitle.Visible = view.ActionTitle is not null;
        _pActionTitle.AddThemeFontSizeOverride("font_size", (int)(20 * scale));
        _pActionTitle.AddThemeColorOverride("font_color", _highContrast ? Colors.Yellow : new Color(0.55f, 0.85f, 1f));
        _pAction.Text = string.Join("\n", view.Action);
        _pAction.Visible = view.Action.Count > 0;
        _pRefusal.Text = view.Refusal ?? "";
        _pRefusal.Visible = view.Refusal is not null;
        _pRefusal.AddThemeColorOverride("font_color", _highContrast ? Colors.Yellow : new Color(1f, 0.55f, 0.45f));
        _pPointing.Text = string.Join("\n", view.Pointing);
        _pPointing.Visible = view.Pointing.Count > 0;
        _pPointing.Modulate = new Color(1, 1, 1, 0.85f);
        _pNotice.Text = view.Notice ?? "";
        _pNotice.Visible = view.Notice is not null;
        _pDetailsButton.Visible = view.Details.Count > 0;
        _pDetailsButton.Text = _detailsOpen ? "Details ▾" : "Details ▸";
        _pDetails.Text = string.Join("\n", view.Details);
        _pDetails.Visible = _detailsOpen && view.Details.Count > 0;
        _pConfirm.Text = view.ConfirmLabel;
        _pConfirm.Disabled = !view.CanConfirm;
        _pCancel.Disabled = !view.CanCancel;
        _pUndo.Disabled = !view.CanUndo;
        _pKeys.Visible = view.CanCancel;
        ((StyleBoxFlat)_panel.GetThemeStylebox("panel")).BgColor = _highContrast ? new Color(0.02f, 0.02f, 0.02f, 0.97f) : new Color(0.12f, 0.12f, 0.15f, 0.93f);
    }

    // The panel as shown (for the UI check): every visible line, and its buttons.
    public UnitPanelView? PanelView => _panelView;

    public string PanelText => string.Join("\n", new[] { _pTitle, _pUnit, _pUnavailable, _pActionTitle, _pAction, _pRefusal, _pPointing, _pNotice, _pDetails }
        .Where(l => l.IsVisibleInTree()).Select(l => l.Text));

    public bool DetailsOpen => _detailsOpen;

    public void ToggleDetails() => _pDetailsButton.EmitSignal(BaseButton.SignalName.Pressed);

    public Button PanelConfirm => _pConfirm;

    public Button PanelCancel => _pCancel;

    public Button PanelUndo => _pUndo;

    public string PanelKeysText => _pKeys.IsVisibleInTree() ? _pKeys.Text : "";

    // The status line as shown (for the UI check).
    public string HudText => _hudText;

    private string _hudText = "";

    // The server's state as this window last drew it (for the UI check: the same render as the HUD and battlefield).
    public Rules.Core.State.MatchState? ServerState => _lastScreen?.State;

    public ScreenState? LastScreen => _lastScreen;

    // The connection in the words the line under the HUD used before stage U3 ("Connection: Connected." and the
    // opponent away and a warning on their own lines), now on the indicator's details; the UI checks wait on it.
    public string ConnectionWords => _lastScreen is { } s
        ? $"Connection: {s.ConnectionText}{(s.OpponentAway is null ? "" : "\n" + s.OpponentAway)}{(s.Warning is null ? "" : "\nWarning: " + s.Warning)}"
        : "";

    public Rules.Core.State.Seat? ScreenSeat => _lastScreen?.Seat;

    // The panel's "POINTING AT" lines as shown (for the UI check); empty when hidden.
    public string PointingText => _pPointing.IsVisibleInTree() ? _pPointing.Text : "";

    // QoL H2: angled, facing north, the whole table (as Shift+F).
    private IconButton ResetViewButton()
    {
        var reset = new IconButton(IconButton.Symbol.Reset, "Reset view (Shift+F): angled view, facing north, the whole table. Double-click a unit or objective to centre on it; double-click again to zoom in.");
        reset.Name = "ResetView";
        reset.Pressed += () => _board.ResetView();
        return reset;
    }

    private void ShowBoardInfo()
    {
        ShowStrip();
        ShowPanel();
        if (_board.Scene is not { } scene)
        {
            _boardInfo.Text = "";
            return;
        }
        _boardInfo.Text = $"Table {scene.Size}; camera {_board.CameraText}.\n" +
            "Keys on the battlefield (Tab to it or click it); ? shows them on one sheet:\n" + KeySheet.Text + "\n" +
            "This view shows the server's state and decides nothing; heights are for display only. The panel beside it says what the selected unit and the chosen action would do; the server decides.";
    }

    // The first hint, or else the first connection button (Rejoin now, Leave): where a keyboard user wants to be.
    private void FocusDefault()
    {
        // Stage N3: only a control on screen (a decision card's choice, a Menu choice while the Menu is open, a connection
        // button), else the Menu button.
        var target = _decisionButtons.GetChildren().Concat(_hints.GetChildren()).Concat(_bannerButtons.GetChildren()).OfType<Button>()
            .FirstOrDefault(n => !n.IsQueuedForDeletion() && n.IsVisibleInTree());
        (target ?? (_match.IsVisibleInTree() ? _menuButton : null))?.GrabFocus();
    }

    private void ScrollLogWithKeys(InputEvent @event)
    {
        var bar = _logScroll.GetVScrollBar();
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp })
        {
            _followLog = false;
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown })
        {
            _followLog = bar.Value + bar.Page >= bar.MaxValue - line();
        }
        int line() => (int)(_theme.DefaultFontSize * 1.5f);
        if (@event is not InputEventKey { Pressed: true } key)
        {
            return;
        }
        var page = (int)_logScroll.Size.Y - line();
        int? to = key.Keycode switch
        {
            Key.Up => _logScroll.ScrollVertical - line(),
            Key.Down => _logScroll.ScrollVertical + line(),
            Key.Pageup => _logScroll.ScrollVertical - page,
            Key.Pagedown => _logScroll.ScrollVertical + page,
            Key.Home => 0,
            Key.End => int.MaxValue,
            _ => null,
        };
        if (to is { } value)
        {
            _logScroll.ScrollVertical = (int)Math.Clamp(value, 0, Math.Max(0, bar.MaxValue - bar.Page));
            _followLog = _logScroll.ScrollVertical >= bar.MaxValue - bar.Page - 2;
            _logScroll.AcceptEvent();
        }
    }

    private static string FeedbackWord(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Sending => "SENT",
        FeedbackKind.Accepted => "DONE",
        FeedbackKind.RulesRefused => "REFUSED BY THE RULES",
        FeedbackKind.ProtocolRefused => "NOT TAKEN",
        FeedbackKind.Stale => "OUT OF DATE",
        _ => "UNKNOWN",
    };

    private async void Send(ActionHint hint)
    {
        if (_controller is { } controller)
        {
            await controller.SendAsync(hint.Command, hint.Label);
        }
    }

    private async void RejoinNow(bool takeSeatBack)
    {
        if (_controller is not { } controller)
        {
            return;
        }
        try
        {
            await controller.RejoinNowAsync(takeSeatBack);
        }
        catch (InvalidOperationException e)
        {
            _banner.Text += (_banner.Text.Length > 0 ? "\n" : "") + e.Message;
            _banner.Visible = true;
        }
    }

    private async void Resync()
    {
        if (_controller is { } controller)
        {
            await controller.ResyncAsync();
        }
    }

    // ---- settings: text size and contrast (accessibility) ----

    private Container Settings()
    {
        var size = new OptionButton();
        foreach (var percent in new[] { 100, 125, 150 })
        {
            size.AddItem($"Text {percent}%");
        }
        size.ItemSelected += index =>
        {
            _scale = ClientSettings.TextScales[index];
            ApplyTheme();
            Remember(_settings with { TextSize = (int)index });
        };
        _textChoice = size;
        var contrast = new CheckButton { Text = "High contrast" };
        contrast.Toggled += on =>
        {
            _highContrast = on;
            ApplyTheme();
            PaintLog();
            Remember(_settings with { HighContrast = on });
        };
        _contrastChoice = contrast;
        // QoL I2: buttons and panels larger or smaller, apart from the text size (the window's content scale).
        var scale = new OptionButton { Name = "InterfaceScale" };
        foreach (var factor in ClientSettings.InterfaceScales)
        {
            scale.AddItem($"Scale {factor * 100:0}%");
        }
        scale.ItemSelected += index =>
        {
            GetWindow().ContentScaleFactor = ClientSettings.InterfaceScales[index];
            Remember(_settings with { InterfaceScale = (int)index });
        };
        _scaleChoice = scale;
        // Visible dice rolls (QoL stage F2): how long a box of dice stays on the battlefield, or Off.
        var dice = new OptionButton();
        foreach (var name in DiceChoices)
        {
            dice.AddItem(name);
        }
        dice.Select(1);
        dice.ItemSelected += index =>
        {
            _board?.SetDiceSeconds(DiceSeconds[index]);
            Remember(_settings with { Dice = (int)index });
        };
        _diceChoice = dice;
        // QoL I3: short sounds for your move, activations, passed and failed rolls and scoring; off by default.
        var sound = new CheckButton { Text = "Sound", Name = "SoundToggle" };
        sound.Toggled += on =>
        {
            _sounds.Enabled = on;
            Remember(_settings with { Sound = on });
        };
        _soundChoice = sound;
        // Stage N4: the settings wrap onto more lines, so the Menu never needs more than its column's width.
        var flow = new HFlowContainer { Name = "SettingsRow" };
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 6);
        foreach (var control in new Control[] { size, contrast, scale, dice, sound })
        {
            flow.AddChild(control);
        }
        return flow;
    }

    private static readonly string[] DiceChoices = ["Dice: Short (2 s)", "Dice: Normal (4 s)", "Dice: Long (8 s)", "Dice: Off"];
    private static readonly double[] DiceSeconds = [2, 4, 8, 0];
    private OptionButton? _diceChoice;
    private OptionButton? _textChoice;
    private OptionButton? _scaleChoice;
    private CheckButton? _contrastChoice;
    private CheckButton? _soundChoice;
    private readonly Sounds _sounds = new() { Name = "Sounds" };
    private readonly TurnAlert _turnAlert = new();
    private bool _focused = true;
    private string _title = "";
    private string _baseTitle = "";

    public Sounds SoundPlayer => _sounds;

    // For the UI check: stands in for the window being in front or not (null: the real focus).
    public bool? FocusOverride
    {
        get => _focusOverride;
        set
        {
            _focusOverride = value;
            UpdateTurnAlert();
        }
    }

    private bool? _focusOverride;

    // For the UI check: each combat-log row's sound, as the events gave them.
    public IReadOnlyList<(int Seq, SoundCue? Sound, bool Replayed)> LogSounds => [.. _logRows.Select(r => (r.Seq, r.Source.Sound, r.Source.Replayed))];

    public int AttentionRequests { get; private set; }

    public bool YourMove => _lastScreen is { End: null } s && s.View.YourMove;

    public string WindowTitle { get; private set; } = "";

    public void ChooseSound(bool on)
    {
        if (_soundChoice is { } choice)
        {
            choice.ButtonPressed = on;
        }
    }

    // QoL I3: the title says "● YOUR MOVE" and the window asks for attention once when the move turns to this window
    // while it is not in front; both end when it is in front again or the move is no longer this window's.
    private void UpdateTurnAlert()
    {
        var yourMove = _lastScreen is { End: null } s && s.View.YourMove && _hotSeat is null;
        var focused = FocusOverride ?? _focused;
        var wasAlerted = _turnAlert.Alerted;
        if (_turnAlert.Update(yourMove, focused))
        {
            AttentionRequests++;
            DisplayServer.WindowRequestAttention();
            _sounds.Play(SoundCue.YourMove);
        }
        if (_turnAlert.Alerted != wasAlerted || _turnAlert.Alerted)
        {
            WindowTitle = _turnAlert.Title(_title);
            DisplayServer.WindowSetTitle(WindowTitle);
        }
    }

    // ---- QoL I2: settings kept between launches ----

    private ClientSettings _settings = ClientSettings.Default;
    // Where they are kept: user://settings.cfg; a UI check run keeps its own file in its output folder and starts from the
    // defaults; the smoke runs keep none.
    private string? _settingsPath;
    private bool _applyingSettings;

    public ClientSettings CurrentSettings => _settings;

    public string? SettingsPath => _settingsPath;

    private void Remember(ClientSettings settings)
    {
        _settings = settings;
        if (_applyingSettings || _settingsPath is null)
        {
            return;
        }
        using var file = Godot.FileAccess.Open(_settingsPath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(settings.Format());
    }

    // Reads the file again and shows what it holds (at start, and for the UI check).
    public void LoadSettings()
    {
        var text = _settingsPath is not null && Godot.FileAccess.FileExists(_settingsPath) ? Godot.FileAccess.GetFileAsString(_settingsPath) : null;
        ApplySettings(ClientSettings.Parse(text));
    }

    private void ApplySettings(ClientSettings settings)
    {
        _applyingSettings = true;
        try
        {
            void Pick(OptionButton? choice, int index)
            {
                if (choice is not null)
                {
                    choice.Select(index);
                    choice.EmitSignal(OptionButton.SignalName.ItemSelected, index);
                }
            }
            Pick(_textChoice, settings.TextSize);
            Pick(_scaleChoice, settings.InterfaceScale);
            Pick(_diceChoice, settings.Dice);
            _soundChoice?.SetPressedNoSignal(settings.Sound);
            _sounds.Enabled = settings.Sound;
            if (_contrastChoice is { } contrast)
            {
                contrast.SetPressedNoSignal(settings.HighContrast);
                _highContrast = settings.HighContrast;
                ApplyTheme();
                PaintLog();
            }
            if (_board is not null && _board.TopDown != settings.TopDown)
            {
                _board.ToggleTopDown();
            }
            _settings = settings;
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    // For the UI check: the interface scale as a player picks it in the Menu.
    public void ChooseScale(int index)
    {
        if (_scaleChoice is { } choice)
        {
            choice.Select(index);
            choice.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        }
    }

    public void ChooseTextSize(int index)
    {
        if (_textChoice is { } choice)
        {
            choice.Select(index);
            choice.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        }
    }

    // For the UI check: the dice setting as a player picks it in the Menu.
    public void ChooseDice(int index)
    {
        if (_diceChoice is { } choice)
        {
            choice.Select(index);
            choice.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        }
    }

    private void ApplyTheme()
    {
        _theme.DefaultFontSize = (int)(18 * _scale);
        var text = _highContrast ? Colors.White : new Color(0.92f, 0.92f, 0.9f);
        _background.Color = _highContrast ? Colors.Black : new Color(0.12f, 0.12f, 0.14f);
        foreach (var type in new[] { "Label", "Button", "LineEdit", "OptionButton", "CheckButton" })
        {
            _theme.SetColor("font_color", type, text);
        }
        _theme.SetColor("font_focus_color", "Button", _highContrast ? Colors.Yellow : Colors.White);
        _board?.SetLook(_scale, _highContrast);
        if (_pTitle is not null)
        {
            ShowPanel();
        }
        if (_hud is not null)
        {
            ShowStrip();
        }
    }

    // ---- headless checks ----

    private string ChosenScenario() => _scenario.GetItemMetadata(_scenario.Selected).AsString();

    private static string ScenarioLabel(string scenario) => ContentNames.Label(scenario);

    // Creates a match on the server and reports the Welcome (M7.2 check, unchanged).
    private static async Task<string> Smoke(Uri server)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var session = await ClientSession.ConnectAsync(server, timeout.Token);
            var answer = await session.CreateAsync("ranged", timeout.Token);
            if (answer is not Welcome welcome)
            {
                return $"SMOKE FAILED: {answer.GetType().Name}";
            }
            await session.WaitForAsync(s => s.Synchronized, timeout.Token);
            return $"SMOKE OK: Welcome seat={welcome.Seat} status={welcome.Status} events={session.Mirror.Events} hash={session.Mirror.Hash[..12]} synchronized={session.Synchronized}";
        }
        catch (Exception e)
        {
            return $"SMOKE FAILED: {e.GetType().Name}: {e.Message}";
        }
    }

    // Creates a match through the match screen's controller, draws the screen, and reports what it shows.
    private async Task<string> SmokeView(Uri server)
    {
        var controller = new MatchController(new MatchControllerOptions { Endpoint = server });
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (await controller.CreateAsync("ranged", timeout.Token) is not Welcome welcome)
            {
                return "SMOKE VIEW FAILED: no Welcome";
            }
            // An opponent joins in the same process, so the server starts the match and the screen has something to show.
            _opponent = new MatchController(new MatchControllerOptions { Endpoint = server });
            if (await _opponent.JoinAsync(welcome.Code, timeout.Token) is not Welcome)
            {
                return "SMOKE VIEW FAILED: the opponent could not join";
            }
            await controller.Session!.WaitForAsync(s => s.Synchronized && s.Mirror.State.Phase != Rules.Core.State.Phase.PreGame, timeout.Token);
            _gapsShown = true; // no dialog in a headless run
            Attach(controller);
            var s = controller.Snapshot();
            Render(s);
            return $"SMOKE VIEW OK: connection={s.Connection} seat={s.Seat} code={s.Code} phase=\"{s.View.Phase}\" " +
                $"whose=\"{s.View.WhoseMove}\" models={s.View.Models.Count} hints={s.Hints.Count} log={s.Log.Count} " +
                $"names={(s.View.NamesProblem is null ? "content" : "ids")} gaps={RulesGaps.All.Count} first=\"{s.Log.FirstOrDefault()?.Text}\"";
        }
        catch (Exception e)
        {
            return $"SMOKE VIEW FAILED: {e.GetType().Name}: {e.Message}";
        }
    }

    // ---- small helpers ----

    private static string? Arg(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    // Brings the event rows up to date with the log: rows that left the list (it keeps the newest 200) are removed and new
    // events are added at the bottom. The words and the order are the log's; only the tag and colours are added.
    private void ShowLog(IReadOnlyList<LogLine> log)
    {
        var first = log.Count == 0 ? int.MaxValue : log[0].Seq;
        var known = _logRows.Count == 0 ? 0 : _logRows[^1].Seq;
        if (log.Count == 0 || log[^1].Seq < known)
        {
            // A new match, or the screen went back: start the list again.
            foreach (var row in _log.GetChildren())
            {
                _log.RemoveChild(row);
                row.QueueFree();
            }
            _logRows.Clear();
            known = 0;
        }
        while (_logRows.Count > 0 && _logRows[0].Seq < first)
        {
            var gone = _logRows[0].Box;
            _log.RemoveChild(gone);
            gone.QueueFree();
            _logRows.RemoveAt(0);
        }
        foreach (var line in log.Where(l => l.Seq > known))
        {
            var row = new HBoxContainer();
            var marker = new Label { Text = "", CustomMinimumSize = new Vector2(22, 0) };
            // Proposal G4: "[41] A4 FIGHTS B3", the verb bold and coloured, the dice under it; the narrator's full line is
            // the row's tooltip.
            var text = new RichTextLabel
            {
                BbcodeEnabled = true,
                FitContent = true,
                ScrollActive = false,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = FullText(line),
                MouseFilter = MouseFilterEnum.Pass,
            };
            row.AddChild(marker);
            // Proposal J1b: the first row of an action that rolled dice has ▸, which opens its steps and outcome under it.
            Button? toggle = null;
            Label? details = null;
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
            column.AddThemeConstantOverride("separation", 0);
            column.AddChild(text);
            if (line.Action is { } action && action.FirstSeq == line.Seq)
            {
                var start = line.Seq;
                toggle = new Button { Text = "▸", Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Show this action's steps and outcome", SizeFlagsVertical = SizeFlags.ShrinkBegin };
                toggle.Pressed += () => ToggleLogAction(start);
                details = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
                column.AddChild(details);
                row.AddChild(toggle);
            }
            row.AddChild(column);
            // QoL I1: a click on a row points out the models its event names (nothing is selected or sent).
            if (line.Models is { Count: > 0 })
            {
                var seq = line.Seq;
                row.MouseFilter = MouseFilterEnum.Stop;
                row.MouseDefaultCursorShape = CursorShape.PointingHand;
                row.GuiInput += input =>
                {
                    if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                    {
                        PickLogRow(seq);
                    }
                };
            }
            _log.AddChild(row);
            _logRows.Add(new LogRow(line.Seq, row, marker, text, line, toggle, details));
        }
        PaintLog();
    }

    private int? _logPicked;

    private void PickLogRow(int seq)
    {
        if (_logRows.FirstOrDefault(r => r.Seq == seq) is not { } row || _board is null)
        {
            return;
        }
        _logPicked = seq;
        _board.PointOut(row.Source.Models ?? []);
        PaintLog();
    }

    private void ToggleScenario()
    {
        if (_scenarioCard.Visible)
        {
            _scenarioCard.Hide();
            return;
        }
        CloseOthers(_scenarioCard);
        RefreshScenario();
        _scenarioCard.PopupCentered();
    }

    // The card follows the match while it is open: rows and the PREVIEW line are rebuilt when they change.
    private void RefreshScenario()
    {
        if (_controller is null || _lastScreen is null)
        {
            return;
        }
        var view = ScenarioCard.Of(_lastScreen.State, _lastScreen.Seat);
        if (_scenarioView is { } old && old.Text == view.Text)
        {
            return;
        }
        _scenarioView = view;
        _scenarioText.Text = string.Join("\n", view.Lines);
        // Old rows leave at once, so the new ones keep their names.
        foreach (var child in _scenarioRows.GetChildren())
        {
            _scenarioRows.RemoveChild(child);
            child.QueueFree();
        }
        _scenarioButtons.Clear();
        foreach (var row in view.Rows)
        {
            var id = row.ObjectiveId;
            var button = Button(row.Text, () => PickScenarioRow(id));
            button.Name = "ScenarioRow_" + id.Replace('-', '_');
            button.Alignment = HorizontalAlignment.Left;
            _scenarioRows.AddChild(button);
            _scenarioButtons[id] = button;
        }
        _scenarioPreview.Text = view.Projection ?? "";
        _scenarioPreview.Visible = view.Projection is not null;
    }

    // A row click selects and centres on the objective (as O does), closes the card and sends nothing.
    private void PickScenarioRow(string objectiveId)
    {
        _scenarioCard.Hide();
        _board.CentreOnObjective(objectiveId);
    }

    // For the UI check.
    public bool ScenarioOpen => _scenarioCard.Visible;

    public ScenarioCardView? ScenarioShown => _scenarioCard.Visible ? _scenarioView : null;

    public string ScenarioShownText => _scenarioCard.Visible
        ? string.Join("\n", new[] { _scenarioText.Text }.Concat(_scenarioRows.GetChildren().OfType<Button>().Select(b => b.Text))
            .Concat(_scenarioPreview.Visible ? [_scenarioPreview.Text] : []))
        : "";

    public void OpenScenario()
    {
        if (!_scenarioCard.Visible)
        {
            ToggleScenario();
        }
    }

    public void CloseScenario() => _scenarioCard.Hide();

    public void PressScenarioRow(string objectiveId) => _scenarioButtons.GetValueOrDefault(objectiveId)?.EmitSignal(BaseButton.SignalName.Pressed);

    private void ToggleKeySheet()
    {
        if (_keySheet.Visible)
        {
            _keySheet.Hide();
        }
        else
        {
            CloseOthers(_keySheet);
            KeySheetFitted = false;
            BuildKeySheet();
        }
    }

    private VBoxContainer? _keySheetBody;
    private readonly List<Label> _keyCaps = [];

    // Stage K1: the sheet is built when it opens, for the window's size and text scale now: two columns of groups, each
    // a heading and its entries; an entry is its keys as bold key caps, then what they do. No scrolling: the sheet is
    // as wide as the window allows and the entries flow into it.
    private void BuildKeySheet(int? fontSize = null)
    {
        if (_keySheetBody is { } old)
        {
            _keySheet.RemoveChild(old);
            old.QueueFree();
        }
        _keyCaps.Clear();
        var view = GetViewportRect().Size;
        var width = (int)Math.Min(view.X - 24, 1600);
        var size = fontSize ?? (int)(15 * _scale);
        var gap = Math.Max(6, size * 3 / 4);
        // Three columns where the window is wide enough for them, else two.
        var count = width >= 66 * size ? 3 : 2;
        var columnWidth = (width - (count + 1) * gap) / count;
        var bold = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.9f };
        var cap = new StyleBoxFlat
        {
            BgColor = _highContrast ? Colors.Black : new Color(0.22f, 0.22f, 0.26f),
            BorderColor = _highContrast ? Colors.Yellow : new Color(0.75f, 0.75f, 0.8f),
            ContentMarginLeft = 5, ContentMarginRight = 5, ContentMarginTop = 0, ContentMarginBottom = 0,
        };
        cap.SetBorderWidthAll(1);
        cap.SetCornerRadiusAll(4);
        var body = new VBoxContainer { Name = "KeySheetBody" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", gap);
        body.AddChild(row);
        var columns = Enumerable.Range(0, count).Select(_ => new VBoxContainer { CustomMinimumSize = new Vector2(columnWidth, 0) }).ToList();
        columns.ForEach(c =>
        {
            c.AddThemeConstantOverride("separation", 1);
            row.AddChild(c);
        });
        // Groups go to columns in order, a new column once this one holds its share of the lines.
        var total = KeySheet.Groups.Sum(g => g.Entries.Count + 1);
        var used = 0;
        foreach (var (group, entries) in KeySheet.Groups)
        {
            var column = columns[Math.Min(count - 1, used * count / total)];
            used += entries.Count + 1;
            var heading = new Label { Text = group.ToUpperInvariant(), Name = "KeyGroup" };
            heading.AddThemeFontOverride("font", bold);
            heading.AddThemeFontSizeOverride("font_size", size);
            heading.AddThemeColorOverride("font_color", _highContrast ? Colors.Yellow : new Color(1f, 0.85f, 0.45f));
            column.AddChild(heading);
            var flow = new HFlowContainer { CustomMinimumSize = new Vector2(columnWidth, 0) };
            flow.AddThemeConstantOverride("h_separation", gap);
            flow.AddThemeConstantOverride("v_separation", 1);
            column.AddChild(flow);
            foreach (var entry in entries)
            {
                var chip = new HBoxContainer { Name = "KeyEntry" };
                chip.AddThemeConstantOverride("separation", 3);
                foreach (var key in entry.Keys)
                {
                    var label = new Label { Text = key, Name = "KeyCap", VerticalAlignment = VerticalAlignment.Center };
                    label.AddThemeFontOverride("font", bold);
                    label.AddThemeFontSizeOverride("font_size", size);
                    label.AddThemeStyleboxOverride("normal", cap);
                    chip.AddChild(label);
                    _keyCaps.Add(label);
                }
                var what = new Label { Text = entry.What, VerticalAlignment = VerticalAlignment.Center };
                what.AddThemeFontSizeOverride("font_size", size);
                // A long description wraps inside the column instead of widening it.
                var keysWidth = entry.Keys.Sum(k => bold.GetStringSize(k, HorizontalAlignment.Left, -1, size).X + 13);
                if (keysWidth + what.GetThemeFont("font").GetStringSize(entry.What, HorizontalAlignment.Left, -1, size).X > columnWidth)
                {
                    what.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    what.CustomMinimumSize = new Vector2(Math.Max(60, columnWidth - keysWidth - 4), 0);
                }
                chip.AddChild(what);
                flow.AddChild(chip);
            }
            column.AddChild(new Control { CustomMinimumSize = new Vector2(0, gap / 3) });
        }
        _keySheetBody = body;
        _keySheet.AddChild(body);
        _keySheet.Size = new Vector2I(width, 10);
        _keySheet.PopupCentered(new Vector2I(width, 10));
        // The flows know their height once laid out at this width: then the sheet takes it, centred again; when it is
        // taller than the window, the words get one size smaller (down to 10 px), so nothing needs scrolling.
        Callable.From(() => FitKeySheet(width, size)).CallDeferred();
    }

    private async void FitKeySheet(int width, int size)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!_keySheet.Visible)
        {
            return;
        }
        _keySheet.ResetSize();
        var height = _keySheet.Size.Y;
        var room = (int)GetViewportRect().Size.Y - _keySheet.GetThemeConstant("title_height") - 8;
        if (height > room && size > 10)
        {
            BuildKeySheet(size - 1);
            return;
        }
        _keySheet.Size = new Vector2I(width, height);
        _keySheet.PopupCentered(_keySheet.Size);
        KeySheetFitted = true;
    }

    // For the UI check: the sheet has taken its final size since it last opened.
    public bool KeySheetFitted { get; private set; }

    // For the UI check: the key sheet's words while it is open (the same plain text as the Menu's), its key caps, and
    // its rectangle on screen.
    public string KeySheetText => _keySheet.Visible ? KeySheet.Text : "";

    public IReadOnlyList<Label> KeyCaps => _keySheet.Visible ? _keyCaps : [];

    public Rect2I KeySheetRect => new(_keySheet.Position, _keySheet.Size);

    public int KeySheetTitleHeight => _keySheet.GetThemeConstant("title_height");

    public int? PickedLogRow => _logPicked;

    public void CloseKeySheet() => _keySheet.Hide();

    public void OpenKeySheet()
    {
        if (!_keySheet.Visible)
        {
            KeySheetFitted = false;
            BuildKeySheet();
        }
    }

    public Rect2? LogRowRect(int seq) => _logRows.FirstOrDefault(r => r.Seq == seq) is { Box: var box } && box.IsVisibleInTree()
        ? box.GetGlobalRect() : null;

    public int LogToggles { get; private set; }

    public bool IsLogToggle(int start, Control? control) => control is not null && _logRows.Any(r => r.Seq == start && r.Toggle == control);

    private void ToggleLogAction(int start)
    {
        LogToggles++;
        if (!_logOpen.Remove(start))
        {
            _logOpen.Add(start);
            // Reading an action's steps: the log stays where it is (it no longer jumps to the newest row as the steps
            // make it taller, nor when new events arrive); End, or scrolling to the bottom, follows the newest again.
            _followLog = false;
        }
        PaintLog();
    }

    // The action as its newest event in the log has it (later events carry it further).
    private ActionResolution? LatestAction(int start) => _logRows.LastOrDefault(r => r.Source.Action?.FirstSeq == start)?.Source.Action;

    // An action's steps and outcome as the expanded row lists them.
    public static string ActionDetails(ActionResolution action) =>
        string.Join("\n", action.Steps.Select(s => $"  · {s}").Append($"  = {action.Outcome}"));

    // For the UI check: the rows that can be expanded, the expanded text of one, and its toggle on screen.
    public IReadOnlyList<int> LogActionRows => [.. _logRows.Where(r => r.Toggle is not null).Select(r => r.Seq)];

    public string? LogActionDetails(int start) => _logRows.FirstOrDefault(r => r.Seq == start) is { Details: { Visible: true } d } ? d.Text : null;

    public ActionResolution? LogAction(int start) => LatestAction(start);

    public LogLine? LogLineAt(int seq) => _logRows.FirstOrDefault(r => r.Seq == seq)?.Source;

    public void ShowLogRow(int seq)
    {
        if (_logRows.FirstOrDefault(r => r.Seq == seq) is { } row)
        {
            // An action's row shows its ▸/▾ (the top line), even when its opened steps are taller than the log.
            _logScroll.EnsureControlVisible((Control?)row.Toggle ?? row.Box);
        }
    }

    public string LogToggleText(int start) => _logRows.FirstOrDefault(r => r.Seq == start) is { Toggle: { } t } ? t.Text : "";

    // Only while the toggle is inside the log's visible part (a row scrolled out of view is drawn clipped).
    public Rect2? LogToggleRect(int start) => _logRows.FirstOrDefault(r => r.Seq == start) is { Toggle: { } t } && t.IsVisibleInTree()
        && _logScroll.GetGlobalRect().HasPoint(t.GetGlobalRect().GetCenter()) ? t.GetGlobalRect() : null;

    public IReadOnlyList<(int Seq, IReadOnlyList<Rules.Core.State.ModelId> Models)> LogRowModels =>
        [.. _logRows.Select(r => (r.Seq, r.Source.Models ?? (IReadOnlyList<Rules.Core.State.ModelId>)[]))];

    private static string FullText(LogLine line) => $"[{line.Seq}] {line.Text.Trim()}{(line.Dice is null ? "" : $"\n      {line.Dice}")}";

    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);

    // Verb colours by kind (every verb is also a word); the newest row is marked ▶ and it and the current turn's header
    // stand out.
    private void PaintLog()
    {
        var lastTurn = _logRows.FindLastIndex(r => r.Source.Kind == EventKind.Turn && r.Source.Text.Contains("--- Turn ", StringComparison.Ordinal));
        for (var i = 0; i < _logRows.Count; i++)
        {
            var (seq, _, marker, line, source, toggle, details) = _logRows[i];
            if (toggle is not null && details is not null)
            {
                var open = _logOpen.Contains(seq);
                toggle.Text = open ? "▾" : "▸";
                details.Visible = open && LatestAction(seq) is not null;
                details.Text = open && LatestAction(seq) is { } action ? ActionDetails(action) : "";
                details.AddThemeColorOverride("font_color", _highContrast ? Colors.White : new Color(0.8f, 0.8f, 0.75f));
            }
            var newest = i == _logRows.Count - 1;
            marker.Text = newest ? "▶" : seq == _logPicked ? "•" : "";
            var colour = (_highContrast ? Colors.Yellow : KindColour(source.Kind)).ToHtml(false);
            var row = source.Row ?? new CompactRow(null, null, source.Text.Trim());
            var verb = row.Verb is null ? "" : $"[b][color=#{colour}]{Escape(row.Verb)}[/color][/b]";
            var words = string.Join(" ", new[] { row.Actor is null ? null : Escape(row.Actor), verb, Escape(row.Rest) }.Where(p => !string.IsNullOrEmpty(p)));
            // An event without a verb (turns, phases, scoring) is drawn in its kind's colour.
            var body = row.Verb is null ? $"[color=#{colour}]{words}[/color]" : words;
            var highlight = newest || i == lastTurn;
            var head = highlight ? $"[b][lb]{seq}][/b] " : $"[lb]{seq}] ";
            var dice = source.Dice is null ? "" : $"\n[color=#{(_highContrast ? "ffffff" : "a6a6a6")}]      {Escape(source.Dice)}[/color]";
            line.Text = head + body + dice;
            line.AddThemeColorOverride("default_color", highlight ? (_highContrast ? Colors.Yellow : new Color(1f, 0.95f, 0.6f)) : (_highContrast ? Colors.White : new Color(0.92f, 0.92f, 0.9f)));
            marker.AddThemeColorOverride("font_color", _highContrast ? Colors.Yellow : new Color(1f, 0.95f, 0.6f));
        }
    }

    private static Color KindColour(EventKind kind) => kind switch
    {
        EventKind.Movement => new Color(0.55f, 0.75f, 1f),
        EventKind.Shooting => new Color(1f, 0.7f, 0.35f),
        EventKind.Charge => new Color(1f, 0.55f, 0.3f),
        EventKind.Fight => new Color(1f, 0.45f, 0.45f),
        EventKind.Injury => new Color(1f, 0.35f, 0.5f),
        EventKind.Status => new Color(0.85f, 0.6f, 1f),
        EventKind.Activation => new Color(0.5f, 0.95f, 0.95f),
        EventKind.Scoring => new Color(0.55f, 1f, 0.55f),
        EventKind.MatchEnd => new Color(1f, 0.9f, 0.3f),
        _ => new Color(0.7f, 0.7f, 0.7f),
    };

    // The event rows as shown: tag and line (for the UI check).
    // The combat log as shown (for the UI check): the marker, the compact line as drawn (without markup), the narrator's
    // full line (the tooltip) and the kind.
    public IReadOnlyList<(string Marker, string Shown, string Full, EventKind Kind)> LogRows =>
        [.. _logRows.Select(r => (r.Marker.Text, r.Line.GetParsedText(), r.Line.TooltipText, r.Source.Kind))];

    // Every row's full line with its dice, in order (the narrator's words, as the event list showed them before G4).
    public string LogFullText => string.Join("\n", _logRows.Select(r => r.Line.TooltipText));

    private static Label Text(string text) => new()
    {
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };

    private static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.All };
        button.Pressed += pressed;
        return button;
    }

    private static HBoxContainer Row(params Control[] controls)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        foreach (var control in controls)
        {
            row.AddChild(control);
        }
        return row;
    }

    private static VBoxContainer Section(string title, Control body)
    {
        var box = new VBoxContainer();
        box.AddChild(Text(title.ToUpperInvariant()));
        box.AddChild(body);
        box.AddChild(new HSeparator());
        return box;
    }

    private void Print(string text)
    {
        GD.Print(text);
        _status.Text = text;
    }
}
