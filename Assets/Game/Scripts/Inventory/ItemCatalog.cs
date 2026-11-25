using System;
using System.Collections.Generic;
using BrokenHeart.World;
using UnityEngine;

namespace BrokenHeart.Inventory
{
    [CreateAssetMenu(menuName = "Broken Heart/Item Catalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [SerializeField] private ItemData[] items;
        public IReadOnlyList<ItemData> Items => items;
        public ItemData Find(string id)
        {
            foreach (var item in items) if (item.Id == id) return item;
            return null;
        }
        public ItemData ForBlock(BlockData block)
        {
            foreach (var item in items) if (item.PlaceableBlock == block) return item;
            return null;
        }
        public void Validate()
        {
            var ids = new HashSet<string>();
            var blocks = new HashSet<BlockData>();
            foreach (var item in items)
                if (item == null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) || item.Icon == null ||
                    (item.PlaceableBlock != null && !blocks.Add(item.PlaceableBlock)))
                    throw new InvalidOperationException("Item catalog requires unique IDs, icons and unique block mappings.");
        }
    }
}
