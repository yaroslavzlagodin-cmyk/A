# Dark Fantasy 32

Five opaque 32x32 PNG tiles, left to right in the preview: GrassCover, GrassSoil, Dirt, Stone, Rubble.
Unity import: Sprite Single, 32 PPU, Point, no mipmaps, uncompressed. Original gameplay textures remain available; these are a separate art set.
Generated with built-in image_gen; atlas cropped and sampled at pixel centers to exact 32x32 PNGs without interpolation. Source and enlarged preview: Logs/Art/DarkFantasy32-source.png and DarkFantasy32-preview.png.

Final prompt: Single five-tile atlas, each 32x32 logical pixels, Minecraft terrain simplicity combined with restrained dark fantasy. Order: full grass, grass-topped dirt, dirt, continuous stone, rubble. Large flat clusters, minimal fine detail, moderate saturation, muted olive green, umber brown, cool slate gray. Uniform ambient lighting, readable medium values, no perspective, no labels, no fine cracks, no noise or blur.

Additional built-in image_gen prompt: Five square swatches — umber bark, olive leaves, copper ore, iron ore, teal crystal ore. Minecraft + restrained dark fantasy, simple large 3–8 pixel clusters, 6–9 colors, moderate saturation, medium values, no noise, glow, detail or perspective. Source: Logs/Art/DarkFantasy32-extra-source.png. All ten 32x32 textures are now assigned to game tiles and item icons by Editor/VisualUpgrade.cs.
