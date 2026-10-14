# Trench-Sim

A personal-use digital tabletop for a skirmish wargame, played with friends: two players, one authoritative server, rules measured in inches.

This is an unofficial fan project, not affiliated with or endorsed by any game publisher. The source code is public; no game builds are distributed.

## Status

Milestone M1 (dice: Success Rolls and Injury Rolls). The project follows a plan of nine milestones, M0 to M8. Each milestone is finished and verified before the next starts.

## Prerequisites

- .NET SDK 10 (`dotnet --version` should print 10.x)
- Git

## Verify

From the repository root:

```sh
dotnet --version
dotnet test
```

`dotnet test` should report every test passing (190 at M2), including four checks that the rules engine uses no Godot, networking, UI or other framework, package or project (its project file, imported build files, restored packages and compiled references).

## Layout

```text
TrenchSim.sln
src/Rules.Core/      rules engine: no Godot, no networking, no game-specific names
tests/Rules.Tests/   xUnit tests for the rules engine
docs/rules-notes.md  where each rule lives in the 1.0.2 rulebook
```

## Repository rules

- No publisher-owned artwork, logos, extracted game assets or copied proprietary code.
- No pasted rulebook text. `docs/rules-notes.md` gives section references; it also holds summaries in our own words of each rule implemented so far.
- Original placeholder content (names, units, equipment, descriptions, art) wherever practical.
- The repository is public, so everything committed here is published.
