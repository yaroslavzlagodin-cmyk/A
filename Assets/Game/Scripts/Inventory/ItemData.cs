using BrokenHeart.World;
using UnityEngine;
using BrokenHeart.Systems.Energy;
using BrokenHeart.Crafting;

namespace BrokenHeart.Inventory
{
    public enum ItemCategory { Block, Material, Tool, Magic, Mechanism, Equipment, Weapon }
    public enum ToolKind { Any, Pickaxe, Axe, Shovel }
    public enum EquipmentSlot { None, Head, Chest, Legs, Feet, Lamp, Shield, Backpack }

    [CreateAssetMenu(menuName = "Broken Heart/Item")]
    public sealed class ItemData : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField, Min(1)] private int maxStack = 99;
        [SerializeField] private ItemCategory category;
        [SerializeField] private BlockData placeableBlock;
        [SerializeField, Min(1)] private float miningPower = 1;
        [SerializeField] private ToolKind toolKind;
        [SerializeField, Min(0)] private float attackDamage;
        [SerializeField, Min(0)] private float attackReach;
        [SerializeField, Min(0)] private float swingInterval;
        [SerializeField] private EnergyDeviceData device;
        [SerializeField] private EquipmentSlot equipmentSlot;
        [SerializeField, Range(0, 0.25f)] private float protection;
        [SerializeField] private StationKind station;
        [SerializeField] private ItemData smeltsTo;
        [SerializeField, Min(0)] private float fuelSeconds;
        [SerializeField, Range(0, 1)] private float sanityProtection;
        public float SanityProtection => sanityProtection;
        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Sprite Icon => icon;
        public int MaxStack => Mathf.Max(1, maxStack);
        public ItemCategory Category => category;
        public BlockData PlaceableBlock => placeableBlock;
        public float MiningPower => miningPower;
        public float AttackDamage => attackDamage;
        public float AttackReach => attackReach;
        public float SwingInterval => swingInterval;
        public EnergyDeviceData Device => device;
        public EquipmentSlot EquipmentSlot => equipmentSlot;
        public float Protection => protection;
        public StationKind Station => station;
        public ItemData SmeltsTo => smeltsTo;
        public float FuelSeconds => fuelSeconds;

        public float MiningPowerFor(BlockData target) => toolKind switch
        {
            ToolKind.Pickaxe when target.Id is "stone" or "copper" or "iron" or "crystal" or
                "ruin" or "stone_bricks" or "mossy_rubble" or "mossy_stone_bricks" => miningPower,
            ToolKind.Axe when target.Id is "wood" or "planks" or "leaves" => miningPower,
            ToolKind.Shovel when target.Id is "dirt" or "grass" or "grass_cover" => miningPower,
            ToolKind.Any => miningPower,
            _ => 1
        };
    }
}
