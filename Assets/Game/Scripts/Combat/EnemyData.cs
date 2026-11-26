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
        [Min(1)] public float sightRange = 14;
        [Min(0.5f)] public float hitReach = 1.4f;
        [Min(0.1f)] public float windup = 0.45f, recovery = 0.85f;
        [Min(1)] public float ambushRange = 3.5f, dashSpeed = 9;
        [Min(0.05f)] public float dashDuration = 0.32f;
        [Min(0)] public float lightRadius = 4, patrolRadius = 3, sanityDamage = 4;
        public DamageKind damageKind;
        public Color warningColor = new Color(1f, 0.46f, 0.26f);
        public Sprite sprite;
        public ItemData drop;
    }
}
