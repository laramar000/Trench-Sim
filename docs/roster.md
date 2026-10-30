# Roster builder data (stage 1)

The roster builder reads plain JSON content files through `src/Roster`. Rules.Core never sees names, costs or
keywords: a match still gets only the model profiles built from a roster (stage 5). Rules version: Digital Rulebook
1.0.2 only. A file for any other version is refused.

Stage 1 ships the format, the keyword vocabulary and the format checks. No battlekit data is in the repository yet:
the tests use invented entries. Costs, faction lists and armoury limits come from *Warbands of Trench Crusade* in
stage 4, each value checked against its page.

## Files

| File | What it holds |
|---|---|
| `content/roster/keywords.json` | The fixed keyword vocabulary (1.0.2 pp. 52-57): id, kind (`effect` or `tag`), what value it takes, page |
| `content/roster/catalogue.json` (stage 4) | Battlekit entries in the format below |

Every file starts with `"format": 1` and `"rulesVersion": "1.0.2"`.

## Keyword values

| `value` | Meaning | Example in a catalogue entry |
|---|---|---|
| `none` | no value | `{ "id": "CRITICAL" }` |
| `number` | a whole number, 1 or more | `{ "id": "AUTOMATIC", "value": 2 }` |
| `inches` | a whole number of inches, 1 or more | `{ "id": "BLAST", "value": 3 }` |
| `text` | a word or phrase | `{ "id": "NEGATE", "value": "GAS" }`, `{ "id": "IGNORE", "value": "COVER" }` |

`+/- DICE`, `+/- INJURY DICE` and `+/- INJURY MODIFIER` are not keywords here: entries carry them as the numbers
`dice`, `injuryDice` and `injuryModifier` (-3 to +3).

## Catalogue entry

| Field | Required | Notes |
|---|---|---|
| `id` | yes | Ours and stable: lower-case letters and digits joined by hyphens. Unique |
| `name` | yes | Our display name (naming policy). Unique, ignoring case |
| `category` | yes | `ranged`, `melee`, `grenade`, `shield`, `armour`, `equipment` or `special` |
| `hands` | ranged and melee | 1 or 2. Never on grenade, armour or equipment |
| `rangeInches` | ranged and grenade | 1 or more. Only ranged, grenade and special entries have one |
| `alsoMelee` | no | Ranged weapons only: "x"/Melee" in the book |
| `cost` | no | `{ "amount": n, "currency": "ducats" or "glory" }`, or `null` while unresolved. Never guessed |
| `keywords` | no | `[{ "id", "value"? }]`, ids from the vocabulary. A keyword with no value or a number may appear once |
| `dice`, `injuryDice`, `injuryModifier` | no | Default 0 |
| `rule` | no | `{ "summary", "page" }`: our own short summary and the page, never the book's text |
| `restrictions` | no | `[{ "kind": "eliteOnly" / "limit" (with `value`) / "shieldCombo" / "other" (with `text`) }]` |
| `source` | yes | `{ "book", "version", "page" }` |
| `status` | yes | `verified` (every value checked against its page; needs a cost), `unverified`, or `discrepancy` (needs a `note`) |
| `note` | for discrepancy | What the sources disagree on, for Jason's ruling |
| `naming` | yes | `keep`, `rename` or `review` (naming policy) |
| `campaignOnly` | no | True for Glory Items and other campaign-only kit |

## Checks

Loading never half-accepts a file: any problem refuses the whole file, and every problem is listed in words, for
example `entry "practice-sidearm": "hands" must be 1 or 2`. The checks cover: invalid JSON, wrong format or rules
version, missing or mistyped fields, duplicate ids and names, unknown keywords and wrong keyword values, impossible
numbers (negative costs, zero ranges, modifiers outside -3 to +3), weapon fields on non-weapons, a verified entry with
no cost, and a discrepancy with no note.

The checks catch format errors, not wrong numbers. Accuracy is checked against the source pages and Trench Companion
(cross-check only) in stage 4. The mapping from source names to display names is kept outside the repository.

## Later stages

- Stage 2: faction lists (models, prices, limits, default kit, armoury with per-faction costs and limits, variants),
  rosters, and the roster validator (budget, model limits, the p. 69 hand, armour, shield and grenade limits, faction
  availability, restrictions).
- Stage 3: the builder screen. Stage 4: the official data. Stage 5: sending a roster to a match; kit the engine can't
  play yet is refused by name at match creation.
