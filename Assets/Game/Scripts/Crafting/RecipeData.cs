using System;
using BrokenHeart.Inventory;
using UnityEngine;

namespace BrokenHeart.Crafting
{
    public enum RecipeCategory { Tools, Blocks, Magic, Mechanisms, Armor, Backpacks }
    [Serializable]
    public struct Ingredient { public ItemData item; [Min(1)] public int count; }
    [CreateAssetMenu(menuName = "Broken Heart/Recipe")]
    public sealed class RecipeData : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private RecipeCategory category;
        [SerializeField] private Ingredient[] ingredients;
        [SerializeField] private ItemData output;
        [SerializeField, Min(1)] private int outputCount = 1;
        [SerializeField] private bool byHand;
        public string Id => id;
        public RecipeCategory Category => category;
        public Ingredient[] Ingredients => ingredients;
        public ItemData Output => output;
        public int OutputCount => outputCount;
        public bool ByHand => byHand;

        internal InventoryModel AfterSpendingMaterials(InventoryModel inventory)
        {
            if (Ingredients == null || Ingredients.Length == 0) return null;
            var remaining = inventory.Copy();
            foreach (var cost in Ingredients)
                if (cost.item == null || cost.count <= 0 || !remaining.Remove(cost.item, cost.count)) return null;
            return remaining;
        }
    }
}
