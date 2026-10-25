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
| 30 | Measure distances base edge to base edge; "within" means the nearest points are no further apart than the distance. A model can see a target if it can see any part of it (base, hands, feet and carried items do not count); sight is all-round; seeing only some of the target is partial line of sight. | Visually checked; used by M3 |
| 31 | Model Placement: a base may not overlap Impassable terrain or another base, and more than half of it must be on the battlefield or in terrain. | Visually checked; used by M3 |
| 35 | Move: the path may be no longer than Movement; a model cannot move within 1" of an enemy unless charging; one that starts within 1" of an enemy must stay within it or retreat; it cannot leave the battlefield; friendly models may be passed once fully cleared. | Visually checked; used by M3 |
| 34 | Actions: once each per activation in any order; Move or Charge or Retreat (one); Dash alongside any; no Shoot with Charge or Fight (no ASSAULT). | Visually checked; used by M4 Slice B |
| 36-37 | Charging (declare, interposing models, Charge Bonus, charge moves) and Retreating (free Fight attacks, move unless Down/Out of Action, end more than 1" away). | Visually checked; used by M4 Slice B |
| 42 | Ranged Attack sequence and the Shooting into Melee box (D6: 1-3 own model, 4-6 enemy; only randomisation in the game). | Visually checked; used by M4 Slice B |
| 45 | Melee Attack sequence and modifiers (Defended Obstacle, Off-Hand, Diving Charge); Critical Success. | Visually checked; used by M4 Slice B |
| 49 | Down Results: ends activation if taken Down in it; +1 INJURY DICE for Melee Attacks on a Down target; Movement halved including Charge Bonus; may measure to any part of a Down model. | Visually checked; used by M4 Slice B |
| 38 | Terrain types and Cover: cover needs a piece at least 1/2" high, at least as wide as the base, between the models and partially blocking sight; "in" terrain means more than half the base is within it. | Visually checked; used by M3 |
| 39-40 | Terrain up to 1" high (and trench walls up to 3") count as Open for walking; climbing and jumping are separate rules. | Visually read; climbing and jumping deferred |
| 46 | Injury Rolls use INJURY DICE and INJURY MODIFIERS. The maximum -INJURY MODIFIER cannot be more than -3 in total. A worked example gives 9 minus 1 = 8, Down. | Visually checked; matches the notes |
| 47 | Bloodbath Roll: spend 6 Blood Markers (3 if the target is Down), roll 3D6, add +/- INJURY DICE and modifiers "in the same way", keep the 3 highest or lowest. DEADLY uses 4D6. | Visually checked; matches the notes; dice interaction still open (question 3) |
| 54 | CRITICAL keyword: +2 INJURY DICE instead of +1 on a Critical Success. DEADLY: 3D6, pick the 3 highest or lowest, +/- INJURY DICE added normally. | Visually checked; treats +1 INJURY DICE as the base critical bonus (see wording discrepancy W1) |
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
- Extra injury dice from a Critical Success, or from a melee attack on a Down model (pp. 18, 20, 48 and 49), are added by the attack that causes the roll (M4), not by the Injury Roll code. A Critical Success adds +1 INJURY DICE (confirmed; see "Rulebook wording discrepancies", W1).

Not in M1: Bloodbath Rolls (p. 47, still not implemented) and spending Blood Markers (done in M4 Slice A); the effects of being Down (p. 49, M4).

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
- R8. After the final Turn a match without a battlefield (the M2 setups) ends with no winner recorded. With a battlefield (M4) the Victory Points decide; see the M4 section.

Not in M2: geometry and positions (deployment uses opaque zone ids), attacks, injuries, Bloodbath, scenario scoring, and the Godot client.

### Geometry (M3)

Pure geometry in `src/Rules.Core/Geometry`, with no positions in the match state in M3 (M4 adds them). Lengths are integers in thousandths of an inch; every decision is integer arithmetic. Confirmed rulebook requirements: measuring and "within" (p. 30), any-part and partial line of sight (p. 30), placement and the more-than-half rule (p. 31), the Move path limits and the 1" enemy rules (p. 35), and the cover conditions (p. 38). Everything the software had to choose, including how the 2D footprint stands in for a model, the Low/Cover/Wall terrain profiles, high ground, and how movement cost is rounded, is listed as provisional in `docs/interpretations.md`. Not in M3: charge and retreat actions, climbing, jumping, falling, the elevation attack bonus, polygon footprints, and applying cover as -1 DICE (M4).

### Ranged combat, Blood Markers and scoring (M4, Slice A)

Code: `src/Rules.Core/Board.cs`, `TurnSequence.Board.cs`, `Reducer.cs`, `Scenarios/ClaimNoMansLand.cs`, `Events/CombatEvents.cs`, `State/`; simulator in `src/Sim`. Tests: `tests/Rules.Tests/Match/`. Slice A is a ranged-only full match; charges, retreats and fighting are Slice B.

Confirmed from the rulebook (1.0.2, in our words):

- Actions: each type of ACTION once per activation; a Dash is a Risky Success Roll taken before moving, may come before or after a Move, and a failed Dash ends the activation (p. 34).
- A Ranged Attack needs a Ranged Weapon, a model not within 1" of an enemy, and a target in Line of Sight within the weapon's range, measured base edge to base edge. At up to half the range it is Short Range; beyond that Long Range, -1 DICE. A target in cover is -1 DICE (pp. 42 to 44).
- The attacker makes a Success Roll. A failure misses; a Success hits and an Injury Roll follows; a **Critical Success adds +1 INJURY DICE to that Injury Roll** (confirmed by the project owner on 14 October 2026; see W1).
- Blood Markers: one per wound, never more than 6 on a model. Whenever a Success Roll is taken for a model with markers, its opponent may spend some or all of them, each as -1 DICE. When an Injury Roll is made against a model with markers, the attacker may spend them, each as +1 INJURY DICE (pp. 27 to 28).
- A model that is taken Down stays on the table; one that is Out of Action leaves it. A Down model stands back up when next activated, with its Movement halved for the rest of that activation (pp. 20, 49).
- Scenario I: four Turns; each Turn's end scores 2 VPs for each Objective a player controls (the Objective is the whole terrain piece); a player controls it with more friendly models on, in or within 1" than enemy models, and models on the piece beat greater numbers merely within 1"; a player wins at once when no enemy models are on the battlefield or the enemy Warband flees; otherwise more VPs wins (pp. 152 to 153).

Provisional choices are listed in `docs/interpretations.md` (U30 to U45); what is missing is in `docs/known-gaps.md`.

### Charge, Retreat, Fight and shooting into melee (M4, Slice B)

Code: `Geometry/Charging.cs`, `Board.cs`, `TurnSequence.Board.cs`, `Reducer.cs`, `Events/CombatEvents.cs`, `Commands/Command.cs`. Tests: `tests/Rules.Tests/Match/{Charge,Fight,Retreat,ShootingIntoMelee,ForgedMeleeEvent,MeleeSimulation}Tests.cs` and `Geometry/ChargeAndRetreatGeometryTests.cs`; the shortest-route follow-up adds `Geometry/{ChargeRouting,ChargeRoutingAudit}Tests.cs`, `Geometry/ReferenceRouter.cs` and `Match/ChargeRoutingPlayTests.cs`. Confirmed rules are C15 to C21 in `docs/interpretations.md`, each with its page, read from the page images (pp. 34, 36, 37, 42, 43, 45, 49; Core pp. 16 and 17 and Comprehensive pp. 27 to 28, 46 to 48 from text and earlier visual checks). Provisional choices are U46 to U65.

Architecture (all through the existing command, event, reducer and staged-roll machinery; no dice before a decision):

- Commands: `ChargeModel`, `RetreatModel`, `FightModel`, `FreeFight`, `DeclineFreeFights`, `ChooseFriendlyTarget`. Decisions added: `FreeFight` (offered to the retreating model's opponent) and `ChooseFriendlyTarget` (the shooter).
- Charge: `ChargeDeclared`, `ChargeRolled` (the D6), `ChargeMoved` (path, cost, reached). Declaration is fully checked first, so a refused charge rolls nothing. The reducer replans the move from the state and the die and requires the same path.
- Retreat: `RetreatDeclared` (path fixed), then `FreeFight` offers and `MeleeAttackDeclared(FreeFight)` attacks, `FreeFightsDeclined`, and `RetreatResolved` (moved, or stayed and `ActivationEnded(TakenDown)`).
- Fight: `MeleeAttackDeclared`, `MeleeSuccessRolled`, then the shared `InjuryRolled` (melee modifier, +1 INJURY DICE for a Critical Success and for a Down target).
- Shooting into melee: `RangedAttackDeclared.IntoMelee`, `ShootingTargetRolled`, `FriendlyTargetChosen`, then the ordinary roll stages.
- State: `ActionsTaken` gains Charge, Retreat, Fight; `RollInProgress` gains attack kind, free-fight, friendly-fire and charge-die fields; `MatchState.Retreat`. `StateHash` is Version 3.

Not rules-confirmed, so flagged: see U46 to U65, in particular the shortest-route charge planner and its conventions (U57 to U65), the contact stop (U47), free-attack sequencing (U50), and the choice of friendly victim and its modifiers (U51, U52). Question for a person to settle: whether Down models may make free attacks, and how a model forced to retreat by an effect (not an ACTION) behaves.

### Replaying a log: what the reducer re-checks (M4 reducer hardening)

A saved match (M6) is replayed by `Reducer.Apply` alone, so the reducer refuses every event that `Decide` could not have produced at that point. Nothing here changes what `Decide` emits: ranged and melee seeds 1 to 400 give the same events as before, field for field. Tests: `tests/Rules.Tests/Match/ForgedLogTests.cs`.

- **Roll-offs.** `RollOffResolved` is no longer a no-op. The reducer re-checks the rounds (every round but the last a tie, the last not), works out the winner from the last round's dice and refuses an event that names a different one. The roll-off must be one the sequence calls for (the zone roll-off before Deployment; the deployment-order roll-off only between equal warbands, after the zones; the Initiative roll-off only on equal counts, once a turn). Its result is kept in `MatchState.RollOff` until the event it decides uses it: the zone decision must go to its winner, `DeploymentOrderSet` and `InitiativeDetermined` must name its winner on equal counts, and nothing else may happen in between. Unequal counts need no roll-off, and the larger warband deploys first and the smaller holds the Initiative.
- **Turn limit.** `TurnStarted` beyond the scenario's turn limit is refused.
- **The end of the match.** The reducer works out when the match is over: a warband has fled, a warband has no model left on the battlefield (during the Activation Phase, with a battlefield), or the final turn has ended. From then on the only event accepted is `MatchEnded` with that reason (and its usual winner checks); `MatchEnded` at any other point is refused. The events `Decide` emits before the formal end (the Injury Roll that takes the last model Out of Action, `WarbandFled`, the final `TurnEnded`) are unaffected.
- **Compulsory follow-ups.** Where the rules leave no choice, only the event they require may come next:
  - a failed Risky roll by the active model (a failed Dash, or a Shaken warband's failed shot or Melee Attack) is followed by `ActivationEnded(RiskyFailure)`, and that reason is refused without such a roll (`MatchState.MustEndActivation`; a failed free attack ends nothing, U49);
  - a Down model whose activation has just started stands up (`ModelStoodUp`, U30);
  - a model taken Down or Out of Action during its own Retreat ends its activation (`ActivationEnded(TakenDown)`);
  - a Shaken warband that passes its Morale Check recovers (`WarbandRecovered`); one that fails flees (`WarbandFled`, `SeatState.MustFlee`); an unshaken warband that fails is offered the choice (`DecisionRequested(FailedMorale)`);
  - before a Success Roll whose model has Blood Markers, and before an Injury Roll whose target has some, the opponent (or attacker) is asked first; no offer is accepted on a friendly-fire Injury Roll (U52).
- **State and hash.** Three fields were needed because the state could not otherwise show them: the unused roll-off result, the forced end of an activation, and the forced flee. Everything else above is worked out from existing state. `StateHash` is Version 4; with those three additions removed, every Version 4 final state hashes to its Version 3 pin.
- **Unknown commands.** A `Command` subtype the rules do not know is refused with `ReasonCode.UnknownCommand`, before anything else is looked at, instead of throwing. It rolls no dice and emits no events.

## Rulebook wording discrepancies

Places where the printed rulebook disagrees with itself. These are not open questions about what to implement: each entry records the ruling in force.

### W1. Critical Success: "+1 INJURY DICE" versus "+1 DICE" (pp. 44 to 45) - RULED

- **Ruling (project owner, 14 October 2026, confirmed):** a Critical Success on a melee or ranged attack grants **+1 INJURY DICE** to the subsequent Injury Roll. It is not an unresolved interpretation.
- **The discrepancy:** the Ranged Attack Success Roll (p. 44) and the Melee Attack Success Roll (p. 45) print "+1 DICE is added to the Injury Roll". Every other passage (the Success Table p. 15, the Core attack steps pp. 16 and 17, the Core injury modifiers p. 18, the Success Rolls section p. 24, the Common Injury Roll Modifiers p. 48, and the CRITICAL keyword on p. 54) says +1 INJURY DICE. Page 25 defines +/- DICE as Success Roll dice and +/- INJURY DICE as Injury Roll dice, so "+1 DICE" on an Injury Roll has no other defined meaning.
- **Implementation:** the attack adds one bonus die to the Injury Roll; it cancels against penalty injury dice exactly like any other INJURY DICE and stacks with Blood Markers spent for +1 INJURY DICE. The Success Roll itself is unchanged (two dice, no extra). Regression tests: `RangedAttackTests.ACriticalSuccessAddsOneInjuryDieAndNoExtraSuccessDie` and `CriticalSuccessAndSpentMarkersStack`; the reducer checks the dice count of every Injury Roll against the Critical Success that caused it.
- The CRITICAL keyword (+2 INJURY DICE instead of +1, p. 54) is not implemented yet because no Slice A content uses keywords.

### W2. Down penalty: "any rolls" (Core p. 20) versus "Success Rolls" (p. 49)

See question 2 below. Adopted: -1 DICE on Success Rolls taken for a Down model.

## Open rules questions

Questions found while checking the rulebook. None is settled; each needs a decision before the milestone named. The injury modifier cap is not in this list because the project owner decided it (see Injuries above).

### 1. (resolved) Critical Success bonus

Resolved by the project owner's ruling of 14 October 2026; see W1 above. It is kept here only so the numbering of the other questions stays stable.

### 2. Down: -1 DICE on "any rolls" or on "Success Rolls"? (adopted provisionally in M4 Slice A)

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
- **Status:** provisional (U31). M4 Slice A applies the penalty as -1 DICE on Success Rolls taken for a Down model, which matches both readings in every case found. In Slice A it never changes a roll, because a Down model stands up at the start of its activation, before it can take any roll. A person should still read pp. 20, 25, 41 and 49.

### 3. Bloodbath Rolls and extra injury dice (not implemented in Slice A)

Bloodbath Rolls (p. 47) say to apply +/- INJURY DICE as for an Injury Roll, but keep the three highest or three lowest dice. The rulebook does not say outright that each extra die is added to the three-die roll; that is only implied. This is not implemented and should not be assumed. Visually checked on p. 47: it says INJURY DICE and modifiers apply "in the same way" with 3 dice kept, and the DEADLY entry on p. 54 says the same for its 3D6. How extra dice combine with the base three still has to be decided.

### 4. Morale Check modifiers and passing (provisional in M2 as R3 and R4)

- **Morale Check:** pp. 21 and 50 call it a Success Roll taken "for a model" or "in the same way as a Success Roll for a model". No model and no modifier is named (p. 50 visually checked; the Core Rules Morale text on p. 21 was read from an unnumbered screenshot).
- **Passing:** neither the Core nor the Comprehensive chapter describes passing an Activation.
- **Provisional interpretation, not official (implemented in M2 as R3 and R4):** a Morale Check takes no modifiers, and a player with unactivated models must activate one. Neither is printed; both need a decision or an official clarification.

### 5. Charge routes: is "shortest" a distance or a Movement cost? (provisional in the M4 Slice B follow-up, U58)

p. 37 says to move by "the shortest route available" and, round obstacles, a route "as short as possible". Difficult terrain doubles the Movement spent (p. 35). The rulebook does not say whether the shortest route minimises distance or Movement cost. Implemented: among the routes the planner builds (through the corners of Impassable terrain and friendly bases), the cheapest in Movement is taken, but no route is bent round a patch of Difficult ground. A scene where the shorter way round crosses Difficult terrain and a slightly longer way is clear shows the difference; the audit test pins the current choice (the clear way). Question for a judge: where one way round crosses Difficult terrain, must the charger take the shorter-distance route, or may it take the cheaper one, or must it bend round Difficult ground?

### 6. Charge routes: a very long detour, and friends that cannot be crossed (provisional, U62 and U65)

p. 37 asks for "the shortest route available that takes it directly towards the target". The planner follows the shortest route however long, so a charger behind a long wall walks the first 12" of a 24" detour and fails, and a charger whose Charge Bonus cannot carry it past a friend in a corridor walks the long way round rather than stopping in front of the friend. Question for a judge: is a charge whose shortest route is far longer than the distance to the target a legal declaration, and when a friend cannot be crossed should the model stop short of it or take the route round it?

## Scenario chosen for the first match

**I. Claim No Man's Land** (pp. 151 to 153; visually checked). The rulebook recommends this scenario for a first game (p. 23). In our own words:

- Fought across a 24-inch-wide strip with a deployment zone on each side and five objective markers, one at the midpoint (p. 151, map).
- The first five terrain pieces cover the objective positions, and a player controls an objective with more models on, in or within 1 inch of it than the enemy (p. 152).
- The roll-off winner picks a deployment zone; players then alternate deploying, starting with the player who has more models. Infiltrators deploy normally (p. 152).
- The game lasts four Turns (p. 152).
- Victory: no enemy models left or an enemy Warband that flees wins at once. Otherwise more Victory Points wins: 2 per controlled objective at each Turn end and 1 per completed Glorious Deed at game end. Equal points draw (pp. 148, 153).
- Seven Glorious Deeds are listed on p. 153. They are not scored in M4 Slice A (see `docs/known-gaps.md`).

## Not in the first match

Listed so nothing is dropped silently. Each moves into the table above when a milestone takes it on. Pages 58 onward are contents only; the others have had an automated text check.

| Topic | Reference (1.0.2) | Deferred until |
| --- | --- | --- |
| Climbing and jumping (falling, Diving Charge) | Climbing & Jumping, p. 39; Falling, p. 41; Diving Charge, p. 46 | After M8 |
| Keywords | Keywords, p. 23; Keywords Glossary, p. 52 | With content that uses them |
| Battlekit (weapons, grenades, shields, equipment) | Battlekit, p. 68 | With content that uses them |
| Terrain types and battlefield archetypes | Trench Crusade Terrain, p. 58 | After M8 (M3 uses box terrain only) |
| Campaign rules | Campaign Rules, p. 87 | Out of scope until after M8 |
| Scenario IV, Trench Warfare | Scenarios, pp. 161-163 | Built in trench-maps stage S6 without Mine Markers or Glorious Deeds (U88) |
| Other scenarios | Scenarios, from p. 144 (end page not verified) | After M8 |
