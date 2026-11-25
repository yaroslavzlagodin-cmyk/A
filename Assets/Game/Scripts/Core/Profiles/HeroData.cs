using UnityEngine;
namespace BrokenHeart.Core.Profiles
{
    [CreateAssetMenu(menuName="Broken Heart/Hero")]
    public sealed class HeroData : ScriptableObject
    {
        public string id, displayName;
        [TextArea] public string description;
        public Color color;
        public Sprite sprite;
    }
}
