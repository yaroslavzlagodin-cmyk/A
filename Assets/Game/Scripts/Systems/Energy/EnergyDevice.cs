using BrokenHeart.Inventory;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace BrokenHeart.Systems.Energy
{
    public sealed class EnergyDevice : MonoBehaviour
    {
        public ItemData Item { get; private set; }
        public EnergyDeviceData Data => Item.Device;
        public float Stored { get; set; }
        public bool Powered { get; set; }
        private Light2D glow;
        public void Initialize(ItemData item)
        {
            Item = item;
            glow = gameObject.AddComponent<Light2D>();
            glow.lightType = Light2D.LightType.Point;
            glow.pointLightOuterRadius = item.Device.consumptionPerSecond > 0 ? 6 : 2;
            glow.color = item.Device.color;
        }
        private void LateUpdate() { glow.intensity = Powered ? 1.3f : Data.generationPerSecond > 0 ? 0.35f : 0.05f; }
    }
}
