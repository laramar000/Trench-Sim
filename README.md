# Trench-Sim

A personal-use digital tabletop for a skirmish wargame, played with friends: two players, one authoritative server, rules measured in inches.

This is an unofficial fan project, not affiliated with or endorsed by any game publisher. The source code is public; no game builds are distributed.

## Status

Milestone M4, Slice A (a simulated full match, ranged combat only: placement, Move, Dash, Shoot, Blood Marker spending, injuries, objectives and victory, with a console simulator). Slice A is a checkpoint, not the finished milestone: Charge, Retreat, Fight and shooting into melee come in Slice B. The project follows a plan of nine milestones, M0 to M8. Each milestone is finished and verified before the next starts.

## Prerequisites

- .NET SDK 10 (`dotnet --version` should print 10.x)
- Git

## Verify

From the repository root:

```sh
dotnet --version
dotnet test
```

`dotnet test` should report every test passing (444 at M4 Slice A), including four checks that the rules engine uses no Godot, networking, UI or other framework, package or project (its project file, imported build files, restored packages and compiled references).

To play one simulated match, or twenty, to the end:

```sh
dotnet run --project src/Sim -- --seed 42
dotnet run --project src/Sim -- --seeds 1-20
```

Each line shows the outcome, Victory Points and counts; the program exits with an error if a match fails to finish.

## Layout

```text
TrenchSim.sln
src/Rules.Core/      rules engine: no Godot, no networking, no game-specific names
src/Sim/             console simulator: two bots play whole matches; reads content/*.json
content/             original test warbands (placeholder content)
tests/Rules.Tests/   xUnit tests for the rules engine
docs/rules-notes.md  where each rule lives in the 1.0.2 rulebook
docs/interpretations.md  rulebook-confirmed requirements versus software interpretations
docs/known-gaps.md   what Slice A leaves out
```

## Repository rules

- No publisher-owned artwork, logos, extracted game assets or copied proprietary code.
- No pasted rulebook text. `docs/rules-notes.md` gives section references; it also holds summaries in our own words of each rule implemented so far.
- Original placeholder content (names, units, equipment, descriptions, art) wherever practical.
- The repository is public, so everything committed here is published.

## License

The original code and original documentation in this repository are licensed under the [Apache License 2.0](LICENSE).

That license covers only material written for this project. It does not cover, and does not grant any rights in, the game, its rules text, artwork, logos, names, setting or other intellectual property of any game publisher or other third party, which remain subject to their owners' rights. It gives no permission to distribute a complete playable client of that game or its protected content, and this repository contains none. This project is unofficial and unaffiliated.

The test project uses the third-party packages Microsoft.NET.Test.Sdk, xunit and xunit.runner.visualstudio, which keep their own licenses; they are restored from NuGet and are not included here. The rules engine (`src/Rules.Core`) uses no third-party packages.
