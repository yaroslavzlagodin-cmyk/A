using UnityEngine;
using UnityEngine.Tilemaps;
using BrokenHeart.Inventory;

namespace BrokenHeart.World
{
    [CreateAssetMenu(menuName = "Broken Heart/Block")]
    public sealed class BlockData : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private Tile tile;
        [SerializeField, Min(0.05f)] private float hardness = 0.4f;
        [SerializeField] private bool solid = true;
        [SerializeField] private bool breakable = true;
        [SerializeField] private ItemData drop;
        public string Id => id;
        public Tile Tile => tile;
        public Sprite Sprite => tile != null ? tile.sprite : null;
        public float Hardness => hardness;
        public bool Solid => solid;
        public bool Breakable => breakable;
        public ItemData Drop => drop;
    }
}
