# Shape Pack v1

Shape Pack v1 is AshLaw's small, strict boundary between community art and the
object world. A pack is a directory containing `shape-pack.json`, one or more
PNG atlases, packed 1-bit alpha masks, and its attribution/licence files.

The starter pack lives at `game/assets/shape-packs/flare-starter/`. Its knight,
goblin, and shortsword are adapted from Flare at the exact revision recorded in
the manifest and `ATTRIBUTION.md`; its chest is original to AshLaw. The importer
reduces colour saturation to 65% while retaining source alpha.

## Manifest contract

The loader rejects unknown fields and validates:

- schema version, pack id, and complete attribution;
- normalized relative asset paths (no rooted paths or traversal);
- atlas dimensions and every frame rectangle;
- one frame for every declared direction/sequence pair;
- positive rational render scale, frame duration, footprint, and height;
- contiguous, non-overlapping mask ranges with an exact file length.

Each frame records its atlas rectangle, foot-point origin, duration, and mask
offset. Mask pixels are row-major and packed most-significant-bit first. Bits
outside a frame are transparent. The masks are for deterministic hit testing;
Godot still uses the PNG alpha channel when drawing.

`ShapeFlags.Sprite` means “atlas-backed content.” It intentionally does not map
to `SortItem.IsSprite`, which is Pentagram's Crusader-style always-on-top
billboard rule and is wrong for Ultima VIII-style world actors.

## Rebuild the Flare starter pack

Clone `https://github.com/flareteam/flare-game`, check out the revision in
`ATTRIBUTION.md`, install Pillow, then run:

```powershell
python tools/import/build_flare_shape_pack.py `
  --flare-root C:\path\to\flare-game `
  --flare-revision 65638cbfea8231c7a40815daec8232b38c11cc9a `
  --output game\assets\shape-packs\flare-starter
```

Adding another community asset means adding an explicit `ShapeSource` entry to
the importer, pinning its source revision, and ensuring its licence is
compatible with redistribution. Flare is a useful baseline for ordinary
humanoids, equipment, and creatures. AshLaw's AD&D-specific silhouettes such as
beholders, behirs, catoblepas, and shadow demons need distinct original art.
