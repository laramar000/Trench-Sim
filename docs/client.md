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
- `Reconnector` (decision D7): after a transient drop it rejoins the seat with its token and the copy it already holds, waiting 1, 2, 4 and 8 seconds, then every 15. It never retries on its own after a seat takeover, a refused or invalid token, a policy close, or a finished or aborted match, and a copy that failed its hash check is left for a manual rejoin. See below.

It references only `Match.Protocol` (which brings `Rules.Core`). It never resolves commands, rolls dice, applies events except through `MatchMirror`, or reads match records.

## Reconnecting

- **One rejoin at a time per seat.** The automatic run (`Reconnector.RunAsync`) and "Rejoin now" (`Reconnector.RejoinNowAsync`) on the same dropped seat join one run and get the same result: there is only ever one new connection, so a client never takes its own seat from itself. "Rejoin now" during a wait between attempts makes the next attempt at once. Once a session has rejoined, the sessions before it are superseded and cannot start another rejoin. `Reconnector.IsReconnecting(mirror)` says whether a run is in progress.
- **Every attempt has a deadline.** Connecting, the WebSocket handshake and the wait for the rejoin answer all stop at `AttemptTimeout` (20 seconds by default) or when `GiveUpAfter` is reached, whichever is sooner. A host that accepts the connection but never answers cannot hold the run open. A connection from an attempt that timed out, failed or was cancelled is closed.
- **Cancelling** a run returns promptly and leaves no attempt or connection open.

### How long automatic attempts last (`ReconnectPolicy.GiveUpAfter`)

- The default is **five minutes**, the server's default `--rejoin-window` (300 seconds).
- The client **cannot find out** the window a server was started with: the protocol tells only the opponent when a dropped seat must be back (`MatchPaused.RejoinBy`), and `Welcome` does not carry it. So when a server runs with a longer `--rejoin-window`, set the client to match, for example `ReconnectPolicy.ForRejoinWindow(TimeSpan.FromMinutes(15))` for `--rejoin-window 900`. Otherwise the client stops trying while the server still holds the seat.
- A shorter server window is harmless: a late rejoin is refused (`MatchAborted` or `UnknownMatch`) and reconnecting stops.
- When automatic attempts stop (`GaveUp`), the match may still be waiting on the server. The client should offer **Rejoin now**, which makes one attempt (at most `AttemptTimeout`) and never runs alongside another. "Rejoin now" also takes the seat back after a takeover or a policy close, because the player chose to, and after a failed copy, which it rebuilds from an empty copy. It never rejoins a finished or aborted match.

### Known limitation: a takeover notice that never arrives

When the player opens a newer window and it takes the seat with the saved token, the server tells the older connection (`Rejected SeatTakenOver`, then a policy close), and the older window then stops for good. If the older connection has already dropped, it never hears this and believes it merely dropped:
- Usually that is harmless. Once the newer window has saved and confirmed its new token, the old token no longer opens the seat, so the older window's rejoin is refused (`InvalidToken`) and it stops. The newer window keeps the seat.
- But if the older window's rejoin reaches the server **before the newer window's token is confirmed**, or the newer window **could not save** its token, the old token still works and the older window takes the seat back. The newer window is then told it was taken over and stops; the two never take the seat back and forth.

Fixing that would need the server to tell a rejoining client that its seat was taken over in the meantime, which is a protocol change and is not part of M7. Both cases are covered by tests.

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
- `ClientAuthorityTest`: `Match.Client` uses the plain SDK and references only `Match.Protocol`; its compiled code references no resolver (`TurnSequence.Decide`), dice source, `Reducer` or record verifier. The Godot client is checked the same way on its **compiled** scripts: the test builds `client/Client.csproj` with the Godot .NET SDK from NuGet (no Godot install needed) into a temporary folder and scans `Client.dll`. Its assembly references must be on an allow-list (never `Match.Server`, `Sim` or `Match.Cli`), and it must not touch the copy of the match directly (`MatchMirror.Apply`/`Reset`, `Codec.ParseServer`) or load code by reflection. If the client cannot be built, the test fails. The scan is itself checked against the server and `Match.Client`, which do use these. A text scan of the Godot sources remains as a quick extra check.
- `TransportTests`: real matches over WebSockets against an in-process server: connection on v2, command numbering, duplicate resends, saved and rotated tokens, reconnecting after a drop, and the cases where it must not reconnect. Reconnect cases also use a fake host that never answers the handshake, never answers the rejoin, or answers busy or shutting down: one run per seat, deadlines, cancellation, "Rejoin now", and the takeover limitation above.

## Headless check

With a server running, `godot --headless --path client --build-solutions --quit`, then
`godot --headless --path client -- --smoke-server ws://127.0.0.1:5080/match` creates a match through `Match.Client` and prints `SMOKE OK: Welcome seat=One …`.

## Compatibility gate (M7.1, passed 2026-10-15)

On Windows 11 (10.0.26200, x64) with Godot `4.7.2.stable.mono.official.ed1daf0bf` and .NET 10.0.13, a gate project built as `net10.0`, ran our rules code with results identical to the reference run (same state hashes), and received a `Welcome` from `trench-server` over `trench-sim.v2`.
