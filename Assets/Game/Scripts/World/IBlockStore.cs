using System;
using UnityEngine;

namespace BrokenHeart.World
{
    // Stores stable IDs, never GameObjects or visual tiles. A chunk store can replace this.
    public interface IBlockStore
    {
        event Action<Vector2Int, string> Changed;
        bool Contains(Vector2Int cell);
        string Get(Vector2Int cell);
        bool Set(Vector2Int cell, string blockId);
    }
}
