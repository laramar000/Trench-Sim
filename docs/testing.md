# Testing Trench Sim (no technical knowledge needed)

Everything is in one place: the **test center**. It builds the game, runs every check, starts the server and the game
windows, connects them, finds the match's record, verifies it and writes a report that says **PASS**, **FAIL** or
**NOT RUN** for each check. You never type a server address, copy a code by hand, look for a file or compare hashes.

## Get it (Windows, once)

Open **PowerShell** (Start menu, type `PowerShell`, press Enter), paste this line and press Enter:

```powershell
irm https://raw.githubusercontent.com/laramar000/Trench-Sim/m7-client/tools/TestCenter/Get-TrenchSim.ps1 | iex
```

It downloads the newest version into `%USERPROFILE%\TrenchSim\Trench-Sim-<version>`, offers to install the .NET 10 SDK if
it is missing, puts a **Test Trench Sim** shortcut on your desktop and opens the test center. Later, menu option 6 gets
the newest version the same way. (Already have a Trench Sim folder? Double-click **Test Trench Sim** in it.)

Godot 4.7.2 (.NET edition) is found by itself in your Downloads, Desktop or Documents folder. If it is not there, the test
center offers to download it, or you can drag the Godot program into its window. It remembers the choice.

## Use it

Double-click **Test Trench Sim**. A menu appears:

| Choice | What happens | What you do |
|---|---|---|
| **1 Run all automated tests** | Builds the game (Debug and Release, warnings count as errors), runs the whole test suite twice, the authority checks (the client never decides anything), then the UI checks for this computer: on Windows the existing Windows UI check, with Godot windows that play full matches by themselves. | Nothing. Do not use the mouse or keyboard while the Godot windows play. About 15 to 25 minutes. |
| **2 Play a test match** | Asks for ranged or melee and the side size (normal, or 1, 2 or 3 models a side for a quick test). The match is played on the map with the two climbable towers. Starts a local server and two game windows side by side. Window A creates the match and window B joins it, by themselves. When the match is over, it finds the server's signed record by the match's id and checks its signature, every event, the final state hash, the event count and the result against both windows. | Play both sides with the mouse. Once, close a window with its **X**: it is reopened and takes its seat back by itself. Click **Copy code** and **Copy final hash** when asked (the clipboard is checked, not your word). Answer two questions (mouse, readability). |
| **3 Check the Godot editor** | Opens the project in the Godot editor and watches what happens. | Press **Build**, then **Play**, look at the game window, close it and the editor. |
| **4 Check a finished match** | Verifies the record of your latest test or hot-seat match. | Press Enter. |
| **5 Clean up** | Stops anything a test left running. | Nothing. |
| **6 Get the newest version** | Downloads it and opens its test center. | Nothing. |

At the end of each run you see a large **RESULT: PASS / FAIL / INCOMPLETE** with every FAIL and NOT RUN line, and the full
report is saved as `test-results\<date>-<kind>\report.md` in the Trench Sim folder, with every program's output in its
`logs` folder. To report a result, send the report file (or paste the lines under RESULT).

- **PASS**: the check ran on this computer and passed.
- **FAIL**: the check ran and failed; the line says what was seen.
- **NOT RUN**: the check did not run here, with the reason (for example a Windows check on Linux, or a question you
  skipped). A NOT RUN check is never counted as passing. **INCOMPLETE** means a check this run was meant to do did not run.

## What it never does

- It never touches your own saved seats, game windows or servers: each test window keeps its saved seat in the test's
  own folder, and the report checks that your own saved seats are unchanged. Only programs whose command line names the
  test's folder are ever stopped.
- It does not change how the game plays. The windows connect through the same Create, Join and Rejoin requests as the
  buttons; the server decides everything, as always. A normal start of the game is unchanged.
- Nothing is left running: every program it starts is stopped at the end, also when you close the test center window
  (Windows stops them with it).

## What still needs a person

The test center asks for help only where a machine cannot judge: using the mouse, closing a window with its X, clicking
Copy code and Copy final hash (then the clipboard is checked automatically), whether the text is easy to read, and the
Godot editor's Build and Play buttons (then it checks that a build ran and that the game started).

## For developers

`dotnet run --project tools/TestCenter -- tests | match [<scenario>, default ranged-towers] | editor | check [<record>] | cleanup`, with
`--godot <path>`, `--no-questions` (nobody at the keyboard; questions become NOT RUN) and `--opengl`. On Linux,
`./test-trench-sim.sh`. Exit codes: 0 PASS, 1 FAIL, 2 INCOMPLETE, 3 could not start. `--client-args-a`/`--client-args-b`
add arguments to the test match's windows; the automated rehearsal of the test match uses them to let the UI check driver
(`--ui-check tm-a` / `tm-b`, client/scripts/UiCheck.cs) play in place of a person, closing window B once at turn 2.
The test match's windows are the normal client started with `--test-match create|join|rejoin --server <url> [--code <code>]
[--test-window A|B] [--test-report <file>]` (Client.Core/TestMatch.cs): they press Create, Join or the saved seat's
Rejoin for the player and write the end-of-match section to the report file.
