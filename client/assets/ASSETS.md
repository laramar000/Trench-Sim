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
