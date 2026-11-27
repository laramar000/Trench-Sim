# Trench Sim automated tests (full)

**Result: PASS.** 6 PASS, 0 FAIL, 3 NOT RUN.

Every check this run was meant to do ran on this computer and passed. Checks listed under NOT RUN were not carried out by this run.

| | |
|---|---|
| Trench Sim version (commit) | f8ecc8e8d3002a3b2f34ed79406bcecd448189c5 (downloaded folder Trench-Sim-f8ecc8e8d3002a3b2f34ed79406bcecd448189c5) |
| Folder | <repo> |
| Computer | <computer>; Microsoft Windows 10.0.26200; .NET 10.0.13 |
| .NET SDK | 10.0.402 |
| Godot | <appdata>\TrenchSim\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe (remembered from an earlier run; reports 4.7.2.stable.mono.official.ed1daf0bf) |
| Started | 2026-11-26 22:10:17 |
| Ended | 2026-11-26 22:29:05 (18.8 minutes) |

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
| Windows UI checks (real Godot windows, full matches) | exit 0: Verdict: PASS (442 PASS, 0 FAIL, 18 NOT TESTED). Exit code 0. Summary: <repo>\test-results\20261126-221017-automated\ui-check\windows-20261126-221541\summary.md |

Logs: `<repo>\test-results\20261126-221017-automated\logs`
