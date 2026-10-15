# Trench-Sim

A personal-use digital tabletop for a skirmish wargame, played with friends: two players, one authoritative server, rules measured in inches.

This is an unofficial fan project, not affiliated with or endorsed by any game publisher. The source code is public; no game builds are distributed.

## Status

Milestone M6: reconnects and signed match records, awaiting review. A player who loses their connection can rejoin within five minutes with a saved seat token, and the match pauses meanwhile. Every match that started is saved as a signed record that `trench-cli verify` checks offline (`docs/records.md`); a valid signature does not prove the dice were fair. Before that, M5: an authoritative WebSocket match server and a terminal client. Two players can play a whole match over the network; the server rolls every die and is the only source of events, and each client checks the server's state hash after every event. The protocol (version 2) is in `docs/protocol.md`. Before that, M4 Slice B (a simulated full match with ranged and melee combat: placement, Move, Dash, Shoot, Charge, Retreat, Fight, shooting into melee, Blood Marker spending, injuries, objectives and victory, with a console simulator; `--melee` uses warbands with Melee Weapons). Slice B is a checkpoint awaiting approval; Bloodbath Rolls, Glorious Deeds and other scenarios are not done. The project follows a plan of nine milestones, M0 to M8. Each milestone is finished and verified before the next starts.

## Prerequisites

- .NET SDK 10 (`dotnet --version` should print 10.x)
- Git

## Verify

From the repository root:

```sh
dotnet --version
dotnet test
```

`dotnet test` should report every test passing (see the checkpoint report for the current count), including four checks that the rules engine uses no Godot, networking, UI or other framework, package or project (its project file, imported build files, restored packages and compiled references).

To play one simulated match, or twenty, to the end:

```sh
dotnet run --project src/Sim -- --seed 42
dotnet run --project src/Sim -- --seeds 1-20
```

Each line shows the outcome, Victory Points and counts; the program exits with an error if a match fails to finish.

To play over the network, start the server and two terminal clients (each in its own terminal):

```sh
dotnet run --project src/Match.Server -- --port 5080
dotnet run --project src/Match.Cli -- --server ws://127.0.0.1:5080/match create ranged
dotnet run --project src/Match.Cli -- --server ws://127.0.0.1:5080/match join <code>
```

The first client prints the match code for the second. Type `help` for commands, or add `--bot <seed>` to let the simulator's bot play a seat.

Server options: `--address`, `--port`, `--join-timeout <seconds>`, `--rejoin-window <seconds>` (300 by default; 0 aborts a match at once when a player drops), `--data <folder>` (records and the signing key, `~/.trench-sim/server` by default) and `--new-signing-key`. On first start the server prints its signing key id and public key.

If a client loses its connection, rejoin with the seat saved in `~/.trench-sim/sessions/`:

```sh
dotnet run --project src/Match.Cli -- --server ws://127.0.0.1:5080/match rejoin <code>
```

Add `--seat One` or `--seat Two` if both seats of that match were played from this machine.

To check a saved match record (copy the server's `keys/signing.pub` to `~/.trench-sim/trusted-keys/`, or pass `--key`):

```sh
dotnet run --project src/Match.Cli -- verify ~/.trench-sim/server/matches/<id>.json --key ~/.trench-sim/server/keys/signing.pub
```

It exits 0 only for a completed match whose signature and every event check out; see `docs/records.md` for the other verdicts.

## Layout

```text
TrenchSim.sln
src/Rules.Core/      rules engine: no Godot, no networking, no game-specific names
src/Sim/             console simulator: two bots play whole matches; reads content/*.json
src/Match.Protocol/  protocol v2 messages, strict JSON codec, client replay, match records and their verifier
                     (Rules.Core, System.Text.Json and System.Security.Cryptography only)
src/Match.Server/    authoritative match server: transport-free Lobby/MatchHost, the WebSocket host, record storage
src/Match.Cli/       terminal client (trench-cli): interactive or bot play, rejoin, verify
content/             original test warbands (placeholder content)
tests/Rules.Tests/   xUnit tests for the rules engine
tests/Server.Tests/  codec, server pipeline, records, reconnect and real-WebSocket integration tests
docs/protocol.md     the match protocol, version 2
docs/records.md      signed match records, the verifier and the server's data folder
docs/rules-notes.md  where each rule lives in the 1.0.2 rulebook
docs/interpretations.md  rulebook-confirmed requirements versus software interpretations
docs/known-gaps.md   what is left out
```

## Repository rules

- No publisher-owned artwork, logos, extracted game assets or copied proprietary code.
- No pasted rulebook text. `docs/rules-notes.md` gives section references; it also holds summaries in our own words of each rule implemented so far.
- Original placeholder content (names, units, equipment, descriptions, art) wherever practical.
- The repository is public, so everything committed here is published.

## License

The original code and original documentation in this repository are licensed under the [Apache License 2.0](LICENSE).

That license covers only material written for this project. It does not cover, and does not grant any rights in, the game, its rules text, artwork, logos, names, setting or other intellectual property of any game publisher or other third party, which remain subject to their owners' rights. It gives no permission to distribute a complete playable client of that game or its protected content, and this repository contains none. This project is unofficial and unaffiliated.

The test project uses the third-party packages Microsoft.NET.Test.Sdk, xunit and xunit.runner.visualstudio, which keep their own licenses; they are restored from NuGet and are not included here. The rules engine (`src/Rules.Core`) and the protocol library (`src/Match.Protocol`) use no third-party packages; the server uses only the ASP.NET Core shared framework that ships with .NET.
