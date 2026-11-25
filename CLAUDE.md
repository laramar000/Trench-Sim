# Hand-over for the next Claude session (Trenches / Trench-Sim)

Written 2026-10-31, last updated 2026-11-20, so a new session can carry on with no other history. Read this file, then `docs/` as needed.

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
  AClientThatStopsReading, AHostThatNeverAnswersTheRejoinTimesOutEachAttempt, the hotseat-turn-banner timing UI check.
  Rerun just that test once.
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
  2026-11-13: "sizes are all wrong scale properly"): `raw` x 0.70" for every human figure, Giant 1.6" and Mechanized 1.45" (the owner, 2026-11-13, after "bigger", "meet in the middle" and 1.55" for the Mechanized: "reduce size of both large non shrine units", smaller than the Walker and bigger than normal units; lore research: the Anchorite Shrine is 12 to 15 ft in the official lore and machine armour "far smaller", so the Walker stands 1.87 times the Mechanized),
  1.13 Walker (`client/assets/catalog.json`: every unit entry gives a height; before, human bodies ran from 0.56 to
  0.93). UI check `unit-figures` now expects each unit's catalog height, not 1.2. `psx_showcase.gd` draws figures at
  their catalog heights. Godot windows: open them on the second monitor (`--screen 1`), the owner plays on the first. `psx_showcase.gd`: `PSX_YAW=<degrees>` turns the
  figures. Budgets: Castigator and Trench Cleric 2,400.
- Done (2026-11-13): Heretic Trooper, the first Heretic figure (`heretic_trooper()` in `tools/art/make_figures.py`,
  `units/heretic-trooper.glb`, catalog `unit:Heretic Trooper` height 1.15", test row 2,000 triangles, 21 figures now). The
  owner sent Heretic references on 2026-11-13 (trooper: rust helm, gas mask and hose, rust chest plates and plaque,
  dark uniform, pale leg wraps, shawl, belt skull and red canister) and chose the Trooper as the next figure. Still to
  build for the Heretics: Heretic Priest, Night Commando, Chorister, Assault Beast, Wretched, Brand-Sworn Heavy
  Infantry, Bombard Witch, Barbed Wire Banshee (references for most are in that message, not in the repo).
  The art folder is 42.7 MB with it, so `AssetManifest.MaxTotalBytes` went from 40 to 64 MB (not asked of the owner first;
  each further painted figure adds about 1.8 MB, so the limit or the texture sizes need a decision before long).
- Done (2026-11-16): Heretic Priest, the owner's choice after the Trooper (`heretic_priest()`, `units/heretic-priest.glb`,
  class B 32 mm, catalog height 1.58", test row 2,800 triangles, 22 figures now, art folder about 45 MB of the 64 MB
  limit). Still to build for the Heretics: Night Commando, Chorister, Assault Beast, Wretched, Brand-Sworn Heavy
  Infantry, Bombard Witch, Barbed Wire Banshee.
  Reworked the same day: the owner said it "needs to look more heretical" and sent eight more Priest references. Now a
  ringed five-point star (point down) on the staff and on the coat's back, an inverted cross raised in the right hand
  and cut as the mitre's only opening, fire at the mitre's tip, a brass triangle with an eye and a padlock on the
  belt, fanged pauldrons, bloodied gauntlets, a black coat with painted flames. These are common occult signs, not the
  setting's own marks. 2,752 triangles.
- Done (2026-11-16): Brand-Sworn Heavy Infantry (`brand_sworn()`, `units/brand-sworn-heavy-infantry.glb`, class B
  32 mm, catalog height 1.58" with its raised axe, test row 2,300 triangles, 23 figures now, art folder about 45 MB of
  the 64 MB limit). The owner said "Yes, next figure" without naming one; this was Claude's recommendation. Still to
  build for the Heretics: Night Commando, Chorister, Assault Beast, Wretched, Bombard Witch, Barbed Wire Banshee.
- Done (2026-11-16): Heretic Trooper reworked on the owner's word ("do a full rework if needed") and twelve more
  references: helm with a peak and flared skirt, scarf and cape (seat colour), braces, taller plaque, thick pale leg
  wraps, a fuller body. Then, the owner: "needs to look more cool and edgy": a saw-backed blade raised and a drum
  pistol levelled (no rifle), near-black uniform, red-lit lenses, a spiked pauldron, a longer torn cape. Then, the
  owner: a black-coated shock trooper with burning eyes, "but WW1": a long black greatcoat, amber lenses, a blackened
  helm, hunched over the rifle in both hands again, the blade at the hip, no pistol. Catalog height 1.14" (was 1.15"),
  test row 2,800 (was 2,000), 2,721 triangles.
- Done (2026-11-16): glowing texels (the owner: "Yes, make them glow", the Trooper's lenses). A figure's painter returns
  four values for a texel that glows; the bake in `tools/art/make_figures.py` writes its alpha as 0.5;
  `client/shaders/psx_figure.gdshader` draws alpha under 0.75 at the texel's own colour, unlit and ungraded (`glow`).
  Only the Heretic Trooper marked texels at first; the Priest's embers and the Brand-Sworn's brands followed on
  2026-11-17. Not seen in a match, only in `psx_showcase.gd`.
- Done (2026-11-16): the Trooper's helm refitted (the owner: "make the helmet and the head look more natural
  together", "the proportions are weird"): built in the head's frame (`FACE_BOX`) and sized from the head, no longer
  level and twice the head's width. Catalog height 1.10" (was 1.14"). A helm on a tilted head belongs in the head's
  frame for every later figure.
- Done (2026-11-16): the Trooper's mask and cape (the owner: "Add more details to cape. Fix gas mask. Give a quality
  check"): solid eyepieces and muzzle, painted face piece and straps; cape with `ACCENT_PAINT` marks (new hook in
  `accent_shading`: a figure's factor on the Accent's baked grey) and a stitched linen patch. 2,981 triangles, test
  row 3,100.
  Quality check run on it (triangles, textures, alpha, bounds, Accent shading; `--ui-check dev` with
  `TRENCH_DEV=deploy,psx`: psx-shader PASS 44 of 44, 0 failures). Tracked art 47.0 MB of the 64 MB limit.
- Done (2026-11-17): the Trooper's open points fixed (the owner: "fix what needs work and keep psx inspiration"): the
  cape drapes over the greatcoat (`cloth_cone(..., extra=(coat,))`; the coat showed through the hem), the rifle is
  shorter so its tip stays inside the 25 mm base, the back patch moved clear of the cape's folded edge. 2,981 triangles, row unchanged. Pushed 1043046.
- Done (2026-11-17, the owner: "Go ahead. No mistakes"): glow on the Priest (mitre fire, staff star, mitre cross slit,
  charred-cross cracks, belt eye) and the Brand-Sworn (all brands, helm slots); painted coat flames stay paint. Both
  rebuilt with no other change (same triangles and bounds); checked in `psx_showcase.gd` against the old builds.
  Then the Priest's shoulders (the owner: "Shoulders on the priest need a quality check", then "the pauldrons look like
  whoopy cushions"): the squashed-sphere pauldrons at fixed points became three `psx_shell` plates a side along each
  posed upper arm (`JOINTS["upperarm01.*"]`), sized from the mantle's stand-off so it flares out below them; spike and
  fangs kept; mantle unchanged. 3,024 triangles, test row 2,800 -> 3,100 (class B allows 4,500). Pauldrons on a raised
  arm belong in the arm's frame for every later figure; a squashed sphere reads as a cushion.
- 2026-11-17, the owner: "Make sure their clothing sits naturally and correctly on their body no floating" (every
  figure) and "all the cloaks need to have more definition": `settle()` (before scaling) moves every loose piece that
  touches nothing onto its nearest neighbour (47 found on 23 figures, none left); `arm_plates()`/`plate_at()`/
  `plate_point()`/`plate_rim()` replace the sphere pauldrons of the Shield, War Prophet, Heretic Trooper and Brand-Sworn
  (rows raised: Shield 1,900, War Prophet 2,100, Brand-Sworn 2,400); the Accent has a 512 px grey texture
  (`accent_cloth()`, cloth parts use material `accentcloth`), multiplied with the seat colour in `Visuals.PaintAccent`.
  All 23 figures rebuilt. Not seen in a live match.
- 2026-11-17: Assault Beast built (`assault_beast()`, `units/assault-beast.glb`, catalog height 1.25", test row 2,800, 24 figures
  now), on the owner's references for mood only (the owner asked to copy the miniature's signature look; declined, an
  original design in the same mood). Chainsaw teeth and an all-fours pose on the owner's word. Approved by the owner
  ("Yes, next figure", 2026-11-17).
- 2026-11-17: Wretched built (`wretched()`, `units/wretched.glb`, catalog height 1.11", test row 2,900, 25 figures now), the
  owner's choice, on their references for mood only. Approved by the owner ("Yes, next figure", 2026-11-17). Still to
  build for the Heretics: Night Commando, Chorister, Bombard Witch, Barbed Wire Banshee (owner picks; references needed).
- 2026-11-17: Night Commando built (`night_commando()`, `units/night-commando.glb`, class B 32 mm, catalog height 1.4",
  test row 3,700, 26 figures now), the owner's choice, on their references. Reworked on the owner's word ("no eyes much
  bulkier and taller. get as close to references as possible"): eyeless helm, bulkier body, tall stance, thigh lames, long
  mail skirt, larger pauldrons (the miniature's own sculpt not copied). Then (the owner: "pauldrons look bad . remember to
  always do a quality check on the clothing being flush and fitting naturally on all models"): domed shoulder caps fitted
  by ray casting, the back cloth laid on the back; then the helm shortened to a rounded dome ("why is the head so long")
  and its neck curtain dropped. Standing rule: before showing any figure, check every piece of kit sits
  flush (floats check plus close renders of shoulders, waist and back from front, side, back). Approved by the owner
  ("Yes, next figure", 2026-11-17).
- 2026-11-17: Chorister built (`chorister()`, `units/chorister.glb`, class B 32 mm, catalog height 1.24", test row 3,300,
  27 figures now), the owner's choice, on their references for mood only. Shown to the owner for review. Still to build
  for the Heretics: Bombard Witch, Barbed Wire Banshee.
- 2026-11-20 (cloud session, branch `claude/adoring-hamilton-s5yohc`): animation plan approved, Jason chose "1" (an
  event's dice box, notes and sound come on its beat; the combat log stays immediate). Stages: A0 baseline timing, A1
  foundation + shot sequence, A2 facing/melee lunge/charge lean, A3 Down topple + Out of Action fade, A4 explosions and
  grenades, A5 feedback pulses. Done: A0 (`BoardTimes`, UI check `board-frame-time`) and A1 (`Client.Core/Choreography.cs`:
  each update's lines and beats in order, at most 2.5 s, Full/Fast/Off; `PlaybackQueue`: chains updates, TakeDue/TakeAll; `BoardView` "animation (stage A1)": due queue,
  glides measured from rest so they queue, flinches, pooled puffs and tracers, `FinishAnimations` on any send, seat change,
  Esc or catch-up; auto-end waits for playback; Menu "Animations", setting `anim=`; tests `ChoreographyTests`, `PlaybackQueueTests`, UI
  checks `board-shot-sequence`, `board-animation-resilience`). A0 also counts terrain builds and model draws.
  Cloud setup used: .NET 10 via dotnet-install.sh into ~/.dotnet (builds.dotnet.microsoft.com answered); the SessionStart
  hook installs Godot 4.4.1 without .NET, which cannot run this client: Godot 4.7.2 mono for Linux was downloaded from
  the GitHub release (SHA-512 checked) into the session scratchpad for the UI checks.
  Checks run here (Linux, xvfb, opengl3): essentials A0 2,241/0 and A1 2,251/0; full UI runner on A1 421 PASS, 0 FAIL;
  A0 vs A1 board-frame-time (gvt-a, same machine): idle frame mean 0.47/0.25 ms, animating 0.71/0.74 ms, update 19.2/16.4
  ms, terrain builds while animating 0 in both. Not run: Windows (owner's Test Center).
- 2026-11-20, Jason: "Proceed with A2: melee and facing animations" (only A2). Done: Turn/Lunge/Lean/Strike/Impact beats
  (`Choreography`, `Motion.Charging/Reached`); `BoardView`: `_facing` (kept across redraws, cleared on a new match),
  `_figures`, `Knock` (flinch + lunge), `FigureTurn`, `Lean`, `PlayStrike`; UI checks `board-animation-resilience` (turn,
  lean, hover) and optional `board-melee-sequence` (pvp-a). Next: A3 only when Jason asks.
  Checks (Linux, xvfb): essentials 2,253/0; full UI runner 425 PASS, 0 FAIL (board-melee-sequence: 3 blows, 8 lunges,
  5 charge impacts); gvt-a reruns after the check fixes 29/0 and 27/0. Animating frame mean 0.64-0.83 ms (A1 0.56-0.75);
  the 0.78-0.83 runs include the check's own hover while its made-up update plays (0.74 with that hover left out); one
  run had a single 277 ms stall in an animating frame, not seen again. Hover marks a model without redrawing it.
- 2026-11-20, Jason: "Proceed with A3: Down and Out of Action animations" (only A3). Done: `StatusTracker`/`LogLine.Changes`;
  Fall/Rise/Casualty beats; `BoardView`: `_held` (a falling model drawn standing until its fall ends), `FallTrack` +
  `Tipped` (figure tips backwards over its feet, settles over its base), casualty figures (`CasualtyFigure`, in `_effects`,
  freed when gone or on FinishAnimations); UI checks `board-casualty-resilience` (gvt-a), optional `board-casualty-sequence`
  (pvp-a, gvt-a). Next: A4 only when Jason asks.
  Checks (Linux, xvfb): essentials 2,255/0; full UI runner 426 PASS, 0 FAIL; gvt-a run 29/0 with board-casualty-sequence
  5 changes (2 falls, 2 rises, 1 going figure, none left); animating frame mean 0.63-0.71 ms, terrain builds while
  animating 0. The pvp melee match took no model Down, so board-casualty-sequence did not report there (optional).
- 2026-11-20, Jason: "Proceed with A4: Battlefield Effects" (only A4). Done: `BattleEffectsTracker`/`LogLine.Fx` (blasts from
  BlastHit/PointBlastHit/AttackScattered/Martyrdom/mines, grenade thrown, attack at a point, SHRAPNEL/GAS/FIRE bursts;
  `ResolutionLog.AttackAt`); beats Lob/Explosion/MineBlast/MartyrBlast/Scatter/Dust/Shrapnel/Gas/Fire; `BoardView` "animation
  stage A4" (pooled grenades, `Streak`, `PlayExplosion`, `PlayMarker`, `Run`/`StopEffects` so a skip stops every effect;
  MaxEffects 96); UI checks `board-blast-effects` (gvt-a), optional `board-blast-sequence` (pvp-a, gvt-a, abilities).
  Next: A5 only when Jason asks.
  Checks (Linux, xvfb): essentials 2,259/0; full UI runner 426 PASS, 0 FAIL (board-blast-effects; abilities'
  board-blast-sequence PASS: 2 explosions, 2 bursts, recorded in abilities.log; the Linux runner now also lists it in its
  summary, tools/UiCheck/Program.cs); gvt-a run 31/0; animating frame mean 0.63-0.67 ms, terrain builds while animating 0.
- 2026-11-21, Jason: "Proceed with A5: Animation Polish & Battlefield Feedback" (only A5; wait for approval after). Done:
  blast rings (`BoardView.BlastRing`) + `Choreography.BlastStagger`; see-through dust/smoke/gas (`SmokeAlpha`) and fewer
  squares when busy; moves turn along their way (`TravelPoint`); Off plays instant turns (`OffFacings`) so Full/Fast/Off
  end alike; a skip hurries effects (`HurrySeconds`, `StopEffects` speeds tweens up). Stage plan A0-A5 complete; next stage
  only when Jason approves.
  Checks (Linux, xvfb): essentials 2,261/0; full UI runner 430 PASS, 0 FAIL (board-blast-effects with rings, clear 207 ms
  after a skip; abilities board-blast-sequence 2 explosions, 4 bursts); gvt-a run 31/0. Animating frame mean 0.49-0.63 ms
  (A4 0.63-0.67), max 6.4 ms in the full run; one 15.3 ms frame in the separate gvt run, not seen again. Not verified: how
  it looks by eye, Windows.
- 2026-11-21, Jason chose "1" for PR #15 (claude/adoring-hamilton-s5yohc into m7-client, which had 52 newer commits: the
  art pipeline's figures): merged m7-client into the branch (no rebase). Conflicts: CLAUDE.md (both sides' notes kept, in
  date order) and `BoardView.DrawModel` (catalog height `figureTall` + A2 facing `FacingNow`). A3 adapted: falls, rises and
  casualty figures use each figure's catalog height (`StandingHeight`, `FallTrack.Height`, `Tipped(height)`); the casualty
  figure sinks 0.5" below the ground as it fades (`psx_figure.gdshader` writes no ALPHA, so the fade may not apply to it).
  Checks after the merge (Linux, xvfb): essentials 2,285/0 (m7-client's figure tests added 24); full UI runner 428 PASS,
  0 FAIL (unit-figures, board-casualty-resilience, board-blast-effects); animating frame mean 0.49 ms, max 6.1 ms.
- 2026-11-21, Jason: "proceed with A5: Animation Polish & Visual Verification" with the painted figures (A5 only; wait for
  approval after). Looked at on screen (Linux Compatibility renderer, xvfb; new dev step `anims`, see docs/client.md):
  fixed the fall-to-lying size jump (`LyingFit`), falls of base-limited figures ending off the base (tip about the feet
  with the drawn length, `Tipped(..., length)`), Out of Action figures not fading (new `fade` dither in
  `psx_figure.gdshader`, own material copies, real-looking base that fades), explosions and FIRE hiding models (smaller,
  see-through, rising). Not looked at: Windows/Vulkan, rises (same code reversed), live matches by eye.
  Checks: essentials 2,285/0; full UI runner 428 PASS, 0 FAIL, but pvp-a activation-ends-itself did not report (its one
  try found the model's Move and Dash unregistered, `MARK auto-end-not-finished`; untouched code, match-dependent); rerun
  (--skip-build) 425 PASS, 0 FAIL with it PASS, the four build steps not run there (PASS in the first run). Casualty
  figure half sunk: fade 0.50, its model's own figure 1. Animating frame mean 0.50-0.51 ms, max 6.4-6.7 ms (merge 0.49/6.1).
- 2026-11-21, Jason: animation audit (no code), then "Implement Stage 1 (B1)" only. Done: a change of the seat shown in
  hot-seat (following the turn or F2) no longer calls `FinishAnimations` (`BoardView.Show`); a new match finishes the
  last one's playback in `Main.Attach`; commands, Esc and catch-up unchanged. Hooks for the checks: `BoardView.SeatSwitches`,
  `FollowedTurn` (set by `Main._Process` when `HotSeat.FollowIfNeeded` switched). New UI checks `hotseat-switch-keeps-playback`
  (hotseat: a made-up shot playing when F2 switches the seat through HotSeat.Switch and Main's redraw plays all its beats;
  with the old finish put back it FAILs: 3 beats shown at once, cut by ShowTimed) and `abilities-switch-keeps-playback`
  (abilities: every switch following the turn while playing plays on, at least one with an action). The hotseat group's
  full match only ends activations (no attacks, 0 animating frames), so real switches are checked in the abilities group.
  Checks (Linux, xvfb): essentials 2,285/0; hotseat group 41 PASS, 0 FAIL; abilities 19 PASS, 1 NOT TESTED (scatter, dice),
  real switches there: 78 following the turn, 1 while playing (a charge's blow and fall), 0 cut short. Not seen: the race
  itself (one seat drawing an update a frame before the other gets it) never came up in these runs, a live hot-seat match
  by eye, Windows. Not run: the full UI runner, hotseat-record-verifies (runner only). Next: B2 only when Jason asks.
  Jason also asked for a "fast animations toggle in the settings": Settings already has "Animations: Full / Fast / Off"
  (the same control as the in-match Menu, saved as `anim=`); asked whether a separate toggle is wanted.
- 2026-11-21, Jason: "Implement Stage 2 (B2)" only. Done: a model put Out of Action during its own move (a live Mine
  Marker halts a Move, Dash, charge or resumed move) goes where the server halted it. The server clears an Out of Action
  model's position, so `StatusTracker` keeps each model's last place and `StatusChange.At` names it (after the event, or
  just before it for Out of Action: the halt from `Reducer.HaltAtMine`); `Choreography.Falls` puts it on the Casualty
  beat (`Beat.At`); `BoardView.StartCasualty` places the casualty figure there and, while the model's glide of the same
  update plays (it already ends at the halt, `Motions.Cut`), the figure rides it (`CasualtyFigure.Rides/Home`, posed
  from `_pose`/`_turned` in `PoseGhost`), then falls and dissolves as before. A move halted where the model stood has
  no glide; it falls there. Hooks: `CasualtyOf`, `CasualtiesRiding`, `LastCasualtyAt`. Tests `CasualtyPlaceTests`
  (bot-played Trench Warfare, 24 seeds: halts for Moves, Dashes, a charge and resumed moves, Full/Fast/Off, finishing
  at once mid-move, every other casualty at the server's last place), `StatusChangesTests` (At); UI check
  `board-casualty-mid-move` (gvt-a: made-up update from engine-shaped lines through `Choreography.Of`, stretched 20x:
  waits at the start, rides with the glide, falls at the halt, finish-at-once frees it, Off draws none).
  Also: B1's `abilities-switch-keeps-playback` is now optional (B2's full run had 81 switches, none while playing, so it
  failed with nothing cut short); a switch that cuts playback still fails; `hotseat-switch-keeps-playback` stays required.
  Checks (Linux, xvfb): essentials 2,288/0; full UI runner 432 PASS, 2 FAIL (that B1 check and abilities-exit, both
  from the no-chance case above; board-casualty-mid-move, board-casualty-resilience, hotseat-mines all PASS).
  After making it optional: abilities group rerun exit 0, 17 PASS, 2 NOT TESTED (dice), again no switch while playing.
  Performance (gvt-a board-frame-time): animating frame mean 0.55 ms, max 8.4 ms (A5 baseline 0.50-0.51 ms, max
  6.4-6.7 ms; earlier runs on this machine ranged 0.49-0.83 ms); terrain builds while animating 0. Not seen: a real
  mine casualty in a live window (dice), Windows. Next: only when Jason asks (R1, T1 and the rest not started).
- 2026-11-21, Jason: "Implement audit item R1" only. Done: every new match comes in through `Main.Attach` (hot-seat,
  host, join, rejoin of a saved seat, rematch; an in-match reconnect does not), which now calls `BoardView.NewMatch`:
  FinishAnimations as before, then clears `_lastSeen`, `LastDrawn` (new Client.Core class, was `_lastShapes`), `_facing`,
  the drawn table (`_scene = null`, so the first update takes the existing `first` path: facings afresh, camera fits the
  new table), `_lastSeq = -1` (the match's events so far are caught up, as for a first match), the selection,
  `_lastActive` and the hook's seat. Found while doing it: after Leave the old `_scene` stayed, so the new match's first
  Show copied the old models back into the caches and took "where models were" from the old table; `_facing` was only
  cleared when the new match's first screen had no battlefield. Hooks: `NewMatches`, `RememberedModels`,
  `RememberedShape`, `FacingCount`. Tests `LastDrawnTests` (ranged then melee match: within a match a model leaving the
  table keeps its last shape; after the clearing nothing of the first match is remembered, even under shared ids;
  without it the first match leaks). UI check `board-new-match-reset` (hotseat, in the online roster match after the
  hot-seat match): with the old behaviour put back it FAILs (all 8 hot-seat models still remembered); with the fix PASS.
  Checks (Linux, xvfb): essentials 2,291/0; hotseat group 42/0; full UI runner 435 PASS, 0 FAIL (incl. rematch-creates,
  pvp-b-rejoin, reopen, board-casualty-mid-move; abilities-switch-keeps-playback reported this time: 2 switches while
  playing, 0 cut short). Performance (gvt-a board-frame-time): animating mean 0.59 ms, max 6.1 ms (B2 run 0.55/8.4, A5
  0.50-0.51/6.4-6.7; range on this machine 0.49-0.83); terrain builds while animating 0. Gap: the roster match's table had
  no models placed yet when checked, so different positions across matches are covered by `LastDrawnTests` only; the
  camera now refits on every new match (the existing first-match path). Not run: Windows. Next: only when Jason asks.
- 2026-11-22, Jason: "Implement audit item T1" only. Done: in Fast, the effects drawn for a set length (muzzle flash, hit
  puff, miss dirt, blow sparks, charge impact dust: `BoardView.PlayEffect`/`PlayStrike` used fixed Full constants) now
  take `Choreography.EffectScale` (new: 1 in Full, exactly as before, also in updates sped up to fit 2.5 s; in Fast the
  beat's own shortening, half normally, floor `FastEffectFloor` 0.35; `FullSeconds(kind)` names the five kinds).
  Explosions, rings, bursts, tracers, lobs, scatter and dust already used the beat's scaled time; unchanged. Speed is
  `BoardView.Animation` when the effect comes due. Hook `EffectLengths`. Tests `FastEffectTimingTests` (Full unchanged
  incl. squeezed updates; Fast half, ends at half the time, overlaps what follows half as long; floor; Off none; other
  beats unscaled; finish-at-once in Fast). UI check `board-fast-effects` (gvt-a: hit, miss, blow, charge in Full then
  Fast through the battlefield: Full 0.16/0.70/0.90/0.15/0.50 s, Fast half; clear 1.00-1.25 s Full vs 0.56-0.62 s Fast;
  a Fast hit skipped with 2 squares showing clear in 0.06 s; Off draws nothing).
  Checks (Linux, xvfb): essentials 2,296/0; full UI runner 431/0, then (after tightening the skip step to wait for
  squares on screen) 433/0. Performance (gvt-a board-frame-time): animating mean 0.49 and 0.44 ms, max 5.9/5.8 ms (R1
  0.59/6.1, A5 0.50-0.51/6.4-6.7); terrain builds while animating 0. Not seen by eye; not run on Windows. Next: only when
  Jason asks (T2-T5 not started).
- 2026-11-22, Jason: "Implement audit item T2" only. Done: the animation code moved unchanged from `client/scripts/BoardView.cs`
  (5,592 -> 3,945 lines) into the partial `client/scripts/BoardView.Animation.cs` (1,659 lines): the A1 playback section,
  the G5 event effects, the A4 battlefield effects and the watchable-moves section with every pose track (ApplyPoses,
  SetPoses, casualty figures). Pure move, checked line by line against the old file (same lines, same order; only the new
  file's header added). Its generated `.cs.uid` is left untracked, as for the other partials (MainMenu.cs...).
  Checks (Linux, xvfb): essentials 2,296/0; full UI runner 431 PASS, 0 FAIL. Performance (gvt-a board-frame-time):
  animating mean 0.44 ms, max 6.7 ms (T1 0.44-0.49/5.8-5.9); terrain builds while animating 0. Not run: Windows.
  Next: only when Jason asks (T3-T5 not started).
- 2026-11-22, Jason: "Implement audit item T3" only. Done: one record of where models are drawn. `_lastSeen` (point and
  radius) is gone, the remembered `ModelShape` gives both; the per-update `before` dictionary of the previous table's
  positions is replaced by the previous table itself (`ShowNotes(log, BoardScene? before)`, one allocation less per
  update); "on the table, else as last drawn" is `LastDrawn.Find(table, id)`, used by `Where`, `StartFall` and
  `StartCasualty` (three hand-written versions before). `NewMatch` clears one cache; `RememberedModels` = `LastDrawn.Models`.
  Tests `LastDrawnTests` (+2: Find replays the old two caches scene by scene over 12 simulated matches and answers the
  same point, height, base and shape for every model, incl. ones just off the table; table first; cleared = new table
  only); B2 `CasualtyPlaceTests` and R1 `board-new-match-reset` unchanged and passing.
  Checks (Linux, xvfb): essentials 2,298/0; full UI runner 426 PASS, 4 FAIL (hotseat-turn-banner, a banner stayed 1.86 s,
  known flaky; host battlefield-readable, a3 on grey ground ratio 1.55, a pixel check that reads only the scene and the
  rendered picture; and the two windows' exit codes), rerun 436 PASS, 0 FAIL. Performance (gvt-a board-frame-time):
  animating mean 0.58/0.60 ms, max 6.6/7.8 ms (T2 0.44/6.7, range on this machine 0.44-0.83); terrain builds while
  animating 0. Not run: Windows. Next: only when Jason asks (T4, T5 not started).
- 2026-11-22, Jason: "Start t4 then immediately t5 after if everything checks out". T4 done: the 53 animation members
  only the UI checks read (counters, Last* records, PlayForCheck, NodeIdsOf, TiltOf, FacingOf, CasualtyLook, FigureFade,
  CasualtyOf, DrawnAt, SeatSwitch/SeatSwitches, EffectLengths, the dev-picture helpers, R1's read-outs...) moved unchanged
  with their comments into the partial `client/scripts/BoardView.AnimationChecks.cs` (225 lines;
  `BoardView.Animation.cs` 1,659 -> 1,439); counters are still counted up in place. B2's `LastCasualtyAt` (written, never
  read) removed. Members the board reads (Animation, Gliding, FinishAnimations, NewMatch, BodyAt, FollowedTurn,
  constants) stay. Checked line by line. Its generated `.cs.uid` left untracked (local exclude).
  Checks (Linux, xvfb): essentials 2,297/1 (AClientThatStopsReading, known flaky; rerun 3/3 PASS); full UI runner 432
  PASS, 0 FAIL. Performance (gvt-a board-frame-time): animating mean 2.02 ms with one 302 ms frame (and a 374 ms idle
  frame in the same run: the machine; about 0.7 ms without it); re-measured after T5 below.
  T5 done: `docs/art-pipeline.md` (section 6 checklist) names the shader uniforms the client sets or reads by name
  (`albedo_tex`, `albedo_colour`, `glow` 1.0, `fade` 1.0) and why `fade` (A5's casualty dissolve, ordered-dither discard)
  must stay. New `PsxShaderContractTests` (6: the four uniforms with types and defaults, the dither line reading fade,
  every uniform the client scripts set or read is declared; with `fade` taken out two fail). No game code or shader
  changed. Checks: essentials 2,304/0; full UI runner 431 PASS, 4 FAIL (all abilities: Detonate never offered in that
  match, so action-keys, action-previews, actions-narrated and abilities-exit; match-dependent), abilities group rerun
  18 PASS, 0 FAIL, 2 NOT TESTED (dice), Detonate ok. Performance re-measured on T4's code (gvt-a): animating mean
  0.56 ms, max 10.1 ms (idle max 403 ms again: the machine); terrain builds while animating 0. Audit items B1, B2, R1,
  T1-T5 all done. Not run: Windows. Next: only when Jason asks.
- 2026-11-22, Jason: final integration review of the animation system (A0-A5, B1-B2, R1, T1-T5). Two faults fixed:
  (1) `BoardView.ShowNotes` took a log behind what it had shown (`newest < _lastSeq`) as a catch-up: FinishAnimations and
  its place moved back, so when that log caught up the events in between were taken in again (dice, notes, sounds,
  animations repeated). Hot-seat seats have separate connections, so a switch can show a seat a moment behind; a log
  rebuilt after a reconnect (`Welcome ReplayFrom 0`) is the same. Since R1 a new match resets `_lastSeq` to -1, so a
  shorter log is now waited out (hook `LogsBehind`). (2) Choosing another Animations speed mid-playback left it playing at
  the old speed (Off kept animating); `Main`'s Animations choice now calls `FinishAnimations` when the speed changes.
  UI checks `hotseat-log-behind-waits` and `hotseat-speed-choice-skips` (hotseat; helper `MadeUpShot`); with the old
  behaviour put back both FAIL (cut by ShowNotes, 3 events shown again; Off kept playing).
  Reviewed without change (no fault found): event order and overlaps (PlaybackQueue, Choreography), moves/charges/
  casualties in one update (B2 riding, a casualty after a still-playing glide), Full/Fast/Off (T1 scale, OffFacings),
  skips and NewMatch, effect pool (a new match is seconds after Leave, past the 0.2 s hurry), server positions only.
  Noted, not changed: the UI-check lists (EffectLines, EffectsPlayed, StatusLines, FxLines, SeatSwitches) grow for a
  whole session, a few small tuples per event; an update of more than 20 events is shown as a catch-up (no animation),
  never seen in play.
  Checks (Linux, xvfb): essentials 2,304/0; full UI runner 438 PASS, 0 FAIL. Performance (gvt-a board-frame-time):
  animating mean 0.63 ms, max 10.2 ms (T5 run 0.56/10.1, range on this machine 0.44-0.83); terrain builds while
  animating 0. Not run: Windows; not looked at by eye.
- 2026-11-22, Jason chose "1" (animation showcase) to start validating the animations by eye. Done: `Client.Core/
  AnimationShowcase.cs` (11 staged scenes from engine-shaped log lines on the table's two closest models and the one
  nearest the target: shot Down, shot Out of Action, miss, mine mid-move, charge + blow, charge carry-on with its first leg
  in contact, grenade + gas, scatter, three overlapping blasts with fire/gas/shrapnel, hot-seat F2 mid-shot, Fast
  grenade); dev step `showcase` in UiCheck (captions; capture mode saves a picture every 0.12 s to <out>/showcase/NN,
  check `dev-showcase`; `TRENCH_SHOWCASE=watch` stays open: F5 again, F6 next, F7 previous, F8 Full/Fast); dev-only
  `BoardView.HoldAfterFall` (a staged Down stays lying; empty in play); Test Center option 9 / `showcase`
  (`tools/TestCenter/AnimationShowcase.cs`, docs/testing.md). Tests `AnimationShowcaseTests` (7).
  Looked at here (Linux, Compatibility renderer, xvfb), all 11 scenes frame by frame: the order and placing are right in
  every scene (falls stay lying, casualty dissolves with its base, the mine casualty falls where it halted, charge lean +
  impact + strike, grenade arc + ring + gas, scatter skim + landing, staggered blasts, F2 switch plays on). Two looks to
  decide (owner): the red hit puff and the pale strike flash are near-solid squares that hide the target for most of
  their time (0.15-0.7 s); stacked fireballs hide models about 0.3 s at their peak. Not changed.
  Checks: essentials 2,311/0; full UI runner 437 PASS, 0 FAIL; gvt-a animating mean 0.61 ms, max 11.4 ms. Not run:
  Windows/Vulkan (the showcase is for that: Test Center option 9).
- 2026-11-23, Jason chose "1": hits and blows see-through. The red hit spatter is four 0.18"->0.38" squares at
  `BoardView.HitAlpha` 0.5 flying apart (was four opaque squares grown to 0.7" from one point); the strike and impact
  flashes 0.2"->0.35"/0.3" at the same alpha (were opaque, to 0.65"); lengths, timing and T1's Fast scaling unchanged;
  muzzle flash and sparks unchanged; stacked fireballs not changed (option 2 not chosen). Hook `ShowingSquares`. UI check
  `hotseat-hits-see-through` (hotseat: made-up hit and blow in Full, every red/pale square every frame at most 0.55
  opaque and 0.4": seen 0.46 and 0.38"). Seen in the showcase capture: the target stays in sight through the hit.
  Checks: essentials 2,311/0; hotseat group 45 PASS, 0 FAIL (animating mean 0.58 ms); showcase 11/11. Full UI runner
  not run (look-only change; hotseat group covers it). Not run: Windows.
- 2026-11-23, Jason: explosion and fire effects "less like large opaque blobs". Cause found: `BoardView.Puff`'s material
  lacked `BillboardKeepScale`, so every effect square was drawn 1" across whatever its size (hits too: 44012e2's size cut
  never showed, only its opacity). Fixed, and explosions, smoke, gas, shrapnel and burning flames retuned in true inches
  (`BlastFlashAlpha` 0.6, `BlastFireAlpha` 0.5, `BlastSmokeAlpha` 0.4, `FireAlpha` 0.7 -> 0.6); lengths and timing
  unchanged. `ShowingSquares` also reports whether each square keeps its scale. New UI check `hotseat-blasts-see-through`.
  Checks (Linux, xvfb): essentials 2,311/0; hotseat group 46/0; gvt-a 32/0 (board-blast-effects 85 squares, cap 96;
  animating mean 0.56 ms, max 9.2 ms); showcase 11/11, scenes 4, 7, 9 looked at frame by frame. Not run: full runner,
  Windows/Vulkan. Every other effect (dust, muzzle flash, hits) is now drawn smaller, at its coded size; looked at in
  scenes 1 and 7 only.
- 2026-11-23, Jason: movement and casualty animations reviewed in the showcase. New dev settings `TRENCH_SHOWCASE_SLOW`
  (slow-motion capture, `BoardView.SlowMotion`, dev only) and `TRENCH_SHOWCASE_ONLY`; twelfth showcase scene (largest
  model Down then up). Fixed: a waiting rise held its model lying before the fall of the same update; falls land with a
  small rebound (`FallCurve`), rises ease in and out (`RiseCurve`); the turn into a fall takes its first half (was 30%);
  the casualty figure starts at the model's own facing (`CasualtyFigure.FromYaw`); gliding figures bob a little each step
  (`StepBob`). Lengths and timing unchanged. Checks (Linux, xvfb): essentials 2,311 + 1 new, 1 flaky transport test
  (`AHostThatNeverAnswersTheRejoinTimesOutEachAttempt`, passed 3/3 alone); hotseat group 46/0; gvt-a 33/0 (animating mean
  0.70 ms, max 10.1 ms); showcase 12/12. Not run: full runner, Windows/Vulkan.
- 2026-11-23, Jason: "I want bodies to lay on the ground not disappear". Done: Out of Action models stay as darkened bodies
  lying where they fell, without base or label, until the match ends; display only (never in the scene's models).
  `StatusTracker.Fallen` (last positions) -> `BoardScene.Of(..., fallen)` -> `BoardScene.Fallen`; `BoardView.Bodies.cs`
  (`SyncBodies`, `EndGhost`, `LayBody`, `BuildCasualty` shared with the casualty figure, which now darkens and drops its
  base instead of dissolving). Works in Full/Fast/Off, catch-up and rejoin. Tests `BodiesTests`; UI check optional
  `board-bodies` (pvp-a, gvt-a). Checks (Linux, xvfb): essentials 2,314/0; hotseat 46/0; gvt-a 33/0 (board-bodies PASS on
  a real casualty; animating mean 0.74 ms, max 12.1 ms); showcase scenes 2 and 4 looked at. Not run: full runner (pvp-a),
  Windows/Vulkan. Open: bodies pile up for the whole match (no limit; a body can overlap a living model standing there).
- 2026-11-23, Jason: "Do all" (after "What's next": 1 verify the animation branch, 2 body polish, 3 rules "later" group,
  4 last two Heretic figures, 5 milestone sign-off). Done: (2) bodies lie in a pool of blood (`BloodPool`, spreads over
  `PoolSeconds` 1.6 s) and are drawn see-through under a model on the table (`BodyUnderfoot` 0.35, `Underfoot`);
  `board-bodies` checks both. (5) `docs/manual-test-checklist.md`: steps for the stage 5d hand-played match / Gate G5 and
  for looking at the showcase on Windows. (3) blocked: the repo has only one-line summaries of the later-group rules
  (Bodyguard, Hide, Hateful, Court spells, INFECTION/BLESSING MARKERS, Puppet Master, Loudspeakers, Set/Defuse Mine,
  Time Slip, Temporal Assassin, Flanking/Forward Positions/INFILTRATOR, oval bases); the owner's pages are needed, as
  for Suicidal Bravery. (4) blocked: references for the Bombard Witch and Barbed Wire Banshee needed. (1) full UI runner
  first try stopped at 181 PASS, 2 FAIL (host too slow while the essentials ran beside it; reopen waited on it).
  Then: the blood pool was not showing (under the zones' 0.02" slabs, and its spread tween stopped by moving the body);
  fixed and looked at in the showcase. Full UI runner at 45ecaeb: 434 PASS, 2 FAIL (host battlefield-readable, a pixel
  heuristic on figure colours at turn 1 with no bodies, and host-exit from it); rerun (--skip-build) 437 PASS, 0 FAIL
  incl. board-bodies in pvp-a. After the pool fix: essentials 2,314/0; gvt-a 34/0 (board-bodies 2 bodies; animating
  mean 0.76 ms); hotseat 46/0. Not run: Windows.
- 2026-11-24, Jason chose option 3 of "shooting running climbing animations?" and said "Implement ragdoll". Built: the
  figure builder adds a 16-bone ragdoll skeleton with skin weights for figures in `RAGDOLL` (`rig_figure`, end of
  `tools/art/make_figures.py`; the joints come from `human_body`'s first body, `RIG_PTS`, through the same warps and
  scaling); `Client.Core/Ragdoll.cs` (deterministic point-and-stick solver, `RagdollTests`); `client/scripts/
  BoardView.Ragdoll.cs` (rig lookup, `Simulate`, `PoseRig`, `TorsoTilt`, `_fallPush`), used by the casualty figure and
  `LayBody`; figures without a skeleton tip over as before. Prototype figures: Rifleman and Skirmisher (the showcase's
  two Out of Action scenes), rebuilt painted with skeletons and looked at in the showcase (scenes 2 and 4); NOT
  committed (the owner's art rule: ask before commit): they are in the session scratchpad (`painted/`), the tree keeps
  the old files. Rebuild here: `uv venv` + `pip install bpy==5.1.2` (Python 3.13), `tools/art/.makehuman` fetched.
  Checks with the rigged prototypes in place (Linux, xvfb): essentials 2,321/0; gvt-a 33/0 (board-casualty-mid-move on
  the rigged Skirmisher; board-bodies PASS on 2 real bodies in the run before the last tilt change, not reported in the
  last run); hotseat 46/0. Next (owner): approve the prototypes, then the other figures one at a time (the Reliquary
  Walker has no human body: it keeps the tip-over). Not run: full runner, Windows.
- 2026-11-24, Jason: "I need the impact to influence how far they ragdoll. Seamless. No hesitation before they ragdoll".
  Done: `Client.Core/RagdollImpacts.cs` (each model's last hit per update: time, direction, strength, lift),
  `Ragdoll.Fall` throws by strength and lift (skid then stick); `BoardView.Play` keeps `_recentImpact`, a rigged
  casualty's fall starts on the hit (not the Injury Roll's beat) and plays at its own length; pool and underfoot follow
  where the body lies. Showcase scene 13. Checks with the rigged prototypes in place: essentials 2,327/0; showcase scenes
  2, 4, 13 looked at (fall on the hit, the blast's centre thrown about two bases). The prototype figures are still not
  committed (owner's approval).
- 2026-11-24, Jason: "Not just how far but the direction of the ragdoll in general". Done: `RagdollImpact.Hit` (where on
  the body: shot 0.65, blow 0.78, charge 0.45, blast 0.22, mine 0.05) and `Ragdoll.Fall(..., hit)` (push at that height,
  harder on the side facing the hit; the body turns and falls from it). Essentials 2,330/0; showcase scenes 2, 4, 13
  looked at (shot knocks it back and twists it, the mine sweeps the legs, the grenade flips it). Prototype figures still
  not committed.
- 2026-11-24, Jason: "Do what's best in whatever order is optimal no mistakes" (after the choice: 2 tone down the big
  throws, 1 approve the prototypes and rig the rest). Done so far: thrown bodies stop at other models' bases
  (`Ragdoll.Fall(..., obstacles)`, recorded per fall in `_fallObstacles`), reach at most about a body length; the figure
  builder rigs every figure with a body of its own (`RAGDOLL`: all but the Reliquary Walker) and prints `RIGCHECK` per bone
  (its middle within 0.08 of the body, owning body vertices); dev step `ragdolls` (lineup of every figure falling, check
  `dev-ragdolls`). Essentials 2,331/0. In progress: the 26 rigged figures building (scratchpad `rigged/`), to be compared
  with the old files (parts, triangles, bounds, skeleton), looked at, then committed with the test
  `UnitFiguresTests.AFigureWithABodyHasTheRagdollSkeleton` (kept aside until then).
  Then: the 26 rebuilt figures differ from the approved ones (the cloth simulation and decimation vary run to run: up
  to 0.15" on the Heretic Trooper's cape, a few triangles), so they were NOT used. Instead the skeleton was grafted onto
  the approved files: bone nodes from the rebuilds, skin weights computed on the approved meshes as `rig_figure` does,
  the approved binary kept byte for byte with the new data appended (script kept in the session scratchpad only:
  `graft.py`; for a future figure, build it with `RAGDOLL` and it is rigged directly). Checked: all 26 figures' parts,
  triangles and bounds exactly as before, 16 bones, the old data byte-identical; dev step `ragdolls` with close-ups
  (`<out>/ragdolls/close-*`): all 26 fall and lie, nothing torn or stretched, the Reliquary Walker unrigged; new test
  `UnitFiguresTests.AFigureWithABodyHasTheRagdollSkeleton`. Essentials 2,357 with 1 known flake (rerun 3/3). Figures
  folder about 59 MB of the 64 MB cap. Not run on Windows.
- 2026-11-25, Jason: ragdoll GIFs "good enough for now", then "Go to 3" (Down falls by ragdoll) and the goal "Complete
  Animation Suite" (ragdoll and terrain the main focus). Done (docs/client.md "Animation suite"): `RagdollGround` (terrain
  heights from TerrainDress parts, trench floors, lying bodies) in `Ragdoll.Fall(..., land)`: bodies stop against walls and
  towers, drape over sandbags, drop into trenches, fall off platforms; `Ragdoll.Down` + `Ragdoll.Rise` (Down falls by
  ragdoll over the base, drawn lying in that pose, `DownFall`; standing up from it); falls on a worker thread
  (`RagdollRig.Pending`); ACTION gestures from events (`ActionCue`; beats Brace, DigIn, Rally, Reach) and shooter `Recoil`;
  showcase scenes 14 (actions) and 15-19 (terrain; `--scenario ranged-towers`). Not covered: suppression, crawling,
  artillery, slopes (none in the rules or table); the Walker has no skeleton. hotseat-turn-banner failed 3 times here, also
  on the previous commit (machine timing).
  Full UI runner at 5ea7ddb: 436 PASS, 2 FAIL (board-casualty-mid-move: a fall due before its worker thread finished
  stood still for a frame; fixed, a due fall now waits for it; gvt-a rerun 0 failures, animating mean 0.78 ms, max
  11.6 ms). Essentials 2,379/0.

