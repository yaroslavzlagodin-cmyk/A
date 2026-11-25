using System.Collections.Generic;
using BrokenHeart.Crafting;
using BrokenHeart.Inventory;
using BrokenHeart.Systems;
using BrokenHeart.Systems.Energy;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BrokenHeart.World
{
    public sealed class SubsurfaceShadows : MonoBehaviour
    {
        private const byte Solid = 1, Sky = 2, Outside = 4;
        private const int SolidCost = 20, AirCost = 7;
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down,
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
        };
        private readonly Queue<int> spread = new();
        private readonly List<(Vector2Int Cell, int Power)> sources = new();
        private readonly List<(Vector2Int Cell, int Power)> previousSources = new();
        private BlockWorld world;
        private Transform observer;
        private DayNightSystem clock;
        private PlayerInventory inventory;
        private EnergyNetwork devices;
        private WorkstationSystem stations;
        private SpriteRenderer layer;
        private Material material;
        private Texture2D mask;
        private Sprite sprite;
        private byte[] geometry, light;
        private Color32[] pixels;
        private Vector2Int origin;
        private int size, lastRevision = -1, lastSkyPower;
        private float nextUpdate;

        internal void Initialize(BlockWorld blockWorld, Transform target, DayNightSystem time)
        {
            world = blockWorld;
            observer = target;
            clock = time;
            inventory = target.GetComponent<PlayerInventory>();
            devices = world.GetComponent<EnergyNetwork>();
            stations = world.GetComponent<WorkstationSystem>();
            size = world.Generation.chunkSize * (world.Generation.viewRadius * 2 + 1) + 16;
            geometry = new byte[size * size];
            light = new byte[geometry.Length];
            pixels = new Color32[geometry.Length];
            mask = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Soft terrain shadows",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            sprite = Sprite.Create(mask, new Rect(0, 0, size, size), Vector2.zero, 1, 0, SpriteMeshType.FullRect);
            var go = new GameObject("Soft Terrain Shadows", typeof(SpriteRenderer));
            go.transform.SetParent(world.Map.transform, false);
            layer = go.GetComponent<SpriteRenderer>();
            material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            var front = world.Map.GetComponent<TilemapRenderer>();
            layer.sharedMaterial = material;
            layer.sortingLayerID = front.sortingLayerID;
            layer.sortingOrder = front.sortingOrder + 1;
            layer.sprite = sprite;
            layer.enabled = false;
        }

        private void LateUpdate()
        {
            if (mask == null || Time.time < nextUpdate && lastRevision == world.TerrainRevision) return;
            nextUpdate = Time.time + 0.09f;
            var cell = world.CellAt(observer.position);
            var chunks = (ChunkBlockStore)world.Store;
            var corner = (chunks.ChunkAt(cell) - Vector2Int.one * world.Generation.viewRadius) * chunks.ChunkSize -
                Vector2Int.one * 8;
            bool geometryChanged = corner != origin || lastRevision != world.TerrainRevision;
            if (geometryChanged) ReadGeometry(corner);
            int skyPower = Mathf.RoundToInt(Mathf.Lerp(60, 83, clock != null ? clock.SurfaceIntensity : 1));

            sources.Clear();
            bool equippedLight = inventory != null && !inventory.Equipped((int)EquipmentSlot.Lamp - 1).Empty;
            sources.Add((cell, equippedLight ? 83 : 63));
            if (devices != null)
                foreach (var device in devices.Devices)
                    if (device.Powered) sources.Add((world.CellAt(device.transform.position), 83));
            if (stations != null)
                foreach (var station in stations.Stations)
                    if (station.BurnRemaining > 0) sources.Add((station.Cell, 72));
            if (!geometryChanged && skyPower == lastSkyPower && SameSources()) return;

            previousSources.Clear();
            previousSources.AddRange(sources);
            lastSkyPower = skyPower;
            Bake(skyPower);
        }

        private bool SameSources()
        {
            if (sources.Count != previousSources.Count) return false;
            for (int i = 0; i < sources.Count; i++)
                if (sources[i] != previousSources[i]) return false;
            return true;
        }

        private void ReadGeometry(Vector2Int corner)
        {
            origin = corner;
            lastRevision = world.TerrainRevision;
            layer.transform.position = world.Map.CellToWorld((Vector3Int)origin);
            for (int x = 0; x < size; x++)
            {
                int worldX = origin.x + x;
                int surface = world.Generator.Surface(worldX);
                for (int y = 0; y < size; y++)
                {
                    var cell = new Vector2Int(worldX, origin.y + y);
                    geometry[y * size + x] = !world.Bounds.Contains(cell) ? Outside :
                        world.IsSolidAt(cell) ? Solid : cell.y > surface ? Sky : (byte)0;
                }
            }
        }

        private void Bake(int skyPower)
        {
            spread.Clear();
            for (int i = 0; i < light.Length; i++)
            {
                light[i] = 0;
                if (geometry[i] == Sky) Illuminate(i, skyPower);
            }
            foreach (var source in sources)
                if (TryIndex(source.Cell, out int index) && (geometry[index] & (Solid | Outside)) == 0)
                    Illuminate(index, source.Power);

            // Light loses more strength crossing rock than crossing air. Full daylight reaches four cells into a wall.
            while (spread.TryDequeue(out int index))
            {
                int x = index % size, y = index / size;
                int power = light[index];
                if (power <= AirCost) continue;
                foreach (var direction in Directions)
                {
                    int nx = x + direction.x, ny = y + direction.y;
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size) continue;
                    int next = ny * size + nx;
                    if (geometry[next] == Outside) continue;
                    // Pay for crossing the wall in either direction so a bright cave cannot light a sealed neighbor.
                    int cost = ((geometry[index] | geometry[next]) & Solid) != 0 ? SolidCost : AirCost;
                    if (direction.x != 0 && direction.y != 0)
                    {
                        if (geometry[y * size + nx] == Solid && geometry[ny * size + x] == Solid) continue;
                        cost += cost * 2 / 5;
                    }
                    Illuminate(next, power - cost);
                }
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                float opacity = geometry[i] == Sky || geometry[i] == Outside ? 0 :
                    1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(3, geometry[i] == Solid ? 61 : 45, light[i]));
                pixels[i] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(opacity * 255));
            }
            mask.SetPixels32(pixels);
            mask.Apply(false);
            layer.enabled = true;
        }

        private void Illuminate(int index, int power)
        {
            if (power <= light[index]) return;
            light[index] = (byte)power;
            spread.Enqueue(index);
        }

        private bool TryIndex(Vector2Int cell, out int index)
        {
            var local = cell - origin;
            index = local.y * size + local.x;
            return (uint)local.x < (uint)size && (uint)local.y < (uint)size;
        }

        public byte OpacityAt(Vector2Int cell) => layer.enabled && TryIndex(cell, out int index) ? pixels[index].a : (byte)0;

        private void OnDestroy()
        {
            Destroy(sprite);
            Destroy(mask);
            Destroy(material);
        }
    }
}
