# Test checklist (Windows)

The owner's checklist of 2026-11-02, with what checks each line. "UI check" means `tools/UiCheck` (Test Center option 8,
or `tools/UiCheck/Run-WindowsUiCheck.ps1`); its check ids are in `tools/UiCheck/expected-checks.txt`. Known flaky tests
are listed in `CLAUDE.md`: rerun a failing one once before reporting it.

## Setup

- [x] Clone the newest `m7-client`, run `git config core.autocrlf false`, then Test Center option 1: about 2,230 tests pass.
- [x] Open `client/` in Godot 4.7.2 .NET (UI check: `godot-launch`, `editor-*`).

## Menus and play flow

- [x] Main menu: Play, Build Your Own Roster, Credits (UI check `menu-navigation`).
- [x] Play, then Start hot-seat; Seat One and Seat Two pick saved rosters (`play-pages`, `play-roster-pickers`).
- [x] Play, then Online, then Host; Connect, then Join with a code; Rejoin saved seats (`pvp-*`, `play-pages`).
- [ ] M7.5 stage 5d: a manual two-window online match, then Gate G5 sign-off. **Needs the owner.** Steps in
  "Stage 5d: the hand-played match" below.

## History log

- [x] Each log row with a roll shows its dice under the row (`hotseat-log-draws-dice`, screenshot
  `05-hotseat-log-dice.png`). No overlap or clipping: judge the screenshot by eye.

## Combat menu keys

Each confirm bar, preview, narration and dice breakdown shows the right text (UI check group `abilities`, see below).

- [x] 4/5: second ranged and melee weapon (Off-Hand). 6: grenade.
- [x] 7/9: BLAST at a point. 8 cannot come up (no legal roster has a second BLAST ranged weapon). The scatter-point
  click is optional: it needs a missed BLAST.
- [x] 0: Grind to Dust (with Piston Legs) and Trample.
- [x] H: Treat (a Medi-kit model only). B: Detonate the Martyrdom Device.
- [x] I: Aim. V: Fortify. Y: Enforced Orthodoxy. Z: Laying on of Hands.

## Decision bars

- [x] A failed Risky roll with a Talisman: the Use / Keep prompt appears and each choice works (group `abilities`).
- [x] The Blood Marker spend prompt before the Success Roll and before the Injury Roll (group `abilities`).
- [x] A failed Morale Check: the flee-or-Shaken choice (group `abilities`; optional, it needs the dice to fail one).

## Roster builder and match readiness

- [x] "For a match" names what is refused; Binoculars show as not playable (`play-roster-pickers`, `roster-builder`).
- [x] A roster with the R6 abilities and upgrades plays in hot-seat (group `abilities`) and online
  (`online-roster-match`, unit tests `RosterOnlineTests`).

## Stage 5d: the hand-played match (owner, Windows)

The last M7.5 stage (docs/client.md, "M7.5"): one online match played by hand in two Godot windows on the same PC. It
covers the checks the automated stages left to it: the mouse, Build and Play from the editor, Copy final hash, closing
a window by its title bar, and reading the screen by eye. Gate G5 is signed off from what this run shows (its criteria
are in the private M7.5 plan).

- [ ] Open `client/` in Godot 4.7.2 .NET, press Build, then Play: the main menu opens with no error in the Output panel.
- [ ] Window 1: Play, Online, Host. Window 2 (a second Play from the editor, or the exported game): Play, Online,
  Connect, Join with the code window 1 shows. Both reach the deployment screen.
- [ ] Play the whole match with the mouse only: deploy by clicking the battlefield, choose units, Move, Dash, Shoot,
  Charge and Fight from the action bar, answer every decision card (Blood Markers, Morale) by clicking.
- [ ] Read by eye in both windows: labels, the unit card, the combat log with its dice, the decision cards, the turn
  banner. Nothing overlaps, clips or is too small at your usual window size.
- [ ] Animations (Menu, Animations: Full): shots, hits, charges, falls and explosions play in order and never hide a
  model for long; a model put Out of Action falls and stays as a darkened body with a pool of blood; switching to Fast
  and Off mid-match works (see "Animations" below for the close look).
- [ ] Mid-match, close window 2 by its title bar (the X), start it again, Play, Online, Connect, Rejoin the saved seat:
  it comes back to the same match with the same events and bodies, and play goes on.
- [ ] At MATCH ENDED both windows show the same result, final state hash and event count. Press **Copy final hash** in
  each and paste both somewhere: they are equal.
- [ ] Check the server's signed record for that match with `trench-cli verify <record.json>` (where records are kept
  and which key to trust: docs/records.md). It says Completed, with the same outcome, final hash and event count.
- [ ] Gate G5: sign off, or note what failed and give it back to Claude.

## Animations (owner, Windows): the showcase

The animation work since 2026-11-20 was checked on Linux with the simpler OpenGL renderer only. Test Center option 9
(Animation showcase) plays 12 staged scenes in a real window, each with a caption saying what to look for. F5 plays the
scene again, F6 the next, F7 the one before, F8 switches Full and Fast.

- [ ] Shots (scenes 1-3): a turn to the target, a muzzle flash, a tracer, a small red spatter on a hit (the target
  stays in sight), dust on a miss.
- [ ] Out of Action (scenes 2, 4): the model falls, its base fades away, and a darkened body stays lying where it fell
  with a pool of blood spreading under it.
- [ ] Mine (scene 4): the model walks in (its figure bobs a little each step) and falls where the mine stopped it.
- [ ] Charges (scenes 5, 6): a lean, a fast glide, impact dust, the blow and the fall; the carry-on slides on with no
  second impact.
- [ ] Explosions (scenes 7-9, 11): a short flash, small tongues of fire, sparks, thin smoke rolling out to the ring;
  every model stays visible through three overlapping blasts.
- [ ] Hot-seat switch (scene 10): the shot plays on across the switch, nothing cut short or shown twice.
- [ ] Down and up (scene 12): the largest model falls with a small bounce as it lands, lies a moment, then rises
  smoothly; nothing jumps.
- [ ] Anything that looks wrong on Windows: note the scene number and what you saw.

