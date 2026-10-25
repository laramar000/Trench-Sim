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

## Maps

| Id | Scenario | Notes |
| --- | --- | --- |
| `claim-no-mans-land` | Claim No Man's Land | The fixed layout (U32) |
| `claim-no-mans-land-towers` | Claim No Man's Land | The same layout with two 2" towers |

`ClaimNoMansLand.Create` and `CreateWithTowers` in Rules.Core build the same two layouts in code. They stay as the
reference: `MapFileTests` checks the files match them, so setups, state hashes and record fingerprints are unchanged.

## Adding a map

1. Add `content/maps/<id>.json`.
2. Add a test that loads it and checks what it is for.

The rules engine does not change.
