# Known gaps (M4 Slice B, M6)

What the simulated match does not do after Slice B. Each item is a decision to defer, not an oversight. Rule references are to the Digital Rulebook 1.0.2.

## Rules not implemented
- **Melee extras** (Slice B implements Charge, Retreat, Fight and shooting into melee, U46 to U65): no Off-Hand or second Melee Weapon, CLEAVE, BLOCK, Fear, Diving Charge, ASSAULT, climbing or jumping charges, or the optional charge continuation. Charge routes go round obstacles (U57 to U65), with these limits: the route is a deterministic approximation, not a proved global shortest (curves are 32-gons, within about 0.6% plus 0.012" of a brute-force search in testing; a doorway up to about 0.01" wider than the base may count as closed; contact is sought at 128 points round the target), the planner does not bend round Difficult terrain (open question 5) or detour to avoid the 1" zones of other enemies, and a declaration needing more than 64 legs within 12" of Movement is refused. Down models are measured from the base only (U55).
- **Bloodbath Rolls** (p. 47): not implemented; the dice interaction is open (question 3 in `rules-notes.md`). Spending 6 markers (3 if Down) is not offered.
- **Glorious Deeds** (p. 153, one VP each at the end of the game): none are scored; Victory Points come from Objectives only. This can change who wins a close game.
- **Elevation attack bonus** (+1 DICE when 3" or more higher, p. 43): not applied (U35).
- **Terrain effects beyond cost**: Dangerous terrain has no effect; climbing, jumping, falling and the Cast Them Down deed do not exist.
- **Keywords, equipment, skills and warband entries**: models have only Movement, base size, one ranged weapon range and flat injury modifiers. The CRITICAL (+2 INJURY DICE) and DEADLY keywords are not implemented.
- **Blessing Markers**, re-rolls (p. 31), campaign rules, the Infiltrators deployment exception: out of scope.
- **Terrain set-up roll-off** and the real terrain pieces of the scenario: the battlefield is a fixed fixture (U32).

## Provisional rulings in force
Rules R1 to R8 (turn sequence, `rules-notes.md`) and U30 to U56 (`interpretations.md`) apply. The ones most likely to be changed by an official ruling or playtesting: the Down penalty reading (U31), who controls an Objective when both sides are on it (U36), a tied game being a draw (U40), the unprinted table size and zone depth (U32).

## Tooling limits
- `LegalCommands` lists decisions (including free attacks and the friendly-target choice) and the end of an activation, but not Move, Dash, Shoot, Charge, Retreat, Fight or PlaceModel commands: they depend on continuous positions, so the simulator's bots generate them and the rules accept or refuse them.
- The two simulated players are simple (random choice among legal-looking options, integer geometry); they exist to prove that whole matches finish and replay identically, not to play well.
- The sight sampler can miss a very narrow gap (see `interpretations.md`, golden scenes).
- Only one scenario exists.
- Replay checks each event against the state before it, but not whether a log is complete: a log cut off before `MatchEnded` replays as a match in progress. A saved match record (M6) must also be checked to end in `MatchEnded`.
- `Decide` assumes a state that `Decide` produced. Called on a state no command could reach (for example a log cut between a warband's elimination and its `MatchEnded`), it can throw `InvalidEventException` while emitting the events that state forbids, rather than return a rejection. The server only ever holds states built from its own decisions.
- Commands with points beyond the table range (±200") or lengths out of range cannot be constructed: `Point2` and `Length` throw in their constructors. A network layer must catch that while reading a message and turn it into a rejection.

## What the routing tests establish (and what they cannot)

The charge planner is checked three ways, none of which proves it finds the true shortest route:

- **Legality** (`ChargeRoutingAuditTests`): every leg of every tested route keeps the base clear of every wall, friend and the target, measured by plain Euclidean distance in floating point with no polygon outlines. This is independent of the planner's own geometry. It shows no tested route cuts a corner; it says nothing about routes that were not produced.
- **Taut string**: no stretch of a route can be replaced by a legal straight chord that is shorter by more than the tolerance. This needs no reference route. It catches routes with slack; it cannot catch a route that is taut but in the wrong corridor.
- **Brute-force reference** (`ReferenceRouter`): a finely sampled outline search written without any Rules.Core code. It shares the planner's *modelling* assumptions (the route is a polyline over the corners of obstacles widened by the base's radius, and the goal is contact with the target's base), so agreement shows the planner implements that model correctly within the tolerance, not that the model is the rulebook's. The reference was itself checked against the closed form for one disc (within 0.05%), against finer sampling (it converges and never rises), and against the table edge (a base may not be placed beyond it).

Over 1,500 random multi-wall scenes and 1,000 scenes with a friend beside the target, run by hand with `AUDIT_SCENES` and `AUDIT_SEED`, no disagreement was found; the checked-in tests use 120 and 80 scenes. Measured: the planner was at most 0.016" longer than the reference and never shorter by more than 0.0012". Not tested: obstacles other than rectangles and discs, routes through more than three walls, and Difficult terrain as a reason to choose a different route.

## Server and records (M6)

- **Dice fairness.** A signed record proves who wrote it and that its events follow the rules from the recorded dice. It does not prove the dice were fair; there is no commit-reveal scheme.
- **Durability** is guaranteed on Linux only. On macOS and Windows, records are flushed but folder flushes are best effort. The CLI's session files are replaced atomically, but their folder is not flushed.
- **Plain `ws://`.** Seat tokens travel unencrypted. Someone who can read the traffic can take a seat; with token rotation, a thief who confirms a token first can lock the owner out of that seat. Run the server behind TLS (`wss://`) where that matters.
- **No restore after a restart.** A match in progress when the server stops is recorded as aborted (graceful stop) or unrecorded (crash), and cannot be continued.
- **No retry of a failed record write.** The match is left with an open marker and becomes `Unrecorded` on the next start.
- **No cumulative pause cap.** Each disconnect has its own five-minute deadline, but separate disconnects can pause one match many times.
- **One rules version** is verifiable at a time; records from other versions are `Unverifiable`.
- **The signing key's owner** is not checked: the key and `keys/` must be owner-only and not links, but the server does not check that the owner is the user it runs as (another user's owner-only key cannot be read by a non-root server anyway). The data folder above `keys/` is not checked.
