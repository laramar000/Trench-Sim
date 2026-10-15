# The Godot client (M7)

The desktop client lives in `client/`. It is a Godot 4 .NET project, kept **outside** `TrenchSim.sln`, so `dotnet build` and `dotnet test` on the solution never need Godot.

The client shows what the server says and sends commands. It never resolves a command, rolls dice or decides an outcome; the server does all of that (see `docs/protocol.md`).

## Status

M7.1 (bootstrap): the project opens and runs, and shows that our .NET 10 libraries load inside Godot. Connecting to a server, the battlefield and the HUD arrive in M7.2 to M7.5.

## What you need

- The **.NET** build of Godot, version in `client/GODOT_VERSION` (4.7.2). The standard build has no C# and cannot open this project: it reports a missing dependency on the C# script.
- The .NET 10 SDK.

## Build and run

1. Optional check without the editor: in `client/`, run `dotnet build`. It should end with `0 Error(s)`.
2. Open the Godot .NET editor, choose **Import**, and pick `client/project.godot` (or the `client` folder).
3. Press **Build** (the hammer, top right), then **Play** (F5).

Godot keeps its cache in `client/.godot/` and may create `client/Client.sln`; both are ignored by git.

## Tests

`dotnet test TrenchSim.sln` runs everything that does not need Godot. `ClientProjectTest` (in `tests/Server.Tests`) checks that the client is outside the solution, pins the Godot SDK to `GODOT_VERSION`, and allows the client to reference only shared client-side libraries (never `Match.Server`, `Sim` or `Match.Cli`).

## Compatibility gate (M7.1, passed 2026-10-15)

On Windows 11 (10.0.26200, x64) with Godot `4.7.2.stable.mono.official.ed1daf0bf` and .NET 10.0.13, a gate project built as `net10.0`, ran our rules code with results identical to the reference run (same state hashes), and received a `Welcome` from `trench-server` over `trench-sim.v2`.
