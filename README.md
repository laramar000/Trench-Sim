# Trench-Sim

A private, personal-use digital tabletop for playing Trench Crusade with friends: two players, one authoritative server, rules measured in inches.

This is an unofficial project, not affiliated with or endorsed by Factory Fortress Inc. or Trench Crusade. It is not for distribution.

## Status

Milestone M0 (setup and rules notes). The project follows a plan of nine milestones, M0 to M8. Each milestone is finished and verified before the next starts.

## Prerequisites

- .NET SDK 10 (`dotnet --version` should print 10.x)
- Git

## Verify

From the repository root:

```sh
dotnet --version
dotnet test
```

`dotnet test` should report one passing test.

## Layout

```text
TrenchPvP.sln
src/Rules.Core/      rules engine: no Godot, no networking, no game-specific names
tests/Rules.Tests/   xUnit tests for the rules engine
docs/rules-notes.md  where each rule lives in the 1.0.2 rulebook
```

## Repository rules

- No publisher-owned artwork, logos, extracted game assets or copied proprietary code.
- No pasted rulebook text. `docs/rules-notes.md` gives section references and summaries in our own words.
- Original placeholder content (names, units, equipment, descriptions, art) wherever practical.
- The repository stays private.
