# Match protocol, version 1

The server is the only authority. Clients send commands. The server checks every command with `Rules.Core` (`TurnSequence.Decide`), rolls every die itself, and is the only source of events, hashes and results. A client's state is only what it rebuilds from the server's events.

Code: `src/Match.Protocol` holds the messages, the JSON codec (`Wire`, `Codec`) and the client mirror (`MatchMirror`). It depends only on `Rules.Core` and `System.Text.Json`. `src/Match.Server` holds the transport-free match pipeline (`Lobby`, `MatchHost`).

## Framing

- One message is one JSON object, sent as one WebSocket text message in UTF-8.
- Every message starts with `"v":1` and then `"type"`, in that order. A message with another version gets `UnsupportedVersion`.
- A client message is at most 16 KiB and a server message at most 1 MiB. JSON nesting is at most 16 levels deep.

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
| `Notice` | `kind` | `OpponentJoined`, `OpponentDisconnected`, `MatchFaulted`, `ServerShutdown`. |

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
2. **Seated?** The connection must hold a seat (`NotJoined`), the match must have started (`MatchNotStarted`), and it must not be closed (`MatchClosed`).
3. **Own seat.** The command's seat must be the connection's seat (`SeatMismatch`). `StartMatch` is refused (`ServerOnlyCommand`); the server issues it when the second player joins.
4. **Resend.** If the command id was already answered with the same seq and command, the original answer is sent again with `duplicate: true` and nothing is applied. If the same id comes with anything else, the result is `IdReused`. The last 256 answers per seat are kept; an older resend is caught at step 5.
5. **Number.** `seq` must equal the next number (`OutOfOrder`, with `nextSeq`).
6. **Fresh?** `knownEvents` must equal the log length (`Stale`, with `events`).
7. **Rules.** The command goes to `TurnSequence.Decide` with the server's dice.
   - A rules refusal is a `Rejected` of kind `Rules`.
   - On acceptance, the events are applied one by one and checked against `Decide`'s state, then appended and sent to both seats. `Accepted` then goes to the sender.

Protocol refusals (steps 1 to 6) do not use up the seq. Rules refusals and acceptances do, and their answers are kept for resends. No refusal changes the state or rolls a die.

## Lifecycle

- **Creating and starting.** The match log starts with `MatchCreated`. When the second player joins, the server issues `StartMatch`. Its events, including the deployment-zone roll-off, go to both players.
- **The end.** After `MatchEnded` the rules refuse every command.
- **Disconnects.** When a player disconnects, the match is closed with no result and the opponent gets `OpponentDisconnected`. The match is removed once both players have gone. Rejoining is M6.
- **Faults.** If the rules throw instead of answering, the match is closed and both players get `MatchFaulted`. The events already sent stay consistent.
- **Shutdown.** Every seated player gets `ServerShutdown`, and every later message gets `ShuttingDown`.

## Client replay

`MatchMirror.Apply` requires each event to be the next one in order. It applies the event with `Reducer.Apply` and requires `StateHash.Compute` of the result to equal the server's `hash`. A gap or a mismatch throws, and the client must stop trusting its copy.
