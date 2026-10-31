# Roster builder data (stages 1 to 4)

The roster builder reads plain JSON content files through `src/Roster`. Rules.Core never sees names, costs or
keywords: a match still gets only the model profiles built from a roster (stage 5). Rules version: Digital Rulebook
1.0.2 only. A file for any other version is refused.

Stage 1 shipped the catalogue format, the keyword vocabulary and the format checks. Stage 2 adds faction lists,
rosters and the roster validator. Stage 3 adds the builder screen. Stage 4 enters the official data: the catalogue
(core battlekit, Glory Items, each faction's own kit) and the faction lists, from the Digital Rulebook and *Warbands of
Trench Crusade*, under our own names, each value checked against its page. Rule texts are our own short summaries.
Factions shipped: The Principality, Pilgrims, The Sultanate, Heretics, The Cult and The Court, each with its book
variants. The Court's Seven Deadly Sins are seven lists ("Dedicated to Wrath" and so on): the standard list has only
the Goetic Powers open to every Court warband.

## Files

| File | What it holds |
|---|---|
| `content/roster/keywords.json` | The fixed keyword vocabulary (1.0.2 pp. 52-57): id, kind (`effect` or `tag`), what value it takes, page |
| `content/roster/catalogue.json` | Battlekit entries in the format below |
| `content/roster/factions/*.json` | One faction list per file: models, armoury, variants |
| `content/roster/practice-catalogue.json`, `practice-faction.json` | Invented practice data for the builder's own tests (`RosterBuilder.Practice()`) |
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
| `cost` | no | `{ "amount": n, "currency": "ducats" or "glory" }`, or `null`. The shipped catalogue leaves it `null`: each faction's armoury prices kit |
| `keywords` | no | `[{ "id", "value"? }]`, ids from the vocabulary. A keyword with no value or a number may appear once |
| `dice`, `injuryDice`, `injuryModifier` | no | Default 0 |
| `rule` | no | `{ "summary", "page" }`: our own short summary and the page, never the book's text |
| `restrictions` | no | `[{ "kind": "eliteOnly" / "limit" (with `value`) / "shieldCombo" / "other" (with `text`) }]` |
| `source` | yes | `{ "book", "version", "page" }` |
| `status` | yes | `verified` (every value checked against its page), `unverified`, or `discrepancy` (needs a `note`) |
| `note` | for discrepancy | What the sources disagree on, for Jason's ruling |
| `naming` | yes | `keep`, `rename` or `review` (naming policy) |
| `campaignOnly` | no | True for Glory Items and other campaign-only kit |

## Checks

Loading never half-accepts a file: any problem refuses the whole file, and every problem is listed in words, for
example `entry "practice-sidearm": "hands" must be 1 or 2`. The checks cover: invalid JSON, wrong format or rules
version, missing or mistyped fields, duplicate ids and names, unknown keywords and wrong keyword values, impossible
numbers (negative costs, zero ranges, modifiers outside -3 to +3), weapon fields on non-weapons, and a discrepancy
with no note.

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
| `kitGroups` | Optional. Groups of armoury items that share one limit, below |
| `variants` | Variant warbands, below |
| `source`, `status`, `note`, `naming` | As for catalogue entries |

**Warband entry (model).** `id`, `name`; `min` (default 0) and `max` (absent = no limit); `cost` (null while
unresolved); `movementInches` and `movementType` (e.g. `infantry`); `ranged` and `melee` as +/- DICE from -4 to +4,
or `null` for "-" (the field must be present); `armour` (0 to -3); `baseMm` as `[diameter]` or `[width, length]`;
`keywords`; `kitAccess`: `armoury` (anything in the armoury), `listed` (only the ids in `listed`) or `none`;
`startsWith` (battlekit included in the model's cost); `prices` (`[{ "kit", "cost" }]`: the model's own price for a
piece, e.g. a reduced-cost rifle); `upgrades` (`[{ "id", "name", "cost", "maxPerWarband"?, "keywords"?, "note"? }]`,
each bought at most once per model; `keywords` are added to the model); `abilities` (`[{ "name", "summary", "page" }]`);
`kitRules` (below); `source`, `status`, `note`, `naming`.

**Kit rules (`kitRules`, optional on a model).** Limits the book puts on one entry's battlekit:
`noCategories` (categories it cannot buy, e.g. `armour` for a model whose armour is built in), `onlyKit`
(`[{ "category", "kit": [ids] }]`: in that category only these pieces, e.g. a model limited to pistols),
`maxCostDucats` (no bought piece costs more), `mustHave` (categories it must carry), `mustHaveWeapon`,
`weaponsNeedKeyword` (e.g. `HEAVY`: its weapons must have it), `arms` (below), `note` (shown as a roster note). Bought
kit is checked against all of them; `mustHave` and `mustHaveWeapon` count everything the model carries, `startsWith`
included.

**Arms (`kitRules.arms`, for a model with more than two arms).** `count` (3 or more: weapons it can carry),
`twoHandedArms` (1 or 2: arms a 2-handed weapon uses), `heavyAsOneHanded` (how many 2-handed HEAVY weapons use one arm),
`maxTwoHanded`, `maxSameWeapon`. A shield uses one arm and never has Shield Combo. These replace the p. 69 hand limits
for ranged, melee and shields: the Amalgam is `{ count 6, twoHandedArms 2, heavyAsOneHanded 2, maxSameWeapon 2 }`
(Warbands p. 140), the Fallen Saint `{ count 3, twoHandedArms 1, maxTwoHanded 1 }` (p. 170).

**Armoury item.** `kit` (a catalogue id), `cost`, `limit` (whole warband), `limitPerModel`, `only` (`[{ "keyword" }
or { "model" }]`: any one of them qualifies), `stipulations` (`bayonetLug`, `needsBayonetLug`, `shieldCombo`,
`headgear`, `consumable`), `from` (a faction id: the piece comes from that faction's armoury), `group` (a kit group id),
`needsKit` (a catalogue id the model must also carry, e.g. a Trench Dog option needs `trench-dog`), `status`, `note`.
An armoury lists a kit once, or once more for each `from`.

**Kit group.** `id`, `name`, `limit` (pieces from the group in the whole warband), `limitPerModel`, `needsUpgrade` (an
upgrade id the model must have bought to take any of them), `note`. Groups model "1 piece of Battlekit from the New
Antioch Armoury" (Weapon Collections, Warbands p. 95; Corrupt Merchants p. 120), Envious Eyes (p. 156), Knight of the
Rotten Cross (p. 128) and the Trench Dog's one Special Training (core p. 142). A variant may add its own groups.

**Variant.** `id`, `name`, `removeModels`, `addModels`, `modelCosts` (`[{ "model", "cost" }]`), `removeArmoury`,
`addArmoury` (an added item replaces the faction's item for the same kit and `from`), `kitGroups`, `budgetDucats` and `budgetGlory` (a
variant's own starting budget, when the book gives one), `specialRules`, `source`, `status`, `note`, `naming`.
Everything else is the faction's. A variant changes a model by removing it and adding a changed copy with the same id.
Book rules the format cannot express are kept as summaries in `specialRules` or a `note`, marked "not checked by the
builder" where a player must follow them.

Loading checks every id the file refers to (catalogue kit, models in variants, keywords), duplicate ids and names,
`min` against `max`, base sizes, dice and armour ranges, and that every listed piece has a price in the armoury (or
the variant's armoury) or the model's own prices.

## Roster

```json
{ "format": 1, "rulesVersion": "1.0.2", "name": "Mud Rats", "faction": "testers", "variant": "raiders",
  "budgetDucats": 700, "budgetGlory": 6,
  "models": [ { "entry": "captain", "name": "Old Bill", "kit": [ "pistol", "knife" ], "upgrades": [] } ] }
```

`variant` and `budgetGlory` (default 0) are optional. A piece of kit may be listed twice (two pistols). A piece bought
from another faction's armoury is written `kit@faction`, e.g. `"bolt-action-rifle@principality"`.

## Roster validator

`RosterValidator.Check(roster, faction, catalogue)` returns the problems that make a roster illegal, notes for things
it does not check itself, and the totals in ducats and Glory. It checks:

- Warbands of Trench Crusade pp. 9-13: the warband entry exists in the faction or variant; `min` and `max` per entry;
  never more than 6 ELITE models (upgrades that add ELITE count); ducats and Glory Points each within their budget; a
  model or piece with no resolved cost cannot be taken; kit access (`armoury`, `listed`, `none`); "X only"; Limit for
  the whole warband (pieces bought, not what models start with) and per model; Headgear; Bayonet Lug; upgrades bought
  once per model and within `maxPerWarband`; kit groups (their limits and needed upgrade) and needed kit.
- 1.0.2 p. 69: one 2-handed or two 1-handed ranged weapons, the same for melee; one type of grenade; one suit of
  armour; one shield; no two pieces of equipment or special battlekit with the same name. A dual-purpose weapon (a
  pistol) counts only as a ranged weapon. With a shield: at most one 1-handed ranged and one 1-handed melee weapon, and
  a 2-handed weapon only when it and the shield both have Shield Combo. STRONG (p. 57) lets one 2-handed melee weapon
  count as 1-handed, except a CUMBERSOME one (p. 54). A model with `arms` is checked against those instead.
- Notes: unverified entries and kit, campaign-only kit, and each upgrade's `note` (conditions the validator cannot
  check, such as a limit that depends on the warband's total cost).

Not checked yet: Mercenaries (hired with Glory, alignment), and special conditions written in notes.

## Later stages

- Stage 3 (done): the builder screen, Build Your Own Roster on the main menu (docs/client.md). It shows the shipped
  factions, the chosen list's special rules and its budget (a variant's own budget when it has one).
- Stage 4: the official data for the six factions. Not entered: Mercenaries (hired with Glory; the format has no
  mercenary hiring yet). Gaps filled after 4b: the Trench Dog (1 Glory) with Loyal Hound and its Special Training
  (+1 Glory each, only the trainings a faction may take); the Amalgam's six arms and the Fallen Saint's three; kit
  from another faction's armoury, priced as in that armoury. Still a gap: rules marked "not checked by the builder".
- Stage 5a (done): `MatchReadiness.Check` turns a legal roster into the engine's warband (content format of
  `content/test-warbands.json`: Movement, round base, one ranged weapon's range, injury modifier and "/Melee", Armour
  from the model plus armour and shield, one Melee Weapon's injury modifier). **Refused by name:** an oval base, a
  second ranged or melee weapon, grenades, equipment and other special battlekit. **Played without, by name:** weapon
  keywords, DICE and INJURY DICE, the model's Ranged and Melee DICE, its keywords, abilities and upgrades; Armour past
  -3 is played as -3. The builder's Check lists both under "For a match". Tests: `MatchReadinessTests`.
- Stage 5b (not started, needs the owner's go): sending the warband to a match. The server builds a match from
  content files on its side (`Sim/ScenarioSetups`, `Match.Server/Lobby.cs`, `CreateMatch(scenario)`), so a roster
  needs either a protocol change (each player's warband sent on Host and Join) or, smallest first, hot-seat only (the
  local server is started with both warband files). `MatchReady.ToContentJson` already writes what the server loads.
