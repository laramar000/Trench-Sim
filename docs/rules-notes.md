# Rules notes

Where each rule used in the first match lives in the official rules. Each item gets a short summary in our own words, under "Summaries of implemented rules", when a milestone implements it. Rulebook text is never pasted here.

- **Rules version targeted:** 1.0.2
- **Source:** Trench Crusade Digital Rulebook 1.0.2, the free PDF on the publisher's rules page (https://www.trenchcrusade.com/rules/)
- **Re-check due:** when version 1.1 is published (announced for late November 2026)

## How these references were gathered

On 14 October 2026 the rulebook PDF was read twice with an automated tool: once for its table of contents, and once for the page number printed at the top of the page where each heading actually appears. Where both reads named a heading, they gave the same page. The second read also found pages the table of contents does not list, such as Bloodbath Rolls on p. 47 and the Down effects on p. 49. The pages below are the printed body pages. The scenario pages (146 to 151) come from the table of contents only, because the second read did not reach that part of the PDF.

No person has checked these pages against a copy of the PDF yet. Before a milestone implements an item, confirm its pages against your own copy of the 1.0.2 PDF and set "Confirmed" to Yes.

The rulebook has a short "Core Rules" chapter (pages 14 to 21) and a longer "Comprehensive Rules" chapter (pages 22 to 51). The comprehensive chapter is the primary reference; core-rules pages are listed as secondary where they cover the same topic.

"Cover" and "Dash" have no headings of their own in the table of contents. Their pages come from the second read: cover is defined under Terrain (p. 38) and applied to ranged attacks on p. 43; Dash is described under Actions (p. 34).

## First-match subset

| Item | Primary reference (1.0.2) | Secondary | Implemented in | Confirmed |
| --- | --- | --- | --- | --- |
| Dice | Success Rolls, p. 24; Success Roll Table, p. 24; +Dice And -Dice, p. 25 | Core Rules, p. 15; Dice (D6s & D3s), p. 23; Re-Rolls, p. 31; Rolling Off, p. 31 | M1 | No |
| Injuries | Injuries, p. 46; Injury Roll Table, p. 48 | Injury Roll Table, p. 18 (Core Rules) | M1 | No |
| Markers | Blood Markers, pp. 27 to 28; Blessing Markers, p. 28; Bloodbath Rolls, p. 47 | Bloodbath Rolls, p. 19 (Core Rules) | M4 | No |
| Turn sequence | The Sequence Of Play, p. 32; 1. Initiative Phase, p. 32; 2. Activation Phase, p. 33 | Core Rules, pp. 14 and 20 | M2 | No |
| Actions | Actions, p. 34 (including Dash) | Actions, pp. 14 to 15 (Core Rules) | M4 | No |
| Movement | Movement, p. 35; Moves, p. 35; Charging, p. 36; Retreating, p. 37 | Measuring Distances, p. 30; Model Placement, p. 31; Fractions, p. 30 | M3 (geometry), M4 (actions) | No |
| Line of sight | Line Of Sight, p. 30; Points on the Battlefield or Terrain Pieces, p. 30 | Model Accuracy, p. 30 | M3 | No |
| Cover | Terrain, p. 38; applied to ranged attacks, p. 43 | Ranged Attack Modifiers, p. 16; Melee Attack Modifiers, p. 17 (Core Rules) | M3 | No |
| Ranged | Ranged Attacks, p. 42 | Ranged Attacks, Shooting Into Melee, Ranged Attack Modifiers, p. 16 (Core Rules) | M4 | No |
| Melee | Melee Attacks, p. 45 | Melee Attacks, Melee Attack Modifiers, p. 17 (Core Rules) | M4 | No |
| Down | Down effects, p. 49; Injury Roll Table, p. 48 | Down Results, p. 20 (Core Rules) | M4 | No |
| Morale | 3. Morale Phase, p. 50 | Morale Phase and Shaken Warbands, p. 21 (Core Rules) | M2 | No |
| Deployment | Scenario Format: Deployment, p. 146 | Scenario Format: The Battlefield, p. 146 | M4 | No |
| Scenario | I. Claim No Man's Land, p. 151 | Scenario Format: Game Length and Victory Conditions, p. 148; Winning The Game, p. 51 | M4 | No |

## Summaries of implemented rules

In our own words. Each names the code that implements it and the open questions a person should check against the PDF.

### Dice (M1)

Code: `src/Rules.Core/Dice/SuccessRoll.cs`, `RollRecord.cs`. Tests: `SuccessRollTests.cs`.

- A Success Roll is two six-sided dice added together.
- Each bonus die (+DICE) and each penalty die (-DICE) adds one extra die to the roll. Bonus and penalty dice cancel one for one first. With bonus dice left over, keep the two highest; with penalty dice left over, keep the two lowest.
- Results: 6 or less fails, 7 to 11 succeeds, 12 or more is a Critical Success.
- A Risky roll is made the same way; if it fails, the model's activation ends (or the ACTION ends, if taken outside the model's activation). The roll only reports this; ending the activation belongs to the turn sequence (M2).
- The rulebook states no limit on how many bonus or penalty dice can apply. The tests cover net -3 to +3, as the plan asks; the code accepts any count.

Not in M1: re-rolls (p. 31), rolling off (p. 31), and making every roll Risky when a warband is Shaken (M2).

### Injuries (M1)

Code: `src/Rules.Core/Dice/InjuryRoll.cs`. Tests: `InjuryRollTests.cs`.

- An Injury Roll is two six-sided dice, with bonus and penalty injury dice handled exactly as for Success Rolls, plus any flat injury modifiers.
- Negative injury modifiers together count for no less than -3. Positive modifiers have no limit. The -3 floor applies to the negatives first, then the positives are added, so +2 and -5 give -1 (not -3).
  - Rationale: the rulebook (Injuries, p. 46; Injury Roll Table, p. 48; Core Rules, p. 18) places the limit on the combined negative modifiers only, and states no limit on positive ones. Flooring the net total instead would let negatives beyond -3 cancel positive modifiers, which the stated limit does not allow.
  - Decision: interpretation chosen by the project owner on 14 October 2026. Regression test: `InjuryRollTests.PlusTwoAndMinusFiveGiveMinusOne`.
- Results: 1 or less, no effect; 2 to 6, a minor hit and 1 Blood Marker; 7 or 8, the model goes Down and takes 1 Blood Marker, or 2 if it was already Down; 9 or more, the model is out of action.
- Extra injury dice from a Critical Success, or from a melee attack on a Down model, are added by the attack that causes the roll (M4). The rulebook's wording on the critical bonus differs between pp. 44 to 45 and p. 48; p. 48 is the reading to confirm.

Not in M1: Bloodbath Rolls (p. 47) and spending Blood Markers (M4); the effects of being Down (p. 49, M4).

## Scenario chosen for the first match

**I. Claim No Man's Land** (p. 151, from the table of contents). The automated read reported that the rulebook recommends Scenario 1 for a first game; that is not yet confirmed by a person. Confirm that recommendation, and check the scenario's special rules, before M4 relies on it.

## Not in the first match

Listed so nothing is dropped silently. Each moves into the table above when a milestone takes it on.

| Topic | Reference (1.0.2) | Deferred until |
| --- | --- | --- |
| Climbing and jumping (falling, Diving Charge) | Climbing & Jumping, p. 39 | After M8 |
| Keywords | Keywords, p. 23; Keywords Glossary, p. 52 | With content that uses them |
| Battlekit (weapons, grenades, shields, equipment) | Battlekit, p. 68 | With content that uses them |
| Terrain types and battlefield archetypes | Trench Crusade Terrain, p. 58 | After M8 (M3 uses box terrain only) |
| Campaign rules | Campaign Rules, p. 87 | Out of scope until after M8 |
| Other scenarios | Scenarios, pp. 144 to 197 | After M8 |
