using System;
using BrokenHeart.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace BrokenHeart.Inventory
{
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInventory : MonoBehaviour
    {
        [SerializeField] private ItemCatalog catalog;
        [SerializeField] private int capacity = 36;
        [SerializeField] private int hotbarSize = 9;
        [SerializeField] private ItemData[] starterItems;
        [SerializeField] private int starterCount = 10;
        public InventoryModel Model { get; private set; }
        public PlayerProgress Progress { get; } = new();
        public ItemCatalog Catalog => catalog;
        public int HotbarSize => Mathf.Min(hotbarSize, capacity);
        public int SelectedIndex { get; private set; }
        public ItemStack Selected => Model[SelectedIndex];
        public bool IsOpen { get; private set; }
        public bool IsMapOpen { get; private set; }
        public int BaseCapacity => capacity;
        public const int BackpackCount = 4;
        public const int EquipmentCount = 6 + BackpackCount;
        public const int SlotsPerBackpack = 9;
        private ItemStack[] equipment = new ItemStack[EquipmentCount];
        private Light2D carriedLight;
        public event Action<ItemData, int> Acquired;
        public event Action ContentsChanged;
        private static readonly Key[] Keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
        private void Awake()
        {
            catalog.Validate();
            Model = new InventoryModel(capacity);
            Model.Changed += () => ContentsChanged?.Invoke();
            foreach (var item in starterItems) Model.Add(item, starterCount);
        }
        private void Update()
        {
            if (Time.timeScale == 0) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.eKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame) SetOpen(!IsOpen);
                if (keyboard.mKey.wasPressedThisFrame) SetMapOpen(!IsMapOpen);
                if (keyboard.escapeKey.wasPressedThisFrame) { IsOpen = false; IsMapOpen = false; }
                for (int i = 0; i < HotbarSize && i < Keys.Length; i++)
                    if (keyboard[Keys[i]].wasPressedThisFrame) Select(i);
            }
            if (IsOpen || IsMapOpen || Mouse.current is not Mouse mouse) return;
            float wheel = mouse.scroll.ReadValue().y;
            if (wheel == 0) return;

            if (keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
            {
                var view = Camera.main;
                if (view != null && view.TryGetComponent<CameraFollow>(out var follow)) follow.StepZoom(wheel);
                return;
            }
            if (HotbarSize == 0) return;
            int direction = wheel > 0 ? -1 : 1;
            Select((SelectedIndex + direction + HotbarSize) % HotbarSize);
        }
        public void SetOpen(bool open)
        {
            IsOpen = open;
            if (open) IsMapOpen = false;
        }
        public void SetMapOpen(bool open)
        {
            IsMapOpen = open;
            if (open) IsOpen = false;
        }
        public void Select(int index) { if (index >= 0 && index < HotbarSize) SelectedIndex = index; }
        public int Collect(ItemData item, int amount)
        {
            int remaining = Model.Add(item, amount);
            if (remaining < amount) Acquired?.Invoke(item, amount - remaining);
            return remaining;
        }

        public bool QuickTransfer(int index)
        {
            if (index < 0 || index >= Model.Capacity || Model[index].Empty) return false;
            var slot = Model[index].Item.EquipmentSlot;
            if (IsOpen && slot != EquipmentSlot.None)
            {
                for (int i = 0; i < EquipmentCount; i++)
                    if (equipment[i].Empty && SlotType(i) == slot && Equip(index, i)) return true;
            }
            return index < HotbarSize
                ? Model.TransferStackWithin(index, HotbarSize, Model.Capacity) > 0
                : Model.TransferStackWithin(index, 0, HotbarSize) > 0;
        }

        public bool QuickUnequip(int index)
        {
            if (index < 0 || index >= EquipmentCount || equipment[index].Empty) return false;
            var next = Model.Copy();
            return next.Add(equipment[index].Item, 1) == 0 && ChangeEquipment(next, index, default);
        }

        public static EquipmentSlot SlotType(int index) =>
            (EquipmentSlot)Mathf.Min(index + 1, (int)EquipmentSlot.Backpack);

        public ItemStack Equipped(int index) => equipment[index];
        public bool HasItem(ItemData item)
        {
            if (item == null) return false;
            if (Model.Count(item) > 0) return true;
            foreach (var stack in equipment)
                if (stack.Item == item) return true;
            return false;
        }
        public float DamageTakenFactor
        {
            get
            {
                float protection = 0;
                foreach (var stack in equipment)
                    if (!stack.Empty) protection += stack.Item.Protection;
                return 1 - Mathf.Clamp(protection, 0, 0.75f);
            }
        }

        public int CapacityWith(ItemStack[] equipped)
        {
            int size = capacity;
            for (int i = EquipmentCount - BackpackCount; i < EquipmentCount; i++)
                if (!equipped[i].Empty) size += SlotsPerBackpack;
            return size;
        }

        public bool Equip(int bagIndex, int gearIndex)
        {
            if (!ValidMove(bagIndex, gearIndex)) return false;
            var source = Model[bagIndex];
            var previous = equipment[gearIndex];
            if (source.Empty || source.Item.EquipmentSlot != SlotType(gearIndex) || previous.Item == source.Item)
                return false;
            var next = Model.Copy();
            next.RemoveAt(bagIndex, 1);
            if (!previous.Empty)
            {
                if (next[bagIndex].Empty) next.SetSlot(bagIndex, previous);
                else if (next.Add(previous.Item, 1) != 0) return false;
            }
            return ChangeEquipment(next, gearIndex, new ItemStack(source.Item, 1));
        }

        public bool Unequip(int gearIndex, int bagIndex)
        {
            if (!ValidMove(bagIndex, gearIndex) || equipment[gearIndex].Empty) return false;
            var source = equipment[gearIndex];
            var target = Model[bagIndex];
            var next = Model.Copy();
            ItemStack replacement = default;
            if (target.Empty || target.Item == source.Item)
            {
                if (target.Count >= source.Item.MaxStack) return false;
                next.SetSlot(bagIndex, new ItemStack(source.Item, target.Count + 1));
            }
            else
            {
                if (target.Count != 1 || target.Item.EquipmentSlot != SlotType(gearIndex)) return false;
                replacement = target;
                next.SetSlot(bagIndex, source);
            }
            return ChangeEquipment(next, gearIndex, replacement);
        }

        public void MoveEquipped(int from, int to)
        {
            if (from < 0 || from >= EquipmentCount || to < 0 || to >= EquipmentCount ||
                from == to || SlotType(from) != SlotType(to)) return;
            (equipment[from], equipment[to]) = (equipment[to], equipment[from]);
        }

        private bool ValidMove(int bagIndex, int gearIndex) =>
            bagIndex >= 0 && bagIndex < Model.Capacity && gearIndex >= 0 && gearIndex < EquipmentCount;

        private bool ChangeEquipment(InventoryModel next, int index, ItemStack stack)
        {
            var equipped = (ItemStack[])equipment.Clone();
            equipped[index] = stack;
            if (!next.TryResize(CapacityWith(equipped))) return false;
            ApplyLoadout(next, equipped);
            return true;
        }

        internal void RestoreLoadout(ItemStack[] slots, ItemStack[] equipped)
        {
            var next = new InventoryModel(CapacityWith(equipped));
            next.Restore(slots);
            ApplyLoadout(next, equipped);
        }

        private void ApplyLoadout(InventoryModel next, ItemStack[] equipped)
        {
            equipment = equipped;
            var lamp = equipment[(int)EquipmentSlot.Lamp - 1];
            if (!lamp.Empty && carriedLight == null)
            {
                var lightObject = new GameObject("Carried Lamp");
                lightObject.transform.SetParent(transform, false);
                lightObject.transform.localPosition = new Vector3(0, 0.35f, 0);
                carriedLight = lightObject.AddComponent<Light2D>();
                carriedLight.lightType = Light2D.LightType.Point;
                carriedLight.pointLightOuterRadius = 5.5f;
                carriedLight.pointLightInnerRadius = 1.2f;
                carriedLight.intensity = 0.95f;
            }
            if (carriedLight != null)
            {
                carriedLight.enabled = !lamp.Empty;
                if (!lamp.Empty) carriedLight.color = lamp.Item.Device != null ? lamp.Item.Device.color : new Color(1, 0.8f, 0.5f);
            }
            Model.ApplyLoadout(next);
        }
    }
}


