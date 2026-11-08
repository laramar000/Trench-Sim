# The Court under the October 2026 Rules Review

The owner chose (2026-11-08) to move the Court from the 1.0.2 book to the October 2026 Rules Review. The
Review is a rewrite of the Court's special rules, Goetic Powers, models, Battlekit and seven variant warbands. This file is the plan and
the progress; the reasoning for each rule is in `docs/interpretations.md` (U142 onwards). The Review's text is not copied into the repo.

## What the Review changes in the engine

- **SIN MARKERS** (a fourth kind of marker, up to 6 on a model, kept beside BLOOD, INFECTION and BLESSING MARKERS) are the fuel of
  Goetic Spells. A spell's cost is paid by removing SIN MARKERS from friendly DEMONIC models, anywhere, in sight or not. (The Flesh
  Divan of Lust lets non-DEMONIC friends pay too.) This replaces the 1.0.2 payment in BLOOD MARKERS from non-CULT/DEMONIC models.
- SIN MARKERS come from the Court's special rules (Mortal Suffering: instead of placing a BLOOD MARKER on an enemy; Perversion of God's
  Laws: the opponent may remove one instead of a BLOOD MARKER landing on a DEMONIC model), the Praetor's Beacon of Corruption, the
  the Sin Vessel, and each variant's own rules.
- Spells changed or new: What is Yours is Mine (cost 1-3, any mix of marker kinds), Exquisite Pain (1-3), Call of the Flesh (also a
  Retreat within 1", a Charge within 12" in sight), Light of the Fallen (new wording), Charm of Acedia (this Activation),
  Slavemaster (Yoke Fiend Commands, Sacrifice kills it), Burning Inferno (a table by SIN MARKERS paid), Left-Hand Path (cast during a
  Move or Dash, redeploy touching terrain within 24"), Uncaring Gluttony (an Equipment piece), Raining Blood, Fool's Reward, Greedy
  Hearts, Undo Creation, Entropy's Arrow, Dispassionate Gaze.

## Stages

| Stage | Content | State |
|---|---|---|
| C1 | `ModelState.SinMarkers`; spells paid in SIN MARKERS from friendly DEMONIC models; Exquisite Pain 1-3; Charm of Acedia lasts the Activation | done (U142) |
| C2a | Beacon of Corruption, Sin Vessel (Break Vessel ACTION) (U144) | done |
| C2b | Mortal Suffering and Perversion of God's Laws on Injury Rolls (the attacker's player chooses) (U145) | done |
| C3a | Light of the Fallen (U146) | done |
| C3b | Raining Blood (U147) | done |
| C3c | Uncaring Gluttony (U148) | done |
| C3d | What is Yours is Mine (1-3, any marker mix), Call of the Flesh details (U149) | done |
| C3e | Burning Inferno (U151) | done |
| C3f | Left-Hand Path (U152) | done |
| C3g | Slavemaster (U153) | done |
| C3 | The variant warbands' own spells (with C5) | open |
| C4a | Base Court list in `court.json` and `catalogue.json` to the Review: models, costs, abilities, Armoury, new kit (U143) | done |
| C4b | The rest of the Review's base text: Hunter, Pit Locust and Wretched details, mercenary Goetic Warlock / Sin Eater lines, every model's engine effects | open |
| C5 | The seven variants' special rules and Battlekit, one variant at a time | open |
| C6 | Client: SIN MARKER display (board drops, unit card), spell picker | open |

Until C2 and C4 are done the engine cannot give a Court model SIN MARKERS in a real match; only tests do.
