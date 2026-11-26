# M7.3 automated UI check

2026-11-26 19:52 UTC. Logs and screenshots are in this folder (logs/, e2e/).

| Result | Check | Detail |
|---|---|---|
| INFO | environment | Microsoft Windows 10.0.26200; .NET 10.0.13; repository <repo>; Godot <appdata>\TrenchSim\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe |
| PASS | godot-version | exit 0: 4.7.2.stable.mono.official.ed1daf0bf |
| PASS | client-build-Debug | dotnet build client/Client.csproj -c Debug -warnaserror: exit 0, 0 Warning(s), 0 Error(s) |
| PASS | client-build-Release | dotnet build client/Client.csproj -c Release -warnaserror: exit 0, 0 Warning(s), 0 Error(s) |
| PASS | build-Match.Server | exit 0, 0 Warning(s), 0 Error(s) |
| PASS | build-Match.Cli | exit 0, 0 Warning(s), 0 Error(s) |
| PASS | godot-build-solutions | godot --headless --build-solutions --quit: exit 0; no errors in the output |
| PASS | godot-launch-scene | godot --path client --quit-after 120 (main scene, real window, 120 frames): exit 0; no errors in the output |
| INFO | server-start-time | 0.9 s from starting the server to "Listening on" |
| PASS | guest-closes-cleanly | second Godot client exit code 0 |
| PASS | cli-played | trench-cli (both seats, bots) deployed 8 models and showed 35 events |
| PASS | both-clients-same-models | host: [A] Sharpshooter (a1): Standing, activated this turn\|[A] Skirmisher (a2): Standing\|[A] Bulwark (a3): Standing\|[A] Banner Carrier (a4): Standing\|[B] Marksman (b1): Standing\|[B] Runner (b2): Standing, activated this turn\|[B] Shieldbearer (b3): Standing\|[B] Lamp Keeper (b4): Out of action \|\| reopened: [A] Sharpshooter (a1): Standing, activated this turn\|[A] Skirmisher (a2): Standing\|[A] Bulwark (a3): Standing\|[A] Banner Carrier (a4): Standing\|[B] Marksman (b1): Standing\|[B] Runner (b2): Standing, activated this turn\|[B] Shieldbearer (b3): Standing\|[B] Lamp Keeper (b4): Out of action |
| PASS | both-clients-same-events | 65 event lines on the host, 65 on the reopened client |
| PASS | both-clients-same-seats-vp-turn | host: Turn 2 of 4. Phase: Activation.\|Seat One [A]: Ash Company. VP 0, zone west.\|Seat Two [B]: Lantern Guard. VP 0, zone east. \|\| reopened: Turn 2 of 4. Phase: Activation.\|Seat One [A]: Ash Company. VP 0, zone west.\|Seat Two [B]: Lantern Guard. VP 0, zone east. |
| PASS | letters-match-seats | Match code L8H7WXKB. You are Seat One [A]. \|\| Match code L8H7WXKB. You are Seat Two [B]. |
| PASS | whose-move-agrees | host: Activation. Your move. \|\| reopened: Activation. Opponent's move (Seat One). |
| PASS | godot-shows-cli-events | all 35 events trench-cli printed are in Godot with the same number and text |
| INFO | server-restart-recovery | Recovered from an earlier stop: 1 match(es) recorded as Unrecorded, 0 bad record(s) set aside, 0 temporary file(s) removed. Listening on ws://127.0.0.1:62134/match (protocol trench-sim.v2). Ctrl+C stops. |
| PASS | host-exit | host Godot exit code 0 (0 = all its checks passed) |
| PASS | reopen-exit | reopened Godot exit code 0 |
| PASS | both-clients-same-final-hash | host: 7cb69b7ef3977cf6215427e1e1f0612d6b3539c2674ee97c355b219310f70985 \|\| reopened: 7cb69b7ef3977cf6215427e1e1f0612d6b3539c2674ee97c355b219310f70985 |
| PASS | host: focus-start | focus at start: LineEdit "ws://127.0.0.1:5080/match" |
| PASS | host: tab-reaches-every-control-connect | 16 stops: LineEdit "ws://127.0.0.1:5080/match" > SpinBox > OptionButton "ranged (normal roster)" > Button "Create match" > OptionButton "The scenario's own map" > Button "Hot-seat: play both sides in this window" > LineEdit "match code" > Button "Join match" > Button "Refresh saved seats" > OptionButton "Text 100%" > CheckButton "High contrast" > OptionButton "Scale 100%" > OptionButton "Dice: Normal (4 s)" > OptionButton "Animations: Full" > CheckButton "Sound" > Button "Back" |
| PASS | host: shift-tab-reverses-connect | Button "Back" < CheckButton "Sound" < OptionButton "Animations: Full" < OptionButton "Dice: Normal (4 s)" < OptionButton "Scale 100%" < CheckButton "High contrast" < OptionButton "Text 100%" < Button "Refresh saved seats" < Button "Join match" < LineEdit "match code" < Button "Hot-seat: play both sides in this window" < OptionButton "The scenario's own map" < Button "Create match" < OptionButton "ranged (normal roster)" < SpinBox < LineEdit "ws://127.0.0.1:5080/match" |
| PASS | host: type-server | LineEdit "ws://127.0.0.1:62135/match" = "ws://127.0.0.1:62135/match" |
| PASS | host: set-window | SpinBox = "10" |
| PASS | host: set-window-value | rejoin window spin box = 10 |
| PASS | host: choose-scenario | OptionButton "ranged (normal roster)" selected "ranged" with Enter, Down, Enter |
| PASS | host: create-enter | Tab reached Button "Create match", then Enter |
| PASS | host: create | Match code L8H7WXKB. You are Seat One [A]. Give the code to your opponent. |
| PASS | host: gaps-shown-at-first-match | visible=True |
| PASS | host: gaps-lists-rules | 9 lines |
| PASS | host: gaps-escape-closes | visible=False |
| PASS | host: gaps-f1-opens | visible=True |
| PASS | host: gaps-enter-closes | visible=False (Enter on the OK button) |
| PASS | host: hud-menu-folded | menu open False; code line shown False; HUD: [A] SEAT ONE · Waiting for the server to start the match · Turn limit 4 · before the game · VP A 0 – B 0 · objectives now: ◆1 – ◆2 – ◆3 – ◆4 – ◆5 – |
| PASS | host: rules-button-space | Tab reached Button "Rules in this build (F1)", then Space |
| PASS | host: gaps-button-space-opens | visible=True |
| PASS | host: gaps-escape-closes-again | visible=False |
| PASS | host: hud-menu-opens-and-closes | opened with everything shown: True; closed again: True |
| PASS | host: tab-reaches-every-control-match | 11 stops: IconButton "" > Button "Leave" > Button "▸ No unit selected" > Battlefield > SignalIcon "" > IconButton "" > IconButton "" > IconButton "" > IconButton "" > IconButton "" > IconButton "" |
| PASS | host: shift-tab-reverses-match | IconButton "" < IconButton "" < IconButton "" < IconButton "" < IconButton "" < IconButton "" < SignalIcon "" < Battlefield < Button "▸ No unit selected" < Button "Leave" < IconButton "" |
| PASS | host: decision-card | card (410, 296), (460, 128) (needs (454, 128); VBoxContainer (436, 104) min (430, 104), Label (436, 26) min (430, 26), VBoxContainer (436, 72) min (284, 72), Button (436, 34) min (284, 34), Button (436, 34) min (280, 34)) on the battlefield (0, 0), (1280, 800): True; title "YOUR CHOICE: which deployment zone you take"; choices Take the west deployment zone / Take the east deployment zone; the Menu lists Take the west deployment zone / Take the east deployment zone; corner icons (996, 6), (276, 36) |
| PASS | host: hint-enter | Tab reached Button "Take the west deployment zone", then Enter |
| PASS | host: hint-sent-shown | SENT: Sent: Take the west deployment zone. Waiting for the server… |
| PASS | host: one-command-at-a-time | Nothing to send: waiting for the server's answer to the last command. |
| PASS | host: hint-done-shown | DONE: Done: Take the west deployment zone. |
| PASS | host: zone-event-shown | [7] Two deploys first. |
| PASS | host: positional-not-offered-explained | Nothing to do until the opponent moves. |
| PASS | host: takeover-shown | Connection: This seat was opened from another window. This window will not take it back on its own. |
| PASS | host: takeback-enter | Tab reached Button "Take the seat back…", then Enter |
| PASS | host: takeback-confirm-shown | This seat was opened from another window. Taking it back closes it there. Take it back? |
| PASS | host: takeback-confirm-focus | focus in the dialog: Button "OK" |
| PASS | host: takeback | Connection: Connected. |
| PASS | host: catch-up-deploys | 8 deployment events after taking the seat back |
| PASS | host: catch-up-models | [A] Sharpshooter (a1): Standing, activated this turn\|[A] Skirmisher (a2): Standing, activated this turn\|[A] Bulwark (a3): Standing\|[A] Banner Carrier (a4): Standing, activated this turn\|[B] Marksman (b1): Standing, activated this turn\|[B] Runner (b2): Standing, activated this turn\|[B] Shieldbearer (b3): Standing, activated this turn\|[B] Lamp Keeper (b4): Standing, activated this turn |
| PASS | host: opponent-away-shown-after-rejoin | Connection: Connected.\|Your opponent disconnected. The match is paused until they rejoin, at the latest 19:46:38 UTC (server time). If they do not, the match is aborted with no result. |
| PASS | host: board-shown | 8 models (8 on the table in MODELS), 9 terrain pieces, 2 zones; Table 36.0" x 36.0"; camera angled, facing north. |
| PASS | host: board-models-labelled | A1 ✓ \| A2 ✓ \| A3 \| A4 ✓ \| B1 ✓ \| B2 ✓ \| B3 ✓ \| B4 ✓ |
| PASS | host: board-labels-short | 16 labels: ZONE B / ZONE A / ◆1 / ◆2 / ◆3 / ◆4 / ◆5 / A1 ✓ / A2 ✓ / A3 / A4 ✓ / B1 ✓ / B2 ✓ / B3 ✓ / B4 ✓ / DASH FAILED |
| PASS | host: board-labels-no-overlap | 16 id, zone, objective and note labels apart |
| PASS | host: layout-battlefield-fills-window | window (1280, 800); row starts 0px down the page, 800px tall (wanted 800); battlefield (0, 0), (1280, 800); unit card (8, 758), (176, 34) (on the battlefield: True) |
| PASS | host: log-drawer | closed at first: True; the icon opened it along the right edge (832, 184), (440, 608) of the battlefield (0, 0), (1280, 800) with the newest row in view: True; the icon closed it: True; Esc closed it: True; nothing sent: True |
| PASS | host: corner-icons | CentreButton "Centre (Home)", FitButton "Fit (F)", ResetView "Reset view (Shift+F)", ScenarioButton "Scenario (G)", KeysButton "Keys (?)", LogButton "Combat log", MenuButton "Menu"; inside the top-right corner; Menu, log, keys and scenario open and close; nothing sent |
| PASS | host: board-see-through-outlines | outlined wall-1 (faded wall-1); not worked out again while still: True (79 -> 81); after a zoom faded = hiding and outlined: True; none outlined top-down: True |
| PASS | host: board-see-through-walls | camera behind wall-1 from a1, facing angled, facing north; hiding wall-1; drawn obj-1 1.0, obj-2 1.0, obj-3 1.0, obj-4 1.0, obj-5 1.0, wall-1 0.3, wall-2 1.0; top-down: obj-1 1.0, obj-2 1.0, obj-3 1.0, obj-4 1.0, obj-5 1.0, wall-1 1.0, wall-2 1.0; camera back to angled, facing north |
| PASS | host: board-tab-focus | focus on Battlefield |
| PASS | host: board-fit-angled | camera angled, facing north |
| PASS | host: board-pick-roundtrip | 8 models; largest error 0.000" |
| PASS | host: board-zoom-pan-fit-keys | zoomed in: True; panned left: True; F fits again: True |
| PASS | host: board-turn-keys | E turned to 90 and kept the table in view: True; Q back to 0 |
| PASS | host: board-top-down | camera top-down, facing north |
| PASS | host: board-pick-roundtrip-top-down | 8 models; largest error 0.000" |
| PASS | host: unit-tab | closed at first: True; tab "▾ A1 · Sharpshooter · Standing"; 34px closed, 350px open |
| PASS | host: unit-card-collapsed | closed: only the tab "▾ A1 · Sharpshooter · Standing" at (8, 758), (280, 34) (open card (8, 441.68002), (380, 350.31998)); no box, words, buttons or keys; it opens again |
| PASS | host: art-fallback | 8 models; real catalog: 8 from art, 0 plain, ground from art True; missing files: 0 from art, 8 plain, ground from art False, noted: ground/missing-for-ui-check.png: file not found / units/missing-for-ui-check.glb: file not found; put back: (8, 0, True) |
| PASS | host: battlefield-readable | 6 models sampled; seats 0.11 apart, hue 151 degrees, least saturation 0.12; fog: False; shadows: False; a1 body 716f6d ground 3f3c34 ratio 2.21 distance 0.36 rim 595d67 \| a4 body 807a6e ground 3f3b32 ratio 2.61 distance 0.43 rim 4b4e54 \| b1 body 827b6e ground 4a4b4a ratio 2.09 distance 0.32 rim 5b4945 \| b2 body 7c7165 ground 4a4b49 ratio 1.84 distance 0.27 rim 5c4743 \| b3 body 251a18 ground 4a4b49 ratio 1.93 distance 0.31 rim 5e4b48 \| b4 body 827a6d ground 383431 ratio 2.91 distance 0.46 rim 5e4b48 |
| PASS | host: unit-figures | 8 models, 8 figures, 0 plain; each inside its base, at its height, accent = body colour, muted sandstone base top with a muted body-colour rim, outlined and in the unit light, base at the rules' radius; catalog 38 entries; art problems:  |
| PASS | host: ground-decor | 11 decals drawn of 11 for this layout; 9 terrain pieces, 9 textured; ground textured True; all flat and clear of terrain |
| PASS | host: terrain-models | 9 pieces; each drawn with its models, inside its footprint and under its height |
| PASS | host: ui-theme | heading font Pirata One; 23 section titles in it at 23px, gold, fitting; iron and parchment buttons with Godot's margins; all as wanted |
| PASS | host: board-select-key | N selected Sharpshooter (a1); A1 · Sharpshooter |
| PASS | host: board-previous-key | P selected Lamp Keeper (b4) |
| PASS | host: board-click-selects | clicked at (793.9224, 350.9828) for Marksman (b1); selected Marksman (b1) |
| PASS | host: panel-unit | panel: B1 · Marksman\|Seat Two [B] · Standing\|✓ Activated this turn\|Move 6.0" · Range 20.0" · Armour 0 · No melee weapon\|— Opponent's unit: it takes no orders from you.\|The match moved on, so the preview was cleared. |
| PASS | host: board-home-centres | selected Marksman (b1) at (28.8, 13.8); camera looks at (28.8, 13.8) |
| PASS | host: board-wheel-zooms-to-pointer | zoomed in: True; table point under the pointer (8.082076072692871, 2.8655738830566406) -> (8.082076072692871, 2.8655738830566406) |
| PASS | host: board-shaky-right-click-no-pan | camera (18, 47.334732, 51.14414) -> (18, 47.334732, 51.14414) |
| PASS | host: board-activate-key | N selected Bulwark (a3); A: DONE: Done: Activate a3. |
| PASS | host: board-move-preview | from [A] Bulwark (a3): Standing, ACTIVE, at (1.4", 21.4"), on the ground, base 1.3".; Move: looks legal (preview; the server decides); path (1.4", 21.4") 0.0" -> (2.4", 21.4") 1.0" -> (2.4", 22.4") 2.0"; labels: 1.0" |
| PASS | host: board-move-sight-icons | accepted path; 4 enemies, 4 icons: b1 partly seen, b2 partly seen, b3 partly seen, b4 partly seen |
| PASS | host: board-preview-card-move | card: MOVE LOOKS LEGAL (preview; the server decides) \| 2.0" used of 5.0" (3.0" left) \| straight line 1.4"; panel: A3 · Bulwark\|Seat One [A] (yours) · Standing\|● Activating now\|Move 5.0" · Range 10.0" · Armour -1 · No melee weapon\|MOVE → (2.4", 22.4")\|PREVIEW · the server decides\|2.0" used of 5.0" (3.0" left)\|straight line 1.4"; ghost at (2.4", 22.4"); real model unmoved: True |
| PASS | host: panel-details-keeps-preview | details button: Details ▸; opened with every detail shown: True; closed: True; before (a3, Move, MoveModel { Seat = One, Model = a3, Path = System.Collections.Immutable.ImmutableArray`1[Rules.Core.Geometry.Point2] }, DONE: Done: Activate a3., 42); after (a3, Move, MoveModel { Seat = One, Model = a3, Path = System.Collections.Immutable.ImmutableArray`1[Rules.Core.Geometry.Point2] }, DONE: Done: Activate a3., 42) |
| PASS | host: board-mouse-path | Backspace removed the waypoint: True; hover moved the cursor to (2.9", 20.4") (wanted (2.9, 20.4)): True; Shift+click fixed a waypoint and sent nothing: True |
| PASS | host: board-preview-cancel-clears | ghost before Esc: True, after: ; labels: 0; mode None; last command unchanged: True |
| PASS | host: board-hover-inspect | pointing at Marksman (b1): True; panel: POINTING AT B1 · Marksman\|Seat Two [B] · Standing\|27.3" from A3 (base to base); ring: True; id size 36 (selected 36); selection kept: True; nothing sent: True; battlefield texts 0 -> 0 |
| PASS | host: board-double-click-centres | Bulwark (a3): centred at the same zoom (45.8) and selected: True; again: zoom 27.5, closer: True; objective obj-1 centred: True; nothing sent: True |
| PASS | host: board-objective-select | O: obj-1 centred at the same zoom: True; panel: Controlled now: nobody (if scored now) \| 2 VP to its controller at the end of each turn \| Not scored yet (wanted 'Controlled now: nobody (if scored now)'); O again: obj-2; nothing sent: True |
| PASS | host: scenario-card | 29 lines, 5 objective rows, turn 1;  |
| PASS | host: scenario-row-centres | card closed: True; selected obj-5 (wanted obj-5); centred at the same zoom: True; nothing sent: True |
| PASS | host: combat-no-auto-target | after S: mode Shoot, target none, preview none, sent nothing True, confirm box hidden; after C: mode Combat, target none, preview none, sent nothing True, menu (350.95816, 313.23685), (252, 283) |
| PASS | host: board-shoot-preview | Shoot: refused. The target is not in the attacker's Line of Sight. (1.0.2 p. 30; preview; the server decides); 9 rays, 0 clear; labels: Hidden |
| PASS | host: board-target-line | 1 line(s); TargetLine { Attacker = a3, Target = b4, From = (2.0", 21.5"), To = (27.3", 26.8"), Tone = Refused, LongRange = False } colour (1, 0.3, 0.25, 1), tag False; wanted a3 -> b4 (Refused, long range False), base edges 0.63" and 0.492" of 26.98" apart |
| PASS | host: board-shot-guide | guide for b4 (target b4), caption Hidden (rules: Hidden) at (829.6886, 461.74115), (50.4388, 20.587265), eye at (830.2033, 409.24362), (49.409435, 49.409435), outline none (cover none); Guide: sight points seen, head 0 of 3, body 0 of 3, legs 0 of 3 |
| PASS | host: board-preview-card-target | card: SHOOT REFUSED (preview; the server decides) \| target [B] Lamp Keeper (b4), activated \| Rules (the server decides): \| Distance 25.9", weapon range 10.0" \| Line of sight: not visible, 0 of 9 sight rays clear, blocked by wall-2 \| Target not in cover \| Guide: sight points seen, head 0 of 3, body 0 of 3, legs 0 of 3 \| The target is not in the attacker's Line of Sight. (1.0.2 p. 30); panel: A3 · Bulwark\|Seat One [A] (yours) · Standing\|● Activating now\|Move 5.0" · Range 10.0" · Armour -1 · No melee weapon\|SHOOT → B4 · Lamp Keeper\|PREVIEW · the server decides\|Rules (the server decides):\|Distance 25.9", weapon range 10.0"\|Line of sight: not visible, 0 of 9 sight rays clear, blocked by wall-2\|Target not in cover\|Guide: sight points seen, head 0 of 3, body 0 of 3, legs 0 of 3\|✕ not valid: B1 (out of range), B3 (out of range), B2 (out of range)\|REFUSED: The target is not in the attacker's Line of Sight. |
| PASS | host: panel-confirm-cancel-fixed | Confirm at (20, 655.9), (170, 34) for Move, (20, 655.9), (170, 34) for Shoot; Cancel at (291, 655.9), (85, 34) |
| PASS | host: board-hover-target-preview | pointing at Lamp Keeper (b4): True; panel: POINTING AT B4 · Lamp Keeper\|Rules (the server decides):\|Distance 25.9", weapon range 10.0"\|Line of sight: not visible, 0 of 9 sight rays clear, blocked by wall-2\|Target not in cover\|Guide: sight points seen, head 0 of 3, body 0 of 3, legs 0 of 3\|✕ The target is not in the attacker's Line of Sight.; expected facts: Distance 25.9", weapon range 10.0" \| Line of sight: not visible, 0 of 9 sight rays clear, blocked by wall-2 \| Target not in cover; ✕ The target is not in the attacker's Line of Sight.; target kept: True; nothing sent: True |
| PASS | host: board-target-cycle-and-click | 4 enemy targets; N went to the next: True; a click picked Lamp Keeper (b4): True |
| PASS | host: board-double-click-target-no-send | double-click on the chosen target Lamp Keeper (b4) (preview legal: False): target kept: True; nothing sent: True; still choosing: True |
| PASS | host: board-camera-keys-change-nothing | after arrows, + -, Q E, Home, F and the wheel: mode Shoot (was Shoot), target b4 (was b4), selected a3 (was a3), preview version 30 (was 30); nothing sent: True |
| PASS | host: board-reset-view | turned and top-down first: True; after Shift+F: yaw 0, top-down False, whole table in view True; action kept: True |
| PASS | host: charge-warns-no-melee-weapon | a3: no Melee Weapon True; the panel says it cannot Fight: True; panel: A3 · Bulwark\|Seat One [A] (yours) · Standing\|● Activating now\|Move 5.0" · Range 10.0" · Armour -1 · No melee weapon\|CHARGE → B4 · Lamp Keeper\|PREVIEW · the server decides\|Rules (the server decides):\|Distance 25.9"; a charge target must be within 12"\|Line of sight: not visible, 0 of 9 sight rays clear, blocked by wall-2\|Target not in cover\|It can charge but cannot Fight: it has no Melee Weapon (1.0.2 pp. 45-46, 49)\|✕ not valid: B1 (out of range), B3 (out of range), B2 (out of range)\|REFUSED: The target of a charge must be visible to the charging model. |
| PASS | host: board-charge-preview | Charge: refused. The target of a charge must be visible to the charging model. (1.0.2 pp. 30, 36; preview; the server decides); Distance 25.9"; a charge target must be within 12". Line of sight: not visible, 0 of 9 sight rays clear, blocked by wall-2. Target not in cover. It can charge but cannot Fight: it has no Melee Weapon (1.0.2 pp. 45-46, 49).; labels:  |
| PASS | host: board-previews-send-nothing | mode None; last command unchanged: True; hint still offered: End Bulwark (a3)'s activation |
| PASS | host: unit-card-closed-bar-buttons | bar before: Move / Dash / End activation; choosing a move: Move here / Cancel (Confirm "Move here"); card shows tab; the bar's Cancel stopped choosing and sent nothing: True |
| PASS | host: board-action-bar-shown | bar: Move / Dash / End activation; offered: Move / Dash; available: Move / Dash / Retreat / Shoot / Charge / Fight / Combat |
| PASS | host: board-bar-click-starts-action | Move offered: True; mode Move; bar:  |
| PASS | host: board-move-area-shown | 202 points drawn of 202 in the area; reach 5" |
| PASS | host: board-click-too-far-not-sent | mode Move; pending ; notes: a3: TOO FAR; last command unchanged: True |
| PASS | host: board-right-click-cancels | mode None; pending ; labels: ; last command unchanged: True |
| PASS | host: board-keys-on-buttons | bar: Move · M / Dash · D / End activation; actions Move, Dash; panel keys line hidden with no action: True |
| PASS | host: board-esc-cancels-everywhere | Move Esc: started True, keys line True, cancelled True; Move right-click: started True, keys line True, cancelled True; Dash Esc: started True, keys line True, cancelled True; Dash right-click: started True, keys line True, cancelled True |
| PASS | host: panel-confirm-shows-action | Confirm "Move here", disabled: False |
| PASS | host: board-no-double-send | Confirm, then Space and Confirm again while waiting: 1 command(s) sent (one confirmation sends 1) |
| PASS | host: board-move-glides | glides 4 -> 5; last (a3, (1.398, 21.413), (6.398, 21.413), 0.25); drawn at (6.398, 21.413); server (6.398, 21.413) |
| PASS | host: board-redraws-changed-parts | terrain drawn 9 -> 9 times; models not at the server's position: []; far enemy b2 unchanged: True, kept its nodes: True |
| PASS | host: board-send-move | Move: looks legal (preview; the server decides); last command: DONE: Done: Move a3 to (6.4", 21.4").; event: [43] a3 moves to (6.398", 21.413") (cost 5.000").; now [A] Bulwark (a3): Standing, ACTIVE, at (6.4", 21.4"), on the ground, base 1.3".; notice: DONE: Move a3 to (6.4", 21.4"). The server accepted it; the battlefield shows its result. |
| PASS | host: log-repaints-changed-rows | 0 of 43 rows painted again after picking row 43 (newest: True); rows marked •: 0 |
| PASS | host: log-click-points-out | row 43 names a3; ringed a3; camera kept while on screen: True (zoom 45.8); off screen first: True, then centred at the same zoom: True; nothing selected or sent: True |
| PASS | host: key-sheet | ? opened 43 lines; same as the Menu's: True; Esc closed it: True |
| PASS | host: key-sheet-keys | 44 keys, each a bold key cap inside the sheet, the sheet inside the window, at text 100/150% and scale 100/150% |
| PASS | host: board-click-dash | clicked (7.0", 19.5"); last command: DONE: Done: Dash a3 to (7.0", 19.5").; event: [45] a3 Dash roll 5: Failure. |
| PASS | host: board-note-shown | notes: a3: DASH FAILED (wanted a DASH note above a3) |
| PASS | host: focus-after-hint-answered | focus after the answer: IconButton "" |
| PASS | host: board-send-shoot | Shoot: looks legal (preview; the server decides); last command: DONE: Done: Shoot b4 with a1.; new events: [57] a1 shoots b4 at long range. / [58] Success Roll 8 (-1 dice): Success. / [59] Injury Roll on b4: 12 (0 injury dice): OutOfAction. |
| PASS | host: activation-hints-used | 3 hint buttons pressed (activate / end activation) |
| PASS | host: live-opponent-events | 5 opponent event lines arrived while connected; 0 times not connected; 16 opponent event lines before the host played |
| PASS | host: turn-scored | [65] b2 ends its activation (RiskyFailure). |
| PASS | host: vp-shown | Activation. Your move.\|Turn 2 of 4. Phase: Activation.\|Seat One [A] (you): Ash Company. VP 0, zone west.\|Seat Two [B]: Lantern Guard. VP 0, zone east. |
| PASS | host: event-list-shows-newest | event list scrolled to 1850 of 2494 |
| PASS | host: reconnecting-shown | Connection: Connection lost; reconnecting… |
| PASS | host: focus-kept-on-redraw | before: Button "Leave"; after the screen was redrawn: Button "Leave" |
| PASS | host: gave-up-offers-rejoin-now | Connection: Could not reconnect. The server may still hold your seat until its rejoin window ends: use Rejoin now. |
| PASS | host: rejoin-now-enter | Tab reached Button "Rejoin now", then Enter |
| PASS | host: rejoin-now-works | Connection: Connected. |
| PASS | host: text-150 | OptionButton "Text 150%" selected "Text 150%" with Enter, Down, Enter |
| PASS | host: text-150-applied | default font size 27 |
| PASS | host: text-150-fits-width | all text within 1280px |
| PASS | host: layout-text-150 | Confirm 113/248px, Undo 79/124px, Cancel 94/125px |
| PASS | host: contrast-space | Tab reached CheckButton "High contrast", then Space |
| PASS | host: high-contrast-applied | background (0, 0, 0, 1), text (1, 1, 1, 1) |
| PASS | host: ui-theme-high-contrast | section titles in the heading font: none; buttons Godot's own: True |
| PASS | host: event-list-scrolls-with-keys | Tab reached the event list: True; Home 0, End 3409, Page Up 2855 |
| PASS | host: event-list-end-returns-to-newest | after End: 3409 of 4003 |
| PASS | host: seats-by-letter-not-colour | 8 model lines, all with [A]/[B] |
| PASS | host: feedback-in-words | DONE: Done: End Sharpshooter (a1)'s activation. |
| PASS | host: connection-in-words | 3 bars; Connected: Connected.\|Command round trip: last 17 ms (17 s ago), median of the last 7: 17 ms.\|Server: ws://127.0.0.1:62135/match |
| PASS | host: settings-remembered | file while changed: text=2 contrast=1 dice=1 topdown=0 scale=0 sound=0 anim=0; after turning back: text=0 contrast=0 dice=1 topdown=0 scale=0 sound=0 anim=0; a file with text=1 contrast=1 dice=3 topdown=1 scale=0 sound=0 anim=0 read again and shown: True; defaults back: True; file <repo>\test-results\20261126-113529-automated\ui-check\windows-20261126-114020\detail\e2e\settings-host.cfg |
| PASS | host: layout-1000x700 | window (1000, 700); battlefield (0, 0), (1000, 700); unit card (8, 364), (380, 328); Confirm and Cancel on the first screen and fit: True (Confirm 79/170px, Undo 55/85px, Cancel 66/85px); nothing past the right edge |
| PASS | host: layout-1280x800 | window (1280, 800); battlefield (0, 0), (1280, 800); unit card (8, 464), (380, 328); Confirm and Cancel on the first screen and fit: True (Confirm 79/170px, Undo 55/85px, Cancel 66/85px); nothing past the right edge |
| PASS | host: layout-scale-125-1280x800 | window 1280x800 at 125%: content (1024, 640) (scaled: True); Confirm and Cancel on the first screen: True; note that they are below: False; words fit: True (Confirm 79/170px, Undo 55/85px, Cancel 66/85px); battlefield top 0, row (0, 0), (1024, 640), panel (8, 324.6), (380, 307.4), Confirm at 588+34; nothing past the right edge |
| PASS | host: layout-scale-150-1280x800 | window 1280x800 at 150%: content (853.3333, 533.3333) (scaled: True); Confirm and Cancel on the first screen: True; note that they are below: False; words fit: True (Confirm 79/159px, Undo 55/79px, Cancel 66/80px); battlefield top 0, row (0, 0), (853, 534), panel (8, 296.90002), (358.25998, 229.09998), Confirm at 482+34; nothing past the right edge |
| PASS | host: layout-scale-125-1000x700 | window 1000x700 at 125%: content (800, 560) (scaled: True); Confirm and Cancel on the first screen: True; note that they are below: False; words fit: True (Confirm 79/148px, Undo 55/74px, Cancel 66/74px); battlefield top 0, row (0, 0), (800, 560), panel (8, 307.82), (336, 244.18), Confirm at 508+34; nothing past the right edge |
| PASS | host: match-on-screen | 1280x800, 1920x1080, 1000x700 at text 100/150% and scale 100/125/150%: nothing to scroll; HUD, Confirm, Cancel, the choices shown, last command, corner icons and the open combat log inside the window |
| PASS | host: battlefield-fills-window | at every size, text size and scale the battlefield is the window's whole width and height under the HUD |
| PASS | host: menus-never-cover | 7 sizes, text sizes and scales: the Menu, opened with its icon while the log drawer was open, closed the drawer (one open at a time), is opaque with high-contrast text, inside the window, and covers none of: Cancel, Confirm, battlefield control, unit card |
| PASS | host: unit-card-collapsed-sizes | 7 window sizes, text sizes and scales: closed, the unit card is only its tab, inside the battlefield; opened, the card is back inside it |
| PASS | host: no-side-column | nothing beside the battlefield at any size: the unit card, last command and log are on it |
| PASS | host: server-stop-shown | Connection: Connection lost; reconnecting… |
| PASS | host: server-restart-clear | Connection: The match is no longer on the server (UnknownMatch). No result was recorded as a win or loss. \|\| actions: Nothing to send: the match is over. |
| PASS | host: match-result-shown | Result: The match is no longer on the server (UnknownMatch). No result was recorded as a win or loss.\|Final state hash: 7cb69b7ef3977cf6215427e1e1f0612d6b3539c2674ee97c355b219310f70985\|Events: 65\|This window did not receive the server's abort message: the hash is this client's copy after the last event it received. \|\| last event [65] |
| PASS | host: result-overlay | result box (330, 159), (620, 379.80005) in the battlefield (0, 0), (1280, 800): centred True, just under the HUD (inset 151) True, width 620 (at most 620), height 380 (at most 60% of 633) True; corner icons (996, 6), (276, 36) |
| PASS | host: copy-final-hash-enter | Tab reached Button "Copy final hash", then Enter |
| PASS | host: copy-final-hash | clipboard: 7cb69b7ef3977cf6215427e1e1f0612d6b3539c2674ee97c355b219310f70985; shown: 7cb69b7ef3977cf6215427e1e1f0612d6b3539c2674ee97c355b219310f70985 |
| PASS | guest: type-server | LineEdit "ws://127.0.0.1:62136/match" = "ws://127.0.0.1:62136/match" |
| PASS | guest: type-code | LineEdit "match code" = "L8H7WXKB" |
| PASS | guest: join-space | Tab reached Button "Join match", then Space |
| PASS | guest: join | Match code L8H7WXKB. You are Seat Two [B]. |
| PASS | guest: gaps-shown-on-join | rules dialog at the first match |
| PASS | guest: zone-event-shown | [4] Seat One must decide: choose a deployment zone. |
| PASS | guest: positional-not-offered-explained | Nothing to do until the opponent moves. |
| PASS | guest: board-place-preview | cursor (33.0", 18.0"); Place: looks legal (preview; the server decides); panel: No unit selected\|Placing a model in your deployment zone.\|PLACE B1 → (33.0", 18.0")\|PREVIEW · the server decides; labels:  |
| PASS | guest: board-cursor-steps | start (33.0", 18.0"); Right (33.5", 18.0"); Shift+Right (35.5", 18.0") |
| PASS | guest: board-place-refused-reason | after 3 steps of 2" to (29.5", 18.0"): Place: refused. A deployed model must be wholly within its deployment zone. (1.0.2 p. 146; preview; the server decides) |
| PASS | guest: board-refused-preview-not-sent | notice: Not sent: the preview says the server would refuse. A deployed model must be wholly within its deployment zone. (1.0.2 p. 146) Confirm again to send it anyway; the server decides.; last command unchanged: True; events 7 -> 7 |
| PASS | guest: board-escape-cancels | mode None; 0 preview labels left |
| PASS | guest: board-send-refused-shown | last command: REFUSED BY THE RULES: Refused by the rules: A deployed model must be wholly within its deployment zone. (rule: 1.0.2 p. 146); battlefield: REFUSED BY THE RULES: Refused by the rules: A deployed model must be wholly within its deployment zone. (1.0.2 p. 146); events 7 -> 7 |
| PASS | guest: board-send-place | last command: DONE: Done: Place b1 at (33.0", 18.0").; event: [8] Two sets up b1 at (33.000", 18.000").; on the battlefield: [B] Marksman (b1): Standing, at (33.0", 18.0"), on the ground, base 1.0".; notice: DONE: Place b1 at (33.0", 18.0"). The server accepted it; the battlefield shows its result. |
| PASS | reopen: type-server | LineEdit "ws://127.0.0.1:62136/match" = "ws://127.0.0.1:62136/match" |
| PASS | reopen: refresh-enter | Tab reached Button "Refresh saved seats", then Enter |
| PASS | reopen: saved-seat-listed | Rejoin match L8H7WXKB as Seat Two |
| PASS | reopen: rejoin-saved-enter | Tab reached Button "Rejoin match L8H7WXKB as Seat Two", then Enter |
| PASS | reopen: reopen-same-seat | Match code L8H7WXKB. You are Seat Two [B]. |
| PASS | reopen: opponent-away-on-cut | Connection: Connected.\|Your opponent disconnected. The match is paused until they rejoin, at the latest 19:47:11 UTC (server time). If they do not, the match is aborted with no result. |
| PASS | reopen: focus-kept-on-redraw | Tab reached Leave: True; after the screen was redrawn: Button "Leave" |
| PASS | reopen: opponent-back-after-rejoin | Connection: Connected. |
| PASS | reopen: server-stop-shown | Connection: Connection lost; reconnecting… |
| PASS | reopen: server-restart-clear | Connection: The match is no longer on the server (UnknownMatch). No result was recorded as a win or loss. \|\| actions: Nothing to send: the match is over. |
| PASS | reopen: match-result-shown | Result: The match is no longer on the server (UnknownMatch). No result was recorded as a win or loss.\|Final state hash: 7cb69b7ef3977cf6215427e1e1f0612d6b3539c2674ee97c355b219310f70985\|Events: 65\|This window did not receive the server's abort message: the hash is this client's copy after the last event it received. \|\| last event [65] |
| PASS | reopen: result-overlay | result box (330, 159), (620, 379.80005) in the battlefield (0, 0), (1280, 800): centred True, just under the HUD (inset 151) True, width 620 (at most 620), height 380 (at most 60% of 633) True; corner icons (996, 6), (276, 36) |
| PASS | reopen: copy-final-hash-enter | Tab reached Button "Copy final hash", then Enter |
| PASS | reopen: copy-final-hash | clipboard: 7cb69b7ef3977cf6215427e1e1f0612d6b3539c2674ee97c355b219310f70985; shown: 7cb69b7ef3977cf6215427e1e1f0612d6b3539c2674ee97c355b219310f70985 |
| PASS | godot-host: engine errors | none in the output |
| PASS | godot-guest: engine errors | none in the output |
| PASS | godot-reopen: engine errors | none in the output |
| PASS | hotseat-exit | hot-seat Godot exit code 0 (0 = all its checks passed) |
| PASS | hotseat: tab-reaches-every-control-menu | 6 stops: Button "Play" > Button "Build Your Own Roster" > Button "Settings" > Button "Credits" > Button "Exit" > Button "·" |
| PASS | hotseat: shift-tab-reverses-menu | Button "·" < Button "Exit" < Button "Credits" < Button "Settings" < Button "Build Your Own Roster" < Button "Play" |
| PASS | hotseat: menu-navigation | menu panel centred: off by (0, -1) px; Settings: settings shown True, Back there True, Esc returned True; Build Your Own Roster: roster shown True, Back there True, Esc returned True; Credits: credits shown True, Back there True, Back returned True; Play: play shown True, Back there True, Esc returned True; developer: shown True, Back returned True |
| PASS | hotseat: tab-reaches-every-control-play | 7 stops: Button "Start hot-seat" > Button "Online" > Button "Back" > OptionButton "ranged (normal roster)" > OptionButton "The scenario's own map" > OptionButton "The scenario's practice warband" > OptionButton "The scenario's practice warband" |
| PASS | hotseat: shift-tab-reverses-play | OptionButton "The scenario's practice warband" < OptionButton "The scenario's practice warband" < OptionButton "The scenario's own map" < OptionButton "ranged (normal roster)" < Button "Back" < Button "Online" < Button "Start hot-seat" |
| PASS | hotseat: play-pages | Play: Start hot-seat and Online; Online: address, Host and Connect; Back returns to Play; Connect: match code (focused), Join and Rejoin |
| PASS | hotseat: tab-reaches-every-control-play-connect | 9 stops: LineEdit "match code" > Button "Join match" > Button "Refresh saved seats" > CheckButton "Advanced" > Button "Back" > LineEdit "ws://127.0.0.1:5080/match" > OptionButton "The scenario's practice warband" > Button "Host match" > Button "Connect" |
| PASS | hotseat: shift-tab-reverses-play-connect | Button "Connect" < Button "Host match" < OptionButton "The scenario's practice warband" < LineEdit "ws://127.0.0.1:5080/match" < Button "Back" < CheckButton "Advanced" < Button "Refresh saved seats" < Button "Join match" < LineEdit "match code" |
| PASS | hotseat: menu-look | menu theme on: Play rust, Exit rust words, focus frame with a left bar, title in bone; high contrast plain; 10 sizes on the menu and Play screens: nothing past the edge, no words cut, no overlaps |
| PASS | hotseat: roster-builder | empty: The Principality: 0 of 700 ducats · 1 problem; lieutenant with pistol and sword: The Principality: 80 of 700 ducats · legal; a second lieutenant: The Principality: 150 of 700 ducats · 1 problem; yeoman with rifle and Trench Mole: The Principality: 125 of 700 ducats · legal; saved True; Pontifical Task Force: Pontifical Task Force: 0 of 500 ducats, 0 of 11 Glory · 1 problem; loaded: The Principality: 125 of 700 ducats · legal |
| PASS | hotseat: hotseat-play-enter | Tab reached Button "Play", then Enter |
| PASS | hotseat: connect-online-enter | Tab reached Button "Online", then Enter |
| PASS | hotseat: connect-connect-enter | Tab reached Button "Connect", then Enter |
| PASS | hotseat: connect-bad-address | ""hello there" is not a server address. It looks like ws://127.0.0.1:5080/match.", Cancel False, Retry False, screen play, code "ABCDEFGH", focus Button "Join match" |
| PASS | hotseat: connect-refused-retry | first: "Nothing answered at that address. Is the server running, and is the port right?", Cancel False, Retry True, screen play, code "ABCDEFGH", focus Button "Join match"; after Retry: "Nothing answered at that address. Is the server running, and is the port right?", Cancel False, Retry True, screen play, code "ABCDEFGH", focus Button "Retry" |
| PASS | hotseat: connect-cancel | while connecting: "Connecting…", Cancel True, Retry False, screen play, code "ABCDEFGH", focus Button "Join match"; after Cancel: "Cancelled. Nothing was joined.", Cancel False, Retry True, screen play, code "ABCDEFGH", focus Button "Retry" |
| PASS | hotseat: connect-timeout | after 2.5 s: "The server did not answer within 2 seconds. Check that it is running and that the address is right, then retry.", Cancel False, Retry True, screen play, code "ABCDEFGH", focus Button "Join match" |
| PASS | hotseat: connect-back-cancels | connecting True, menu True, stayed True; back on Play: "Cancelled. Nothing was joined.", Cancel False, Retry True, screen play, code "ABCDEFGH", focus Button "Start hot-seat" |
| PASS | hotseat: play-roster-pickers | one seat's roster: "Choose a saved roster for both seats, or the practice warbands for both."; hosting with Binoculars: "Your roster Picker Lookouts has what the game cannot play yet: model 1, Lieutenant: Binoculars (the engine has no equipment yet)" |
| PASS | hotseat: hotseat-choose-scenario | OptionButton "ranged (normal roster)" selected "ranged" with Enter, Down, Enter |
| PASS | hotseat: hotseat-start-enter | Tab reached Button "Start hot-seat", then Enter |
| PASS | hotseat: hotseat-started | HOT-SEAT: you control Seat One [A]. Waiting for the server to start the match. Switch to Seat Two with F2 (the seat follows the turn).\|Local server ws://127.0.0.1:55496/match (process 19672); it stops when you leave or close this window. |
| PASS | hotseat: hotseat-server-running | local server ws://127.0.0.1:55496/match; Connection: Connected. |
| PASS | hotseat: hotseat-one-window-both-seats | Match code 695UUUNF. You are Seat Two [B]. \|\| HOT-SEAT: you control Seat Two [B]. Seat Two to move. Switch to Seat One with F2 (the seat follows the turn).\|Local server ws://127.0.0.1:55496/match (process 19672); it stops when you leave or close this window. |
| PASS | hotseat: deploy-refused-explained | pointed at (2.0", 18.0"): reason box 'A deployed model must be wholly within its deployment zone.' at (364.1422, 369.5), (305, 61) (ghost at (340.1422, 400)); rules: A deployed model must be wholly within its deployment zone.; after a click: sent 0 -> 0, notice Not sent: A deployed model must be wholly within its deployment zone. (1.0.2 p. 146) Choose another point; the keys can still send it (Space twice) for the server to decide. (now: Not sent: A deployed model must be wholly within its deployment zone. (1.0.2 p. 146) Choose another point; the keys can still send it (Space twice) for the server to decide.); the click: Place at (2.0, 18.0): Refused; controls taking clicks there: ColorRect @ColorRect@3, ScrollContainer @ScrollContainer@6, VBoxContainer @VBoxContainer@7, VBoxContainer @VBoxContainer@372, VBoxContainer @VBoxContainer@365, BoxContainer BattlefieldAndPanel, Battlefield @SubViewportContainer@344; after: Battlefield @SubViewportContainer@344; under the pointer Battlefield; seat shown Seat One -> Seat One |
| PASS | hotseat: deploy-direct | mode Place; bar Place here / Cancel / ▶ a1 / a2 / a3 / a4; to place a1,a2,a3,a4; HUD: [A] SEAT ONE · YOUR MOVE · Turn limit 4 · Deployment · Place a1 in ZONE A: click a spot · VP A 0 – B 0 · objectives now: ◆1 – ◆2 – ◆3 – ◆4 – ◆5 –; one click at (31.3", 2.6"): answer Accepted, sent 0 -> 1; event [8] One sets up a1 at (31.300", 2.600").; a1 drawn at (31.3, 2.6) |
| PASS | hotseat: hotseat-both-seats-deploy | 2 placements by Seat One and 2 by Seat Two, all sent from the battlefield of the seat shown |
| PASS | hotseat: hotseat-follows-turn | seats shown in turn: Seat Two, Seat One, Seat Two, Seat One, Seat Two |
| PASS | hotseat: hotseat-whose-move-shown | HOT-SEAT: you control Seat One [A]. Seat One to move. Switch to Seat Two with F2 (the seat follows the turn).\|Local server ws://127.0.0.1:55496/match (process 19672); it stops when you leave or close this window. |
| PASS | hotseat: hotseat-f2-switches | before F2: Seat One; after: Seat Two; Match code 695UUUNF. You are Seat Two [B]. |
| PASS | hotseat: hotseat-f2-kept | after 30 frames: Seat Two |
| PASS | hotseat: hotseat-switch-keeps-playback | Two -> One by F2 with a made-up shot playing: 3 beats drawn on their time, 0 shown at once, [MuzzleFlash Tracer HitPuff], tracers drawn 1, cut short by nobody, still afterwards |
| PASS | hotseat: hotseat-log-behind-waits | log of 11 events handed with the last 3 missing, then whole, while a made-up shot played: waited out 1, still playing after it True, cut short 0 time(s) [], tracer drawn 1, events shown again 0 |
| PASS | hotseat: hotseat-speed-choice-skips | same speed picked mid-playback: still playing True; Off picked mid-playback: playing before True, shown at once with no tracer drawn True; speed put back to "Animations: Full" True |
| PASS | hotseat: hotseat-hits-see-through | a made-up hit and blow: 336 red spatter and 9 pale flash squares seen over the frames; most opaque 0.49 (at most 0.55), largest 0.38" (at most 0.4) |
| PASS | hotseat: hotseat-action-gestures | aim: lift -0.083..0.000, lean 0.16 rad, push 0.00 in ok, back at rest True; shot: lift 0.000..0.000, lean 0.00 rad, push 0.12 in ok, back at rest True; dig in: lift -0.128..0.000, lean 0.40 rad, push 0.00 in ok, back at rest True; tend: lift 0.000..0.000, lean 0.00 rad, push 0.25 in ok, back at rest True; rally: lift 0.000..0.053, lean 0.14 rad, push 0.00 in ok, back at rest True; walk: 61 frames posed, thigh swung up to 26.0 degrees, back at rest True; wound: reels started 2 (2), body recoils on the hit 2 (2), trunk reeled 0.09 rad by a pistol, 0.29 rad by a blast, model moved at most 0.000", back at rest on its place True; gestures 3 (3), recoils 3 (3) |
| PASS | hotseat: hotseat-blasts-see-through | a made-up grenade blast with fire, gas and shrapnel, then a Martyrdom blast: 2717 squares over the frames, all drawn at their size True, at most 61 at once (at least 12); largest 1.32" (at most 1.4); most opaque square wider than 0.3" 0.51 (at most 0.62) |
| PASS | hotseat: hotseat-full-match | [116] MATCH ENDED: draw (VictoryPoints).; 72 clicks and placements by both seats in this window; 116 events |
| PASS | hotseat: board-frame-time | idle frames 3399 mean 0.09 ms max 19.37 ms; animating frames 694 mean 0.17 ms max 13.43 ms; updates 163 mean 8.44 ms max 145.15 ms; terrain builds 1 (0 while animating); models drawn 446 (4 while animating) |
| PASS | hotseat: hotseat-turn-banner | 40 seat changes, each with the server's words (2 decisions): SEAT A: YOUR TURN \| SEAT B: YOUR TURN \| SEAT A DECIDES: choose who activates first; banners whose time ran out stayed 1.50-1.50 s |
| PASS | hotseat: hotseat-bar-seat-letter | 40 looks at the action bar, each starting with the shown seat's letter |
| PASS | hotseat: hotseat-log-draws-dice | 6 of 6 log rows with a roll draw its dice; log open: True |
| PASS | hotseat: hotseat-leave-enter | Tab reached Button "Leave", then Enter |
| PASS | hotseat: hotseat-server-stopped | local server ws://127.0.0.1:55496/match no longer accepts connections; screen after Leave: menu, focus Button "Play" |
| PASS | hotseat: online-roster-type-server | LineEdit "ws://127.0.0.1:55503/match" = "ws://127.0.0.1:55503/match" |
| PASS | hotseat: board-new-match-reset | new matches started 1; the hot-seat match left 8 remembered models (a1 Sharpshooter r0.49, b1 Marksman r0.49, a2 Skirmisher r0.49, b2 Runner r0.49); now 0 remembered, 0 on this table (); not of this table or not as it draws them: []; the hot-seat match's own still remembered: []; facings kept 0; playing False; casualty figures 0 |
| PASS | hotseat: online-roster-match | hosted KPBZY4SM with Picker Line.roster.json; second player joined with Picker Saints.roster.json: Welcome; the window shows Picker Line / Picker Saints · Lieutenant (a1), Lieutenant (b1) |
| PASS | godot-hotseat: engine errors | none in the output |
| PASS | hotseat-no-server-left | no Match.Server process uses the hot-seat folder |
| PASS | hotseat-record-verifies | trench-cli verify c8e3e81412a30256bc2fd14bb6dbea88.json --key signing.pub: exit 0: Completed: Completed: a draw (VictoryPoints). Match c8e3e81412a30256bc2fd14bb6dbea88 (code 695UUUNF, ranged), written by build unknown under rules 1.0.2. Final hash dd72f5300e30a34fa74a60b3cbd819c1e4d0131044006d5072a266313f468946: hash matches. This proves the signing server wrote this record and that every event follows the rules from the recorded dice. It does not prove the dice were fair: the server that holds the key chose them. \|\| the window showed dd72f5300e30a34fa74a60b3cbd819c1e4d0131044006d5072a266313f468946 |
| PASS | hotseat-mines-exit | hot-seat Godot exit code 0 (0 = all its checks passed) |
| PASS | hotseat-mines: mines-choose-scenario | OptionButton "ranged, Trench Warfare, 1 v 1 (test roster)" selected "ranged-1v1-trench-warfare" with Enter, Down, Enter |
| PASS | hotseat-mines: mines-start-enter | Tab reached Button "Hot-seat: play both sides in this window", then Enter |
| PASS | hotseat-mines: mines-hotseat-started | HOT-SEAT: you control Seat One [A]. Waiting for the server to start the match. Switch to Seat Two with F2 (the seat follows the turn).\|Local server ws://127.0.0.1:55506/match (process 18196); it stops when you leave or close this window. |
| PASS | hotseat-mines: mines-card | title YOUR CHOICE: lay 12 Mine Markers, 4 secret duds; Lay disabled; area TableRect { X0 = 18, Y0 = 0, X1 = 30, Y1 = 36, Width = 12, Depth = 36, CentreX = 24, CentreY = 18 }; seat Seat Two |
| PASS | hotseat-mines: mines-click-places | clicked at (666.4698, 616.8277); placed 1, drawn 1; note: mine-1 placed at (19.2", 30.0").; click: Mines at (19.2, 30.0): mine-1 placed at (19.2", 30.0"). |
| PASS | hotseat-mines: mines-fill | Tab reached Button "Fill in at random", then Enter |
| PASS | hotseat-mines: mines-filled | placed 12, duds 4, drawn 12, Lay enabled; Markers and duds filled in at random. |
| PASS | hotseat-mines: mines-lay | Tab reached Button "Lay the mines", then Enter |
| PASS | hotseat-mines: mines-laid | log: [7] Two sets up 12 Mine Markers; 4 of them are secret duds.; drawn 12; seat shown Seat One (defender Seat Two): 0 duds, 12 unknown |
| PASS | godot-hotseat-mines: engine errors | none in the output |
| PASS | hotseat-mines-no-server-left | no Match.Server process uses the hot-seat folder |
| PASS | abilities-exit | abilities Godot exit code 0 (0 = all its checks passed) |
| PASS | abilities: abilities-match-1-started | R6 Faith.roster.json against R6 Iron.roster.json |
| PASS | abilities: abilities-match-1-to-the-end | [137] MATCH ENDED: Two wins (OpponentFled). |
| PASS | abilities: abilities-match-2-started | R6 Monastery.roster.json against R6 Brass.roster.json |
| PASS | abilities: abilities-match-2-to-the-end | [179] MATCH ENDED: Two wins (VictoryPoints). |
| PASS | abilities: abilities-rosters-play-to-the-end | [137] MATCH ENDED: Two wins (OpponentFled). \| [179] MATCH ENDED: Two wins (VictoryPoints). |
| PASS | abilities: board-blast-sequence | 1 events seen live with 1 explosions and 0 marker bursts; 1 explosion and 0 burst beats came due (1 explosions and 0 bursts drawn, 0 grenades thrown); still showing 0, playing False |
| PASS | abilities: abilities-action-keys | I Aim: ok; V Fortify: ok; Y Orthodoxy: ok; Z LayHands: ok; H Treat: ok; B Detonate: ok |
| PASS | abilities: abilities-lay-hands | a Laying on of Hands was previewed and sent |
| PASS | abilities: abilities-action-previews | I Aim: its words and Confirm button; V Fortify: its words and Confirm button; Y Orthodoxy: its words and Confirm button; Z LayHands: its words and Confirm button; B Detonate: its words and Confirm button |
| PASS | abilities: abilities-combat-keys | 4 Shoot, 2nd weapon: ok; 5 Fight, 2nd weapon: ok; 6 Throw grenade: ok; 7 Blast a point: ok; 9 Grenade at a point: ok; 0 Trample: ok, 0 Grind to Dust: ok |
| PASS | abilities: abilities-actions-narrated | Aim ok; Fortify ok; Orthodoxy ok; LayHands ok; Detonate ok |
| PASS | abilities: abilities-decision-talisman | 7: match-1: [Use a Talisman: the failed Risky roll does not end the activation / Keep the Talisman: the activation ends] chose "Use a Talisman: the failed Risky roll does not end the activation" \| match-1: [Use a Talisman: the failed Risky roll does not end the activation / Keep the Talisman: the activation ends] chose "Keep the Talisman: the activation ends" \| match-2: [Use a Talisman: the failed Risky roll does not end the activation / Keep the Talisman: the activation ends] chose "Use a Talisman: the failed Risky roll does not end the activation"; 4 "uses a Talisman" rows; 5 activations ended by a failed Risky roll |
| PASS | abilities: abilities-decision-morale | 1: match-1: [Failed Morale Check: become Shaken / Failed Morale Check: Sound Retreat (the warband flees)] chose "Failed Morale Check: become Shaken" |
| PASS | godot-abilities: engine errors | none in the output |
| PASS | abilities-no-server-left | no Match.Server process uses the abilities folder |
| PASS | pvp-b-hard-killed | second Godot client killed at the start of turn 2 with 57 events on its screen (exit code -1; it never finished on its own) |
| FAIL | pvp-a-exit | first Godot client exit code 1 (0 = all its checks passed) |
| PASS | pvp-b-rejoin-exit | rejoined Godot client exit code 0 |
| PASS | pvp-both-same-final-hash | Seat One: ea1ce3c257718c3d5c423eacf69b363a77279e4d11db50e03763497352262a04 \|\| Seat Two: ea1ce3c257718c3d5c423eacf69b363a77279e4d11db50e03763497352262a04 |
| PASS | pvp-both-same-event-count | Seat One: 201 \|\| Seat Two: 201 |
| PASS | pvp-both-same-events | 200 event lines on Seat One, 200 on Seat Two |
| PASS | pvp-both-same-unit-looks | Seat One: a1:Activated:Down,a2:Activated:Standing,a3:Activated:Standing,b1:Activated:Standing,b2:Activated:Standing,b3:Activated:Standing,b4:Activated:Standing \|\| Seat Two: a1:Activated:Down,a2:Activated:Standing,a3:Activated:Standing,b1:Activated:Standing,b2:Activated:Standing,b3:Activated:Standing,b4:Activated:Standing |
| PASS | pvp-both-same-dice | 24 rolls shown in both windows, all alike (Seat One showed 30, Seat Two after rejoining 27) |
| PASS | pvp-rejoin-shows-no-old-dice | the rejoined window was sent the match again up to event 1 and caught up on 2-57 without showing them; it showed 27 rolls, 0 of them from before |
| PASS | pvp-both-same-status-strip | Seat One: [A] SEAT ONE · Nobody: the match has ended · Turn 4 of 4 · ended · VP A 2 – B 5 · objectives now: ◆1 – ◆2 – ◆3 – ◆4 – ◆5 B \|\| Seat Two: [B] SEAT TWO · Nobody: the match has ended · Turn 4 of 4 · ended · VP A 2 – B 5 · objectives now: ◆1 – ◆2 – ◆3 – ◆4 – ◆5 B |
| PASS | pvp-opponent-sees-only-chosen-unit | Seat One's activation after switching: [22] One activates a2.; in Seat Two's window: True |
| PASS | pvp-both-agree-on-outcome | [201] MATCH ENDED: Two wins (VictoryPoints). \|\| Seat One: You lost: victory points after the final turn. \|\| Seat Two: You won: victory points after the final turn. |
| PASS | pvp-no-events-lost-or-repeated-across-kill | the 57 events shown before the kill are the first of the 59 shown after rejoining |
| INFO | pvp-commands | Seat One: Activate DONE 16, Carry DONE 1, Charge DONE 7, End DONE 14, Fight DONE 2, Move DONE 5, Move NOT SENT 1, Place DONE 4, Shoot DONE 2, Spend DONE 1 \|\| Seat Two after rejoining: Activate DONE 12, Carry DONE 1, Charge DONE 6, End DONE 12, Fight DONE 5, Let DONE 1, Move DONE 3 |
| PASS | pvp-record-verifies-completed | trench-cli verify 23c505bb99cf5f0201e7b4ce20b097c0.json --key signing.pub: exit 0: Completed: Completed: Seat Two won (VictoryPoints). Match 23c505bb99cf5f0201e7b4ce20b097c0 (code 23G2ZNJX, melee), written by build unknown under rules 1.0.2. Final hash ea1ce3c257718c3d5c423eacf69b363a77279e4d11db50e03763497352262a04: hash matches. This proves the signing server wrote this record and that every event follows the rules from the recorded dice. It does not prove the dice were fair: the server that holds the key chose them. |
| PASS | pvp-record-hash-matches-both-clients | record: ea1ce3c257718c3d5c423eacf69b363a77279e4d11db50e03763497352262a04 \|\| Seat One: ea1ce3c257718c3d5c423eacf69b363a77279e4d11db50e03763497352262a04 \|\| Seat Two: ea1ce3c257718c3d5c423eacf69b363a77279e4d11db50e03763497352262a04 |
| PASS | pvp-record-event-count-matches-both-clients | record: 201 events \|\| Seat One: 201 \|\| Seat Two: 201 |
| PASS | pvp-record-outcome-matches | record: Completed: Completed: Seat Two won (VictoryPoints). \|\| clients: [201] MATCH ENDED: Two wins (VictoryPoints). |
| INFO | pvp-run-time | 191 s from the server start to the record check |
| PASS | pvp-a: pvp-play-enter | Tab reached Button "Play", then Enter |
| PASS | pvp-a: pvp-choose-scenario | OptionButton "melee (shooting and close combat)" selected "melee" with Enter, Down, Enter |
| PASS | pvp-a: pvp-create-online-enter | Tab reached Button "Online", then Enter |
| PASS | pvp-a: pvp-type-server | LineEdit "ws://127.0.0.1:64517/match" = "ws://127.0.0.1:64517/match" |
| PASS | pvp-a: pvp-create-enter | Tab reached Button "Host match", then Enter |
| PASS | pvp-a: pvp-create | Match code 23G2ZNJX. You are Seat One [A]. Give the code to your opponent. |
| PASS | pvp-a: pvp-eligible-units-marked | rings on a1, a2, a3, a4; the rules offer a1, a2, a3, a4 |
| PASS | pvp-a: board-next-eligible | J chose a1 (expected a1), then a2 (expected a2), Shift+J a1; the rules offer a1, a2, a3, a4; camera kept while on screen: True; nothing sent: True |
| PASS | pvp-a: pvp-choose-unit-sends-nothing | chosen a1; bar: Move / Dash / Take no action / Not activated yet; events 21 -> 21; last command unchanged: True |
| PASS | pvp-a: pvp-status-strip | HUD: [A] SEAT ONE · YOUR MOVE · Turn 1 of 4 · Activation · VP A 0 – B 0 · objectives now: ◆1 – ◆2 – ◆3 – ◆4 – ◆5 – \|\| centre: YOUR MOVE · VP A 0 – B 0 \|\| panel: A2 · Skirmisher\|Seat One [A] (yours) · Standing\|◌ Chosen: not activated yet (nothing sent)\|Move 7.0" · Range 12.0" · Armour 0 · Melee weapon\|Actions: Move · Dash · Take no action |
| PASS | pvp-a: pvp-switch-unit-sends-nothing | chosen a2 (wanted a2); bar: Move / Dash / Take no action / Not activated yet; events 21 -> 21 |
| PASS | pvp-a: pvp-first-action-activates-chosen | Move; new events: [22] One activates a2. \| [23] a2 moves to (34.500", 16.500") (cost 1.582").; last command: DONE: Done: Move a2 to (34.5", 16.5"). |
| PASS | pvp-a: pvp-no-switch-after-action | chosen ; notes: a1: CAN'T SWITCH; notice: Skirmisher (a2) has already started its activation on the server, so you cannot switch to Sharpshooter (a1): an activation is fixed once the server has it. End Skirmisher (a2)'s activation first. |
| PASS | pvp-a: pvp-refused-mid-match | Move: refused. A Move ACTION can be taken only once per activation. (1.0.2 p. 34; preview; the server decides); first Space: not sent; answer: REFUSED BY THE RULES: Refused by the rules: A Move ACTION can be taken only once per activation. (rule: 1.0.2 p. 34); events 23 -> 23; model at 34.5,16.5 (was 34.5,16.5) |
| PASS | pvp-a: pvp-server-refusal-note | notes: a1: CAN'T SWITCH / a2: REFUSED; answer: REFUSED BY THE RULES: Refused by the rules: A Move ACTION can be taken only once per activation. (rule: 1.0.2 p. 34); panel: A2 · Skirmisher\|Seat One [A] (yours) · Standing\|● Activating now\|Move 7.0" · Range 12.0" · Armour 0 · Melee weapon\|MOVE → (14.5", 16.5")\|PREVIEW · the server decides\|24.0" used of 7.0" (0.0" left)\|straight line 20.0"\|REFUSED: A Move ACTION can be taken only once per activation.\|REFUSED BY THE RULES: Refused by the rules: A Move ACTION can be taken only once per activation. (1.0.2 p. 34) |
| PASS | pvp-a: pvp-activated-unit-greyed | a2: greyed True, label says activated True; a1 Eligible, a2 Activated, a3 Eligible, a4 Eligible, b1 Eligible, b2 Eligible, b3 Eligible, b4 Eligible |
| PASS | pvp-a: turn-alert | not in front when the move turned: title '● YOUR MOVE · Trenches', asked for attention 1 time(s); in front again: title 'Trenches' |
| PASS | pvp-a: board-move-glides | glides 2 -> 3; last (a1, (33, 18), (28, 18), 0.25); drawn at (28, 18); server (28, 18) |
| PASS | pvp-a: board-redraws-changed-parts | terrain drawn 1 -> 1 times; models not at the server's position: []; far enemy b4 unchanged: True, kept its nodes: True |
| PASS | pvp-a: pvp-move-followed | end (28.0", 18.0") off screen before: True; followed 0 -> 1; camera on (28, 0, 18) |
| PASS | pvp-a: unit-card-closed-send | bar: Move here / Cancel; card shows tab; Confirm on the bar: True; 1 command(s) sent; move done: True (DONE: Done: Move a1 to (28.0", 18.0").) |
| PASS | pvp-a: pvp-preview-dice | Shoot: Hit roll: -1 DICE (Long Range, p. 43) \| Injury roll: modifier +1; +1 DICE on a critical hit; panel: A1 · Sharpshooter\|Seat One [A] (yours) · Standing\|● Activating now\|Move 6.0" · Range 24.0" · Armour 0 · No melee weapon\|SHOOT → B2 · Runner\|PREVIEW · the server decides\|Rules (the server decides):\|Distance 23.0", weapon range 24.0"\|Line of sight: partly visible, 6 of 9 sight rays clear, blocked by obj-5\|Target not in cover\|Long range: beyond half the weapon's range (1 penalty die)\|Hit roll: -1 DICE (Long Range, p. 43)\|Injury roll: modifier +1; +1 DICE on a critical hit\|dice: rolled by the server\|Guide: sight points seen, head 3 of 3, body 3 of 3, legs 0 of 3\|✕ not valid: B1 (out of range), B4 (out of range) |
| PASS | pvp-a: pvp-dice-offscreen-tray | model a1 off screen: True; box 34 beside it before: True, in the tray now: True; camera stayed: True |
| PASS | pvp-a: pvp-dice-tray-inside | box (815.99994, 680), (454.00006, 110) in the battlefield (0, 0), (1280, 800), bottom-right (log drawer inset 0); dice 2,3,6 ✗ MISSED 5 |
| PASS | pvp-a: pvp-dice-does-not-block-battlefield | pointer over box 34 at (757.5711, 525.5352): over Battlefield |
| PASS | pvp-a: pvp-dice-click-closes | box 34 closed by its ✕; nothing sent: True; selection kept: True |
| PASS | pvp-a: activation-ends-itself | Sharpshooter (a1) after Move, Dash, Shoot: note "Nothing left for Sharpshooter (a1) to do: its activation ends by itself. Esc keeps it going."; no key pressed; sent 10 -> 11 (1 by itself, 1 in the match); last command: DONE: Done: End Sharpshooter (a1)'s activation.; event: [35] a1 ends its activation (Voluntary). |
| PASS | pvp-a: pvp-activated-look-resets | a2: activated again in turn 2 False, look Waiting; a1 Waiting, a2 Waiting, a3 Waiting, a4 Waiting, b1 Waiting, b2 Waiting, b3 Waiting, b4 Waiting |
| PASS | pvp-a: pvp-opponent-drop-pauses | Connection: Connected.\|Your opponent disconnected. The match is paused until they rejoin, at the latest 19:53:38 UTC (server time). If they do not, the match is aborted with no result. \|\| actions: Nothing to send: the match is paused while your opponent is away. |
| PASS | pvp-a: combat-menu | A1 · Sharpshooter: choose an attack: Shoot open / Charge greyed: The target of a charge must be within 12". (1.0.2 p. 36) / Fight greyed: That model has no Melee Weapon. (1.0.2 pp. 45-46, 49); the rules' own verdicts before any target, beside the bar, nothing sent |
| PASS | pvp-a: shoot-confirm-button | "Confirm shoot" shown beside Runner (b2): True at (595.0711, 400) (True); nothing sent before it: True; one click: DONE: Done: Shoot b2 with a1.; sent 13 -> 14; events: [61] a1 shoots b2 at long range. / [62] Success Roll 6 (-1 dice): Failure. |
| PASS | pvp-a: pvp-dice-fades | box 62 gone 8.02 s after its last roll (setting 8 s); faded on the way: True |
| PASS | pvp-a: charge-confirm-button | Charge chosen with 1 click; Confirm charge shown beside Runner (b2): True at (730.7589, 400) (True); nothing sent before it: True; one click: DONE: Done: Charge b2 with a4.; sent 16 -> 17; events: [80] a4 declares a charge on b2. / [81] a4 Charge Bonus 6. / [82] a4 charges to (19.194", 18.399") (cost 12.000"): falls short of b2. (charge line: True) |
| INFO | pvp-a: wait | WAIT fight-after-charge timed out after 1s |
| PASS | pvp-a: pvp-dice-off | Off: 3 new rolls in the log, 0 shown, 0 boxes on screen |
| INFO | pvp-a: wait | WAIT fight-after-charge timed out after 1s |
| INFO | pvp-a: wait | WAIT fight-after-charge timed out after 1s |
| INFO | pvp-a: wait | WAIT fight-after-charge timed out after 1s |
| INFO | pvp-a: wait | WAIT fight-after-charge timed out after 1s |
| PASS | pvp-a: fight-from-combat-menu | dice display on, 1 new rolls in the log; Fight chosen on the charged enemy, Confirm fight shown: True, nothing sent before it: True; one click on it: DONE: Done: Fight b1 with a2.; sent 23 -> 24; rolls shown: A2 FIGHTS B1: hit roll 4,3,3 ✗ MISSED 6; events: [167] a2 fights b1 (defended obstacle: obj-5). / [168] Melee Success Roll 6 (-1 dice): Failure. |
| INFO | pvp-a: wait | WAIT fight-after-charge timed out after 1s |
| PASS | pvp-a: result-details | closed at first: True; top: YOU LOST\|A 2 – B 5\|Glorious Deeds: A Suicidal Bravery · B Resist and Bite · A Hold Your Ground\|Victory points after the final turn (wanted YOU LOST); Details shows the hash, Copy final hash and the summary: True (True, True, True, True) |
| PASS | pvp-a: pvp-match-completed | Result: You lost: victory points after the final turn.\|Final state hash: ea1ce3c257718c3d5c423eacf69b363a77279e4d11db50e03763497352262a04\|Events: 201\|VP by turn: turn 1: A +0, B +0 · turn 2: A +0, B +0 · turn 3: A +0, B +2 · turn 4: A +0, B +2; total A 2, B 5\|Glorious Deeds (+1 VP each): A Suicidal Bravery (A1) · B Resist and Bite (B2) · A Hold Your Ground\|Models at the end: Down A 1, B 0 · Out of Action A 1, B 0\|Rolls passed / failed (the server's dice): A 5 / 3, B 4 / 2 \|\| [201] MATCH ENDED: Two wins (VictoryPoints). \|\| events 2 to 201, each once and in order |
| PASS | pvp-a: event-effects | 9 events with effects (9 live, 0 caught up), 11 effects played; taken in twice: ;  |
| PASS | pvp-a: board-melee-sequence | 7 blows seen live, 7 strikes, 13 lunges, 38 turns, 15 charge leans, 6 charge impacts, 10 flinches; still playing: False |
| PASS | pvp-a: board-casualty-sequence | 4 status changes seen live; 2 falls, 1 rises, 1 going figures (0 freed); casualty figures left 0, held standing 0, still playing False |
| FAIL | pvp-a: board-bodies | 1 models Out of Action, 1 bodies; bodies of models on the table []; drawn directly again 1 of 1 alike; a4: ((19.194, 0, 18.399), 0.759171, 0.55, False, 1, True, (19.488813, 0.65552163, 18.119738)) (wanted at (19.19, 18.40), lying (tilt 1.2 to 2.6), shade 0.55, no base, a pool, fade 1) |
| PASS | pvp-a: pvp-dice-shown-once-as-logged | 30 rolls shown, 30 compared with the log's dice, 33 in the log; shown twice: ;  |
| PASS | pvp-a: pvp-dice-beside-model-clear | 3 looks at a box beside its model;  |
| PASS | pvp-a: pvp-dice-stacked | 134: 134, 135 in order / 175: 175, 176 in order / 193: 193, 194 in order |
| PASS | pvp-a: sound-cues | OK 1 events with a sound while off, 0 played, Sound on: False; after Sound on: 48 events with a sound, 48 played, same order: True; your-move sounds 15 (attention asked 20) |
| PASS | pvp-a: hud-on-battlefield | 16 looks at the HUD strip;  |
| PASS | pvp-a: hud-objectives-now | 16 looks at the HUD and the objective marks;  |
| PASS | pvp-a: pvp-panel-follows-state | 32 looks at the panel;  |
| PASS | pvp-a: panel-engaged-with | 32 looks, 5 at an engaged unit;  |
| PASS | pvp-a: board-melee-links | 127 looks, 50 with engaged models;  |
| PASS | pvp-a: dice-why-shown | 30 rolls shown, 11 with a why; drawn: 49 why lines, 84 outcome lines;  |
| PASS | pvp-a: log-action-expands | row 192 B4 FIGHTS A3: opened '  · Hit roll 8: HIT ✓ 8 (-1 DICE Defended obstacle (obj-5))\|  · Injury roll on A3: MINOR HIT 3 (4-1), +1 Blood Marker (Armour -1)\|  = HIT ✓ 8 → injury MINOR HIT 3 (4-1), +1 Blood Marker' (wanted '  · Hit roll 8: HIT ✓ 8 (-1 DICE Defended obstacle (obj-5))\|  · Injury roll on A3: MINOR HIT 3 (4-1), +1 Blood Marker (Armour -1)\|  = HIT ✓ 8 → injury MINOR HIT 3 (4-1), +1 Blood Marker'); closed again: True; nothing sent: True |
| PASS | pvp-a: fight-opens-after-charge | 7 accepted charges looked at, 0 where Fight being chosen on the charged enemy disagreed with the rules' Fight check |
| PASS | pvp-a: board-target-icons | 34 looks in target modes, 136 icons checked against the rules' sight and cover;  |
| PASS | pvp-a: board-fight-targets | 17 looks in Fight, 1 targets the rules accept (each ringed and tagged ENGAGED), 50 refused (grey and crossed);  |
| PASS | pvp-a: pvp-labels-show-blood | 126 labels checked, 6 with markers;  |
| PASS | pvp-a: pvp-nothing-to-send | actions: Nothing to send: the match is over. |
| PASS | pvp-a: pvp-event-log-tags | 200 rows, 200 lines, same order and words: True; numbered alike: True; verbs seen:  ACTIVATES, MOVES , ENDS ; newest: ▶ [201] MATCH ENDED: Two wins (VictoryPoints).; other: ; marked:  |
| PASS | pvp-a: log-outcome-marks | 200 rows: 15 ✓, 12 ✕, 173 unmarked; wrong:  |
| PASS | pvp-a: match-summary | VP by turn: turn 1: A +0, B +0 · turn 2: A +0, B +0 · turn 3: A +0, B +2 · turn 4: A +0, B +2; total A 2, B 5\|Glorious Deeds (+1 VP each): A Suicidal Bravery (A1) · B Resist and Bite (B2) · A Hold Your Ground\|Models at the end: Down A 1, B 0 · Out of Action A 1, B 0\|Rolls passed / failed (the server's dice): A 5 / 3, B 4 / 2; final VP A 2, B 5; Rematch shown: True |
| PASS | pvp-b: pvp-play-enter | Tab reached Button "Play", then Enter |
| PASS | pvp-b: pvp-join-online-enter | Tab reached Button "Online", then Enter |
| PASS | pvp-b: pvp-join-connect-enter | Tab reached Button "Connect", then Enter |
| PASS | pvp-b: pvp-type-server | LineEdit "ws://127.0.0.1:64517/match" = "ws://127.0.0.1:64517/match" |
| PASS | pvp-b: pvp-type-code | LineEdit "match code" = "23G2ZNJX" |
| PASS | pvp-b: pvp-join-enter | Tab reached Button "Join match", then Enter |
| PASS | pvp-b: pvp-join | Match code 23G2ZNJX. You are Seat Two [B]. |
| INFO | pvp-b: wait | WAIT pvp-wait timed out after 5s |
| PASS | pvp-b: board-move-glides | glides 1 -> 2; last (b1, (3, 18), (4, 18), 0.25); drawn at (4, 18); server (4, 18) |
| PASS | pvp-b: board-redraws-changed-parts | terrain drawn 1 -> 1 times; models not at the server's position: []; far enemy a3 unchanged: True, kept its nodes: True |
| PASS | pvp-b-rejoin: pvp-play-enter | Tab reached Button "Play", then Enter |
| PASS | pvp-b-rejoin: pvp-rejoin-online-enter | Tab reached Button "Online", then Enter |
| PASS | pvp-b-rejoin: pvp-rejoin-connect-enter | Tab reached Button "Connect", then Enter |
| PASS | pvp-b-rejoin: pvp-type-server | LineEdit "ws://127.0.0.1:64517/match" = "ws://127.0.0.1:64517/match" |
| PASS | pvp-b-rejoin: pvp-refresh-enter | Tab reached Button "Refresh saved seats", then Enter |
| PASS | pvp-b-rejoin: pvp-saved-seat-listed | Rejoin match 23G2ZNJX as Seat Two |
| PASS | pvp-b-rejoin: pvp-rejoin-saved-enter | Tab reached Button "Rejoin match 23G2ZNJX as Seat Two", then Enter |
| PASS | pvp-b-rejoin: pvp-rejoin-same-seat | Match code 23G2ZNJX. You are Seat Two [B]. |
| PASS | pvp-b-rejoin: pvp-rejoin-caught-up | Connection: Connected. \|\| 59 events after rejoining; events 1 to 59, each once and in order |
| PASS | pvp-b-rejoin: board-move-glides | glides 0 -> 1; last (b1, (4, 18), (8.5, 18), 0.25); drawn at (8.5, 18); server (8.5, 18) |
| PASS | pvp-b-rejoin: board-redraws-changed-parts | terrain drawn 1 -> 1 times; models not at the server's position: []; far enemy a3 unchanged: True, kept its nodes: True |
| PASS | pvp-b-rejoin: pvp-preview-dice | Fight: Hit roll: -1 DICE (Defended Obstacle, p. 45) \| Injury roll: no extra dice; +1 DICE on a critical hit; panel: B2 · Runner\|Seat Two [B] (yours) · Standing\|Engaged with: A4 (0.0")\|● Activating now\|Move 8.0" · Range 10.0" · Armour 0 · Melee weapon\|FIGHT → A4 · Banner Carrier\|PREVIEW · the server decides\|Rules (the server decides):\|Distance 0.0"; a Melee Attack needs the target within 1"\|Line of sight: partly visible, 3 of 9 sight rays clear, blocked by obj-5\|Target in cover (obj-5)\|Engaged: within 1" of the attacker\|Hit roll: -1 DICE (Defended Obstacle, p. 45)\|Injury roll: no extra dice; +1 DICE on a critical hit\|dice: rolled by the server\|✕ not valid: A2 (not within 1"), A1 (not within 1"), A3 (not within 1") |
| PASS | pvp-b-rejoin: pvp-plays-on-after-rejoin | 40 commands answered DONE after rejoining |
| PASS | pvp-b-rejoin: result-details | closed at first: True; top: YOU WON\|A 2 – B 5\|Glorious Deeds: A Suicidal Bravery · B Resist and Bite · A Hold Your Ground\|Victory points after the final turn (wanted YOU WON); Details shows the hash, Copy final hash and the summary: True (True, True, True, True) |
| PASS | pvp-b-rejoin: pvp-match-completed | Result: You won: victory points after the final turn.\|Final state hash: ea1ce3c257718c3d5c423eacf69b363a77279e4d11db50e03763497352262a04\|Events: 201\|VP by turn: turn 1: A +0, B +0 · turn 2: A +0, B +0 · turn 3: A +0, B +2 · turn 4: A +0, B +2; total A 2, B 5\|Glorious Deeds (+1 VP each): A Suicidal Bravery (A1) · B Resist and Bite (B2) · A Hold Your Ground\|Models at the end: Down A 1, B 0 · Out of Action A 1, B 0\|Rolls passed / failed (the server's dice): A 5 / 3, B 4 / 2\|Rematch: the window that created this match can start one and give you its code. \|\| [201] MATCH ENDED: Two wins (VictoryPoints). \|\| events 2 to 201, each once and in order |
| PASS | pvp-b-rejoin: event-effects | 8 events with effects (8 live, 0 caught up), 9 effects played; taken in twice: ;  |
| PASS | pvp-b-rejoin: pvp-dice-shown-once-as-logged | 27 rolls shown, 27 compared with the log's dice, 33 in the log; shown twice: ;  |
| PASS | pvp-b-rejoin: pvp-dice-beside-model-clear | 1 looks at a box beside its model;  |
| PASS | pvp-b-rejoin: pvp-dice-stacked | 134: 134, 135 in order / 193: 193, 194 in order |
| PASS | pvp-b-rejoin: pvp-labels-show-blood | 101 labels checked, 9 with markers;  |
| PASS | pvp-b-rejoin: pvp-nothing-to-send | actions: Nothing to send: the match is over. |
| PASS | pvp-b-rejoin: pvp-event-log-tags | 200 rows, 200 lines, same order and words: True; numbered alike: True; verbs seen:  ACTIVATES, MOVES , ENDS ; newest: ▶ [201] MATCH ENDED: Two wins (VictoryPoints).; other: ; marked:  |
| PASS | pvp-b-rejoin: log-outcome-marks | 200 rows: 15 ✓, 12 ✕, 173 unmarked; wrong:  |
| PASS | pvp-b-rejoin: match-summary | VP by turn: turn 1: A +0, B +0 · turn 2: A +0, B +0 · turn 3: A +0, B +2 · turn 4: A +0, B +2; total A 2, B 5\|Glorious Deeds (+1 VP each): A Suicidal Bravery (A1) · B Resist and Bite (B2) · A Hold Your Ground\|Models at the end: Down A 1, B 0 · Out of Action A 1, B 0\|Rolls passed / failed (the server's dice): A 5 / 3, B 4 / 2; final VP A 2, B 5; joined: rematch note shown: True |
| PASS | godot-pvp-a: engine errors | none in the output |
| PASS | godot-pvp-b: engine errors | none in the output |
| PASS | godot-pvp-b-rejoin: engine errors | none in the output |
| PASS | pvp-no-server-left | no Match.Server process uses this match's data folder |
| PASS | gvt-a-exit | Godot client exit code 0 (0 = all its checks passed) |
| PASS | gvt-cli-exit | trench-cli --bot exit code 0 |
| PASS | gvt-both-reach-match-ended | Godot: [156] MATCH ENDED: Two wins (VictoryPoints). \|\| trench-cli: [156] MATCH ENDED: Two wins (VictoryPoints). |
| PASS | gvt-outcome-agrees | Godot (Seat One): You lost: victory points after the final turn. \|\| trench-cli (Seat Two): [156] MATCH ENDED: Two wins (VictoryPoints). |
| PASS | gvt-same-final-hash | Godot: 469540ddd6c076935e9c352f30e0866dab1b5f4584c9ec402ad77ee9992e59c4 \|\| trench-cli: 469540ddd6c076935e9c352f30e0866dab1b5f4584c9ec402ad77ee9992e59c4 |
| PASS | gvt-same-event-count | Godot: 156 \|\| trench-cli: 156 |
| PASS | gvt-same-event-text | 133 events trench-cli printed are in Godot's list with the same number and text |
| INFO | gvt-commands | Godot (Seat One): Activate DONE 15, Charge DONE 1, Dash DONE 1, End DONE 12, Move DONE 10, Place DONE 4, Shoot DONE 2 |
| PASS | gvt-record-verifies-completed | trench-cli verify be83a120fbc419cbc1e7742246b50ccb.json --key signing.pub: exit 0: Completed: Completed: Seat Two won (VictoryPoints). Match be83a120fbc419cbc1e7742246b50ccb (code MUB7ZSMF, ranged-towers), written by build unknown under rules 1.0.2. Final hash 469540ddd6c076935e9c352f30e0866dab1b5f4584c9ec402ad77ee9992e59c4: hash matches. This proves the signing server wrote this record and that every event follows the rules from the recorded dice. It does not prove the dice were fair: the server that holds the key chose them. |
| PASS | gvt-record-hash-matches | record: 469540ddd6c076935e9c352f30e0866dab1b5f4584c9ec402ad77ee9992e59c4 \|\| Godot: 469540ddd6c076935e9c352f30e0866dab1b5f4584c9ec402ad77ee9992e59c4 \|\| trench-cli: 469540ddd6c076935e9c352f30e0866dab1b5f4584c9ec402ad77ee9992e59c4 |
| PASS | gvt-record-event-count-matches | record: 156 events \|\| Godot: 156 \|\| trench-cli: 156 |
| PASS | gvt-record-outcome-matches | record: Completed: Completed: Seat Two won (VictoryPoints). \|\| clients: [156] MATCH ENDED: Two wins (VictoryPoints). |
| INFO | gvt-run-time | 85 s from the server start to the record check |
| PASS | gvt-a: pvp-choose-scenario | OptionButton "ranged with towers (normal roster)" selected "ranged-towers" with Enter, Down, Enter |
| PASS | gvt-a: pvp-type-server | LineEdit "ws://127.0.0.1:50999/match" = "ws://127.0.0.1:50999/match" |
| PASS | gvt-a: pvp-create-enter | Tab reached Button "Create match", then Enter |
| PASS | gvt-a: pvp-create | Match code MUB7ZSMF. You are Seat One [A]. Give the code to your opponent. |
| PASS | gvt-a: trench-drawn | 6 trenches (trench-ne 1 part, trench-ne-2 1 part, trench-nw 1 part, trench-nw-2 1 part, trench-se 2 parts, trench-sw 2 parts), 15 ground boxes around them, 0 models in a trench; pits at 1" with the ground around them exactly |
| PASS | gvt-a: tower-drawn | towers tower-n 2" east+west,tower-s 2" east+west; drawn tower-n,tower-s with 4 ladders; marks shown: True,True |
| PASS | gvt-a: board-move-glides | glides 1 -> 2; last (a1, (33, 18), (28, 18), 0.25); drawn at (28, 18); server (28, 18) |
| PASS | gvt-a: board-animation-resilience | a1 drawn 3.00" off its spot; selected: nodes redrawn True, still 3.00" off, leaning 0.244 rad; hovered True: nodes redrawn False, still 3.00" off, leaning 0.244 rad; finished facing (-1.2075653, -1.2075655, 0) (wanted yaw -1.208), drawn again (-1.2075655, -1.2075655, 0): kept True; tracer and flinch drawn True; b2 kept its nodes True; terrain builds 1 -> 1; playing True; finished at once: 0.000" and 0.000" from the server's spots, still playing False; Off plays nothing True; playing right after PlayForCheck True, cut short by [], first look after 20 ms |
| PASS | gvt-a: board-casualty-resilience | a1 falling, tipped 0.52 rad; b1 rising, tipped 0.95 rad; casualty figures 1; selected: nodes redrawn True, still tipped 0.52 rad; a2 kept its nodes True; terrain builds 1 -> 1; playing True, cut short by []; finished at once: a1 (3.5643927E-06, 1), b1 (1.296266E-06, 1), upright True; casualty figures left 0, held standing 0, still playing False; half way: casualty figure (shade, base alpha) (0.7603049, 0.46734428), its model's own figure fade 1: True; Off plays nothing True |
| PASS | gvt-a: board-casualty-mid-move | a2 from (35.00, 18.00) halted at (32.00, 18.00); before the move: ((35, 0, 18), 2.0427924E-06) at the start True; half way: ((33.4982, 0, 18), 0.009211644), on the way True, where its glide draws the model ((33.4982, 18)) True; finished at once half way: none left and the model where the server has it True; half way through the fall: ((32, 0, 18), 0.38349473) at the halt True; travelled first 3 of 3; Off draws none True; terrain builds 1 -> 1; walked its move True |
| PASS | gvt-a: board-blast-effects | 85 effect squares showing (at most 96); grenade ((28, 0.95, 18), (7.692, 0.5, 25.719)) from a1 at (28.0, 18.0) to b1 at (7.7, 25.7): True; explosions drawn 3, last ((13.7844, 0, 23.4033), 3, MartyrBlast) (wanted MartyrBlast at (13.8", 23.4"), radius 3): True; marker bursts 3; blast rings 3, last ((13.7844, 0, 23.4033), 3): True; a2 kept its nodes True; terrain builds 1 -> 1; finished at once: hurried 96, clear after 197 ms, showing 0, still playing False; Off plays nothing True |
| PASS | gvt-a: board-fast-effects | hit: Full [0.16 0.70] s, clear 1.04 s; Fast [0.08 0.35] s, clear 0.53 s; miss: Full [0.16 0.90] s, clear 1.21 s; Fast [0.08 0.45] s, clear 0.63 s; blow: Full [0.15 0.70] s, clear 0.98 s; Fast [0.08 0.35] s, clear 0.52 s; charge: Full [0.50] s, clear 1.05 s; Fast [0.25] s, clear 0.53 s; Fast clears sooner True; a Fast hit finished at once with 1 effect squares showing clear in 0.07 s True; Off draws nothing True |
| PASS | gvt-a: board-redraws-changed-parts | terrain drawn 1 -> 1 times; models not at the server's position: []; far enemy b2 unchanged: True, kept its nodes: True |
| PASS | gvt-a: pvp-preview-dice | Shoot: Hit roll: -1 DICE (Long Range, p. 43) \| Injury roll: modifier +1; +1 DICE on a critical hit; panel: A1 · Sharpshooter\|Seat One [A] (yours) · Standing\|● Activating now\|Move 6.0" · Range 24.0" · Armour 0 · No melee weapon\|SHOOT → B4 · Lamp Keeper\|PREVIEW · the server decides\|Rules (the server decides):\|Distance 16.0", weapon range 24.0"\|Line of sight: partly visible, 6 of 9 sight rays clear, blocked by obj-5\|Target not in cover\|Long range: beyond half the weapon's range (1 penalty die)\|Hit roll: -1 DICE (Long Range, p. 43)\|Injury roll: modifier +1; +1 DICE on a critical hit\|dice: rolled by the server\|Guide: sight points seen, head 3 of 3, body 3 of 3, legs 0 of 3\|✕ not valid: B2 (not in sight), B3 (out of range) |
| PASS | gvt-a: result-details | closed at first: True; top: YOU LOST\|A 2 – B 10\|Victory points after the final turn (wanted YOU LOST); Details shows the hash, Copy final hash and the summary: True (True, True, True, True) |
| PASS | gvt-a: pvp-match-completed | Result: You lost: victory points after the final turn.\|Final state hash: 469540ddd6c076935e9c352f30e0866dab1b5f4584c9ec402ad77ee9992e59c4\|Events: 156\|VP by turn: turn 1: A +0, B +2 · turn 2: A +0, B +2 · turn 3: A +0, B +4 · turn 4: A +2, B +2; total A 2, B 10\|Models at the end: Down A 0, B 0 · Out of Action A 1, B 0\|Rolls passed / failed (the server's dice): A 1 / 3, B 4 / 2 \|\| [156] MATCH ENDED: Two wins (VictoryPoints). \|\| events 1 to 156, each once and in order |
| PASS | gvt-a: event-effects | 4 events with effects (4 live, 0 caught up), 8 effects played; taken in twice: ;  |
| PASS | gvt-a: board-frame-time | idle frames 3919 mean 0.15 ms max 23.95 ms; animating frames 872 mean 0.27 ms max 20.31 ms; updates 101 mean 10.68 ms max 169.33 ms; terrain builds 1 (0 while animating); models drawn 156 (70 while animating) |
| PASS | gvt-a: board-shot-sequence | 4 shots seen live, 4 tracer beats, 10 drawn (last ((13.677, 0.85, 23.655), (28, 0.7, 18), True)), 10 flinches; still playing: False |
| PASS | gvt-a: board-casualty-sequence | 1 status changes seen live; 0 falls, 0 rises, 1 going figures (5 freed); casualty figures left 0, held standing 0, still playing False |
| PASS | gvt-a: board-bodies | 1 models Out of Action, 1 bodies; bodies of models on the table []; drawn directly again 1 of 1 alike;  |
| PASS | gvt-a: pvp-dice-shown-once-as-logged | 16 rolls shown, 16 compared with the log's dice, 16 in the log; shown twice: ;  |
| PASS | gvt-a: pvp-dice-stacked | 79: 79, 80 in order / 106: 106, 107 in order |
| PASS | gvt-a: hud-on-battlefield | 14 looks at the HUD strip;  |
| PASS | gvt-a: hud-objectives-now | 14 looks at the HUD and the objective marks;  |
| PASS | gvt-a: pvp-panel-follows-state | 28 looks at the panel;  |
| PASS | gvt-a: panel-engaged-with | 28 looks, 0 at an engaged unit;  |
| PASS | gvt-a: pvp-labels-show-blood | 105 labels checked, 5 with markers;  |
| PASS | gvt-a: pvp-nothing-to-send | actions: Nothing to send: the match is over. |
| PASS | gvt-a: pvp-event-log-tags | 156 rows, 156 lines, same order and words: True; numbered alike: True; verbs seen:  ACTIVATES, MOVES , ENDS ; newest: ▶ [156] MATCH ENDED: Two wins (VictoryPoints).; other: ; marked:  |
| PASS | gvt-a: log-outcome-marks | 156 rows: 5 ✓, 6 ✕, 145 unmarked; wrong:  |
| PASS | gvt-a: match-summary | VP by turn: turn 1: A +0, B +2 · turn 2: A +0, B +2 · turn 3: A +0, B +4 · turn 4: A +2, B +2; total A 2, B 10\|Models at the end: Down A 0, B 0 · Out of Action A 1, B 0\|Rolls passed / failed (the server's dice): A 1 / 3, B 4 / 2; final VP A 2, B 10; Rematch shown: True |
| PASS | gvt-a: rematch-creates | code MUB7ZSMF -> KQ8ESPUH; a new match not started yet: True; seat One; old summary gone: True |
| PASS | godot-gvt-a: engine errors | none in the output |
| PASS | gvt-no-server-left | no Match.Server process uses this match's data folder |

439 passed, 2 failed.
