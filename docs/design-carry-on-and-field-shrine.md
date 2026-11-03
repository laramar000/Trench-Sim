# Design: charge carry-on and Field Shrine (draft for the owner, 2026-11-03)

**Status (2026-11-03):** section A (carry-on) is built locally as U126, with decisions A1 and A2 as recommended; the Godot key is not bound yet. Section B (Field Shrine) stage 1 (state, deployment, geometry, Morale) is built locally as U127, decisions B1 to B4 as recommended; stages 2 (attacks and blasts) and 3 (client and roster panel) are not started.

Nothing here is built. Both features follow the standing rule: design first, then a stage with tests, pushed after the
essential check. Rule readings are paraphrased from the Digital Rulebook 1.0.2 (charge moves p. 37, Suicidal Bravery
p. 153, Field Shrine p. 82); no book text is copied.

## A. Charge carry-on past contact (U47, Suicidal Bravery)

**The rule.** A charging model stops as soon as it touches its target. It may carry on if the rest of its move lets it
stay in contact with the target and finish within 1" of another enemy model (p. 37).

**Today.** `Charging.Plan` follows the shortest route and stops at first contact (U47). `ChargeMoved` is replanned by
the reducer from the prior state and the recorded Charge Bonus, so any new move must be a pure function of the same
inputs. The Suicidal Bravery deed (U123) already fires whenever a successful charge ends within 1" of a second enemy,
so a carry-on needs no change to the deed.

**Design.**
1. `ChargeModel` gets an optional `CarryOn` flag (init, left off the wire when false). `ChargeDeclared` carries it, and
   `RollInProgress.CarryOn` holds it (hashed only when set). With the flag false, every plan, event, hash and pin is
   exactly as now.
2. With the flag set, after the route reaches first contact with allowance left, the planner looks for a stop point on
   the ring of 128 contact points the planner already builds around the target (radius contact + 1 thousandth). It
   walks the ring both ways from the contact point, one ring step at a time, and takes the nearest ring point that is
   within 1" (edge to edge) of some enemy other than the target. Equal distance goes to the lower ring index.
3. The slide must keep every leg legal by the exact tests of `Movement.CheckPath`: no overlap with other bases or
   blocking terrain, on the table, and a total cost within the allowance. Ring chords dip about 0.0005" inside the ring
   at the largest base pair, which the extra thousandth of ring radius already covers. The leg limit stays 64.
4. If no ring point qualifies, the charge ends at first contact as today. The flag then changes nothing.
5. The reducer replans with the flag from state and refuses any other path, as it does now.
6. Client: the charge confirm bar gets a "Carry on past contact" toggle, shown only when a preview finds a qualifying
   point. The preview draws the slid path. CLI: `charge A1 B1 carry`.

**Tests.** Flag false equals today's plan for the audit scenes and every pin. Flag true: a scene with a second enemy
beside the target ends within 1" of it; the path is legal leg by leg and within cost; both directions and the tie rule;
no qualifying point leaves the charge at first contact; a forged longer path is refused; Suicidal Bravery completes.

**Risk.** The planner has heavy audit tests (brute-force reference router). The new code is gated by the flag, so those
only need to keep passing unchanged. Cost: about 300 lines plus tests.

**Decisions.**
1. Carry-on as an optional flag on the charge (recommended: keeps the player's choice and every existing pin), or
   automatic whenever it qualifies (simpler, but changes pins and removes a choice the book words as optional).
2. Choose the stop point as the nearest qualifying ring point (recommended), or let the player click the point.

## B. Field Shrine (equipment, p. 82)

**The rule.** After its carrier is deployed, the owner may also deploy the Field Shrine anywhere wholly in their zone.
It is an Impassable piece on a 40 mm base, at least half an inch tall. It is not a model. While on the table, each
shrine adds 3 to the count of the owner's models that are not Down or Out of Action in the Morale Phase, up to +9. Either
side can attack it as if it were an enemy model; one hit, or being inside a blast, removes it with no Injury Roll. The
carrier no longer carries it once deployed.

**Design.**
1. State: `MatchState.Shrines` (id, seat, position), hashed only when present. A new `ModelProfile.FieldShrine` flag,
   set by `MatchReadiness` for a model carrying the equipment (the piece moves into `PlayedEquipment`).
2. Deployment: when a model with the flag is placed, the owner gets a `PlaceShrine` decision with `PlaceShrine(seat, model,
   at)` and `DeclineShrine(seat, model)`. Events `ShrineDeployed` and `ShrineDeclined`. The base must lie wholly in the
   zone and not overlap any base, shrine or Impassable terrain.
3. Geometry: a shrine is an obstacle for movement, placement and the charge planner (nobody may cross or end on it),
   so it joins the obstacle lists as a neutral base. It is not a model for engagement, interposition or Initiative.
4. Attacks: `Shoot`/`FightModel` and the blast sweep accept a shrine id as a target. A ranged or melee Success rolls as
   usual (range and sight to its base, no cover), and a hit emits `ShrineDestroyed` instead of an Injury Roll. A blast
   removes every shrine whose base is inside the radius with sight to the blast centre. Free attacks, charges and
   retreats never involve shrines.
5. Morale: the half test uses the warband's size plus the shrine bonus. It is the same shape as the standing-count rule:
   `ceil((size + bonus) / 2)` models Down or Out of Action are needed, bonus = min(9, 3 x owner's shrines).
6. Client: a placeholder shrine piece on the board, a "Place Field Shrine" decision bar, targeting by click, log and
   narration lines. The Godot piece is decorative only.
7. The roster side: Field Shrine moves from "refused" to "played" in the builder's "For a match" panel.

**Tests.** Deployment (in zone, no overlaps, decline, only the carrier's seat); movement and the charge planner treat it
as Impassable; a ranged hit, a melee hit and a blast each remove it without an Injury Roll; Morale thresholds with 0,
1, 3 and 4 shrines; forged logs (a shrine hit with an Injury Roll, a shrine deployed outside the zone, a bonus over 9);
wire round trips, hash coverage, replay.

**Risk.** It touches the command and event set, targeting in three attack pipelines, the obstacle lists in four
geometry modules, Morale, the wire, the hash and the client. Two to three stages: (S1) state, deployment, geometry and
Morale; (S2) attack and blast removal; (S3) client and roster.

**Decisions.**
1. Sight: the shrine does not block sight or give cover, as other bases (recommended, matches U3), or it blocks sight
   as a half-inch terrain piece (needs a height in the sight code).
2. Attackable by ranged, melee and blast together (recommended), or ranged and blast first with melee in a later stage.
3. Deploying it optional (recommended, the book says "can"), or forced.
4. Morale formula as in point 5 (recommended), or subtract the bonus from the Down count instead.
