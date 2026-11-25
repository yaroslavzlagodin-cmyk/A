using UnityEngine;

namespace BrokenHeart.Inventory
{
    [RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
    public sealed class ItemDrop : MonoBehaviour
    {
        public ItemData Item { get; private set; }
        public int Count { get; private set; }
        private float availableAt;
        public void Initialize(ItemData item, int amount, Collider2D owner = null)
        {
            Item = item;
            Count = amount;
            GetComponent<SpriteRenderer>().sprite = item.Icon;
            availableAt = Time.time + 0.3f;
            if (owner != null)
                foreach (var shape in GetComponents<Collider2D>())
                    if (!shape.isTrigger) Physics2D.IgnoreCollision(shape, owner);
        }
        private void OnTriggerStay2D(Collider2D other)
        {
            if (Time.time < availableAt || Count <= 0) return;
            var inventory = other.GetComponent<PlayerInventory>();
            if (inventory != null) TryCollect(inventory);
        }
        public int TryCollect(PlayerInventory inventory)
        {
            if (Count <= 0 || Item == null) return 0;
            int before = Count;
            Count = inventory.Collect(Item, Count);
            if (Count == 0) { gameObject.SetActive(false); Destroy(gameObject); }
            return before - Count;
        }
    }
}
