using System.Collections.Generic;
using BrokenHeart.Inventory;
using BrokenHeart.Systems.Energy;
using BrokenHeart.World;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

namespace BrokenHeart.Crafting
{
    public sealed class WorkstationSystem : MonoBehaviour
    {
        public const int MaxStations = 512;
        [SerializeField] private BlockWorld world;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private DropSpawner drops;
        [SerializeField] private EnergyNetwork energy;
        private readonly Dictionary<Vector2Int, Workstation> stations = new();
        private readonly Dictionary<Vector2Int, Workstation> occupied = new();
        private Workstation opened;
        private Collider2D playerShape;
        private SpriteRenderer preview;
        private Material previewMaterial;
        private const float Reach = 5;
        public IEnumerable<Workstation> Stations => stations.Values;
        public Workstation OpenStation => opened != null && inventory.IsOpen && IsNear(opened) ? opened : null;

        private void Awake()
        {
            playerShape = inventory.GetComponent<Collider2D>();
            world.CellChanged += OnCellChanged;
        }

        private void OnDestroy()
        {
            world.CellChanged -= OnCellChanged;
            if (previewMaterial != null) Destroy(previewMaterial);
        }

        private void Update()
        {
            if (opened != null && OpenStation == null) Close(inventory.IsOpen);
            Tick(Time.deltaTime);
        }

        public void Tick(float seconds)
        {
            if (seconds <= 0) return;
            foreach (var station in stations.Values)
            {
                if (station.Kind != StationKind.Furnace) continue;
                station.Tick(seconds);
                station.Glow.enabled = station.BurnRemaining > 0;
            }
        }

        public Workstation At(Vector2Int cell) => occupied.TryGetValue(cell, out var station) ? station : null;

        public bool CanPlace(ItemData item, Vector2Int anchor)
        {
            if (item == null || item.Station == StationKind.None || stations.Count >= MaxStations || !IsNear(anchor))
                return false;
            var bounds = playerShape.bounds;
            foreach (var offset in Workstation.Footprint(item.Station == StationKind.Furnace))
            {
                var cell = anchor + offset;
                if (!world.Bounds.Contains(cell) || At(cell) != null || energy?.At(cell) != null || world.Get(cell) != null ||
                    new Bounds(world.Center(cell), new Vector3(1, 1, 2)).Intersects(bounds))
                    return false;
                if (offset.y == 0 && world.Get(cell + Vector2Int.down)?.Solid != true) return false;
            }
            return true;
        }

        public Workstation Place(ItemData item, Vector2Int cell) =>
            CanPlace(item, cell) ? Spawn(item, cell, item.Station == StationKind.Furnace) : null;

        internal Workstation Spawn(ItemData item, Vector2Int cell, bool fullSizeFurnace)
        {
            var station = new Workstation(item, cell, fullSizeFurnace);
            var go = new GameObject(item.DisplayName, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sharedMaterial = world.Map.GetComponent<TilemapRenderer>().sharedMaterial;
            SetVisual(renderer, item, cell, fullSizeFurnace);
            renderer.sortingOrder = 4;
            if (station.Kind == StationKind.Furnace)
            {
                var glow = new GameObject("Furnace Light");
                glow.transform.SetParent(transform, false);
                glow.transform.position = world.Center(cell) + (fullSizeFurnace ? Vector3.right * 0.5f : Vector3.zero);
                // Keep its radius in world units even when the furnace sprite is resized.
                glow.transform.SetParent(go.transform, true);
                station.Glow = glow.AddComponent<Light2D>();
                station.Glow.lightType = Light2D.LightType.Point;
                station.Glow.color = new Color(1, 0.54f, 0.23f);
                station.Glow.intensity = 0.54f;
                station.Glow.pointLightOuterRadius = 4;
                station.Glow.enabled = false;
            }
            station.Visual = go;
            stations.Add(cell, station);
            foreach (var offset in Workstation.Footprint(fullSizeFurnace)) occupied.Add(cell + offset, station);
            return station;
        }

        private void SetVisual(SpriteRenderer renderer, ItemData item, Vector2Int anchor, bool fullSizeFurnace)
        {
            renderer.sprite = item.Icon;
            var spriteSize = item.Icon.rect.size / item.Icon.pixelsPerUnit;
            Vector3 position = world.Center(anchor);
            Vector2 size = Vector2.one;
            if (fullSizeFurnace)
            {
                size = new Vector2(3, 2);
                position += new Vector3(1, 0.5f, 0);
            }
            else if (item.Station == StationKind.Furnace)
            {
                size.y = spriteSize.y / spriteSize.x;
                position.y += (size.y - 1) * 0.5f;
            }
            renderer.transform.position = position;
            renderer.transform.localScale = new Vector3(size.x / spriteSize.x, size.y / spriteSize.y, 1);
        }

        public void ShowPreview(ItemData item, Vector2Int cell)
        {
            if (preview == null)
            {
                var go = new GameObject("Workstation Placement Preview", typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                preview = go.GetComponent<SpriteRenderer>();
                previewMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
                preview.sharedMaterial = previewMaterial;
                preview.sortingOrder = 18;
            }
            SetVisual(preview, item, cell, item.Station == StationKind.Furnace);
            preview.color = CanPlace(item, cell) ? new Color(0.62f, 1, 0.76f, 0.60f) : new Color(1, 0.39f, 0.35f, 0.56f);
            preview.enabled = true;
        }

        public void HidePreview()
        {
            if (preview != null) preview.enabled = false;
        }

        public bool Open(Vector2Int cell)
        {
            if (!IsNear(cell) || At(cell) is not Workstation station) return false;
            inventory.SetOpen(true);
            opened = station;
            return true;
        }

        private bool IsNear(Vector2Int cell) => Vector2.Distance(playerShape.bounds.center, world.Center(cell)) <= Reach;
        private bool IsNear(Workstation station)
        {
            foreach (var offset in Workstation.Footprint(station.FullSizeFurnace))
                if (IsNear(station.Cell + offset)) return true;
            return false;
        }

        private void Close(bool closeInventory)
        {
            if (opened == null) return;
            opened = null;
            if (closeInventory) inventory.SetOpen(false);
        }

        public bool Transfer(bool fromStation, int from, bool toStation, int to, int amount)
        {
            var station = OpenStation;
            var storage = station?.Contents;
            if (storage == null || (!fromStation && !toStation)) return false;
            var source = fromStation ? storage : inventory.Model;
            var destination = toStation ? storage : inventory.Model;
            if (from < 0 || from >= source.Capacity || to < 0 || to >= destination.Capacity) return false;
            var stack = source[from];
            if (stack.Empty || toStation && !Workstation.Accepts(station.Kind, to, stack.Item)) return false;
            var replaced = destination[to].Item;
            bool canSwap = !fromStation || Workstation.Accepts(station.Kind, from, replaced);
            var oldOre = station.Kind == StationKind.Furnace ? storage[Workstation.OreSlot].Item : null;
            if (!source.MoveTo(from, destination, to, amount, canSwap)) return false;
            station.TransferOreChanged(oldOre);
            return true;
        }

        public int QuickTransfer(bool fromStation, int index)
        {
            var station = OpenStation;
            if (station?.Contents == null) return 0;
            var storage = station.Contents;
            if (station.Kind == StationKind.Chest)
                return fromStation ? storage.TransferStackTo(index, inventory.Model) :
                    inventory.Model.TransferStackTo(index, storage);

            if (fromStation)
            {
                var oldOre = storage[Workstation.OreSlot].Item;
                int moved = storage.TransferStackTo(index, inventory.Model);
                station.TransferOreChanged(oldOre);
                return moved;
            }
            if (index < 0 || index >= inventory.Model.Capacity) return 0;
            var stack = inventory.Model[index];
            for (int i = Workstation.OreSlot; i <= Workstation.FuelSlot; i++)
            {
                var target = storage[i];
                if (Workstation.Accepts(station.Kind, i, stack.Item) && (target.Empty || target.Item == stack.Item) &&
                    Transfer(false, index, true, i, int.MaxValue))
                    return stack.Count - inventory.Model[index].Count;
            }
            return 0;
        }

        public int TransferMatchingChestItems(bool fromChest)
        {
            if (OpenStation is not { Kind: StationKind.Chest } chest) return 0;
            return fromChest ? chest.Contents.TransferMatchingTo(inventory.Model) :
                inventory.Model.TransferMatchingTo(chest.Contents);
        }

        public bool Remove(Workstation station, bool dropItems)
        {
            if (station == null || At(station.Cell) != station || !stations.Remove(station.Cell)) return false;
            foreach (var offset in Workstation.Footprint(station.FullSizeFurnace)) occupied.Remove(station.Cell + offset);
            if (opened == station) Close(true);
            if (dropItems)
            {
                var position = world.Center(station.Cell);
                drops.Spawn(station.Item, 1, position);
                var contents = station.Contents;
                for (int i = 0; contents != null && i < contents.Capacity; i++)
                {
                    var stack = contents[i];
                    if (!stack.Empty) drops.Spawn(stack.Item, stack.Count, position);
                }
            }
            station.Visual.SetActive(false);
            Destroy(station.Visual);
            return true;
        }

        private void OnCellChanged(Vector2Int cell)
        {
            var inside = At(cell);
            var above = At(cell + Vector2Int.up);
            if (inside == null && above == null) return;
            var block = world.Get(cell);
            if (inside != null && block != null) Remove(inside, true);
            if (above != null && above.Cell.y == cell.y + 1 && block?.Solid != true) Remove(above, true);
        }

        internal void Clear()
        {
            Close(true);
            HidePreview();
            foreach (var station in stations.Values)
            {
                station.Visual.SetActive(false);
                Destroy(station.Visual);
            }
            stations.Clear();
            occupied.Clear();
        }
    }
}
