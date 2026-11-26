# Trench Sim automated tests (full)

**Result: FAIL.** 5 PASS, 1 FAIL, 3 NOT RUN.

At least one check failed: see FAIL below. The logs folder holds every program's full output.

| | |
|---|---|
| Trench Sim version (commit) | 86289f49ce1fbadaf2e64b250a572ecc94ecd69a (downloaded folder Trench-Sim-86289f49ce1fbadaf2e64b250a572ecc94ecd69a) |
| Folder | <repo> |
| Computer | <computer>; Microsoft Windows 10.0.26200; .NET 10.0.13 |
| .NET SDK | 10.0.402 |
| Godot | <appdata>\TrenchSim\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe (remembered from an earlier run; reports 4.7.2.stable.mono.official.ed1daf0bf) |
| Started | 2026-11-26 11:35:29 |
| Ended | 2026-11-26 11:52:25 (16.9 minutes) |

## FAIL

| Check | Detail |
|---|---|
| Windows UI checks (real Godot windows, full matches) | exit 1: Verdict: FAIL (440 PASS, 2 FAIL, 18 NOT TESTED). Exit code 1. Summary: <repo>\test-results\20261126-113529-automated\ui-check\windows-20261126-114020\summary.md |

## NOT RUN

| Check | Detail |
|---|---|
| Linux UI checks (real Godot windows, full matches) | this computer runs Windows |
| Mouse, title-bar X, Copy code, Copy final hash, readability | need a person: menu option 2, Play a test match |
| Build and Play buttons in the Godot editor | need a person: menu option 3, Check the Godot editor |

## PASS

| Check | Detail |
|---|---|
| Build (Debug, warnings are errors) | exit 0, 0 warnings, 0 errors |
| Build (Release, warnings are errors) | exit 0, 0 warnings, 0 errors |
| Test suite (Debug) | 2,391 passed, 0 failed of 2,391 |
| Test suite (Release) | 2,391 passed, 0 failed of 2,391 |
| Authority checks (the client never resolves, rolls or applies anything itself) | 24 passed, 0 failed of 24 |

## INFO

| Check | Detail |
|---|---|
| Windows UI check detail: pvp-a-exit | first Godot client exit code 1 (0 = all its checks passed) |
| Windows UI check detail: pvp-a: board-bodies | 1 models Out of Action, 1 bodies; bodies of models on the table []; drawn directly again 1 of 1 alike; a4: ((19.194, 0, 18.399), 0.759171, 0.55, False, 1, True, (19.488813, 0.65552163, 18.119738)) (wanted at (19.19, 18.40), lying (tilt 1.2 to 2.6), shade 0.55, no base, a pool, fade 1) |

Logs: `<repo>\test-results\20261126-113529-automated\logs`
