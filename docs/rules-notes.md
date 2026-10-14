# Rules notes

Where each rule used in the first match lives in the official rules. Each item gets a short summary in our own words, under "Summaries of implemented rules", when a milestone implements it. Rulebook text is never pasted here.

- **Rules version targeted:** 1.0.2
- **Source:** Trench Crusade Digital Rulebook 1.0.2, the free PDF on the publisher's rules page (https://www.trenchcrusade.com/rules/)
- **Re-check due:** when version 1.1 is published (announced for late November 2026)

## How these references were checked

The container that maintains this project cannot download the PDF directly (its network policy blocks the publisher's site). On 14 October 2026 the PDF's extracted text was instead read through an automated web-fetch tool: the tool extracts the PDF's text and a language model answers questions about it. Passes made:

1. Every running page header (the printed page number above each page's text), with the words just before and after it, so each heading could be placed on its page.
2. Every heading in the first-match table, placed against those headers.
3. Short quotes for each rule summarised below, used to check the summaries. The quotes are kept in the project's private notes, not in this public repository.

The extracted text the tool could read ends partway through printed page 54 (the keyword glossary). Nothing after that could be checked except through the contents list on page 6.

### Check status labels

Each reference below carries one of these labels:

- **Automated text check:** the extracted text appeared to support the claim on the cited printed page. This is not proof: text extraction can scramble layout, and the model reading it can misquote or misplace text. The page wording and page assignment may still be wrong.
- **Contents only:** the reference was checked against the contents list on page 6, not against the section itself.
- **Not verified:** the relevant page or text could not be inspected.
- **Human-verified:** a person has read the cited page in the PDF and confirmed the claim. No reference has this label yet.

Before a milestone relies on an item, a person should read its pages in the 1.0.2 PDF and mark it Human-verified.

The rulebook has a short "Core Rules" chapter (pages 14 to 21) and a longer "Comprehensive Rules" chapter (pages 22 to 51). The comprehensive chapter is the primary reference; core-rules pages are listed as secondary where they cover the same topic.

## First-match subset

| Item | Primary reference (1.0.2) | Secondary | Implemented in | Check status |
| --- | --- | --- | --- | --- |
| Dice | Success Rolls, p. 24; Success Roll Table, p. 24; Risky Success Rolls, p. 24; +Dice And -Dice, p. 25; Injury Dice, p. 25 | Success Rolls and +Dice and -Dice, p. 15 (Core Rules); Dice (D6s & D3s), p. 23; Re-Rolls, p. 31; Rolling Off, p. 31 | M1 | Automated text check |
| Injuries | Injuries, p. 46; Making an Injury Roll and Injury Roll Table, p. 48 | Injury Rolls, p. 18 (Core Rules) | M1 | Automated text check |
| Markers | Blood Markers, pp. 27 to 28; Blessing Markers, p. 28; Bloodbath Rolls, p. 47 | Blood Markers and Bloodbath Rolls, p. 19; Blessing Markers, p. 20 (Core Rules) | M4 | Automated text check |
| Turn sequence | The Sequence Of Play, p. 32; 1. Initiative Phase, p. 32; 2. Activation Phase, p. 33 | Core Rules, pp. 14 and 20 | M2 | Automated text check |
| Actions | Actions, p. 34 (Dash is one of the common actions listed there) | Actions, pp. 14 to 15 (Core Rules; Dash on p. 15) | M4 | Automated text check |
| Movement | Movement, p. 35; Charging, p. 36; Charge Moves and Retreating, p. 37 | Measuring Distances, p. 30; Model Placement, p. 31; Fractions, p. 30 | M3 (geometry), M4 (actions) | Automated text check |
| Line of sight | Line Of Sight, p. 30; Points on the Battlefield or Terrain Pieces, p. 30 | Model Accuracy, p. 30 | M3 | Automated text check |
| Cover | Cover is defined under Terrain, p. 38; ranged cover modifier, p. 43; melee "Defended Obstacle" modifier, p. 45 | Cover and Ranged Attack Modifiers, p. 16; Melee Attack Modifiers, p. 17 (Core Rules) | M3 | Automated text check |
| Ranged | Combat and Ranged Attacks, p. 42; Measuring the Range, p. 43; Ranged Attack Success Roll, p. 44 | Ranged Attacks, Shooting Into Melee, Ranged Attack Modifiers, p. 16 (Core Rules) | M4 | Automated text check |
| Melee | Melee Attacks, p. 45 | Melee Attacks, Melee Attack Modifiers, p. 17 (Core Rules) | M4 | Automated text check |
| Down | Down Results, p. 49; Injury Roll Table, p. 48 | Down Results, p. 20 (Core Rules) | M4 | Automated text check |
| Morale | 3. Morale Phase, Shaken Warbands, Sounding the Retreat, p. 50 | The Morale Phase and Shaken Warbands, p. 21 (Core Rules) | M2 | Automated text check |
| Deployment | Scenario Format, p. 145; The Battlefield and Deployment, p. 146 | | M4 | Contents only |
| Scenario | I. Claim No Man's Land, p. 151; first-game recommendation, p. 23 | Game Length and Victory Conditions, p. 148; Winning The Game, p. 51 | M4 | Automated text check for pp. 23 and 51; contents only for pp. 145 to 151; scenario special rules not verified |

## Summaries of implemented rules

In our own words. Each names the code that implements it. Page references and claims here have had an automated text check only (see above); none is human-verified.

### Dice (M1)

Code: `src/Rules.Core/Dice/SuccessRoll.cs`, `RollRecord.cs`. Tests: `SuccessRollTests.cs`.

- A Success Roll is two six-sided dice added together.
- Each bonus die (+DICE) and each penalty die (-DICE) adds one extra die to the roll. Bonus and penalty dice cancel one for one first. With bonus dice left over, keep the two highest; with penalty dice left over, keep the two lowest.
- Results: 6 or less fails, 7 to 11 succeeds, 12 or more is a Critical Success.
- A Risky roll is made the same way; if it fails, the model's activation ends (or the ACTION ends, if taken outside the model's activation). The roll only reports this; ending the activation belongs to the turn sequence (M2).
- The automated text check found no limit on bonus or penalty dice (pp. 15 and 25), so the code has none. The tests cover net -3 to +3 because the plan asks for that range; that is a test range, not a rules limit. A separate test (`NetDiceAreNotCapped`) rolls with net +5 and -5.

Not in M1: re-rolls (p. 31), rolling off (p. 31), and treating every Success Roll as Risky while a warband is Shaken (M2; pp. 21 and 50, automated text check only).

### Injuries (M1)

Code: `src/Rules.Core/Dice/InjuryRoll.cs`. Tests: `InjuryRollTests.cs`.

- An Injury Roll is two six-sided dice, with bonus and penalty injury dice handled exactly as for Success Rolls, plus any flat injury modifiers.
- Negative injury modifiers together count for no less than -3. Positive modifiers have no limit. The -3 floor applies to the negatives first, then the positives are added, so +2 and -5 give -1 (not -3).
  - Rationale: all three statements of the limit (Injury Rolls, p. 18; Injuries, p. 46; Common Injury Roll Modifiers, p. 48) word it as a limit on the total of the negative injury modifiers, and none mentions positive modifiers or the net modifier. So the limit is applied to the negatives on their own, before anything else is added.
  - Decision: interpretation chosen by the project owner on 14 October 2026. Regression test: `InjuryRollTests.PlusTwoAndMinusFiveGiveMinusOne`.
- Results: 1 or less, no effect; 2 to 6, a minor hit and 1 Blood Marker; 7 or 8, the model goes Down and takes 1 Blood Marker, or 2 if it was already Down; 9 or more, the model is out of action. The "already Down" extra marker belongs to the Down result only; a minor hit on a model that is already Down still gives 1 Blood Marker.
- Extra injury dice from a Critical Success, or from a melee attack on a Down model (pp. 18, 20, 48 and 49), are added by the attack that causes the roll (M4), not by the Injury Roll code. The critical bonus has a wording conflict; see "Open rules questions".

Not in M1: Bloodbath Rolls (p. 47) and spending Blood Markers (M4); the effects of being Down (p. 49, M4).

## Open rules questions

Questions found while checking the rulebook. None is settled; each needs a decision before the milestone named. The injury modifier cap is not in this list because the project owner decided it (see Injuries above).

### 1. Critical Success bonus: +1 INJURY DICE or +1 DICE? (before M4)

- **The wording** (automated text check). Eight passages describe the bonus:
  - Five say a Critical Success adds +1 INJURY DICE to the Injury Roll: the Success Table (p. 15), the Core Rules ranged and melee attack steps (pp. 16 and 17), the Core Rules injury modifier list (p. 18), and the Common Injury Roll Modifiers list (p. 48).
  - The Comprehensive Rules Success Rolls section (p. 24) says the same thing in a sentence of its own.
  - The Ranged Attack Success Roll (p. 44) and the Melee Attack Success Roll (p. 45) instead say +1 DICE is added to the Injury Roll.
  - The CRITICAL keyword entry in the glossary could not be read; it is past the end of the text the tool could reach.
- **Interpretation A: +1 INJURY DICE.** The bonus is one extra injury die: roll three dice and keep the two highest, with normal cancelling against any -INJURY DICE. This follows six of the eight passages. It also follows p. 25, which says +/- DICE affect Success Rolls and +/- INJURY DICE affect Injury Rolls, so "+1 DICE added to the Injury Roll" has no defined meaning on its own.
- **Interpretation B: +1 DICE, taken literally.** Taken as written, the bonus would be a Success Roll die, which p. 25 says cannot apply to an Injury Roll. It could also be read as a separate kind of bonus die that does not cancel against -INJURY DICE. Nothing else in the rulebook supports either version.
- **Practical effect on M4.** Under A, the attack code passes one extra bonus die to `InjuryRoll.Roll`, and it cancels against penalty injury dice; the M1 code needs no change. Under B, the M4 attack code would need a separate bonus die that does not cancel, which the M1 roll does not support. The difference shows only when the Injury Roll also has -INJURY DICE: under A, a critical can be cancelled out; under the second version of B, it cannot.
- **Status.** Open. A is the better-supported provisional reading, but it is not settled. It needs a person to read pp. 44, 45 and 48 and the CRITICAL glossary entry, or an official FAQ or errata. M1 code is unchanged.

### 2. Down: -1 DICE on "any rolls" or on "Success Rolls"? (decide before M4)

- **The wording** (automated text check):
  - Down Results in the Core Rules (p. 20) apply -1 DICE to any rolls you take for a Down model. The Quickstart rules (p. 4) use the same wording.
  - Down Results in the Comprehensive Rules (p. 49) apply it to any Success Rolls you take for a Down model.
  - The other Down effects match between the two lists. The Comprehensive list adds one extra point about falling from a ledge.
- **Related rules found:**
  - +/- DICE are defined as extra dice on a Success Roll (p. 25). +/- INJURY DICE are the separate version for Injury Rolls (p. 25; Core Rules p. 18).
  - The rulebook's convention (p. 22) is that you "take" rolls for your own models and "make" rolls against your opponent's models.
- **Which rolls each reading could affect:**
  - *Success Rolls taken for your own Down model,* including Risky ones and the Morale check if it were taken for a model: affected under both readings.
  - *Injury Rolls and Bloodbath Rolls against a Down model:* these are made against it, not taken for it, so neither reading applies the penalty. A melee attack on a Down target gets +1 INJURY DICE under both texts.
  - *Injury Rolls taken for your own Down model:* the only likely case is a falling Injury Roll after a Down model falls from a ledge (pp. 41 and 49). Only the "any rolls" reading could reach it, and only if -1 DICE were also read as a penalty die on an Injury Roll, which p. 25's definitions don't support. If it did apply, it would lower the injury, helping the fallen model.
  - *Initiative, roll-offs, D3 rolls and the charge D6:* player rolls, or moves a Down model can't make. Neither reading applies, because -1 DICE only works on a keep-two-of-several roll.
- **Practical consequence:** under the rulebook's own definitions, both readings appear to give the same results in every case found. The one exception is the falling-Injury-Roll edge case, which would need a strained reading of "-1 DICE".
- **Authoritative clarification:** the publisher's rules page (checked 14 October 2026) links no FAQ, errata or clarification for 1.0.2.
- **Confidence:** moderate that the readings are equivalent in play, based on automated text checks only.
- **Status:** open. Before M4 implements attack and injury resolution, decide whether the Down penalty is applied as a -1 DICE on Success Rolls taken for the Down model (which matches both readings in every case found), and confirm by having a person read pp. 20, 25, 41 and 49.

### 3. Bloodbath Rolls and extra injury dice (deferred to M4)

Bloodbath Rolls (p. 47) say to apply +/- INJURY DICE as for an Injury Roll, but keep the three highest or three lowest dice. The rulebook does not say outright that each extra die is added to the three-die roll; that is only implied. This is not implemented and should not be assumed.

## Scenario chosen for the first match

**I. Claim No Man's Land** (p. 151, from the table of contents). The rulebook recommends this scenario for a first game (Scenario, p. 23; automated text check). The scenario's own pages could not be checked; read its special rules before M4 relies on it.

## Not in the first match

Listed so nothing is dropped silently. Each moves into the table above when a milestone takes it on. Pages 58 onward are contents only; the others have had an automated text check.

| Topic | Reference (1.0.2) | Deferred until |
| --- | --- | --- |
| Climbing and jumping (falling, Diving Charge) | Climbing & Jumping, p. 39; Falling, p. 41; Diving Charge, p. 46 | After M8 |
| Keywords | Keywords, p. 23; Keywords Glossary, p. 52 | With content that uses them |
| Battlekit (weapons, grenades, shields, equipment) | Battlekit, p. 68 | With content that uses them |
| Terrain types and battlefield archetypes | Trench Crusade Terrain, p. 58 | After M8 (M3 uses box terrain only) |
| Campaign rules | Campaign Rules, p. 87 | Out of scope until after M8 |
| Other scenarios | Scenarios, from p. 144 (end page not verified) | After M8 |
