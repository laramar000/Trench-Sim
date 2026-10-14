# Known gaps (M4 Slice B)

What the simulated match does not do after Slice B. Each item is a decision to defer, not an oversight. Rule references are to the Digital Rulebook 1.0.2.

## Rules not implemented
- **Melee extras** (Slice B implements Charge, Retreat, Fight and shooting into melee, U46 to U56): no Off-Hand or second Melee Weapon, CLEAVE, BLOCK, Fear, Diving Charge, ASSAULT, climbing or jumping charges, detour charge routes (the charge route is a straight line), or the optional charge continuation. Down models are measured from the base only (U55).
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
