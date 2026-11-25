# User-approved textures

These PNGs are unscaled pixel-for-pixel crops from the user's final attached sheet
`codex-clipboard-c51274d0-2f75-4ec7-bdcd-d5a8dbd871af.png`.
No image generation, recoloring, palette reduction, or resampling was used.
Only pure white (255,255,255) pixels within Leaves were made transparent.

Crop rectangles (x, y, width, height):

| PNG | Rectangle |
| --- | --- |
| Leaves | 45,19,141,141 |
| GrassCover | 203,19,141,141 |
| Stone | 366,19,141,141 |
| Wood | 45,178,141,140 |
| GrassSoil | 203,178,141,140 |
| Rubble | 366,178,141,140 |
| MossyRubble | 529,178,140,140 |
| Planks | 45,336,141,140 |
| Dirt | 203,336,141,140 |
| StoneBricks | 366,336,141,140 |
| MossyStoneBricks | 529,336,140,140 |

Native screenshot dimensions intentionally retained to preserve the supplied pixels.
Unity imports use point filtering, no compression/mipmaps/NPOT scaling or RGB dilation.
Tile transforms fit the slightly non-square screenshot crops into one world cell.
The existing world lighting still applies, including day/night changes.

Apply/verify via `BrokenHeart.Editor.UserApprovedTextures.Apply` in Unity batch mode,
or the Broken Heart / Apply User Approved Textures menu. Existing block IDs stay
unchanged; four additional blocks have inventory entries and crafting recipes.
