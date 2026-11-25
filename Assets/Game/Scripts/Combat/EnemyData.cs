using BrokenHeart.Inventory;
using UnityEngine;
namespace BrokenHeart.Combat
{
    [CreateAssetMenu(menuName="Broken Heart/Enemy")]
    public sealed class EnemyData : ScriptableObject
    {
        public string biome="forest";
        public bool underground;
        public float speed=2, health=30, damage=8;
        public Sprite sprite;
        public ItemData drop;
    }
}
