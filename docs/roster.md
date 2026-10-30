# Roster builder data (stages 1 and 2)

The roster builder reads plain JSON content files through `src/Roster`. Rules.Core never sees names, costs or
keywords: a match still gets only the model profiles built from a roster (stage 5). Rules version: Digital Rulebook
1.0.2 only. A file for any other version is refused.

Stage 1 shipped the catalogue format, the keyword vocabulary and the format checks. Stage 2 adds faction lists,
rosters and the roster validator. No battlekit or faction data is in the repository yet: the tests use invented data. Costs, faction lists and armoury limits come from *Warbands of Trench Crusade* in
stage 4, each value checked against its page.

## Files

| File | What it holds |
|---|---|
| `content/roster/keywords.json` | The fixed keyword vocabulary (1.0.2 pp. 52-57): id, kind (`effect` or `tag`), what value it takes, page |
| `content/roster/catalogue.json` (stage 4) | Battlekit entries in the format below |
| `content/roster/factions/*.json` (stage 4) | One faction list per file: models, armoury, variants |
| a player's roster file | One warband: faction, variant, budget, models with their kit and upgrades |

Faction keywords use our faction names (Jason, 2026-10-30): PRINCIPALITY, PILGRIM, SULTANATE, HERETIC, CULT, COURT.

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

## Faction list

| Field | Notes |
|---|---|
| `id`, `name` | Ours. Display names follow the naming policy (The Principality, Pilgrims, The Sultanate, Heretics, The Cult, The Court) |
| `alignment` | `faithful` or `fallen` |
| `budgetDucats` | Usually 700 |
| `specialRules` | `[{ "name", "summary", "page" }]`: our summaries, never the book's text |
| `models` | Warband entries, below |
| `armoury` | Armoury items, below |
| `variants` | Variant warbands, below |
| `source`, `status`, `note`, `naming` | As for catalogue entries |

**Warband entry (model).** `id`, `name`; `min` (default 0) and `max` (absent = no limit); `cost` (null while
unresolved); `movementInches` and `movementType` (e.g. `infantry`); `ranged` and `melee` as +/- DICE from -3 to +3,
or `null` for "-" (the field must be present); `armour` (0 to -3); `baseMm` as `[diameter]` or `[width, length]`;
`keywords`; `kitAccess`: `armoury` (anything in the armoury), `listed` (only the ids in `listed`) or `none`;
`startsWith` (battlekit included in the model's cost); `prices` (`[{ "kit", "cost" }]`: the model's own price for a
piece, e.g. a reduced-cost rifle); `upgrades` (`[{ "id", "name", "cost", "maxPerWarband"?, "keywords"?, "note"? }]`,
each bought at most once per model; `keywords` are added to the model); `abilities` (`[{ "name", "summary", "page" }]`);
`source`, `status`, `note`, `naming`.

**Armoury item.** `kit` (a catalogue id), `cost`, `limit` (whole warband), `limitPerModel`, `only` (`[{ "keyword" }
or { "model" }]`: any one of them qualifies), `stipulations` (`bayonetLug`, `needsBayonetLug`, `shieldCombo`,
`headgear`, `consumable`), `status`, `note`.

**Variant.** `id`, `name`, `removeModels`, `addModels`, `modelCosts` (`[{ "model", "cost" }]`), `removeArmoury`,
`addArmoury` (an added item replaces the faction's item for the same kit), `specialRules`, `source`, `status`,
`note`, `naming`. Everything else is the faction's.

Loading checks every id the file refers to (catalogue kit, models in variants, keywords), duplicate ids and names,
`min` against `max`, base sizes, dice and armour ranges, and that every listed piece has a price in the armoury (or
the variant's armoury) or the model's own prices.

## Roster

```json
{ "format": 1, "rulesVersion": "1.0.2", "name": "Mud Rats", "faction": "testers", "variant": "raiders",
  "budgetDucats": 700, "budgetGlory": 6,
  "models": [ { "entry": "captain", "name": "Old Bill", "kit": [ "pistol", "knife" ], "upgrades": [] } ] }
```

`variant` and `budgetGlory` (default 0) are optional. A piece of kit may be listed twice (two pistols).

## Roster validator

`RosterValidator.Check(roster, faction, catalogue)` returns the problems that make a roster illegal, notes for things
it does not check itself, and the totals in ducats and Glory. It checks:

- Warbands of Trench Crusade pp. 9-13: the warband entry exists in the faction or variant; `min` and `max` per entry;
  never more than 6 ELITE models (upgrades that add ELITE count); ducats and Glory Points each within their budget; a
  model or piece with no resolved cost cannot be taken; kit access (`armoury`, `listed`, `none`); "X only"; Limit for
  the whole warband (pieces bought, not what models start with) and per model; Headgear; Bayonet Lug; upgrades bought
  once per model and within `maxPerWarband`.
- 1.0.2 p. 69: one 2-handed or two 1-handed ranged weapons, the same for melee; one type of grenade; one suit of
  armour; one shield; no two pieces of equipment or special battlekit with the same name. A dual-purpose weapon (a
  pistol) counts only as a ranged weapon. With a shield: at most one 1-handed ranged and one 1-handed melee weapon, and
  a 2-handed weapon only when it and the shield both have Shield Combo. STRONG (p. 57) lets one 2-handed melee weapon
  count as 1-handed, except a CUMBERSOME one (p. 54).
- Notes: unverified entries and kit, campaign-only kit, and each upgrade's `note` (conditions the validator cannot
  check, such as a limit that depends on the warband's total cost).

Not checked yet: Mercenaries (hired with Glory, alignment), and special conditions written in notes.

## Later stages

- Stage 3: the builder screen. Stage 4: the official data. Stage 5: sending a roster to a match; kit the engine can't
  play yet is refused by name at match creation.
