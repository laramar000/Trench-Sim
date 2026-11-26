# Trench Sim automated tests (full)

**Result: FAIL.** 5 PASS, 1 FAIL, 3 NOT RUN.

At least one check failed: see FAIL below. The logs folder holds every program's full output.

| | |
|---|---|
| Trench Sim version (commit) | def6105676b1e19342e828348bd0518cfdfd6182 (downloaded folder Trench-Sim-def6105676b1e19342e828348bd0518cfdfd6182) |
| Folder | <repo> |
| Computer | <computer>; Microsoft Windows 10.0.26200; .NET 10.0.13 |
| .NET SDK | 10.0.402 |
| Godot | <appdata>\TrenchSim\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe (remembered from an earlier run; reports 4.7.2.stable.mono.official.ed1daf0bf) |
| Started | 2026-11-26 09:49:21 |
| Ended | 2026-11-26 10:06:58 (17.6 minutes) |

## FAIL

| Check | Detail |
|---|---|
| Windows UI checks (real Godot windows, full matches) | exit 1: Verdict: FAIL (440 PASS, 4 FAIL, 16 NOT TESTED). Exit code 1. Summary: <repo>\test-results\20261126-094921-automated\ui-check\windows-20261126-095408\summary.md |

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
| Windows UI check detail: host-exit | host Godot exit code 1 (0 = all its checks passed) |
| Windows UI check detail: host: battlefield-readable | 7 models sampled; seats 0.12 apart, hue 152 degrees, least saturation 0.11; fog: False; shadows: False; problems: the seats' bodies are only 0.12 apart in colour and 152 degrees apart in hue (least saturation 0.11); a1 body 7c7567 ground 3a3a31 ratio 2.52 distance 0.41 rim 505764 \\| a2 body 636160 ground 3d3b32 ratio 1.83 distance 0.28 rim 53555b \\| a3 body 827b6e ground 4a4b4a ratio 2.09 distance 0.32 rim 5f6164 \\| a4 body 827a6d ground 3a3731 ratio 2.80 distance 0.45 rim 5b5e64 \\| b1 body 81796c ground 4a4b4a ratio 2.04 distance 0.31 rim 6a5953 \\| b2 body 80776b ground 393731 ratio 2.72 distance 0.44 rim 543a35 \\| b4 body 26250f ground 4a4b4a ratio 1.77 distance 0.31 rim 62534f |
| Windows UI check detail: pvp-a-exit | first Godot client exit code 1 (0 = all its checks passed) |
| Windows UI check detail: pvp-a: board-bodies | 1 models Out of Action, 1 bodies; bodies of models on the table []; drawn directly again 0 of 1 alike; b2 drawn directly: ((19.016, 0, 18), 1.6587067, 0.55, False, 0.35, True, (18.748009, 0.0749, 18.485481)) (was ((19.016, 0, 18), 1.5344238, 0.55, False, 1, True, (19.043648, 0.0749, 18.150118))) |

Logs: `<repo>\test-results\20261126-094921-automated\logs`
