# The Court under the October 2026 Rules Review

The owner chose (2026-11-08) to move the Court of the Seven-Headed Serpent from the 1.0.2 book to the October 2026 Rules Review. The
Review is a rewrite of the Court's special rules, Goetic Powers, models, Battlekit and seven variant warbands. This file is the plan and
the progress; the reasoning for each rule is in `docs/interpretations.md` (U142 onwards). The Review's text is not copied into the repo.

## What the Review changes in the engine

- **SIN MARKERS** (a fourth kind of marker, up to 6 on a model, kept beside BLOOD, INFECTION and BLESSING MARKERS) are the fuel of
  Goetic Spells. A spell's cost is paid by removing SIN MARKERS from friendly DEMONIC models, anywhere, in sight or not. (The Flesh
  Divan of Lust lets non-DEMONIC friends pay too.) This replaces the 1.0.2 payment in BLOOD MARKERS from non-CULT/DEMONIC models.
- SIN MARKERS come from the Court's special rules (Mortal Suffering: instead of placing a BLOOD MARKER on an enemy; Perversion of God's
  Laws: the opponent may remove one instead of a BLOOD MARKER landing on a DEMONIC model), the Praetor's Beacon of Corruption, the
  Chthonic Vessel, and each variant's own rules.
- Spells changed or new: What is Yours is Mine (cost 1-3, any mix of marker kinds), Exquisite Pain (1-3), Call of the Flesh (also a
  Retreat within 1", a Charge within 12" in sight), Light of Samael (replaces Light of the Fallen), Charm of Acedia (this Activation),
  Slavemaster (Yoke Fiend Commands, Sacrifice kills it), Burning Inferno (a table by SIN MARKERS paid), Left-Hand Path (cast during a
  Move or Dash, redeploy touching terrain within 24"), Uncaring Gluttony (an Equipment piece), Raining Blood, Fool's Reward, Greedy
  Hearts, Undo Creation, Entropy's Arrow, Dispassionate Gaze.

## Stages

| Stage | Content | State |
|---|---|---|
| C1 | `ModelState.SinMarkers`; spells paid in SIN MARKERS from friendly DEMONIC models; Exquisite Pain 1-3; Charm of Acedia lasts the Activation | done (U142) |
| C2 | SIN sources: Mortal Suffering and Perversion of God's Laws (decisions), Beacon of Corruption, Chthonic Vessel (Break Vessel ACTION) | open |
| C3 | Spells per the Review text: What is Yours is Mine, Call of the Flesh, Light of Samael, Slavemaster, Burning Inferno, Left-Hand Path, Uncaring Gluttony, Raining Blood, then the variant spells | open |
| C4 | Court roster data (`content/roster/factions/court.json`) to the Review: models, costs, abilities, Armoury and Battlekit | open |
| C5 | The seven variants' special rules and Battlekit, one variant at a time | open |
| C6 | Client: SIN MARKER display (board drops, unit card), spell picker | open |

Until C2 and C4 are done the engine cannot give a Court model SIN MARKERS in a real match; only tests do.
