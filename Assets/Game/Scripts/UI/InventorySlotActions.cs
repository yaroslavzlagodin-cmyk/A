using BrokenHeart.Inventory;
using BrokenHeart.Crafting;
using UnityEngine;

namespace BrokenHeart.UI
{
    internal enum SlotKind { Bag, Equipment, Station }
    internal readonly struct SlotTarget
    {
        public readonly int Index;
        public readonly SlotKind Kind;
        public bool Equipment => Kind == SlotKind.Equipment;
        public bool Station => Kind == SlotKind.Station;
        public SlotTarget(int index, SlotKind kind = SlotKind.Bag) { Index = index; Kind = kind; }
        public bool Matches(SlotTarget other) => Index == other.Index && Kind == other.Kind;
    }

    internal sealed class InventorySlotActions
    {
        private readonly PlayerInventory inventory;
        private readonly WorkstationSystem stations;

        public InventorySlotActions(PlayerInventory inventory, WorkstationSystem stations)
        {
            this.inventory = inventory;
            this.stations = stations;
        }

        public void Select(SlotTarget slot)
        {
            if (slot.Kind == SlotKind.Bag && slot.Index < inventory.HotbarSize) inventory.Select(slot.Index);
        }

        public void QuickTransfer(SlotTarget target)
        {
            if (target.Equipment) inventory.QuickUnequip(target.Index);
            else if (stations?.OpenStation?.Contents != null) stations.QuickTransfer(target.Station, target.Index);
            else if (!target.Station) inventory.QuickTransfer(target.Index);
        }

        public ItemStack StackAt(SlotTarget target)
        {
            if (target.Equipment) return inventory.Equipped(target.Index);
            var model = target.Station ? stations?.OpenStation?.Contents : inventory.Model;
            return model != null && target.Index < model.Capacity ? model[target.Index] : default;
        }

        public bool MoveStack(SlotTarget source, SlotTarget destination, bool split)
        {
            int amount = split ? Mathf.CeilToInt(StackAt(source).Count / 2f) : int.MaxValue;
            if (source.Station || destination.Station)
                return !source.Equipment && !destination.Equipment && stations != null &&
                    stations.Transfer(source.Station, source.Index, destination.Station, destination.Index, amount);
            if (source.Equipment && destination.Equipment)
                return inventory.MoveEquipped(source.Index, destination.Index);
            if (destination.Equipment) return inventory.Equip(source.Index, destination.Index);
            if (source.Equipment) return inventory.Unequip(source.Index, destination.Index);
            return inventory.Model.Move(source.Index, destination.Index, amount);
        }
    }
}
