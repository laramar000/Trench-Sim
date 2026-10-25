# Line of sight

Since stage L2 of the line-of-sight plan (2026-10-25) the rules see in three dimensions, with heights. The code is
`src/Rules.Core/Geometry/HeightSight.cs`; the readings are U77 (sight) and U78 (cover) in `docs/interpretations.md`.

## How it works

- **Heights.** A figure is 1.2" tall. The shooter's eye is 1.1" above its base. Terrain is a box from the ground up:
  Low 0.25", Cover 0.75", Wall 3", and a tower its climb height. The client draws terrain at these same heights, and a
  test (`BoardSceneHeightTests`) keeps them equal.
- **Rays.** Nine rays run from the eye to nine points on the target: three heights (legs 0.3", body 0.7", head 1.1"
  above its base), each left, middle and right across the line, at 0.8 of the base radius.
- **Blocking.** A ray is blocked when it passes through the inside of a box. Grazing a face, edge or corner does not
  count. Other models never block. The platform a model stands on never blocks sight to or from it.
- **Result.** Full (all nine seen), Partial (some) or None (none), with every ray, its heights and what blocked it.
- **Cover (p. 38).** The piece is at least 1/2" high, the target touches it, it is as wide as the base across the line,
  and it hides at least one point while the target is still partly seen.
- **Who decides.** The server, through `Cover.Of`, for shots, forced shots, charges and Fight. The client's previews
  call the same function, so they cannot disagree. No Godot raycast decides anything.

## Elevated position (stage L3)

+1 DICE to a Ranged Attack when the attacker's base stands at least 3" higher than the target's (p. 43, U79). Exactly
3" counts. It is recorded on the attack declaration and shown in the dice preview with its page. Towers are under 3", so
it cannot happen on today's maps; trenches make it reachable.

## On screen (stages L4 to L6)

Everything shown comes from the same rules function the server uses (`Cover.Of`). Nothing on screen decides anything.

- **Target icons (L4).** In Shoot, Charge and Fight every enemy gets an eye above it: open for Full, half closed for
  Partial, struck through and dimmed for None, with a gold shield when it would be in cover. Black and white in high
  contrast. The icons are drawn from 16 x 16 text masks in `Client.Core/TargetSights.cs` (no image files).
- **Shot guide (L5).** Pointing at or choosing a Shoot target draws a thin line from the shooter's eye to the target's
  body point, solid while clear and broken from where it is blocked; a 3 x 3 figure of the nine points (head, body,
  legs) above the target with GUIDE beside it; and the cover piece outlined in gold. The panel lists the rules' facts
  under "Rules (the server decides):", the hit-roll modifiers with their pages, and the guide's points in words.
- **Movement visibility (L6).** While choosing where to move, the same icons show how the model would see each enemy
  from the end of the path, at the height of that point. Movement rules are unchanged. Measured at about 0.2 ms per
  destination with five models on the table (`DestinationSightsTests`), against an 80 ms pointer interval.

## What changed in stage L2

The ten ruled scenes (`tests/Rules.Tests/Scenes`), before and after. Each scene's ruling says what it was before.

| Scene | Before L2 (top-down) | Since L2 (heights) | Why |
| --- | --- | --- | --- |
| 01 open field | Full 4/4 | Full 9/9 | Nothing between |
| 02 barricade cover | Full 4/4, cover | Partial 3/9, cover | The 0.75" barricade hides legs and body; the head shows |
| 03 wall corner | Partial 1/5, cover | None 0/9 | The side points sit at 0.8 of the radius, so the corner hides them all |
| 04 narrow gap | Partial 2/4 | Partial 3/9 | Only the middle column passes the slit |
| 05 behind a long wall | None 0/4 | None 0/9 | Unchanged |
| 06 high ground over a wall | Full 5/5 | None 0/9 | A 3" platform cannot see down behind a 3" wall (U20 shortcut gone) |
| 07 roof target behind a wall | None 0/4 | None 0/9 | Unchanged |
| 08 roof to roof past a tower | None 0/4 | Full 9/9 | Both roofs and the tower are 3", so the rays pass over it |
| 09 roof edge boundary | Full 4/4 | Full 9/9 | Unchanged |
| 10 grazing corner | Full 1/1 (one fixed ray) | Partial 6/9 | The middle ray still only grazes; the right column passes through the wall |

Seeded simulated matches whose answers changed with the new sight: ranged seeds 4 and 12, melee seed 29 (new final
hashes pinned). Stored match records still replay and verify: commands are checked when they are made and the events
carry the results.

## Known limits

- A slit narrower than the spacing of the points can hide a target the table would show a sliver of.
- The side points at 0.8 of the radius see less round a corner than the old rim points did.
- A Down model is the same height as a standing one.
