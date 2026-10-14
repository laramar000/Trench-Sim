# Rules notes

Where each rule used in the first match lives in the official rules. Each item gets a short summary in our own words, under "Summaries of implemented rules", when a milestone implements it. Rulebook text is never pasted here.

- **Rules version targeted:** 1.0.2
- **Source:** Trench Crusade Digital Rulebook 1.0.2, the free PDF on the publisher's rules page (https://www.trenchcrusade.com/rules/)
- **Re-check due:** when version 1.1 is published (announced for late November 2026)

## How these references were checked

The container that maintains this project cannot download the PDF directly (its network policy blocks the publisher's site). On 14 October 2026 the PDF's extracted text was instead read through an automated web-fetch tool, which reads the file and answers questions about it. Three passes were made:

1. Every running page header (the printed page number above each page's text), with the words just before and after it, so each heading could be placed on its page.
2. Every heading in the first-match table, placed against those headers.
3. Short quotes for each rule summarised below, used to check the summaries (the quotes are kept out of this public repository).

Results:

- **Pages 1 to 52:** every page reference in the table below was found on the page given. "Checked" in the table means this automated check passed.
- **Pages 53 onward:** the extracted text the tool could read ends on page 54, so later pages could not be checked. References to them (terrain, battlekit, campaign and scenario pages) come from the table of contents on page 6 and are marked "Contents only".
- **Not yet done:** no person has compared these pages with the PDF by eye. The tool can misread, so before a milestone relies on an item, confirm it against your own copy of the 1.0.2 PDF and set "Confirmed" to Yes.

The rulebook has a short "Core Rules" chapter (pages 14 to 21) and a longer "Comprehensive Rules" chapter (pages 22 to 51). The comprehensive chapter is the primary reference; core-rules pages are listed as secondary where they cover the same topic.

## First-match subset

| Item | Primary reference (1.0.2) | Secondary | Implemented in | Checked | Confirmed |
| --- | --- | --- | --- | --- | --- |
| Dice | Success Rolls, p. 24; Success Roll Table, p. 24; Risky Success Rolls, p. 24; +Dice And -Dice, p. 25; Injury Dice, p. 25 | Success Rolls and +Dice and -Dice, p. 15 (Core Rules); Dice (D6s & D3s), p. 23; Re-Rolls, p. 31; Rolling Off, p. 31 | M1 | Yes | No |
| Injuries | Injuries, p. 46; Making an Injury Roll and Injury Roll Table, p. 48 | Injury Rolls, p. 18 (Core Rules) | M1 | Yes | No |
| Markers | Blood Markers, pp. 27 to 28; Blessing Markers, p. 28; Bloodbath Rolls, p. 47 | Blood Markers and Bloodbath Rolls, p. 19; Blessing Markers, p. 20 (Core Rules) | M4 | Yes | No |
| Turn sequence | The Sequence Of Play, p. 32; 1. Initiative Phase, p. 32; 2. Activation Phase, p. 33 | Core Rules, pp. 14 and 20 | M2 | Yes | No |
| Actions | Actions, p. 34 (Dash is one of the common actions listed there) | Actions, pp. 14 to 15 (Core Rules; Dash on p. 15) | M4 | Yes | No |
| Movement | Movement, p. 35; Charging, p. 36; Charge Moves and Retreating, p. 37 | Measuring Distances, p. 30; Model Placement, p. 31; Fractions, p. 30 | M3 (geometry), M4 (actions) | Yes | No |
| Line of sight | Line Of Sight, p. 30; Points on the Battlefield or Terrain Pieces, p. 30 | Model Accuracy, p. 30 | M3 | Yes | No |
| Cover | Cover is defined under Terrain, p. 38; ranged cover modifier, p. 43; melee "Defended Obstacle" modifier, p. 45 | Cover and Ranged Attack Modifiers, p. 16; Melee Attack Modifiers, p. 17 (Core Rules) | M3 | Yes | No |
| Ranged | Combat and Ranged Attacks, p. 42; Measuring the Range, p. 43; Ranged Attack Success Roll, p. 44 | Ranged Attacks, Shooting Into Melee, Ranged Attack Modifiers, p. 16 (Core Rules) | M4 | Yes | No |
| Melee | Melee Attacks, p. 45 | Melee Attacks, Melee Attack Modifiers, p. 17 (Core Rules) | M4 | Yes | No |
| Down | Down Results, p. 49; Injury Roll Table, p. 48 | Down Results, p. 20 (Core Rules) | M4 | Yes | No |
| Morale | 3. Morale Phase, Shaken Warbands, Sounding the Retreat, p. 50 | The Morale Phase and Shaken Warbands, p. 21 (Core Rules) | M2 | Yes | No |
| Deployment | Scenario Format, p. 145; The Battlefield and Deployment, p. 146 | | M4 | Contents only | No |
| Scenario | I. Claim No Man's Land, p. 151; first-game recommendation, p. 23 | Game Length and Victory Conditions, p. 148; Winning The Game, p. 51 | M4 | p. 23 and p. 51 yes; pp. 145 to 151 contents only | No |

## Summaries of implemented rules

In our own words. Each names the code that implements it and the open questions a person should check against the PDF.

### Dice (M1)

Code: `src/Rules.Core/Dice/SuccessRoll.cs`, `RollRecord.cs`. Tests: `SuccessRollTests.cs`.

- A Success Roll is two six-sided dice added together.
- Each bonus die (+DICE) and each penalty die (-DICE) adds one extra die to the roll. Bonus and penalty dice cancel one for one first. With bonus dice left over, keep the two highest; with penalty dice left over, keep the two lowest.
- Results: 6 or less fails, 7 to 11 succeeds, 12 or more is a Critical Success.
- A Risky roll is made the same way; if it fails, the model's activation ends (or the ACTION ends, if taken outside the model's activation). The roll only reports this; ending the activation belongs to the turn sequence (M2).
- No limit on bonus or penalty dice was found in the rulebook (checked on pp. 15 and 25), so the code has none. The tests cover net -3 to +3 because the plan asks for that range; it is a test range, not a rules limit.

Not in M1: re-rolls (p. 31), rolling off (p. 31), and making every roll Risky when a warband is Shaken (M2).

### Injuries (M1)

Code: `src/Rules.Core/Dice/InjuryRoll.cs`. Tests: `InjuryRollTests.cs`.

- An Injury Roll is two six-sided dice, with bonus and penalty injury dice handled exactly as for Success Rolls, plus any flat injury modifiers.
- Negative injury modifiers together count for no less than -3. Positive modifiers have no limit. The -3 floor applies to the negatives first, then the positives are added, so +2 and -5 give -1 (not -3).
  - Rationale: all three statements of the limit (Injury Rolls, p. 18; Injuries, p. 46; Common Injury Roll Modifiers, p. 48) word it as a limit on the total of the negative injury modifiers, and none mentions positive modifiers or the net modifier. So the limit is applied to the negatives on their own, before anything else is added.
  - Decision: interpretation chosen by the project owner on 14 October 2026. Regression test: `InjuryRollTests.PlusTwoAndMinusFiveGiveMinusOne`.
- Results: 1 or less, no effect; 2 to 6, a minor hit and 1 Blood Marker; 7 or 8, the model goes Down and takes 1 Blood Marker, or 2 if it was already Down; 9 or more, the model is out of action.
- Extra injury dice from a Critical Success, or from a melee attack on a Down model (pp. 18, 20, 48 and 49), are added by the attack that causes the roll (M4), not by the Injury Roll code. The critical bonus has a wording conflict; see "Open rules questions".

Not in M1: Bloodbath Rolls (p. 47) and spending Blood Markers (M4); the effects of being Down (p. 49, M4).

## Open rules questions

Questions found while checking the rulebook. None is settled; each needs a decision before the milestone named.

### 1. Critical Success bonus: +1 INJURY DICE or +1 DICE? (before M4)

- **The wording.** Eight passages describe the bonus:
  - Five say a Critical Success adds +1 INJURY DICE to the Injury Roll: the Success Table (p. 15), the Core Rules ranged and melee attack steps (pp. 16 and 17), the Core Rules injury modifier list (p. 18), and the Common Injury Roll Modifiers list (p. 48).
  - The Comprehensive Rules Success Rolls section (p. 24) says the same thing in a sentence of its own.
  - The Ranged Attack Success Roll (p. 44) and the Melee Attack Success Roll (p. 45) instead say +1 DICE is added to the Injury Roll.
  - The CRITICAL keyword entry in the glossary could not be read; it is past the end of the text the tool could reach.
- **Interpretation A: +1 INJURY DICE.** The bonus is one extra injury die: roll three dice and keep the two highest, with normal cancelling against any -INJURY DICE. This follows six of the eight passages. It also follows p. 25, which says +/- DICE affect Success Rolls and +/- INJURY DICE affect Injury Rolls, so "+1 DICE added to the Injury Roll" has no defined meaning on its own.
- **Interpretation B: +1 DICE, taken literally.** Taken as written, the bonus would be a Success Roll die, which p. 25 says cannot apply to an Injury Roll. It could also be read as a separate kind of bonus die that does not cancel against -INJURY DICE. Nothing else in the rulebook supports either version.
- **Practical effect on M4.** Under A, the attack code passes one extra bonus die to `InjuryRoll.Roll`, and it cancels against penalty injury dice; the M1 code needs no change. Under B, the M4 attack code would need a separate bonus die that does not cancel, which the M1 roll does not support. The difference shows only when the target has -INJURY DICE (for example from armour or a keyword): under A, a critical can be cancelled out; under the second version of B, it cannot.
- **Status.** A looks much better supported, but it is not settled. It needs a person to read pp. 44, 45 and 48 and the CRITICAL glossary entry, or an official FAQ or errata. M1 code is unchanged.

### 2. Down: -1 DICE on "any rolls" or on "Success Rolls"? (before M4)

The Core Rules (p. 20) say a Down model takes -1 DICE on any rolls it makes. The Comprehensive Rules (p. 49) say Success Rolls. The difference matters only if a Down model makes some other kind of roll.

### 3. Bloodbath Rolls and extra injury dice (deferred to M4)

Bloodbath Rolls (p. 47) say to apply +/- INJURY DICE as for an Injury Roll, but keep the three highest or three lowest dice. The rulebook does not say outright that each extra die is added to the three-die roll; that is only implied. This is not implemented and should not be assumed.

## Scenario chosen for the first match

**I. Claim No Man's Land** (p. 151, from the table of contents). The rulebook recommends this scenario for a first game (Scenario, p. 23; found by the automated check). The scenario's own pages could not be checked; read its special rules before M4 relies on it.

## Not in the first match

Listed so nothing is dropped silently. Each moves into the table above when a milestone takes it on. Pages 58 onward come from the table of contents only.

| Topic | Reference (1.0.2) | Deferred until |
| --- | --- | --- |
| Climbing and jumping (falling, Diving Charge) | Climbing & Jumping, p. 39; Falling, p. 41; Diving Charge, p. 46 (checked) | After M8 |
| Keywords | Keywords, p. 23; Keywords Glossary, p. 52 (checked) | With content that uses them |
| Battlekit (weapons, grenades, shields, equipment) | Battlekit, p. 68 | With content that uses them |
| Terrain types and battlefield archetypes | Trench Crusade Terrain, p. 58 | After M8 (M3 uses box terrain only) |
| Campaign rules | Campaign Rules, p. 87 | Out of scope until after M8 |
| Other scenarios | Scenarios, from p. 144 (where they end was not checked) | After M8 |
