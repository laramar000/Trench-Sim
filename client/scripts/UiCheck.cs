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
        Check("positional-not-offered-explained", actions.Contains("on the battlefield") || actions.Contains("Nothing to do until the opponent moves"), actions.Replace('\n', '|'));
        await Shot($"{_role}-zone");
        Dump("zone");
        await PlacementPreviewChecks();
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
        var previewsChecked = false;
        var meleeTried = new HashSet<string>();
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
        bool MatchEnded() => Lines(LogText()).Skip(start).Any(l => l.Contains("MATCH ENDED"));
        var earlier = Lines(LogText()).Count(IsOpponentEvent);
        // On a fast machine trench-cli can finish the opponent's activations of this turn before the host takes its seat
        // back; then the turn is scored with no opponent event to see, so play on into the next turn until one arrives.
        var deadline = Time.GetTicksMsec() + 240_000;
        while (Time.GetTicksMsec() < deadline && !(TurnScoredSinceStart() && (liveEvents > 0 || MatchEnded())) && presses < 60)
        {
            var before = Lines(LogText()).Count;
            if (!previewsChecked && Hint() is not null && Controls<BoardView>().FirstOrDefault(b => b.IsVisibleInTree()) is { } board
                && board.Interaction.Available.Contains(InteractionMode.Move))
            {
                previewsChecked = true;
                await ActionPreviewChecks(board);
                CountOpponentEvents();
                continue;
            }
            if (previewsChecked && Hint() is not null && Controls<BoardView>().FirstOrDefault(b => b.IsVisibleInTree()) is { } later)
            {
                if (later.Interaction.Available.Contains(InteractionMode.Charge) && !(_shootSent && _chargeSent && _fightSent) && !meleeTried.Contains(later.Interaction.Available.Count + ":" + Section("MATCH")))
                {
                    meleeTried.Add(later.Interaction.Available.Count + ":" + Section("MATCH"));
                    await MeleeFromTheBattlefield(later);
                    CountOpponentEvents();
                    continue;
                }
            }
            // A on the battlefield activates the selected model, at the first chance (before or after the previews).
            if (!_activateChecked && Hint() is { } activate && activate.Text.StartsWith("Activate ", StringComparison.Ordinal)
                && Controls<BoardView>().FirstOrDefault(b => b.IsVisibleInTree()) is { } chooser && await ActivateByKey(chooser))
            {
                presses++;
                CountOpponentEvents();
                continue;
            }
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
        Check("live-opponent-events", liveEvents > 0 && connections == 0, $"{liveEvents} opponent event lines arrived while connected; {connections} times not connected; {earlier} opponent event lines before the host played");
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

    // M7.4 stage 4b, on the deploying seat's turn: placing a model is previewed with the keyboard cursor, inside and
    // outside the deployment zone, with the rule's reason when refused; getting it ready sends nothing.
    private async Task PlacementPreviewChecks()
    {
        // Who deploys first is settled by a roll-off after the zone is chosen; wait until this client has seen it.
        await Until(() => LogText().Contains(" deploys first.", StringComparison.Ordinal) ? "ok" : null, 20, "deployment-order");
        await Frames(3);
        var board = Controls<BoardView>().FirstOrDefault(b => b.IsVisibleInTree());
        if (board is null || !board.Interaction.Available.Contains(InteractionMode.Place))
        {
            Mark("not-deploying");
            return;
        }
        Mark("deploying");
        var lastCommand = Section("LAST COMMAND");
        var events = Lines(LogText()).Count;
        if (!await FocusByTab(board, 80))
        {
            Check("board-place-preview", false, $"Tab never reached the battlefield; focus on {Describe(Focused())}");
            return;
        }
        var interaction = board.Interaction;
        await KeyPress(Key.L);
        var preview = interaction.Preview;
        Check("board-place-preview", interaction.Mode == InteractionMode.Place && preview is { Legal: true } && OverlayHas(board, $"PLACE LOOKS LEGAL ({Preview.Caveat})")
            && Section("BATTLEFIELD").Contains(preview.Headline, StringComparison.Ordinal),
            $"cursor {interaction.Cursor}; {preview?.Headline}; labels: {string.Join(" / ", board.OverlayTexts)}");
        await Shot($"{_role}-place-preview");

        var start = interaction.Cursor;
        await KeyPress(Key.Right);
        var half = interaction.Cursor;
        await KeyPress(Key.Right, shift: true);
        var two = interaction.Cursor;
        Check("board-cursor-steps", Near(half, start.X + 0.5, start.Y) && Near(two, start.X + 2.5, start.Y) && Section("BATTLEFIELD").Contains($"Cursor at {two}.", StringComparison.Ordinal),
            $"start {start}; Right {half}; Shift+Right {two}");

        // Towards the middle of the table, 2" at a time, until the preview refuses (outside the zone).
        var (towards, back) = Math.Abs(18 - start.X) > Math.Abs(18 - start.Y)
            ? (start.X < 18 ? (Key.Right, Key.Left) : (Key.Left, Key.Right))
            : (start.Y < 18 ? (Key.Down, Key.Up) : (Key.Up, Key.Down));
        var steps = 0;
        while (steps < 14 && interaction.Preview is { Legal: true })
        {
            await KeyPress(towards, shift: true);
            steps++;
        }
        var refused = interaction.Preview;
        Check("board-place-refused-reason", refused?.Refusal is { } r && OverlayHas(board, $"PLACE REFUSED ({Preview.Caveat})") && OverlayHas(board, r.Text)
            && Section("BATTLEFIELD").Contains($"{r.Text} ({r.RuleRef}; {Preview.Caveat})", StringComparison.Ordinal),
            $"after {steps} steps of 2\" to {interaction.Cursor}: {refused?.Headline}");
        await Shot($"{_role}-place-refused");
        // Stage 4c: a refusing preview is not sent on the first confirmation.
        await KeyPress(Key.Space);
        Check("board-refused-preview-not-sent", interaction.Pending is null && interaction.Answer is null
            && (interaction.Notice ?? "").StartsWith("Not sent: the preview says the server would refuse.", StringComparison.Ordinal)
            && Section("LAST COMMAND") == lastCommand && Lines(LogText()).Count == events,
            $"notice: {interaction.Notice}; last command unchanged: {Section("LAST COMMAND") == lastCommand}; events {events} -> {Lines(LogText()).Count}");
        await KeyPress(Key.Escape);
        Check("board-escape-cancels", interaction.Mode == InteractionMode.None && board.OverlayTexts.Count == 0 && !All(board).OfType<Label3D>().Any(l => !l.IsQueuedForDeletion() && l.Text.Contains(Preview.Caveat, StringComparison.Ordinal)),
            $"mode {interaction.Mode}; {board.OverlayTexts.Count} preview labels left");

        // Confirmed twice, it is sent anyway, and the server's refusal is shown (the server decides, not the preview).
        await KeyPress(Key.L);
        steps = 0;
        while (steps < 14 && interaction.Preview is { Legal: true })
        {
            await KeyPress(towards, shift: true);
            steps++;
        }
        await KeyPress(Key.Space);
        await KeyPress(Key.Space);
        var refusedAnswer = await Answered(lastCommand);
        Check("board-send-refused-shown", refusedAnswer.StartsWith("REFUSED BY THE RULES:", StringComparison.Ordinal)
            && (interaction.Notice ?? "").StartsWith("REFUSED BY THE RULES:", StringComparison.Ordinal) && Section("BATTLEFIELD").Contains(interaction.Notice!, StringComparison.Ordinal)
            && OverlayHas(board, "REFUSED BY THE RULES:") && interaction.Mode == InteractionMode.Place && Lines(LogText()).Count == events,
            $"last command: {refusedAnswer}; battlefield: {interaction.Notice}; events {events} -> {Lines(LogText()).Count}");
        await Shot($"{_role}-place-refused-by-server");

        // Back to the zone centre: one confirmation sends the legal placement once, and the server's events place it.
        for (var i = 0; i < steps; i++)
        {
            await KeyPress(back, shift: true);
        }
        var placing = interaction.Preview;
        var before = Section("LAST COMMAND");
        await KeyPress(Key.Space);
        var placed = await Answered(before);
        var deployedLine = await Until(() => Lines(LogText()).Skip(events).FirstOrDefault(l => l.Contains(" sets up ", StringComparison.Ordinal)), 10, "placed");
        await Frames(3);
        var shape = placing is null ? null : board.Scene?.Find(placing.Model);
        Check("board-send-place", placing is { Legal: true } && placed.StartsWith("DONE:", StringComparison.Ordinal) && placed.Contains(Interaction.Describe(placing), StringComparison.Ordinal) && deployedLine is not null
            && shape is not null && Near(new TablePoint(shape.X, shape.Y), placing.Path[0].At.X, placing.Path[0].At.Y)
            && (interaction.Notice ?? "").StartsWith("DONE: Place ", StringComparison.Ordinal) && Lines(LogText()).Skip(events).Count(l => l.Contains(" sets up ", StringComparison.Ordinal)) == 1,
            $"last command: {placed}; event: {deployedLine}; on the battlefield: {shape?.Text ?? "missing"}; notice: {interaction.Notice}");
        await Shot($"{_role}-placed");
    }

    // The server's answer to the command just sent: the LAST COMMAND text once it is neither the old text nor SENT.
    private async Task<string> Answered(string before) =>
        await Until(() => Section("LAST COMMAND") is var now && now != before && !now.StartsWith("SENT:", StringComparison.Ordinal) ? now : null, 20, "answer") ?? Section("LAST COMMAND");

    // M7.4 stage 4b, during the host's own activation: Move with waypoints (keys and mouse), Shoot with the rules' sight
    // rays and target cycling (keys and mouse), and Charge with its reach. Nothing is sent.
    private async Task ActionPreviewChecks(BoardView board)
    {
        var lastCommand = Section("LAST COMMAND");
        var interaction = board.Interaction;
        if (!await FocusByTab(board, 80) || board.Scene is not { } scene)
        {
            Check("board-move-preview", false, $"Tab never reached the battlefield; focus on {Describe(Focused())}");
            return;
        }
        await KeyPress(Key.M);
        var actor = scene.Find(interaction.Actor);
        var startOk = actor is not null && Near(interaction.Cursor, actor.X, actor.Y);
        await KeyPress(Key.Right);
        await KeyPress(Key.Right);
        await KeyPress(Key.Enter);
        await KeyPress(Key.Down);
        await KeyPress(Key.Down);
        var move = interaction.Preview;
        var verdict = move is { Legal: true } ? "LOOKS LEGAL" : "REFUSED";
        Check("board-move-preview", startOk && move is { Kind: PreviewKind.Move } && move.Path.Count == 3 && interaction.Waypoints.Count == 1
            && OverlayHas(board, $"MOVE {verdict} ({Preview.Caveat})") && OverlayHas(board, $"{BoardScene.Inch(move.Path[^1].Inches)} used")
            && board.OverlayTexts.Contains(BoardScene.Inch(move.Path[1].Inches)) && Section("BATTLEFIELD").Contains(move.Headline, StringComparison.Ordinal),
            $"from {actor?.Text}; {move?.Headline}; path {string.Join(" -> ", move?.Path.Select(p => $"{p.At} {BoardScene.Inch(p.Inches)}") ?? [])}; labels: {string.Join(" / ", board.OverlayTexts)}");
        await Shot("host-move-preview");
        await KeyPress(Key.Backspace);
        var removed = interaction.Waypoints.Count == 0;

        // The mouse: moving over the table moves the cursor (throttled), and a click fixes a waypoint there.
        var hoverAt = (X: Math.Clamp((actor?.X ?? 18) + 1.5, 1, 35), Y: Math.Clamp((actor?.Y ?? 18) - 1.0, 1, 35));
        var hovered = false;
        var clickedPoint = false;
        if (board.ScreenOf(hoverAt.X, hoverAt.Y) is { } local)
        {
            var global = board.GetGlobalTransformWithCanvas() * local;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = global, GlobalPosition = global });
            hovered = await Until(() => Near(interaction.Cursor, hoverAt.X, hoverAt.Y, 0.15) ? "ok" : null, 3, "hover") is not null;
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = global, GlobalPosition = global });
                await Frames(2);
            }
            clickedPoint = interaction.Waypoints.Count == 1 && Near(interaction.Waypoints[0], hoverAt.X, hoverAt.Y, 0.15);
        }
        Check("board-mouse-path", removed && hovered && clickedPoint, $"Backspace removed the waypoint: {removed}; hover moved the cursor to {interaction.Cursor} (wanted ({hoverAt.X:0.0}, {hoverAt.Y:0.0})): {hovered}; click fixed a waypoint: {clickedPoint}");
        await KeyPress(Key.Escape);

        // Shoot: the rules' own sight rays, distance and cover, on the battlefield and in the text.
        await KeyPress(Key.S);
        var targets = interaction.Targets();
        Preview? shot = null;
        for (var i = 0; i < targets.Count; i++)
        {
            shot = interaction.Preview;
            if (shot is { Rays.Count: > 0 })
            {
                break;
            }
            await KeyPress(Key.N);
        }
        var early = shot?.Refusal?.Code is Rules.Core.ReasonCode.AttackerEngaged or Rules.Core.ReasonCode.NoRangedWeapon;
        var sightShown = shot is { Rays.Count: > 0 } && OverlayHas(board, $"SHOOT {(shot.Legal ? "LOOKS LEGAL" : "REFUSED")} ({Preview.Caveat})")
            && OverlayHas(board, $"of {shot.Rays.Count} sight rays clear") && OverlayHas(board, "Distance ") && Section("BATTLEFIELD").Contains(shot.Headline, StringComparison.Ordinal);
        Check("board-shoot-preview", sightShown || (early && OverlayHas(board, shot!.Refusal!.Text)),
            $"{shot?.Headline}; {shot?.Rays.Count} rays, {shot?.Rays.Count(r => r.Clear)} clear; labels: {string.Join(" / ", board.OverlayTexts)}");
        await Shot("host-shoot-preview");
        var current = interaction.Target;
        await KeyPress(Key.N);
        var expected = current is { } c && targets.IndexOf(c) is var at && at >= 0 ? targets[(at + 1) % targets.Count] : (Rules.Core.State.ModelId?)null;
        var cycled = targets.Count < 2 || interaction.Target == expected;
        var other = targets.Where(t => t != interaction.Target).Select(t => scene.Find(t)).FirstOrDefault(m => m is not null && board.ScreenOf(m.X, m.Y, m.Standing) is not null);
        var picked = targets.Count < 2;
        if (other is not null && board.ScreenOf(other.X, other.Y, other.Standing) is { } spot)
        {
            var global = board.GetGlobalTransformWithCanvas() * spot;
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = global, GlobalPosition = global });
                await Frames(2);
            }
            picked = interaction.Target == other.Id;
        }
        Check("board-target-cycle-and-click", targets.Count > 0 && cycled && picked, $"{targets.Count} enemy targets; N went to the next: {cycled}; a click picked {other?.Name ?? "nobody"}: {picked}");

        await KeyPress(Key.C);
        var charge = interaction.Preview;
        var chargeShown = charge is not null && OverlayHas(board, $"CHARGE {(charge.Legal ? "LOOKS LEGAL" : "REFUSED")} ({Preview.Caveat})")
            && (charge.Legal ? OverlayHas(board, "Charge path ") && OverlayHas(board, ": reaches on ") : OverlayHas(board, charge.Refusal!.Text));
        Check("board-charge-preview", chargeShown, $"{charge?.Headline}; {string.Join(" ", charge?.Facts ?? [])}; labels: {string.Join(" / ", board.OverlayTexts)}");
        await Shot("host-charge-preview");
        await KeyPress(Key.Escape);
        Check("board-previews-send-nothing", interaction.Mode == InteractionMode.None && board.OverlayTexts.Count == 0 && Section("LAST COMMAND") == lastCommand && Hint() is not null,
            $"mode {interaction.Mode}; last command unchanged: {Section("LAST COMMAND") == lastCommand}; hint still offered: {Hint()?.Text}");
        Dump("previews");

        // Stage 4c: a Move with the keyboard cursor, sent once with Space; the server's events move the model.
        var moved = await SendFirstLegalMove(board);
        Check("board-send-move", moved.Pass, moved.Detail);
        await Shot("host-moved");

        // Then a Shoot at the first target the preview says is legal (if any), sent with Space.
        _shootSent = await SendFirstLegalTarget(board, Key.S, "board-send-shoot");
    }

    // M (Move), then half-inch steps towards the nearest enemy for as long as the preview looks legal (so a shot may come
    // into range), or else a short step in any direction that looks legal; Space sends it.
    private async Task<(bool Pass, string Detail)> SendFirstLegalMove(BoardView board)
    {
        var interaction = board.Interaction;
        var tries = new List<(Key Key, int Presses, bool Furthest)>();
        if (board.Scene is { } scene && scene.Models.FirstOrDefault(m => m.Active) is { } me
            && scene.Models.Where(m => !m.Yours).OrderBy(m => (m.X - me.X) * (m.X - me.X) + (m.Y - me.Y) * (m.Y - me.Y)).FirstOrDefault() is { } enemy)
        {
            // The camera faces north (yaw 0): Right is +x and Up is -y on the table.
            var key = Math.Abs(enemy.X - me.X) >= Math.Abs(enemy.Y - me.Y) ? (enemy.X > me.X ? Key.Right : Key.Left) : (enemy.Y > me.Y ? Key.Down : Key.Up);
            tries.Add((key, 16, true));
        }
        tries.AddRange(new[] { (Key.Right, 2, false), (Key.Left, 2, false), (Key.Up, 2, false), (Key.Down, 2, false), (Key.Right, 4, false), (Key.Left, 4, false), (Key.Up, 4, false), (Key.Down, 4, false) });
        foreach (var (key, presses, furthest) in tries)
        {
            await KeyPress(Key.M);
            var legal = 0;
            for (var i = 0; i < presses; i++)
            {
                await KeyPress(key);
                if (furthest)
                {
                    if (interaction.Preview is not { Legal: true })
                    {
                        break;
                    }
                    legal++;
                }
            }
            if (furthest && legal > 0 && interaction.Preview is not { Legal: true })
            {
                // One step back to the last point the preview called legal.
                await KeyPress(key switch { Key.Right => Key.Left, Key.Left => Key.Right, Key.Up => Key.Down, _ => Key.Up });
            }
            if (interaction.Preview is not { Legal: true } preview)
            {
                await KeyPress(Key.Escape);
                continue;
            }
            var events = Lines(LogText()).Count;
            var before = Section("LAST COMMAND");
            await KeyPress(Key.Space);
            var answer = await Answered(before);
            var line = await Until(() => Lines(LogText()).Skip(events).FirstOrDefault(l => l.Contains(" moves ", StringComparison.Ordinal)), 10, "moved");
            await Frames(3);
            var end = preview.Path[^1].At;
            var shape = board.Scene?.Find(preview.Model);
            var pass = answer.StartsWith("DONE:", StringComparison.Ordinal) && answer.Contains(Interaction.Describe(preview), StringComparison.Ordinal) && line is not null && shape is not null && Near(new TablePoint(shape.X, shape.Y), end.X, end.Y)
                && (interaction.Notice ?? "").StartsWith("DONE: Move ", StringComparison.Ordinal) && interaction.Mode == InteractionMode.None;
            return (pass, $"{preview.Headline}; last command: {answer}; event: {line}; now {shape?.Text}; notice: {interaction.Notice}");
        }
        return (false, "no direction gave a legal Move preview within 2\"");
    }

    // The action key, then N through the targets until the preview looks legal; Space sends it. Reports the check only
    // when some target is legal (otherwise the scenario offers nothing to send, and the check stays NOT TESTED).
    private async Task<bool> SendFirstLegalTarget(BoardView board, Key action, string id)
    {
        var interaction = board.Interaction;
        await KeyPress(action);
        var count = interaction.Targets().Count;
        for (var i = 0; i < count && interaction.Preview is not { Legal: true }; i++)
        {
            await KeyPress(Key.N);
        }
        if (interaction.Preview is not { Legal: true } preview)
        {
            Mark($"{id}-nothing-legal", interaction.Preview?.Headline ?? "no target");
            await KeyPress(Key.Escape);
            return false;
        }
        var events = Lines(LogText()).Count;
        var before = Section("LAST COMMAND");
        await KeyPress(Key.Space);
        var answer = await Answered(before);
        await Until(() => Lines(LogText()).Count > events ? "ok" : null, 10, "events");
        var word = $"DONE: {Interaction.Describe(preview)}";
        Check(id, answer.StartsWith("DONE:", StringComparison.Ordinal) && answer.Contains(Interaction.Describe(preview), StringComparison.Ordinal)
            && (interaction.Notice ?? "").StartsWith(word, StringComparison.Ordinal) && Lines(LogText()).Count > events,
            $"{preview.Headline}; last command: {answer}; new events: {string.Join(" / ", Lines(LogText()).Skip(events).Take(4))}");
        await Shot($"host-{id}");
        return true;
    }

    // Stage 4c, in later activations of the host: A activates the selected model (once), and a Charge or a Fight is sent
    // when the preview says one is legal (only when the match offers one).
    private bool _activateChecked;
    private bool _shootSent;
    private bool _chargeSent;
    private bool _fightSent;

    private async Task<bool> ActivateByKey(BoardView board)
    {
        if (_activateChecked || board.Scene is not { } scene || !await FocusByTab(board, 80))
        {
            return false;
        }
        var hints = Controls<Button>().Where(b => b.IsVisibleInTree() && b.Text.StartsWith("Activate ", StringComparison.Ordinal)).Select(b => b.Text).ToList();
        for (var i = 0; i < scene.Models.Count && !(board.Selected is { Yours: true } m && hints.Any(h => h.StartsWith($"Activate {m.Name} ", StringComparison.Ordinal))); i++)
        {
            await KeyPress(Key.N);
        }
        if (board.Selected is not { Yours: true } chosen || !hints.Any(h => h.StartsWith($"Activate {chosen.Name} ", StringComparison.Ordinal)))
        {
            return false;
        }
        _activateChecked = true;
        var before = Section("LAST COMMAND");
        await KeyPress(Key.A);
        var answer = await Answered(before);
        await Until(() => board.Interaction.Available.Contains(InteractionMode.Move) ? "ok" : null, 10, "active");
        Check("board-activate-key", answer.StartsWith("DONE:", StringComparison.Ordinal) && answer.Contains($"Activate {chosen.Id.Value}", StringComparison.Ordinal) && board.Interaction.Available.Contains(InteractionMode.Move),
            $"N selected {chosen.Name}; A: {answer}");
        return true;
    }

    private async Task MeleeFromTheBattlefield(BoardView board)
    {
        if (!await FocusByTab(board, 80))
        {
            return;
        }
        if (!_shootSent)
        {
            // A Shoot excludes a Charge or a Fight in the same activation, so the melee actions wait for another one.
            _shootSent = await SendFirstLegalTarget(board, Key.S, "board-send-shoot");
            if (_shootSent)
            {
                return;
            }
        }
        if (!_chargeSent)
        {
            _chargeSent = await SendFirstLegalTarget(board, Key.C, "board-send-charge");
            if (_chargeSent)
            {
                // The charge is resolved by the server (rolls, then the move); wait for it before fighting.
                await Until(() => (LogText().Contains(" charges to ", StringComparison.Ordinal) || LogText().Contains("the charge fails", StringComparison.Ordinal)) && Hint() is not null ? "ok" : null, 20, "charge-resolved");
            }
        }
        if (!_fightSent && board.Interaction.Available.Contains(InteractionMode.Fight))
        {
            _fightSent = await SendFirstLegalTarget(board, Key.X, "board-send-fight");
        }
    }

    private static bool Near(TablePoint p, double x, double y, double within = 0.001) => Math.Abs(p.X - x) <= within && Math.Abs(p.Y - y) <= within;

    private bool OverlayHas(BoardView board, string text) =>
        board.OverlayTexts.Any(t => t.Contains(text, StringComparison.Ordinal))
        && All(board).OfType<Label3D>().Any(l => !l.IsQueuedForDeletion() && l.Text.Contains(text, StringComparison.Ordinal));

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
