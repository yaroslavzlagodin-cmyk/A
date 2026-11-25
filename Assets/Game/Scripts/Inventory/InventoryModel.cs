using System;
using System.Collections.Generic;

namespace BrokenHeart.Inventory
{
    public readonly struct ItemStack
    {
        public readonly ItemData Item;
        public readonly int Count;
        public bool Empty => Item == null || Count <= 0;
        public ItemStack(ItemData item, int count) { Item = count > 0 ? item : null; Count = item != null ? Math.Max(0, count) : 0; }
    }

    // Owns all stack mutations. Views receive immutable snapshots, not the backing array.
    public sealed class InventoryModel
    {
        private ItemStack[] slots;
        public event Action Changed;
        public int Capacity => slots.Length;
        public ItemStack this[int index] => slots[index];
        public InventoryModel(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            slots = new ItemStack[capacity];
        }
        public int Count(ItemData item)
        {
            int count = 0;
            foreach (var slot in slots) if (slot.Item == item) count += slot.Count;
            return count;
        }
        public int SpaceFor(ItemData item)
        {
            if (item == null) return 0;
            int room = 0;
            foreach (var slot in slots)
                if (slot.Empty) room += item.MaxStack;
                else if (slot.Item == item) room += item.MaxStack - slot.Count;
            return room;
        }
        // Returns the amount left over; callers must retain it in the world.
        public int Add(ItemData item, int amount)
        {
            if (item == null || amount < 0) throw new ArgumentException("Invalid item or amount.");
            int remaining = Insert(item, amount, 0, Capacity);
            if (remaining != amount) Changed?.Invoke();
            return remaining;
        }

        private int Insert(ItemData item, int amount, int firstSlot, int endSlot)
        {
            int remaining = amount;
            for (int pass = 0; pass < 2; pass++)
                for (int i = firstSlot; i < endSlot && remaining > 0; i++)
                {
                    var slot = slots[i];
                    if (pass == 0 ? slot.Item != item : !slot.Empty) continue;
                    int added = Math.Min(remaining, item.MaxStack - slot.Count);
                    slots[i] = new ItemStack(item, slot.Count + added);
                    remaining -= added;
                }
            return remaining;
        }
        public bool RemoveAt(int index, int amount)
        {
            if (index < 0 || index >= Capacity || amount <= 0 || slots[index].Count < amount) return false;
            slots[index] = new ItemStack(slots[index].Item, slots[index].Count - amount);
            Changed?.Invoke();
            return true;
        }
        public bool Remove(ItemData item, int amount)
        {
            if (item == null || amount <= 0 || Count(item) < amount) return false;
            for (int i = 0; i < Capacity && amount > 0; i++)
            {
                if (slots[i].Item != item) continue;
                int take = Math.Min(amount, slots[i].Count);
                slots[i] = new ItemStack(item, slots[i].Count - take);
                amount -= take;
            }
            Changed?.Invoke();
            return true;
        }
        public bool Move(int from, int to, int amount = int.MaxValue) => MoveTo(from, this, to, amount, true);

        internal bool MoveTo(int from, InventoryModel destination, int to, int amount, bool allowSwap)
        {
            if (from < 0 || to < 0 || from >= Capacity || to >= destination.Capacity ||
                (this == destination && from == to) || amount <= 0 || slots[from].Empty) return false;
            var source = slots[from];
            var target = destination.slots[to];
            if (!target.Empty && target.Item != source.Item)
            {
                if (!allowSwap || amount < source.Count) return false;
                destination.slots[to] = source;
                slots[from] = target;
            }
            else
            {
                int count = Math.Min(Math.Min(amount, source.Count), source.Item.MaxStack - target.Count);
                if (count == 0) return false;
                destination.slots[to] = new ItemStack(source.Item, target.Count + count);
                slots[from] = new ItemStack(source.Item, source.Count - count);
            }
            Changed?.Invoke();
            if (destination != this) destination.Changed?.Invoke();
            return true;
        }

        public int TransferStackTo(int index, InventoryModel destination)
        {
            if (destination == null || destination == this || index < 0 || index >= Capacity) return 0;
            int moved = TransferIntoFreeSpace(index, destination, 0, destination.Capacity);
            NotifyTransfer(destination, moved);
            return moved;
        }

        public int TransferStackWithin(int index, int firstSlot, int endSlot)
        {
            if (index < 0 || index >= Capacity || firstSlot < 0 || firstSlot >= endSlot || endSlot > Capacity ||
                (index >= firstSlot && index < endSlot)) return 0;
            int moved = TransferIntoFreeSpace(index, this, firstSlot, endSlot);
            if (moved > 0) Changed?.Invoke();
            return moved;
        }

        public int TransferMatchingTo(InventoryModel destination)
        {
            if (destination == null || destination == this) return 0;
            var matches = new HashSet<ItemData>();
            foreach (var stack in destination.slots)
                if (!stack.Empty) matches.Add(stack.Item);
            int moved = 0;
            for (int index = 0; index < Capacity; index++)
                if (matches.Contains(slots[index].Item))
                    moved += TransferIntoFreeSpace(index, destination, 0, destination.Capacity);
            NotifyTransfer(destination, moved);
            return moved;
        }

        private int TransferIntoFreeSpace(int index, InventoryModel destination, int firstSlot, int endSlot)
        {
            var stack = slots[index];
            if (stack.Empty) return 0;
            int remaining = destination.Insert(stack.Item, stack.Count, firstSlot, endSlot);
            slots[index] = new ItemStack(stack.Item, remaining);
            return stack.Count - remaining;
        }

        private void NotifyTransfer(InventoryModel destination, int moved)
        {
            if (moved == 0) return;
            Changed?.Invoke();
            destination.Changed?.Invoke();
        }

        public void SortFrom(int firstSlot)
        {
            if (firstSlot < 0 || firstSlot >= Capacity) return;
            var items = new List<ItemStack>();
            for (int i = firstSlot; i < Capacity; i++)
                if (!slots[i].Empty) items.Add(slots[i]);
            if (items.Count == 0) return;
            items.Sort(CompareItems);
            Array.Clear(slots, firstSlot, Capacity - firstSlot);
            int next = firstSlot;
            foreach (var stack in items)
            {
                int remaining = stack.Count;
                if (next > firstSlot && slots[next - 1].Item == stack.Item)
                {
                    var last = slots[next - 1];
                    int merged = Math.Min(remaining, stack.Item.MaxStack - last.Count);
                    slots[next - 1] = new ItemStack(last.Item, last.Count + merged);
                    remaining -= merged;
                }
                if (remaining > 0) slots[next++] = new ItemStack(stack.Item, remaining);
            }
            Changed?.Invoke();
        }

        private static int CompareItems(ItemStack left, ItemStack right)
        {
            int category = left.Item.Category.CompareTo(right.Item.Category);
            if (category != 0) return category;
            int name = StringComparer.OrdinalIgnoreCase.Compare(left.Item.DisplayName, right.Item.DisplayName);
            return name != 0 ? name : StringComparer.Ordinal.Compare(left.Item.Id, right.Item.Id);
        }

        public InventoryModel Copy()
        {
            var copy = new InventoryModel(Capacity);
            Array.Copy(slots, copy.slots, Capacity);
            return copy;
        }
        public void Restore(ItemStack[] restored)
        {
            if (restored == null || restored.Length != Capacity) throw new ArgumentException("Invalid slot count.");
            foreach (var slot in restored)
                if (slot.Count < 0 || (!slot.Empty && slot.Count > slot.Item.MaxStack)) throw new ArgumentException("Invalid stack.");
            Array.Copy(restored, slots, Capacity);
            Changed?.Invoke();
        }
        public void ReplaceWith(InventoryModel other)
        {
            if (other.Capacity != Capacity) throw new ArgumentException("Inventory capacity differs.");
            Array.Copy(other.slots, slots, Capacity);
            Changed?.Invoke();
        }

        internal void SetSlot(int index, ItemStack stack) => slots[index] = stack;
        internal bool TryResize(int capacity)
        {
            for (int i = capacity; i < Capacity; i++)
                if (!slots[i].Empty) return false;
            Array.Resize(ref slots, capacity);
            return true;
        }
        internal void ApplyLoadout(InventoryModel other)
        {
            slots = (ItemStack[])other.slots.Clone();
            Changed?.Invoke();
        }
    }
}
