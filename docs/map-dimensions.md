# Battlefield dimensions: research and proposal (2026-11-03, for review)

Nothing in the game has been changed. This is the research the owner asked for before the carry-on and Field Shrine work.
Page numbers are the printed pages of the Digital Rulebook 1.0.2; the wording is paraphrased.

## 1. What the rulebook recommends

| Source | Dimension |
|---|---|
| Quick start, p. 8, and p. 9 | A battlefield of roughly 3 ft by 3 ft (90 cm) or 4 ft by 4 ft (120 cm), or any surface with 24" between the two forces at the start. |
| p. 22 (Battlefield) | Most games fit a kitchen table at least 30" wide; some need 36" by 36" or 48" by 48". |
| p. 58 (Terrain) | A dining table is enough for most scenarios; the larger ones need a 36" or 48" square board; the suggested home-made board is 48" square. |
| Scenario I, Claim No Man's Land, p. 151 map | Zones labelled 6" deep with 24" between them: 6 + 24 + 6 = 36" along the deployment axis. The other axis is not printed. The book recommends this scenario for a first game (p. 23). |
| Scenario V, Armoured Train, p. 164 | 48" by 48". |
| Scenario X, Don't Breathe, p. 187 | 48" by 48". |
| Scenario XI, The High Ground, p. 192 | At least 36" by 36". |
| Scenarios II, III, IV, VI, VII, VIII, IX, XII | No size is printed in the text. Their maps carry labels (for example 24" gaps, a 28" label on VI and VIII, 12" and 8" marks on IV) whose meaning is lost when the page is read as text. |

**Standard recommended size:** 36" by 36" (3 ft by 3 ft). **Alternative:** 48" by 48" (4 ft by 4 ft) for the larger scenarios, and as the
roomier general option. Minimum for any game: 24" between the forces, and a surface at least 30" wide.

**Official sources checked.** Only the rulebook text. The publisher's site returned an access error from this session's
network, so no FAQ or errata could be read. The map labels need a look at the page images, which I have not seen.

## 2. What the game has now

- All eight maps in `content/maps` (Claim No Man's Land and its tower variant, Broken Line, Crossfire, Dust, Ruined Village,
  Shell Holes, Trench Warfare) and the generator (`Sim/MapGenerator.cs`, `TableSize`) are 36" by 36".
- Every map has two 6" deep zones along the west and east edges, 24" apart. Lengths are integers in thousandths of an inch,
  so one in-game unit is one inch (U1).
- Scenario I matches the book. U32 already records that the book prints no table size for it.

## 3. Discrepancies and open points

1. **Scenario IV, Trench Warfare.** Its map carries 24" and 12" labels and three 8" trench sections. The game uses the same
   36" table with 6" zones as the other maps. I cannot tell from the text whether the book's zones are 12" deep (which would
   not fit 6 + 24 + 6) or whether 12" is something else. Needs the page image. **Possible discrepancy.**
2. **The 48" scenarios** (V, X) and the "at least 36"" scenario XI are not built, so there is no 48" support to compare.
   The `Battlefield` type already takes any width and height, so a larger map is a data change, not an engine change.
3. **Camera and zoom.** I have not run the Godot client in this session (no Godot here), so I cannot say whether the camera
   frames a full 36" or 48" table without distortion. **Unverified.**
4. **No in-game size choice.** The menu has no way to pick 3 ft or 4 ft; each map fixes its own size.
5. **Screenshot.** A full-battlefield screenshot needs the Godot client. Not possible in this session.

## 4. Proposal (for approval; no dimension is changed until you approve)

1. Keep 36" by 36" as the default (it is the book's recommended 3 ft table and matches Scenario I's printed 6 + 24 + 6).
2. Treat 48" by 48" as the official alternative. Alternatives work per map: a map file states its own width and height, a
   scenario states the sizes it allows, and the Play page offers a size only for scenarios that allow more than one. Nothing
   existing is resized or stretched.
3. Before any change: you look at the Scenario IV map page and tell me its zone depth and table size, and I verify the
   camera in a Godot run on Windows (or you do), with a screenshot of the full table.
4. Tests to add: every shipped map's size equals its stated table; one unit equals one inch (a 1" path costs 1.000"); zones
   lie wholly inside the table and sit 24" apart on the standard maps; placement and movement refuse points past each edge;
   a generated map keeps its declared size; the existing geometry, movement and charge audit tests pass unchanged.
5. The generator keeps 36" and gains a size parameter only with the 48" stage.

## 4a. Decision (owner, 2026-11-03)

**36" by 36" is the default and 48" by 48" is the alternative.** Done so far: the dimension tests
(`tests/Server.Tests/BattlefieldDimensionTests.cs`: every shipped and generated map is 36" by 36", zones 6" deep and 24"
apart, one unit is one inch, edges stop models). No map or dimension was changed. Still open: the Scenario IV zone depth
(needs the page image), the 48" support (a per-scenario allowed-size list, a size parameter for the generator, and a Play
page choice shown only where a scenario allows two sizes), and the camera check with a screenshot of the full table.

## 5. Order of work

Review this note first. Then, with the dimension question settled, carry-on (A1, A2 recommended), then Field Shrine in three
stages (B1 to B4 recommended), each with a full build and test run, and nothing pushed until you have reviewed it.
