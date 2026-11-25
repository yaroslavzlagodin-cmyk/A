using System.Collections.Generic;
using UnityEngine;

namespace BrokenHeart.World
{
    public sealed class TerrainGenerator
    {
        private readonly int seed;
        private readonly WorldGenerationData data;
        private readonly Dictionary<int, Tree> forestTrees = new();
        private const int ForestSpacing = 6;
        private const int MaxCrownRise = 5;
        public int Version { get; }

        public TerrainGenerator(int seed, WorldGenerationData data, int? version = null)
        {
            this.seed = seed;
            this.data = data;
            Version = version ?? data.version;
        }
        private uint Hash(int value, int salt)
        {
            uint h = unchecked((uint)seed ^ (uint)value * 374761393u ^ (uint)salt * 668265263u);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
        private float Noise(float x, float y, int salt = 0) => Mathf.PerlinNoise(x + (Hash(0,salt) % 20000) * 0.037f, y + (Hash(1,salt) % 20000) * 0.041f);
        private float MountainWeight(int x)
        {
            if (!data.mountains) return 0;
            float scale = Mathf.Max(0.25f,data.biomeScale);
            float field = Noise(x * 0.007f / scale, 13, 911);
            if (data.biomeLayout == 1) field = Mathf.Clamp01(Mathf.Abs(x) / (220f*scale)) * 0.6f + field * 0.4f;
            if (data.biomeLayout == 2) field = 0.5f + Mathf.Sin(x / (55f*scale)) * 0.22f + (field-0.5f)*0.4f;
            return Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.4f,0.68f,field));
        }
        public string Biome(int x) => Mathf.Abs(x) < 20 || MountainWeight(x) < 0.55f ? "forest" : "mountains";
        public int Surface(int x)
        {
            if (Mathf.Abs(x) <= 12) return 0;
            float scale = Mathf.Max(0.25f,data.terrainScale);
            float warped = x + (Noise(x * 0.006f, 7, 43)-0.5f)*36;
            float broad = (Noise(warped*0.009f/scale,0,71)-0.5f)*18;
            float hills = (Noise(warped*0.027f/scale,0,83)-0.5f)*9;
            float detail = (Noise(x*0.065f/scale,0,97)-0.5f)*2.5f;
            float ridge = 1-Mathf.Abs(Noise(warped*0.013f/scale,0,101)*2-1);
            float peaks = MountainWeight(x)*Mathf.Pow(ridge,1.8f)*data.mountainHeight;
            float spawnBlend = Mathf.SmoothStep(0,1,Mathf.InverseLerp(12,34,Mathf.Abs(x)));
            if (Version >= WorldGenerationData.CaveVersion)
            {
                float valleys = Noise(warped * 0.0045f / scale, 31, 433);
                broad += (valleys - 0.5f) * 15;
                peaks *= Mathf.Lerp(0.62f, 1.09f, valleys);
            }
            return Mathf.RoundToInt((broad+hills+detail+peaks)*spawnBlend);
        }
        private enum CrownShape { Round, Tall, Pine, Spreading }

        private readonly struct Tree
        {
            public readonly int X, Ground, Height, Radius;
            public readonly CrownShape Shape;
            public readonly int Lean;
            public Tree(int x, int ground, int height, int radius, CrownShape shape = CrownShape.Round, int lean = 0)
            {
                X = x; Ground = ground; Height = height; Radius = radius; Shape = shape; Lean = lean;
            }
            public int Crown => Ground + Height;
        }

        private string Vegetation(int x, int y) => TreePart(x, y, out _);

        private string TreePart(int x, int y, out Tree tree) => Version >= WorldGenerationData.ForestVersion
            ? ForestPart(x, y, out tree) : LegacyTreePart(x, y, out tree);

        private string LegacyTreePart(int x, int y, out Tree tree)
        {
            int bucket = Mathf.FloorToInt(x/8f);
            // Nearby candidates are derived from seed, never from generation order or chunk boundaries.
            for (int b=bucket-1;b<=bucket+1;b++)
            {
                int treeX=b*8+1+(int)(Hash(b,23)%6);
                if (Mathf.Abs(treeX)<17 || Biome(treeX)!="forest") continue;
                float density=Noise(treeX*0.022f,6,151);
                if (Hash(b,29)%100 > Mathf.Lerp(28,90,density)) continue;
                int ground=Surface(treeX), height=4+(int)(Hash(b,31)%4), radius=2+(int)(Hash(b,37)%2);
                int dx=Mathf.Abs(x-treeX), crown=ground+height;
                if (dx > radius + 1) continue;
                if (x == treeX && y > ground && y <= crown)
                {
                    tree = new Tree(treeX, ground, height, radius);
                    return data.wood.Id;
                }
                float shape=dx*dx/(float)(radius*radius)+Mathf.Pow((y-crown)/(radius*0.85f),2);
                if (shape <= 1.15f && y > ground + 2)
                {
                    tree = new Tree(treeX, ground, height, radius);
                    return data.leaves.Id;
                }
            }
            tree = default;
            return null;
        }

        private Tree ForestTree(int bucket)
        {
            if (forestTrees.TryGetValue(bucket, out var tree)) return tree;
            int x = bucket * ForestSpacing + 1 + (int)(Hash(bucket, 109) % 4);
            if (Mathf.Abs(x) >= 15)
            {
                bool mountain = Biome(x) == "mountains";
                float density = Noise(x * 0.019f, 7, 173);
                float chance = mountain ? Mathf.Lerp(7, 36, density) : Mathf.Lerp(66, 98, density);
                if (Hash(bucket, 179) % 100 < chance)
                {
                    int ground = Surface(x);
                    if (!IsCave(x, ground, 0))
                    {
                        var shape = mountain ? CrownShape.Pine : (CrownShape)(Hash(bucket, 181) % 4);
                        int heightRoll = (int)(Hash(bucket, 191) % 6);
                        int widthRoll = (int)(Hash(bucket, 193) % 2);
                        (int height, int radius) = shape switch
                        {
                            CrownShape.Tall => (10 + heightRoll, 3 + widthRoll),
                            CrownShape.Pine => (9 + heightRoll, 4 + widthRoll),
                            CrownShape.Spreading => (7 + heightRoll, 5 + widthRoll),
                            _ => (7 + heightRoll, 4 + widthRoll)
                        };
                        tree = new Tree(x, ground, height, radius, shape, (Hash(bucket, 197) & 1) == 0 ? -1 : 1);
                    }
                }
            }
            forestTrees.Add(bucket, tree);
            return tree;
        }

        private string ForestPart(int x, int y, out Tree owner)
        {
            owner = default;
            string part = null;
            int score = int.MaxValue;
            int bucket = Mathf.FloorToInt(x / (float)ForestSpacing);
            for (int i = bucket - 1; i <= bucket + 1; i++)
            {
                var tree = ForestTree(i);
                int dx = x - tree.X;
                if (tree.Height == 0 || Mathf.Abs(dx) > tree.Radius || y <= tree.Ground || y > tree.Crown + MaxCrownRise)
                    continue;
                if (dx == 0 && y <= tree.Crown)
                {
                    owner = tree;
                    return data.wood.Id;
                }
                int dy = y - tree.Crown;
                bool branch = HasBranch(tree, dx, dy);
                if (!branch && !HasLeaves(tree, dx, dy)) continue;
                // Trunks win first, then branches, then the closest crown.
                int nextScore = (branch ? 0 : 10) + Mathf.Abs(dx);
                if (nextScore >= score) continue;
                owner = tree;
                part = branch ? data.wood.Id : data.leaves.Id;
                score = nextScore;
            }
            return part;
        }

        private static bool HasBranch(Tree tree, int x, int y)
        {
            if (tree.Shape == CrownShape.Pine || tree.Shape == CrownShape.Tall) return false;
            if (y >= -4 && y <= -2 && x * tree.Lean >= y + 5 && x * tree.Lean <= y + 6) return true;
            return tree.Shape == CrownShape.Spreading && y >= -3 && y <= -1 && -x * tree.Lean == y + 4;
        }

        private static bool HasLeaves(Tree tree, int x, int y)
        {
            float radius = tree.Radius;
            return tree.Shape switch
            {
                CrownShape.Tall => Ellipse(x - 0.45f * tree.Lean, y, radius, 5.25f),
                CrownShape.Pine => PineLeaves(x, y, tree.Radius),
                CrownShape.Spreading => Ellipse(x, y, radius + 0.35f, 2.6f) ||
                    Ellipse(x - tree.Lean * 2, y - 2, radius - 2, 2.5f) ||
                    Ellipse(x + tree.Lean * 2, y + 2, radius - 2, 1.9f),
                _ => Ellipse(x - tree.Lean * 0.6f, y, radius - 0.25f, 3.8f) ||
                    Ellipse(x + tree.Lean * 2, y + 2, radius - 1.5f, 2.4f)
            };
        }

        private static bool Ellipse(float x, float y, float width, float height) =>
            x * x / (width * width) + y * y / (height * height) <= 1;

        private static bool PineLeaves(int x, int y, int radius)
        {
            int fromTip = 4 - y;
            if (fromTip < 0 || fromTip > 11) return false;
            int tier = fromTip / 3;
            int width = tier * (radius - 1) / 3 + fromTip % 3;
            return Mathf.Abs(x) <= Mathf.Min(radius, width);
        }

        public IEnumerable<(Vector2Int Cell, string Id)> TreeAt(Vector2Int cell)
        {
            if (cell.y <= Surface(cell.x) || TreePart(cell.x, cell.y, out var tree) == null) yield break;
            int top = tree.Crown + (Version >= WorldGenerationData.ForestVersion ? MaxCrownRise : tree.Radius);
            for (int x = tree.X - tree.Radius; x <= tree.X + tree.Radius; x++)
            {
                int surface = Surface(x);
                for (int y = Mathf.Max(surface, tree.Ground) + 1; y <= top; y++)
                {
                    string part = TreePart(x, y, out var owner);
                    if (part != null && owner.X == tree.X) yield return (new Vector2Int(x, y), part);
                }
            }
        }
        private bool IsCave(int x, int y, int depth)
        {
            if (Mathf.Abs(x) < 20 && depth < 9) return false;

            // Each entrance slopes into the ground, independent of which chunk is built first.
            const int spacing = 88;
            int region = Mathf.FloorToInt(x / (float)spacing);
            for (int i = region - 1; i <= region + 1; i++)
            {
                int entrance = i * spacing + 29 + (int)(Hash(i, 307) % 23);
                int direction = (Hash(i, 331) & 1) == 0 ? 1 : -1;
                int along = (x - entrance) * direction;
                if (along < -2 || along > 47) continue;
                float down = 0.50f * along + Mathf.Sin(along * 0.18f) * 1.8f;
                float width = Mathf.Lerp(2.1f, 5.3f, Mathf.SmoothStep(0, 1, along / 30f));
                width *= Mathf.SmoothStep(0, 1, (48 - along) / 9f);
                if (Mathf.Abs(depth - down) < width) return true;
            }

            if (depth < 7) return false;
            float warp = (Noise(x * 0.041f, y * 0.043f, 509) - 0.5f) * 8;
            float chamber = Noise((x + warp) * 0.052f, (y - warp) * 0.078f, 521);
            float network = Noise((x - warp) * 0.031f, y * 0.057f, 557);
            float rock = Noise(x * 0.019f, y * 0.033f, 569);
            float cover = Mathf.Clamp01((depth - 7) / 14f);
            return chamber > Mathf.Lerp(0.76f, 0.62f, cover)
                || (cover > 0.25f && rock > 0.41f && Mathf.Abs(network - 0.50f) < cover * 0.051f);
        }

        public BlockData Background(int depth) => depth < 1 ? null : depth < 5 ? data.dirt : data.stone;

        public string Generate(Vector2Int cell) => Generate(cell.x, cell.y, Surface(cell.x));

        internal string Generate(int x, int y, int surface)
        {
            if(y>surface) return Vegetation(x,y);
            int depth = surface - y;
            bool updated = Version >= WorldGenerationData.CaveVersion;
            if (updated && IsCave(x, y, depth)) return null;
            bool scree = updated && surface > 11 && MountainWeight(x) > 0.60f && Noise(x * 0.088f, 8, 601) > 0.48f;
            if (depth == 0) return scree ? data.stone.Id : data.grass.Id;
            int soilDepth = scree ? 2 : 3 + Mathf.RoundToInt(Noise(x * 0.05f, 0, 203) * (updated ? 4 : 3));
            if (depth < soilDepth) return data.dirt.Id;
            foreach(var ore in data.ores)
            {
                float threshold=Mathf.Clamp(ore.threshold-Mathf.Log(Mathf.Max(0.1f,data.resourceMultiplier))*0.10f,0.45f,0.92f);
                if (depth < ore.minimumDepth) continue;
                int salt = ore.minimumDepth * 67;
                float vein = updated
                    ? Noise((x + y * 0.38f) * 0.145f, y * 0.24f, salt) * 0.79f + Noise(x * 0.32f, y * 0.27f, salt + 1) * 0.21f
                    : Noise(x * 0.21f, y * 0.23f, salt);
                if (vein > threshold) return ore.block.Id;
            }
            return data.stone.Id;
        }
    }
}
