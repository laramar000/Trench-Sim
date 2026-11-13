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
  `docs/interpretations.md` (rules readings U1-U117), `docs/known-gaps.md`, `docs/line-of-sight.md`, `docs/maps.md`,
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
- Windows: the owner tests on Windows with Test Center (install line in `docs/testing.md`). Last Windows results:
  2026-11-02 at a5f6cb5, full Windows UI check (Run-WindowsUiCheck.ps1) PASS: 406 PASS, 0 FAIL, 11 NOT TESTED (5 optional
  the match gave no chance for: board-send-shoot/charge/fight, charge-confirm-button, pvp-dice-beside-model-clear; 6 manual).

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
  Pushed 28cf852.
- Done: R5i (U114): Piston Legs (`GrindsToDust`) give the Grind to Dust ACTION: a `FightModel`/`MeleeAttackDeclared`
  with weapon 3 (`ModelProfile.GrindWeapon`, `ActionsTaken.Grind`), at a Down enemy within 1" on a base <= 32 mm, no
  Melee Weapon stats (`Using(Melee, 3)`); its own ACTION beside a Fight. Client: Combat menu key 0. Tests `GrindTests`
  and three more. Pushed ed2f05f.
- Done: R5j (U115): Martyrdom Device (the Ecclesiastic Prisoner's upgrade, `ModelProfile.Martyrdom`,
  `MatchReadiness.MartyrdomUpgrade`): command `TriggerMartyrdom`, event `MartyrdomTriggered(model, hit, close)`, then one
  `InjuryRolled` per hit model (bearer last, its roll 4D6 added together: `InjuryRoll.AllFourDice`), via
  `RollInProgress.Martyrdom`/`MartyrClose` and the blast queue; `ModelState.MartyrdomSpent`. Client: Detonate, key B.
  Tests `MartyrdomTests` and three more. Martyrdom Dog and Awaited not played. Pushed 013355b.
- Done: R5k (U116): Blessed Icon / Unholy Trinket = Talisman (`ModelProfile.Talismans` 0-2, `ModelState.TalismansUsed`):
  a failed Risky roll that would end the activation first asks the owner (`DecisionKind.UseTalisman`, helper
  `EndForRisky` in TurnSequence.Board); `UseTalisman` -> `TalismanUsed` (activation goes on, RELOAD still ends it),
  `DeclineTalisman` -> `ActivationEnded(RiskyFailure)`. Not asked with a Crucifix or when Down. Client: decision bar "Use
  Talisman"/"Keep Talisman". Tests `TalismanTests` and three more. Pushed 61dd615.
- Done: R6a (U117): New Antioch abilities played by name (`MatchReadiness.PlayedAbilities`): Absolute Faith
  (`ShotsIgnoreMarkers`), Expert Medic (+2 Treat DICE), Finish the Fallen (`Reducer.FinishDice`), Shock Charge
  (`ExtraChargeDie`, `ChargeRolled.Dropped`), Onward Christian Soldiers! (`InspiresFaithful`, `Board.Inspired`), Assault
  Drill/Battlefield Demolition (readiness drops HEAVY). New traits Principality/Cult/Demonic. Essentials 2,194/0.
- 2026-11-01: Windows clone at D:\Claude\Trench-Sim set up. It needs `git config core.autocrlf false` (CRLF checkouts
  break the golden-text and map-file tests); `.claude/settings.json` allows dotnet build/test/run. Essentials 2,194/0
  on Windows at d88ce8b.
- Done: R6b (U118): Aim and Fortify ACTIONs (Principality and Sultanate), one command `TakeSelfAction(seat, model,
  SelfActionKind)`, events `SelfActionDeclared`/`SelfActionRolled`, `RollStage.SelfRoll`, `ActionsTaken.Aim/Fortify`.
  Aim: +2 DICE on the roll, then `MatchState.Aiming` gives +2 DICE to its Ranged Attacks until the activation ends
  (`AttackDice.Shot(aim:)`, `DicePreview.AimDice`). Fortify: sets `ModelState.DugInAt`, so `Board.HasCover` no longer
  checks `DigsIn`. Client: action bar, keys I and V, Confirm aim/fortify, previews, log, notes, breakdown; Bot aims
  before shooting and fortifies first. Godot client compiles (`dotnet build client/Client.csproj`); no UI check run.
  Essentials 2,202/0 on Windows.
- Pushed R6b 6d53b77.
- 2026-11-01, Jason: "Do 1 - 4" then "No mistakes" = R6c, R6d, R6e, R6f in order, each pushed after the essentials.
- Done: R6c (U119): Agile, SwiftDash (Frightening Speed/Loping Dash/Mad Dash), QuickToRise (`ModelStoodUp.Quick`),
  Corpulent/Artificial Life (InjuryDiceTaken), UndeadFortitude/AbioticLife (`AttackDice.TakenDice`), Stealthy, HuntsTheTough
  (`AttackDice.HuntDice`), CounterCharge (`MatchState.CounterCharging`), MementoMori (`InjuryRolled/MineExploded.Spared`,
  reuses `ToughSpent`), Nailed Cross (readiness adds its kit). Units with no weapon (Amalgam, Plague Hound, Alchemical
  Lion, Assault Beast...) are still refused until R6d's unarmed attacks. Essentials 2,212/0. Pushed 2c4c5ab.
- Done: R6d (U120): readiness `NaturalWeapon` gives a weaponless model with Teeth & Claws / Feeble Flailing / Crushing
  Blows / Poison Stingers / Overwhelming Horde its Melee Weapon; `ModelProfile.Horde` (`Reducer.HordeDice`, +1 per friend
  within 3" on weapon 0); Trample ACTION = `ModelProfile.TrampleWeapon` 4, `Tramples`, `ActionsTaken.Trample`, shares
  Grind's code (`ModelProfile.IsActionAttack`), Combat key 0 (`Interaction.ZeroKeyWeapon`). Plague Hound and Pit Locust
  still refused (oval bases). Essentials 2,219/0. Pushed 0178220.
- Done: R6e (U121): `SelfActionKind.EnforcedOrthodoxy` (+1 DICE, Risky, stands Down friends within 8",
  `SelfActionRolled.StoodUp`) and `LayingOnHands` (plain Success Roll, `TakeSelfAction.Target`, removes 1 or 3 on a
  Critical, `SelfActionRolled.Removed`, Holy Water +1); `Reducer.SelfActionBonus/SelfActionRisky/OrthodoxyStands/HandsRefusal`;
  wire gained `OmitWhenEmpty` (only `SelfActionRolled.StoodUp`); `Words.SelfAction*` for all four kinds; keys Y and Z.
  Essentials 2,227/0.
- 2026-11-02, Jason (mid-R6e): "In the history log I want to visually see the dice for the rolls so there's no
  wondering" = next, before R6f. Done: `LogLine.Faces` + a settled `DieStrip` (scale 0.6) under each log row with a roll;
  not seen in Godot yet (no Godot on this Windows PC). Pushed R6e 592b378.
  Pushed log dice 664901a.
- Done: R6f (U122): the survey was wrong, the purchase options were already upgrades. Readiness `PlayedUpgrades` plays
  Legionnaire (Ranged/Melee +1), Zealot Strength (STRONG; Trench Pilgrim base 32 mm), Janissary Veteran (STRONG +
  Counter-Charge), Fearsome, Rapid Assault, Blind Rage (SwiftDash), Kavass (Melee +0). `RosterValidator` refuses
  `ExclusiveUpgrades` on one model and more Legionnaires than plain Troopers. Essentials 2,230/0. That ends Jason's
  "Do 1 - 4".
- Next (not started, owner to choose): the survey's "later" group (Court spells, INFECTION/BLESSING MARKERS, Bodyguard,
  moving other models, Set/Defuse Mine, Hide, Hateful, multi-Fight models, Time Slip, deployment rules), oval bases
  (Plague Hound, Pit Locust), and a Windows Godot run of the battlefield (keys I V Y Z, Trample on key 0).
- 2026-11-02: Godot 4.7.2 .NET installed (SHA-512 checked; now at D:\Claude\Godot, see the checklist note below).
  Hot-seat UI group run four times on Windows: 39/39 PASS with the new `hotseat-log-draws-dice` check and its
  screenshot `05-hotseat-log-dice.png`; one run had `tab-reaches-every-control-play-connect` FAIL once (passed on rerun,
  untouched code: treat as flaky). The new keys and Trample are not covered by any UI check; the online two-window run
  (BoardChecks) was not run.
- R6 survey (2026-11-01): 76 abilities not played yet. Proposed batches, smallest first (owner picks):
  R6c numbers on existing hooks: Dash +1 DICE (Frightening Speed, Loping Dash, Mad Dash, Agile's Dash/Climb part),
  INJURY DICE against it (Corpulent -2, Artificial Life, Undead Fortitude unless FIRE, Abiotic Life vs GAS), Stealth
  Generator (-1 DICE to shots at it), Mubarizun (+1 INJURY DICE vs TOUGH), Counter-Charge, Memento Mori (first Out of
  Action = No Effect), Nailed Cross (free Iron Capirote + Gas Mask). R6d unarmed attacks (no engine support yet): Teeth
  & Claws x2, Feeble Flailing, Crushing Blows, Poison Stingers, Overwhelming Horde, Trample ACTION x2 (like Grind).
  R6e Treat-like ACTIONs: Enforced Orthodoxy, Laying on of Hands. R6f builder purchase options (Legionnaires, Zealot
  Strength, Janissary Veteran, Trench Moles, Light Skirmishers, Plague Knight Ranks). Later, new systems: Court spells,
  INFECTION and BLESSING MARKERS, Bodyguard, moving other models (Puppet Master, Loudspeakers), Set/Defuse Mine, Hide,
  Hateful, multi-Fight models, Time Slip/Temporal Assassin, deployment rules. Not planned: campaign-only (Chattel,
  Resurrection, Law of Hell, Witch Battery limit). The engine has no Jump or Diving Charge.
- Pushed before: R5a e2ad5d8 ... R5f a93a518, gaps f787535 (U111), R5g 6305404 (U112), R5h 28cf852 (U113),
  R5i ed2f05f (U114), R5j 013355b (U115), R5k 61dd615 (U116).
- Decided: Urn of the Bitter Ashes protects any model within 3" (Jason card "1 Any model", U109).
- Still refused by name in matches: AMMUNITION with two possible guns (no deployment choice yet), and every equipment
  piece not in `MatchReadiness.PlayedEquipment`. No Windows run since 17a9099/e917e6b.
- 2026-11-02: owner's checklist (`docs/manual-test-checklist.md`). New UI check group `abilities` (UiCheck.cs
  `AbilitiesPlay`; in the full runner as step 4c, ids in expected-checks.txt; alone: `godot --path client -- --ui-check
  abilities --out <dir>`). Godot is now at D:\Claude\Godot\Godot_v4.7.2-stable_mono_win64 (the Downloads copy is gone;
  point Test Center at it once). Two hot-seat matches with R6 rosters (R6 Faith v R6 Iron, R6 Monastery v R6 Brass).
  Run abilities-14 (ui-check-results, not in git): failures=0, engine errors 0; action keys I V Y Z H B, previews,
  Treat (Medi-kit model moves next to a hurt friend first), Combat keys 4 5 6 7 9 0, narration, Talisman Use and Keep,
  Blood Markers before Success and Injury Rolls all PASS. Optional, dice-dependent: Morale (PASS in abilities-13, NOT
  TESTED in 14) and scatter (never came up). Key 8 is not checked: no legal roster has a second BLAST ranged weapon.
  The Godot client must not read `MatchState.Models` (ImmutableArray adds a System.Collections.Immutable reference and
  fails ClientAuthorityTest); use `board.Scene.Models` plus `state.FindModel`. Readiness no longer names
  purchase-option abilities (Zealot Strength, Janissary Veteran, Heretic Legionnaires, Light Skirmishers, Trench Moles,
  Plague Knight Ranks) as played without. Still the owner's: M7.5 stage 5d two-window match and Gate G5 sign-off.
- 2026-11-03: the cloud session (branch `claude/pensive-planck-ep84pj`, no .NET SDK there) settled rulings in a6d17e3
  (docs only: U31, U32, U40, R1, R2, R3, R7, Bloodbath); applied here on m7-client. That branch lacks m7-client's
  commits after 664901a; nothing merged. Built here: Bloodbath Rolls (U123; `SpendMarkers.Bloodbath`,
  `MarkerSpendPurpose.Bloodbath`, `InjuryRolled.Bloodbath`, all left off the wire when false; tests
  `Match/BloodbathTests`, `BloodbathWireTests`; CLI `bloodbath <n>`). R3 now notes LEADER +1 DICE on Morale (already
  played). Not built, owner needed: Suicidal Bravery (the deed's text is not in the repo, and the optional charge
  continuation, U47, is not built) and Field Shrine (DEPLOYABLE equipment: the engine has no deployed objects; part of
  the "later" deployment-rules group). Owner also: allow builds.dotnet.microsoft.com in the cloud environment, the merge
  plan for PRs #1-#11 and `docs-known-gaps`, and which roster work comes next.
  UI runs after Bloodbath: abilities-15/16/17 (failures 0, 2, 0). Run 16's two FAILs: Laying on of Hands never had a
  target with a BLOOD MARKER (dice); fixed: its model now moves next to a hurt friend like Treat, and it is the optional
  check `abilities-lay-hands` when no target ever comes up. No run offered a Bloodbath Roll (needs 6 markers, or 3 on a
  Down target), so its button and log words are checked by unit tests only. Run 15 stalled ~200 s on one injury
  marker decision ("Spend 4" clicked, no answer, then answered by the hint fallback); not seen in 16 or 17. UiCheck now
  writes `MARK decision-unanswered ...` with LAST COMMAND when that happens: read it if it comes back.
- 2026-11-03, later: owner pasted p. 37 (charge carry-on) and p. 153 (Suicidal Bravery) and chose "player chooses".
  Built U124: after a charge that ends in contact, if sliding round the target (contact ring, 2.8125-degree steps) can
  end within 1" of another enemy, the charging player gets `DecisionKind.CarryOnCharge` (`CarryOnCharge` /
  `StopCharge`; events `ChargeCarriedOn`, `ChargeStopped`; RollStage `ChargeCarryOn` keeps the legs in DashPath).
  Suicidal Bravery (Scenario I) completes when a successful charge ends within 1" of a second enemy. ReplayCompatibility
  2 -> 3; golden records regenerated (TRENCH_SIM_UPDATE_GOLDEN=1); melee seeds 1, 5, 15, 23 re-pinned. Tests
  `Match/ChargeCarryOnTests`. UI run abilities-18: failures=0, but no carry-on came up (its bar is unit-tested only). Still blocked: Field Shrine (no deployables).
- 2026-11-03: owner said "Merge the plans". m7-client (which holds m0-setup to m6-records, PRs #1-#11) took in
  docs-known-gaps, main (#13, #14: SessionStart hooks; .claude/settings.json now has both the dotnet permissions and
  the hooks) and claude/pensive-planck-ep84pj; essentials passed; main was fast-forwarded to it (no force-push).
- 2026-11-03: art pipeline. `docs/art-pipeline.md` is the owner-approved standard (style, blank mannequin faces, size
  classes A-F by base with triangle and texture budgets, modules as separate glTF nodes, workflow, checklist, all 55
  units with bases). Template: Trench Pilgrim (`client/assets/units/trench-pilgrim.glb`, 1,842 triangles; tall
  capirote with no head under it, gaunt MakeHuman minmuscle-minweight body, seat colour on mantle and capirote band),
  catalog `unit:Trench Pilgrim`; source `tools/art/blend/trench-pilgrim.blend`; renders `tools/art/render_turnaround.py`.
  Build: Python 3.13 (winget) with `bpy==5.1.2`, `python tools/art/make_figures.py --figure <name> <units> <makehuman>`
  (FIG_BLEND=<folder> saves the .blend). The four older figures were not rebuilt (the shared head code changed).
  Owner's rules for art: one model at a time, shown for approval before the next; ask before commit. Open: per-entry
  figure height in the catalog (large bases, tall headgear), oval bases; licence warning given (TC community licence
  excludes video games and AI art); the owner chose to go on.
- 2026-11-03: second figure, Stigmatic Nun (`units/stigmatic-nun.glb`, 1,847 triangles, approved "good enough"), built
  on the owner's reference images as cues (white blood-spattered habit and hood, porcelain votive mask with a cross slit
  chosen from three mock-ups, `NUN_FACE` keeps visor/plate/sallet; fitted plate corset by shrinkwrap; serrated blade and
  pistol gripped in the fist via `FISTS`). New script pieces: `spattered()` baked blood/mud material, female body
  shapes (`shape="female-young-..."`), `head=None`. Owner reviews each figure from turnaround and close-ups.
- 2026-11-03: third figure, Ecclesiastic Prisoner (`units/ecclesiastic-prisoner.glb`, 1,795 triangles, approved), on
  the owner's reference cues: stooped gaunt body, enclosed spiked iron helm (no head), stocks board, finned bomb on the
  back. The bomb and chest ropes are placed by ray-casting the posed body (`back_y`), so they rest on it. The 25 mm
  pilgrims are done; the War Prophet, Castigator (32 mm), Sanctified Giant (40 mm) and Reliquary Walker (60 mm) wait for
  the per-entry figure height in the client catalog.
- 2026-11-03: fourth figure, War Prophet (`units/war-prophet.glb`, class B 32 mm, 2,066 triangles, catalog height 1.55",
  approved). One-piece pointed helm (built from rings, no ledge), cross frame with horns and bells, anti-tank hammer,
  seat colour on the pauldrons' top lames and tabards. Feet now flat on the ground for every figure (shared foot aim);
  trench-pilgrim (thinner, taller capirote), stigmatic-nun and ecclesiastic-prisoner were rebuilt with it. Figure tests
  take each figure's budget by class; `BODY_BUDGET` sets a larger class's body budget in the script. Left: Castigator
  (32 mm), Sanctified Giant (40 mm), Reliquary Walker (60 mm).
- 2026-11-03: fifth figure, Castigator (`units/castigator.glb`, class B 32 mm, catalog height 1.5", 3,053 triangles:
  the owner raised its budget to 3,100 for a detailed gas mask; `UnitFiguresTests` lists budgets per figure, file size
  limit scales with budget). Gas mask: sealed iron eyepieces (no glass, no rivets: rivets made them read as eyes),
  mouthpiece angled down, harness straps cut away from the face; no head under it. Left: Sanctified Giant (40 mm),
  Reliquary Walker (60 mm).
- 2026-11-03: sixth figure, Reliquary Walker (`units/reliquary-walker.glb`, class E 60 mm, catalog height 2.4", 7,635
  triangles; the owner raised its budget to 6,000 then 8,000). The owner's references were the shrine walker (Catherine
  wheel, bonebreaker mace), which in the roster is the Reliquary Walker, not the Sanctified Giant. Hard-surface hull, no
  head (tabernacle and spire), seat colour on pauldron tops and the spire band; the martyr on the wheel is a posed
  MakeHuman body (`human_body` called inside the figure, moved to Weapon2, then transformed onto the wheel). New script
  globals: `TEX_SIZES` (texture size by module; the Walker has 256 px body and Weapon2). `UnitFiguresTests` rows now give
  budget, largest texture and footprint width. `render_turnaround.py` takes an optional height. Left: Sanctified Giant
  (40 mm), references still to come from the owner.
- 2026-11-03: seventh figure, Sanctified Giant (`units/sanctified-giant.glb`, class C 40 mm, catalog height 1.6", 5,043
  triangles, budget 5,200: the owner allows larger units more). Owner's references were the Communicant: striding,
  hunched brute, hood with a nailed iron cross (no face, no head), polearm with a chain/bell/skull cluster, skirt split
  for the striding leg. The hood sits on the skeleton's neck joint (`NECK`, set by `human_body`).
- 2026-11-03: cloth pass on every Pilgrim figure (the owner: "all of the cloth we've done looks terrible"): `drape()`
  (Blender cloth sim, pinned edge, self-collision, inside-only smoothing, reduced, solidify outward without a rim),
  `cloth_cone`, `cloth_panel`. Each figure stays within its budget (War Prophet body 1,150, Castigator body 1,000 to fit).
- 2026-11-03: Trench Pilgrim rebuilt as a WWI trench soldier (owner's references): tunic, webbing, breeches,
  puttees, iron capirote base; the robe, tabard and red mantle are gone (seat colour only on the capirote band).
  The four older generic figures were not rebuilt. All Pilgrim figures are done.
- 2026-11-03: Principality started (owner chose it next). First figure Yeoman (`units/yeoman.glb`, class A, 2,399
  triangles, budget 2,400 for the gas mask): shared `ww1_uniform()`/`ww1_webbing()` (also used by the Trench Pilgrim),
  `ww1_gas_mask()` on the owner's WWI mask references (fogged blank lenses, chin drum, no hose), seat-colour steel
  helmet. The Castigator's mask is `gas_mask()` (shared, unchanged). Left: Lieutenant, Blind Marksman, Trench Cleric,
  Shock Trooper, Combat Engineer, Mechanized Heavy Infantry (40 mm), Combat Medic, Holy Warrior.
- 2026-11-03: Shock Trooper done (`units/shock-trooper.glb`, class A, 2,698 triangles, budget 2,700). Owner wanted it
  distinct from the Yeoman: dark grey `assault` kit, knee pads, tall boots, deep seat-colour helmet with an iron brow
  plate, two-lame chest plate, shoulder plates, spiked trench hammer in a wind-up over the right shoulder.
  `ww1_uniform(tunic=, band=)` takes materials now. Left: Lieutenant, Blind Marksman, Trench Cleric, Combat Engineer,
  Mechanized Heavy Infantry (40 mm), Combat Medic, Holy Warrior.
- 2026-11-03: Lieutenant done (`units/lieutenant.glb`, class B 32 mm, catalog height 1.5", 3,395 triangles, budget 3,400),
  on the owner's references: green greatcoat, breastplate with medals, Sam Browne, puttees, curved sword on the shoulder,
  fist on hip, large oval shield with a patriarchal cross, blank mannequin face (no gas mask). New shared pieces:
  `sallet(closed=)` (lofted round the head; closed = eye slit, head removed, for the Yeoman and later; open = visor
  swung up resting on top, head shrunk), `boots()` (lofted laced boots in their own `Boots` node, centred on the leg,
  body feet removed). Bake: faces covering no texel centre take their material's colour (no black blots). Owner: it is
  fine to remove hidden body parts up front to stop clipping. Left: Blind Marksman, Trench Cleric, Combat Engineer,
  Mechanized Heavy Infantry (40 mm), Combat Medic, Holy Warrior.
- 2026-11-03: Blind Marksman done (`units/blind-marksman.glb`, class B 32 mm, catalog height 1.25", 2,779 triangles,
  budget 2,800), on the owner's sniper references: crouched on one knee at rest, a very long rifle held upright with
  its butt grounded and the muzzle above the hood (brass scope, folded bipod), lofted deep hood (no point), red
  blindfold on a blank face, cloak pinned at the neck (the back under it removed), seat-colour stole, webbing and
  pouches. Kneeling: `ww1_uniform(bands=False)` and plain booted feet (`boots()` fits standing feet only). Left: Trench
  Cleric, Combat Engineer, Mechanized Heavy Infantry (40 mm), Combat Medic, Holy Warrior.
- 2026-11-04: Trench Cleric done (`units/trench-cleric.glb`, class B 32 mm, catalog height 1.65", 3,978 triangles, budget
  4,000: the owner raised it), on the owner's references: tall flat-topped seat-colour cap over a blank mannequin face, olive
  cuirass of lames front and back (`plate()` shrinkwrap, cut behind the sides), boxy pauldrons with seat-colour slabs and
  plated sleeves, gilt shrine on the left shoulder, rope stole, belt pouches, ray-fitted chain fringe, long pale vestment with
  prayer strips, open book (Weapon2 node), pistol, a great riveted cross on the back. Left: Combat Engineer, Mechanized
  Heavy Infantry (40 mm), Combat Medic, Holy Warrior.
- 2026-11-10: art style change. The owner wants every figure in a PS2-era painted-texture look (clothes, straps, plates and
  grime painted into a 256 px body texture on a smooth low-poly body; solid shapes only for silhouette: headgear, weapons,
  packs, pauldrons, big hulls). All 17 figures were converted (`PAINT` bake hook, shared helpers `psx_*`, `ww1_paint`,
  `toeless_feet`, `KIT`, `JOINTS` in `tools/art/make_figures.py`), budgets in `UnitFiguresTests` lowered to the new counts.
  Combat Engineer done and registered (`units/combat-engineer.glb`, 2,923 triangles). PSX look in the game:
  `client/shaders/psx_figure.gdshader` (vertex snap, affine textures, colour steps, grimdark grade), applied in
  `Visuals.UseNearestFilter`; seat colours drawn on figures are darkened (`BoardView.Grimdark`). Test it before any new figure:
  `TRENCH_DEV=deploy,psx godot --path client -- --ui-check dev` and `tools/art/psx_showcase.gd`.
- 2026-11-10: the last three Principality figures done in the painted style and registered (not yet shown to the owner,
  not committed): Mechanized Heavy Infantry (`units/mechanized-heavy-infantry.glb`, class C 40 mm, height 1.6", 3,934
  triangles: riveted plate, bucket helm in a gorget, back engine with two exhaust stacks and hoses, water-cooled heavy
  machine gun, seat colour on the pauldron tops), Combat Medic (`units/combat-medic.glb`, class A 25 mm as the roster
  gives, 1,283 triangles: peaked cap in the seat colour, greatcoat, armband with a patriarchal cross, vial bandolier,
  medi-kit bag, medical chest, pistol) and Holy Warrior (`units/holy-warrior.glb`, class B 32 mm, height 1.55", 2,422
  triangles: sugar-loaf great helm with a brass cross, gilt cross on the cuirass, tabard and pauldron tops in the seat
  colour, two-handed sword at vigil). New shared helpers `psx_shell` and `hose`. Left: none (20 figures).
- 2026-11-12: detail pass on all 20 figures (the owner: "1 more passover for detail", "highest quality possible"). The bake
  in `tools/art/make_figures.py` now packs each UV layout tight (`uv.pack_islands`, the default left half a texture
  empty), paints every texel as the mean of 3 x 3 samples (thin painted lines no longer break into dashes) and adds grain
  in `psx_lit`; every Body, Coat and Robe texture is 1,024 px, the other modules 512 px (`UnitFiguresTests` rows 1,024).
  The Accent carries baked grey shading in its vertex colours (`accent_shading`, exported as COLOR_0); the client
  multiplies it with the seat colour (`BoardView`: `VertexColorUseAsAlbedo`, still unshaded). Standard-bearer: crossbar,
  pennant and lantern hang outward from his right, the pennant waved (budget 1,700); Combat Medic: apron folded (1,600).
  A build takes 3 to 10 minutes a figure; `psx_showcase.gd` also takes full paths to .glb files. The units folder is
  about 40 MB. Renders (not in git): `ui-check-results/s5/` (`g1`..`g5` all figures, `before`, `after`).
- 2026-11-12/13: dynamic poses on all 20 figures (the owner: "make all of their posing more dynamic and less static").
  New in `tools/art/make_figures.py`: `legs(r, L=(x, y, toe), R=..., drop=, heel=)` (stance by inverse kinematics, the
  ground rises to the feet: `GROUND`; grounded kit adds it), `TWIST` and `LOOK` (shoulders and head turned, a warp of the
  finished figure, `dynamic_warp`), `WARP` (a figure's own warp: Holy Warrior sword into a high guard, Reliquary Walker
  stride; its mace no longer hangs below the feet), `FIG_FAST=1` (unpainted build in seconds). The `FIG` line prints
  `raw=` (metres before scaling) and `feet=` (must be 0). Poses: Rifleman doubling forward, Skirmisher stalking in a
  crouch, Shield braced with the mace back, Standard striding and looking back, Trench Pilgrim charging, Nun striding
  with the pistol levelled, Prisoner staggering, War Prophet preaching with a hand raised, Castigator flail swung back,
  Walker lumbering stride, Giant twisted, Yeoman and Shock Trooper charging, Lieutenant sword thrust up and forward,
  Blind Marksman head turned (still kneeling), Cleric book high and pistol levelled, Engineer crouched assault,
  Mechanized wide braced stance, Medic running, Holy Warrior high guard. Catalog heights on one true scale (the owner,
  2026-11-13: "sizes are all wrong scale properly"): `raw` x 0.70" for every human figure, Mechanized and Giant both 1.85" (the owner: "bigger", then "meet in the middle"),
  1.13 Walker (`client/assets/catalog.json`: every unit entry gives a height; before, human bodies ran from 0.56 to
  0.93). UI check `unit-figures` now expects each unit's catalog height, not 1.2. `psx_showcase.gd` draws figures at
  their catalog heights. Godot windows: open them on the second monitor (`--screen 1`), the owner plays on the first. `psx_showcase.gd`: `PSX_YAW=<degrees>` turns the
  figures. Budgets: Castigator and Trench Cleric 2,400.
