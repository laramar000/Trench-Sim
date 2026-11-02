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
- [ ] M7.5 stage 5d: a manual two-window online match, then Gate G5 sign-off. **Needs the owner.**

## History log

- [x] Each log row with a roll shows its dice under the row (`hotseat-log-draws-dice`, screenshot
  `05-hotseat-log-dice.png`). No overlap or clipping: judge the screenshot by eye.

## Combat menu keys

Each confirm bar, preview, narration and dice breakdown shows the right text (UI check group `abilities`, see below).

- [x] 4/5: second ranged and melee weapon (Off-Hand). 6: grenade.
- [x] 7/8/9: BLAST at a point; a scatter point is clicked anywhere.
- [x] 0: Grind to Dust (with Piston Legs) and Trample.
- [x] H: Treat (a Medi-kit model only). B: Detonate the Martyrdom Device.
- [x] I: Aim. V: Fortify. Y: Enforced Orthodoxy. Z: Laying on of Hands.

## Decision bars

- [x] A failed Risky roll with a Talisman: the Use / Keep prompt appears and each choice works (group `abilities`).
- [x] The Blood Marker spend prompt before the Success Roll and before the Injury Roll (group `abilities`).
- [x] A failed Morale Check: the flee-or-Shaken choice (group `abilities`).

## Roster builder and match readiness

- [x] "For a match" names what is refused; Binoculars show as not playable (`play-roster-pickers`, `roster-builder`).
- [x] A roster with the R6 abilities and upgrades plays in hot-seat (group `abilities`) and online
  (`online-roster-match`, unit tests `RosterOnlineTests`).
