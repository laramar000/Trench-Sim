using Client.Core;
using Godot;

// M7.3 verification only: drives the real screens with real key presses, as a keyboard user would, and reports what
// the screens show. Started by tools/UiCheck (godot --path client -- --ui-check <role> --out <folder> ...), which runs
// the server, a proxy it can cut, a second client and trench-cli. It reads only what is on screen (labels, buttons,
// dialogs, focus); it never touches the match controller, so it checks the UI rather than the code behind it.
// Lines written to <out>/<role>.log: "CHECK <id> PASS|FAIL <detail>", "MARK <name> [value]", "DUMP <tag> <key>=<value>".
// The runner answers by creating files <out>/signal-<name>.
public partial class UiCheck : Node
{
    private readonly string _role;
    private readonly string _out;
    private readonly string _server;
    private readonly string? _code;
    private readonly string _scenario;
    private readonly Control _main;
    private readonly bool _screens;
    private int _failures;
    private int _shots;

    public UiCheck(Control main, string role, string output, string server, string? code, string scenario)
    {
        _main = main;
        _role = role;
        _out = output;
        _server = server;
        _code = code;
        _scenario = scenario;
        _screens = DisplayServer.GetName() != "headless";
        System.IO.Directory.CreateDirectory(output);
    }

    public override async void _Ready()
    {
        try
        {
            await Frames(3);
            Mark("started", $"{DisplayServer.GetName()} {Engine.GetVersionInfo()["string"]}");
            switch (_role)
            {
                case "host":
                    await Host();
                    break;
                case "guest":
                    await Guest();
                    break;
                case "reopen":
                    await Reopen();
                    break;
                default:
                    Check("role", false, $"unknown role {_role}");
                    break;
            }
        }
        catch (Exception e)
        {
            Check("script", false, $"{e.GetType().Name}: {e.Message}".Replace('\n', ' '));
        }
        Mark("finished", $"failures={_failures}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- the roles ----

    private async Task Host()
    {
        var first = Focused();
        Check("focus-start", first is not null, $"focus at start: {Describe(first)}");
        await TabTraversal("connect");
        await TypeInto(Field("ws://"), _server, "type-server");
        await TypeInto(SpinLine(), "10", "set-window", enter: true);
        Check("set-window-value", Spin()?.Value == 10, $"rejoin window spin box = {Spin()?.Value}");
        await ChooseOption(Option("ranged"), _scenario, "choose-scenario");
        await Press(ButtonNamed("Create match"), Key.Enter, "create-enter");
        var code = await Until(() => Label("Match code ") is { } l && l.Text.Contains("Seat One [A]") ? l.Text : null, 20, "create");
        Check("create", code is not null, code ?? "match screen with the code did not appear");
        await Shot("host-created");
        await GapsDialogChecks();
        await TabTraversal("match");
        var matchCode = code?.Split(' ')[2].TrimEnd('.');
        Mark("code", matchCode ?? "");

        await PlayZone();

        // Phase 2: trench-cli takes the seat (and plays); then take it back with the keyboard.
        var takeBack = await Until(() => ButtonNamed("Take the seat back…"), 120, "takeover");
        Check("takeover-shown", takeBack is not null && Banner().Contains("another", StringComparison.OrdinalIgnoreCase),
            Banner());
        await Shot("host-taken-over");
        await Signal("takeback", 300);
        await Press(ButtonNamed("Take the seat back…"), Key.Enter, "takeback-enter");
        var confirm = await Until(() => Dialog<ConfirmationDialog>(), 10, "confirm");
        Check("takeback-confirm-shown", confirm is not null, confirm?.DialogText ?? "no confirmation dialog");
        Check("takeback-confirm-focus", confirm is not null && confirm.GuiGetFocusOwner() is Button b && b == confirm.GetOkButton(),
            $"focus in the dialog: {Describe(confirm?.GuiGetFocusOwner())}");
        await KeyPress(Key.Enter);
        var back = await Until(() => Banner().StartsWith("Connection: Connected", StringComparison.Ordinal) && LogText().Contains("sets up") ? Banner() : null, 30, "takeback");
        Check("takeback", back is not null, Banner());
        // The events arrive one by one after the Welcome; all 8 models were deployed by trench-cli.
        await Until(() => Lines(LogText()).Count(l => l.Contains(" sets up ")) >= 8 ? "ok" : null, 20, "catch-up");
        await Frames(3);
        var deployed = Lines(LogText()).Count(l => l.Contains(" sets up "));
        Check("catch-up-deploys", deployed == 8, $"{deployed} deployment events after taking the seat back");
        Check("catch-up-models", Section("MODELS").Split('\n').All(l => !l.Contains("not deployed")), Section("MODELS").Replace('\n', '|'));
        var away = await Until(() => Banner().Contains("paused", StringComparison.OrdinalIgnoreCase) ? Banner() : null, 10, "opponent-away");
        Check("opponent-away-shown-after-rejoin", away is not null, Banner());
        await Shot("host-took-back");
        Dump("took-back");
        await BoardChecks();
        Mark("took-back");

        await PlayActivations();
        Dump("played");
        Mark("played");

        await Signal("compare", 120);
        await Until(() => Banner().Contains("paused") ? null : "ok", 30, "resume");
        Dump("compare");
        Mark("compared");

        // Phase 3: the connection is cut and new connections are refused until the runner restores them.
        await FocusByTab(ButtonNamed("Leave"), 60);
        var before = Describe(Focused());
        await Signal("cut", 120);
        var reconnecting = await Until(() => Banner().Contains("Reconnecting", StringComparison.OrdinalIgnoreCase) ? Banner() : null, 20, "reconnecting");
        Check("reconnecting-shown", reconnecting is not null, Banner());
        await Frames(3);
        Check("focus-kept-on-redraw", Focused() is Button { Text: "Leave" } kept && !kept.IsQueuedForDeletion(), $"before: {before}; after the screen was redrawn: {Describe(Focused())}");
        await Shot("host-reconnecting");
        var rejoin = await Until(() => Banner().Contains("Could not reconnect") ? ButtonNamed("Rejoin now") : null, 40, "gave-up");
        Check("gave-up-offers-rejoin-now", rejoin is not null, Banner());
        await Shot("host-gave-up");
        Mark("gave-up");
        await Signal("restored", 60);
        await Press(ButtonNamed("Rejoin now"), Key.Enter, "rejoin-now-enter");
        var rejoined = await Until(() => Banner().StartsWith("Connection: Connected", StringComparison.Ordinal) ? Banner() : null, 30, "rejoin-now");
        Check("rejoin-now-works", rejoined is not null, Banner());
        Mark("rejoined");

        await Accessibility();
        Mark("a11y-done");

        // Phase 4: the server itself stops and starts again.
        await Signal("server-stopped", 120);
        var lost = await Until(() => Banner().Contains("Reconnecting", StringComparison.OrdinalIgnoreCase) || ButtonNamed("Rejoin now") is not null ? Banner() : null, 30, "server-stop");
        Check("server-stop-shown", lost is not null, Banner());
        Mark("server-stop-seen");
        await Signal("server-started", 120);
        await AfterServerRestart();
        await Shot("host-after-server-restart");
    }

    private async Task Guest()
    {
        await TypeInto(Field("ws://"), _server, "type-server");
        await TypeInto(Field("", placeholder: "match code"), _code ?? "", "type-code");
        await Press(ButtonNamed("Join match"), Key.Space, "join-space");
        var joined = await Until(() => Label("Match code ") is { } l && l.Text.Contains("Seat Two [B]") ? l.Text : null, 20, "join");
        Check("join", joined is not null && joined.Contains(_code ?? "?"), joined ?? "no match screen");
        Check("gaps-shown-on-join", GapsDialog()?.Visible == true, "rules dialog at the first match");
        await KeyPress(Key.Escape);
        await PlayZone();
        await Signal("close", 120);
        Mark("closing");
    }

    private async Task Reopen()
    {
        await TypeInto(Field("ws://"), _server, "type-server");
        await Press(ButtonNamed("Refresh saved seats"), Key.Enter, "refresh-enter");
        var saved = await Until(() => ButtonStarting("Rejoin match "), 10, "saved");
        Check("saved-seat-listed", saved is not null && saved.Text.Contains(_code ?? "?") && saved.Text.EndsWith("Seat Two", StringComparison.Ordinal),
            saved?.Text ?? "no saved seat button");
        await Press(saved, Key.Enter, "rejoin-saved-enter");
        var joined = await Until(() => Label("Match code ") is { } l && l.Text.Contains("Seat Two [B]") ? l.Text : null, 20, "reopen");
        Check("reopen-same-seat", joined is not null && joined.Contains(_code ?? "?"), joined ?? "no match screen");
        await KeyPress(Key.Escape);
        await Until(() => Banner().StartsWith("Connection: Connected", StringComparison.Ordinal) && !LogText().EndsWith("…") ? "ok" : null, 20, "synced");
        await Signal("compare", 120);
        await Until(() => Banner().Contains("paused") ? null : "ok", 30, "resume");
        await Frames(10);
        Dump("compare");
        Mark("compared");
        await Shot("reopen-compare");

        // While the host's connection is cut, this screen shows the match paused, then going on.
        await FocusByTab(ButtonNamed("Leave"), 60);
        var paused = await Until(() => Banner().Contains("paused", StringComparison.OrdinalIgnoreCase) ? Banner() : null, 60, "opponent-away");
        Check("opponent-away-on-cut", paused is not null, Banner());
        await Frames(3);
        Check("focus-kept-on-redraw", Focused() is Button { Text: "Leave" } kept && !kept.IsQueuedForDeletion(), $"after the screen was redrawn: {Describe(Focused())}");
        await Shot("reopen-opponent-away");
        var resumed = await Until(() => Banner().Contains("paused", StringComparison.OrdinalIgnoreCase) ? null : Banner(), 90, "opponent-back");
        Check("opponent-back-after-rejoin", resumed is not null, Banner());
        await Signal("server-stopped", 300);
        var lost = await Until(() => Banner().Contains("Reconnecting", StringComparison.OrdinalIgnoreCase) ? Banner() : null, 30, "server-stop");
        Check("server-stop-shown", lost is not null, Banner());
        Mark("server-stop-seen");
        await Signal("server-started", 120);
        await AfterServerRestart();
    }

    // Both seats: the deployment-zone choice is made with a hint button. The runner holds the proxy so that the
    // "sent" state can be seen before the server's answer arrives.
    private async Task PlayZone()
    {
        await Until(() => LogText().Contains("must decide: choose a deployment zone") ? "ok" : null, 20, "zone-decision");
        Dump("joined");
        Mark("joined");
        var hint = await Until(() => Hint() ?? (Section("YOUR ACTIONS").Contains("Nothing to do until the opponent moves") ? (Control)_main : null), 20, "hint-or-wait");
        if (hint is Button)
        {
            Mark("decider");
            await Signal("hold", 60);
            await Press(Hint(), Key.Enter, "hint-enter");
            var sent = await Until(() => Section("LAST COMMAND").StartsWith("SENT:", StringComparison.Ordinal) ? Section("LAST COMMAND") : null, 10, "sent");
            Check("hint-sent-shown", sent is not null, Section("LAST COMMAND"));
            Check("one-command-at-a-time", Hint() is null && Section("YOUR ACTIONS").Contains("waiting for the server's answer"), Section("YOUR ACTIONS").Replace('\n', '|'));
            await Shot($"{_role}-sent");
            Mark("sent");
            var done = await Until(() => Section("LAST COMMAND").StartsWith("DONE:", StringComparison.Ordinal) ? Section("LAST COMMAND") : null, 20, "done");
            Check("hint-done-shown", done is not null, Section("LAST COMMAND"));
        }
        await Until(() => LogText().Contains(" zone.") ? "ok" : null, 20, "zone-chosen");
        await Frames(5);
        Check($"zone-event-shown", LogText().Contains(" zone."), Lines(LogText()).LastOrDefault() ?? "");
        var actions = Section("YOUR ACTIONS");
        Check("positional-not-offered-explained", actions.Contains("M7.4") || actions.Contains("Nothing to do until the opponent moves"), actions.Replace('\n', '|'));
        await Shot($"{_role}-zone");
        Dump("zone");
        Mark("zone-done");
    }

    // An event line by the opponent of the host (the host is always Seat One): "[22] Two activates b1." or "[23] b1 moves …".
    // "Two activates first." is left out: the initiative holder, possibly the host, chooses it.
    private static bool IsOpponentEvent(string line)
    {
        var text = line[(line.IndexOf("] ", StringComparison.Ordinal) + 2)..];
        return (text.StartsWith("Two ", StringComparison.Ordinal) && text != "Two activates first.")
            || (text.Length > 2 && text[0] == 'b' && char.IsAsciiDigit(text[1]) && text[2] == ' ');
    }

    // The host activates and ends activations with hint buttons while trench-cli plays the other seat, until the first
    // turn is scored. The other seat's events arrive without a reconnect. Focus must survive the redraws they cause.
    private async Task PlayActivations()
    {
        var connections = 0;
        var presses = 0;
        var liveEvents = 0;
        var focusChecked = false;
        // The opponent's events (Seat Two and its models b1..b4) seen arriving while connected, whenever they arrive:
        // while the host waits for the opponent, and while it waits for the answer to its own command.
        var seen = Lines(LogText()).Count;
        void CountOpponentEvents()
        {
            var lines = Lines(LogText());
            if (Banner().StartsWith("Connection: Connected", StringComparison.Ordinal))
            {
                liveEvents += lines.Skip(seen).Count(IsOpponentEvent);
            }
            seen = Math.Max(seen, lines.Count);
        }
        // Play until a turn is scored after taking the seat back. trench-cli may already have finished the turn it played
        // for this seat (it is stopped from outside, so how far it gets depends on the machine); then the next turn counts.
        var start = seen;
        bool TurnScoredSinceStart() => Lines(LogText()).Skip(start).Any(l => l.Contains("scoring") || l.Contains("MATCH ENDED"));
        var deadline = Time.GetTicksMsec() + 240_000;
        while (Time.GetTicksMsec() < deadline && !TurnScoredSinceStart() && presses < 60)
        {
            var before = Lines(LogText()).Count;
            if (Hint() is { } hint)
            {
                await Press(hint, Key.Enter, null);
                presses++;
                await Until(() => Section("LAST COMMAND").StartsWith("SENT:", StringComparison.Ordinal) ? null : "answered", 20, "answer");
                CountOpponentEvents();
                if (!Section("LAST COMMAND").StartsWith("DONE:", StringComparison.Ordinal))
                {
                    Check("activation-hint-done", false, Section("LAST COMMAND"));
                }
                if (!focusChecked)
                {
                    // The pressed button is gone after the answer; the focus must land on another control, not vanish.
                    focusChecked = true;
                    await Frames(3);
                    Check("focus-after-hint-answered", Focused() is Button { Visible: true } next && !next.IsQueuedForDeletion(), $"focus after the answer: {Describe(Focused())}");
                }
                continue;
            }
            var changed = await Until(() => Lines(LogText()).Count > before || Hint() is not null ? "changed" : null, 30, "opponent");
            CountOpponentEvents();
            if (changed is null)
            {
                break;
            }
            if (!Banner().StartsWith("Connection: Connected", StringComparison.Ordinal))
            {
                connections++;
            }
        }
        Check("activation-hints-used", presses >= 2, $"{presses} hint buttons pressed (activate / end activation)");
        Check("live-opponent-events", liveEvents > 0 && connections == 0, $"{liveEvents} opponent event lines arrived while connected; {connections} times not connected");
        Check("turn-scored", TurnScoredSinceStart(), Lines(LogText()).LastOrDefault() ?? "");
        Check("vp-shown", Section("MATCH").Contains("VP "), Section("MATCH").Replace('\n', '|'));
        Check("event-list-shows-newest", EventListAtBottom(), $"event list scrolled to {EventList()?.ScrollVertical} of {EventList()?.GetVScrollBar().MaxValue}");
        await Shot("host-played");
    }

    private async Task GapsDialogChecks()
    {
        var gaps = GapsDialog();
        Check("gaps-shown-at-first-match", gaps?.Visible == true, gaps is null ? "no rules dialog" : $"visible={gaps.Visible}");
        Check("gaps-lists-rules", gaps is not null && gaps.DialogText.Split('\n').Length >= 8, $"{gaps?.DialogText.Split('\n').Length} lines");
        await Shot("host-gaps");
        await KeyPress(Key.Escape);
        Check("gaps-escape-closes", gaps?.Visible == false, $"visible={gaps?.Visible}");
        await KeyPress(Key.F1);
        Check("gaps-f1-opens", gaps?.Visible == true, $"visible={gaps?.Visible}");
        await KeyPress(Key.Enter);
        Check("gaps-enter-closes", gaps?.Visible == false, $"visible={gaps?.Visible} (Enter on the OK button)");
        await Press(ButtonNamed("Rules in this build (F1)"), Key.Space, "rules-button-space");
        Check("gaps-button-space-opens", gaps?.Visible == true, $"visible={gaps?.Visible}");
        await KeyPress(Key.Escape);
        Check("gaps-escape-closes-again", gaps?.Visible == false, $"visible={gaps?.Visible}");
    }

    // M7.4 stage 4a: the 3D battlefield shows the deployed match, its camera keeps the table in view, screen points map
    // back to the right inches, and models can be selected with keys and the mouse. Read-only: nothing is sent.
    private async Task BoardChecks()
    {
        var board = Controls<BoardView>().FirstOrDefault(b => b.IsVisibleInTree());
        var scene = board?.Scene;
        // Every model the MODELS column lists as on the table (deployed, not out of action) is on the battlefield.
        var onTable = Section("MODELS").Split('\n').Where(l => l.StartsWith('[') && !l.Contains("not deployed") && !l.Contains("Out of action")).ToList();
        Check("board-shown", scene is { Terrain.Count: 9, Zones.Count: 2 } && scene.Models.Count == onTable.Count && onTable.Count > 0
            && onTable.All(l => scene.Models.Any(m => l.StartsWith($"[{m.SeatLetter}] {m.Name}:", StringComparison.Ordinal)))
            && Section("BATTLEFIELD").Contains("Table 36.0\" x 36.0\""),
            scene is null ? "no battlefield" : $"{scene.Models.Count} models ({onTable.Count} on the table in MODELS), {scene.Terrain.Count} terrain pieces, {scene.Zones.Count} zones; {Section("BATTLEFIELD").Split('\n')[0]}");
        if (board is null || scene is null)
        {
            return;
        }
        var labels = All(board).OfType<Label3D>().Where(l => !l.IsQueuedForDeletion()).Select(l => l.Text).ToList();
        var unlabelled = scene.Models.Where(m => !labels.Any(t => t.Contains(m.Label, StringComparison.Ordinal))).Select(m => m.Name).ToList();
        Check("board-models-labelled", unlabelled.Count == 0 && scene.Models.All(m => m.Label.StartsWith($"[{m.SeatLetter}] ", StringComparison.Ordinal)),
            unlabelled.Count == 0 ? string.Join(" | ", scene.Models.Select(m => m.Label)) : $"no label for {string.Join(", ", unlabelled)}");
        var reached = await FocusByTab(board, 80);
        Check("board-tab-focus", reached, $"focus on {Describe(Focused())}");
        if (!reached)
        {
            return;
        }

        await KeyPress(Key.F);
        await Frames(3);
        Check("board-fit-angled", !board.TopDown && board.TableInView(0.01f) && AllModelsInView(board, scene), $"camera {board.CameraText}");
        await Shot("board-angled");
        Check("board-pick-roundtrip", PickRoundTrip(board, scene, out var angled), angled);

        var before = board.Camera.Position;
        await KeyPress(Key.Equal);
        await Frames(2);
        var zoomed = board.Camera.Position.DistanceTo(new Vector3(18, 0, 18)) < before.DistanceTo(new Vector3(18, 0, 18));
        await KeyPress(Key.Left);
        await Frames(2);
        var panned = board.Camera.Position.X < before.X - 0.5f;
        await KeyPress(Key.F);
        await Frames(3);
        Check("board-zoom-pan-fit-keys", zoomed && panned && board.TableInView(0.01f), $"zoomed in: {zoomed}; panned left: {panned}; F fits again: {board.TableInView(0.01f)}");

        await KeyPress(Key.E);
        await Frames(3);
        var turned = board.Yaw == 90 && board.CameraText.Contains("facing west", StringComparison.Ordinal) && board.TableInView(0.01f) && AllModelsInView(board, scene);
        await KeyPress(Key.Q);
        await Frames(3);
        Check("board-turn-keys", turned && board.Yaw == 0, $"E turned to 90 and kept the table in view: {turned}; Q back to {board.Yaw}");

        await KeyPress(Key.T);
        await Frames(3);
        var top = board.TopDown && board.TableInView(0.01f) && AllModelsInView(board, scene) && Section("BATTLEFIELD").Contains("camera top-down", StringComparison.Ordinal);
        Check("board-top-down", top, $"camera {board.CameraText}");
        await Shot("board-top-down");
        Check("board-pick-roundtrip-top-down", PickRoundTrip(board, scene, out var flat), flat);
        await KeyPress(Key.T);
        await Frames(3);

        await KeyPress(Key.N);
        await Frames(2);
        var first = board.Selected;
        var info = Section("BATTLEFIELD");
        Check("board-select-key", first is { Yours: true } && info.Contains($"Selected: {first.Text}", StringComparison.Ordinal)
            && Section("MODELS").Contains($"[{first.SeatLetter}] {first.Name}:", StringComparison.Ordinal)
            && All(board).OfType<Label3D>().Any(l => !l.IsQueuedForDeletion() && l.Text.StartsWith("SELECTED ", StringComparison.Ordinal)),
            first is null ? "nothing selected" : $"N selected {first.Name}; {info.Split('\n')[0]}");
        await KeyPress(Key.P);
        await Frames(2);
        Check("board-previous-key", board.Selected?.Id == scene.Next(first?.Id, forward: false)?.Id && board.Selected?.Id != first?.Id, $"P selected {board.Selected?.Name}");

        // A real mouse click (left button) on an opponent's model selects it.
        var target = scene.Models.First(m => !m.Yours);
        var local = board.ScreenOf(target.X, target.Y, target.Standing);
        var clicked = false;
        if (local is { } at)
        {
            var global = board.GetGlobalTransformWithCanvas() * at;
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = global, GlobalPosition = global });
                await Frames(2);
            }
            clicked = board.Selected?.Id == target.Id;
        }
        Check("board-click-selects", clicked, $"clicked at {local} for {target.Name}; selected {board.Selected?.Name ?? "nothing"}");
        Dump("board");
    }

    private static bool AllModelsInView(BoardView board, BoardScene scene) => scene.Models.All(m => board.InView(board.ScreenOf(m.X, m.Y, m.Standing)));

    // Each model's centre, shown on screen and turned back into a table point, lands within 0.05" and picks that model.
    private static bool PickRoundTrip(BoardView board, BoardScene scene, out string detail)
    {
        var worst = 0.0;
        var wrong = new List<string>();
        foreach (var m in scene.Models)
        {
            if (board.ScreenOf(m.X, m.Y, m.Standing) is not { } screen)
            {
                wrong.Add($"{m.Name} behind the camera");
                continue;
            }
            var back = board.TableAt(screen, m.Standing);
            worst = Math.Max(worst, back is { } p ? Math.Sqrt((p.X - m.X) * (p.X - m.X) + (p.Y - m.Y) * (p.Y - m.Y)) : 99);
            if (board.ModelAt(screen)?.Id != m.Id)
            {
                wrong.Add($"{m.Name} picked {board.ModelAt(screen)?.Name ?? "nothing"}");
            }
        }
        detail = $"{scene.Models.Count} models; largest error {worst.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}\"{(wrong.Count == 0 ? "" : "; " + string.Join(", ", wrong))}";
        return worst <= 0.05 && wrong.Count == 0;
    }

    private async Task Accessibility()
    {
        await ChooseOption(Option("Text 100%"), "Text 150%", "text-150");
        await Frames(5);
        Check("text-150-applied", _main.Theme?.DefaultFontSize == 27, $"default font size {_main.Theme?.DefaultFontSize}");
        var width = _main.GetViewportRect().Size.X;
        var overflow = Controls<Control>().Where(c => (c is Button or Godot.Label or OptionButton) && c.IsVisibleInTree() && c.GetGlobalRect().End.X > width + 1)
            .Select(Describe).ToList();
        Check("text-150-fits-width", overflow.Count == 0, overflow.Count == 0 ? $"all text within {width}px" : string.Join("; ", overflow));
        await Shot("host-text-150");
        await Press(Controls<CheckButton>().FirstOrDefault(c => c.Text == "High contrast"), Key.Space, "contrast-space");
        var background = Controls<ColorRect>().FirstOrDefault()?.Color;
        Check("high-contrast-applied", background == Colors.Black && _main.Theme?.GetColor("font_color", "Label") == Colors.White,
            $"background {background}, text {_main.Theme?.GetColor("font_color", "Label")}");
        await Shot("host-high-contrast");
        var log = EventList();
        var reached = await FocusByTab(log, 60);
        await KeyPress(Key.Home);
        var top = log?.ScrollVertical;
        await KeyPress(Key.End);
        await Frames(3);
        var bottom = log?.ScrollVertical;
        await KeyPress(Key.Pageup);
        var paged = log?.ScrollVertical;
        Check("event-list-scrolls-with-keys", reached && top == 0 && bottom > 0 && paged < bottom, $"Tab reached the event list: {reached}; Home {top}, End {bottom}, Page Up {paged}");
        await KeyPress(Key.End);
        await Frames(3);
        Check("event-list-end-returns-to-newest", EventListAtBottom(), $"after End: {log?.ScrollVertical} of {log?.GetVScrollBar().MaxValue}");
        var models = Section("MODELS").Split('\n');
        Check("seats-by-letter-not-colour", models.All(l => l.StartsWith("[A] ", StringComparison.Ordinal) || l.StartsWith("[B] ", StringComparison.Ordinal))
            && Section("MATCH").Contains("[A]") && Section("MATCH").Contains("[B]"), $"{models.Length} model lines, all with [A]/[B]");
        Check("feedback-in-words", Section("LAST COMMAND").Split(':')[0] is "DONE" or "SENT" or "REFUSED BY THE RULES" or "NOT TAKEN" or "OUT OF DATE" or "UNKNOWN" or "None yet.",
            Section("LAST COMMAND"));
        Check("connection-in-words", Banner().StartsWith("Connection: ", StringComparison.Ordinal), Banner());
        // Turn the settings back so later screenshots are comparable.
        await Press(Controls<CheckButton>().FirstOrDefault(c => c.Text == "High contrast"), Key.Space, null);
        await ChooseOption(Option("Text 150%"), "Text 100%", null);
    }

    // After the server restarted: automatic attempts (or Rejoin now, pressed with the keyboard once they stop) reach
    // the new server, and the screen must then say plainly what became of the match.
    private async Task AfterServerRestart()
    {
        var settled = await Until(() => Banner().Contains("Reconnecting", StringComparison.OrdinalIgnoreCase) ? null : Banner(), 90, "server-restart");
        if (ButtonNamed("Rejoin now") is { } rejoin)
        {
            await Press(rejoin, Key.Enter, "rejoin-now-after-restart-enter");
            await Frames(10);
            settled = await Until(() => Banner().Contains("Reconnecting", StringComparison.OrdinalIgnoreCase) ? null : Banner(), 60, "server-restart-rejoin");
        }
        Check("server-restart-clear", settled is not null && !Banner().StartsWith("Connection: Connected", StringComparison.Ordinal) && Hint() is null,
            $"{Banner()} || actions: {Section("YOUR ACTIONS")}");
        Dump("server-restart");
    }

    // ---- keyboard ----

    private async Task KeyPress(Key key, bool shift = false, bool ctrl = false)
    {
        foreach (var pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, ShiftPressed = shift, CtrlPressed = ctrl });
            await Frames(2);
        }
    }

    private async Task TypeText(string text)
    {
        foreach (var c in text)
        {
            var key = char.IsAsciiLetter(c) ? Key.A + (char.ToUpperInvariant(c) - 'A') : char.IsAsciiDigit(c) ? Key.Key0 + (c - '0') : Key.None;
            Input.ParseInputEvent(new InputEventKey { Keycode = key, Unicode = c, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = key, Unicode = c, Pressed = false });
            await Frames(1);
        }
    }

    // Moves the focus with Tab (forward) until it is on the control; fails the check when it never gets there.
    private async Task<bool> FocusByTab(Control? target, int limit)
    {
        if (target is null)
        {
            return false;
        }
        for (var i = 0; i < limit; i++)
        {
            if (Focused() == target)
            {
                return true;
            }
            await KeyPress(Key.Tab);
        }
        return Focused() == target;
    }

    private async Task Press(Control? target, Key key, string? id)
    {
        var reached = await FocusByTab(target, 60);
        if (id is not null)
        {
            Check(id, reached, reached ? $"Tab reached {Describe(target)}, then {key}" : $"Tab never reached {Describe(target)}");
        }
        if (reached)
        {
            await KeyPress(key);
        }
    }

    private async Task TypeInto(LineEdit? field, string text, string id, bool enter = false)
    {
        var reached = await FocusByTab(field, 60);
        if (reached)
        {
            await KeyPress(Key.A, ctrl: true);
            await TypeText(text);
            if (enter)
            {
                await KeyPress(Key.Enter);
            }
        }
        Check(id, reached && field!.Text == text, $"{Describe(field)} = \"{field?.Text}\"");
    }

    private async Task ChooseOption(OptionButton? option, string item, string? id)
    {
        var reached = await FocusByTab(option, 60);
        if (reached)
        {
            await KeyPress(Key.Enter);
            await Frames(3);
            for (var i = 0; i < option!.ItemCount && option.GetPopup().Visible && option.GetPopup().GetFocusedItem() is var at && (at < 0 || option.GetItemText(at) != item); i++)
            {
                await KeyPress(Key.Down);
            }
            await KeyPress(Key.Enter);
        }
        if (id is not null)
        {
            Check(id, option is not null && option.GetItemText(option.Selected) == item,
                $"{Describe(option)} selected \"{(option is null ? "" : option.GetItemText(option.Selected))}\" with Enter, Down, Enter");
        }
    }

    private async Task TabTraversal(string screen)
    {
        var expected = Controls<Control>().Where(c => c.FocusMode == Control.FocusModeEnum.All && c.IsVisibleInTree()
            && !(c is BaseButton { Disabled: true }) && c.GetWindow() == _main.GetWindow()).ToList();
        if (Focused() is null)
        {
            await KeyPress(Key.Tab);
        }
        var start = Focused();
        var forward = new List<Control>();
        for (var i = 0; i < expected.Count + 5 && Focused() is { } now; i++)
        {
            forward.Add(now);
            await KeyPress(Key.Tab);
            if (Focused() == start)
            {
                break;
            }
        }
        var missing = expected.Except(forward).Select(Describe).ToList();
        Check($"tab-reaches-every-control-{screen}", start is not null && missing.Count == 0 && Focused() == start,
            $"{forward.Count} stops: {string.Join(" > ", forward.Select(Describe))}{(missing.Count == 0 ? "" : $"; never reached: {string.Join(", ", missing)}")}");
        var backward = new List<Control>();
        for (var i = 0; i < forward.Count; i++)
        {
            await KeyPress(Key.Tab, shift: true);
            backward.Add(Focused()!);
        }
        var reversed = Enumerable.Reverse(forward).ToList();
        Check($"shift-tab-reverses-{screen}", backward.SequenceEqual(reversed), $"{string.Join(" < ", backward.Select(Describe))}");
    }

    // ---- reading the screen ----

    private ScrollContainer? EventList() => Controls<ScrollContainer>().FirstOrDefault(c => c.FocusMode == Control.FocusModeEnum.All && c.IsVisibleInTree());

    private bool EventListAtBottom() => EventList() is { } list && list.ScrollVertical >= list.GetVScrollBar().MaxValue - list.GetVScrollBar().Page - 2;

    private Control? Focused() => _main.GetViewport().GuiGetFocusOwner();

    private IEnumerable<T> Controls<T>() where T : Node => All(_main).OfType<T>();

    private static IEnumerable<Node> All(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var below in All(child))
            {
                yield return below;
            }
        }
    }

    private Button? ButtonNamed(string text) => Controls<Button>().FirstOrDefault(b => b.Text == text && b.IsVisibleInTree() && !b.IsQueuedForDeletion());

    private Button? ButtonStarting(string text) => Controls<Button>().FirstOrDefault(b => b.Text.StartsWith(text, StringComparison.Ordinal) && b.IsVisibleInTree() && !b.IsQueuedForDeletion());

    private Button? Hint() => Controls<Button>().FirstOrDefault(b => b.Text.EndsWith("(hint; the server decides)", StringComparison.Ordinal) && b.IsVisibleInTree() && !b.IsQueuedForDeletion());

    private LineEdit? Field(string startsWith, string? placeholder = null) => Controls<LineEdit>().FirstOrDefault(l => l.IsVisibleInTree() && l.GetParent() is not SpinBox
        && (placeholder is null ? l.Text.StartsWith(startsWith, StringComparison.Ordinal) : l.PlaceholderText == placeholder));

    private SpinBox? Spin() => Controls<SpinBox>().FirstOrDefault(s => s.IsVisibleInTree());

    private LineEdit? SpinLine() => Spin()?.GetLineEdit();

    private OptionButton? Option(string selected) => Controls<OptionButton>().FirstOrDefault(o => o.IsVisibleInTree() && o.GetItemText(o.Selected) == selected);

    private Godot.Label? Label(string startsWith) => Controls<Godot.Label>().FirstOrDefault(l => l.IsVisibleInTree() && l.Text.StartsWith(startsWith, StringComparison.Ordinal));

    private string Banner() => Label("Connection: ")?.Text ?? "";

    // The body of a section: the control after its title label.
    private string Section(string title)
    {
        var heading = Controls<Godot.Label>().FirstOrDefault(l => l.IsVisibleInTree() && l.Text.StartsWith(title, StringComparison.Ordinal) && l.GetParent() is VBoxContainer);
        if (heading?.GetParent() is not { } box || heading.GetIndex() + 1 >= box.GetChildCount())
        {
            return "";
        }
        var body = box.GetChild(heading.GetIndex() + 1);
        return string.Join("\n", (body is Godot.Label own ? [own] : All(body).OfType<Godot.Label>()).Where(l => l.IsVisibleInTree() && !l.IsQueuedForDeletion()).Select(l => l.Text).Where(t => t.Length > 0));
    }

    private string LogText() => Section("EVENTS");

    private static List<string> Lines(string text) => [.. text.Split('\n').Where(l => l.StartsWith('['))];

    private AcceptDialog? GapsDialog() => Controls<AcceptDialog>().FirstOrDefault(d => d is not ConfirmationDialog && d.Title == "Rules in this build");

    private T? Dialog<T>() where T : AcceptDialog => Controls<T>().FirstOrDefault(d => d.Visible);

    private static string Describe(Control? control) => control switch
    {
        null => "nothing",
        Button b => $"{b.GetType().Name} \"{b.Text}\"",
        LineEdit { PlaceholderText.Length: > 0 } l => $"LineEdit \"{l.PlaceholderText}\"",
        LineEdit l when l.GetParent() is SpinBox => "SpinBox",
        LineEdit l => $"LineEdit \"{l.Text}\"",
        BoardView => "Battlefield",
        Godot.Label l => $"Label \"{(l.Text.Length > 40 ? l.Text[..40] + "…" : l.Text)}\"",
        _ => control.GetType().Name,
    };

    // ---- waiting, reporting ----

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    // Checks the condition every frame until it holds or the time runs out (bounded; no fixed sleeps).
    private async Task<T?> Until<T>(Func<T?> condition, double seconds, string what) where T : class
    {
        var deadline = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (true)
        {
            if (condition() is { } value)
            {
                return value;
            }
            if (Time.GetTicksMsec() > deadline)
            {
                Write($"WAIT {what} timed out after {seconds}s");
                return null;
            }
            await Frames(1);
        }
    }

    private async Task Signal(string name, double seconds)
    {
        var path = System.IO.Path.Combine(_out, $"signal-{name}");
        Write($"WAIT signal {name}");
        if (await Until(() => System.IO.File.Exists(path) ? "ok" : null, seconds, $"signal-{name}") is null)
        {
            throw new TimeoutException($"the runner never sent '{name}'");
        }
    }

    private void Check(string id, bool pass, string detail)
    {
        if (!pass)
        {
            _failures++;
        }
        Write($"CHECK {id} {(pass ? "PASS" : "FAIL")} {detail.Replace('\n', '|')}");
    }

    private void Mark(string name, string value = "") => Write($"MARK {name} {value}".TrimEnd());

    private void Dump(string tag)
    {
        Write($"DUMP {tag} code={Label("Match code ")?.Text}");
        Write($"DUMP {tag} banner={Banner().Replace('\n', '|')}");
        Write($"DUMP {tag} match={Section("MATCH").Replace('\n', '|')}");
        Write($"DUMP {tag} models={Section("MODELS").Replace('\n', '|')}");
        Write($"DUMP {tag} log={string.Join("|", Lines(LogText()))}");
    }

    private async Task Shot(string name)
    {
        if (!_screens)
        {
            return;
        }
        await Frames(3);
        var path = System.IO.Path.Combine(_out, $"{++_shots:00}-{name}.png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        Write($"SHOT {path}");
    }

    private void Write(string line)
    {
        GD.Print($"UICHECK {_role} {line}");
        System.IO.File.AppendAllText(System.IO.Path.Combine(_out, $"{_role}.log"), $"{DateTime.UtcNow:HH:mm:ss.fff} {line}\n");
    }
}
