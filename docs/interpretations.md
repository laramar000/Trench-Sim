# Interpretations (M3 geometry)

Every place where the software had to pick a reading of the rules. "Confirmed" rows were checked against the Digital Rulebook 1.0.2 page image; "provisional" rows are Trench-Sim choices that an official ruling or playtesting may change. Wording is paraphrased. The game rules this file does not touch (Down penalties, critical hits, Bloodbath dice) are unchanged.

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
| U12 | Movement cost: each leg is rounded up to a millionth of an inch, the legs are added, and the total is rounded up once to the next thousandth. A path never costs less than its true length, a single leg of exactly the allowance is legal, and splitting a path into many short legs changes the cost by only a few millionths of an inch per leg. (An earlier plan rounded every leg up to a thousandth, which charged 0.600" for 300 diagonal legs of 0.001" whose true total is 0.4243"; the test `ManyShortLegsCostTheSameAsOneLongLegToTheThousandth` records the comparison.) |
| U13 | A ray that only grazes an edge or corner does not count as crossing. |
| U14 | Terrain heights are replaced by profiles chosen at setup: Low (under 1/2"), Cover (1/2" to 1"), Wall (substantial). The thresholds come from pp. 38-39; assigning real terrain to a profile is the scenario author's job. |
| U15 | "On or in contact": the target's base touches or overlaps the piece's footprint. |
| U16 | Sample points on the target are a fixed set (centre and facing rim points); the sampler is replaceable. |
| U17 | High ground: more than half the base is within a walkable High-surface block (lowest id wins). |
| U18 | Walkable platforms may be entered and left anywhere along their footprint at no extra cost; no climbing, jumping or falling. |
| U19 | A platform never obstructs sight for a model standing on it, or sight to a model on it. |
| U20 | An attacker with high ground is not obstructed by Ground-surface Walls (XCOM-style gameplay abstraction, not 3D). |
| U21 | Targets on high ground get no extra shelter from ground attackers; sight is asymmetric by design. |
| U22 | Cover and line of sight share one obstruction rule; a piece that does not obstruct gives no cover as a Wall. |
| U23 | Cover-profile pieces give cover to attackers on either level, when the cover conditions hold. |
| U24 | The elevation attack bonus is not implemented in M3. |

## Terrain combinations (checked when a piece is built)
Low and Cover: ground surface, walkable (non-walkable only if Impassable). Wall on the ground: not walkable. Wall with a High surface: walkable platform or non-walkable tower. Impassable: never walkable. Difficult and Dangerous: walkable only. High surface needs a Wall. Walkable High platforms may not overlap each other. Anything else throws ArgumentException.

## Golden scenes
Ten scenes in `tests/Rules.Tests/Scenes/*.json`, each with its written ruling and a `status`: scene 01 is confirmed (p. 30); the other nine rest on provisional readings named in their `basis`. The sight sampler (U16) is the part most likely to change; the scenes are the check on it.

## Not an interpretation, but a limit
Rectangles only (no polygons; the `Footprint` type isolates this so polygons can be added later without changing movement or sight code); no climbing, jumping, falling, charge or retreat geometry; positions are not in MatchState until M4.
