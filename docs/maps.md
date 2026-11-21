# Maps

Since stage S1 of the trench and maps plan, a battlefield layout is a data file in `content/maps/<id>.json`, not
C# code. The format is described at the top of `src/Sim/MapFiles.cs`.

- **What a map holds:** the table size, terrain pieces, two deployment zones, which pieces are Objectives, and the
  scenario ids it was built for. It holds no rules: scoring, turns and roles belong to the scenario.
- **Who reads it:** the server, through `Sim.ScenarioSetups`. The finished `Battlefield` goes into the match setup,
  so it is hashed, recorded, signed and sent to both clients exactly as before. Clients never read map files.
- **Checks:** every check the rules types already make (ids, bounds, towers, objectives), plus map checks: pieces
  may touch but not overlap, two separate Cover or Wall pieces (towers included) may not touch at all, along an edge
  or at a corner, because sight would pass along the seam (U94; Low pieces and trench sections may still touch, see
  U94 for why), unknown fields are refused, and the file name must equal the id.
- **Shapes (stage S2):** a piece is one rectangle (`rect`) or, for a ground piece, joined rectangles (`parts`): two
  to 16 rectangles that do not overlap and are joined along shared edges, for L, T and plus shapes. Seams between
  parts are inside the piece. Platforms, roofs, towers and deployment zones stay one rectangle. A one-rectangle piece
  is stored, sent and hashed exactly as before; joined parts travel as a list of rectangles.
- **Trenches (stage S3):** a `low` piece with `"depth"` (1/2" to under 3") is a trench section (U80 to U83 in
  `docs/interpretations.md`). All trenches on a map share one depth and nothing may overlap one; sections that touch
  form a network.
- **Set-up rules (stage S4):** `MapDefinition.Problems(baseRadius)` (Rules.Core `MapLayout.Check`) lists what a layout
  breaks; every content map must have none, for the largest base in the content (`MapFileTests`).
  - Blocking terrain (p. 146, U85): from anywhere a base fits wholly in its deployment zone it must be able to move,
    on the ground, until its centre is out of the zone, and to every Objective. Towers and unwalkable pieces block.
  - Archetype spacing (pp. 65-67, U84, U86), only for a map that names its `archetype`, edge to edge: No Man's Land
    3" between pieces and trenches wholly in one zone; Decimated Ruins 3" between pieces, ruins (`"ruin": true`)
    chained within 9", no trenches; Trench Lines one trench network from an edge to the opposite edge, other pieces
    1" from trenches and 3" from each other. Touching trench sections are one trench.
- **Scenario and map (stage S5):** a scenario's rules are a `ScenarioDefinition` (Rules.Core) with what it needs
  from a map (U87). `ScenarioSetups.OnMap` puts a setup's models on another map, refusing a map not made for the
  scenario or not fitting it. `CreateMatch` may name a `map`; the record keeps the map id and `mapHash`.

## Maps

| Id | Scenario | Notes |
| --- | --- | --- |
| `claim-no-mans-land` | Claim No Man's Land | The fixed layout (U32) |
| `trench-warfare` | Trench Warfare | Stage S6: three 8" trench sections in the east zone (the defender's, U88), No Man's Land terrain between the zones. Server names `ranged-trench-warfare`, `melee-2v2-trench-warfare` and so on |
| `claim-no-mans-land-towers` | Claim No Man's Land | The same layout with two 2" towers and, since stage S3, six 1" trench sections in the deployment zones (the Test Center's default map). A test layout closer than No Man's Land spacing, so it names no archetype |
| `ruined-village`, `shell-holes`, `crossfire` | Claim No Man's Land | Stage S7 presets: the five Objectives on the printed marker positions (U32) as ruined houses, shell holes or sandbag positions |
| `dust`, `broken-line` | Trench Warfare | Stage S7 presets: Dust has two L-shaped trench sites and a connector; Broken Line has three straight sections behind a cratered No Man's Land |

`ClaimNoMansLand.Create` and `CreateWithTowers` in Rules.Core build the same two layouts in code. They stay as the
reference: `MapFileTests` checks the files match them, so setups, state hashes and record fingerprints are unchanged.

## Generated maps

Stage S8: a map can also be made from a seed. Its id names the kind, the generator version and the seed, for
example `generated-trench-lines-v1-42`, so a match record that names the map says how to make it again, and the
record's map hash says it was that map (`trench-cli verify` makes it again to check the setup). The same id gives
the same map on every machine and build: the generator uses its own SplitMix64 numbers and whole half-inches,
and a test pins the hash of one map. A change that moves any map must raise `MapGenerator.Version`.

Every generated map keeps the set-up rules of its archetype for a base up to 1" radius (each try that does not is
thrown away and the next one, from the same seed, is made). `MapFiles.ToJson` writes one as a map file.
Known limitation: a generated map is used without passing through the map loader, so the U94 check on touching
Cover and Wall pieces does not run on it; the generator's spacing (below) keeps such pieces apart.

| Kind | Archetype | What it makes |
| --- | --- | --- |
| `trench-lines` | Trench Lines (p. 67, U89) | One trench line of 4"-12" sections, 2.5" or 3" wide, from one edge to the opposite edge, with bends, sometimes a dead end or a crossroads; then two ruined corners, two areas of difficult ground and two landmarks, 1" from trenches and 3" from each other. No scenario this build plays uses Trench Lines yet, so the server makes these maps but no match takes one |
| `claim-no-mans-land` | No Man's Land (p. 65, U90) | For Claim No Man's Land: the five Objectives on the printed marker positions (U32), up to two trench sections in each zone, then ruined buildings, ruined corners, difficult ground and landmarks 3" apart |
| `trench-warfare` | No Man's Land (p. 65, U88, U90) | For Trench Warfare: the defender's three 8"+ sections in one zone, the other pieces between the zones |
| `decimated-ruins` | Decimated Ruins (p. 66, U90) | Six ruined buildings and two ruined corners chained 3"-9" apart, then difficult ground and landmarks. No scenario this build plays uses it yet |

Stage S9: `generated-claim-no-mans-land` or `generated-trench-warfare` (no version or seed) asks the server for a new
map: the server picks the seed and the record names the map, for example `generated-claim-no-mans-land-v1-1234`.
The client's Map picker offers these as "Random map (a new one each match)".

## Adding a map

1. Add `content/maps/<id>.json`.
2. Add a test that loads it and checks what it is for. `MapFileTests` checks it keeps the set-up rules.

The rules engine does not change.
