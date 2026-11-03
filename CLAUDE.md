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

## Map size and battlefield dimensions (standing, owner 2026-11-03)

The battlefield must accurately reflect the recommended Trench Crusade tabletop dimensions. Before implementing the charge
carry-on or Field Shrine:

1. Research the recommended official battlefield dimensions for Trench Crusade 1.0.2.
2. Identify the standard recommended map size and any alternative sizes for different game formats or model counts.
3. Verify against official sources; do not rely on assumptions or community conventions when official guidance exists.
4. Compare the recommended dimensions with the current Trench Sim battlefield.
5. Document discrepancies and propose the changes before implementing (`docs/map-dimensions.md`).

Implementation requirements:

- The in-game battlefield accurately represents the recommended tabletop dimensions; one in-game inch is one tabletop inch.
- Deployment zones, terrain placement, movement, shooting ranges, charge distances and map boundaries use the correct dimensions.
- The camera and zoom fit the full recommended battlefield without distorting its proportions.
- Never silently resize or stretch existing maps; review existing and generated maps for compatibility.
- If several official sizes exist, say which is the default and how alternatives work.

Validation: tests of the battlefield dimensions and the measurement scale; deployment boundaries and map edges; existing
geometry and movement calculations unchanged; an in-game screenshot of the full battlefield at the right proportions.

Do not change battlefield dimensions until the research and the proposed implementation have been reviewed and approved.

Approved 2026-11-03: 36" by 36" is the default table and 48" by 48" the alternative (`docs/map-dimensions.md` section 4a). Dimension
tests exist; 48" table sizes are accepted per scenario (U125, `MapNeeds.TableSizes`; no 48" map shipped); the Scenario IV zone depth
and the camera check are still open.

Carry-on past contact is built (U126: `ChargeModel.CarryOn`, `ChargeDeclared.CarryOn`, `RollInProgress.CarryOn`, planner
`ChargePlanner.CarryOn`; `Interaction.ToggleCarryOn`; CLI `charge A1 B1 carry`). K in Charge mode toggles it in the Godot client (compiled, not run in Godot). Field Shrine stage 1 is built (U127: `FieldShrines`, `ShrineState`, `PlaceShrine`/`DeclineShrine`, `Relation.Impassable`, Morale
bonus); stage 2 (attacks and blasts remove it) and stage 3 (client piece, click placement) are not. All
of this work is local commits on `claude/pensive-planck-ep84pj` after `d9ac75a`; nothing pushed until the owner approves.

Decisions kept for the carry-on and Field Shrine work: A1 and A2 recommended; B1 to B4 recommended; carry-on first, then
Field Shrine in three stages; full build and tests after each stage; **no pushing until the owner has reviewed and approved**.

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
- 2026-11-03, Jason: "do it all no mistakes" = Suicidal Bravery, Field Shrine, Bloodbath Rolls. Done: Suicidal Bravery (U123,
  `GloriousDeed.SuicidalBravery`, `Deeds.Track` on `ChargeMoved`; only a stop at first contact already within 1" of a
  second enemy counts, the carry-on past contact is not planned). Melee seeds 1 and 23 re-pinned. Bloodbath Rolls
  done (U124: `SpendMarkers.Bloodbath`, `MarkersSpent` purpose `Bloodbath`, `InjuryRolled.Bloodbath`,
  `RollInProgress.Bloodbath`, `Reducer.BloodbathCost`; client bar "Bloodbath (N)"; CLI `markers 6 bloodbath`; no pin changed).
  Essentials 2,243/0 (one transport test is flaky, passed on rerun). Field Shrine and the Suicidal Bravery carry-on past
  contact not started: each needs a design the owner has not seen (see the chat report of 2026-11-03).
- Next (not started, owner to choose): the survey's "later" group (Court spells, INFECTION/BLESSING MARKERS, Bodyguard,
  moving other models, Set/Defuse Mine, Hide, Hateful, multi-Fight models, Time Slip, deployment rules), oval bases
  (Plague Hound, Pit Locust), and a Windows Godot run to check the UI (log dice, keys I V Y Z, Trample on key 0).
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
