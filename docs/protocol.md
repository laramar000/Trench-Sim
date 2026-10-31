# Match protocol, version 2

The server is the only authority. Clients send commands. The server checks every command with `Rules.Core` (`TurnSequence.Decide`), rolls every die itself, and is the only source of events, hashes and results. A client's state is only what it rebuilds from the server's events.

Code:
- `src/Match.Protocol` holds the messages, the JSON codec (`Wire`, `Codec`), the client mirror (`MatchMirror`) and `CommandSeats`. It depends only on `Rules.Core` and `System.Text.Json`, and is guarded by `ProtocolDependencyTest`.
- `src/Match.Server` holds the transport-free match pipeline (`Lobby`, `MatchHost`), the WebSocket transport (`MatchServer`) and the signed match records (`RecordStore`, `RecordWriter`; see `docs/records.md`).
- `src/Match.Cli` is the terminal client (`trench-cli`). Its `ClientSession` is also what the integration tests use.
- Nothing here depends on Godot. `Rules.Core` has no networking.

## Changes from version 1

Version 2 (M6) replaces version 1; the server speaks only version 2.
- The subprotocol is `trench-sim.v2` and every message starts with `"v":2`. A client offering only `trench-sim.v1` gets HTTP 400; a message with `"v":1` gets `UnsupportedVersion`.
- `Welcome` now carries the match id, a seat token, where the replay starts, the event count and hash, the next command number, the last answer and the match status.
- New client messages `Rejoin` and `TokenSaved`; new server messages `MatchPaused` and `MatchResumed`.
- A seated player who drops no longer aborts the match: it pauses for the rejoin window (5 minutes by default), and aborts with `RejoinExpired` if the seat is not back in time.
- New protocol errors `InvalidToken`, `MatchPaused`, `MatchFinished` and `SeatTakenOver`.
- **Behaviour change:** a command sent after the match ended by the rules is now refused as protocol `MatchFinished`, which does not use up the seq. In version 1 it was a rules refusal (`MatchEnded`) that did.
- Every match that started is recorded and signed when it ends (`docs/records.md`).

## Transport and version negotiation

- **Endpoint:** a WebSocket at `ws://host:port/match`.
- **Handshake:** the client must offer the subprotocol `trench-sim.v2`, which the server selects. A client that does not offer it gets HTTP 400 and no socket, so incompatible versions are refused before any message is read. A plain HTTP request also gets 400.
- **Per-message version:** every message also starts with `"v":2` and then `"type"`, in that order. A message with another `v`, or with `v` missing or out of place, gets `UnsupportedVersion` or `Malformed`.
- **Framing:** one message is one JSON object, sent as one WebSocket text message in UTF-8. It may be split over several frames.
- **Keep-alive:** the server pings every 15 seconds and drops a connection that stops answering.

## Size limits and bounded parsing

- **Client messages:** at most 16 KiB. The server reads at most that much into its buffer.
  - **Up to 1 MiB:** a longer message is read and thrown away, then refused as `TooLarge`. The connection stays open.
  - **Over 1 MiB:** the server sends `TooLarge` and closes with 1009 (MessageTooBig).
- **Binary messages:** refused as `Malformed`.
- **Invalid UTF-8:** a text frame that is not valid UTF-8 fails the connection with 1007 (InvalidPayloadData), as RFC 6455 requires. For a seated player that is a disconnect.
- **Parsing:** `Codec` checks the size and the UTF-8, then parses with a JSON depth limit of 16. Every failure becomes a `Rejected` with a fixed text; the server's exception messages are never echoed. A malformed message never ends a match or the server.
- **Server messages:** at most 1 MiB. The client's `ClientSession` treats a bigger or unreadable message as a server fault and stops trusting its copy.

## Client to server

| type | fields | meaning |
|---|---|---|
| `CreateMatch` | `scenario`, optional `map` | Creates a match of a named scenario (`ranged`, `melee`). The sender takes seat One. With `map` (trench-maps stage S5, left out when not given), the scenario's models play on that server map instead; a map the server lacks is refused `UnknownMap`, one not made for the scenario or not fitting it `MapDoesNotFit`. With `roster` (roster stage 5c, left out when not given, scenario `roster` only): the text of the sender's saved roster file. The server checks it itself with its own roster data (legal, and nothing the engine cannot play, else `RosterRefused` with the reason by name). Until a second player joins with a roster of their own, the match waits with a placeholder setup (Seat One's roster on both sides); when they join, the server replaces it with the real setup and sends Seat One a new `Welcome` with `replayFrom` 0, then seats Seat Two. Only the real match is recorded. |
| `JoinMatch` | `code`, optional `roster` | Joins by match code. The sender takes seat Two. No message can choose a seat. With `roster` (roster stage 5c, left out when not given): the sender's saved roster, needed to join a match hosted with one and refused `RosterRefused` for a match hosted with the practice warbands. |
| `SubmitCommand` | `commandId`, `seq`, `knownEvents`, `command` | A command for the sender's own seat. |
| `Rejoin` | `code`, `seatToken`, `knownEvents`, `knownHash` | Takes a seat back after a lost connection. See Rejoining. |
| `TokenSaved` | (none) | The client has saved the seat token from its last `Welcome`. See Seat tokens. |

**SubmitCommand fields**
- `commandId`: 1 to 64 printable ASCII characters, chosen by the client.
- `seq`: the seat's command number. It starts at 1 and must be exactly one more than the last number the server counted.
- `knownEvents`: how many events the client has applied.
- `command`: a `Rules.Core` command, `{"type":"MoveModel","seat":"One","model":"a1","path":[{"x":1000,"y":2000}]}`.

Example:

```json
{"v":2,"type":"SubmitCommand","commandId":"c1","seq":1,"knownEvents":7,"command":{"type":"DeployModel","seat":"One","model":"a1"}}
```

## Server to client

| type | fields | when |
|---|---|---|
| `Welcome` | `code`, `matchId`, `seat`, `scenario`, `seatToken`, `replayFrom`, `events`, `hash`, `nextSeq`, `lastAnswer`, `status` | After a create, join or rejoin. Events `replayFrom + 1` to `events` follow it. |
| `EventPublished` | `seq`, `event`, `hash` | Each event, numbered from 1, with the state hash after it. Both seats get every event, in log order. |
| `Accepted` | `commandId`, `seq`, `firstEvent`, `lastEvent`, `duplicate` | Sent to the sender after the command's events. |
| `Rejected` | `commandId`, `seq`, `kind`, `code`, `text`, `ruleRef`, `events`, `nextSeq`, `duplicate` | Sent to the sender only. Nothing changed and no die was rolled. |
| `MatchPaused` | `seat`, `rejoinBy` | A seated player dropped while the match was being played; nothing can be played until they are back. |
| `MatchResumed` | `seat` | That player is back and play continues. |
| `MatchAborted` | `reason`, `seat`, `events`, `hash` | The match stopped before the rules ended it. See Aborted matches. |
| `Notice` | `kind` | `OpponentJoined`; `OpponentDisconnected`, after a match that ended by the rules; `ServerShutdown`. |

**Welcome fields**
- `matchId`: 32 lowercase hex characters, unique per match; the record file is named after it.
- `seatToken`: a new secret for this seat (43 base64url characters). Null on a read-only rejoin to a match that is over.
- `replayFrom`: how many of the client's events the server kept. 0 means the client must start from an empty copy.
- `events`, `hash`: the client is in step once its copy holds `events` events and hashes to `hash`.
- `nextSeq`: the seat's next command number. `lastAnswer`: the seat's most recent `Accepted` or rules `Rejected`, or null.
- `status`: `Waiting`, `Playing`, `Paused`, `Finished` or `Aborted`.

**Rejected fields**
- `kind` is `Rules` (the code is a `ReasonCode`) or `Protocol` (the code is a `ProtocolError`).
- `commandId` and `seq` are null when the message could not be read.

## Values on the wire

- **Objects:** an object is the parameters of its public constructor, read back through that constructor. A value the rules' own constructors refuse is refused on the wire as `InvalidValue`; for example, a point beyond ±200 inches or a die of 7.
- **Optional init fields:** a public init property the constructor does not take is written after the parameters and is optional on reading. One of them, a terrain piece's `climb` (towers proposal, stage T1: a tower's height and ladders), is left out when null, so a battlefield without towers is written exactly as before and every existing record keeps its bytes. A client built before towers refuses a match that has one (an unknown field); clients and the server ship together, so the protocol version is unchanged.
- **Lengths and coordinates:** whole thousandths of an inch.
- **Ids:** model ids are strings.
- **Enums:** by exact name. Numbers, other casing and comma lists are refused. A flags enum is an array of names.
- **Strictness:** a field the type does not have is refused, and so is a repeated field, a missing required field, or null where the type does not allow it. So a command cannot carry dice, outcomes, events or hashes.
- **Types:** a command `type` is the class name of a `Rules.Core` command. An unknown type is refused as `UnknownType`, including event types and server messages sent by a client.

## Order of checks for SubmitCommand

1. **Read.** If the message cannot be read, the result is `Malformed`, `InvalidValue`, `UnknownType`, `UnsupportedVersion` or `TooLarge`.
2. **Seated?** The connection must hold a seat (`NotJoined`) and the match must be in play: not waiting (`MatchNotStarted`), paused (`MatchPaused`), finished (`MatchFinished`) or aborted (`MatchAborted`).
3. **Own seat.** The command's seat must be the connection's seat (`SeatMismatch`). `StartMatch` is refused (`ServerOnlyCommand`); the server issues it when the second player joins.
4. **Resend.** If the command id was already answered with the same seq and command (`knownEvents` is not compared, so a resend after a rejoin is recognised), the original answer is sent again with `duplicate: true` and nothing is applied. If the same id comes with anything else, the result is `IdReused`. The last 256 answers per seat are kept; an older resend is caught at step 5.
5. **Number.** `seq` must equal the next number (`OutOfOrder`, with `nextSeq`).
6. **Fresh?** `knownEvents` must equal the log length (`Stale`, with `events`).
7. **Rules.** The command goes to `TurnSequence.Decide` with the server's dice.
   - A rules refusal is a `Rejected` of kind `Rules`.
   - On acceptance, the events are applied one by one and checked against `Decide`'s state, then appended and sent to both seats. `Accepted` then goes to the sender.

Protocol refusals (steps 1 to 6) do not use up the seq. Rules refusals and acceptances do, and their answers are kept for resends. No refusal changes the state or rolls a die.

## A connection that drops during a command

The Lobby handles one whole message at a time under one lock. `Lobby.Receive` returns only when the command has been decided, its events appended to the log and queued to both players, and its answer queued. The transport calls `Lobby.Disconnect` only after its receive loop has ended, so the order is fixed:
- **A whole message received before the drop** is processed exactly once, completely, before the disconnect. That is true even if its answer never reaches the sender. The opponent receives its events, then `MatchAborted`.
- **A message cut off by the drop** is never processed.
- **No re-execution:** the aborted match accepts nothing more, so the command cannot run twice. Within a live connection, a retry of the same command id gets the cached answer.

A dropped seated player can rejoin (below). The `Welcome` tells it the next seq and its last answer, and a resend of its last command with the same id gets the cached answer, so a command is applied at most once even if the answer was lost.

## Aborted matches

A match ends in one of two ways.
- **By the rules:** the last event is `MatchEnded`, the state has an outcome, and the match status is `Finished`.
- **Aborted:** the match stopped before the rules ended it. This is not a draw or a victory. The log has no `MatchEnded`, the state has no outcome, and every later command is refused with `MatchAborted`.

Both players still connected get `MatchAborted{reason, seat, events, hash}`. The reasons are:
- `Disconnected`: `seat` names the player who left. Only with a rejoin window of 0 (`--rejoin-window 0`), which keeps version 1's immediate abort.
- `RejoinExpired`: `seat` names the player whose rejoin deadline passed first.
- `Faulted`: the rules threw instead of answering, or `Decide`'s state differed from its applied events. Nothing is guessed, and the events already sent stay consistent.
- `ServerShutdown`.

Leaving a `Finished` match aborts nothing; the opponent gets `Notice OpponentDisconnected`. A match that never started is removed as soon as its creator leaves, with no record. A finished or aborted match is kept for read-only rejoins until the rejoin window has passed since the later of its end and its last player leaving.

## Seat tokens

Every `Welcome` that seats a player in a match not yet over carries a new seat token. The token is the only way to take the seat back; the code alone is not enough.
- The server keeps only each token's SHA-256 and compares with a constant-time check. Tokens are never logged.
- **Rotation.** A token stays valid alongside the older ones until it is confirmed. The client saves the token durably and then sends `TokenSaved`; from then on only that token is valid. Presenting a newer token in a `Rejoin` also proves it arrived and confirms it. At most 4 unconfirmed tokens are kept per seat.
- So a lost `Welcome` never locks the player out: the previous token still works. A repeated `TokenSaved` is harmless; a `TokenSaved` with no token sent on that connection gets `InvalidToken`, and one after the match is over gets `MatchFinished` or `MatchAborted`.
- The CLI saves seats under `~/.trench-sim/sessions/` (or `$TRENCH_SIM_HOME/sessions/`): the folder is owner-only (0700), each file owner-only (0600) and replaced atomically. A file or folder others can read is refused, and a missing, damaged or unwritable file is reported, never used. If the save fails, the client does not send `TokenSaved` and keeps using the old token.

## Rejoining

`Rejoin{code, seatToken, knownEvents, knownHash}` takes a seat back on a new connection. `knownHash` is the client's hash after `knownEvents` events, and is null exactly when `knownEvents` is 0.
- **Checks.** A connection that already holds a seat gets `AlreadyJoined`; an unknown code gets `UnknownMatch`; a token that is not valid for the match gets `InvalidToken`. After 3 invalid tokens the connection is closed.
- **Takeover.** A valid rejoin takes the seat from any connection still holding it: that connection gets `SeatTakenOver` and is closed. A seat is never held by two connections; the last valid rejoin wins.
- **Replay.** If the client's first `knownEvents` events match the server's log (the hash at that point is equal), `replayFrom` is `knownEvents` and only the later events are sent. Otherwise `replayFrom` is 0 and every event is sent, so a client with a wrong or longer history starts again.
- **Pause.** If a seated player drops while the match is being played, the match is paused: both seats get `MatchPaused{seat, rejoinBy}` (the opponent at once, the absent player when they rejoin), and commands are refused with `MatchPaused`. `rejoinBy` is fixed when that player drops (now plus the rejoin window); rejoining or dropping again in the meantime never moves it. When every absent player is back, the others get `MatchResumed`. If a deadline passes first, the match aborts with `RejoinExpired` for the seat whose deadline passed first. There is no cap on total pause time across separate disconnects; each disconnect has its own deadline.
- **Read-only.** A finished or aborted match can be rejoined while it is kept, to see the whole log and how it ended. No token is issued (`seatToken` is null) and nothing changes: commands get `MatchFinished` or `MatchAborted`, and `TokenSaved` is refused. An aborted match also sends its `MatchAborted` after the events.
- A match that was not finished when the server stopped is never restored to play: after a restart its code is unknown and its record says it was aborted or unrecorded.


## Lifecycle

- **Creating and starting.** The match log starts with `MatchCreated`. When the second player joins, the server issues `StartMatch`. Its events, including the deployment-zone roll-off, go to both players.
- **Join timeout.** A connection that has not created or joined a match within the join timeout (30 seconds by default) gets `Rejected JoinTimeout` and is closed with 1008 (PolicyViolation).
- **Clients that stop reading.** Such a client is dropped, and dropping it counts as a disconnect: its match is paused, as for any other drop. The server notices in one of three ways, whichever comes first:
  - 4096 messages are waiting for the client (a rejoin's replay of the log is not counted);
  - one message has taken 10 seconds to send, because the client's TCP window is full;
  - the client has not answered a keep-alive ping within 30 seconds, with pings sent every 15 seconds.

  Keep-alive alone is not enough: while a send is blocked, pings cannot be sent, which is why the send timeout exists. Other matches are not affected.

## Graceful shutdown

On Ctrl+C or SIGTERM:
1. The server stops accepting connections; new ones get 503.
2. Every match in progress, paused or waiting is aborted with `MatchAborted ServerShutdown`. Every seated player then gets `Notice ServerShutdown`.
3. Each connection's queued messages are sent, followed by a close with 1001 (Going Away). The client has up to 5 seconds to answer the close.
4. The server waits for the records still being written, within the same 5 seconds, and then the host stops. A record it could not finish is settled on the next start (`docs/records.md`).

Messages arriving during shutdown are refused with `ShuttingDown`.

## Client replay

The server sends events in log order, one message each, to both players, through one ordered outbound queue per connection. Before sending, it applies each accepted event itself and checks the result against `Decide`'s state.

`MatchMirror.Apply` requires each event to be the next one in order. It applies the event with `Reducer.Apply` and requires `StateHash.Compute` of the result to equal the server's `hash`. A gap or a mismatch throws, and the client must stop trusting its copy. `ClientSession` then drops the connection and reports the failure. A `MatchAborted` whose count or hash differs from the client's copy is treated the same way.

## A model's own dice (roster stage R1)

- `ModelProfile` has six optional numbers, each left out when zero, so every setup without them is written exactly as
  before and the protocol version stays 2: `meleeDice`, `meleeWeaponDice`, `meleeWeaponInjuryDice`, `rangedDice`,
  `rangedWeaponDice`, `rangedWeaponInjuryDice` (written after the constructor fields, in that order). Each is -6 to +6;
  weapon fields need that weapon. A value out of range is an invalid value (interpretation U97).

## Rosters online (roster stage 5c)

- Optional fields, each left out when not used, so every message without them is written exactly as before and the
  protocol version stays 2: `CreateMatch.roster`, `JoinMatch.roster` and `Welcome.warbands`.
- `Welcome.warbands`: in a match played with rosters sent online, the models' names in the warband content format
  (both warbands, Seat One first). Names only; a client uses them only if every profile in them equals the setup's.
- `RosterRefused` (new error code): a roster that cannot be read or played (the text says why, by name), a roster sent
  with another scenario, a roster match joined without a roster, a practice match joined with one, or a server that has
  no roster data. A refused join leaves the match waiting for another player.
- The second `Welcome` Seat One gets when the match is set up answers no request. A client must not take it as the
  answer to its next command: `Match.Client.ClientSession` keeps a second `Welcome` for the same code and seat out of
  its answer queue (test `RosterSessionTests`; before this fix two `trench-cli --bot` players could stall).
- Names a player gives (the roster's and its models') reach the opponent; the server refuses names over 60 characters
  or with control characters.
- Tests: `Server.Tests/RosterOnlineTests` (with `RosterSessionTests`, `RosterCliTests`), `Client.Tests/RosterHotSeatTests` (two controllers on the real server).
