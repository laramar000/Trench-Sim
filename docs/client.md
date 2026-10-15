# The Godot client (M7)

The desktop client lives in `client/`. It is a Godot 4 .NET project, kept **outside** `TrenchSim.sln`, so `dotnet build` and `dotnet test` on the solution never need Godot.

The client shows what the server says and sends commands. It never resolves a command, rolls dice or decides an outcome; the server does all of that (see `docs/protocol.md`).

## Status

- M7.1 (bootstrap): the project opens and runs, and shows that our .NET 10 libraries load inside Godot.
- M7.2 (shared client library): the client reaches the server through `Match.Client`. There is no match screen yet; the battlefield and HUD arrive in M7.3 to M7.5. Accepted 2026-10-15 at `276876e`, after Jason built and ran the client in the Godot 4.7.2 .NET editor on Windows 11.
- M7.3 (match state): a minimal match screen that shows the server's state of the match, as text, through the new `Client.Core` library. There is no battlefield yet: placing, moving and every command that needs a position or a target arrive in M7.4, so for now a match is deployed and played through `trench-cli`. Accepted 2026-10-15 at `23f133a`: on Jason's Windows 11 PC, `Run-WindowsUiCheck.ps1` exited 0 (all 93 UI checks, plus the project opening in the Godot 4.7.2 .NET editor without errors). The manual checks (editor Build and Play buttons, mouse, Copy code, title-bar close, readability by eye) were deferred by Jason and have not been done.
- M7.4 (battlefield and interaction), in stages approved by Jason on 2026-10-15 (3D now with placeholder shapes, a top-down mode, display heights Low 0.25", Cover 0.75", Wall 3", camera turns in 90° steps; true 3D rules are a later decision). Stage 4a (read-only 3D battlefield, camera, selection) passed on Jason's Windows PC (106 PASS, exit 0). Stage 4b (previews; nothing sent) is done and waits for Jason's Windows run; 4c (sending positional commands) follows only with his approval.

## The battlefield (M7.4)

`Client.Core.BoardScene` describes the battlefield in inches from the server's state: the table, the deployment zones (with the seat that chose each), terrain with its profile, kind and Objective status in words, and every model on the table with its base, level and status. `client/scripts/BoardView.cs` draws it in 3D with placeholder shapes: 1 scene unit is 1 inch, table x is scene X and table y is scene Z (y = 0 is north).

The rules have no heights: a model is on the ground or on a High platform (U17), and line of sight is a 2D test where Walls block and Cover, Low terrain and models do not. The 3D heights (`DisplayHeight`: Low 0.25", Cover 0.75", Wall 3") are display conventions only. The picture can suggest something the rules do not say, for example that a model on a platform sees over a wall; the rules' answer is what counts, and stage 4b draws line of sight from the rules' own sight rays.

Camera: angled (55°) or top-down (orthographic), turned in 90° steps, with pan, zoom and fit-table. Keys when the battlefield has focus: arrows pan, + and - zoom, Q and E turn, F fits, T switches angled and top-down, N and P select the next and previous model (yours first). Tab and Shift+Tab still move between controls, so selection uses N and P rather than Tab. Mouse: left click selects, right or middle drag pans, the wheel zooms. Labels keep their size on screen, follow the text size setting and show through walls; the selected model gets a ring and "SELECTED" in its label. Nothing on the battlefield sends a command yet.

### Previews (stage 4b)

On your deployment turn, or while your model is activating, the battlefield previews a command before it exists: `Client.Core.Interaction` holds the choice (an action, then a placement point, a path of waypoints or a target) and `Client.Core.Previews` works out what the server would probably answer with the rules' own read-only geometry (`Placement.Check`, `Movement.CheckPath` and `Cost`, `Cover.Of` with `LineOfSight.Between`, `Measure`, `Charging.CheckRoute` and `Plan`). Every preview says "preview; the server decides", and a refused one shows the rule's own reason and page. Getting a command ready (Space) only says so: sending arrives in stage 4c.

- Keys on the battlefield: L place a model, M move, D dash, R retreat, S shoot, C charge, X fight (F is Fit and P is Previous). Then arrows move the cursor half an inch (2" with Shift) relative to the camera; Enter fixes a waypoint (or gets a placement or a target ready); Backspace removes the last waypoint; N and P choose the target (nearest enemy first) or the model to place; Space gets the command ready; Esc cancels.
- Mouse: moving over the table moves the cursor (worked out at most every 80 ms), a click fixes a waypoint or picks the target.
- Drawn: the path with the inches used at each waypoint and at the end (difficult terrain counts double, as in `Movement.Cost`) and a ring where the base would stand; for a target, the rules' sight rays from the attacker (clear ones in colour, blocked ones grey), the distance, line of sight ("5 of 5 sight rays clear") and cover; for a charge, the planned path and the Charge roll that would reach base contact; for a placement, the base and whether it fits the zone. Legal or refused is always written on the battlefield, never shown by colour alone.
- Agreement: the server's validation is internal to `Rules.Core`, so `Previews` repeats its few checks in the same order and words. `PreviewAgreementTests` keeps them equal: over 24 simulated matches (both content files), every preview's command is decided by the rules, and a "legal" preview is always accepted while a refused one is refused with the same reason code, text and rule reference (about 230,000 previews).

Known limits of the placeholder view: labels of models close together overlap; models are small (a 1" base on a 36" table) until zoomed in. Previews of Dash only cover the move: the Risky roll is the server's.

## The shared client library (`src/Match.Client`)

Plain C#, used by both `trench-cli` and the Godot client, so protocol handling exists once:
- `ClientSession`: one WebSocket connection on `trench-sim.v2`. It numbers commands, resends an unanswered one with the same id, keeps the copy of the match (`MatchMirror`, every event hash-checked), and saves a new seat token before confirming it (`TokenSaved`).
- `SessionStore`: saved seats in `~/.trench-sim/sessions` (or `$TRENCH_SIM_HOME/sessions`), owner-only on Linux and macOS.
- `Reconnector` (decision D7): after a transient drop it rejoins the seat with its token and the copy it already holds, waiting 1, 2, 4 and 8 seconds, then every 15. It never retries on its own after a seat takeover, a refused or invalid token, a policy close, or a finished or aborted match, and a copy that failed its hash check is left for a manual rejoin. See below.

It references only `Match.Protocol` (which brings `Rules.Core`). It never resolves commands, rolls dice, applies events except through `MatchMirror`, or reads match records.

## The presentation library (`src/Client.Core`, M7.3)

Plain C#, tested without Godot. It references only `Match.Client`. The Godot scripts use it to turn the server's state into text and to send the player's choices; it decides nothing.
- `MatchController`: one seat in one match. It creates, joins or rejoins a saved seat, sends one command at a time and reports the answer, follows the connection (drops, automatic reconnects through `Reconnector`, takeovers, the match ending or being aborted), and keeps the recent events. `Snapshot()` gives a `ScreenState` with everything the screen shows; `Changed` says when to read it again.
- `MatchView`: status, turn, phase, whose move it is, both seats, every model, the current activation, the pending decision, the outcome. Whose move it is is display-only: the server alone decides whether a command is taken.
- `ActionHints`: the commands the rules engine's read-only query (`TurnSequence.LegalCommands`) lists for your seat, labelled. They are hints; the server accepts or refuses every command. Commands that need a position or a target are not offered as hints; the screen says they are previewed on the battlefield and sent from it in stage 4c (`NotYetOffered`).
- `EventText`: a line of text and the dice for every event. Its event text is a copy of the simulator's narrator (`Sim.Narrator`), because the client must not reference `Sim`, which holds a dice source; a test runs 80 simulated matches and checks the two give the same text for every event. `ContentNames` (a copy of `Sim.Content.Parse`, also checked by a test) names models from the warband files built into the library, and falls back to model ids, with a note on screen, when the files do not match the server's match.
- `RulesGaps`: the rules this build does not yet implement (design §10), shown to playtesters.
- `BoardScene` (M7.4): the battlefield in inches for the 3D view. `Interaction` and `Previews` (M7.4 stage 4b): the battlefield choice in progress and what the server would probably say about it, from read-only rules geometry; see "Previews" above.

### Out-of-date commands (Stale)

The server refuses a command sent against an older state of the match (`Stale`). The missing events are already on their way on the same connection, so the client waits for them (five seconds at most, `StaleWait`), without polling and without reconnecting, then says "Nothing was done; choose again" and shows the current actions. Repeated refusals are counted ("3 times in a row") and each waits for its own events, so there is no busy loop. If the events do not arrive in time, the screen says so and offers **Resync**, which only the player can choose: it closes the connection and takes the seat back with the copy it already holds.

## The match screen (M7.3)

The Godot client is two screens built in code (`client/scripts/Main.cs`).
- **Connect:** the server address; the server's rejoin window in seconds (default 300, see below), which sets how long automatic reconnecting lasts; create a match for a scenario, join one by its code, or rejoin a seat saved on this computer.
- **Match:** the connection line, with **Rejoin now** when automatic attempts have stopped, **Take the seat back…** (with a confirmation) after another window took the seat, and **Resync** after an unresolved Stale refusal; the match code with **Copy code**; MATCH (turn, phase, whose move, both seats with VP, the pending decision, the outcome); MODELS; YOUR ACTIONS (hint buttons, marked "the server decides", or why there is nothing to do); LAST COMMAND (sending, done, refused by the rules with the rule reference, not taken by the server, out of date); and EVENTS, numbered, with the dice for every roll.
- **Rules in this build** opens on the first match and with F1.
- **Keyboard focus:** the connect screen starts in the server field, the match screen on the first action (or the connection buttons). Buttons are rebuilt whenever the match changes, and the focus stays on the same button (or moves to the first action if it is gone), so it is never lost. The event list takes focus too: arrows, Page Up/Down, Home and End scroll it; it follows new events unless the player has scrolled up, and End follows them again.
- **Readable without colour:** seats are told apart by letters ([A], [B]) and words, never by colour alone. Text size is 100, 125 or 150 %, and there is a high-contrast switch. Every control works with the keyboard (Tab, Shift+Tab, Enter or Space; Esc closes a dialog), and the hints line says so.
- The scripts only render the controller's `ScreenState` and pass on button presses. They never resolve commands, roll dice, apply events or read match records; `ClientAuthorityTest` checks the compiled scripts.

## Reconnecting

- **One rejoin at a time per seat.** The automatic run (`Reconnector.RunAsync`) and "Rejoin now" (`Reconnector.RejoinNowAsync`) on the same dropped seat join one run and get the same result: there is only ever one new connection, so a client never takes its own seat from itself. "Rejoin now" during a wait between attempts makes the next attempt at once. Once a session has rejoined, the sessions before it are superseded and cannot start another rejoin. `Reconnector.IsReconnecting(mirror)` says whether a run is in progress.
- **Every attempt has a deadline.** Connecting, the WebSocket handshake and the wait for the rejoin answer all stop at `AttemptTimeout` (20 seconds by default) or when `GiveUpAfter` is reached, whichever is sooner. A host that accepts the connection but never answers cannot hold the run open. A connection from an attempt that timed out, failed or was cancelled is closed.
- **Cancelling** a run returns promptly and leaves no attempt or connection open.

### How long automatic attempts last (`ReconnectPolicy.GiveUpAfter`)

- The default is **five minutes**, the server's default `--rejoin-window` (300 seconds).
- The client **cannot find out** the window a server was started with: the protocol tells only the opponent when a dropped seat must be back (`MatchPaused.RejoinBy`), and `Welcome` does not carry it. So when a server runs with a longer `--rejoin-window`, set the client to match, for example `ReconnectPolicy.ForRejoinWindow(TimeSpan.FromMinutes(15))` for `--rejoin-window 900`. Otherwise the client stops trying while the server still holds the seat.
- A shorter server window is harmless: a late rejoin is refused (`MatchAborted` or `UnknownMatch`) and reconnecting stops.
- When automatic attempts stop (`GaveUp`), the match may still be waiting on the server. The client should offer **Rejoin now**, which makes one attempt (at most `AttemptTimeout`) and never runs alongside another. "Rejoin now" also takes the seat back after a takeover or a policy close, because the player chose to, and after a failed copy, which it rebuilds from an empty copy. It never rejoins a finished or aborted match.

### Known limitation: a takeover notice that never arrives

When the player opens a newer window and it takes the seat with the saved token, the server tells the older connection (`Rejected SeatTakenOver`, then a policy close), and the older window then stops for good. If the older connection has already dropped, it never hears this and believes it merely dropped:
- Usually that is harmless. Once the newer window has saved and confirmed its new token, the old token no longer opens the seat, so the older window's rejoin is refused (`InvalidToken`) and it stops. The newer window keeps the seat.
- But if the older window's rejoin reaches the server **before the newer window's token is confirmed**, or the newer window **could not save** its token, the old token still works and the older window takes the seat back. The newer window is then told it was taken over and stops; the two never take the seat back and forth.

Fixing that would need the server to tell a rejoining client that its seat was taken over in the meantime, which is a protocol change and is not part of M7. Both cases are covered by tests.

"Rejoin now" after a takeover uses the newest token saved on this computer, so the player can take the seat back from the newer window's token.

### Known limitation: intermittent M5 network tests

Some M5 server network tests (`tests/Server.Tests`) fail now and then on timing. They failed the same way before M7 began; fixing them is a separate, test-only change. The M7.3 client tests do not depend on them.

## What you need

- The **.NET** build of Godot, version in `client/GODOT_VERSION` (4.7.2). The standard build has no C# and cannot open this project: it reports a missing dependency on the C# script.
- The .NET 10 SDK.

## Build and run

1. Optional check without the editor: in `client/`, run `dotnet build`. It should end with `0 Error(s)`.
2. Open the Godot .NET editor, choose **Import**, and pick `client/project.godot` (or the `client` folder).
3. Press **Build** (the hammer, top right), then **Play** (F5).

Godot keeps its cache in `client/.godot/` and may create `client/Client.sln`; both are ignored by git.

## Tests

`dotnet test TrenchSim.sln` runs everything that does not need Godot. In `tests/Client.Tests`:
- `ClientProjectTest`: the Godot client is outside the solution, its SDK matches `GODOT_VERSION`, and it may reference only `Client.Core`, `Match.Client` and `Match.Protocol` (never `Match.Server`, `Sim` or `Match.Cli`).
- `ClientAuthorityTest`: `Match.Client` uses the plain SDK and references only `Match.Protocol`; its compiled code references no resolver (`TurnSequence.Decide`), dice source, `Reducer` or record verifier. The Godot client is checked the same way on its **compiled** scripts: the test builds `client/Client.csproj` with the Godot .NET SDK from NuGet (no Godot install needed) into a temporary folder and scans `Client.dll`. Its assembly references must be on an allow-list (never `Match.Server`, `Sim` or `Match.Cli`), and it must not touch the copy of the match directly (`MatchMirror.Apply`/`Reset`, `Codec.ParseServer`) or load code by reflection. If the client cannot be built, the test fails. The scan is itself checked against the server and `Match.Client`, which do use these. A text scan of the Godot sources remains as a quick extra check.
- `ClientAuthorityTest` also checks `Client.Core`: plain SDK, references only `Match.Client`, and its compiled code uses no resolver, dice source, `Reducer`, record verifier or direct change to the copy of the match, and does use the read-only `TurnSequence.LegalCommands` and `Reconnector`. The compiled Godot scripts must use `Client.Core.MatchController`.
- `PreviewAgreementTests` (M7.4 stage 4b): previews agree with the server's own decisions over simulated matches (the only place `TurnSequence.Decide` meets a preview; the client never calls it). `InteractionTests`: each interaction state, cursor steps, waypoints, target cycling, clearing when the match moves on, sight rays and inches in the facts, the hover throttle. `BoardSceneTests` (4a): the battlefield description agrees with the rules geometry.
- `ClientCoreTests`: event text and content parsing agree with `Sim` over 80 simulated matches; every event has a line and every roll its dice; names and the fallback; hints equal the legal commands for the seat; the view agrees with the state; outcome wording; the rules gaps.
- `ControllerTests` (part of `TransportTests`): two controllers play a whole match against the server; one command at a time; protocol refusals; Stale refusals wait for the events and never reconnect on their own, and Resync recovers; a drop shows Reconnecting and the opponent sees the pause; a rejoin while the opponent is away shows the pause; "Rejoin now" after automatic attempts stop; taking a seat back after a takeover needs confirmation; an aborted match is shown and its saved seats are forgotten.
- `TransportTests`: real matches over WebSockets against an in-process server: connection on v2, command numbering, duplicate resends, saved and rotated tokens, reconnecting after a drop, and the cases where it must not reconnect. Reconnect cases also use a fake host that never answers the handshake, never answers the rejoin, or answers busy or shutting down: one run per seat, deadlines, cancellation, "Rejoin now", and the takeover limitation above.

## Automated UI check (`tools/UiCheck`)

One command checks the client the way a player would use it, with real processes and real key presses:

```
dotnet run --project tools/UiCheck -- --godot <path to the Godot 4.7.2 .NET executable>
```

It builds `client/` (Debug and Release, warnings as errors), builds the server and `trench-cli`, has Godot build and launch the project, then plays a scripted session: a server, a proxy in front of it that can hold or cut traffic, two Godot clients (`--ui-check host|guest|reopen`, driven by `client/scripts/UiCheck.cs` through key presses only) and `trench-cli` bots. It covers creating and joining, the rules dialog (Esc, F1, Enter, Space), Tab and Shift+Tab order, hint buttons (SENT then DONE, one at a time), a seat taken over by `trench-cli` and taken back, events arriving live and after a rejoin, both clients showing the same models, events, seats and VP, the same event text as `trench-cli`, a cut connection (Reconnecting, then Rejoin now), reopening a saved seat after closing the client, a server restart, text at 150 % and high contrast, and (M7.4) the battlefield: every model on the table drawn and labelled, the table in view after fit in the angled, turned and top-down cameras, screen points mapping back to the same table point within 0.05", and selection by N, P and a mouse click; then (stage 4b) previews: placing a model inside and outside the zone with the rule's reason, half-inch and 2" cursor steps, a move with a waypoint and inch labels, a path by mouse hover and click, a shot with the rules' sight rays and distance, target cycling by N and by click, a charge preview, and that none of this sends a command. Results, logs and screenshots go to `ui-check-results/<time>/` (`summary.md` lists every check as PASS or FAIL); the exit code is 0 only when all pass. Saved seats live in that folder too (`TRENCH_SIM_HOME`), never in the player's own. On Linux without a display run it under `xvfb-run -a`, and add `--opengl` where there is no Vulkan.

The UI-check code only runs with `--ui-check`; it reads the screen (labels, buttons, dialogs, focus) and never the match controller.

What it found and what changed in M7.3:
- Nothing had keyboard focus when the client started; buttons rebuilt on each change dropped the focus.
- After a rejoin while the opponent was away, the screen could miss the pause notice (the server sends it right after the Welcome, before the controller switched to the new connection). Fixed in `MatchController`; `ARejoinWhileTheOpponentIsAwayShowsThePause` covers it.
- At 150 % text, long section titles and hint buttons widened the page beyond the window; they now wrap.
- The event list could not be scrolled with the keyboard.

A server restart does not keep matches: the server records an unfinished match as Unrecorded when it starts again, and a client that rejoins is told the match is gone (`UnknownMatch`); the screen says so and offers nothing to send.

`ERROR: Condition "status < 0" is true. Returning: ERR_CANT_OPEN` (at `init_output_device`, `drivers/alsa/audio_driver_alsa.cpp`) appears when the machine has no audio output, as in a Linux container. Godot then says "All audio drivers failed, falling back to the dummy driver" and runs without sound; the client plays no sound, so nothing is lost. `--audio-driver Dummy` avoids the message.

### Unattended run on Windows (`Run-WindowsUiCheck.ps1`)

From the repository folder, in Windows PowerShell 5.1 or PowerShell 7:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\UiCheck\Run-WindowsUiCheck.ps1 -GodotPath "C:\...\Godot_v4.7.2-stable_mono_win64_console.exe"
```

The script never prompts. It:
- finds Godot from `-GodotPath`, then `$env:GODOT`, then the exact file name `Godot_v4.7.2-stable_mono_win64_console.exe` (up to 4 folders deep) in Downloads, Desktop, Documents, `%LOCALAPPDATA%`, Program Files, `C:\Godot` and `C:\Tools`. A candidate counts only if `--version` reports `4.7.2.stable.mono.`; if different builds are found it stops rather than choosing;
- runs the UI check above (limit `-TimeoutMinutes`, default 30), then opens the project in the real Godot editor with `--editor --quit` (limit `-EditorTimeoutSeconds`, default 300) and fails on any engine error. The editor reformats `client/project.godot` when it opens it; the script restores the original file;
- keeps the PC awake while it runs, and stops only the processes it started (found by parent process and start time, or by the run folder in their command line), on a time limit, an error, or at the end. Godot windows it did not start are left alone;
- writes `ui-check-results\windows-<time>\`: `summary.md` (the verdict, PASS / FAIL / NOT TESTED, start and end time, commands and exit codes), `console.log` (the runner's complete output), `run.log`, the editor's output and `detail\` (the runner's own `summary.md`, client logs and screenshots, unchanged).

Exit codes: 0 every automated check passed; 1 a check failed; 2 incomplete (a time limit, or a check in `expected-checks.txt` did not report); 3 it could not start (no desktop session, Godot not found or not 4.7.2 .NET, no .NET 10 SDK). Checks that did not run are listed as NOT TESTED, never as PASS.

It needs a signed-in Windows desktop session. Godot opens real windows, so the script refuses to run from a service, or a scheduled task set to run whether the user is logged on or not (FAIL `preflight-desktop`). The key presses are sent inside Godot, so the windows need no focus and nobody needs to click anything. A locked screen is recorded in `summary.md`; turn off the screen lock (or keep the PC awake and unlocked) for the run, since some graphics drivers stop drawing to locked sessions.

What stays manual, and why it is not automated:
- **Editor Build and Play buttons.** The editor draws its own interface, so Windows UI Automation sees one window with no buttons in it; only clicks at screen positions could press them, which break with DPI, layout and editor version. The same work is checked from the command line (`godot-build-solutions`, `godot-launch-scene`, `editor-opens-project`).
- **Mouse.** Same reason, and real mouse input (SendInput) also needs the window in front on an unlocked screen. Every automated check uses the keyboard.
- **Copy code to the clipboard.** Reading the clipboard is easy; pressing Copy code needs a click or a new check inside the client.
- **Closing a window with its title-bar X.** Posting `WM_CLOSE` to the right window at the right moment needs the runner's phases to coordinate with it; the check closes the client from inside instead.
- **150 % text and high contrast, judged by eye.** Size, width and colours are checked automatically; whether it reads well is a judgement. Compare the screenshots in `detail\e2e\`.

None of these needs remote access: they are a few minutes in front of the PC (see the M7.3 checklist).

## Headless check

With a server running, `godot --headless --path client --build-solutions --quit`, then
`godot --headless --path client -- --smoke-server ws://127.0.0.1:5080/match` creates a match through `Match.Client` and prints `SMOKE OK: Welcome seat=One …`.

`godot --path client -- --smoke-view ws://127.0.0.1:5080/match --screenshot screen.png` opens the match screen on a new match (an in-process opponent joins it), prints `SMOKE VIEW OK: …` and saves a screenshot. It needs a display; on Linux without one, run it under `xvfb-run` with `--rendering-driver opengl3`.

## Compatibility gate (M7.1, passed 2026-10-15)

On Windows 11 (10.0.26200, x64) with Godot `4.7.2.stable.mono.official.ed1daf0bf` and .NET 10.0.13, a gate project built as `net10.0`, ran our rules code with results identical to the reference run (same state hashes), and received a `Welcome` from `trench-server` over `trench-sim.v2`.
