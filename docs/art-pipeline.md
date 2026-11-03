# Character art pipeline

The standard every unit figure follows. The template is the Trench Pilgrim (`client/assets/units/trench-pilgrim.glb`,
built by `tools/art/make_figures.py --figure trench-pilgrim`). Status: **approved by the owner as the template**
(2026-11-03). Nothing below changes a rule; the art is decoration only, and the rules' bases and positions never move.

## 1. Visual direction

Grimdark tabletop miniatures translated into a low-poly game: a world-war trench army fighting a holy war. Dark,
oppressive, gothic. Every figure must read first as a silhouette, then by its equipment, then by its colour.

- **Genre, not copies.** WWI trench kit (steel helmets, greatcoats, webbing, bolt-action rifles, trench clubs, gas
  masks as shapes) worn over or beside medieval religious dress (robes, tabards, hoods, crosses, rosaries, wax seals,
  prayer strips, reliquaries). Each design is original. The Trench Crusade community licence (trenchcrusade.com,
  version 1.00, February 2026) forbids basing fan work on official concept art, art or miniatures, so official images
  are not used as modelling references, and no official design is reproduced.
- **Palette.** Restrained, desaturated: mud browns (`3e3428` robe, `5a5040` coat, `6a5c46` rags), gunmetal and rust
  (`4c5048` helmet, `3a3a3c` metal, `55555a` iron), leather (`4a3020`), bone and parchment (`c8bca0`), old blood
  (`4e1610`), brass (`8a6a2a`). Saturated colour is reserved for the seat colour (below).
- **Faces.** Every humanoid head is a smooth, pale mannequin surface (`cfc4b0`, plaster): no eyes, eyebrows, nose,
  mouth, ears or facial hair, and no texture marks that read as features. Helmets, hoods and masks keep their shapes;
  an opening (eye slit, visor) shows the blank surface or darkness, never a feature. The MakeHuman face is removed
  completely, with every body face inside the head's column (so its baked shading cannot leave marks).
  Headgear that hides the whole head (the Trench Pilgrim's capirote) is built with no head under it (`head=None`),
  so nothing can show through.
- **Build.** Lean and gaunt rather than heavy: the pilgrims use MakeHuman's minimum-muscle, minimum-weight body
  (`shape="minmuscle-minweight"`); robes and capes stay close to the body. Bulk is for armoured and large units.
- **Proportions.** Slightly heroic, not cartoonish: the plain head 12% larger than anatomical, gloved mitten hands,
  weapons and helmets about 10-15% over true scale so they read from the game camera. Realistic anatomy (fingers,
  faces, muscles) is avoided; the body is a posed mannequin under clothing.
- **Seat colour where the camera sees it.** The game camera looks down steeply, so the parts painted with the seat
  colour (the "Accent" node) must include something seen from above: a shoulder mantle, a crest, a plume, a banner,
  not only a tabard on the front.

## 2. Technical requirements (from the Godot client)

- **Units:** game units are inches, Y up. A figure is authored 1.2 tall with its feet at y = 0, centred on the origin,
  facing +Z. The client (`Visuals.FitOnBase`) scales each figure to the plain body's height (1.2") and, if it is wider
  than the base, narrower still, then turns it to face the middle of the battlefield. Down models lie on their backs.
- **Camera:** perspective, steep and high. The fitted view shows the whole table; double-click focus is 8" away
  (`BoardView.CloseDistance`), the closest zoom 4". A figure is judged at 8", where it is about 60 to 120 pixels tall
  in a 1280 x 800 window.
- **Loading:** figures are read at run time with Godot's glTF loader (the `units` folder has `.gdignore`), so the
  export must be plain glTF 2.0: meshes, PNG textures, nearest sampling, no armature, no animation, no custom
  extensions. Parts named `Accent...` are drawn with the seat colour by the client (`Visuals.PaintAccent`).
- **Catalog:** `client/assets/catalog.json` maps `unit:<model name>` to a file. A model with no entry is drawn as the
  plain cylinder. High-contrast mode always draws the plain shapes.
- **Bases (rules data, `baseMm` in content/roster):** 25, 32, 40, 50 and 60 mm round, and 30 x 60 mm oval (oval bases
  are refused in matches for now). A figure's footprint, weapons included, must fit inside its base: a 25 mm base is
  0.984" across, so a 1.2" infantry figure may be at most about 0.95" wide.

## 3. Size classes and budgets

| Class | Base | Figure height | Body | All modules | Total triangles | Textures |
|---|---|---|---|---|---|---|
| A infantry | 25 mm | 1.2" | 1,000 | 600-850 | 1,850 | body 128 px, each module 64 px |
| B elite | 32 mm | 1.3" (bulkier, same head size) | 1,200 | 900 | 2,100 | body 128, modules 64 |
| C heavy | 40 mm | 1.6" | 1,600 | 1,000 | 2,600 | body 256, modules 64 |
| D large | 50 mm | 2.0" | 2,200 | 1,200 | 3,400 | body 256, modules 128 |
| E huge | 60 mm | 2.4" | 2,800 | 1,400 | 4,200 | body 256, modules 128 |
| F oval | 30 x 60 mm | by the creature; length along the oval | 2,000 | 1,000 | 3,000 | body 256, modules 64 |

Exceptions the owner allowed: the Castigator, 3,100 triangles (class B), for its detailed gas mask (2026-11-03).

Figure height (built 2026-11-03): a catalog entry may give its figure's standing height in inches, for example
`"unit:Sanctified Giant": {"file": "units/sanctified-giant.glb", "height": 1.6}` (0.5" to 6"; `VisualCatalog.HeightFor`).
Without one a figure stands 1.2", the plain body's height. The client scales the whole figure to that height and, if it is
wider than its base, narrower still. Tall headgear counts in the height: the Trench Pilgrim's capirote makes its body
shorter than a bare-headed figure's at the same height, which an entry's height can make up for. Oval bases are still
drawn round.

## 4. Materials, textures and lighting

- One baked texture per mesh node: diffuse colour multiplied by baked ambient occlusion (`0.55 + 0.45 x AO`), cut to
  15-bit colour (5 bits per channel), sampled nearest. No normal, roughness or metal maps: roughness 1, metallic 0,
  so the game's lights shade everything alike.
- Source materials are the named palette entries of `make_figures.py` (`M`), each with a little noise for grime.
- Accent parts are untextured; the client gives them the seat colour.
- Lighting is the game's own; nothing is baked except occlusion. Review renders use a flat studio light
  (`tools/art/render_turnaround.py`) and the game itself.

## 5. Modules

Each figure is one glTF file with one mesh node per module, so later work can show, hide or swap kit:

| Node | Holds |
|---|---|
| `Body` | posed body, mannequin head, gloved hands, clothing that is part of the figure |
| `Accent` | everything in the seat colour |
| `Helmet` | headgear (helmet, hood, mask), its seals and strips |
| `Weapon` | the main weapon (a second weapon would be `Weapon2`) |
| `Chest` | webbing, bandoliers, breastplates, seals on the chest |
| `Back` | packs, banners on the back, reliquaries |
| `Hip` | satchels, canteens, sidearms, grenades |

Modules are built in the script with `module("Name")` before the parts that belong to them. Placement comes from the
posed rig (hands, head, shoulders), so the same module code fits any pose. Today the kit a figure shows is fixed per
unit; showing the kit a roster actually chose is a later step (it needs the client to know each model's kit).

## 6. Workflow (Blender)

1. **Design note** per unit: silhouette, headgear, weapon, two or three distinctive details, class (section 3).
2. **Script** the figure in `tools/art/make_figures.py`: pose the MakeHuman base body (CC0) with its own skeleton,
   dress it, build its modules. Scripts keep every figure reproducible and reviewable in git; the `.blend` is a
   by-product for inspection or hand fixes.
3. **Build**: `python tools/art/make_figures.py --figure <name> <units folder> <makehuman folder>` (Python 3.11-3.13
   with `bpy==5.1.2`); set `FIG_BLEND=<folder>` to also save the `.blend`. The script decimates the body to budget
   (head and hands kept), unwraps, bakes, quantises and exports, and prints each node's triangles.
4. **Review renders**: `python tools/art/render_turnaround.py <glb> <png> <base mm>`: front, three-quarter, side and
   back on the rules' base.
5. **In-game check** at 8" and 4" from the game camera, in a hot-seat match.
6. **Catalog** entry, credit in `client/assets/ASSETS.md`, tests (`UnitFiguresTests`: size, budget, Accent, height).

Export settings: glTF binary (`.glb`), Y up, transforms applied, triangulated, no armature or animation, PNG textures
embedded with nearest sampling.

## 7. Review checklist

- Face blank from every angle, at every zoom; no dark marks on the head.
- Silhouette tells the unit apart from the others at 8".
- Seat colour visible from above.
- Footprint inside the base; nothing floating or sunk; nothing pierces the body.
- Budget and texture sizes of its class; nodes named as in section 5.

## Appendix: units and bases

From the faction lists (`content/roster/factions`, the rulebook's base sizes). Visual design notes are written per unit
when its figure is made.

| Faction | Unit | Base (mm) | Class | Keywords |
|---|---|---|---|---|
| The Court | Praetor | 50 | D large | COURT, DEMONIC, ELITE, FEAR, FLYING, LEADER, STRONG, TOUGH |
| The Court | Sorcerer | 50 | D large | COURT, DEMONIC, ELITE, FEAR, FLYING |
| The Court | Shadow Hunter | 32 | B elite | COURT, DEMONIC, ELITE, INFILTRATOR |
| The Court | Hell Knight | 40 | C heavy | COURT, DEMONIC, ELITE, STRONG |
| The Court | Wretched | 25 | A infantry | COURT |
| The Court | Pit Locust | 30 x 60 | F oval | COURT, DEMONIC, FEAR, FLYING |
| The Court | Chain Fiend | 32 | B elite | COURT, DEMONIC |
| The Court | Fallen Saint | 60 | E huge | COURT, DEMONIC, IGNORE, FEAR, STRONG, TOUGH |
| The Cult | Bloated Lord | 50 | D large | CULT, ELITE, FEAR, LEADER, NEGATE, STRONG, TOUGH |
| The Cult | Plague Knight | 32 | B elite | CULT, ELITE, FEAR, NEGATE, STRONG |
| The Cult | Corpse Guard | 32 | B elite | CULT, ELITE, FEAR, NEGATE |
| The Cult | Plague Thrall | 25 | A infantry | CULT, FEAR, NEGATE |
| The Cult | Fly Thrall | 25 | A infantry | CULT, FEAR, FLYING, NEGATE |
| The Cult | Plague Hound | 30 x 60 | F oval | CULT, FEAR, NEGATE |
| The Cult | Fly Herald | 40 | C heavy | CULT, FEAR, FLYING, NEGATE, SKIRMISHER |
| The Cult | Amalgam | 60 | E huge | CULT, FEAR, NEGATE, STRONG, TOUGH |
| The Cult | Executor | 32 | B elite | CULT, ELITE, FEAR, LEADER, NEGATE, STRONG, TOUGH |
| Heretics | Heretic Priest | 32 | B elite | HERETIC, ELITE, LEADER, TOUGH |
| Heretics | Night Commando | 32 | B elite | HERETIC, ELITE, INFILTRATOR |
| Heretics | Chorister | 32 | B elite | HERETIC, ELITE, FEAR |
| Heretics | Heretic Trooper | 25 | A infantry | HERETIC |
| Heretics | Assault Beast | 50 | D large | HERETIC, ARTIFICIAL, FEAR, NEGATE, TOUGH |
| Heretics | Wretched | 25 | A infantry | HERETIC |
| Heretics | Brand-Sworn Heavy Infantry | 32 | B elite | HERETIC, STRONG |
| Heretics | Bombard Witch | 32 | B elite | HERETIC, ARTIFICIAL, NEGATE, NEGATE |
| Heretics | Barbed Wire Banshee | 32 | B elite | HERETIC, ELITE, FEAR |
| Pilgrims | War Prophet | 32 | B elite | PILGRIM, ELITE, LEADER |
| Pilgrims | Sanctified Giant | 40 | C heavy | PILGRIM, ELITE, REGENERATE, STRONG, TOUGH |
| Pilgrims | Castigator | 32 | B elite | PILGRIM, ELITE |
| Pilgrims | Trench Pilgrim | 25 | A infantry | PILGRIM |
| Pilgrims | Stigmatic Nun | 25 | A infantry | PILGRIM, REGENERATE |
| Pilgrims | Ecclesiastic Prisoner | 25 | A infantry | PILGRIM |
| Pilgrims | Reliquary Walker | 60 | E huge | PILGRIM, FEAR, NEGATE, STRONG, TOUGH |
| The Principality | Lieutenant | 32 | B elite | PRINCIPALITY, ELITE, LEADER, TOUGH |
| The Principality | Blind Marksman | 25 | A infantry | PRINCIPALITY, ELITE |
| The Principality | Trench Cleric | 32 | B elite | PRINCIPALITY, ELITE, NEGATE |
| The Principality | Yeoman | 25 | A infantry | PRINCIPALITY |
| The Principality | Shock Trooper | 25 | A infantry | PRINCIPALITY |
| The Principality | Combat Engineer | 25 | A infantry | PRINCIPALITY, NEGATE |
| The Principality | Mechanized Heavy Infantry | 40 | C heavy | PRINCIPALITY, STRONG |
| The Principality | Combat Medic | 25 | A infantry | PRINCIPALITY, NEGATE |
| The Principality | Holy Warrior | 32 | B elite | PRINCIPALITY, ELITE, NEGATE |
| The Sultanate | Yüzbaşı | 32 | B elite | SULTANATE, ELITE, LEADER, NEGATE, TOUGH |
| The Sultanate | Alchemist | 32 | B elite | SULTANATE, ELITE |
| The Sultanate | Assassin | 32 | B elite | SULTANATE, ELITE, INFILTRATOR |
| The Sultanate | Azeb | 25 | A infantry | SULTANATE |
| The Sultanate | Sapper | 25 | A infantry | SULTANATE, NEGATE |
| The Sultanate | Alchemical Lion | 50 | D large | SULTANATE, ARTIFICIAL |
| The Sultanate | Janissary | 32 | B elite | SULTANATE, STRONG |
| The Sultanate | Brazen Bull | 60 | E huge | SULTANATE, ARTIFICIAL, FEAR, STRONG, TOUGH |
| The Sultanate | Master Assassin | 32 | B elite | SULTANATE, ELITE, INFILTRATOR, LEADER, TOUGH |
| The Sultanate | Dervish | 32 | B elite | SULTANATE, IGNORE |
| The Sultanate | Fāris | 32 | B elite | SULTANATE, ELITE, STRONG |
| The Sultanate | Homunculus | 25 | A infantry | SULTANATE, ARTIFICIAL |
| The Sultanate | Silahdar | 32 | B elite | SULTANATE, ELITE, LEADER, NEGATE, STRONG, TOUGH |
