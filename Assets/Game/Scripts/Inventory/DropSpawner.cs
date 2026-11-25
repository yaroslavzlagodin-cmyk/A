using UnityEngine;

namespace BrokenHeart.Inventory
{
    public sealed class DropSpawner : MonoBehaviour
    {
        [SerializeField] private ItemDrop prefab;
        public ItemDrop Spawn(ItemData item, int count, Vector3 position, Collider2D owner = null)
        {
            if (item == null || count <= 0) return null;
            var drop = Instantiate(prefab, position, Quaternion.identity);
            drop.Initialize(item, count, owner);
            drop.GetComponent<Rigidbody2D>().linearVelocity = new Vector2(0, 1.5f);
            return drop;
        }
    }
}
