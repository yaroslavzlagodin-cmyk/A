using BrokenHeart.World;
using UnityEngine;

namespace BrokenHeart.Inventory
{
    [DefaultExecutionOrder(10)]
    [RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
    public sealed class ItemDrop : MonoBehaviour
    {
        public ItemData Item { get; private set; }
        public int Count { get; private set; }
        private float availableAt;
        private Rigidbody2D body;
        private BlockWorld world;
        private Collider2D ownerShape;
        private Collider2D[] shapes;

        public void Initialize(ItemData item, int amount, Collider2D owner = null, BlockWorld terrain = null)
        {
            Item = item;
            Count = amount;
            world = terrain;
            body = GetComponent<Rigidbody2D>();
            body.linearVelocity = new Vector2(0, 1.5f);
            GetComponent<SpriteRenderer>().sprite = item.Icon;
            availableAt = Time.time + 0.3f;
            if (owner != null)
            {
                ownerShape = owner;
                shapes = GetComponents<Collider2D>();
                IgnoreOwnerCollision();
            }
        }
        private void FixedUpdate()
        {
            if (world == null || body == null) return;
            // Keep the drop saveable while it has no terrain collider to land on.
            bool loaded = world.HasLoadedTerrain(body.position);
            if (body.simulated == loaded) return;
            body.simulated = loaded;
            if (loaded) IgnoreOwnerCollision();
        }

        private void IgnoreOwnerCollision()
        {
            if (ownerShape == null || !ownerShape.isActiveAndEnabled) return;
            foreach (var shape in shapes)
                if (!shape.isTrigger) Physics2D.IgnoreCollision(shape, ownerShape);
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
