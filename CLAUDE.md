# Hand-over for the next Claude session (Trenches / Trench-Sim)

Written 2026-10-31, last updated 2026-11-01, so a new session can carry on with no other history. Read this file, then `docs/` as needed.

## What this is

**Trenches** (repo name Trench-Sim): a personal-use digital tabletop for a skirmish wargame, two players, one
authoritative server, rules in inches. The game is called "Trenches" in the menu and in conversation. It is an
unofficial fan project (Credits say so); the repo is public, so no copied rule text, price tables or catalogues
beyond what the data files need, and setting-specific names are renamed (see Naming below).

Long-term aim: an XCOM-style tactical game, 3D battlefield with an angled camera. `Rules.Core` stays engine-independent
and server-authoritative: the client only shows the server's state and sends commands.

Working branch: **`m7-client`** (all current work). Older milestone branches m0-setup ... m6-records hold draft PRs
#1-#11, never merged; do not merge anything without the owner's explicit word.

## Layout

- `src/Rules.Core` rules engine (no Godot, networking or packages; tests enforce this). `src/Sim` console simulator.
- `src/Match.Protocol`, `src/Match.Server`, `src/Match.Client`, `src/Match.Cli` (`trench-cli`, incl. `verify` for
  signed match records).
- `src/Client.Core` client logic (no Godot): interaction, previews, narration, `RosterBuilder`.
- `client/` Godot 4.7.2 .NET project: `scripts/Main.cs` and partials (`MainMenu.cs`, `RosterScreen.cs`, ...),
  `scripts/UiCheck.cs` (automated UI checks driven inside real Godot windows).
- `src/Roster` + `content/roster/` roster builder: catalogue, keyword vocabulary, six faction lists (format in
  `docs/roster.md`).
- `tools/TestCenter` one-click test menu (Windows `Test Trench Sim.cmd`, Linux `./test-trench-sim.sh`);
  `tools/UiCheck` UI-check runner (Linux needs xvfb; Windows `tools/UiCheck/Run-WindowsUiCheck.ps1`).
- Docs: `docs/client.md` (every client feature with its tests), `docs/roster.md`, `docs/testing.md`,
  `docs/interpretations.md` (rules readings U1-U113), `docs/known-gaps.md`, `docs/line-of-sight.md`, `docs/maps.md`,
  `docs/protocol.md`, `docs/records.md`.

## How to test (cut to the essentials, owner's request 2026-10-31)

- Essential check before every push: `dotnet build TrenchSim.sln -c Debug -warnaserror` then `dotnet test TrenchSim.sln`
  (about 1,960 tests, a few minutes). Test Center option 1 / `dotnet run --project tools/TestCenter -- tests` does
  exactly this plus the authority checks.
- Run UI checks only for the screen you changed, e.g. the hot-seat group (includes the roster builder):
  `xvfb-run -a godot --rendering-driver opengl3 --path client -- --ui-check hotseat --out <dir> --scenario ranged`.
- The full run (Release + all UI checks, 30-75 minutes) is option 8 / `tests --full`: only when the owner asks or
  before handing over a release.
- Known flaky tests (sandbox network timing, unrelated to most changes): CancellingARun*, ARejoinWhile*,
  AnAborted*, ALocalServerThatCannotStart*, ATakeover*, DuplicateAndStale*, AConnectionThatNeverJoins*,
  AClientThatStopsReading, the hotseat-turn-banner timing UI check. Rerun just that test once.
- Godot UI-check gotchas: drivers click only spots no box covers (`UiCheck.Clickable`); `RemoveChild` before `QueueFree`
  when reusing node names; node names cannot contain `. : @ / " %`; UI-check code must not touch ImmutableArray.
- Client.Core must not use System.Random (use `Spread`); the Godot client must not reference System.Text.Json.
- Keep untracked Godot `*.import` and generated `*.cs.uid` files out of commits unless a change needs them.

## How the owner wants work done

- Inspect first, smallest fix, never claim a check that was not run. Report: Changes, Cause, Tests, Verification
  gaps, Commit.
- Push without asking once the essential check passes; stop and report if it fails. Never force-push, never rewrite
  remote history, verify with `git ls-remote` after pushing.
- Never merge PRs and never start a new milestone or roster stage without the owner's ask.
- Decisions: a short numbered list with one-line trade-offs and a recommendation, ending "Choose a number" (the owner
  answers on a phone).
- No screenshots in reports, no post-mortems unless asked, plain short replies.
- Bot/AI opponent deferred until asked. Keep hot-seat. Towers and vertical terrain belong in the default maps.
- Graphics are decorative only: never change rules, protocol, records, hashes, movement, LOS or base sizes for looks.

## Naming policy (standing)

Keep generic names (Rifle, Shotgun, Gas Mask, Trench Dog...). Rename distinctive setting-specific names (factions,
signature units, special kit) to original, recognisable alternatives; mechanics stay exact. The source-name mapping
is private to the owner and never goes in this repo. Faction display names: The Principality, Pilgrims, The Sultanate,
Heretics, The Cult, The Court. Rules version 1.0.2; no paid expansions; a fan companion app is a cross-check only.

## Where things stand (2026-11-01)

- Game: M0-M7.4 accepted; M7.5 stages 5a-5c accepted, 5d (manual two-window match) and Gate G5 not yet accepted.
  Main menu (Play, Build Your Own Roster, Credits) done. Battlefield perf and long-range line done.
- Play screen pages done (2026-10-31): one Play button -> Play page (Start hot-seat, Online); Online -> Host and
  Connect; Connect -> Join with a code and Rejoin saved seats. Lobby stays private by code only; keep the menu font.
  Not yet checked on Windows.
- Roster builder: stages 1-4 done. Stage 4 shipped the six factions with all variants (Court sins as seven
  "Dedicated to X" lists), then the gap fix: Trench Dog (1 Glory, Loyal Hound and one Special Training +1 each),
  many-armed models (`kitRules.arms`: Amalgam 6, Fallen Saint 3), kit bought from another faction (armoury `from`,
  kit groups, roster refs `kit@faction`). Not entered: Mercenaries (format has no hiring), Restraining Muzzle Glory
  extras, October Rules Review 2026 changes (lists follow the 1.0.2 books).
- **Roster stage 5a done (2026-10-31):** `src/Roster/MatchReadiness.cs` turns a legal roster into the engine's warband
  content and names what it refuses and what it plays without; the builder shows it under "For a match".
  **Stage 5b (hot-seat) done:** Play page "Seat One plays / Seat Two plays" picks saved rosters; the local server runs
  with `--warbands`. **Stage 5c (online) done:** Online page "You play"; CreateMatch/JoinMatch carry the roster text,
  the server checks it (Roster/RosterData.cs) and sets the match up when both are in (docs/protocol.md). Next roster
  work: playing what MatchReadiness leaves out, plan R1-R6 (private: roster-builder/rules-from-rosters-plan.md).
  **R1 dice done (2026-10-31, Jason chose "Dice first"):** model Ranged/Melee DICE and weapon DICE/INJURY DICE count
  in attacks (U97). **R2a roll keywords done** (Jason chose "Keywords next"): CRITICAL, IGNORE ARMOUR, ARMOUR
  PIERCING, SHOTGUN, RISKY, PISTOL, IGNORE COVER/LONG RANGE/ELEVATED POSITION (U98). **R2b done:** ASSAULT, HEAVY, RELOAD
  (U99, `Rules.Core/ActionLimits.cs`, `ActivationEnded(Reload)`). **HEAVY builder limit and R3 done** (Jason: "do 1 2
  and 3 all together"): second ranged/melee weapons (`ModelProfile.SecondRangedWeapon/SecondMeleeWeapon`, `Using`),
  Off-Hand, CLEAVE (`MatchState.AttacksLeft`), BLOCK (`ChargedThisTurn`), IMPERVIOUS (`ImperviousArmour`), U100;
  Combat menu keys 4/5. R4 grenades/area attacks is the third part of that ask. **R4a done:** one grenade per model
  (`ModelProfile.Grenade`, Shoot weapon 2, Combat menu key 6), SHRAPNEL/GAS/FIRE extra markers
  (`InjuryRolled.ExtraMarkers`), NEGATE (`ModelProfile.Negates`, DEMONIC = NEGATE FIRE, mines too), U101. **R4b BLAST and
  R4c SCATTER done** (U102, U103): `Weapon.Blast`/`RangedWeaponBlast`, `BlastHit`, `RollStage.Blast` with a queue of
  Injury Rolls; a missed SCATTER attack's point is the opponent's `ChooseScatterPoint` decision (`ScatterAttack`,
  `AttackScattered`, `ScatterMissed`, `Geometry.Blast`). That finishes Jason's "1 2 and 3" ask. **2026-11-01 Jason: "1 2 and 3 no mistakes" =
  R5 equipment, R6 unit abilities and the blast leftovers.** R5a done: DEADLY (U104, Injury Roll keeps 3 dice) and BLAST
  at a point (U105: `ShootPoint`, `PointAttackDeclared`/`PointSuccessRolled`/`PointBlastHit`, Combat menu keys 7/8/9,
  scatter click anywhere, `Interaction.ScatterAt`). R5b done: model traits (U106: `ModelTraits` FEAR, NEGATE FEAR, TOUGH,
  NEGATE HEAVY/STRONG, LEADER; `MeleeAttackDeclared.Fear`, `InjuryRolled/MineExploded.Tough`, `ModelState.ToughSpent`,
  LEADER +1 Morale DICE) and 8 equipment pieces (MatchReadiness `PlayedEquipment`); builder checks HELD. R5c done:
  AMMUNITION into its one gun, Holy Smoke/Zealot's Draught (`InjuryDiceTaken`), Grovelling Devotees (`MeleeInjuryBonus`), U107. R5d done: Pontiff's Crucifix (`KeepsGoingAfterRisky`, `Reducer.RiskyEnds`),
  Mountaineer Kit (`ClimbDice`), Musical Instrument (`Fanfare`, +1 Dash DICE within 4"), Cloak of the Mountain (`BlendsIn`,
  Cover -2), U108. R5e done: Urn of the Bitter Ashes (`SinisterWhispers`, -1 DICE to shots within 3"), Sacrificial
  Lamb (`WardedByBlood`, `InjuryRolled/MineExploded.Warded`, `ModelState.WardSpent`), U109; Jason chose "Any model" for
  the Urn. R5f done: COVER keyword (`HasCover` shields, `DigsIn` Shovels with `ModelState.DugInAt`; events name cover
  "COVER", `TurnSequence.CoverKeyword`), U110.
- The faction JSON files are the source of truth now. The earlier generator scripts held private source names and are
  not in the repo: edit `content/roster/*.json` directly, keep `docs/roster.md` and the tests (`tests/Roster.Tests`,
  `tests/Client.Tests/RosterBuilderTests.cs`) in step, and run the Roster tests (`dotnet test tests/Roster.Tests`).
- Windows: the owner tests on Windows with Test Center (install line in `docs/testing.md`). Last Windows results were
  at 17a9099/e917e6b; later commits are checked on Linux only.

## Current step (keep this section up to date at every push)

- 2026-11-01 15:45Z, Jason: "2 and 1 then 3 no mistakes only essential tests" = (2) new ACTION kit: Medi-kit Treat +
  Holy Water of Lalibela, Piston Legs (Grind to Dust), Martyrdom Device (Trigger); then (1) Blessed Icon and Unholy
  Trinket (a player "use it" choice); then (3) R6 unit abilities by faction. Testing: essentials only (build +
  `dotnet test TrenchSim.sln`), no UI checks. Keep this hand-over updated at every push.
- Done: R5h (U113): Medi-kit (`ModelProfile.Treats`) gives the Treat ACTION: command `TreatModel(seat, model, target,
  choice)` (`TreatChoice.RemoveMarker`/`StandUp`), events `TreatDeclared` and `TreatRolled`, `RollStage.TreatRoll`,
  `ActionsTaken.Treat`; Risky; the opponent may spend the model's markers. Holy Water (`HealingWaters`) +1 DICE.
  Client: key H, Treat only offered for a Medi-kit model; Bot treats first when it can. Tests `TreatTests` and three more.
  The "not playable yet" example in tests and the `play-roster-pickers` UI check is now Binoculars (UI check not run).
  Essentials 2,159/0.
- Pushed before: R5a e2ad5d8 ... R5f a93a518, gaps f787535 (U111), R5g 6305404 (U112).
- Decided: Urn of the Bitter Ashes protects any model within 3" (Jason card "1 Any model", U109).
- Next: Piston Legs (Grind to Dust: own ACTION, melee attack on a Down enemy within 1" with base <= 32 mm), then
  Martyrdom Device (once per game, Injury Rolls within 3"), then Blessed Icon/Unholy Trinket, then R6.
- Still refused by name in matches: AMMUNITION with two possible guns (no deployment choice yet), and every equipment
  piece not in `MatchReadiness.PlayedEquipment`. No Windows run since 17a9099/e917e6b.
