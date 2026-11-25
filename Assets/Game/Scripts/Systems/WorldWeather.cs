using BrokenHeart.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BrokenHeart.Systems
{
    public enum WeatherKind { Clear, Rain, Fog, Storm }

    public sealed class WorldWeather : MonoBehaviour
    {
        [SerializeField] private Transform observer;
        [SerializeField] private BlockWorld world;
        [SerializeField] private Shader particleShader;
        private ParticleSystem rain, fog;
        private Material rainMaterial, fogMaterial;
        private Texture2D fogTexture;
        private System.Random random;
        private float changeIn, thunderIn, flashTime, wind, windTarget;
        public WeatherKind Current { get; private set; }
        public Transform Observer => observer;
        public float RainRate => rain.emission.rateOverTime.constant;
        public float CloudCover { get; private set; }
        public float Flash => flashTime > 0
            ? Mathf.Clamp01(flashTime / 0.35f) * (0.27f + 0.56f * Mathf.Pow(Mathf.Cos(flashTime * 29), 6))
            : 0;
        public event System.Action Thunder;

        private void Awake()
        {
            random = new System.Random(world.Seed ^ 9321);
            var shader = particleShader != null ? particleShader : Shader.Find("Universal Render Pipeline/Particles/Unlit");
            rainMaterial = ParticleMaterial(shader);
            fogMaterial = ParticleMaterial(shader);
            fogTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float falloff = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f);
                float cloud = Mathf.PerlinNoise(x * 0.14f + 10, y * 0.17f + 13);
                fogTexture.SetPixel(x, y, new Color(1, 1, 1, falloff * falloff * Mathf.Lerp(0.4f, 1, cloud)));
            }
            fogTexture.Apply();
            fogMaterial.SetTexture("_BaseMap", fogTexture);
            rain = MakeParticles("Rain", rainMaterial, false);
            fog = MakeParticles("Fog", fogMaterial, true);
            SetWeather(WeatherKind.Clear);
        }

        private static Material ParticleMaterial(Shader shader)
        {
            var material = new Material(shader);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            return material;
        }

        private ParticleSystem MakeParticles(string label, Material material, bool mist)
        {
            var go = new GameObject(label, typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = mist ? 100 : 1400;
            main.startLifetime = mist ? new ParticleSystem.MinMaxCurve(8, 15) : new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSpeed = 0;
            main.startSize3D = true;
            main.startSizeX = mist ? new ParticleSystem.MinMaxCurve(11, 22) : new ParticleSystem.MinMaxCurve(0.025f, 0.048f);
            main.startSizeY = mist ? new ParticleSystem.MinMaxCurve(4, 9) : new ParticleSystem.MinMaxCurve(0.32f, 0.66f);
            main.startSizeZ = 1;
            main.startColor = mist ? new Color(0.60f, 0.67f, 0.75f, 0.21f)
                : new ParticleSystem.MinMaxGradient(new Color(0.49f, 0.66f, 0.82f, 0.29f), new Color(0.69f, 0.82f, 0.92f, 0.62f));
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(52, mist ? 9 : 1, 0.1f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = mist ? 1.1f : -3;
            velocity.y = mist ? 0 : -19;
            velocity.z = 0;
            if (mist)
            {
                var fade = ps.colorOverLifetime;
                var opacity = new Gradient();
                opacity.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.28f), new GradientAlphaKey(0, 1) });
                fade.enabled = true;
                fade.color = opacity;
            }
            else
            {
                var collision = ps.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision2D;
                collision.collidesWith = 1;
                collision.quality = ParticleSystemCollisionQuality.Medium;
                collision.lifetimeLoss = 1;
                collision.radiusScale = 0.2f;
            }
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = mist ? 35 : 30;
            ps.Play();
            return ps;
        }

        public void SetWeather(WeatherKind kind)
        {
            Current = kind;
            windTarget = kind == WeatherKind.Storm ? -7 : kind == WeatherKind.Rain ? -3 : -1;
            changeIn = kind == WeatherKind.Clear ? random.Next(95, 181) : random.Next(45, 101);
            thunderIn = random.Next(3, 10);
            flashTime = 0;
            UpdateEmission(Shelter());
            if (kind == WeatherKind.Clear) { rain.Clear(); fog.Clear(); }
        }

        private float Shelter() => Mathf.Clamp01((world.DepthAt(world.CellAt(observer.position)) - 5) / 11f);

        private void UpdateEmission(float shelter)
        {
            var rainEmission = rain.emission;
            var fogEmission = fog.emission;
            rainEmission.rateOverTime = (Current == WeatherKind.Storm ? 510 : Current == WeatherKind.Rain ? 245 : 0) * (1 - shelter);
            fogEmission.rateOverTime = (Current == WeatherKind.Fog ? 7 : Current == WeatherKind.Storm ? 2.5f : 0) * (1 - shelter);
        }

        private void Update()
        {
            if (Time.timeScale == 0) return;
            float dt = Time.deltaTime;
            rain.transform.position = observer.position + Vector3.up * 14;
            fog.transform.position = observer.position + Vector3.up * 2;
            if (Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame)
                SetWeather((WeatherKind)(((int)Current + 1) % 4));
            changeIn -= dt;
            if (changeIn <= 0) ChooseWeather();

            float shelter = Shelter();
            float cover = Current switch { WeatherKind.Rain => 0.46f, WeatherKind.Fog => 0.58f, WeatherKind.Storm => 0.83f, _ => 0 };
            CloudCover = Mathf.MoveTowards(CloudCover, cover, dt * 0.18f);
            wind = Mathf.MoveTowards(wind, windTarget, dt * 2);
            float gust = wind * (0.82f + 0.25f * Mathf.Sin(Time.time * 0.91f) + 0.15f * Mathf.Sin(Time.time * 2.3f));
            var rainVelocity = rain.velocityOverLifetime;
            rainVelocity.x = gust;
            var rainMain = rain.main;
            rainMain.startRotation = Mathf.Atan2(gust, 19);
            var fogVelocity = fog.velocityOverLifetime;
            fogVelocity.x = 0.6f - wind * 0.24f;
            UpdateEmission(shelter);

            flashTime -= dt;
            thunderIn -= dt;
            if (Current == WeatherKind.Storm && thunderIn <= 0)
            {
                flashTime = shelter < 0.85f ? 0.4f : 0;
                thunderIn = random.Next(7, 19);
                Thunder?.Invoke();
            }
        }

        private void ChooseWeather()
        {
            int roll = random.Next(100);
            SetWeather(Current switch
            {
                WeatherKind.Clear => roll < 74 ? WeatherKind.Rain : WeatherKind.Fog,
                WeatherKind.Rain => roll < 38 ? WeatherKind.Storm : roll < 56 ? WeatherKind.Fog : WeatherKind.Clear,
                WeatherKind.Storm => WeatherKind.Rain,
                _ => roll < 72 ? WeatherKind.Clear : WeatherKind.Rain
            });
        }

        private void OnDestroy()
        {
            Destroy(rainMaterial);
            Destroy(fogMaterial);
            Destroy(fogTexture);
        }
    }
}
