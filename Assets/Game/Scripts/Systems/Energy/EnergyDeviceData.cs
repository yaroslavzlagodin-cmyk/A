using UnityEngine;
namespace BrokenHeart.Systems.Energy
{
    [CreateAssetMenu(menuName = "Broken Heart/Energy Device")]
    public sealed class EnergyDeviceData : ScriptableObject
    {
        [Min(1)] public float capacity = 20;
        [Min(0)] public float generationPerSecond;
        [Min(0)] public float consumptionPerSecond;
        [Min(1)] public float connectionRange = 6;
        public Color color = Color.cyan;
    }
}
