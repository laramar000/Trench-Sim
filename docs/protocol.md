# Match protocol, version 1

The server is the only authority. Clients send commands. The server checks every command with `Rules.Core` (`TurnSequence.Decide`), rolls every die itself, and is the only source of events, hashes and results. A client's state is only what it rebuilds from the server's events.

Code:
- `src/Match.Protocol` holds the messages, the JSON codec (`Wire`, `Codec`), the client mirror (`MatchMirror`) and `CommandSeats`. It depends only on `Rules.Core` and `System.Text.Json`, and is guarded by `ProtocolDependencyTest`.
- `src/Match.Server` holds the transport-free match pipeline (`Lobby`, `MatchHost`) and the WebSocket transport (`MatchServer`).
- `src/Match.Cli` is the terminal client (`trench-cli`). Its `ClientSession` is also what the integration tests use.
- Nothing here depends on Godot. `Rules.Core` has no networking.

## Transport and version negotiation

- **Endpoint:** a WebSocket at `ws://host:port/match`.
- **Handshake:** the client must offer the subprotocol `trench-sim.v1`, which the server selects. A client that does not offer it gets HTTP 400 and no socket, so incompatible versions are refused before any message is read. A plain HTTP request also gets 400.
- **Per-message version:** every message also starts with `"v":1` and then `"type"`, in that order. A message with another `v`, or with `v` missing or out of place, gets `UnsupportedVersion` or `Malformed`.
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
| `CreateMatch` | `scenario` | Creates a match of a named scenario (`ranged`, `melee`). The sender takes seat One. |
| `JoinMatch` | `code` | Joins by match code. The sender takes seat Two. No message can choose a seat. |
| `SubmitCommand` | `commandId`, `seq`, `knownEvents`, `command` | A command for the sender's own seat. |

**SubmitCommand fields**
- `commandId`: 1 to 64 printable ASCII characters, chosen by the client.
- `seq`: the seat's command number. It starts at 1 and must be exactly one more than the last number the server counted.
- `knownEvents`: how many events the client has applied.
- `command`: a `Rules.Core` command, `{"type":"MoveModel","seat":"One","model":"a1","path":[{"x":1000,"y":2000}]}`.

Example:

```json
{"v":1,"type":"SubmitCommand","commandId":"c1","seq":1,"knownEvents":7,"command":{"type":"DeployModel","seat":"One","model":"a1"}}
```

## Server to client

| type | fields | when |
|---|---|---|
| `Welcome` | `code`, `seat`, `scenario` | After a create or join. The match's events so far follow it. |
| `EventPublished` | `seq`, `event`, `hash` | Each event, numbered from 1, with the state hash after it. Both seats get every event, in log order. |
| `Accepted` | `commandId`, `seq`, `firstEvent`, `lastEvent`, `duplicate` | Sent to the sender after the command's events. |
| `Rejected` | `commandId`, `seq`, `kind`, `code`, `text`, `ruleRef`, `events`, `nextSeq`, `duplicate` | Sent to the sender only. Nothing changed and no die was rolled. |
| `MatchAborted` | `reason`, `seat`, `events`, `hash` | The match stopped before the rules ended it. See Aborted matches. |
| `Notice` | `kind` | `OpponentJoined`; `OpponentDisconnected`, after a match that ended by the rules; `ServerShutdown`. |

**Rejected fields**
- `kind` is `Rules` (the code is a `ReasonCode`) or `Protocol` (the code is a `ProtocolError`).
- `commandId` and `seq` are null when the message could not be read.

## Values on the wire

- **Objects:** an object is the parameters of its public constructor, read back through that constructor. A value the rules' own constructors refuse is refused on the wire as `InvalidValue`; for example, a point beyond ±200 inches or a die of 7.
- **Lengths and coordinates:** whole thousandths of an inch.
- **Ids:** model ids are strings.
- **Enums:** by exact name. Numbers, other casing and comma lists are refused. A flags enum is an array of names.
- **Strictness:** a field the type does not have is refused, and so is a repeated field, a missing required field, or null where the type does not allow it. So a command cannot carry dice, outcomes, events or hashes.
- **Types:** a command `type` is the class name of a `Rules.Core` command. An unknown type is refused as `UnknownType`, including event types and server messages sent by a client.

## Order of checks for SubmitCommand

1. **Read.** If the message cannot be read, the result is `Malformed`, `InvalidValue`, `UnknownType`, `UnsupportedVersion` or `TooLarge`.
2. **Seated?** The connection must hold a seat (`NotJoined`), the match must have started (`MatchNotStarted`), and it must not be aborted (`MatchAborted`).
3. **Own seat.** The command's seat must be the connection's seat (`SeatMismatch`). `StartMatch` is refused (`ServerOnlyCommand`); the server issues it when the second player joins.
4. **Resend.** If the command id was already answered with the same seq and command, the original answer is sent again with `duplicate: true` and nothing is applied. If the same id comes with anything else, the result is `IdReused`. The last 256 answers per seat are kept; an older resend is caught at step 5.
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

The opponent's `MatchAborted` carries the final event count and hash, so both sides can confirm they hold the same log. In M6 a rejoining client will use its `knownEvents` and its command ids to learn whether its last command was applied.

## Aborted matches

A match ends in one of two ways.
- **By the rules:** the last event is `MatchEnded`, the state has an outcome, and the match status is `Finished`.
- **Aborted:** the match stopped before the rules ended it. This is not a draw or a victory. The log has no `MatchEnded`, the state has no outcome, and every later command is refused with `MatchAborted`.

Both players still connected get `MatchAborted{reason, seat, events, hash}`. The reasons are:
- `Disconnected`: `seat` names the player who left. In M5 there is no rejoin.
- `Faulted`: the rules threw instead of answering, or `Decide`'s state differed from its applied events. Nothing is guessed, and the events already sent stay consistent.
- `ServerShutdown`.

Leaving a `Finished` match aborts nothing; the opponent gets `Notice OpponentDisconnected`. A match is removed once both players have gone.

## Lifecycle

- **Creating and starting.** The match log starts with `MatchCreated`. When the second player joins, the server issues `StartMatch`. Its events, including the deployment-zone roll-off, go to both players.
- **Join timeout.** A connection that has not created or joined a match within the join timeout (30 seconds by default) gets `Rejected JoinTimeout` and is closed with 1008 (PolicyViolation).
- **Clients that stop reading.** Such a client is dropped, and dropping it counts as a disconnect: its match is aborted with `MatchAborted Disconnected`, which its opponent receives if still connected. The server notices in one of three ways, whichever comes first:
  - 4096 messages are waiting for the client;
  - one message has taken 10 seconds to send, because the client's TCP window is full;
  - the client has not answered a keep-alive ping within 30 seconds, with pings sent every 15 seconds.

  Keep-alive alone is not enough: while a send is blocked, pings cannot be sent, which is why the send timeout exists. Other matches are not affected.

## Graceful shutdown

On Ctrl+C or SIGTERM:
1. The server stops accepting connections; new ones get 503.
2. Every match in progress or waiting is aborted with `MatchAborted ServerShutdown`. Every seated player then gets `Notice ServerShutdown`.
3. Each connection's queued messages are sent, followed by a close with 1001 (Going Away). The client has up to 5 seconds to answer the close, and then the host stops.

Messages arriving during shutdown are refused with `ShuttingDown`.

## Client replay

The server sends events in log order, one message each, to both players, through one ordered outbound queue per connection. Before sending, it applies each accepted event itself and checks the result against `Decide`'s state.

`MatchMirror.Apply` requires each event to be the next one in order. It applies the event with `Reducer.Apply` and requires `StateHash.Compute` of the result to equal the server's `hash`. A gap or a mismatch throws, and the client must stop trusting its copy. `ClientSession` then drops the connection and reports the failure. A `MatchAborted` whose count or hash differs from the client's copy is treated the same way.
