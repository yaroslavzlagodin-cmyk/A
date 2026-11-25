using System;
using System.Collections.Generic;
using UnityEngine;

namespace BrokenHeart.World
{
    public sealed class BlockStore : IBlockStore
    {
        private readonly RectInt bounds;
        private readonly Dictionary<Vector2Int, string> blocks = new();
        public event Action<Vector2Int, string> Changed;
        public BlockStore(RectInt bounds) { this.bounds = bounds; }
        public bool Contains(Vector2Int cell) => bounds.Contains(cell);
        public string Get(Vector2Int cell) => blocks.TryGetValue(cell, out var id) ? id : null;
        public bool Set(Vector2Int cell, string blockId)
        {
            if (!Contains(cell) || Get(cell) == blockId) return false;
            if (string.IsNullOrEmpty(blockId)) blocks.Remove(cell);
            else blocks[cell] = blockId;
            Changed?.Invoke(cell, blockId);
            return true;
        }
    }
}
