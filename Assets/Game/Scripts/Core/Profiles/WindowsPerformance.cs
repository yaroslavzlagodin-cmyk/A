using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BrokenHeart.Inventory;
using BrokenHeart.Player;
using BrokenHeart.Systems;
using BrokenHeart.Systems.Energy;
using BrokenHeart.UI;
using BrokenHeart.World;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Process = System.Diagnostics.Process;

namespace BrokenHeart.Core.Profiles
{
    internal sealed class WindowsPerformance : MonoBehaviour
    {
        private const int Width = 20000;
        private const int Height = 1600;
        private const int Devices = 64;
        private const int TrackY = 48;
        private const int TargetFps = 60;
        private const float CycleSeconds = 30f;
        private WorldProfile profile;
        private CharacterProfile hero;
        private string output;
        private float duration;
        private Keyboard keyboard;
        private BlockWorld world;
        private PlayerInventory inventory;
        private EnergyNetwork energy;
        private BlockInteraction interaction;
        private ItemData dirt;
        private BlockData track;
        private Rigidbody2D body;
        private int trackEnd, startDirt, mined, phase = -1, cycle = -1;
        private Vector2Int mineCell;
        private bool mining;

        [Serializable]
        private sealed class Metrics
        {
            public int samples;
            public double mean, p50, p95, p99, max;
        }

        [Serializable]
        private sealed class Snapshot
        {
            public double seconds;
            public float x, y;
            public int activeChunks, cachedChunks, devices;
            public long workingSetBytes, managedBytes;
        }

        [Serializable]
        private sealed class Results
        {
            public string version, unity, cpu, gpu, cpuMarker, gcMarker;
            public int worldWidth, worldHeight, seed, targetFps, screenWidth, screenHeight, devices;
            public int frames, mined, mapOpens, activeChunksMax, cachedChunksMax;
            public float distance;
            public double elapsed;
            public long workingSetMax;
            public Metrics frameMs, cpuMs, gcBytes, physics2DMs, mapFrameMs, mapRasterMs, dropSpawnMs, weatherMs;
            public List<Snapshot> series;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (Application.isEditor || !Debug.isDebugBuild ||
                Array.IndexOf(arguments, "-broken-heart-performance") < 0) return;

            try
            {
                string output = Path.GetFullPath(Argument(arguments, "-performance-output",
                    Path.Combine(Path.GetDirectoryName(Application.dataPath), "Performance", "results.json")));
                if (!float.TryParse(Argument(arguments, "-performance-seconds", "600"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float duration) || !float.IsFinite(duration) || duration < 5 || duration > 3600)
                    throw new ArgumentException("Performance duration must be between 5 and 3600 seconds.");

                string folder = Path.GetDirectoryName(output);
                Directory.CreateDirectory(folder);
                ProfileLibrary.Root = Path.Combine(folder, "PerformanceSaves", Guid.NewGuid().ToString("N"));
                var profile = new WorldProfile
                {
                    name = "Performance world", seed = "43147", width = Width, height = Height, difficulty = 0, mountains = false
                };
                var hero = new CharacterProfile { name = "Performance hero" };
                var library = new LibraryData { windowWidth = 1280, windowHeight = 720, fps = TargetFps };
                library.worlds.Add(profile);
                library.characters.Add(hero);
                ProfileLibrary.Save(library);
                Application.runInBackground = true;
                // The test keyboard supplies queued input even when the player window has no focus.
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                var runner = new GameObject("World performance test").AddComponent<WindowsPerformance>();
                runner.profile = profile;
                runner.hero = hero;
                runner.output = output;
                runner.duration = duration;
                DontDestroyOnLoad(runner.gameObject);
            }
            catch (Exception error) { Fail(error); }
        }

        private async void Start()
        {
            try
            {
                var menu = FindAnyObjectByType<FrontEndMenu>();
                Require(menu != null, "The performance test requires the lobby.");
                menu.Launch(profile, hero);
                double deadline = Time.realtimeSinceStartupAsDouble + 45;
                while (SceneManager.GetActiveScene().name != "Game")
                {
                    Require(Time.realtimeSinceStartupAsDouble < deadline, "The performance world did not load.");
                    await Awaitable.NextFrameAsync();
                }
                await Awaitable.NextFrameAsync();
                await Awaitable.NextFrameAsync();
                Prepare();
                await Awaitable.WaitForSecondsAsync(3f);
                await Measure();
                Application.Quit(0);
            }
            catch (Exception error) { Fail(error); }
        }

        private void Prepare()
        {
            world = FindAnyObjectByType<BlockWorld>();
            inventory = FindAnyObjectByType<PlayerInventory>();
            Require(world != null && inventory != null && world.GetComponent<SaveSystem>().Status == "Save loaded" &&
                world.Bounds.width == Width && world.Bounds.height == Height, "The maximum-size world did not initialize.");
            body = inventory.GetComponent<Rigidbody2D>();
            interaction = inventory.GetComponent<BlockInteraction>();
            // The scripted mine calls keep their progress even if the desktop has focus elsewhere.
            interaction.enabled = false;
            energy = world.GetComponent<EnergyNetwork>();
            dirt = inventory.Catalog.Find("dirt");
            track = inventory.Catalog.Find("stone").PlaceableBlock;
            inventory.Select(inventory.HotbarSize - 1);
            startDirt = inventory.Model.Count(dirt);
            keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFps;
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);

            energy.Clear();
            var generator = inventory.Catalog.Find("crystal_generator");
            var lamp = inventory.Catalog.Find("crystal_lamp");
            for (int group = 0; group < 8; group++)
            for (int device = 0; device < 8; device++)
            {
                var cell = new Vector2Int(-5600 + group * 1600 + device * 2, TrackY - 4);
                world.Set(cell, null);
                world.Set(cell + Vector2Int.down, track);
                Require(energy.CanPlace(cell), "A benchmark energy device has no space or support.");
                energy.Spawn(device % 4 == 0 ? generator : lamp, world.Center(cell));
            }
            trackEnd = -8;
            ExtendTrack(16);
            body.position = new Vector2(0.5f, TrackY + 2.1f);
            body.linearVelocity = Vector2.zero;
            inventory.transform.position = body.position;
            world.RefreshVisible(true);
            world.Map.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
        }

        private async Awaitable Measure()
        {
            using var process = Process.GetCurrentProcess();
            using var cpu = CreateRecorder(ProfilerCategory.Internal, "Main Thread");
            using var gc = CreateRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            using var physics = CreateRecorder(ProfilerCategory.Physics2D, "Physics2D.Simulate");
            using var mapRaster = CreateRecorder(ProfilerCategory.Scripts, "Broken Heart.MapRaster");
            using var weather = CreateRecorder(ProfilerCategory.Scripts, "Broken Heart.Weather");
            var stats = new Results
            {
                version = Application.version, unity = Application.unityVersion,
                cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName, worldWidth = Width, worldHeight = Height,
                seed = world.Seed, targetFps = TargetFps, screenWidth = Screen.width, screenHeight = Screen.height,
                devices = Devices, series = new List<Snapshot>((int)duration + 2)
            };
            int frameCapacity = (int)(duration * 70f);
            var frameSamples = new List<double>(frameCapacity);
            var cpuSamples = new List<double>(frameCapacity);
            var gcSamples = new List<double>(frameCapacity);
            var physicsSamples = new List<double>(frameCapacity);
            var mapSamples = new List<double>(frameCapacity / 5);
            var rasterSamples = new List<double>(frameCapacity);
            var weatherSamples = new List<double>(frameCapacity);
            var dropSamples = new List<double>((int)duration / 30 + 1);
            ProfilerRecorder drop = default;
            var chunks = (ChunkBlockStore)world.Store;
            float startX = body.position.x;
            double start = Time.realtimeSinceStartupAsDouble, previousFrame = start, nextSample = 0;
            Debug.Log($"WORLD PERFORMANCE: measuring {duration}s at {stats.screenWidth}x{stats.screenHeight} in {Width}x{Height} with {energy.Devices.Count} devices.");
            try
            {
                for (;;)
                {
                    await Awaitable.NextFrameAsync();
                    double time = Time.realtimeSinceStartupAsDouble;
                    double elapsed = time - start;
                    double frameMs = (time - previousFrame) * 1000;
                    previousFrame = time;
                    frameSamples.Add(frameMs);
                    if (inventory.IsMapOpen) mapSamples.Add(frameMs);
                    AddSample(cpu, cpuSamples, 1e-6);
                    AddSample(gc, gcSamples, 1);
                    AddSample(physics, physicsSamples, 1e-6);
                    AddSample(mapRaster, rasterSamples, 1e-6, true);
                    AddSample(weather, weatherSamples, 1e-6);
                    stats.activeChunksMax = Mathf.Max(stats.activeChunksMax, world.VisibleChunks);
                    stats.cachedChunksMax = Mathf.Max(stats.cachedChunksMax, chunks.CachedChunks);
                    if (elapsed >= nextSample)
                    {
                        process.Refresh();
                        long ram = process.WorkingSet64;
                        stats.workingSetMax = Math.Max(stats.workingSetMax, ram);
                        stats.series.Add(new Snapshot
                        {
                            seconds = elapsed, x = body.position.x, y = body.position.y, devices = energy.Devices.Count,
                            activeChunks = world.VisibleChunks, cachedChunks = chunks.CachedChunks,
                            workingSetBytes = ram, managedBytes = GC.GetTotalMemory(false)
                        });
                        Require(energy.Devices.Count == Devices, "The device fixture lost an energy device.");
                        Require(body.position.y >= TrackY + 0.2f, "The hero fell through the benchmark track.");
                        if (stats.series.Count == 6 || stats.series.Count % 30 == 0)
                            Debug.Log($"WORLD PERFORMANCE: {elapsed:F1}s / {body.position.x - startX:F1} blocks, " +
                                $"{mined} mined, {stats.mapOpens} map visits, {chunks.CachedChunks} cached chunks.");
                        nextSample = Math.Floor(elapsed) + 1;
                    }
                    if (elapsed >= duration)
                    {
                        stats.elapsed = elapsed;
                        break;
                    }
                    int previousMined = mined;
                    RunWorkload((float)elapsed, stats);
                    if (mined != previousMined)
                    {
                        if (mined == 1) drop = CreateRecorder(ProfilerCategory.Scripts, "Broken Heart.DropSpawn");
                        else AddSample(drop, dropSamples, 1e-6);
                    }
                }
            }
            finally { drop.Dispose(); }

            stats.frames = frameSamples.Count;
            stats.mined = mined;
            stats.distance = body.position.x - startX;
            float minimumDistance = Mathf.Max(1f, (duration - 12f) * 3f);
            Require(stats.distance >= minimumDistance,
                $"The hero covered {stats.distance:F2} of {minimumDistance:F2} required blocks. " +
                $"Focus: {Application.isFocused}, test keyboard: {keyboard.enabled}, " +
                $"current keyboard: {Keyboard.current == keyboard}, right input: {keyboard.dKey.isPressed}, " +
                $"velocity: {body.linearVelocity}, mined: {mined}, map visits: {stats.mapOpens}.");
            Require(mined >= 1 && (duration < 25 || stats.mapOpens > 0), "Mining or map workload did not run.");
            int accounted = inventory.Model.Count(dirt) - startDirt +
                FindObjectsByType<ItemDrop>().Where(drop => drop.Item == dirt).Sum(drop => drop.Count);
            Require(accounted == mined, "A mined block's drop was lost or duplicated.");
            stats.cpuMarker = cpuSamples.Count > 0 ? "Main Thread (includes camera/render and waits)" : "unavailable";
            stats.gcMarker = gcSamples.Count > 0 ? "GC Allocated In Frame" : "unavailable";
            stats.frameMs = Summarize(frameSamples);
            stats.cpuMs = Summarize(cpuSamples);
            stats.gcBytes = Summarize(gcSamples);
            stats.physics2DMs = Summarize(physicsSamples);
            stats.mapFrameMs = Summarize(mapSamples);
            stats.mapRasterMs = Summarize(rasterSamples);
            stats.weatherMs = Summarize(weatherSamples);
            stats.dropSpawnMs = Summarize(dropSamples);
            File.WriteAllText(output, JsonUtility.ToJson(stats, true));
            Debug.Log($"WORLD PERFORMANCE: PASS {output}");
        }

        private void RunWorkload(float elapsed, Results stats)
        {
            keyboard.MakeCurrent();
            int nextCycle = (int)(elapsed / CycleSeconds);
            float cycleTime = elapsed - nextCycle * CycleSeconds;
            int nextPhase = cycleTime < 3f ? 0 : cycleTime < 22f ? 1 : cycleTime < 26f ? 2 : 3;
            if (nextCycle != cycle || nextPhase != phase)
            {
                cycle = nextCycle;
                phase = nextPhase;
                inventory.SetMapOpen(phase == 2);
                InputSystem.QueueStateEvent(keyboard, phase is 1 or 3 ? new KeyboardState(Key.D) : new KeyboardState());
                if (phase == 2) stats.mapOpens++;
                if (phase == 0)
                {
                    mineCell = new Vector2Int(Mathf.FloorToInt(body.position.x) + 1, TrackY + 2);
                    world.Set(mineCell, dirt.PlaceableBlock);
                    mining = true;
                    world.GetComponent<WorldWeather>().SetWeather(cycle % 2 == 0 ? WeatherKind.Rain : WeatherKind.Clear);
                }
                if (phase == 1) CollectNearbyDrops();
            }
            ExtendTrack(Mathf.FloorToInt(body.position.x) + 16);
            if (!mining || phase != 0 || !interaction.Mine(mineCell, Time.deltaTime)) return;
            mined++;
            mining = false;
        }

        private void ExtendTrack(int end)
        {
            while (trackEnd < end)
            {
                int x = ++trackEnd;
                world.Set(new Vector2Int(x, TrackY), track);
                for (int y = TrackY + 1; y <= TrackY + 3; y++)
                    world.Set(new Vector2Int(x, y), null);
            }
        }

        private void CollectNearbyDrops()
        {
            foreach (var drop in FindObjectsByType<ItemDrop>())
                if (drop.Item == dirt && Vector2.Distance(body.position, drop.transform.position) <= 3f)
                    drop.TryCollect(inventory);
        }

        private static ProfilerRecorder CreateRecorder(ProfilerCategory category, string marker)
        {
            try { return ProfilerRecorder.StartNew(category, marker, 1); }
            catch (ArgumentException) { return default; }
        }

        private static void AddSample(ProfilerRecorder recorder, List<double> values, double scale, bool reset = false)
        {
            if (!recorder.Valid || recorder.Count == 0) return;
            values.Add(recorder.LastValue * scale);
            if (reset)
            {
                recorder.Reset();
                recorder.Start();
            }
        }

        private static Metrics Summarize(List<double> samples)
        {
            if (samples.Count == 0) return new Metrics();
            samples.Sort();
            return new Metrics
            {
                samples = samples.Count, mean = samples.Average(), max = samples[^1],
                p50 = samples[(int)(0.50 * (samples.Count - 1))], p95 = samples[(int)(0.95 * (samples.Count - 1))],
                p99 = samples[(int)(0.99 * (samples.Count - 1))]
            };
        }

        private static string Argument(string[] args, string key, string fallback)
        {
            int index = Array.IndexOf(args, key);
            return index < 0 ? fallback : index + 1 < args.Length ? args[index + 1] :
                throw new ArgumentException($"Missing value for {key}.");
        }

        private void OnDestroy()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Fail(Exception error)
        {
            Debug.LogError($"WORLD PERFORMANCE: FAIL {error}");
            Application.Quit(1);
        }
    }
}
