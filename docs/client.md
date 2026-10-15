# The Godot client (M7)

The desktop client lives in `client/`. It is a Godot 4 .NET project, kept **outside** `TrenchSim.sln`, so `dotnet build` and `dotnet test` on the solution never need Godot.

The client shows what the server says and sends commands. It never resolves a command, rolls dice or decides an outcome; the server does all of that (see `docs/protocol.md`).

## Status

- M7.1 (bootstrap): the project opens and runs, and shows that our .NET 10 libraries load inside Godot.
- M7.2 (shared client library): the client reaches the server through `Match.Client`. There is no match screen yet; the battlefield and HUD arrive in M7.3 to M7.5.

## The shared client library (`src/Match.Client`)

Plain C#, used by both `trench-cli` and the Godot client, so protocol handling exists once:
- `ClientSession`: one WebSocket connection on `trench-sim.v2`. It numbers commands, resends an unanswered one with the same id, keeps the copy of the match (`MatchMirror`, every event hash-checked), and saves a new seat token before confirming it (`TokenSaved`).
- `SessionStore`: saved seats in `~/.trench-sim/sessions` (or `$TRENCH_SIM_HOME/sessions`), owner-only on Linux and macOS.
- `Reconnector` (decision D7): after a transient drop it rejoins the seat with its token and the copy it already holds, waiting 1, 2, 4 and 8 seconds, then every 15, for up to five minutes. It never retries after a seat takeover, a refused or invalid token, or a finished or aborted match, and a copy that failed its hash check is left for a manual rejoin.

It references only `Match.Protocol` (which brings `Rules.Core`). It never resolves commands, rolls dice, applies events except through `MatchMirror`, or reads match records.

## What you need

- The **.NET** build of Godot, version in `client/GODOT_VERSION` (4.7.2). The standard build has no C# and cannot open this project: it reports a missing dependency on the C# script.
- The .NET 10 SDK.

## Build and run

1. Optional check without the editor: in `client/`, run `dotnet build`. It should end with `0 Error(s)`.
2. Open the Godot .NET editor, choose **Import**, and pick `client/project.godot` (or the `client` folder).
3. Press **Build** (the hammer, top right), then **Play** (F5).

Godot keeps its cache in `client/.godot/` and may create `client/Client.sln`; both are ignored by git.

## Tests

`dotnet test TrenchSim.sln` runs everything that does not need Godot. In `tests/Client.Tests`:
- `ClientProjectTest`: the Godot client is outside the solution, its SDK matches `GODOT_VERSION`, and it may reference only `Match.Client` and `Match.Protocol` (never `Match.Server`, `Sim` or `Match.Cli`).
- `ClientAuthorityTest`: `Match.Client` uses the plain SDK and references only `Match.Protocol`; its compiled code references no resolver (`TurnSequence.Decide`), dice source, `Reducer` or record verifier; the Godot scripts contain none of these either. The scan is itself checked against the server, which does use them.
- `TransportTests`: real matches over WebSockets against an in-process server: connection on v2, command numbering, duplicate resends, saved and rotated tokens, reconnecting after a drop, and the cases where it must not reconnect.

## Headless check

With a server running, `godot --headless --path client --build-solutions --quit`, then
`godot --headless --path client -- --smoke-server ws://127.0.0.1:5080/match` creates a match through `Match.Client` and prints `SMOKE OK: Welcome seat=One …`.

## Compatibility gate (M7.1, passed 2026-10-15)

On Windows 11 (10.0.26200, x64) with Godot `4.7.2.stable.mono.official.ed1daf0bf` and .NET 10.0.13, a gate project built as `net10.0`, ran our rules code with results identical to the reference run (same state hashes), and received a `Welcome` from `trench-server` over `trench-sim.v2`.
