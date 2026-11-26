using System;
using System.Collections.Generic;
using BrokenHeart.Inventory;
using BrokenHeart.Crafting;
using UnityEngine;

namespace BrokenHeart.Systems
{
    public sealed class MemorySystem : MonoBehaviour
    {
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private RecipeData[] recipes;
        private readonly HashSet<string> unlocked = new();
        private readonly HashSet<string> studyItems = new();
        private readonly HashSet<string> knownMaterials = new();
        public event Action<string> Remembered;
        public IEnumerable<string> Unlocked => unlocked;
        public IEnumerable<string> StudyItems => studyItems;
        public IEnumerable<string> KnownMaterials => knownMaterials;
        public long RecipeRevision { get; private set; }
        public string Notification { get; private set; }
        private float notificationUntil;
        public string ActiveNotification => Time.time < notificationUntil ? Notification : "";
        private void OnEnable()
        {
            inventory.Acquired += OnPickup;
            inventory.ContentsChanged += ObserveInventory;
            ObserveInventory();
        }
        private void OnDisable()
        {
            inventory.Acquired -= OnPickup;
            inventory.ContentsChanged -= ObserveInventory;
        }
        public bool Knows(RecipeData recipe) => recipe != null && unlocked.Contains(recipe.Id);
        public bool KnowsMaterial(ItemData item) => item != null && knownMaterials.Contains(item.Id);
        public bool CanView(RecipeData recipe) => recipe != null && (Knows(recipe) ||
            recipe.Ingredients != null && Array.Exists(recipe.Ingredients, cost => KnowsMaterial(cost.item)));
        public bool HasUnknownMaterials(RecipeData recipe) => recipe.Ingredients == null ||
            Array.Exists(recipe.Ingredients, cost => !KnowsMaterial(cost.item));

        private void ObserveInventory()
        {
            if (inventory.Model == null) return;
            for (int i = 0; i < inventory.Model.Capacity; i++) Remember(inventory.Model[i]);
            for (int i = 0; i < PlayerInventory.EquipmentCount; i++) Remember(inventory.Equipped(i));
        }

        private void Remember(ItemStack stack)
        {
            if (!stack.Empty && knownMaterials.Add(stack.Item.Id)) RecipeRevision++;
        }

        private void OnPickup(ItemData item, int amount)
        {
            LearnFromMaterial(item);
            if (HasUnlearnedRecipeFor(item)) studyItems.Add(item.Id);
        }

        internal void OnCrafted(RecipeData recipe)
        {
            Learn(recipe);
            LearnFromMaterial(recipe.Output);
        }

        private void LearnFromMaterial(ItemData item)
        {
            foreach (var recipe in recipes)
            {
                if (recipe == null || recipe.Output == null || recipe.Ingredients == null || Knows(recipe)) continue;
                if (Array.Exists(recipe.Ingredients, ingredient => ingredient.item == item) &&
                    recipe.AfterSpendingMaterials(inventory.Model) != null)
                    Learn(recipe);
            }
        }

        private bool HasUnlearnedRecipeFor(ItemData item) =>
            Array.Exists(recipes, recipe => recipe != null && recipe.Output == item && !Knows(recipe));

        public bool CanStudy(ItemData item) =>
            item != null && studyItems.Contains(item.Id) && inventory.HasItem(item) && HasUnlearnedRecipeFor(item);

        public bool Study(ItemData item)
        {
            if (!CanStudy(item)) return false;
            studyItems.Remove(item.Id);
            foreach (var recipe in recipes)
                if (recipe != null && recipe.Output == item && !Knows(recipe)) Learn(recipe);
            return true;
        }

        private void Learn(RecipeData recipe)
        {
            if (!unlocked.Add(recipe.Id)) return;
            RecipeRevision++;
            inventory.Progress.Gain(10);
            Notify("Memory restored: " + recipe.Output.DisplayName);
        }

        public void UnlockMindProtection()
        {
            if (unlocked.Add("memory.mind_protection"))
                Notify("Mind-protection memory restored.");
        }

        public void Restore(IEnumerable<string> ids, IEnumerable<string> pendingStudy = null, IEnumerable<string> materials = null)
        {
            RecipeRevision++;
            unlocked.Clear();
            foreach (string id in ids) unlocked.Add(id);
            knownMaterials.Clear();
            if (materials != null)
                foreach (string id in materials)
                    if (inventory.Catalog.Find(id) != null) knownMaterials.Add(id);
            ObserveInventory();
            studyItems.Clear();
            if (pendingStudy != null)
            {
                foreach (string id in pendingStudy)
                {
                    var item = inventory.Catalog.Find(id);
                    if (item != null && HasUnlearnedRecipeFor(item)) studyItems.Add(id);
                }
            }
            notificationUntil = 0;
        }
        private void Notify(string message)
        {
            Notification = message;
            notificationUntil = Time.time + 7;
            Remembered?.Invoke(message);
        }
    }
}
