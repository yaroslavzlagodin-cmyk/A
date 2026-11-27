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
        [SerializeField] private KnowledgeBook book;
        public IReadOnlyList<KnowledgeEntry> Journal => book.entries;
        public int TrailStep { get; private set; }
        public Vector2Int GiantTrace { get; private set; }
        public bool HasKnowledge(string id) => !string.IsNullOrEmpty(id) && unlocked.Contains(id);
        public bool MeetsKnowledge(RecipeData recipe) => recipe != null &&
            (string.IsNullOrEmpty(recipe.RequiredKnowledge) || HasKnowledge(recipe.RequiredKnowledge));

        // Discoveries belong to the hero. The trail must be followed separately in each world.
        internal void Record(Discovery action, Vector2Int cell)
        {
            foreach (var entry in book.entries)
            {
                if (entry.id == "lore.trail" && TrailStep < 3) continue;
                if (entry.condition != action || (!string.IsNullOrEmpty(entry.prerequisite) && !HasKnowledge(entry.prerequisite))) continue;
                if (unlocked.Add(entry.id))
                {
                    inventory.Progress.Gain(entry.experience);
                    foreach (var recipe in recipes)
                        if (recipe != null && recipe.RequiredKnowledge == entry.id) unlocked.Add(recipe.Id);
                    RecipeRevision++;
                    Notify(entry.title);
                }
            }
            if (TrailStep == 0 && action == Discovery.Observe) TrailStep = 1;
            else if (TrailStep == 1 && action == Discovery.Examine) TrailStep = 2;
            else if (TrailStep == 3 && action == Discovery.Defeat)
            {
                TrailStep = 4;
                GiantTrace = cell;
                Notify("The giant left a trail.");
            }
        }

        internal void RestoreTrail(int step, Vector2Int trace)
        {
            TrailStep = step;
            GiantTrace = step == 4 ? trace : default;
        }

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
        public bool Knows(RecipeData recipe) => MeetsKnowledge(recipe) && unlocked.Contains(recipe.Id);
        public bool KnowsMaterial(ItemData item) => item != null && knownMaterials.Contains(item.Id);
        public bool CanView(RecipeData recipe) => MeetsKnowledge(recipe) && (Knows(recipe) ||
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
            if (TrailStep == 2 && recipe.RequiredKnowledge == "lore.darkness") TrailStep = 3;
            LearnFromMaterial(recipe.Output);
        }

        private void LearnFromMaterial(ItemData item)
        {
            foreach (var recipe in recipes)
            {
                if (!MeetsKnowledge(recipe) || recipe.Output == null || recipe.Ingredients == null || Knows(recipe)) continue;
                if (Array.Exists(recipe.Ingredients, ingredient => ingredient.item == item) &&
                    recipe.AfterSpendingMaterials(inventory.Model) != null)
                    Learn(recipe);
            }
        }

        private bool HasUnlearnedRecipeFor(ItemData item) =>
            Array.Exists(recipes, recipe => MeetsKnowledge(recipe) && recipe.Output == item && !Knows(recipe));

        public bool CanStudy(ItemData item) =>
            item != null && studyItems.Contains(item.Id) && inventory.HasItem(item) && HasUnlearnedRecipeFor(item);

        public bool Study(ItemData item)
        {
            if (!CanStudy(item)) return false;
            studyItems.Remove(item.Id);
            foreach (var recipe in recipes)
                if (MeetsKnowledge(recipe) && recipe.Output == item && !Knows(recipe)) Learn(recipe);
            return true;
        }

        private void Learn(RecipeData recipe)
        {
            if (!MeetsKnowledge(recipe) || !unlocked.Add(recipe.Id)) return;
            RecipeRevision++;
            inventory.Progress.Gain(10);
            Notify("Memory restored: " + recipe.Output.DisplayName);
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
