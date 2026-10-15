# Match records, version 1

Every match that started is written to disk as a signed record when it ends. A record is for checking a result afterwards. It is never used to put a match back into play: a match that was interrupted stays interrupted.

Code:
- `src/Match.Protocol/MatchRecord.cs`: the format, signing and key ids.
- `src/Match.Protocol/RecordVerifier.cs`: the verifier. It reads untrusted bytes and never throws.
- `src/Match.Server/RecordStore.cs`, `RecordWriter.cs`, `DurableFile.cs`, `ServerData.cs`: the server's data folder, durable writes and recovery.
- `src/Match.Cli/Verify.cs`: `trench-cli verify`.

## What a record proves, and what it does not

A record that verifies as `Completed` proves two things:
- the holder of the signing key wrote it, unchanged since;
- every event follows the rules from the recorded setup and the recorded dice, and each hash matches.

**It does not prove the dice were fair.** The server rolls every die and holds the key, so a dishonest server could choose its dice and sign the result. There is no commit-reveal scheme in M6. A record is only as trustworthy as the person who runs the server.

## Format

One JSON object, UTF-8, at most 16 MiB and 100,000 events:

```json
{"format":"trench-sim.match-record","recordVersion":1,"body":{...},
"signature":{"alg":"ES256","keyId":"sha256:<hex>","value":"<base64url>"}}
```

- **body**: `matchId`, `code`, `createdAt`, `closedAt`, `engine`, `scenario`, `contentFingerprint`, `events` and `result`.
  - `engine`: `rulesVersion` (`1.0.2`), `stateHashVersion` (4), `replayCompatibility` (1), `protocolVersion` (2) and `build` (the git commit the server was built from).
  - `events`: every event as `{seq, event, hash}`, in the protocol's wire form. The first is `MatchCreated`, which holds the whole setup.
  - `contentFingerprint`: `sha256:` and the SHA-256 of the setup. `--content` compares it with a content folder.
  - `result`: `{kind, events, hash, winner, reason, seat}`. `kind` is `Completed`, `Aborted` or `Unrecorded`.
- **signature**: ECDSA P-256 with SHA-256. The 64-byte signature is in IEEE P1363 form, encoded as base64url.
  - The signed bytes are `trench-sim.match-record\n1\n<keyId>\n` followed by the body exactly as it appears in the file, byte for byte.
  - `keyId` is `sha256:` and the hex SHA-256 of the public key's SubjectPublicKeyInfo.

## Verdicts

`trench-cli verify <record.json> [--key <public.pem>]... [--content <folder>] [--quiet]` checks a record offline, without connecting to anything; `--quiet` prints only the verdict line. It trusts only keys given with `--key` and the `*.pub` files in `~/.trench-sim/trusted-keys/` (or `$TRENCH_SIM_HOME/trusted-keys/`). It never trusts a key named inside the record.

| verdict | exit | meaning |
|---|---|---|
| `Completed` | 0 | Signed by a trusted key, and every event replays with matching hashes to a `MatchEnded` that agrees with the result. **Only this verdict counts as a finished match.** |
| `Aborted` | 10 | Signed and replays, but the match stopped before the rules ended it (disconnect, expired rejoin, fault, shutdown). Not a draw or a victory. |
| `Unrecorded` | 11 | Signed stub: the server stopped before it could write this match's record. No result. |
| `Invalid` | 20 | The record is damaged or forged: bad signature, malformed JSON, broken setup or sequence, an illegal event, a hash mismatch, or a result that does not match the events. |
| `Unverifiable` | 30 | This verifier cannot judge it: unsupported record version, signature algorithm or rules version, or no trusted key with its id. |
| usage | 64 | Bad arguments, or the file or a key cannot be read. |

One rules version is supported at a time. A record from another `rulesVersion`, `stateHashVersion` or `replayCompatibility` is `Unverifiable`, never replayed under different rules. Golden records in `tests/Server.Tests/golden/` keep this version's output fixed.

The checks run in this order: size and UTF-8; format and record version; algorithm; trusted key; signature; body; engine versions; Unrecorded stub; setup (exactly one `MatchCreated`, first); each event replayed with `Reducer.Apply` and its hash compared; the result.

## The server's data folder

`trench-server --data <folder>` (default `~/.trench-sim/server/`, created 0700):

```text
server.lock          held while the server runs; a second server on the same folder refuses to start
keys/signing.key     PKCS#8 private key, 0600
keys/signing.pub     public key (PEM), to copy into players' trusted-keys folders
matches/<id>.json    one record per match
matches/open/<id>.json  open-match markers
```

**Signing key.** On first start (no `keys/` folder) the server creates the key, written 0600, and prints its id and the public PEM. Afterwards a missing, unreadable, non-P-256 or mismatched key stops the server rather than signing with a new one. The server does not check the permissions of an existing key file. `--new-signing-key` creates a key only after both old key files have been moved away (for example to `keys/retired/`); records signed with the old key still verify with its `.pub`.

**Writing a record.** When a match starts, the server writes an open-match marker. When the match ends (finished or aborted), one background writer:
1. signs the record;
2. writes it to a temporary file, flushes it to disk, renames it into place without overwriting, and flushes the folder;
3. reads it back and verifies it with the server's own key;
4. only then deletes the marker and reports it written.

If any step fails there is no retry. The marker stays, the failure is logged, and the record is never reported as `Completed`. On the next start the marker is settled (below).

**Recovery on start.** Before accepting connections the server:
- deletes leftover temporary files;
- settles each marker. A record that is present and verifies is kept and the marker cleared. Otherwise any record that is there is set aside as `<id>.json.bad`, and a signed `Unrecorded` stub takes its place. A marker it cannot read is set aside as `.bad`.

So a match whose record was never safely written shows as `Unrecorded`, never as `Completed`, a draw or a victory. The matches themselves are not restored: their codes are unknown after a restart.

**Durability.** Linux only. There the server flushes each file and its folder (`fsync`), so a record reported as written survives a power cut. On macOS and Windows the same steps run, but folder flushes are not available or not guaranteed, so durability is best effort.
