using System.Collections.Generic;
using BrokenHeart.Inventory;
using UnityEngine;

namespace BrokenHeart.Crafting
{
    public enum StationKind { None, Workbench, Furnace, Chest }

    public sealed class Workstation
    {
        private static readonly Vector2Int[] SingleCell = { Vector2Int.zero };
        private static readonly Vector2Int[] FurnaceCells =
        {
            Vector2Int.zero, Vector2Int.right, new(2, 0), Vector2Int.up, Vector2Int.one
        };
        public const int OreSlot = 0, FuelSlot = 1, OutputSlot = 2;
        public const float SecondsPerIngot = 3;
        public ItemData Item { get; }
        public StationKind Kind => Item.Station;
        public Vector2Int Cell { get; }
        public bool FullSizeFurnace { get; }
        public InventoryModel Contents { get; }
        public float BurnRemaining { get; private set; }
        public float SmeltSeconds { get; private set; }
        public float Progress => SmeltSeconds / SecondsPerIngot;
        internal GameObject Visual { get; set; }
        internal UnityEngine.Rendering.Universal.Light2D Glow { get; set; }

        internal Workstation(ItemData item, Vector2Int cell, bool fullSizeFurnace)
        {
            Item = item;
            Cell = cell;
            FullSizeFurnace = fullSizeFurnace;
            int capacity = CapacityFor(Kind);
            if (capacity > 0) Contents = new InventoryModel(capacity);
        }

        public static IReadOnlyList<Vector2Int> Footprint(bool fullSizeFurnace) =>
            fullSizeFurnace ? FurnaceCells : SingleCell;

        public static int CapacityFor(StationKind kind) => kind switch
        {
            StationKind.Chest => 18,
            StationKind.Furnace => 3,
            _ => 0
        };

        internal static bool Accepts(StationKind kind, int index, ItemData item) =>
            item != null && (kind == StationKind.Chest || kind == StationKind.Furnace && (index switch
            {
                OreSlot => item.SmeltsTo != null,
                FuelSlot => item.FuelSeconds > 0,
                _ => false
            }));

        internal void TransferOreChanged(ItemData previousOre)
        {
            if (Kind == StationKind.Furnace && Contents[OreSlot].Item != previousOre) SmeltSeconds = 0;
        }

        internal void Restore(ItemStack[] slots, float burn, float smelt)
        {
            Contents?.Restore(slots);
            BurnRemaining = burn;
            SmeltSeconds = smelt;
        }

        internal void Tick(float seconds)
        {
            while (seconds > 0)
            {
                var product = Contents[OreSlot].Item?.SmeltsTo;
                var result = Contents[OutputSlot];
                bool canSmelt = product != null &&
                    (result.Empty || result.Item == product && result.Count < product.MaxStack);
                if (!canSmelt)
                {
                    BurnRemaining = Mathf.Max(0, BurnRemaining - seconds);
                    return;
                }
                if (BurnRemaining <= 0)
                {
                    var fuel = Contents[FuelSlot].Item;
                    if (fuel == null || fuel.FuelSeconds <= 0) return;
                    Contents.SetSlot(FuelSlot, new ItemStack(fuel, Contents[FuelSlot].Count - 1));
                    BurnRemaining = fuel.FuelSeconds;
                }

                float step = Mathf.Min(seconds, Mathf.Min(BurnRemaining, SecondsPerIngot - SmeltSeconds));
                BurnRemaining -= step;
                SmeltSeconds += step;
                seconds -= step;
                if (SmeltSeconds < SecondsPerIngot) continue;
                var ore = Contents[OreSlot];
                Contents.SetSlot(OreSlot, new ItemStack(ore.Item, ore.Count - 1));
                Contents.SetSlot(OutputSlot, new ItemStack(product, result.Count + 1));
                SmeltSeconds = 0;
            }
        }
    }
}
