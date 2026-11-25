using System;
using System.Collections.Generic;
using UnityEngine;

namespace BrokenHeart.World
{
    [Serializable]
    public struct BlockEdit { public int x, y; public string id; }
    public sealed class ChunkBlockStore : IBlockStore
    {
        private sealed class CachedChunk
        {
            public readonly string[] Cells;
            public readonly LinkedListNode<Vector2Int> Recency;

            public CachedChunk(Vector2Int coordinate, int cellCount)
            {
                Cells = new string[cellCount];
                Recency = new LinkedListNode<Vector2Int>(coordinate);
            }
        }

        public const int InactiveChunkLimit = 24;
        private readonly Dictionary<Vector2Int, CachedChunk> chunks = new();
        // Chunks linked here are inactive. Active chunks stay pinned until their tiles leave the world view.
        private readonly LinkedList<Vector2Int> inactive = new();
        private readonly Dictionary<Vector2Int, string> edits = new();
        private readonly RectInt bounds;
        private readonly TerrainGenerator generator;
        public int ChunkSize { get; }
        public int CachedChunks => chunks.Count;
        public int InactiveChunks => inactive.Count;
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
            return GetChunk(chunk, false).Cells[IndexInChunk(cell, chunk)];
        }

        public string Peek(Vector2Int cell)
        {
            if (!Contains(cell)) return null;
            return edits.TryGetValue(cell, out var changed) ? changed : ReadBaseBlock(cell);
        }

        private string ReadBaseBlock(Vector2Int cell)
        {
            var chunk = ChunkAt(cell);
            return chunks.TryGetValue(chunk, out var cached)
                ? cached.Cells[IndexInChunk(cell, chunk)]
                : generator.Generate(cell);
        }

        private int IndexInChunk(Vector2Int cell, Vector2Int chunk)
        {
            var local = cell - chunk * ChunkSize;
            return local.y * ChunkSize + local.x;
        }

        private CachedChunk GetChunk(Vector2Int chunk, bool pin)
        {
            if (chunks.TryGetValue(chunk, out var cached))
            {
                if (cached.Recency.List != null)
                {
                    inactive.Remove(cached.Recency);
                    if (!pin) inactive.AddFirst(cached.Recency);
                }
                return cached;
            }

            cached = new CachedChunk(chunk, ChunkSize * ChunkSize);
            var origin = chunk * ChunkSize;
            for (int x = 0; x < ChunkSize; x++)
            {
                int surface = generator.Surface(origin.x + x);
                for (int y = 0; y < ChunkSize; y++)
                    cached.Cells[y * ChunkSize + x] = generator.Generate(origin.x + x, origin.y + y, surface);
            }
            chunks.Add(chunk, cached);
            if (!pin) CacheInactive(cached);
            return cached;
        }

        internal void Pin(Vector2Int chunk)
        {
            var cells = new RectInt(chunk * ChunkSize, Vector2Int.one * ChunkSize);
            if (bounds.Overlaps(cells)) GetChunk(chunk, true);
        }

        internal void Release(Vector2Int chunk)
        {
            if (chunks.TryGetValue(chunk, out var cached) && cached.Recency.List == null)
                CacheInactive(cached);
        }

        private void CacheInactive(CachedChunk chunk)
        {
            inactive.AddFirst(chunk.Recency);
            if (inactive.Count > InactiveChunkLimit) Unload(inactive.Last.Value);
        }

        public bool Set(Vector2Int cell, string id)
        {
            if (!Contains(cell)) return false;
            string original = ReadBaseBlock(cell);
            string previous = edits.TryGetValue(cell, out var changed) ? changed : original;
            if (previous == id) return false;
            if (original == id) edits.Remove(cell); else edits[cell] = id;
            Changed?.Invoke(cell, id);
            return true;
        }

        public void Unload(Vector2Int chunk)
        {
            if (chunks.Remove(chunk, out var cached) && cached.Recency.List != null) inactive.Remove(cached.Recency);
        }
        public BlockEdit[] ExportEdits()
        {
            var result = new BlockEdit[edits.Count]; int i = 0;
            foreach (var edit in edits) result[i++] = new BlockEdit { x = edit.Key.x, y = edit.Key.y, id = edit.Value };
            return result;
        }
    }
}
