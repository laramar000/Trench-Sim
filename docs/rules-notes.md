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

The extracted text the tool could read ended partway through printed page 54 (the keyword glossary). Later, the project owner supplied the full extracted text and page images; see "Visual verification" below.

### Check status labels

Each reference below carries one of these labels:

- **Automated text check:** the extracted text appeared to support the claim on the cited printed page. This is not proof: text extraction can scramble layout, and the model reading it can misquote or misplace text. The page wording and page assignment may still be wrong.
- **Contents only:** the reference was checked against the contents list on page 6, not against the section itself.
- **Not verified:** the relevant page or text could not be inspected.
- **Visually checked:** the page image (a rendered page showing its printed page number) was read directly and compared with the claim, and the printed page number was visible on the image. Done by the assistant that maintains this project, using page images the project owner supplied on 14 October 2026. One reader, not independent, and not a person's sign-off. Pages are listed under "Visual verification" below.
- **Human-verified:** a person has read the cited page in the PDF and confirmed the claim. No reference has this label yet.

Before a milestone relies on an item, a person should read its pages in the 1.0.2 PDF and mark it Human-verified.

## Visual verification

Page images of the 1.0.2 PDF were supplied on 14 October 2026 and read directly. They are kept outside this repository. Below are the pages read this way, with short paraphrases and the printed page number each was read from. Where the printed text disagrees with itself, the point is listed under "Open rules questions" and stays provisional.

| Page | What the page says (paraphrase) | Result |
| --- | --- | --- |
| 15 | Success Roll: 2D6, one extra die per +DICE or -DICE, keep the 2 highest or 2 lowest, +/- cancel in pairs. Bands 2-6 / 7-11 / 12+. A Critical Success adds +1 INJURY DICE to Ranged or Melee Attacks. | Visually checked; matches the notes |
| 24 | Same bands and procedure. A critical hit makes the Injury Roll with +1 INJURY DICE. A failed Risky roll ends the Activation, or the ACTION when taken outside one. | Visually checked; matches the notes |
| 25 | +DICE and -DICE add dice and cancel in pairs, with no cap stated. INJURY DICE work the same way on Injury Rolls. A +/- DICE Keyword applies only to Success Rolls, and a +/- INJURY DICE Keyword only to Injury Rolls. | Visually checked; matches the notes |
| 46 | Injury Rolls use INJURY DICE and INJURY MODIFIERS. The maximum -INJURY MODIFIER cannot be more than -3 in total. A worked example gives 9 minus 1 = 8, Down. | Visually checked; matches the notes |
| 47 | Bloodbath Roll: spend 6 Blood Markers (3 if the target is Down), roll 3D6, add +/- INJURY DICE and modifiers "in the same way", keep the 3 highest or lowest. DEADLY uses 4D6. | Visually checked; matches the notes; dice interaction still open (question 3) |
| 54 | CRITICAL keyword: +2 INJURY DICE instead of +1 on a Critical Success. DEADLY: 3D6, pick the 3 highest or lowest, +/- INJURY DICE added normally. | Visually checked; new information, see question 1 |
| 152 | Claim No Man's Land: five objectives, control rule by models on, in or within 1" of the objective, deployment by roll-off then alternating, Infiltrators deploy normally, lasts four Turns. | Visually checked; resolves the game length |
| 153 | Immediate win if no enemy models remain or the enemy Warband flees; otherwise more Victory Points wins. 2 VPs per controlled Objective each Turn end, 1 VP per Glorious Deed at game end. Seven Glorious Deeds are listed. | Visually checked; resolves the victory conditions |

Pages also read from page images in the same exercise, with matches to these notes unless noted: pp. 18, 20, 23, 44, 48, 49, 50, 51 and 145 to 151, and p. 45 and p. 54 from screenshots showing their printed numbers. Pp. 14, 31, 32 and 33 were read from screenshots whose printed page number was visible for pp. 14, 31 and 32 only. The printed examples on pp. 46 and 48 both give Down; the p. 48 one is covered by a regression test (`InjuryRollTests.PrintedExampleWithTwoBonusInjuryDiceIsDown`). Other pages in the first-match table stay at their earlier labels.

The rulebook has a short "Core Rules" chapter (pages 14 to 21) and a longer "Comprehensive Rules" chapter (pages 22 to 51). The comprehensive chapter is the primary reference; core-rules pages are listed as secondary where they cover the same topic.

## First-match subset

| Item | Primary reference (1.0.2) | Secondary | Implemented in | Check status |
| --- | --- | --- | --- | --- |
| Dice | Success Rolls, p. 24; Success Roll Table, p. 24; Risky Success Rolls, p. 24; +Dice And -Dice, p. 25; Injury Dice, p. 25 | Success Rolls and +Dice and -Dice, p. 15 (Core Rules); Dice (D6s & D3s), p. 23; Re-Rolls, p. 31; Rolling Off, p. 31 | M1 | Visually checked for pp. 15, 24, 25; automated text check for the rest |
| Injuries | Injuries, p. 46; Making an Injury Roll and Injury Roll Table, p. 48 | Injury Rolls, p. 18 (Core Rules) | M1 | Visually checked for pp. 18, 46, 48; automated text check for the rest |
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
| Deployment | Scenario Format, p. 145; The Battlefield and Deployment, p. 146 | | M4 | Visually checked for pp. 145 to 147 |
| Scenario | I. Claim No Man's Land, p. 151; first-game recommendation, p. 23 | Game Length and Victory Conditions, p. 148; Winning The Game, p. 51 | M4 | Visually checked for pp. 23, 51, 145 to 153; Scenario 1's game length, victory conditions and glorious deeds are on pp. 152 and 153 |

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

### Turn sequence (M2)

Code: `src/Rules.Core/State/`, `Events/`, `Commands/`, `TurnSequence.cs`, `Reducer.cs`, `StateHash.cs`, `TurnRules.cs`. Tests: `TurnSequenceTests.cs`, `ReplayAndHashTests.cs`, `RandomPlayTests.cs`.

How it works: `TurnSequence.Decide(state, command, dice)` checks a command, rolls any dice the rules call for (roll-offs, Morale Checks), and returns the events it produced plus the new state. A refused command returns a reason and no events, rolls no dice and leaves the state alone. Every state change is an event applied by the pure `Reducer`, so replaying a log rebuilds the state without any dice. `StateHash` writes the state as canonical text (fixed field order, models sorted by id) and hashes it with SHA-256; two golden tests pin the format.

Confirmed from the rulebook (1.0.2, in our words):

- A Turn has three Phases in order: Initiative, Activation, Morale (p. 32).
- The Initiative goes to the player with fewer models; equal counts are settled by a roll-off, where each player rolls a D6, the higher wins and a tie is rolled again (pp. 31, 32).
- The Initiative holder decides who activates first. Players then alternate activating one model each, each model once per Turn, and the player with more models finishes theirs (p. 33).
- An activation ends when the player stops, or when a Risky roll fails (pp. 20, 24 in the Core Rules; the failure is reported by the dice code, M1).
- In the Morale Phase a warband with at least half its models Down or out of action (rounded up) takes a Morale Check, a Success Roll. A failed check either makes the warband Shaken or sounds the retreat (p. 50). A Shaken warband makes its rolls Risky, checks again next Turn, and flees if it fails (pp. 21, 50). A fleeing warband loses (p. 51).
- Scenario I: four Turns, the deployment-zone roll-off winner picks a zone, and players alternate deploying starting with the side that has more models (p. 152).

Provisional rulings (our own; each marked in code and tests, to be confirmed or replaced):

- R1. Initiative counts models that are not Down or out of action. The Core Rules (p. 14) word this differently; a `RulesProfile` setting selects the count and is part of the state hash.
- R2. A roll-off tie is simply rolled again.
- R3. A Morale Check is a plain Success Roll with no modifiers.
- R4. There is no Pass command: a player with an unactivated model must activate one, and may end that activation without taking any action.
- R5. When both warbands must check, the Initiative holder chooses who checks first (Option B, chosen by the project owner on 14 October 2026). Reasoning: the rulebook does not say, and the simultaneous-activities box on p. 32 gives that player the call when order matters, so we reasoned by analogy. This is a guess, not a printed rule; a `RulesProfile` setting offers "Initiative holder first" without asking. If the first-checking warband flees, the match ends before the other checks.
- R6. Warband size for the half test is every model in the force, including those out of action.
- R7. When both sides have the same number of models, a separate deployment-order roll-off (not the zone-choice roll-off) decides who deploys first, and its winner starts. The rulebook gives no tie-break for equal counts, so this is provisional.
- R8. After the final Turn the match ends with no winner recorded; victory points belong to M4.

Not in M2: geometry and positions (deployment uses opaque zone ids), attacks, injuries, Bloodbath, scenario scoring, and the Godot client.

## Open rules questions

Questions found while checking the rulebook. None is settled; each needs a decision before the milestone named. The injury modifier cap is not in this list because the project owner decided it (see Injuries above).

### 1. Critical Success bonus: +1 INJURY DICE or +1 DICE? (before M4)

- **The wording** (automated text check). Eight passages describe the bonus:
  - Five say a Critical Success adds +1 INJURY DICE to the Injury Roll: the Success Table (p. 15), the Core Rules ranged and melee attack steps (pp. 16 and 17), the Core Rules injury modifier list (p. 18), and the Common Injury Roll Modifiers list (p. 48).
  - The Comprehensive Rules Success Rolls section (p. 24) says the same thing in a sentence of its own.
  - The Ranged Attack Success Roll (p. 44) and the Melee Attack Success Roll (p. 45) instead say +1 DICE is added to the Injury Roll.
  - Visual check: pp. 15, 18, 24 and 48 print "+1 INJURY DICE" and pp. 44 and 45 print "+1 DICE", as above. The CRITICAL keyword entry (p. 54) gives "+2 INJURY DICE instead of +1 INJURY DICE", which treats +1 INJURY DICE as the base bonus and supports interpretation A.
- **Interpretation A: +1 INJURY DICE.** The bonus is one extra injury die: roll three dice and keep the two highest, with normal cancelling against any -INJURY DICE. This follows six of the eight passages. It also follows p. 25, which says +/- DICE affect Success Rolls and +/- INJURY DICE affect Injury Rolls, so "+1 DICE added to the Injury Roll" has no defined meaning on its own.
- **Interpretation B: +1 DICE, taken literally.** Taken as written, the bonus would be a Success Roll die, which p. 25 says cannot apply to an Injury Roll. It could also be read as a separate kind of bonus die that does not cancel against -INJURY DICE. Nothing else in the rulebook supports either version.
- **Practical effect on M4.** Under A, the attack code passes one extra bonus die to `InjuryRoll.Roll`, and it cancels against penalty injury dice; the M1 code needs no change. Under B, the M4 attack code would need a separate bonus die that does not cancel, which the M1 roll does not support. The difference shows only when the Injury Roll also has -INJURY DICE: under A, a critical can be cancelled out; under the second version of B, it cannot.
- **Status.** Open. A is the better-supported provisional reading and the one the code assumes, but the printed wording conflict is real and nothing official settles it. It needs an official FAQ or errata. M1 code is unchanged.

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
- **Visual check:** Core p. 20 applies the -1 DICE penalty to any rolls taken for a Down model; Comprehensive p. 49 applies it to Success Rolls only. The two chapters differ in print, so the conflict is real.
- **Authoritative clarification:** the publisher's rules page (checked 14 October 2026) links no FAQ, errata or clarification for 1.0.2.
- **Confidence:** moderate that the readings are equivalent in play, based on automated text checks only.
- **Status:** open. Before M4 implements attack and injury resolution, decide whether the Down penalty is applied as a -1 DICE on Success Rolls taken for the Down model (which matches both readings in every case found), and confirm by having a person read pp. 20, 25, 41 and 49.

### 3. Bloodbath Rolls and extra injury dice (deferred to M4)

Bloodbath Rolls (p. 47) say to apply +/- INJURY DICE as for an Injury Roll, but keep the three highest or three lowest dice. The rulebook does not say outright that each extra die is added to the three-die roll; that is only implied. This is not implemented and should not be assumed. Visually checked on p. 47: it says INJURY DICE and modifiers apply "in the same way" with 3 dice kept, and the DEADLY entry on p. 54 says the same for its 3D6. How extra dice combine with the base three still has to be decided.

### 4. Morale Check modifiers and passing (provisional in M2 as R3 and R4)

- **Morale Check:** pp. 21 and 50 call it a Success Roll taken "for a model" or "in the same way as a Success Roll for a model". No model and no modifier is named (p. 50 visually checked; the Core Rules Morale text on p. 21 was read from an unnumbered screenshot).
- **Passing:** neither the Core nor the Comprehensive chapter describes passing an Activation.
- **Provisional interpretation, not official (implemented in M2 as R3 and R4):** a Morale Check takes no modifiers, and a player with unactivated models must activate one. Neither is printed; both need a decision or an official clarification.

## Scenario chosen for the first match

**I. Claim No Man's Land** (pp. 151 to 153; visually checked). The rulebook recommends this scenario for a first game (p. 23). In our own words:

- Fought across a 24-inch-wide strip with a deployment zone on each side and five objective markers, one at the midpoint (p. 151, map).
- The first five terrain pieces cover the objective positions, and a player controls an objective with more models on, in or within 1 inch of it than the enemy (p. 152).
- The roll-off winner picks a deployment zone; players then alternate deploying, starting with the player who has more models. Infiltrators deploy normally (p. 152).
- The game lasts four Turns (p. 152).
- Victory: no enemy models left or an enemy Warband that flees wins at once. Otherwise more Victory Points wins: 2 per controlled objective at each Turn end and 1 per completed Glorious Deed at game end. Equal points draw (pp. 148, 153).
- Seven Glorious Deeds are listed on p. 153. Their exact conditions will be read when M4 implements scoring.

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
