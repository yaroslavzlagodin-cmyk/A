using UnityEngine;

namespace BrokenHeart.World
{
    public static class TestTerrain
    {
        public static int Surface(int x, int seed)
        {
            // Flat safe spawn area; integer hash makes the remaining terraces reproducible.
            if (Mathf.Abs(x) < 8) return 0;
            uint hash = unchecked((uint)(Mathf.FloorToInt(x / 6f) * 374761393 + seed));
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            return (int)((hash ^ (hash >> 16)) % 4) - 1;
        }

        public static void Fill(IBlockStore store, RectInt bounds, int seed,
            string grass, string dirt, string stone)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                int top = Surface(x, seed);
                for (int y = bounds.yMin; y <= top; y++)
                    store.Set(new Vector2Int(x, y), y == top ? grass : y > top - 4 ? dirt : stone);
            }
        }
    }
}
