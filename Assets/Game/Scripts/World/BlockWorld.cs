using System.Collections.Generic;
using BrokenHeart.Inventory;
using BrokenHeart.Player;
using BrokenHeart.Systems;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BrokenHeart.World
{
    public sealed class BlockWorld : MonoBehaviour
    {
        [SerializeField] private Tilemap tilemap;
        [SerializeField] private BlockData[] palette;
        [SerializeField] private int seed = 1207;
        [SerializeField] private RectInt bounds = new(-64, -24, 128, 56);
        [SerializeField] private WorldGenerationData generation;
        [SerializeField] private Transform observer;
        // Controls both the shadow mask and depth tint on terrain tiles.
        [SerializeField] private bool terrainShadowsEnabled = false;
        private readonly HashSet<Vector2Int> visible = new();
        private Vector2Int lastChunk = new(int.MinValue, int.MinValue);
        private RectInt lastCameraChunks;
        private readonly Dictionary<string, BlockData> definitions = new();
        private readonly HashSet<Vector2Int> explored = new();
        private Tilemap background;
        private DayNightSystem clock;
        private PlayerInventory inventory;
        private Camera view;
        private CameraFollow cameraFollow;
        private Vector2Int lastSurveyOrigin;
        private int lastSurveyRadius;
        private bool surveyDirty = true;
        public IBlockStore Store { get; private set; }
        public Tilemap Map => tilemap;
        public BlockData[] Palette => palette;
        public RectInt Bounds => bounds;
        public int Seed => seed;
        public WorldGenerationData Generation => generation;
        public TerrainGenerator Generator { get; private set; }
        public int VisibleChunks => visible.Count;
        public int ExplorationRevision { get; private set; }
        internal int TerrainRevision { get; private set; }
        internal bool TerrainShadowsEnabled => terrainShadowsEnabled;
        public event System.Action<Vector2Int> CellChanged;
        public IEnumerable<Vector2Int> ExploredCells => explored;
        public void Configure(int newSeed,RectInt newBounds,WorldGenerationData settings)
        { seed=newSeed;bounds=newBounds;generation=settings; }

        private void Awake()
        {
            if (tilemap == null || palette == null || palette.Length < 3)
                throw new System.InvalidOperationException("BlockWorld requires a Tilemap and terrain palette.");
            foreach (var block in palette)
            {
                if (block == null || string.IsNullOrWhiteSpace(block.Id) || block.Tile == null ||
                    !definitions.TryAdd(block.Id, block))
                    throw new System.InvalidOperationException("Block IDs must be unique and tiles assigned.");
            }
            clock = GetComponent<DayNightSystem>();
            view = Camera.main;
            if (view != null) cameraFollow = view.GetComponent<CameraFollow>();
            if (observer != null) inventory = observer.GetComponent<PlayerInventory>();
            var walls = new GameObject("Background Walls", typeof(Tilemap), typeof(TilemapRenderer));
            walls.transform.SetParent(tilemap.transform, false);
            background = walls.GetComponent<Tilemap>();
            var renderer = walls.GetComponent<TilemapRenderer>();
            var foreground = tilemap.GetComponent<TilemapRenderer>();
            renderer.sharedMaterial = foreground.sharedMaterial;
            renderer.sortingLayerID = foreground.sortingLayerID;
            renderer.sortingOrder = foreground.sortingOrder - 1;
            ResetWorld(seed);
            if (terrainShadowsEnabled && generation != null && observer != null)
                gameObject.AddComponent<SubsurfaceShadows>().Initialize(this, observer, clock);
        }

        public void ResetWorld(int newSeed, int? version = null)
        {
            if (Store != null) Store.Changed -= RenderCell;
            seed = newSeed; visible.Clear(); lastChunk = new Vector2Int(int.MinValue, int.MinValue);
            explored.Clear();
            surveyDirty = true;
            ExplorationRevision++;
            TerrainRevision++;
            tilemap.ClearAllTiles();
            background.ClearAllTiles();
            Generator = generation != null ? new TerrainGenerator(seed, generation, version) : null;
            Store = generation != null ? new ChunkBlockStore(bounds, generation.chunkSize, Generator) : new BlockStore(bounds);
            Store.Changed += RenderCell;
            if (generation == null) TestTerrain.Fill(Store, bounds, seed, palette[0].Id, palette[1].Id, palette[2].Id);
            else RefreshVisible(true);
        }
        private void Update()
        {
            if (Generator == null || observer == null) return;
            Survey();
        }

        private void LateUpdate() => RefreshVisible(false);

        private void Survey()
        {
            var origin = CellAt(observer.position);
            if (!Bounds.Contains(origin)) return;
            bool underground = DepthAt(origin) > 3;
            bool lamp = inventory != null && !inventory.Equipped((int)EquipmentSlot.Lamp - 1).Empty;
            int radius = underground ? (lamp ? 8 : 5) : clock != null && clock.IsNight ? (lamp ? 10 : 6) : 14;
            if (!surveyDirty && origin == lastSurveyOrigin && radius == lastSurveyRadius) return;
            surveyDirty = false;
            lastSurveyOrigin = origin;
            lastSurveyRadius = radius;
            int previous = explored.Count;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                var cell = origin + new Vector2Int(dx, dy);
                if (Bounds.Contains(cell) && !explored.Contains(cell) && HasSight(origin, cell)) explored.Add(cell);
            }
            if (explored.Count != previous) ExplorationRevision++;
        }

        private bool HasSight(Vector2Int from, Vector2Int to)
        {
            var delta = to - from;
            int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            for (int i = 1; i < steps; i++)
            {
                var cell = from + Vector2Int.RoundToInt((Vector2)delta * (i / (float)steps));
                if (IsSolidAt(cell)) return false;
            }
            return true;
        }

        public bool IsExplored(Vector2Int cell) => explored.Contains(cell);
        public int DepthAt(Vector2Int cell) => Generator != null ? Generator.Surface(cell.x) - cell.y : 0;

        public void RestoreExploration(IEnumerable<Vector2Int> cells)
        {
            if (cells != null) explored.UnionWith(cells);
            ExplorationRevision++;
            if (Generator != null && observer != null) Survey();
        }

        internal bool IsSolidAt(Vector2Int cell)
        {
            var id = Store is ChunkBlockStore chunks ? chunks.Peek(cell) : Store.Get(cell);
            return IsSolidId(id);
        }

        internal bool IsSolidId(string id) => id != null && definitions.TryGetValue(id, out var block) && block.Solid;

        private Color ForegroundColor(int depth)
        {
            if (!terrainShadowsEnabled || depth < 1) return Color.white;
            float shade = 1 - Mathf.SmoothStep(0, 0.22f, depth / 28f);
            return new Color(shade, shade, Mathf.Min(1, shade * 1.08f));
        }

        private Color WallColor(int depth)
        {
            float shade = terrainShadowsEnabled ? Mathf.Lerp(0.56f, 0.39f, Mathf.Clamp01(depth / 34f)) : 0.56f;
            return new Color(shade * 0.83f, shade * 0.87f, shade);
        }

        public void RefreshVisible(bool force)
        {
            if (!(Store is ChunkBlockStore chunks) || observer == null) return;
            var center = chunks.ChunkAt(CellAt(observer.position));
            var cameraChunks = GetZoomedOutCameraChunks(chunks);
            if (!force && center == lastChunk && cameraChunks.Equals(lastCameraChunks)) return;
            lastChunk = center;
            lastCameraChunks = cameraChunks;
            var wanted = new HashSet<Vector2Int>();
            int size = generation.chunkSize;
            var empty = new TileBase[size * size];
            for (int y = -generation.viewRadius; y <= generation.viewRadius; y++)
                for (int x = -generation.viewRadius; x <= generation.viewRadius; x++) wanted.Add(center + new Vector2Int(x, y));
            foreach (var chunk in cameraChunks.allPositionsWithin) wanted.Add(chunk);
            foreach (var chunk in visible)
                if (!wanted.Contains(chunk))
                {
                    var area = new BoundsInt(chunk.x * size, chunk.y * size, 0, size, size, 1);
                    tilemap.SetTilesBlock(area, empty);
                    background.SetTilesBlock(area, empty);
                    chunks.Unload(chunk);
                }
            foreach (var chunk in wanted)
            {
                if (!force && visible.Contains(chunk)) continue;
                var tiles = new TileChangeData[size * size];
                var walls = new TileChangeData[size * size];
                var origin = chunk * size;
                for (int x = 0; x < size; x++)
                {
                    int surface = Generator.Surface(origin.x + x);
                    for (int y = 0; y < size; y++)
                    {
                        int index = y * size + x;
                        var cell = origin + new Vector2Int(x, y);
                        int depth = surface - cell.y;
                        var block = Get(cell);
                        var wall = Bounds.Contains(cell) ? Generator.Background(depth) : null;
                        TileBase tile = block != null ? block.Tile : null;
                        TileBase wallTile = wall != null ? wall.Tile : null;
                        tiles[index] = new TileChangeData((Vector3Int)cell, tile,
                            tile != null ? ForegroundColor(depth) : Color.white, TileTransform(tile));
                        walls[index] = new TileChangeData((Vector3Int)cell, wallTile,
                            WallColor(depth), TileTransform(wallTile));
                    }
                }
                tilemap.SetTiles(tiles, true);
                background.SetTiles(walls, true);
            }
            visible.Clear(); foreach (var chunk in wanted) visible.Add(chunk);
        }

        private RectInt GetZoomedOutCameraChunks(ChunkBlockStore chunks)
        {
            if (view == null || cameraFollow == null || cameraFollow.Zoom >= 1f) return default;
            float halfHeight = view.orthographicSize;
            var halfSize = new Vector3(halfHeight * view.aspect, halfHeight);
            var position = view.transform.position;
            // Include the tiles along the viewport edge when the camera snaps to the pixel grid.
            var min = Vector2Int.Max(CellAt(position - halfSize) - Vector2Int.one, bounds.min);
            var max = Vector2Int.Min(CellAt(position + halfSize) + Vector2Int.one, bounds.max - Vector2Int.one);
            if (min.x > max.x || min.y > max.y) return default;

            var first = chunks.ChunkAt(min);
            var last = chunks.ChunkAt(max);
            return new RectInt(first, last - first + Vector2Int.one);
        }

        private static Matrix4x4 TileTransform(TileBase tile) => tile is Tile simple ? simple.transform : Matrix4x4.identity;
        public bool KnowsId(string id) => string.IsNullOrEmpty(id) || definitions.ContainsKey(id);

        public Vector2Int CellAt(Vector3 position) => (Vector2Int)tilemap.WorldToCell(position);
        public Vector3 Center(Vector2Int cell) => tilemap.GetCellCenterWorld((Vector3Int)cell);
        public BlockData Get(Vector2Int cell) => Store != null && Store.Get(cell) is string id
            && definitions.TryGetValue(id, out var data) ? data : null;
        public bool Set(Vector2Int cell, BlockData block)
        {
            if (block != null && (!definitions.TryGetValue(block.Id, out var known) || known != block)) return false;
            return Store != null && Store.Set(cell, block != null ? block.Id : null);
        }

        private void RenderCell(Vector2Int cell, string id)
        {
            surveyDirty = true;
            TerrainRevision++;
            if (Store is not ChunkBlockStore chunks || visible.Contains(chunks.ChunkAt(cell)))
            {
                var position = (Vector3Int)cell;
                var block = id != null && definitions.TryGetValue(id, out var data) ? data : null;
                Tile tile = block != null ? block.Tile : null;
                tilemap.SetTile(new TileChangeData(position, tile, ForegroundColor(DepthAt(cell)), TileTransform(tile)), true);
                if (block != null) tilemap.SetColliderType(position,
                    block.Solid ? Tile.ColliderType.Grid : Tile.ColliderType.None);
            }
            CellChanged?.Invoke(cell);
        }
        private void OnDestroy() { if (Store != null) Store.Changed -= RenderCell; }
    }
}
