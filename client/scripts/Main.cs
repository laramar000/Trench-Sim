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
    private HBoxContainer _bannerButtons = null!;
    private Label _code2 = null!;
    private VBoxContainer _endSection = null!;
    private Label _end = null!;
    private string? _finalHash;
    private Label _state = null!;
    private Label _models = null!;
    private VBoxContainer _boardSection = null!;
    private BoardView _board = null!;
    private Label _boardInfo = null!;
    private VBoxContainer _hints = null!;
    private Label _notYet = null!;
    private Label _feedback = null!;
    private Label _log = null!;
    private ScrollContainer _logScroll = null!;
    private AcceptDialog _gaps = null!;
    private ConfirmationDialog _takeBack = null!;

    public override async void _Ready()
    {
        Theme = _theme;
        _background = new ColorRect { Color = new Color(0.12f, 0.12f, 0.14f) };
        _background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_background);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.OffsetLeft = 16;
        scroll.OffsetTop = 12;
        scroll.OffsetRight = -16;
        scroll.OffsetBottom = -12;
        AddChild(scroll);
        _root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _root.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_root);

        _root.AddChild(Text($"Trench Sim client (M7.4: battlefield, in progress). Godot {Engine.GetVersionInfo()["string"]}, " +
            $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}. Rules version {ClaimNoMansLand.RulesVersion}; protocol {ClientSession.SubProtocol}."));
        _root.AddChild(Settings());
        _root.AddChild(Text(Hints));
        _status = Text("");
        _root.AddChild(_status);
        BuildConnect();
        BuildMatch();
        ApplyTheme();
        ShowConnect();

        var args = OS.GetCmdlineUserArgs();
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
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.F1 })
        {
            _gaps.PopupCentered();
        }
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.F2 } && _hotSeat is not null)
        {
            SwitchSeat();
        }
        // Esc cancels the action being chosen also when the board does not have the keyboard (after a click on the side
        // panel or the action bar). It only cancels; it never sends anything.
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape } && _board is not null
            && _board.Interaction.Mode != InteractionMode.None && _board.Interaction.Pending is null)
        {
            _board.CancelPreview();
            GetViewport().SetInputAsHandled();
        }
    }

    // Closing the window (or quitting) stops the hot-seat server at once, so no server process is left behind.
    public override void _Notification(int what)
    {
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
        _connect.AddChild(Row(Text("Scenario:"), _scenario, Button("Create match", () => Start(c => c.CreateAsync(ChosenScenario())))));
        _connect.AddChild(Text("The 1 v 1, 2 v 2 and 3 v 3 choices are small test rosters (each side's first models) for trying mechanics quickly; " +
            "the normal roster is the full warbands."));
        _connect.AddChild(Row(Text("Single player:"), Button("Hot-seat: play both sides in this window", StartHotSeat)));
        _connect.AddChild(Text("Hot-seat starts a local server on this PC (no server address needed) and seats you as Seat One and Seat Two of a " +
            "match in the scenario above. F2 switches the side you control; by default the window follows the turn."));
        _code = new LineEdit { PlaceholderText = "match code", CustomMinimumSize = new Vector2(160, 0) };
        _connect.AddChild(Row(Text("Join with a code:"), _code, Button("Join match", () => Start(c => c.JoinAsync(_code.Text.Trim().ToUpperInvariant())))));
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

    private async void Leave()
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
        _match = new VBoxContainer();
        _match.AddThemeConstantOverride("separation", 8);
        // Hot-seat only: which seat this window controls, whose move it is, and the local server.
        _hotLine = Text("");
        _match.AddChild(_hotLine);
        _banner = Text("");
        _match.AddChild(_banner);
        _bannerButtons = new HBoxContainer();
        _match.AddChild(_bannerButtons);
        _code2 = Text("");
        _match.AddChild(Row(_code2, Button("Copy code", () =>
        {
            if (_controller?.Snapshot().Code is { } code)
            {
                DisplayServer.ClipboardSet(code);
            }
        }), Button("Rules in this build (F1)", () => _gaps.PopupCentered())));
        // Once the match has ended or been aborted: the result, the final state hash and the event count (M7.5). The
        // hash can be compared with the one `trench-cli verify` prints for the server's signed record.
        _end = Text("");
        var endBox = new VBoxContainer();
        endBox.AddChild(_end);
        endBox.AddChild(Row(Button("Copy final hash", () =>
        {
            if (_finalHash is { } hash)
            {
                DisplayServer.ClipboardSet(hash);
            }
        })));
        _endSection = Section("Result of the match", endBox);
        _endSection.Visible = false;
        _match.AddChild(_endSection);
        // The battlefield (M7.4): a 3D view of the server's state, with the camera keys and the selected model in words.
        _board = new BoardView();
        _boardInfo = Text("");
        var boardBox = new VBoxContainer();
        boardBox.AddChild(_board);
        boardBox.AddChild(_boardInfo);
        _board.Changed += ShowBoardInfo;
        // A confirmed battlefield command goes the same way as a hint button: one at a time, answered by the server.
        _board.Interaction.Send = (command, label) => _controller is { } controller
            ? controller.SendAsync(command, label)
            : Task.FromResult(new Feedback(FeedbackKind.ProtocolRefused, "Not sent: not in a match."));
        _boardSection = Section("Battlefield", boardBox);
        _match.AddChild(_boardSection);
        // Two columns: the match on the left, what you can do and what happened on the right.
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 };
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 };
        columns.AddChild(left);
        columns.AddChild(right);
        _match.AddChild(columns);
        _state = Text("");
        left.AddChild(Section("Match", _state));
        _models = Text("");
        left.AddChild(Section("Models ([A] Seat One, [B] Seat Two)", _models));
        _hints = new VBoxContainer();
        _notYet = Text("");
        var actions = new VBoxContainer();
        actions.AddChild(Text("Available to you: hints from the rules. The server accepts or refuses every command."));
        actions.AddChild(_hints);
        actions.AddChild(_notYet);
        right.AddChild(Section("Your actions", actions));
        _feedback = Text("");
        right.AddChild(Section("Last command", _feedback));
        _log = Text("");
        // The event list takes focus (Tab), so it can be scrolled with the keyboard: arrows, Page Up/Down, Home, End.
        _logScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 260), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FocusMode = FocusModeEnum.All };
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
        right.AddChild(Section("Events (newest at the bottom; Tab here, then arrows or Page Up/Down to scroll)", _logScroll));
        _root.AddChild(_match);

        _gaps = new AcceptDialog { Title = RulesGaps.Title, DialogText = RulesGaps.Text, DialogAutowrap = true, Size = new Vector2I(760, 480) };
        AddChild(_gaps);
        _takeBack = new ConfirmationDialog
        {
            Title = "Take the seat back?",
            DialogText = "This seat was opened from another window. Taking it back closes it there. Take it back?",
            DialogAutowrap = true,
        };
        _takeBack.Confirmed += () => RejoinNow(true);
        AddChild(_takeBack);
    }

    private void Render(ScreenState s)
    {
        if (s.Version == _shown)
        {
            return;
        }
        _shown = s.Version;
        var view = s.View;
        _status.Text = $"{view.Status}";
        _hotLine.Visible = _hotSeat is not null;
        _hotLine.Text = _hotSeat is { } h
            ? $"{h.Banner()}\nLocal server {h.Server?.Endpoint} (process {h.Server?.ProcessId}); it stops when you leave or close this window."
            : "";
        _banner.Text = $"Connection: {s.ConnectionText}{(s.OpponentAway is null ? "" : "\n" + s.OpponentAway)}{(s.Warning is null ? "" : "\nWarning: " + s.Warning)}";

        // Buttons are rebuilt on every change; keep the keyboard focus on the same button (or a sensible one).
        var focused = GetViewport().GuiGetFocusOwner();
        var refocus = focused is Button b && (_bannerButtons.IsAncestorOf(b) || _hints.IsAncestorOf(b)) ? b.Text : null;
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
            _bannerButtons.AddChild(Button($"Switch to {Words.Seat(hot.OtherSeat)} (F2)", SwitchSeat));
            _bannerButtons.AddChild(Button(hot.FollowTurn ? "Follow the turn: on" : "Follow the turn: off", () =>
            {
                hot.FollowTurn = !hot.FollowTurn;
                _shown = -1;
                System.Threading.Interlocked.Exchange(ref _changed, 1);
            }));
        }
        _bannerButtons.AddChild(Button("Leave", Leave));

        _code2.Text = s.Code is null ? "" : $"Match code {s.Code}. You are {(s.Seat is { } seat ? $"{Words.Seat(seat)} [{Words.SeatLetter(seat)}]" : "not seated")}." +
            (_hotSeat is null && (view.Phase == Words.Phase(Rules.Core.State.Phase.PreGame) || view.Models.Count == 0) ? " Give the code to your opponent." : "");
        _endSection.Visible = s.End is not null;
        _finalHash = s.End?.Hash;
        _end.Text = s.End is { } end ? string.Join("\n", end.Lines()) : "";
        if (_test is { } test)
        {
            DisplayServer.WindowSetTitle(test.Title(s.Seat));
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
        _board.Show(s.Board, s.State, s.Seat, s.Log);
        _state.Text = string.Join("\n", view.Lines());
        _models.Text = view.Models.Count == 0 ? "No models yet." : string.Join("\n", view.Models.Select(m => m.Text));

        foreach (var child in _hints.GetChildren())
        {
            child.QueueFree();
        }
        foreach (var hint in s.Hints)
        {
            var chosen = hint;
            var button = Button(chosen.Label + "  (hint; the server decides)", () => Send(chosen));
            // Long labels wrap instead of widening the page (text at 150 % must still fit the window).
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.Alignment = HorizontalAlignment.Left;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _hints.AddChild(button);
        }
        if (s.Hints.Count == 0)
        {
            _hints.AddChild(Text(s.InputLocked is { } locked ? $"Nothing to send: {locked}" : view.YourMove ? "No choice to make here." : "Nothing to do until the opponent moves."));
        }
        _notYet.Text = s.NotYetOffered ?? "";
        _feedback.Text = s.Feedback is { } f ? $"{FeedbackWord(f.Kind)}: {f.Text}{(f.RuleRef is null ? "" : $" (rule: {f.RuleRef})")}" : "None yet.";
        _log.Text = string.Join("\n", s.Log.Select(l => $"[{l.Seq}] {l.Text.Trim()}{(l.Dice is null ? "" : $"\n      {l.Dice}")}"));
        if (refocus is not null)
        {
            var same = _bannerButtons.GetChildren().Concat(_hints.GetChildren()).OfType<Button>().FirstOrDefault(n => !n.IsQueuedForDeletion() && n.Text == refocus);
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

    private void ShowBoardInfo()
    {
        if (_board.Scene is not { } scene)
        {
            _boardInfo.Text = "";
            return;
        }
        _boardInfo.Text = $"Table {scene.Size}; camera {_board.CameraText}. Selected: {_board.Selected?.Text ?? "none"}\n" +
            "Keys on the battlefield (Tab to it or click it): arrows pan, + and - zoom, Q and E turn 90°, F fits the table, T top-down or angled, " +
            "N and P next and previous model, A activates the selected model. Actions: L place, M move, D dash, R retreat, S shoot, C charge, X fight; " +
            "then arrows (Shift: 2\") move the cursor, Enter fixes a waypoint, Space sends, Esc cancels. Mouse: click selects (or picks the target or a waypoint), right-drag pans, wheel zooms. Distances are in inches. " +
            "This view shows the server's state and decides nothing; heights are for display only.\n" +
            "ACTIONS ON THE BATTLEFIELD (previews say what the server would probably answer; the server decides): " + string.Join("\n", _board.Interaction.Lines());
    }

    // The first hint, or else the first connection button (Rejoin now, Leave): where a keyboard user wants to be.
    private void FocusDefault()
    {
        var target = _hints.GetChildren().Concat(_bannerButtons.GetChildren()).OfType<Button>().FirstOrDefault(n => !n.IsQueuedForDeletion());
        target?.GrabFocus();
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
            _banner.Text += "\n" + e.Message;
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

    private HBoxContainer Settings()
    {
        var size = new OptionButton();
        foreach (var percent in new[] { 100, 125, 150 })
        {
            size.AddItem($"Text {percent}%");
        }
        size.ItemSelected += index =>
        {
            _scale = new[] { 1f, 1.25f, 1.5f }[index];
            ApplyTheme();
        };
        var contrast = new CheckButton { Text = "High contrast" };
        contrast.Toggled += on =>
        {
            _highContrast = on;
            ApplyTheme();
        };
        return Row(size, contrast);
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
    }

    // ---- headless checks ----

    private string ChosenScenario() => _scenario.GetItemMetadata(_scenario.Selected).AsString();

    private static string ScenarioLabel(string scenario) => scenario switch
    {
        "ranged" => "ranged (normal roster)",
        "melee" => "melee (normal roster)",
        _ => $"{scenario[..^4]}, {scenario[^3]} v {scenario[^1]} (test roster)",
    };

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
