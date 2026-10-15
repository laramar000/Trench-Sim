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
    private int _changed;
    private long _shown = -1;
    private bool _gapsShown;
    private float _scale = 1f;
    private bool _highContrast;

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
    private HBoxContainer _bannerButtons = null!;
    private Label _code2 = null!;
    private Label _state = null!;
    private Label _models = null!;
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

        _root.AddChild(Text($"Trench Sim client (M7.3: match state). Godot {Engine.GetVersionInfo()["string"]}, " +
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
        if (Arg(args, "--smoke-server") is { } smoke)
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
            Render(_controller.Snapshot());
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.F1 })
        {
            _gaps.PopupCentered();
        }
    }

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
        _scenario = new OptionButton();
        _scenario.AddItem("ranged");
        _scenario.AddItem("melee");
        _connect.AddChild(Row(Text("Scenario:"), _scenario, Button("Create match", () => Start(c => c.CreateAsync(_scenario.GetItemText(_scenario.Selected))))));
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
        if (!_gapsShown)
        {
            // Playtesters see the rules gaps once per session at the first match, and any time with F1.
            _gapsShown = true;
            _gaps.PopupCentered();
        }
    }

    private async void Leave()
    {
        if (_controller is { } controller)
        {
            _controller = null;
            await controller.DisposeAsync();
        }
        ShowConnect();
    }

    // ---- match screen ----

    private void BuildMatch()
    {
        _match = new VBoxContainer();
        _match.AddThemeConstantOverride("separation", 8);
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
        _logScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 260), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _logScroll.AddChild(_log);
        _log.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        right.AddChild(Section("Events (newest at the bottom)", _logScroll));
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
        _banner.Text = $"Connection: {s.ConnectionText}{(s.OpponentAway is null ? "" : "\n" + s.OpponentAway)}{(s.Warning is null ? "" : "\nWarning: " + s.Warning)}";

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
        _bannerButtons.AddChild(Button("Leave", Leave));

        _code2.Text = s.Code is null ? "" : $"Match code {s.Code}. You are {(s.Seat is { } seat ? $"{Words.Seat(seat)} [{Words.SeatLetter(seat)}]" : "not seated")}." +
            (view.Phase == Words.Phase(Rules.Core.State.Phase.PreGame) || view.Models.Count == 0 ? " Give the code to your opponent." : "");
        _state.Text = string.Join("\n", view.Lines());
        _models.Text = view.Models.Count == 0 ? "No models yet." : string.Join("\n", view.Models.Select(m => m.Text));

        foreach (var child in _hints.GetChildren())
        {
            child.QueueFree();
        }
        foreach (var hint in s.Hints)
        {
            var chosen = hint;
            _hints.AddChild(Button(chosen.Label + "  (hint; the server decides)", () => Send(chosen)));
        }
        if (s.Hints.Count == 0)
        {
            _hints.AddChild(Text(s.InputLocked is { } locked ? $"Nothing to send: {locked}" : view.YourMove ? "No choice to make here." : "Nothing to do until the opponent moves."));
        }
        _notYet.Text = s.NotYetOffered ?? "";
        _feedback.Text = s.Feedback is { } f ? $"{FeedbackWord(f.Kind)}: {f.Text}{(f.RuleRef is null ? "" : $" (rule: {f.RuleRef})")}" : "None yet.";
        _log.Text = string.Join("\n", s.Log.Select(l => $"[{l.Seq}] {l.Text.Trim()}{(l.Dice is null ? "" : $"\n      {l.Dice}")}"));
        _logScroll.CallDeferred(ScrollContainer.MethodName.SetVScroll, int.MaxValue);
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
    }

    // ---- headless checks ----

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
        box.AddChild(new Label { Text = title.ToUpperInvariant() });
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
