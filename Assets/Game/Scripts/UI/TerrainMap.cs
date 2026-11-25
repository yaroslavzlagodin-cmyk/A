using System;
using BrokenHeart.World;
using UnityEngine;

namespace BrokenHeart.UI
{
    internal sealed class TerrainMap : IDisposable
    {
        private static readonly Color32 Sky = new(27, 45, 61, 255);
        private static readonly Color32 Stone = new(74, 75, 92, 255);
        private static readonly Color32 Soil = new(100, 72, 60, 255);
        private static readonly Color32 Grass = new(127, 156, 98, 255);
        private static readonly Color32 Forest = new(57, 112, 86, 255);
        private static readonly Color32 Mountain = new(156, 149, 139, 255);
        private static readonly Color32 Wood = new(154, 107, 67, 255);
        private static readonly Color32 Copper = new(190, 116, 78, 255);
        private static readonly Color32 Iron = new(158, 151, 157, 255);
        private static readonly Color32 Crystal = new(140, 194, 207, 255);
        private static readonly Color32 Masonry = new(120, 119, 135, 255);
        private static readonly Color32 Cave = new(28, 30, 43, 255);
        private static readonly Color32 Unexplored = new(6, 10, 17, 255);

        private IBlockStore store;
        private RectInt view;
        private Texture2D texture;
        private Color32[] pixels;
        private bool dirty = true;
        private int explorationRevision;

        public void Draw(BlockWorld world, Rect rect, RectInt bounds, int maxWidth, int maxHeight)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (store != world.Store)
            {
                if (store != null) store.Changed -= OnBlockChanged;
                store = world.Store;
                store.Changed += OnBlockChanged;
                dirty = true;
            }
            int width = Mathf.Min(bounds.width, maxWidth);
            int height = Mathf.Min(bounds.height, maxHeight);
            if (dirty || explorationRevision != world.ExplorationRevision || view != bounds ||
                texture == null || texture.width != width || texture.height != height)
                Render(world, bounds, width, height);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
        }

        private void Render(BlockWorld world, RectInt bounds, int width, int height)
        {
            if (texture == null || texture.width != width || texture.height != height)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "Terrain Map",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                pixels = new Color32[width * height];
            }
            Array.Fill(pixels, Unexplored);
            if (bounds.width == width && bounds.height == height)
            {
                for (int i = 0; i < width; i++)
                {
                    int x = bounds.xMin + i;
                    int surface = world.Generator.Surface(x);
                    Color32 ground = GroundColor(world, x);
                    for (int j = 0; j < height; j++)
                    {
                        var cell = new Vector2Int(x, bounds.yMin + j);
                        if (world.IsExplored(cell)) pixels[j * width + i] = CellColor(cell, ground, surface);
                    }
                }
            }
            else
            {
                // Project revealed cells into the overview so thin paths remain visible when zoomed out.
                foreach (var cell in world.ExploredCells)
                {
                    if (!bounds.Contains(cell)) continue;
                    int i = (cell.x - bounds.xMin) * width / bounds.width;
                    int j = (cell.y - bounds.yMin) * height / bounds.height;
                    int index = j * width + i;
                    if (!pixels[index].Equals(Unexplored)) continue;
                    pixels[index] = CellColor(cell, GroundColor(world, cell.x), world.Generator.Surface(cell.x));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            view = bounds;
            explorationRevision = world.ExplorationRevision;
            dirty = false;
        }

        private Color32 CellColor(Vector2Int cell, Color32 ground, int surface) =>
            BlockColor(store is ChunkBlockStore chunks ? chunks.Peek(cell) : store.Get(cell), ground, surface - cell.y);

        private static Color32 GroundColor(BlockWorld world, int x) => world.Generator.Biome(x) switch
        {
            "forest" => Forest,
            "mountains" => Mountain,
            _ => Grass
        };

        private static Color32 StoneShade(int depth) => Color32.Lerp(Stone, Cave, Mathf.Clamp01(depth / 180f) * 0.64f);

        private static Color32 BlockColor(string id, Color32 ground, int depth) => id switch
        {
            null => depth > 0 ? Cave : Sky,
            "grass" or "grass_cover" => ground,
            "dirt" => Soil,
            "stone" => StoneShade(depth),
            "leaves" => Forest,
            "wood" or "planks" => Wood,
            "copper" => Copper,
            "iron" => Iron,
            "crystal" => Crystal,
            _ => Masonry
        };

        private void OnBlockChanged(Vector2Int cell, string id)
        {
            if (view.Contains(cell)) dirty = true;
        }

        public void Dispose()
        {
            if (store != null) store.Changed -= OnBlockChanged;
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
    }
}
