# Maps

Since stage S1 of the trench and maps plan, a battlefield layout is a data file in `content/maps/<id>.json`, not
C# code. The format is described at the top of `src/Sim/MapFiles.cs`.

- **What a map holds:** the table size, terrain pieces, two deployment zones, which pieces are Objectives, and the
  scenario ids it was built for. It holds no rules: scoring, turns and roles belong to the scenario.
- **Who reads it:** the server, through `Sim.ScenarioSetups`. The finished `Battlefield` goes into the match setup,
  so it is hashed, recorded, signed and sent to both clients exactly as before. Clients never read map files.
- **Checks:** every check the rules types already make (ids, bounds, towers, objectives), plus map checks: pieces
  may touch but not overlap, unknown fields are refused, and the file name must equal the id.
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

## Maps

| Id | Scenario | Notes |
| --- | --- | --- |
| `claim-no-mans-land` | Claim No Man's Land | The fixed layout (U32) |
| `claim-no-mans-land-towers` | Claim No Man's Land | The same layout with two 2" towers and, since stage S3, six 1" trench sections in the deployment zones (the Test Center's default map). A test layout closer than No Man's Land spacing, so it names no archetype |

`ClaimNoMansLand.Create` and `CreateWithTowers` in Rules.Core build the same two layouts in code. They stay as the
reference: `MapFileTests` checks the files match them, so setups, state hashes and record fingerprints are unchanged.

## Adding a map

1. Add `content/maps/<id>.json`.
2. Add a test that loads it and checks what it is for. `MapFileTests` checks it keeps the set-up rules.

The rules engine does not change.
