using BrokenHeart.Inventory;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace BrokenHeart.Systems.Energy
{
    public interface IDimmableLight
    {
        bool IsDimmed { get; }
        void DimFor(float seconds);
        void RestoreLight();
    }

    public sealed class EnergyDevice : MonoBehaviour, IDimmableLight
    {
        public ItemData Item { get; private set; }
        public EnergyDeviceData Data => Item.Device;
        public float Stored { get; set; }
        public bool Powered { get; set; }
        private Light2D glow;
        private float dimUntil;
        public bool IsDimmed => Time.time < dimUntil;
        public void DimFor(float seconds) => dimUntil = Mathf.Max(dimUntil, Time.time + Mathf.Max(0, seconds));
        public void RestoreLight() => dimUntil = 0;
        public void Initialize(ItemData item)
        {
            Item = item;
            glow = gameObject.AddComponent<Light2D>();
            glow.lightType = Light2D.LightType.Point;
            glow.pointLightOuterRadius = item.Device.consumptionPerSecond > 0 ? 6 : 2;
            glow.color = item.Device.color;
        }
        private void LateUpdate() { glow.intensity = Powered ? (IsDimmed ? 0.12f : 1.3f) : Data.generationPerSecond > 0 ? 0.35f : 0.05f; }
    }
}
