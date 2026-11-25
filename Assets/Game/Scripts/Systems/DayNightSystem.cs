using BrokenHeart.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
namespace BrokenHeart.Systems
{
    public sealed class DayNightSystem : MonoBehaviour
    {
        [SerializeField] private Light2D sunlight;
        [SerializeField] private Camera view;
        [SerializeField] private WorldWeather weather;
        [SerializeField] private float dayLength = 480;
        private BlockWorld world;
        private Light2D nearbyLight;
        private float shade;
        public float Elapsed { get; private set; }
        public float Fraction => Mathf.Repeat(Elapsed / dayLength + 0.35f, 1);
        public int Day => 1 + Mathf.FloorToInt(Elapsed / dayLength + 0.35f);
        public bool IsNight => Fraction < 0.23f || Fraction > 0.77f;
        public float Intensity => sunlight.intensity;
        public float SurfaceIntensity { get; private set; } = 1;
        public string TimeLabel => $"{(int)(Fraction * 24):00}:{(int)(Fraction * 24 * 60) % 60:00}";
        private void Start()
        {
            world = GetComponent<BlockWorld>();
            if (weather == null || weather.Observer == null) return;
            var glow = new GameObject("Nearby Light", typeof(Light2D));
            glow.transform.SetParent(transform, false);
            nearbyLight = glow.GetComponent<Light2D>();
            nearbyLight.lightType = Light2D.LightType.Point;
            nearbyLight.color = new Color(0.68f, 0.77f, 0.97f);
            nearbyLight.pointLightOuterRadius = 4.7f;
            nearbyLight.pointLightInnerRadius = 0.85f;
            ApplyLighting();
        }
        private void Update()
        {
            if (Time.timeScale == 0) return;
            if (Debug.isDebugBuild && Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame) Advance(dayLength / 4);
            Advance(Time.deltaTime);
            shade = Mathf.MoveTowards(shade, DepthShade(), Time.deltaTime * 1.8f);
            ApplyLighting();
        }
        public void Advance(float seconds) { Elapsed = Mathf.Max(0,Elapsed + seconds); }
        public void Restore(float elapsed) { Elapsed = Mathf.Max(0,elapsed); ApplyLighting(); }
        public void SetHour(float hour) { Elapsed = Mathf.Repeat(hour / 24 - 0.35f,1) * dayLength; ApplyLighting(); }
        public void ApplyLighting()
        {
            float angle = Mathf.Sin((Fraction - 0.25f) * Mathf.PI * 2);
            float sun = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.16f, 0.6f, angle));
            float twilight = Mathf.Clamp01(1 - Mathf.Abs(angle - 0.05f) * 3.2f);
            float cover = weather != null ? weather.CloudCover : 0;
            float flash = (weather != null ? weather.Flash : 0) * (1 - shade);
            float daylight = Mathf.Lerp(0.070f, 1, sun) * Mathf.Lerp(1, 0.61f, cover);
            SurfaceIntensity = daylight;
            sunlight.intensity = Mathf.Lerp(daylight, 0.09f, shade) + flash;
            var skyTint = Color.Lerp(new Color(0.52f, 0.64f, 0.94f), new Color(1, 0.97f, 0.89f), sun);
            skyTint = Color.Lerp(skyTint, new Color(1, 0.70f, 0.49f), twilight * 0.43f * (1 - cover));
            sunlight.color = Color.Lerp(skyTint, new Color(0.56f, 0.65f, 0.81f), Mathf.Max(shade, cover * 0.55f));
            var sky = Color.Lerp(new Color(0.009f, 0.015f, 0.037f), new Color(0.35f, 0.57f, 0.67f), sun);
            sky = Color.Lerp(sky, new Color(0.26f, 0.16f, 0.23f), twilight * 0.43f * (1 - cover));
            sky = Color.Lerp(sky, new Color(0.16f, 0.20f, 0.27f) * Mathf.Lerp(0.19f, 1, sun), cover);
            view.backgroundColor = Color.Lerp(sky, new Color(0.012f, 0.016f, 0.024f), shade) +
                new Color(0.51f, 0.60f, 0.79f) * flash * 0.55f;
            if (nearbyLight != null)
            {
                float glow = Mathf.Lerp(0.28f, 0.43f, shade) * (1 - sun * (1 - shade));
                nearbyLight.enabled = glow > 0.01f;
                nearbyLight.intensity = glow;
                nearbyLight.transform.position = weather.Observer.position + Vector3.up * 0.4f;
            }
        }

        private float DepthShade()
        {
            if (world == null || !world.TerrainShadowsEnabled || weather == null || weather.Observer == null) return 0;
            int depth = world.DepthAt(world.CellAt(weather.Observer.position));
            return Mathf.SmoothStep(0, 1, (depth - 2) / 14f);
        }
    }
}
