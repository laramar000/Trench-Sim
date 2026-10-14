# Interpretations (M3 geometry, M4 match rules)

Every place where the software had to pick a reading of the rules. "Confirmed" rows were checked against the Digital Rulebook 1.0.2 page image; "provisional" rows are Trench-Sim choices that an official ruling or playtesting may change. Wording is paraphrased. M4 Slice A rows (C9 to C14, U30 to U45) are at the end of each table. Rulebook wording discrepancies and their rulings are in `docs/rules-notes.md`.

## Confirmed by the rulebook
| Id | Rule | Page |
|---|---|---|
| C1 | Distances are measured from base edge to base edge | 30 |
| C2 | "Within" means the nearest points are no further apart than the distance | 30 |
| C3 | A model can see a target if it can see any part of it (base, hands, feet and carried items excluded); sight is all-round; seeing only part is partial line of sight | 30 |
| C4 | A placed base may not overlap impassable terrain or another base; more than half of it must be on the battlefield or in terrain | 31 |
| C5 | Move: path no longer than Movement; no moving within 1" of an enemy unless charging; a model starting within 1" of an enemy must stay within it or retreat; cannot leave the battlefield; friendly models only passed if fully cleared | 35 |
| C6 | Cover: terrain at least 1/2" high, at least as wide as the base, between the models and partially blocking sight | 38 |
| C7 | "In" terrain means more than half the base is within it | 38 |
| C8 | Terrain up to 1" high (and trench walls up to 3") count as Open for walking; climbing and jumping are separate (out of scope) | 39-40 |
| C9 | A Critical Success on a melee or ranged attack grants +1 INJURY DICE to the subsequent Injury Roll. Confirmed by the project owner on 14 October 2026. The "+1 DICE" printed on pp. 44-45 is a rulebook wording discrepancy (W1 in rules-notes.md); Injury Dice are the implementation. | 15, 18, 24, 44-45, 48, 54 |
| C10 | Ranged Attack: no attack within 1" of an enemy; target in Line of Sight and in range edge to edge; Long Range (more than half the range) -1 DICE; Cover -1 DICE; Success hits and an Injury Roll follows | 42-44 |
| C11 | Blood Markers: one per wound, at most 6 per model; the opponent may spend a rolling model's markers as -1 DICE on its Success Roll; the attacker may spend the target's markers as +1 INJURY DICE on the Injury Roll | 27-28 |
| C12 | Each type of ACTION once per activation; a Dash is a Risky Success Roll taken before moving, before or after a Move; a failed Dash ends the activation | 34 |
| C13 | A Down model stands back up when next activated, with Movement halved for the rest of that activation | 20, 49 |
| C14 | Scenario I: four Turns; 2 VPs per Objective controlled at each Turn's end; control by models on, in or within 1" of the piece, models on the piece winning over numbers nearby; immediate win by elimination or enemy flight; otherwise more VPs wins | 152-153 |

## Provisional (Trench-Sim choices)
| Id | Choice |
|---|---|
| U1 | Lengths are integers in thousandths of an inch; 1 mm = 0.03937" rounded to nearest thousandth (25.4 mm per inch). |
| U2 | The base footprint stands in for the model body in line of sight (the body cannot be separated from its base in 2D). |
| U3 | Other models never block line of sight. |
| U4 | "As wide as its base": the terrain's extent across the attacker-target line is at least the base diameter. |
| U5 | "Between and partially blocking": at least one sight ray crosses the piece (a Wall must also obstruct that ray). |
| U6 | Battlefield edge and terrain "more than half" tests use an exact bracketed circle-versus-rectangle method; where it cannot decide within about 0.001" it refuses (conservative). |
| U7 | The move-end clause ("no further from the start than Movement") is treated as already covered by the path-length limit. |
| U8 | Difficult terrain doubles the part of a leg inside it, proportionally for a partial crossing. |
| U9 | Substantial (Wall) terrain on the ground cannot be entered or crossed, because climbing is out of scope. |
| U10 | A path may cross a friendly base but may not end overlapping one. |
| U11 | Down models obstruct like any other base. |
| U12 | Movement cost: each leg is rounded up to a millionth of an inch, the legs are added, and the total is rounded up once to the next thousandth. A path never costs less than its true length, a single leg of exactly the allowance is legal, and splitting a path into many short legs changes the cost by at most about three millionths of an inch per leg, never downward. The cost is held in an Int128 and is reported clamped to the largest Length, so an absurdly long path is refused as too long rather than throwing. (An earlier plan rounded every leg up to a thousandth, which charged 0.600" for 300 diagonal legs of 0.001" whose true total is 0.4243"; the test `ManyShortLegsCostTheSameAsOneLongLegToTheThousandth` records the comparison.) |
| U13 | A ray that only grazes an edge or corner does not count as crossing. |
| U14 | Terrain heights are replaced by profiles chosen at setup: Low (under 1/2"), Cover (1/2" to 1"), Wall (substantial). The thresholds come from pp. 38-39; assigning real terrain to a profile is the scenario author's job. |
| U15 | "On or in contact": the target's base touches or overlaps the piece's footprint. |
| U16 | Sample points on the target are a fixed set (centre and facing rim points); the sampler is replaceable. |
| U17 | High ground: more than half the base is within a walkable High-surface block (lowest id wins). |
| U18 | Walkable platforms may be entered and left anywhere along their footprint at no extra cost; no climbing, jumping or falling. |
| U19 | A platform never obstructs sight from or to a model standing on it. "Standing on it" means more than half the base is on it (high ground) or the base centre is strictly inside its footprint (a ground model overlapping a corner); touching or overlapping its edge from outside does not count. |
| U20 | An attacker with high ground is not obstructed by Ground-surface Walls (XCOM-style gameplay abstraction, not 3D). |
| U21 | Targets on high ground get no extra shelter from ground attackers; sight is asymmetric by design. |
| U22 | Cover and line of sight share one obstruction rule; a piece that does not obstruct gives no cover as a Wall. |
| U23 | Cover-profile pieces give cover to attackers on either level, when the cover conditions hold. |
| U24 | The elevation attack bonus is not implemented in M3. |
| U25 | Deployment: a base must lie wholly within its zone (touching the edge is allowed). The "wholly within" wording comes from text extraction of p. 146 and was not separately confirmed against the page image. |
| U26 | Difficult terrain doubling ("every 1\" counts as 2\"") comes from the keyword text, not a visually checked page. Dangerous terrain has no effect in M3 (the flag exists only). |
| U27 | Leaving the battlefield is checked at the waypoints only. The set of base positions with more than half the base on the table is convex, so a straight leg between two legal waypoints cannot dip below half. |
| U28 | Cover from a Cover-profile piece needs a sight ray to cross it, and rays end on the target, so a model standing inside the piece gets cover even if most of the piece lies behind it, and a piece merely touching the far side of the base does not. |
| U29 | CheckPath does not validate the mover's starting position or cap the number of waypoints; callers (the M4 command layer) own both. The start-within-1\" test uses the mover's current position, and retreat is not implemented. |
| U30 | A Down model stands up automatically, as an event right after its activation starts, rather than as an action it chooses. Halved Movement is rounded down to a thousandth and limits both Move and Dash. |
| U31 | The Down penalty is -1 DICE on Success Rolls taken for a Down model (the reading that agrees with both chapters, question 2). A Down model always stands before acting, so in Slice A the penalty never changes a roll. |
| U32 | The Claim No Man's Land battlefield is a fixture: 36" x 36" table, 6" deep zones along the west and east edges (24" apart), five 4" x 4" Cover rubble Objective pieces centred on the printed marker positions (6" in from each zone edge and the table edge, plus the midpoint), and extra non-Objective walls and rough ground. The book prints none of the table size, zone depth or terrain; the terrain roll-off and set-up are not played. |
| U33 | Blood Marker spending is a pause with no dice rolled: before the Success Roll of a Dash or Shoot by a model holding markers (the opponent chooses 0 to all), and before an Injury Roll against a model holding markers (the attacker chooses 0 to all). Nothing is asked when there is nothing to spend. Spent markers leave the model at once. |
| U34 | An Out of Action model is taken off the table (no position) and keeps its marker count, which nothing reads. |
| U35 | The elevated-attacker +1 DICE (3" or more higher, p. 43) is not applied. M3's discrete High level cannot measure 3"; each attack declaration records both models' levels so the bonus can be added later without changing the log format. |
| U36 | "On" an Objective piece means more than half the base is within it; "in or within 1\"" means the nearest point of the base is at most 1" from the piece. Down models count; Out of Action models are off the table and do not. If exactly one side has a model on the piece, that side controls it; otherwise the larger count of models on or within 1" does, and equal counts control nothing. |
| U37 | A Shaken warband's attack Success Rolls are Risky, like its Dashes. A failed Risky shot is a miss and then ends the activation. |
| U38 | Shooting at a model within 1" of one of the shooter's own models (the D6 1-3 friendly-target rule, p. 43) is refused as not supported. It cannot arise in Slice A, because no Move or Dash may end within 1" of an enemy. |
| U39 | "Models on the battlefield" for the elimination win means deployed models that are not Out of Action. The win is checked right after each Injury Roll. |
| U40 | Equal Victory Points after the final Turn are a draw (the book says "more Victory Points" wins and is silent on a tie). The outcome reason is VictoryPoints with no winner. |
| U41 | Objectives are scored once per Turn, after the Morale Phase and before the Turn ends; a Turn in which a warband flees ends the match unscored. |
| U42 | A weapon's range must be an even number of thousandths of an inch so that half the range, the Short/Long boundary, is exact. |
| U43 | A Move or Dash path has at most 16 waypoints (command size limit, checked before any geometry). |
| U44 | In a match with a battlefield, models are set up one at a time with PlaceModel at a point (base wholly within the zone, using M3 placement). DeployModel is refused there; PlaceModel is refused without a battlefield. |
| U45 | Slice A models have one ranged weapon. Weapon and Armour appear only as flat injury modifiers (Armour 0 to -3, weapon -3 to +3), combined by the M1 rule that negatives total at most -3. |

## Terrain combinations (checked when a piece is built)
Low and Cover: ground surface, walkable (non-walkable only if Impassable). Wall on the ground: not walkable. Wall with a High surface: walkable platform or non-walkable tower. Impassable: never walkable. Difficult and Dangerous: walkable only. High surface needs a Wall. Walkable High platforms may not overlap each other. Anything else throws ArgumentException.

## Golden scenes
Ten scenes in `tests/Rules.Tests/Scenes/*.json`, each with its written ruling and a `status`: scene 01 is confirmed (p. 30); the other nine rest on provisional readings named in their `basis`. The sight sampler (U16) is the part most likely to change; the scenes are the check on it. Known sampler limitation, recorded by `AGapNarrowerThanTheRaySpacingCanBeMissedByTheDefaultSampler`: rim points perpendicular to the line of sight are not tried, so a sliver of a wider target visible through a very narrow gap can be missed.

## Not an interpretation, but a limit
Rectangles only (no polygons; the `Footprint` type isolates this so polygons can be added later without changing movement or sight code); no climbing, jumping, falling, charge or retreat geometry. Positions entered the match state in M4.
