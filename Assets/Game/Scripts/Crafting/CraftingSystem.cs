using System.Collections.Generic;
using BrokenHeart.Inventory;
using BrokenHeart.Systems;
using UnityEngine;

namespace BrokenHeart.Crafting
{
    public sealed class CraftingSystem : MonoBehaviour
    {
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private MemorySystem memory;
        [SerializeField] private RecipeData[] recipes;
        [SerializeField] private WorkstationSystem stations;
        public IReadOnlyList<RecipeData> Recipes => recipes;
        public bool AtWorkbench => stations != null && stations.OpenStation?.Kind == StationKind.Workbench;
        public bool AvailableHere(RecipeData recipe) => recipe != null && (recipe.ByHand || AtWorkbench);
        private readonly Dictionary<RecipeData, (bool Allowed, string Reason)> availability = new();
        private long inventoryRevision = -1, recipeRevision = -1, definitionRevision = -1;
        private Workstation cachedStation;

        public bool CanCraft(RecipeData recipe, out string reason)
        {
            if (recipe == null) { reason = "Unknown recipe"; return false; }
            var station = stations?.OpenStation;
            if (inventoryRevision != inventory.Model.Revision || recipeRevision != memory.RecipeRevision ||
                definitionRevision != RecipeData.DefinitionRevision || cachedStation != station)
            {
                availability.Clear();
                inventoryRevision = inventory.Model.Revision;
                recipeRevision = memory.RecipeRevision;
                definitionRevision = RecipeData.DefinitionRevision;
                cachedStation = station;
            }
            if (!availability.TryGetValue(recipe, out var result))
            {
                bool allowed = Prepare(recipe, out _, out reason);
                availability[recipe] = (allowed, reason);
                return allowed;
            }
            reason = result.Reason;
            return result.Allowed;
        }

        public void ListVisible(RecipeCategory? category, List<RecipeData> visible)
        {
            visible.Clear();
            foreach (var recipe in recipes)
                if (AvailableHere(recipe) && memory.CanView(recipe) && recipe.Output != null &&
                    (!category.HasValue || category.Value == recipe.Category)) visible.Add(recipe);
        }

        public bool Craft(RecipeData recipe, out string reason)
        {
            if (!Prepare(recipe, out var next, out reason)) return false;
            inventory.Model.ReplaceWith(next);
            memory.OnCrafted(recipe);
            inventory.Progress.Gain(3);
            return true;
        }
        private bool Prepare(RecipeData recipe, out InventoryModel next, out string reason)
        {
            next = null;
            if (recipe == null || System.Array.IndexOf(recipes, recipe) < 0) { reason = "Unknown recipe"; return false; }
            if (!AvailableHere(recipe)) { reason = "Requires a workbench"; return false; }
            if (!memory.MeetsKnowledge(recipe)) { reason = "Requires field research"; return false; }
            if (!memory.CanView(recipe)) { reason = "Unlearned recipe"; return false; }
            if (memory.HasUnknownMaterials(recipe)) { reason = "Undiscovered materials"; return false; }
            next = recipe.AfterSpendingMaterials(inventory.Model);
            if (next == null) { reason = "Missing materials"; return false; }
            if (recipe.Output == null || recipe.OutputCount <= 0 || next.Add(recipe.Output, recipe.OutputCount) != 0)
            { reason = "Backpack full"; return false; }
            reason = "Craft";
            return true;
        }
    }
}
