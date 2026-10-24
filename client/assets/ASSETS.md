# Art files

Every art file under `client/assets/` is listed here. `AssetManifestTests` fail the build's tests if a file is missing from this list, a listed file is missing, a detail is empty, or a licence is not accepted.

Rules:
- **Accepted licences:**
  - `CC0-1.0`;
  - `CC-BY-4.0`, with credit given here and in the game's credits;
  - `OFL-1.1`, for fonts;
  - `Original`, for work made for this project.
- **Not accepted:**
  - NC, ND or "personal use" licences;
  - files with no clear licence;
  - files taken from games;
  - official Trench Crusade art, miniature sculpts, logos or designs, or close copies of them;
  - AI-generated files whose origin is unclear.
- **Size:** at most 8 MB for one file and 40 MB in all, and no Git LFS. The Test Center downloads the branch as a ZIP, and GitHub's ZIPs leave LFS files out.
- **Formats:** glTF 2.0 (`.glb`) for models and PNG for textures, imported by Godot 4.7.2. They must look right in both Forward+ (Windows) and Compatibility (the Linux UI check).
- **Rules are not affected:** art is decoration only. The rules' bases, footprints, heights and positions decide everything. `catalog.json` says which file draws what, and anything without a working file is drawn with the plain shapes.

## Assets

| File | Source | Author | Licence | Date | Changes |
|---|---|---|---|---|---|
| `units/rifleman.glb` | https://github.com/makehumancommunity/makehuman (base body, commit a8bc2d5; built by tools/art/make_figures.py) | MakeHuman team (body); Trench Sim project (Claude) (pose, equipment, texture) | CC0-1.0 | 2026-10-24 | Posed with MakeHuman's skeleton, reduced to about 1,400 triangles, dressed with original equipment, one 128x128 PS1-style baked texture. Rifleman: long rifle at port arms, Brodie-style helmet, pack. Sharpshooter, Marksman. |
| `units/skirmisher.glb` | https://github.com/makehumancommunity/makehuman (base body, commit a8bc2d5; built by tools/art/make_figures.py) | MakeHuman team (body); Trench Sim project (Claude) (pose, equipment, texture) | CC0-1.0 | 2026-10-24 | Posed with MakeHuman's skeleton, reduced to about 1,400 triangles, dressed with original equipment, one 128x128 PS1-style baked texture. Skirmisher: hooded, leaning forward, short bayonet carbine, satchel. Skirmisher, Runner. |
| `units/shield.glb` | https://github.com/makehumancommunity/makehuman (base body, commit a8bc2d5; built by tools/art/make_figures.py) | MakeHuman team (body); Trench Sim project (Claude) (pose, equipment, texture) | CC0-1.0 | 2026-10-24 | Posed with MakeHuman's skeleton, reduced to about 1,400 triangles, dressed with original equipment, one 128x128 PS1-style baked texture. Shield: great helm, pauldrons, iron pavise, raised mace. Bulwark, Shieldbearer. |
| `units/standard.glb` | https://github.com/makehumancommunity/makehuman (base body, commit a8bc2d5; built by tools/art/make_figures.py) | MakeHuman team (body); Trench Sim project (Claude) (pose, equipment, texture) | CC0-1.0 | 2026-10-24 | Posed with MakeHuman's skeleton, reduced to about 1,400 triangles, dressed with original equipment, one 128x128 PS1-style baked texture. Standard: tall pole, pennant, finial and lantern. Banner Carrier, Lamp Keeper. |
| `ground/mud.png` | tools/art/make_textures.py (made for this project) | Trench Sim project (Claude) | Original | 2026-10-24 | None: generated as is. Churned mud with ruts and wet patches, 128x128, 15-bit colour. |
| `ground/crater.png` | tools/art/make_textures.py (made for this project) | Trench Sim project (Claude) | Original | 2026-10-24 | None: generated as is. Shell crater decal with a wet bowl and raised rim, 64x64, cut-out edge. |
| `ground/duckboard.png` | tools/art/make_textures.py (made for this project) | Trench Sim project (Claude) | Original | 2026-10-24 | None: generated as is. Broken duckboard decal, 128x32, cut-out gaps. |
| `terrain/rubble.png` | tools/art/make_textures.py (made for this project) | Trench Sim project (Claude) | Original | 2026-10-24 | None: generated as is. Rubble of brick and stone for Low terrain, 128x128. |
| `terrain/sandbags.png` | tools/art/make_textures.py (made for this project) | Trench Sim project (Claude) | Original | 2026-10-24 | None: generated as is. Stacked sandbags for Cover terrain, 128x128. |
| `terrain/stone.png` | tools/art/make_textures.py (made for this project) | Trench Sim project (Claude) | Original | 2026-10-24 | None: generated as is. Sooted masonry blocks for Wall terrain, 128x128. |
