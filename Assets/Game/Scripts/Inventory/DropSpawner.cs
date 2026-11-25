using BrokenHeart.World;
using Unity.Profiling;
using UnityEngine;

namespace BrokenHeart.Inventory
{
    public sealed class DropSpawner : MonoBehaviour
    {
        private static readonly ProfilerMarker SpawnDrop = new("Broken Heart.DropSpawn");
        [SerializeField] private ItemDrop prefab;
        private BlockWorld world;
        private void Awake() => world = GetComponent<BlockWorld>();

        public ItemDrop Spawn(ItemData item, int count, Vector3 position, Collider2D owner = null)
        {
            if (item == null || count <= 0) return null;
            using var measure = SpawnDrop.Auto();
            var drop = Instantiate(prefab, position, Quaternion.identity);
            drop.Initialize(item, count, owner, world);
            return drop;
        }
    }
}
