using System;
using System.Collections.Generic;
using UnityEngine;

namespace BrokenHeart.World
{
    [Serializable]
    public struct BlockEdit { public int x, y; public string id; }
    public sealed class ChunkBlockStore : IBlockStore
    {
        private readonly Dictionary<Vector2Int, string[]> chunks = new();
        private readonly Dictionary<Vector2Int, string> edits = new();
        private readonly RectInt bounds;
        private readonly TerrainGenerator generator;
        public int ChunkSize { get; }
        public int CachedChunks => chunks.Count;
        public event Action<Vector2Int, string> Changed;
        public ChunkBlockStore(RectInt bounds, int size, TerrainGenerator generator)
        { this.bounds = bounds; ChunkSize = size; this.generator = generator; }
        public Vector2Int ChunkAt(Vector2Int cell) => new(Mathf.FloorToInt(cell.x / (float)ChunkSize), Mathf.FloorToInt(cell.y / (float)ChunkSize));
        public bool Contains(Vector2Int cell) => bounds.Contains(cell);
        public string Get(Vector2Int cell)
        {
            if (!Contains(cell)) return null;
            if (edits.TryGetValue(cell, out var changed)) return changed;
            var chunk = ChunkAt(cell);
            if (!chunks.TryGetValue(chunk, out var cells))
            {
                cells = new string[ChunkSize * ChunkSize];
                var origin = chunk * ChunkSize;
                for (int x = 0; x < ChunkSize; x++)
                {
                    int surface = generator.Surface(origin.x + x);
                    for (int y = 0; y < ChunkSize; y++)
                        cells[y * ChunkSize + x] = generator.Generate(origin.x + x, origin.y + y, surface);
                }
                chunks.Add(chunk, cells);
            }
            var local = cell - chunk * ChunkSize;
            return cells[local.y * ChunkSize + local.x];
        }
        public string Peek(Vector2Int cell)
        {
            if (!Contains(cell)) return null;
            if (edits.TryGetValue(cell, out var changed)) return changed;
            var chunk = ChunkAt(cell);
            if (!chunks.TryGetValue(chunk, out var cells)) return generator.Generate(cell);
            var local = cell - chunk * ChunkSize;
            return cells[local.y * ChunkSize + local.x];
        }
        public bool Set(Vector2Int cell, string id)
        {
            if (!Contains(cell) || Get(cell) == id) return false;
            if (generator.Generate(cell) == id) edits.Remove(cell); else edits[cell] = id;
            Changed?.Invoke(cell, id);
            return true;
        }
        public void Unload(Vector2Int chunk) => chunks.Remove(chunk);
        public BlockEdit[] ExportEdits()
        {
            var result = new BlockEdit[edits.Count]; int i = 0;
            foreach (var edit in edits) result[i++] = new BlockEdit { x = edit.Key.x, y = edit.Key.y, id = edit.Value };
            return result;
        }
    }
}
