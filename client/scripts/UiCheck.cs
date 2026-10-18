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
                case "hotseat":
                    await HotSeatPlay();
                    break;
                case "pvp-a":
                    await PvpCreate();
                    break;
                case "pvp-b":
                    await PvpJoin();
                    break;
                case "pvp-b-rejoin":
                    await PvpRejoin();
                    break;
                case "gvt-a":
                    // M7.5 stage 5c: the same play as pvp-a, against trench-cli --bot.
                    await PvpCreate();
                    break;
                case "tm-a" or "tm-b":
                    // The test center's guided test match (tools/TestCenter), played here in place of a person: the
                    // window connected itself (--test-match); this plays the match from there.
                    await TestMatchPlay();
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
        await WindowSizeChecks();
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
        // The previews also need one fresh activation of the host's (see below), so play on until it has had one.
        while (Time.GetTicksMsec() < deadline && !(TurnScoredSinceStart() && (liveEvents > 0 || MatchEnded()) && (previewsChecked || MatchEnded())) && presses < 60)
        {
            var before = Lines(LogText()).Count;
            // The previews need an activation that has done nothing yet: when the host takes its seat back in the middle
            // of an activation trench-cli started (it already moved or shot), they wait for the host's next activation.
            if (!previewsChecked && Hint() is not null && Controls<BoardView>().FirstOrDefault(b => b.IsVisibleInTree()) is { } board
                && board.Interaction.Available.Contains(InteractionMode.Move)
                && Section("MATCH").Contains("is activating. Done so far: nothing yet", StringComparison.Ordinal))
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
        // Proposal G3: the Menu is folded at first (the match code, settings and keys are not on screen), and a click on
        // Menu shows them; the rules button is pressed from there.
        var main = (Main)_main;
        var code = Controls<Godot.Label>().FirstOrDefault(l => l.Text.StartsWith("Match code ", StringComparison.Ordinal));
        Check("hud-menu-folded", !main.MenuOpen && code is not null && !code.IsVisibleInTree() && Option("Text 100%") is null && Strip().Length > 0,
            $"menu open {main.MenuOpen}; code line shown {code?.IsVisibleInTree()}; HUD: {Strip()}");
        await ClickButton(main.MenuButton);
        await Frames(2);
        var opened = main.MenuOpen && code?.IsVisibleInTree() == true && Option("Text 100%") is not null && ButtonNamed("Rules in this build (F1)")?.IsVisibleInTree() == true;
        await Press(ButtonNamed("Rules in this build (F1)"), Key.Space, "rules-button-space");
        Check("gaps-button-space-opens", gaps?.Visible == true, $"visible={gaps?.Visible}");
        await KeyPress(Key.Escape);
        Check("gaps-escape-closes-again", gaps?.Visible == false, $"visible={gaps?.Visible}");
        await ClickButton(main.MenuButton);
        await Frames(2);
        Check("hud-menu-opens-and-closes", opened && !main.MenuOpen && code?.IsVisibleInTree() == false, $"opened with everything shown: {opened}; closed again: {!main.MenuOpen}");
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
            && BoardInfo().Contains("Table 36.0\" x 36.0\""),
            scene is null ? "no battlefield" : $"{scene.Models.Count} models ({onTable.Count} on the table in MODELS), {scene.Terrain.Count} terrain pieces, {scene.Zones.Count} zones; {BoardInfo().Split('\n')[0]}");
        if (board is null || scene is null)
        {
            return;
        }
        // Proposal G: every model carries its short id ("A2") on the battlefield; the full name is in the panel.
        var unlabelled = scene.Models.Where(m => board.IdLabelOf(m.Id)?.StartsWith(UnitPanel.ShortId(m.Id), StringComparison.Ordinal) != true).Select(m => m.Name).ToList();
        Check("board-models-labelled", unlabelled.Count == 0 && scene.Models.All(m => m.Label.StartsWith($"[{m.SeatLetter}] ", StringComparison.Ordinal)),
            unlabelled.Count == 0 ? string.Join(" | ", scene.Models.Select(m => board.IdLabelOf(m.Id))) : $"no short id for {string.Join(", ", unlabelled)}");
        await LabelLayoutChecks(board, "board");
        await LayoutChecks(board);
        await SeeThroughChecks(board, scene);
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
        var top = board.TopDown && board.TableInView(0.01f) && AllModelsInView(board, scene) && BoardInfo().Contains("camera top-down", StringComparison.Ordinal);
        Check("board-top-down", top, $"camera {board.CameraText}");
        await Shot("board-top-down");
        Check("board-pick-roundtrip-top-down", PickRoundTrip(board, scene, out var flat), flat);
        await KeyPress(Key.T);
        await Frames(3);

        // Start from no selection: when the seat is taken back during one of its activations, the activating model is
        // already selected (so its bar shows), and N would go on from it, possibly past the last model of this seat.
        board.Select(null);
        await Frames(2);
        await KeyPress(Key.N);
        await Frames(2);
        var first = board.Selected;
        var info = PanelText;
        Check("board-select-key", first is { Yours: true }
            && Section("MODELS").Contains($"[{first.SeatLetter}] {first.Name}:", StringComparison.Ordinal)
            && ((Main)_main).PanelView?.Title == UnitPanel.Heading(first),
            first is null ? "nothing selected" : $"N selected {first.Name}; {info.Split('\n')[0]}");
        await KeyPress(Key.P);
        await Frames(2);
        Check("board-previous-key", board.Selected?.Id == scene.Next(first?.Id, forward: false)?.Id && board.Selected?.Id != first?.Id, $"P selected {board.Selected?.Name}");

        // A real mouse click (left button) on an opponent's model selects it.
        var target = scene.Models.First(m => !m.Yours);
        var local = board.ScreenOf(target.X, target.Y, target.Standing);
        var clicked = false;
        await MouseOn(board);
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
        await Frames(2);
        var panel = ((Main)_main).PanelView;
        Check("panel-unit", panel is not null && panel.Title == UnitPanel.Heading(target) && PanelHas(Words.Status(target.Status))
            && (panel.Unavailable is null || panel.Unavailable.StartsWith("Opponent's unit", StringComparison.Ordinal)),
            $"panel: {PanelText.Replace('\n', '|')}");
        await CameraChecks(board, scene);
        Dump("board");
    }

    // QoL stage E: Home centres on the selected model; the wheel zooms towards the pointer (the table point under it stays
    // under it); a right-click that shakes a few pixels does not move the camera. F fits the table again after each.
    private async Task CameraChecks(BoardView board, BoardScene scene)
    {
        await MouseOn(board);
        var selected = board.Selected;
        await FocusByTab(board, 60);
        await KeyPress(Key.Home);
        await Frames(3);
        var centred = selected is not null && Math.Abs(board.CameraTarget.X - selected.X) < 0.01 && Math.Abs(board.CameraTarget.Z - selected.Y) < 0.01
            && board.ScreenOf(selected.X, selected.Y, selected.Standing) is { } onScreen && onScreen.DistanceTo(board.Size / 2) < board.Size.Y * 0.1f;
        Check("board-home-centres", centred, $"selected {selected?.Name} at ({selected?.X:0.0}, {selected?.Y:0.0}); camera looks at ({board.CameraTarget.X:0.0}, {board.CameraTarget.Z:0.0})");
        await Shot("board-home-centred");
        await KeyPress(Key.F);
        await Frames(3);

        // The wheel, a quarter of the way in from the top-left: the table point there stays there.
        var at = board.Size * 0.3f + new Vector2(board.Size.X * 0.1f, 0);
        var before = board.TableAt(at);
        var distance = board.Camera.Position.DistanceTo(board.CameraTarget);
        var global = board.GetGlobalTransformWithCanvas() * at;
        // A real wheel notch is a press and a release; a press left unreleased would keep later clicks on the battlefield.
        foreach (var pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = pressed, Position = global, GlobalPosition = global });
            await Frames(2);
        }
        await Frames(1);
        var after = board.TableAt(at);
        var closer = board.Camera.Position.DistanceTo(board.CameraTarget) < distance - 0.01f;
        var kept = before is { } a && after is { } b && Math.Abs(a.X - b.X) < 0.3 && Math.Abs(a.Y - b.Y) < 0.3;
        Check("board-wheel-zooms-to-pointer", closer && kept, $"zoomed in: {closer}; table point under the pointer {before} -> {after}");
        await KeyPress(Key.F);
        await Frames(3);

        // A shaky right-click (3 pixels) does not pan.
        var camera = board.Camera.Position;
        var press = board.GetGlobalTransformWithCanvas() * (board.Size * 0.5f + new Vector2(0, board.Size.Y * 0.35f));
        var shake = press + new Vector2(3, 2);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = press, GlobalPosition = press });
        await Frames(2);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = shake, GlobalPosition = shake, Relative = new Vector2(3, 2), ButtonMask = MouseButtonMask.Right });
        await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = shake, GlobalPosition = shake });
        await Frames(3);
        Check("board-shaky-right-click-no-pan", board.Camera.Position.IsEqualApprox(camera), $"camera {camera} -> {board.Camera.Position}");
        // The battlefield keeps taking the mouse, as it did after the click check before these camera checks.
        await KeyPress(Key.F);
        await Frames(3);
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
        await MouseOff(board);
        await KeyPress(Key.L);
        var preview = interaction.Preview;
        Check("board-place-preview", interaction.Mode == InteractionMode.Place && preview is { Legal: true } && PanelHas($"PLACE {UnitPanel.ShortId(preview.Model)} → ")
            && InPanel(board, UnitPanelView.PreviewLine) && OffField(board, Preview.Caveat) && PanelHas($"→ {preview.Path[0].At}"),
            $"cursor {interaction.Cursor}; {preview?.Headline}; panel: {PanelText.Replace('\n', '|')}; labels: {string.Join(" / ", board.OverlayTexts)}");
        await Shot($"{_role}-place-preview");

        var start = interaction.Cursor;
        await KeyPress(Key.Right);
        var half = interaction.Cursor;
        await KeyPress(Key.Right, shift: true);
        var two = interaction.Cursor;
        Check("board-cursor-steps", Near(half, start.X + 0.5, start.Y) && Near(two, start.X + 2.5, start.Y) && PanelHas($"→ {two}"),
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
        Check("board-place-refused-reason", refused?.Refusal is { } r && InPanel(board, $"REFUSED: {r.Text}")
            && ((Main)_main).PanelView?.Details.Contains($"Rule: {r.RuleRef}") == true,
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
        await MouseOff(board);
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
            && (interaction.Notice ?? "").StartsWith("REFUSED BY THE RULES:", StringComparison.Ordinal) && PanelHas(interaction.Notice!)
            && InPanel(board, "REFUSED BY THE RULES:") && interaction.Mode == InteractionMode.Place && Lines(LogText()).Count == events,
            $"last command: {refusedAnswer}; battlefield: {interaction.Notice}; events {events} -> {Lines(LogText()).Count}");
        await Shot($"{_role}-place-refused-by-server");

        // Back to the zone centre: one confirmation sends the legal placement once, and the server's events place it.
        await MouseOff(board);
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
        await MouseOff(board);
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
            && InPanel(board, $"MOVE → {move.Path[^1].At}") && InPanel(board, $"{BoardScene.Inch(move.Path[^1].Inches)} used") && (move.Legal || PanelHas("REFUSED: "))
            && board.OverlayTexts.Contains(BoardScene.Inch(move.Path[1].Inches)),
            $"from {actor?.Text}; {move?.Headline}; path {string.Join(" -> ", move?.Path.Select(p => $"{p.At} {BoardScene.Inch(p.Inches)}") ?? [])}; labels: {string.Join(" / ", board.OverlayTexts)}");
        // Stage B: the card says how far (the rules' path cost), of how much, what is left and the straight line; a
        // see-through ghost marks the end while the real model has not moved.
        IReadOnlyList<string> card = move is null ? [] : PreviewCard.Lines(move);
        var ghost = board.GhostAt is { } g && move is not null && Near(g, move.Path[^1].At.X, move.Path[^1].At.Y, 0.01);
        var still = actor is not null && board.Scene?.Find(actor.Id) is { } real && Near(new TablePoint(real.X, real.Y), actor.X, actor.Y, 0.001);
        Check("board-preview-card-move", move is not null && move.Left is not null && InPanel(board, $"used of {BoardScene.Inch(move.Allowance!.Value)} ({BoardScene.Inch(move.Left.Value)} left)")
            && InPanel(board, "straight line ") && card.Skip(1).Where(l => move.Refusal is not { } r || l != $"{r.Text} ({r.RuleRef})").All(l => InPanel(board, l)) && ghost && still,
            $"card: {string.Join(" | ", card)}; panel: {PanelText.Replace('\n', '|')}; ghost at {board.GhostAt}; real model unmoved: {still}");
        await PanelDetailsChecks(board);
        var confirmAtMove = ((Main)_main).PanelConfirm.GetGlobalRect();
        await CloseShot(board, actor?.Id, "host-move-preview");
        await KeyPress(Key.Backspace);
        var removed = interaction.Waypoints.Count == 0;

        // The mouse: moving over the table moves the cursor (throttled), and Shift+click fixes a waypoint there (a plain
        // click on a spot the preview accepts would send the move).
        var hoverAt = (X: Math.Clamp((actor?.X ?? 18) + 1.5, 1, 35), Y: Math.Clamp((actor?.Y ?? 18) - 1.0, 1, 35));
        var hovered = false;
        var clickedPoint = false;
        await MouseOn(board);
        if (board.ScreenOf(hoverAt.X, hoverAt.Y) is { } local)
        {
            var global = board.GetGlobalTransformWithCanvas() * local;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = global, GlobalPosition = global });
            hovered = await Until(() => Near(interaction.Cursor, hoverAt.X, hoverAt.Y, 0.15) ? "ok" : null, 3, "hover") is not null;
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, ShiftPressed = true, Position = global, GlobalPosition = global });
                await Frames(2);
            }
            clickedPoint = interaction.Waypoints.Count == 1 && Near(interaction.Waypoints[0], hoverAt.X, hoverAt.Y, 0.15) && interaction.Pending is null;
        }
        Check("board-mouse-path", removed && hovered && clickedPoint, $"Backspace removed the waypoint: {removed}; hover moved the cursor to {interaction.Cursor} (wanted ({hoverAt.X:0.0}, {hoverAt.Y:0.0})): {hovered}; Shift+click fixed a waypoint and sent nothing: {clickedPoint}");
        var hadGhost = board.GhostAt is not null;
        await KeyPress(Key.Escape);
        await Frames(2);
        Check("board-preview-cancel-clears", hadGhost && board.GhostAt is null && board.OverlayTexts.Count == 0 && interaction.Mode == InteractionMode.None
            && interaction.Pending is null && Section("LAST COMMAND") == lastCommand,
            $"ghost before Esc: {hadGhost}, after: {board.GhostAt}; labels: {board.OverlayTexts.Count}; mode {interaction.Mode}; last command unchanged: {Section("LAST COMMAND") == lastCommand}");
        await MouseOff(board);

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
        var sightShown = shot is { Rays.Count: > 0 } && InPanel(board, "SHOOT → ") && (shot.Legal || InPanel(board, $"REFUSED: {shot.Refusal!.Text}"))
            && InPanel(board, $"of {shot.Rays.Count} sight rays clear") && InPanel(board, "Distance ");
        Check("board-shoot-preview", sightShown || (early && InPanel(board, shot!.Refusal!.Text)),
            $"{shot?.Headline}; {shot?.Rays.Count} rays, {shot?.Rays.Count(r => r.Clear)} clear; labels: {string.Join(" / ", board.OverlayTexts)}");
        var aimed = interaction.Target is { } t && scene.Find(t) is { } targetShape ? targetShape : null;
        IReadOnlyList<string> targetCard = shot is null || aimed is null ? [] : PreviewCard.Lines(shot, aimed);
        Check("board-preview-card-target", early || (aimed is not null && InPanel(board, $"SHOOT → {UnitPanel.Heading(aimed)}") && InPanel(board, "Line of sight: ")
            && (InPanel(board, "Target in cover (") || InPanel(board, "Target not in cover"))
            && targetCard.Skip(1).Where(l => !l.StartsWith("target ", StringComparison.Ordinal) && (shot!.Refusal is not { } r || l != $"{r.Text} ({r.RuleRef})")).All(l => InPanel(board, l))),
            $"card: {string.Join(" | ", targetCard)}; panel: {PanelText.Replace('\n', '|')}");
        var confirmAtShoot = ((Main)_main).PanelConfirm.GetGlobalRect();
        Check("panel-confirm-cancel-fixed", confirmAtShoot.IsEqualApprox(confirmAtMove) && ((Main)_main).PanelCancel.GetGlobalRect().Position.Y == confirmAtShoot.Position.Y,
            $"Confirm at {confirmAtMove} for Move, {confirmAtShoot} for Shoot; Cancel at {((Main)_main).PanelCancel.GetGlobalRect()}");
        await CloseShot(board, interaction.Target, "host-shoot-preview");
        var current = interaction.Target;
        await KeyPress(Key.N);
        var expected = current is { } c && targets.IndexOf(c) is var at && at >= 0 ? targets[(at + 1) % targets.Count] : (Rules.Core.State.ModelId?)null;
        var cycled = targets.Count < 2 || interaction.Target == expected;
        var other = targets.Where(t => t != interaction.Target).Select(t => scene.Find(t)).FirstOrDefault(m => m is not null && board.ScreenOf(m.X, m.Y, m.Standing) is not null);
        var picked = targets.Count < 2;
        await MouseOn(board);
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
        await MouseOff(board);
        Check("board-target-cycle-and-click", targets.Count > 0 && cycled && picked, $"{targets.Count} enemy targets; N went to the next: {cycled}; a click picked {other?.Name ?? "nobody"}: {picked}");

        await KeyPress(Key.C);
        var charge = interaction.Preview;
        var chargeShown = charge is not null && InPanel(board, "CHARGE → ") && OffField(board, Preview.Caveat)
            && (charge.Legal ? (charge.ChargePath.Count < 2 || board.OverlayTexts.Any(t => t is "ANY" or "SHORT" || t.EndsWith('+')))
                && (charge.ChargePath.Count < 2 || InPanel(board, "Charge roll") || InPanel(board, "stops short")) : InPanel(board, $"REFUSED: {charge.Refusal!.Text}"));
        Check("board-charge-preview", chargeShown, $"{charge?.Headline}; {string.Join(" ", charge?.Facts ?? [])}; labels: {string.Join(" / ", board.OverlayTexts)}");
        await Shot("host-charge-preview");
        // The panel's Cancel, clicked with the mouse, stops choosing and sends nothing (as Esc does).
        await ClickButton(((Main)_main).PanelCancel);
        Check("board-previews-send-nothing", interaction.Mode == InteractionMode.None && board.OverlayTexts.Count == 0 && Section("LAST COMMAND") == lastCommand && Hint() is not null,
            $"mode {interaction.Mode}; last command unchanged: {Section("LAST COMMAND") == lastCommand}; hint still offered: {Hint()?.Text}");
        Dump("previews");
        await ActionBarChecks(board);

        // Stage 4c: a Move with the keyboard cursor, sent once with Space; the server's events move the model.
        var moved = await SendFirstLegalMove(board);
        Check("board-send-move", moved.Pass, moved.Detail);
        await Shot("host-moved");

        // Then a Shoot at the first target the preview says is legal (if any), sent with Space.
        _shootSent = await SendFirstLegalTarget(board, Key.S, "board-send-shoot");

        // Then a Dash with the mouse alone, if the activation still offers one.
        await ClickDash(board);
    }

    // Proposal G: Details opens and closes in place and changes nothing else: the selection, the action, the preview,
    // LAST COMMAND and the event list stay as they were, and nothing is sent.
    private async Task PanelDetailsChecks(BoardView board)
    {
        var main = (Main)_main;
        var interaction = board.Interaction;
        var before = (board.Selected?.Id, interaction.Mode, interaction.Preview?.Command, Section("LAST COMMAND"), Lines(LogText()).Count);
        var button = All(main).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text.StartsWith("Details ", StringComparison.Ordinal));
        var opened = false;
        var closed = false;
        if (button is not null)
        {
            await ClickButton(button);
            opened = main.DetailsOpen && main.PanelView is { Details.Count: > 0 } view && view.Details.All(PanelHas);
            await ClickButton(button);
            closed = !main.DetailsOpen;
        }
        var after = (board.Selected?.Id, interaction.Mode, interaction.Preview?.Command, Section("LAST COMMAND"), Lines(LogText()).Count);
        Check("panel-details-keeps-preview", opened && closed && before == after && interaction.Pending is null,
            $"details button: {button?.Text}; opened with every detail shown: {opened}; closed: {closed}; before {before}; after {after}");
    }

    // The action bar next to the acting model: it shows exactly the actions the interaction offers plus End activation;
    // a real mouse click on Move starts choosing a move; a right-click on the battlefield cancels it and sends nothing.
    private async Task ActionBarChecks(BoardView board)
    {
        var interaction = board.Interaction;
        var lastCommand = Section("LAST COMMAND");
        await Frames(3);
        var offered = interaction.Offered.Select(Interaction.Words).ToList();
        var labels = board.BarLabels;
        Check("board-action-bar-shown", labels.Count > 0 && labels.Take(offered.Count).SequenceEqual(offered) && labels.Contains("End activation")
            && interaction.Available.Except(interaction.Offered).All(m => !labels.Contains(Interaction.Words(m))),
            $"bar: {string.Join(" / ", labels)}; offered: {string.Join(" / ", offered)}; available: {string.Join(" / ", interaction.Available)}");
        await Shot("host-action-bar");

        var started = false;
        if (board.BarButtonAt(Interaction.Words(InteractionMode.Move)) is { } at)
        {
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at });
                await Frames(2);
            }
            await Frames(2);
            // Proposal G: the bar next to the unit goes away while choosing; Confirm and Cancel are in the panel.
            started = interaction.Mode == InteractionMode.Move && board.BarLabels.Count == 0
                && ((Main)_main).PanelConfirm.Text == "Move here" && !((Main)_main).PanelCancel.Disabled;
        }
        Check("board-bar-click-starts-action", started, $"Move offered: {offered.Contains(Interaction.Words(InteractionMode.Move))}; mode {interaction.Mode}; bar: {string.Join(" / ", board.BarLabels)}");

        // The area the move can reach is drawn, and it is the rules' area (Interaction.ReachArea).
        await Frames(3);
        var area = interaction.ReachArea();
        Check("board-move-area-shown", started && area.Count > 0 && board.AreaPoints == area.Count, $"{board.AreaPoints} points drawn of {area.Count} in the area; reach {interaction.Reach()}\"");

        // A click beyond the model's movement is not sent: the mode stays, and a note above the model says why.
        var refusedShown = false;
        var refusedDetail = "no spot beyond the reach";
        await MouseOn(board);
        if (started && board.Scene is { } shown && shown.Find(interaction.Actor) is { } me && interaction.Reach() is { } reach
            && FarSpot(board, shown, me, reach + 1.5) is { } far)
        {
            var global = board.GetGlobalTransformWithCanvas() * far;
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = global, GlobalPosition = global });
                await Frames(2);
            }
            await Frames(2);
            refusedShown = interaction.Mode == InteractionMode.Move && interaction.Pending is null && Section("LAST COMMAND") == lastCommand
                && board.NoteTexts.Contains($"{me.Id.Value}: TOO FAR");
            refusedDetail = $"mode {interaction.Mode}; pending {interaction.Pending}; notes: {string.Join(" / ", board.NoteTexts)}; last command unchanged: {Section("LAST COMMAND") == lastCommand}";
        }
        Check("board-click-too-far-not-sent", refusedShown, refusedDetail);
        await Shot("host-too-far");

        var cancelled = false;
        await MouseOn(board);
        var free = board.Scene is { } scene ? FreeSpot(board, scene) : null;
        if (started && free is { } spot)
        {
            var global = board.GetGlobalTransformWithCanvas() * spot;
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = pressed, Position = global, GlobalPosition = global });
                await Frames(2);
            }
            cancelled = interaction.Mode == InteractionMode.None && interaction.Pending is null && board.OverlayTexts.Count == 0 && Section("LAST COMMAND") == lastCommand;
        }
        await MouseOff(board);
        if (interaction.Mode != InteractionMode.None)
        {
            await KeyPress(Key.Escape);
        }
        Check("board-right-click-cancels", cancelled, $"mode {interaction.Mode}; pending {interaction.Pending}; labels: {string.Join(" / ", board.OverlayTexts)}; last command unchanged: {Section("LAST COMMAND") == lastCommand}");
    }

    // A spot on the battlefield at least this far from the model and away from every model (board-local).
    private static Vector2? FarSpot(BoardView board, BoardScene scene, ModelShape from, double distance)
    {
        for (var x = 1.0; x <= 35; x += 2)
        {
            for (var y = 1.0; y <= 35; y += 2)
            {
                if ((from.X - x) * (from.X - x) + (from.Y - y) * (from.Y - y) > distance * distance
                    && scene.Models.All(m => (m.X - x) * (m.X - x) + (m.Y - y) * (m.Y - y) > 9) && board.ScreenOf(x, y) is { } spot)
                {
                    return spot;
                }
            }
        }
        return null;
    }

    // Dash with the mouse alone: the bar's Dash button, then one click on a spot of the area; the server's answer and the
    // Dash roll's note above the model follow. Reported only when the match offers a Dash then (optional).
    private async Task ClickDash(BoardView board)
    {
        var interaction = board.Interaction;
        if (!interaction.Offered.Contains(InteractionMode.Dash) || board.BarButtonAt("Dash") is not { } button)
        {
            return;
        }
        foreach (var pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = button, GlobalPosition = button });
            await Frames(2);
        }
        await Frames(3);
        var area = interaction.ReachArea();
        var actor = interaction.Actor;
        if (interaction.Mode != InteractionMode.Dash || board.Scene?.Find(actor) is not { } me || area.Count == 0)
        {
            Check("board-click-dash", false, $"mode {interaction.Mode}; area {area.Count} points");
            return;
        }
        // A spot about 2" away that no model stands on.
        var spot = area.Where(p => board.Scene!.Models.All(m => (m.X - p.X) * (m.X - p.X) + (m.Y - p.Y) * (m.Y - p.Y) > 2.25))
            .OrderBy(p => Math.Abs(Math.Sqrt((p.X - me.X) * (p.X - me.X) + (p.Y - me.Y) * (p.Y - me.Y)) - 2)).Select(p => (TablePoint?)p).FirstOrDefault();
        await MouseOn(board);
        var before = Section("LAST COMMAND");
        var events = Lines(LogText()).Count;
        if (spot is { } chosen && board.ScreenOf(chosen.X, chosen.Y) is { } local)
        {
            var global = board.GetGlobalTransformWithCanvas() * local;
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = global, GlobalPosition = global });
                await Frames(2);
            }
        }
        await MouseOff(board);
        var answer = await Answered(before);
        var rolled = await Until(() => Lines(LogText()).Skip(events).FirstOrDefault(l => l.Contains(" Dash roll ", StringComparison.Ordinal)), 10, "dash rolled");
        var note = await Until(() => board.NoteTexts.FirstOrDefault(n => n.StartsWith($"{me.Id.Value}: DASH", StringComparison.Ordinal)), 3, "dash note");
        Check("board-click-dash", answer.StartsWith("DONE:", StringComparison.Ordinal) && answer.Contains("Dash", StringComparison.Ordinal) && rolled is not null,
            $"clicked {spot}; last command: {answer}; event: {rolled}");
        Check("board-note-shown", note is not null, $"notes: {string.Join(" / ", board.NoteTexts)} (wanted a DASH note above {me.Id.Value})");
        await Shot("host-dash-note");
    }

    // A point on the battlefield away from every model (board-local), for a click that must not pick a model.
    private static Vector2? FreeSpot(BoardView board, BoardScene scene)
    {
        for (var x = 2.0; x <= 34; x += 4)
        {
            for (var y = 2.0; y <= 34; y += 4)
            {
                if (scene.Models.All(m => (m.X - x) * (m.X - x) + (m.Y - y) * (m.Y - y) > 9) && board.ScreenOf(x, y) is { } spot)
                {
                    return spot;
                }
            }
        }
        return null;
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
        await MouseOff(board);
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
            var glides = board.GlidesStarted;
            var followed = board.FollowedMoves;
            var end0 = preview.Path[^1].At;
            var standing0 = board.Scene?.Find(preview.Model)?.Standing ?? 0;
            var checkFollow = _role == "pvp-a" && !_followChecked;
            var endOffScreen = false;
            if (checkFollow)
            {
                // Improvement 3: look away from where the move ends (zoomed in on the model furthest from it), so the
                // camera has to follow the move there.
                _followChecked = true;
                var far = board.Scene?.Models.OrderByDescending(m => (m.X - end0.X) * (m.X - end0.X) + (m.Y - end0.Y) * (m.Y - end0.Y)).FirstOrDefault();
                board.CentreOn(far?.Id, 5);
                await Frames(2);
                endOffScreen = !OnBoardScreen(board, end0.X, end0.Y, standing0);
            }
            if (_role == "host")
            {
                // The panel's Confirm, clicked with the mouse, sends the previewed move (as Space does).
                var confirm = ((Main)_main).PanelConfirm;
                Check("panel-confirm-shows-action", !confirm.Disabled && confirm.Text == "Move here", $"Confirm \"{confirm.Text}\", disabled: {confirm.Disabled}");
                await ClickButton(confirm);
            }
            else
            {
                await KeyPress(Key.Space);
            }
            var answer = await Answered(before);
            var line = await Until(() => Lines(LogText()).Skip(events).FirstOrDefault(l => l.Contains(" moves ", StringComparison.Ordinal)), 10, "moved");
            await Frames(3);
            var end = preview.Path[^1].At;
            var shape = board.Scene?.Find(preview.Model);
            if (!_glideChecked && shape is not null && line is not null)
            {
                // Improvement 3: the move was shown travelling from where the model stood to where the server put it,
                // briefly, and the model is drawn at the server's position once it stops.
                _glideChecked = true;
                var last = board.LastGlide;
                var drawn = board.DrawnAt(preview.Model);
                Check("board-move-glides", board.GlidesStarted > glides && last is { } g && g.Model == preview.Model
                    && Near(new TablePoint(g.To.X, g.To.Y), shape.X, shape.Y) && Near(new TablePoint(g.From.X, g.From.Y), preview.Path[0].At.X, preview.Path[0].At.Y)
                    && g.Seconds is >= BoardView.GlideMinSeconds and <= BoardView.GlideMaxSeconds && !board.Gliding && drawn is { } d && Near(new TablePoint(d.X, d.Y), shape.X, shape.Y),
                    $"glides {glides} -> {board.GlidesStarted}; last {last}; drawn at {drawn}; server ({shape.X}, {shape.Y})");
            }
            if (checkFollow)
            {
                Check("pvp-move-followed", endOffScreen && board.FollowedMoves > followed && OnBoardScreen(board, end.X, end.Y, standing0),
                    $"end {end} off screen before: {endOffScreen}; followed {followed} -> {board.FollowedMoves}; camera on {board.CameraTarget}");
                board.Fit();
                await Frames(3);
            }
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
        await MouseOff(board);
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

    // Proposal G: the selected-unit and action panel beside the battlefield, as shown.
    // Proposal G3: the battlefield and its panel fill the window under the HUD; the combat log sits under the panel in
    // the right-hand column, beside the battlefield.
    private async Task LayoutChecks(BoardView board)
    {
        await Frames(3);
        var main = (Main)_main;
        var row = main.BattlefieldRow.GetGlobalRect();
        var height = _main.GetViewportRect().Size.Y;
        var want = Math.Max(440f, height - 24 - main.BattlefieldTop - 4);
        Check("layout-battlefield-fills-window", Math.Abs(row.Size.Y - want) <= 2 && board.GetGlobalRect().Size.Y >= row.Size.Y - 2,
            $"window {height}px; row starts {main.BattlefieldTop}px down the page, {row.Size.Y}px tall (wanted {want}); battlefield {board.GetGlobalRect().Size}");
        var panel = Controls<PanelContainer>().FirstOrDefault(p => p.Name == "UnitPanel")?.GetGlobalRect();
        var log = main.CombatLog.GetGlobalRect();
        Check("layout-log-beside-battlefield", main.CombatLog.IsVisibleInTree() && panel is { } p && log.Position.X >= board.GetGlobalRect().End.X
            && log.Position.Y >= p.End.Y && log.End.Y <= row.End.Y + 1 && log.Size.Y >= 100 && main.BattlefieldRow.IsAncestorOf(main.CombatLog),
            $"battlefield {board.GetGlobalRect()}; panel {panel}; log {log}");
    }

    // See-through walls: the camera is moved until a tall piece stands between it and a model; that piece (and only the
    // pieces hiding a model) is drawn faint, every other piece solid; top-down nothing is faint. The camera is put back
    // as it was (angled, facing the same way, the table fitted).
    private async Task SeeThroughChecks(BoardView board, BoardScene scene)
    {
        var yaw = board.Yaw;
        var wasTopDown = board.TopDown;
        if (wasTopDown)
        {
            board.ToggleTopDown();
        }
        // Deployment is rolled, so where models stand varies: put the camera just behind the tallest piece, on the
        // line from a model through the piece's middle, so that piece must hide that model.
        IReadOnlySet<string> hiding = new HashSet<string>();
        var where = "";
        var piece = scene.Terrain.Where(t => t.Height >= SeeThrough.MinHeight).OrderByDescending(t => t.Height).FirstOrDefault();
        var model = scene.Models.FirstOrDefault(m => board.BodyAt(m.Id) is not null);
        if (piece is not null && model is not null && board.BodyAt(model.Id) is { } body)
        {
            var middle = new Vector3((float)(piece.Area.X0 + piece.Area.X1) / 2, (float)piece.Height / 2, (float)(piece.Area.Y0 + piece.Area.Y1) / 2);
            var half = (float)Math.Sqrt(Math.Pow(piece.Area.X1 - piece.Area.X0, 2) + Math.Pow(piece.Area.Y1 - piece.Area.Y0, 2)) / 2;
            var camera = middle + (middle - body).Normalized() * (half + 1.5f);
            board.PlaceCameraAt(camera);
            await Frames(2);
            hiding = board.HidingTerrain();
            where = $"camera behind {piece.Id} from {model.Id.Value}, facing {board.CameraText}";
        }
        var tall = scene.Terrain.Where(t => t.Height >= SeeThrough.MinHeight).Select(t => t.Id).ToList();
        string Alphas() => string.Join(", ", tall.Select(id => $"{id} {board.TerrainAlpha(id):0.0}"));
        var faded = board.FadedTerrain.ToHashSet();
        var fadeOk = piece is not null && hiding.Contains(piece.Id) && faded.SetEquals(hiding)
            && tall.All(id => board.TerrainAlpha(id) is { } a && (hiding.Contains(id) ? Math.Abs(a - BoardView.FadedAlpha) < 0.01f : a == 1f));
        var fadedText = $"{where}; hiding {string.Join(",", hiding)}; drawn {Alphas()}";
        board.ToggleTopDown();
        await Frames(3);
        var topDownSolid = board.FadedTerrain.Count == 0 && tall.All(id => board.TerrainAlpha(id) == 1f);
        var topText = Alphas();
        board.ToggleTopDown();
        board.Turn(((yaw - board.Yaw) / 90 % 4 + 4) % 4);
        if (wasTopDown)
        {
            board.ToggleTopDown();
        }
        board.Fit();
        await Frames(3);
        Check("board-see-through-walls", fadeOk && topDownSolid && board.Yaw == yaw && board.TopDown == wasTopDown,
            $"{fadedText}; top-down: {topText}; camera back to {board.CameraText}");
    }

    // Proposal G: the words over the battlefield are short (ids, zones, objectives, notes of a word or two, inches), and
    // the model ids and the zone and objective labels do not cover each other on screen.
    private async Task LabelLayoutChecks(BoardView board, string prefix)
    {
        await Frames(3);
        var texts = All(board).OfType<Label3D>().Where(l => !l.IsQueuedForDeletion() && l.Visible).Select(l => l.Text).ToList();
        var long_ = texts.Where(t => t.Length > 18 || t.Contains(':') || t.Contains('\n')).ToList();
        Check($"{prefix}-labels-short", texts.Count > 0 && long_.Count == 0,
            long_.Count == 0 ? $"{texts.Count} labels: {string.Join(" / ", texts.Take(30))}" : $"long: {string.Join(" / ", long_)}");
        var boxes = board.ScreenLabelBoxes();
        var overlaps = new List<string>();
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                if (boxes[i].Box.Grow(-1).Intersects(boxes[j].Box.Grow(-1)))
                {
                    overlaps.Add($"{boxes[i].Text} {boxes[i].Box} x {boxes[j].Text} {boxes[j].Box}");
                }
            }
        }
        Check($"{prefix}-labels-no-overlap", boxes.Count > 0 && overlaps.Count == 0,
            overlaps.Count == 0 ? $"{boxes.Count} id, zone, objective and note labels apart" : string.Join("; ", overlaps));
    }

    private string PanelText => ((Main)_main).PanelText;

    private bool PanelHas(string text) => PanelText.Contains(text, StringComparison.Ordinal);

    // No label floating over the battlefield carries this text (the panel says it instead).
    private static bool OffField(BoardView board, string text) =>
        !All(board).OfType<Label3D>().Any(l => !l.IsQueuedForDeletion() && l.Text.Contains(text, StringComparison.Ordinal));

    // In the panel, and not over the battlefield.
    private bool InPanel(BoardView board, string text) => PanelHas(text) && OffField(board, text);

    // A real mouse click on a button; the keyboard focus goes back where it was, so later key presses still reach the
    // battlefield, as they would for a player who clicks a panel button and then uses the keys again on the battlefield.
    private async Task ClickButton(Button button)
    {
        var owner = GetViewport().GuiGetFocusOwner();
        var at = button.GetGlobalRect().GetCenter();
        foreach (var pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at });
            await Frames(2);
        }
        // Give the keyboard focus back, as a mouse click does not move it, unless that control is now hidden.
        if (owner is not null && GodotObject.IsInstanceValid(owner) && !owner.IsQueuedForDeletion() && owner.IsVisibleInTree())
        {
            owner.GrabFocus();
        }
        await Frames(1);
    }

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
        // Text size and high contrast are in the Menu (proposal G3).
        await OpenMenu(true);
        await ChooseOption(Option("Text 100%"), "Text 150%", "text-150");
        await Frames(5);
        Check("text-150-applied", _main.Theme?.DefaultFontSize == 27, $"default font size {_main.Theme?.DefaultFontSize}");
        var width = _main.GetViewportRect().Size.X;
        var overflow = Controls<Control>().Where(c => (c is Button or Godot.Label or OptionButton) && c.IsVisibleInTree() && c.GetGlobalRect().End.X > width + 1)
            .Select(Describe).ToList();
        Check("text-150-fits-width", overflow.Count == 0, overflow.Count == 0 ? $"all text within {width}px" : string.Join("; ", overflow));
        await Shot("host-text-150");
        Check("layout-text-150", PanelButtonsFit(out var buttons) && Strip().Length > 0 && ((Main)_main).PanelConfirm.IsVisibleInTree(), buttons);
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
        await OpenMenu(false);
    }

    // Proposal G5: at 1280 x 800 the panel and log sit beside the battlefield; under 1100 pixels of width (1000 x 700) they
    // move under it, side by side, and the battlefield keeps the full width. Either way Confirm and Cancel are on the
    // first screen, their words fit, and nothing runs off the right edge.
    private async Task WindowSizeChecks()
    {
        var main = (Main)_main;
        // Through the window itself: without a window manager (xvfb-run) DisplayServer.WindowSetSize does not resize the view.
        var window = _main.GetWindow();
        var original = window.Size;
        foreach (var (w, h) in new[] { (1000, 700), (1280, 800) })
        {
            window.Size = new Vector2I(w, h);
            for (var i = 0; i < 60 && _main.GetViewportRect().Size != new Vector2(w, h); i++)
            {
                await Frames(1);
            }
            await Frames(6);
            var size = _main.GetViewportRect().Size;
            var board = Controls<BoardView>().First(b => b.IsVisibleInTree()).GetGlobalRect();
            var column = main.RightColumn.GetGlobalRect();
            var panel = Controls<PanelContainer>().First(p => p.Name == "UnitPanel").GetGlobalRect();
            var log = main.CombatLog.GetGlobalRect();
            bool OnFirstScreen(Control c) => c.IsVisibleInTree() && main.PageY(c) + c.Size.Y <= size.Y - 24 + 2;
            var fits = PanelButtonsFit(out var words);
            var buttonsShown = OnFirstScreen(main.PanelConfirm) && OnFirstScreen(main.PanelCancel) && fits;
            var overflow = Controls<Control>().Where(c => (c is Button or Godot.Label or OptionButton) && c.IsVisibleInTree() && c.GetGlobalRect().End.X > size.X + 1)
                .Select(Describe).ToList();
            var placed = w < Main.StackBelow
                ? main.Stacked && column.Position.Y >= board.End.Y && log.Position.X >= panel.End.X && board.Size.X >= size.X - 40
                : !main.Stacked && column.Position.X >= board.End.X && log.Position.Y >= panel.End.Y;
            Check($"layout-{w}x{h}", size == new Vector2(w, h) && placed && buttonsShown && overflow.Count == 0,
                $"window {size}; stacked {main.Stacked}; battlefield {board}; panel {panel}; log {log}; Confirm and Cancel on the first screen and fit: {buttonsShown} ({words}); "
                + (overflow.Count == 0 ? "nothing past the right edge" : "past the right edge: " + string.Join("; ", overflow)));
            await Shot($"host-layout-{w}x{h}");
        }
        window.Size = original;
        await Frames(6);
    }

    // The panel's Confirm, Undo and Cancel show their whole words.
    private bool PanelButtonsFit(out string detail)
    {
        var main = (Main)_main;
        var parts = new List<string>();
        var fit = true;
        foreach (var button in new[] { main.PanelConfirm, main.PanelUndo, main.PanelCancel })
        {
            var font = button.GetThemeFont("font");
            var need = font.GetStringSize(button.Text, HorizontalAlignment.Left, -1, button.GetThemeFontSize("font_size")).X + 8;
            fit &= need <= button.Size.X;
            parts.Add($"{button.Text} {need:0}/{button.Size.X:0}px");
        }
        detail = string.Join(", ", parts);
        return fit;
    }

    private async Task OpenMenu(bool open)
    {
        if (((Main)_main).MenuOpen != open)
        {
            await ClickButton(((Main)_main).MenuButton);
            await Frames(2);
        }
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
        await MatchResultChecks();
        Dump("server-restart");
    }

    // M7.5 stage 5a: once the match is over (here aborted: the server lost it), the screen shows the result, the final
    // state hash and the event count, and "Copy final hash" puts exactly that hash on the clipboard.
    private async Task MatchResultChecks()
    {
        var result = await Until(() => Section("RESULT OF THE MATCH") is { Length: > 0 } r ? r : null, 10, "match-result") ?? "";
        // Plain string parsing: the client's assemblies are limited (ClientAuthorityTest), so no Regex here.
        string? Value(string prefix) => result.Split('\n').FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
        var hash = Value("Final state hash: ") is { Length: 64 } h && h.All(char.IsAsciiHexDigitLower) ? h : null;
        var events = Value("Events: ") is { } e && int.TryParse(e, out var n) ? n : -1;
        var lastLine = Lines(LogText()).LastOrDefault() ?? "";
        var lastSeq = lastLine.IndexOf(']') is > 1 and var close && int.TryParse(lastLine[1..close], out var seq) ? seq : -2;
        var lost = Banner().Contains("no longer on the server", StringComparison.Ordinal);
        Check("match-result-shown", result.StartsWith("Result: ", StringComparison.Ordinal) && result.Contains("No result was recorded as a win or loss.", StringComparison.Ordinal)
            && hash is not null && events == lastSeq
            && (!lost || result.Contains("did not receive the server's abort message", StringComparison.Ordinal)),
            $"{result.Replace('\n', '|')} || last event [{lastSeq}]");
        await Shot($"{_role}-match-result");
        // The Windows clipboard can be opened by one program at a time ("Unable to open clipboard" otherwise), so the
        // two windows take turns: the host copies first, then the reopened client.
        if (_role == "reopen")
        {
            await Signal("host-copied", 60);
        }
        DisplayServer.ClipboardSet("");
        await Press(ButtonNamed("Copy final hash"), Key.Enter, "copy-final-hash-enter");
        await Frames(3);
        var copied = DisplayServer.ClipboardGet();
        Check("copy-final-hash", hash is not null && copied == hash, $"clipboard: {copied}; shown: {hash ?? "none"}");
        Write($"DUMP server-restart final_hash={hash ?? ""}");
        if (_role == "host")
        {
            Mark("final-hash-copied");
        }
    }

    // ---- single player (hot-seat) ----

    private const string HotSeatButton = "Hot-seat: play both sides in this window";

    // One window, both seats: the client starts the local server itself, seats you as Seat One and Seat Two, follows
    // the turn through deployment (placements sent from the battlefield for both seats), switches by hand with F2, and
    // stops the server when you leave.
    private async Task HotSeatPlay()
    {
        await ChooseOption(Option("ranged"), _scenario, "hotseat-choose-scenario");
        await Press(ButtonNamed(HotSeatButton), Key.Enter, "hotseat-start-enter");
        // The first start builds the server, which can take a while.
        var line = await Until(() => Label("HOT-SEAT:")?.Text, 300, "hotseat");
        var problem = Controls<Godot.Label>().FirstOrDefault(l => l.IsVisibleInTree() && l.Text.StartsWith("Could not start hot-seat", StringComparison.Ordinal))?.Text;
        Check("hotseat-started", line is not null && line.Contains("you control Seat One [A]", StringComparison.Ordinal) && line.Contains("Local server ws://127.0.0.1:", StringComparison.Ordinal),
            (line ?? problem ?? "the hot-seat match screen did not appear").Replace('\n', '|'));
        if (line is null)
        {
            return;
        }
        // The local server's address, from the hot-seat line; the runner checks its process separately.
        var urlText = line[(line.IndexOf("Local server ", StringComparison.Ordinal) + 13)..];
        var endpoint = new Uri(urlText[..urlText.IndexOf(' ')]);
        Check("hotseat-server-running", await Reachable(endpoint) && Banner().StartsWith("Connection: Connected", StringComparison.Ordinal), $"local server {endpoint}; {Banner()}");
        await Frames(5);
        if (Dialog<AcceptDialog>() is not null)
        {
            await KeyPress(Key.Escape);
        }
        await Shot("hotseat-started");
        // The window may already follow the turn to Seat Two; the code line and the hot-seat line name the same seat.
        var first = HotControlled();
        var other = first == "Seat One" ? "Seat Two" : "Seat One";
        Check("hotseat-one-window-both-seats", Label("Match code ")?.Text is { } code && code.Contains($"You are {first} [", StringComparison.Ordinal)
            && !code.Contains("Give the code", StringComparison.Ordinal) && Label("HOT-SEAT:")!.Text.Contains($"Switch to {other} with F2", StringComparison.Ordinal),
            $"{Label("Match code ")?.Text ?? "no match code line"} || {Label("HOT-SEAT:")?.Text.Replace('\n', '|')}");

        // Deployment, following the turn: hints (zone, roll-off decisions) and placements on the battlefield, as
        // whichever seat the window shows.
        var controlled = new List<string>();
        var actions = 0;
        var deadline = Time.GetTicksMsec() + 240_000;
        int Placed(string seat) => Lines(LogText()).Count(l => l.Contains($"] {seat} sets up ", StringComparison.Ordinal));
        while (Time.GetTicksMsec() < deadline && actions < 40 && (Placed("One") < 2 || Placed("Two") < 2))
        {
            await Frames(3);
            var seat = HotControlled();
            if (controlled.LastOrDefault() != seat)
            {
                controlled.Add(seat);
            }
            var board = Controls<BoardView>().FirstOrDefault(b => b.IsVisibleInTree());
            if (Hint() is { } hint)
            {
                var before = Section("LAST COMMAND");
                await Press(hint, Key.Enter, null);
                // The answer, or the window following the turn to the other seat (whose last command is then shown).
                await Until(() => HotControlled() != seat || (Section("LAST COMMAND") is var now && now != before && !now.StartsWith("SENT:", StringComparison.Ordinal)) ? "answered" : null, 20, "hotseat-answer");
                actions++;
            }
            else if (board is not null && board.Interaction.Available.Contains(InteractionMode.Place))
            {
                if (!await PlaceSomewhere(board))
                {
                    Check("hotseat-place", false, $"{seat}: {board.Interaction.Notice} {Section("LAST COMMAND")}");
                    break;
                }
                actions++;
            }
            else
            {
                var events = Lines(LogText()).Count;
                await Until(() => Lines(LogText()).Count > events || Hint() is not null || HotControlled() != seat ? "changed" : null, 10, "hotseat-next");
            }
        }
        var one = Placed("One");
        var two = Placed("Two");
        Check("hotseat-both-seats-deploy", one >= 2 && two >= 2, $"{one} placements by Seat One and {two} by Seat Two, all sent from the battlefield of the seat shown");
        Check("hotseat-follows-turn", controlled.Distinct().Count() == 2 && controlled.Count >= 3, $"seats shown in turn: {string.Join(", ", controlled)}");
        Check("hotseat-whose-move-shown", Label("HOT-SEAT:")?.Text is { } now && now.Contains(" to move.", StringComparison.Ordinal), Label("HOT-SEAT:")?.Text.Replace('\n', '|') ?? "");
        await Shot("hotseat-deployed");

        // F2 switches by hand, and the switch is kept (following the turn waits for the match to move on).
        var shown = HotControlled();
        await KeyPress(Key.F2);
        await Frames(5);
        var switched = HotControlled();
        Check("hotseat-f2-switches", switched != shown && Label("Match code ")?.Text.Contains($"You are {switched}", StringComparison.Ordinal) == true,
            $"before F2: {shown}; after: {switched}; {Label("Match code ")?.Text}");
        await Frames(30);
        Check("hotseat-f2-kept", HotControlled() == switched, $"after 30 frames: {HotControlled()}");
        await KeyPress(Key.F2);
        await Frames(5);

        // Leaving ends both seats and stops the local server: no process is left.
        await Press(ButtonNamed("Leave"), Key.Enter, "hotseat-leave-enter");
        var stopped = false;
        for (var i = 0; i < 20 && !stopped; i++)
        {
            stopped = !await Reachable(endpoint);
            if (!stopped)
            {
                await Frames(30);
            }
        }
        Check("hotseat-server-stopped", stopped && ButtonNamed(HotSeatButton) is not null, $"local server {endpoint} {(stopped ? "no longer accepts connections" : "still accepts connections")}");
    }

    // Whether a server answers at the address (a WebSocket connection is accepted).
    private static async Task<bool> Reachable(Uri endpoint)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await using var session = await Match.Client.ClientSession.ConnectAsync(endpoint, timeout.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // "Seat One" or "Seat Two": the seat the hot-seat line says this window controls.
    private string HotControlled()
    {
        var text = Label("HOT-SEAT:")?.Text ?? "";
        var at = text.IndexOf("you control ", StringComparison.Ordinal);
        return at < 0 ? "" : text[(at + 12)..].Split(" [")[0];
    }

    // L on the battlefield, then 2" steps in a widening square until the preview looks legal; Space sends it.
    private async Task<bool> PlaceSomewhere(BoardView board)
    {
        var interaction = board.Interaction;
        if (!await FocusByTab(board, 80))
        {
            return false;
        }
        await MouseOff(board);
        await KeyPress(Key.L);
        Key[] turns = [Key.Right, Key.Down, Key.Left, Key.Up];
        for (var leg = 0; leg < 24 && interaction.Preview is not { Legal: true }; leg++)
        {
            for (var i = 0; i < leg / 2 + 1 && interaction.Preview is not { Legal: true }; i++)
            {
                await KeyPress(turns[leg % 4], shift: true);
            }
        }
        if (interaction.Preview is not { Legal: true })
        {
            await KeyPress(Key.Escape);
            return false;
        }
        await KeyPress(Key.Space);
        // The battlefield's own answer: it stays when the window then follows the turn to the other seat.
        var answer = await Until(() => interaction.Pending is null && interaction.Answer is { } a ? a.Kind.ToString() : null, 20, "hotseat-placed");
        return answer == nameof(FeedbackKind.Accepted);
    }

    // ---- M7.5 stage 5b: two Godot clients play one whole melee match ----
    //
    // pvp-a creates the match (Seat One) and pvp-b joins it (Seat Two); both play it to the end with the keyboard. The
    // runner hard-kills pvp-b at the start of turn 2 and starts pvp-b-rejoin in the same home folder, which rejoins from
    // the saved seat and plays on. What they press is a fixed, test-only sequence, not a game feature and not an AI: the
    // first hint shown; every placement found by stepping the cursor; and in each activation of its own, Fight, else
    // Charge (then Fight), else Shoot, else a Move towards the nearest enemy, the first target or point whose preview
    // looks legal, then the first hint again (ending the activation). The server decides everything.

    private readonly Dictionary<string, int> _pvpAnswers = [];
    private int _pvpDone;
    private bool _pvpRefusalChecked;

    private async Task PvpCreate()
    {
        await TypeInto(Field("ws://"), _server, "pvp-type-server");
        await ChooseOption(Option("ranged"), _scenario, "pvp-choose-scenario");
        await Press(ButtonNamed("Create match"), Key.Enter, "pvp-create-enter");
        var code = await Until(() => Label("Match code ") is { } l && l.Text.Contains("Seat One [A]") ? l.Text : null, 20, "create");
        Check("pvp-create", code is not null, code ?? "match screen with the code did not appear");
        if (code is null)
        {
            return;
        }
        await CloseRulesDialog();
        Mark("code", code.Split(' ')[2].TrimEnd('.'));
        await PvpPlay();
        await PvpEnd();
    }

    private async Task PvpJoin()
    {
        await TypeInto(Field("ws://"), _server, "pvp-type-server");
        await TypeInto(Field("", placeholder: "match code"), _code ?? "", "pvp-type-code");
        await Press(ButtonNamed("Join match"), Key.Enter, "pvp-join-enter");
        var joined = await Until(() => Label("Match code ") is { } l && l.Text.Contains("Seat Two [B]") ? l.Text : null, 20, "join");
        Check("pvp-join", joined is not null && joined.Contains(_code ?? "?"), joined ?? "no match screen");
        await CloseRulesDialog();
        // Killed by the runner during play: this role never ends on its own.
        await PvpPlay();
        await PvpEnd();
    }

    private async Task TestMatchPlay()
    {
        var seat = _role == "tm-a" ? "Seat One [A]" : "Seat Two [B]";
        var shown = await Until(() => Label("Match code ") is { } l && l.Text.Contains(seat) ? l.Text : null, 60, "test-match");
        Check("tm-connected-itself", shown is not null, shown ?? "the window did not connect to its match by itself");
        if (shown is null)
        {
            return;
        }
        await CloseRulesDialog();
        await PvpPlay();
        if (!MatchOver())
        {
            return;
        }
        await PvpEnd();
    }

    private async Task PvpRejoin()
    {
        await TypeInto(Field("ws://"), _server, "pvp-type-server");
        await Press(ButtonNamed("Refresh saved seats"), Key.Enter, "pvp-refresh-enter");
        var saved = await Until(() => ButtonStarting("Rejoin match "), 10, "saved");
        Check("pvp-saved-seat-listed", saved is not null && saved.Text.Contains(_code ?? "?") && saved.Text.EndsWith("Seat Two", StringComparison.Ordinal),
            saved?.Text ?? "no saved seat button");
        await Press(saved, Key.Enter, "pvp-rejoin-saved-enter");
        var joined = await Until(() => Label("Match code ") is { } l && l.Text.Contains("Seat Two [B]") ? l.Text : null, 20, "rejoin");
        Check("pvp-rejoin-same-seat", joined is not null && joined.Contains(_code ?? "?"), joined ?? "no match screen");
        await CloseRulesDialog();
        // Caught up: connected, and every event from the first to the newest, once each and in order.
        var synced = await Until(() => Banner().StartsWith("Connection: Connected", StringComparison.Ordinal) && LogText().Contains("--- Turn 2 ---", StringComparison.Ordinal)
            && !LogText().EndsWith('…') ? "ok" : null, 30, "rejoin-synced");
        await Frames(5);
        var lines = Lines(LogText());
        var (inOrder, order) = OnceEachInOrder(lines);
        Check("pvp-rejoin-caught-up", synced is not null && inOrder, $"{Banner()} || {lines.Count} events after rejoining; {order}");
        Write($"DUMP rejoined log={string.Join("|", lines)}");
        var done = _pvpDone;
        await PvpPlay();
        Check("pvp-plays-on-after-rejoin", _pvpDone > done, $"{_pvpDone - done} commands answered DONE after rejoining");
        await PvpEnd();
    }

    // The rules dialog opens on a client's first match; Escape closes it.
    private async Task CloseRulesDialog()
    {
        await Until(() => GapsDialog()?.Visible == true ? "ok" : null, 3, "rules-dialog");
        if (GapsDialog()?.Visible == true)
        {
            await KeyPress(Key.Escape);
            await Frames(3);
        }
    }

    private bool MatchOver() => LogText().Contains("MATCH ENDED", StringComparison.Ordinal) || Section("RESULT OF THE MATCH").Length > 0;

    // "[12] One activates a2." (not "One activates first.").
    private static bool IsActivationStart(string line) =>
        (line.Contains("] One activates ", StringComparison.Ordinal) || line.Contains("] Two activates ", StringComparison.Ordinal)) && !line.EndsWith(" activates first.", StringComparison.Ordinal);

    // Consecutive event numbers with none missing and none twice, ending at the newest. The screen lists the newest
    // 200 events (MatchControllerOptions.LogLines), so a longer match starts its list later than event 1.
    private static (bool Pass, string Detail) OnceEachInOrder(List<string> lines)
    {
        static int Seq(string line) => line.IndexOf(']') is > 1 and var close && int.TryParse(line[1..close], out var seq) ? seq : -1;
        var first = lines.Count == 0 ? -1 : Seq(lines[0]);
        for (var i = 0; i < lines.Count; i++)
        {
            if (Seq(lines[i]) != first + i)
            {
                return (false, $"line {i + 1} is {lines[i]}");
            }
        }
        return (lines.Count > 0 && (first == 1 || lines.Count == 200), $"events {first} to {first + lines.Count - 1}, each once and in order");
    }

    private void Answer(string action, string answer)
    {
        var kind = answer.Split(':')[0];
        _pvpAnswers[$"{action} {kind}"] = _pvpAnswers.GetValueOrDefault($"{action} {kind}") + 1;
        if (kind == "DONE")
        {
            _pvpDone++;
        }
    }

    // The answer to a command just sent: a new LAST COMMAND text, or new events once nothing is waiting (an accepted
    // command always adds events, and its DONE text can equal the previous one).
    private async Task<string> AnswerAfter(string before, int events) =>
        await Until(() => Section("LAST COMMAND") is var now && !now.StartsWith("SENT:", StringComparison.Ordinal) && (now != before || Lines(LogText()).Count > events) ? now : null, 20, "pvp-answer")
        ?? Section("LAST COMMAND");

    private async Task PvpPlay()
    {
        var deadline = Time.GetTicksMsec() + 1_500_000;
        var tried = new HashSet<int>();
        var turnTwo = false;
        var pauseSeen = false;
        var placeFailures = 0;
        while (Time.GetTicksMsec() < deadline && !MatchOver())
        {
            if (_role == "pvp-b" && !turnTwo && LogText().Contains("--- Turn 2 ---", StringComparison.Ordinal))
            {
                // The runner kills this process now, whatever it is doing; what it has seen so far must survive.
                turnTwo = true;
                var seen = Lines(LogText());
                Write($"DUMP before-kill log={string.Join("|", seen)}");
                Mark("turn-2", seen.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (_role == "tm-b" && LogText().Contains("--- Turn 2 ---", StringComparison.Ordinal) && !System.IO.File.Exists(System.IO.Path.Combine(_out, "tm-b-closed")))
            {
                // Once per match, as a player closing the window: the test center must reopen it and rejoin.
                System.IO.File.WriteAllText(System.IO.Path.Combine(_out, "tm-b-closed"), "");
                Mark("closing");
                GetTree().Quit();
                return;
            }
            if (_role == "pvp-a" && !pauseSeen && Banner().Contains("paused", StringComparison.OrdinalIgnoreCase))
            {
                pauseSeen = true;
                await Frames(3);
                Check("pvp-opponent-drop-pauses", Banner().Contains("paused", StringComparison.OrdinalIgnoreCase) && Hint() is null,
                    $"{Banner()} || actions: {Section("YOUR ACTIONS").Replace('\n', '|')}");
                Mark("saw-pause");
                continue;
            }
            var board = Controls<BoardView>().FirstOrDefault(b => b.IsVisibleInTree());
            var hint = Hint();
            if (board is not null && board.Interaction.Available.Contains(InteractionMode.Place) && board.Interaction.Pending is null
                && Banner().StartsWith("Connection: Connected", StringComparison.Ordinal) && !Banner().Contains("paused", StringComparison.OrdinalIgnoreCase))
            {
                var placed = await PlaceSomewhere(board);
                Answer("Place", placed ? "DONE" : "NOT DONE");
                placeFailures = placed ? 0 : placeFailures + 1;
                if (placeFailures >= 5)
                {
                    Check("pvp-place", false, $"no placement was accepted 5 times in a row: {board.Interaction.Notice} {Section("LAST COMMAND")}");
                    break;
                }
                continue;
            }
            // After a look check (and its screenshot) the screen may have been redrawn: read the board and hints again.
            if (_role == "pvp-a" && board?.Scene is { } looking && await UnitLookChecks(board, looking))
            {
                continue;
            }
            // Once, at Seat One's first turn to activate with two units to choose from: choose, switch, act by mouse.
            if (_role == "pvp-a" && !_choiceChecked && hint is not null && hint.Text.StartsWith("Activate ", StringComparison.Ordinal) && board?.Scene is { } choosing
                && board.Interaction.Pending is null && choosing.Models.Count(m => m.Yours && board.Interaction.CanChoose(m.Id)) >= 2)
            {
                await PvpChooseAndSwitch(board);
                continue;
            }
            var activation = Lines(LogText()).Count(IsActivationStart);
            if (hint is not null && board is not null && board.Interaction.Available.Contains(InteractionMode.Move) && tried.Add(activation))
            {
                if (_role == "pvp-a" && !_pvpRefusalChecked)
                {
                    await PvpRefusedMidMatch(board);
                }
                await PvpActivation(board);
                continue;
            }
            if (hint is not null)
            {
                var label = hint.Text;
                var before = Section("LAST COMMAND");
                var events = Lines(LogText()).Count;
                if (!await FocusByTab(hint, 60))
                {
                    continue;
                }
                await KeyPress(Key.Enter);
                Answer(label.StartsWith("End ", StringComparison.Ordinal) ? "End" : label.Split(' ')[0], await AnswerAfter(before, events));
                continue;
            }
            var count = Lines(LogText()).Count;
            var banner = Banner();
            await Until(() => Lines(LogText()).Count != count || Hint() is not null || Banner() != banner || MatchOver()
                || Controls<BoardView>().FirstOrDefault(b => b.IsVisibleInTree()) is { } b && b.Interaction.Available.Count > 0 ? "changed" : null, 5, "pvp-wait");
        }
    }

    private bool _choiceChecked;
    private bool _activatedLookChecked;
    private bool _lookResetChecked;
    private string? _chosenUnit;

    // Whether every model is drawn as its server state says: a grey wash exactly on the models that activated this turn.
    private static (bool Pass, string Detail) LooksMatchState(BoardView board, BoardScene scene)
    {
        var wrong = new List<string>();
        foreach (var model in scene.Models)
        {
            var seat = board.SeatColourOf(model);
            var expected = model.Look == UnitLook.Activated ? seat.Lerp(new Color(0.5f, 0.5f, 0.5f), BoardView.ActivatedWash) : seat;
            if (board.BodyColourOf(model.Id) is not { } drawn || !drawn.IsEqualApprox(expected))
            {
                wrong.Add($"{model.Id.Value} ({model.Look})");
            }
        }
        return (wrong.Count == 0, wrong.Count == 0 ? string.Join(", ", scene.Models.Select(m => $"{m.Id.Value} {m.Look}")) : $"drawn wrong: {string.Join(", ", wrong)}");
    }

    // Stage A: once the chosen unit's activation has ended, it is greyed (and says "activated") while the units yet to
    // activate keep their seat colour; at the start of turn 2 the grey is gone from every unit that has not activated again.
    private async Task<bool> UnitLookChecks(BoardView board, BoardScene scene)
    {
        var did = false;
        var log = Lines(LogText());
        if (!_activatedLookChecked && _chosenUnit is { } unit && log.Any(l => l.Contains($" {unit} ends its activation", StringComparison.Ordinal))
            && !log.Any(l => l.Contains("--- Turn 2 ---", StringComparison.Ordinal)))
        {
            _activatedLookChecked = true;
            did = true;
            await Frames(3);
            scene = board.Scene!;
            var me = scene.Models.FirstOrDefault(m => m.Id.Value == unit);
            var (pass, detail) = LooksMatchState(board, scene);
            var grey = me is not null && me.Look == UnitLook.Activated && board.BodyColourOf(me.Id) is { } c && !c.IsEqualApprox(board.SeatColourOf(me));
            var label = me is not null && board.IdLabelOf(me.Id) is { } idText && idText.EndsWith(" ✓", StringComparison.Ordinal);
            Check("pvp-activated-unit-greyed", pass && grey && label && scene.Models.Any(m => m.Yours && m.Look != UnitLook.Activated && board.BodyColourOf(m.Id) is { } s && s.IsEqualApprox(board.SeatColourOf(m))),
                $"{unit}: greyed {grey}, label says activated {label}; {detail}");
            await CloseShot(board, me?.Id, "pvp-a-activated-greyed");
        }
        var turnTwo = log.FindIndex(l => l.Contains("--- Turn 2 ---", StringComparison.Ordinal));
        if (!_lookResetChecked && _activatedLookChecked && turnTwo >= 0 && _chosenUnit is { } again)
        {
            _lookResetChecked = true;
            did = true;
            await Frames(3);
            scene = board.Scene!;
            // The unit activated in turn 1 is drawn normally again, unless it has already activated in turn 2.
            var activatedAgain = Lines(LogText()).Skip(turnTwo).Any(l => l.EndsWith($" activates {again}.", StringComparison.Ordinal));
            var me = scene.Models.FirstOrDefault(m => m.Id.Value == again);
            var (pass, detail) = LooksMatchState(board, scene);
            var reset = me is null || activatedAgain || (me.Look != UnitLook.Activated && board.BodyColourOf(me.Id) is { } c && c.IsEqualApprox(board.SeatColourOf(me)));
            Check("pvp-activated-look-resets", pass && reset, $"{again}: activated again in turn 2 {activatedAgain}, look {me?.Look}; {detail}");
            await CloseShot(board, me?.Id, "pvp-a-turn-2-look-reset");
        }
        return did;
    }

    // A real left-click on a model (board-local position from the scene).
    private async Task ClickModel(BoardView board, Rules.Core.State.ModelId id)
    {
        await MouseOn(board);
        if (board.Scene?.Find(id) is { } m && board.ScreenOf(m.X, m.Y, m.Standing) is { } local)
        {
            var global = board.GetGlobalTransformWithCanvas() * local;
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = global, GlobalPosition = global });
                await Frames(2);
            }
        }
        await Frames(3);
        await MouseOff(board);
    }

    // Choosing a unit sends nothing; switching to another sends nothing; the first action (a mouse Move, else "Take no
    // action") activates the unit finally chosen and nobody else, then acts; after that, clicking another own unit
    // switches nothing and says why. Both windows' event lists are compared by the runner (pvp-both-same-events).
    private async Task PvpChooseAndSwitch(BoardView board)
    {
        _choiceChecked = true;
        var interaction = board.Interaction;
        var scene = board.Scene!;
        var units = scene.Models.Where(m => m.Yours && interaction.CanChoose(m.Id)).Select(m => m.Id).ToList();
        var (first, second) = (units[0], units[1]);
        var lastCommand = Section("LAST COMMAND");
        var events = Lines(LogText()).Count;
        bool NothingSent() => Section("LAST COMMAND") == lastCommand && Lines(LogText()).Count == events && interaction.Pending is null;

        // Your units the rules let you activate now carry the faint ring, and no other model does.
        var ringed = board.EligibleRings.Select(i => i.Value).Order(StringComparer.Ordinal).ToList();
        var wanted = units.Where(u => u != board.Selected?.Id).Select(i => i.Value).Order(StringComparer.Ordinal).ToList();
        Check("pvp-eligible-units-marked", ringed.Count >= 2 && ringed.SequenceEqual(wanted), $"rings on {string.Join(", ", ringed)}; the rules offer {string.Join(", ", units.Select(u => u.Value))}");
        await CloseShot(board, first, "pvp-a-eligible-rings");

        await ClickModel(board, first);
        await Frames(10);
        Check("pvp-choose-unit-sends-nothing", interaction.Chosen == first && board.BarLabels.Contains("Not activated yet") && NothingSent(),
            $"chosen {interaction.Chosen?.Value}; bar: {string.Join(" / ", board.BarLabels)}; events {events} -> {Lines(LogText()).Count}; last command unchanged: {Section("LAST COMMAND") == lastCommand}");
        await CloseShot(board, first, "pvp-a-chosen");

        await ClickModel(board, second);
        await Frames(10);
        await Frames(3);
        var strip = Strip();
        // Proposal G3: the HUD on top (seat, whose move, turn, phase, VP, units left); the chosen unit's state is in the panel.
        Check("pvp-status-strip", strip.StartsWith("[A] SEAT ONE · YOUR MOVE · Turn ", StringComparison.Ordinal)
            && strip.Contains($"to activate: you {units.Count}, ", StringComparison.Ordinal) && strip.Contains(" · Activation · ", StringComparison.Ordinal)
            && strip.Contains("VP you ", StringComparison.Ordinal) && PanelHas("Chosen: not activated yet (nothing sent)"),
            $"HUD: {strip} || panel: {PanelText.Replace('\n', '|')}");
        Check("pvp-switch-unit-sends-nothing", interaction.Chosen == second && board.BarLabels.Contains("Not activated yet") && NothingSent(),
            $"chosen {interaction.Chosen?.Value} (wanted {second.Value}); bar: {string.Join(" / ", board.BarLabels)}; events {events} -> {Lines(LogText()).Count}");
        await CloseShot(board, second, "pvp-a-switched");

        // The first action: Move by mouse to a free spot about 1" away, else "Take no action".
        var sentMove = false;
        if (interaction.Offered.Contains(InteractionMode.Move) && board.BarButtonAt(Interaction.Words(InteractionMode.Move)) is { } move)
        {
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = move, GlobalPosition = move });
                await Frames(2);
            }
            await Frames(3);
            var me = scene.Find(second)!;
            var spot = interaction.ReachArea().Where(p => scene.Models.All(m => (m.X - p.X) * (m.X - p.X) + (m.Y - p.Y) * (m.Y - p.Y) > 2.25))
                .OrderBy(p => Math.Abs(Math.Sqrt((p.X - me.X) * (p.X - me.X) + (p.Y - me.Y) * (p.Y - me.Y)) - 1)).Select(p => (TablePoint?)p).FirstOrDefault();
            await MouseOn(board);
            if (interaction.Mode == InteractionMode.Move && spot is { } at && board.ScreenOf(at.X, at.Y) is { } local)
            {
                var global = board.GetGlobalTransformWithCanvas() * local;
                foreach (var pressed in new[] { true, false })
                {
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = global, GlobalPosition = global });
                    await Frames(2);
                }
                sentMove = true;
            }
            await MouseOff(board);
        }
        if (!sentMove)
        {
            if (interaction.Mode != InteractionMode.None)
            {
                await KeyPress(Key.Escape);
            }
            board.PressBar("Take no action");
        }
        var acted = sentMove ? $" {second.Value} moves to " : $" {second.Value} ends its activation";
        var done = await Until(() => Lines(LogText()).Skip(events).Any(l => l.Contains(acted, StringComparison.Ordinal)) && interaction.Pending is null ? "done" : null, 30, "first action");
        var added = Lines(LogText()).Skip(events).ToList();
        var activations = added.Where(IsActivationStart).ToList();
        Check("pvp-first-action-activates-chosen", done is not null && added.Count > 0 && added[0] == $"[{events + 1}] One activates {second.Value}." && activations.Count == 1
            && interaction.Chosen is null && Section("LAST COMMAND").StartsWith("DONE:", StringComparison.Ordinal),
            $"{(sentMove ? "Move" : "Take no action")}; new events: {string.Join(" | ", added.Take(4))}; last command: {Section("LAST COMMAND")}");
        Answer(sentMove ? "Move" : "End", done is not null ? "DONE" : "NOT DONE");
        Write($"DUMP choice activation={(added.Count > 0 ? added[0] : "")}");
        _chosenUnit = second.Value;
        await CloseShot(board, second, "pvp-a-chosen-acted");

        // Committed now: clicking the first unit again switches nothing, sends nothing, and the note says why.
        if (interaction.Mode != InteractionMode.None)
        {
            await KeyPress(Key.Escape);
        }
        (lastCommand, events) = (Section("LAST COMMAND"), Lines(LogText()).Count);
        var stillActive = Lines(LogText()).Count(IsActivationStart);
        await ClickModel(board, first);
        var note = await Until(() => board.NoteTexts.FirstOrDefault(n => n == $"{first.Value}: CAN'T SWITCH"), 3, "switch refused note");
        Check("pvp-no-switch-after-action", interaction.Chosen is null && note is not null && interaction.Notice?.Contains("cannot switch", StringComparison.Ordinal) == true
            && NothingSent() && Lines(LogText()).Count(IsActivationStart) == stillActive,
            $"chosen {interaction.Chosen?.Value}; notes: {string.Join(" / ", board.NoteTexts)}; notice: {interaction.Notice}");
        await CloseShot(board, first, "pvp-a-no-switch");
        // Back to the acting unit, so the fixed sequence goes on with it.
        board.Select(second);
        await Frames(3);
    }

    // One own activation: Fight, else Charge (then Fight), else Shoot, else Move; the activation is then ended by its hint.
    private async Task PvpActivation(BoardView board)
    {
        CheckBloodLabels(board);
        await CheckDiceTray(board);
        if (!await FocusByTab(board, 80))
        {
            return;
        }
        if (await PvpTarget(board, Key.X, "Fight") is not null)
        {
            return;
        }
        if (await PvpTarget(board, Key.C, "Charge") is { } charge)
        {
            if (charge.StartsWith("DONE:", StringComparison.Ordinal))
            {
                await Until(() => Hint() is not null ? "ok" : null, 20, "charge-resolved");
                await PvpTarget(board, Key.X, "Fight");
            }
            return;
        }
        if (await PvpTarget(board, Key.S, "Shoot") is not null)
        {
            return;
        }
        var events = Lines(LogText()).Count;
        var (moved, _) = await SendFirstLegalMove(board);
        Answer("Move", moved ? "DONE" : Lines(LogText()).Count > events ? "OTHER" : "NOT SENT");
    }

    // The action key, then N through the targets until the preview looks legal; Space sends it. Null when no target
    // looks legal (nothing is sent).
    private bool _diceChecked;
    private bool _glideChecked;
    private bool _followChecked;

    private static bool OnBoardScreen(BoardView board, double x, double y, double height) =>
        board.ScreenOf(x, y, height) is { } p && p.X >= 0 && p.Y >= 0 && p.X <= board.Size.X && p.Y <= board.Size.Y;
    private int _bloodLabelsChecked;
    private int _bloodLabelsWithMarkers;
    private readonly List<string> _bloodLabelProblems = [];

    // Every label drawn on the battlefield against its model in the battlefield's scene (built from the server's state;
    // BoardSceneTests check the scene's counts against the rules' state): the Blood Markers, exactly when there are some.
    private void CheckBloodLabels(BoardView? board)
    {
        if (board?.Scene is not { } scene)
        {
            return;
        }
        foreach (var shape in scene.Models)
        {
            if (board.IdLabelOf(shape.Id) is not { } label)
            {
                continue;
            }
            _bloodLabelsChecked++;
            if (shape.BloodMarkers > 0)
            {
                _bloodLabelsWithMarkers++;
            }
            var shown = label.Contains(ModelShape.MarkerMark, StringComparison.Ordinal);
            if (shown != shape.BloodMarkers > 0 || (shown && !label.Contains($" {ModelShape.MarkerMark}{shape.BloodMarkers}", StringComparison.Ordinal)))
            {
                _bloodLabelProblems.Add($"{label} has {shape.BloodMarkers}");
            }
        }
    }

    // ---- visible dice rolls (QoL stage F2) ----

    private bool _diceClickChecked;
    private bool _diceLong;
    private bool _diceFadeChecked;
    private int _diceOffShown = -1;
    private int _diceOffLogDice = -1;
    private bool _diceOffChecked;
    private readonly List<string> _diceStacks = [];

    private int LogDiceCount() => _main is Main screen ? screen.LogRows.Count(r => r.Full.Contains("\n      Dice: ", StringComparison.Ordinal)) : 0;

    // Checked at each own activation and at the end. Every window: boxes with several rows keep event order. The first
    // match window also clicks a box shut, watches one fade after its time, and turns the dice Off until a new roll has
    // come in without being shown.
    private async Task CheckDiceTray(BoardView? board)
    {
        if (board is null)
        {
            return;
        }
        var tray = board.Dice;
        foreach (var box in tray.Feed.Boxes.Where(b => b.Rows.Count > 1))
        {
            _diceStacks.Add($"{box.Id}: {string.Join(", ", box.Rows.Select(r => r.Seq))} {(box.Rows.Select(r => r.Seq).SequenceEqual(box.Rows.Select(r => r.Seq).Order()) ? "in order" : "OUT OF ORDER")}");
        }
        if (_role != "pvp-a")
        {
            return;
        }
        if (!_diceLong)
        {
            // Long (8 s), so that a box is still there when this window's next activation starts.
            _diceLong = true;
            ((Main)_main).ChooseDice(2);
        }
        if (_diceOffShown >= 0 && !_diceOffChecked && LogDiceCount() > _diceOffLogDice)
        {
            _diceOffChecked = true;
            var shownNow = tray.Feed.Shown.Count;
            Check("pvp-dice-off", tray.BoxIds.Count == 0 && shownNow == _diceOffShown,
                $"Off: {LogDiceCount() - _diceOffLogDice} new rolls in the log, {shownNow - _diceOffShown} shown, {tray.BoxIds.Count} boxes on screen");
            ((Main)_main).ChooseDice(1);
            await Frames(2);
            return;
        }
        if (tray.BoxIds.Count == 0)
        {
            return;
        }
        if (!_diceClickChecked)
        {
            _diceClickChecked = true;
            await Frames(3);
            var id = tray.BoxIds[^1];
            if (tray.BoxRect(id) is not { } rect)
            {
                return;
            }
            var field = new Rect2(Vector2.Zero, board.Size);
            Check("pvp-dice-tray-inside", field.Encloses(rect) && Math.Abs(rect.Position.X - DiceTray.Margin) < 1 && rect.End.Y <= board.Size.Y - DiceTray.Margin + 1
                && rect.End.Y >= board.Size.Y - DiceTray.Margin - 1,
                $"box {rect} in the battlefield {field}; dice {string.Join(" / ", tray.Feed.Boxes.Single(b => b.Id == id).Rows.Select(r => $"{string.Join(",", r.Roll.Faces)} {r.Roll.Result}"))}");
            var before = Section("LAST COMMAND");
            var selected = board.Selected?.Id;
            var at = board.GlobalPosition + rect.GetCenter();
            foreach (var pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at });
                await Frames(2);
            }
            await Frames(2);
            Check("pvp-dice-click-closes", !tray.BoxIds.Contains(id) && Section("LAST COMMAND") == before && board.Selected?.Id == selected,
                $"box {id} {(tray.BoxIds.Contains(id) ? "still shown" : "closed")}; nothing sent: {Section("LAST COMMAND") == before}; selection kept: {board.Selected?.Id == selected}");
            return;
        }
        if (!_diceFadeChecked)
        {
            _diceFadeChecked = true;
            var id = tray.BoxIds[^1];
            var box = tray.Feed.Boxes.Single(b => b.Id == id);
            var updated = box.Updated;
            var start = DiceTray.Now;
            var halfway = false;
            while (tray.BoxIds.Contains(id) && DiceTray.Now - start < 15)
            {
                updated = box.Updated;
                var alpha = tray.BoxAlpha(id);
                halfway |= alpha is > 0.05f and < 0.95f;
                await Frames(1);
            }
            var gone = DiceTray.Now - updated;
            var seconds = tray.Feed.Seconds;
            Check("pvp-dice-fades", !tray.BoxIds.Contains(id) && halfway && gone >= seconds - 0.05 && gone <= seconds + 1.5,
                $"box {id} gone {gone:0.00} s after its last roll (setting {seconds} s); faded on the way: {halfway}");
            // Then Off: the boxes go at once, and the next rolls are not shown (they stay in the combat log).
            ((Main)_main).ChooseDice(3);
            await Frames(2);
            _diceOffShown = tray.Feed.Shown.Count;
            _diceOffLogDice = LogDiceCount();
        }
    }

    // At the end of the match, in every window: each roll shown once, each equal to the dice its event has in the combat
    // log, and none from before this window first showed the match.
    private void DiceEndChecks(BoardView? board)
    {
        if (board is null || _main is not Main screen)
        {
            return;
        }
        var shown = board.Dice.Feed.Shown;
        var logged = new Dictionary<int, string>();
        foreach (var row in screen.LogRows)
        {
            var close = row.Full.IndexOf(']', StringComparison.Ordinal);
            var dice = row.Full.IndexOf("\n      Dice: ", StringComparison.Ordinal);
            if (row.Full.StartsWith('[') && close > 1 && dice >= 0 && int.TryParse(row.Full[1..close], System.Globalization.CultureInfo.InvariantCulture, out var seq))
            {
                logged[seq] = string.Join(",", LogFaces(row.Full[(dice + 13)..]));
            }
        }
        var problems = new List<string>();
        foreach (var row in shown)
        {
            var faces = string.Join(",", row.Roll.Faces);
            if (logged.TryGetValue(row.Seq, out var log) && log != faces)
            {
                problems.Add($"[{row.Seq}] shown {faces}, log {log}");
            }
            if (row.Seq <= board.ReplayedUpTo || board.Skipped.Any(r => row.Seq > r.From && row.Seq <= r.To))
            {
                problems.Add($"[{row.Seq}] shown though this window caught up on it without showing it");
            }
        }
        var twice = shown.GroupBy(r => r.Seq).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var compared = shown.Count(r => logged.ContainsKey(r.Seq));
        Check("pvp-dice-shown-once-as-logged", shown.Count > 0 && compared > 0 && problems.Count == 0 && twice.Count == 0,
            $"{shown.Count} rolls shown, {compared} compared with the log's dice, {logged.Count} in the log; shown twice: {string.Join(",", twice)}; {string.Join(" / ", problems.Take(5))}");
        if (_diceStacks.Count > 0)
        {
            Check("pvp-dice-stacked", _diceStacks.All(s => s.EndsWith("in order", StringComparison.Ordinal)), string.Join(" / ", _diceStacks.Distinct().Take(6)));
        }
    }

    // The faces the combat log lists for a roll: "rolled 5, 2, 6; kept ...", "D6 4", or a roll-off's rounds.
    private static List<int> LogFaces(string dice)
    {
        string part;
        if (dice.StartsWith("rolled ", StringComparison.Ordinal))
        {
            part = dice[7..dice.IndexOf(';')];
            return [.. part.Split(", ").Select(x => int.Parse(x, System.Globalization.CultureInfo.InvariantCulture))];
        }
        if (dice.StartsWith("D6 ", StringComparison.Ordinal))
        {
            return [int.Parse(dice[3..].Trim(), System.Globalization.CultureInfo.InvariantCulture)];
        }
        // "Seat One 3, Seat Two 3 (tie, rolled again); Seat One 2, Seat Two 5": the digit after each seat's name.
        var faces = new List<int>();
        for (var at = 0; (at = dice.IndexOf("Seat ", at, StringComparison.Ordinal)) >= 0; at += 5)
        {
            var space = dice.IndexOf(' ', at + 5);
            if (space > 0 && space + 1 < dice.Length && char.IsAsciiDigit(dice[space + 1]))
            {
                faces.Add(dice[space + 1] - '0');
            }
        }
        return faces;
    }

    private async Task<string?> PvpTarget(BoardView board, Key action, string name)
    {
        if (Hint() is null || !await FocusByTab(board, 80))
        {
            return null;
        }
        var interaction = board.Interaction;
        await MouseOff(board);
        await KeyPress(action);
        var count = interaction.Mode == InteractionMode.None ? 0 : interaction.Targets().Count;
        for (var i = 0; i < count && interaction.Preview is not { Legal: true }; i++)
        {
            await KeyPress(Key.N);
        }
        if (interaction.Preview is not { Legal: true })
        {
            await KeyPress(Key.Escape);
            return null;
        }
        if (!_diceChecked && interaction.Preview is { Kind: PreviewKind.Shoot or PreviewKind.Fight, Target: { } aimed } attack)
        {
            // XCOM-style UX, improvement 1: the first legal attack preview shows its dice in the panel, as the rules
            // count them (the Client.Core tests check every number against the rules); no odds.
            _diceChecked = true;
            var diceLines = attack.Dice?.Lines(attack.Model, aimed) ?? [];
            Check("pvp-preview-dice", diceLines.Count > 0 && diceLines.All(PanelHas) && PanelHas("Hit roll: ") && PanelHas("Injury roll: ") && !PanelHas("%"),
                $"{name}: {string.Join(" | ", diceLines)}; panel: {PanelText.Replace('\n', '|')}");
        }
        var before = Section("LAST COMMAND");
        var events = Lines(LogText()).Count;
        await KeyPress(Key.Space);
        var answer = await AnswerAfter(before, events);
        Answer(name, answer);
        await Frames(3);
        return answer;
    }

    // Seat One's first activation: a Move far beyond the model's reach. The preview refuses it, the first Space sends
    // nothing, the second sends it anyway, and the server refuses it: no event, the model stays where it was.
    private async Task PvpRefusedMidMatch(BoardView board)
    {
        _pvpRefusalChecked = true;
        var interaction = board.Interaction;
        var me = board.Scene?.Models.FirstOrDefault(m => m.Active);
        if (me is null || !await FocusByTab(board, 80))
        {
            Check("pvp-refused-mid-match", false, $"no active model of this seat on the battlefield, or Tab never reached it; focus on {Describe(Focused())}");
            return;
        }
        await MouseOff(board);
        await KeyPress(Key.M);
        var away = me.X < 18 ? Key.Right : Key.Left;
        for (var i = 0; i < 10; i++)
        {
            await KeyPress(away, shift: true);
        }
        var preview = interaction.Preview;
        var before = Section("LAST COMMAND");
        var events = Lines(LogText()).Count;
        await KeyPress(Key.Space);
        var notSent = interaction.Pending is null && (interaction.Notice ?? "").StartsWith("Not sent: the preview says the server would refuse.", StringComparison.Ordinal);
        await KeyPress(Key.Space);
        var answer = await Answered(before);
        await Frames(5);
        var after = board.Scene?.Find(me.Id);
        Check("pvp-refused-mid-match", preview?.Refusal is not null && notSent && answer.StartsWith("REFUSED BY THE RULES:", StringComparison.Ordinal)
            && Lines(LogText()).Count == events && after is not null && Near(new TablePoint(after.X, after.Y), me.X, me.Y) && Hint() is not null,
            $"{preview?.Headline}; first Space: {(notSent ? "not sent" : interaction.Notice)}; answer: {answer}; events {events} -> {Lines(LogText()).Count}; model at {after?.X},{after?.Y} (was {me.X},{me.Y})");
        // Proposal G: a short REFUSED over the model; the reason and its page in the panel.
        var note = board.NoteTexts.FirstOrDefault(n => n == $"{me.Id.Value}: REFUSED");
        Check("pvp-server-refusal-note", note is not null && answer.StartsWith("REFUSED BY THE RULES:", StringComparison.Ordinal)
            && InPanel(board, "REFUSED BY THE RULES:") && PanelText.Contains(" p. ", StringComparison.Ordinal),
            $"notes: {string.Join(" / ", board.NoteTexts)}; answer: {answer}; panel: {PanelText.Replace('\n', '|')}");
        await CloseShot(board, me.Id, $"{_role}-refused-mid-match");
        await KeyPress(Key.Escape);
    }

    // Both seats at the end: the outcome as this seat sees it, the final state hash and the event count, all from the
    // screen, and nothing left to send. The runner compares the two windows with each other and with the signed record.
    private async Task PvpEnd()
    {
        var result = await Until(() => Section("RESULT OF THE MATCH") is { Length: > 0 } r ? r : null, 30, "pvp-result") ?? "";
        string? Value(string prefix) => result.Split('\n').FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
        var hash = Value("Final state hash: ") is { Length: 64 } h && h.All(char.IsAsciiHexDigitLower) ? h : null;
        var events = Value("Events: ") is { } e && int.TryParse(e, out var n) ? n : -1;
        await Frames(5);
        var lines = Lines(LogText());
        var (inOrder, order) = OnceEachInOrder(lines);
        var ended = lines.LastOrDefault(l => l.Contains("MATCH ENDED", StringComparison.Ordinal)) ?? "";
        var seat = _role is "pvp-a" or "gvt-a" or "tm-a" ? "One" : "Two";
        var expected = ended.Contains("MATCH ENDED: draw", StringComparison.Ordinal) ? "Draw" : ended.Contains($"MATCH ENDED: {seat} wins", StringComparison.Ordinal) ? "You won" : "You lost";
        Check("pvp-match-completed", ended.Length > 0 && result.StartsWith($"Result: {expected}", StringComparison.Ordinal) && hash is not null
            && inOrder && lines[^1] == ended && lines[^1].StartsWith($"[{events}] ", StringComparison.Ordinal),
            $"{result.Replace('\n', '|')} || {ended} || {order}");
        var board = Controls<BoardView>().FirstOrDefault(b => b.IsVisibleInTree());
        // XCOM-style UX, improvement 2: each label on the battlefield carries its model's Blood Markers as the server's
        // state has them ("•2"); checked through the whole match, here at the end too.
        CheckBloodLabels(board);
        await CheckDiceTray(board);
        DiceEndChecks(board);
        Check("pvp-labels-show-blood", _bloodLabelsChecked > 0 && _bloodLabelProblems.Count == 0,
            $"{_bloodLabelsChecked} labels checked, {_bloodLabelsWithMarkers} with markers; {string.Join(" / ", _bloodLabelProblems.Take(5))}");
        Check("pvp-nothing-to-send", Hint() is null && (board is null || board.Interaction.Available.Count == 0),
            $"actions: {Section("YOUR ACTIONS").Replace('\n', '|')}");
        await Shot($"{_role}-match-ended");
        // The event list, scrolled into view, for the report.
        if (EventList() is { } eventList)
        {
            for (Node? up = eventList.GetParent(); up is not null; up = up.GetParent())
            {
                if (up is ScrollContainer outer)
                {
                    outer.EnsureControlVisible(eventList);
                    break;
                }
            }
            await Frames(3);
            await Shot($"{_role}-event-list");
        }
        Write($"DUMP pvp-end final_hash={hash ?? ""}");
        Write($"DUMP pvp-end events={events}");
        Write($"DUMP pvp-end result={Value("Result: ") ?? ""}");
        Write($"DUMP pvp-end ended={ended}");
        Write($"DUMP pvp-end answers={string.Join(", ", _pvpAnswers.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key} {a.Value}"))}");
        Write($"DUMP pvp-end log={string.Join("|", lines)}");
        Write($"DUMP pvp-end strip={Strip()}");
        Write($"DUMP pvp-end dice={string.Join("|", (board?.Dice.Feed.Shown ?? []).Select(r => $"{r.Seq}:{string.Join(",", r.Roll.Faces)}:{r.Roll.Result}{(r.FollowUp is null ? "" : " " + r.FollowUp)}"))}");
        Write($"DUMP pvp-end dice_replayed={board?.ReplayedUpTo ?? 0}");
        Write($"DUMP pvp-end dice_skipped={string.Join(",", (board?.Skipped ?? []).Select(r => $"{r.From}-{r.To}"))}");
        // Stage D: one row per event line, in the same order and words, each with its kind's tag; the newest is marked.
        if (_main is Main screen)
        {
            var rows = screen.LogRows;
            var rowLines = rows.Select(r => r.Full.Split('\n')[0]).ToList();
            // Proposal G4: each row shows the compact line ("[41] A4 FIGHTS B3") under the same number, the narrator's line
            // on its tooltip; the newest is marked.
            var numbered = rows.All(r => r.Shown.StartsWith(r.Full[..(r.Full.IndexOf(']') + 1)] + " ", StringComparison.Ordinal));
            var verbs = new[] { " ACTIVATES", " MOVES ", " ENDS " }.Where(v => rows.Any(r => r.Shown.Contains(v, StringComparison.Ordinal))).ToList();
            Check("pvp-event-log-tags", rows.Count > 0 && rowLines.SequenceEqual(lines) && numbered && rows.All(r => r.Kind != EventKind.Other)
                && rows[^1].Marker == "▶" && rows[^1].Full.Contains("MATCH ENDED", StringComparison.Ordinal) && rows.Take(rows.Count - 1).All(r => r.Marker.Length == 0)
                && verbs.Count == 3,
                $"{rows.Count} rows, {lines.Count} lines, same order and words: {rowLines.SequenceEqual(lines)}; numbered alike: {numbered}; verbs seen: {string.Join(",", verbs)}; newest: {rows.LastOrDefault().Marker} {rows.LastOrDefault().Shown}");
        }
        else
        {
            Check("pvp-event-log-tags", false, "the match screen was not found");
        }
        Write($"DUMP pvp-end looks={(board?.Scene is { } final ? string.Join(",", final.Models.Select(m => $"{m.Id.Value}:{m.Look}:{m.Status}")) : "")}");
        Mark("ended");
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
            for (var i = 0; i < option!.ItemCount && option.GetPopup().Visible && option.GetPopup().GetFocusedItem() is var at && (at < 0 || ItemName(option, at) != item); i++)
            {
                await KeyPress(Key.Down);
            }
            await KeyPress(Key.Enter);
        }
        if (id is not null)
        {
            Check(id, option is not null && ItemName(option, option.Selected) == item,
                $"{Describe(option)} selected \"{(option is null ? "" : ItemName(option, option.Selected))}\" with Enter, Down, Enter");
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

    private OptionButton? Option(string selected) => Controls<OptionButton>().FirstOrDefault(o => o.IsVisibleInTree() && o.Selected >= 0 && ItemName(o, o.Selected) == selected);

    // An item's name: the scenario name kept with it (the scenario list shows a longer label), else its text.
    private static string ItemName(OptionButton option, int index) =>
        index >= 0 && option.GetItemMetadata(index).VariantType == Variant.Type.String ? option.GetItemMetadata(index).AsString() : option.GetItemText(index);

    // A label shown on screen, or one in the Menu while it is folded (proposal G3: the match code is in the Menu).
    private Godot.Label? Label(string startsWith) => Controls<Godot.Label>().FirstOrDefault(l => (l.IsVisibleInTree() || ((Main)_main).InMatchMenu(l))
        && l.Text.StartsWith(startsWith, StringComparison.Ordinal));

    private string Banner() => Label("Connection: ")?.Text ?? "";

    // The body of a section: the control after its title label.
    // The compact status line over the battlefield (QoL stage C).
    private string Strip() => Controls<Godot.Label>().FirstOrDefault(l => l.Name == "TopHud" && l.IsVisibleInTree())?.Text ?? "";

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

    // The combat log's rows in the narrator's full words (each row's tooltip), as the event list showed them before G4.
    private string LogText() => ((Main)_main).CombatLog.IsVisibleInTree() ? ((Main)_main).LogFullText : "";

    // The battlefield's camera and keys, in the Menu (read whether the Menu is open or folded).
    private string BoardInfo() => ((Main)_main).BoardInfoText;

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

    // The real mouse pointer may rest over (or cross) the window, and hovering over the battlefield moves its cursor.
    // The keyboard checks make the battlefield ignore the mouse first, so only their keys move the cursor; the mouse
    // checks turn it back on. The real pointer is never moved.
    private async Task MouseOff(BoardView board)
    {
        board.MouseFilter = Control.MouseFilterEnum.Ignore;
        await Frames(2);
    }

    private async Task MouseOn(BoardView board)
    {
        board.MouseFilter = Control.MouseFilterEnum.Stop;
        await Frames(2);
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // A model still travelling (improvement 3) is drawn between two places: checks measure the drawing once it stops.
        for (var i = 0; i < 120 && Controls<BoardView>().Any(b => b.Gliding); i++)
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

    // A screenshot close to a model, for the reports; the camera fits the table again afterwards.
    private async Task CloseShot(BoardView board, Rules.Core.State.ModelId? id, string name)
    {
        if (!_screens)
        {
            return;
        }
        board.CentreOn(id, 16);
        await Frames(4);
        await Shot(name);
        board.Fit();
        await Frames(2);
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
