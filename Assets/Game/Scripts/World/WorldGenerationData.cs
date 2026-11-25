using System;
using UnityEngine;

namespace BrokenHeart.World
{
    [Serializable]
    public struct OreRule { public BlockData block; public int minimumDepth; [Range(0,1)] public float threshold; }
    [CreateAssetMenu(menuName = "Broken Heart/World Generation")]
    public sealed class WorldGenerationData : ScriptableObject
    {
        public const int LegacyVersion = 2;
        public const int CaveVersion = 3;
        public const int ForestVersion = 4;
        public const int CurrentVersion = ForestVersion;
        public BlockData grass, dirt, stone, wood, leaves, ruin;
        public OreRule[] ores;
        [Min(8)] public int chunkSize = 16;
        [Range(1,4)] public int viewRadius = 2;
        public int version = CurrentVersion;
        public bool mountains = true;
        [Range(0,48)] public float mountainHeight = 28;
        [Range(0.25f,3)] public float terrainScale = 1;
        [Range(0.25f,3)] public float biomeScale = 1;
        [Range(0.1f,3)] public float resourceMultiplier = 1;
        public int biomeLayout;
    }
}
