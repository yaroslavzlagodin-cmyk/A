using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrokenHeart.Combat;
using BrokenHeart.Inventory;
using BrokenHeart.Player;
using BrokenHeart.Systems;
using BrokenHeart.UI;
using BrokenHeart.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BrokenHeart.Core.Profiles
{
    internal sealed class WindowsSmoke : MonoBehaviour
    {
        private WorldProfile worldProfile;
        private CharacterProfile character;
        private CharacterProfile hardcore;
        private (WorldProfile world, CharacterProfile hero, int generation) legacyJourney;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (Application.isEditor || !Debug.isDebugBuild ||
                Array.IndexOf(arguments, "-broken-heart-smoke") < 0) return;

            try
            {
                // Set the test save directory before the menu reads its profile library.
                string buildFolder = Path.GetDirectoryName(Application.dataPath);
                string smokeRoot = Path.Combine(buildFolder, "SmokeSaves", Guid.NewGuid().ToString("N"));
                ProfileLibrary.Root = Path.Combine(smokeRoot, "Current");
                var legacy = ImportLegacyFixture(arguments, smokeRoot);
                var world = new WorldProfile { name = "Smoke test world", seed = "20261125", difficulty = 0 };
                var hero = new CharacterProfile { name = "Smoke test hero", challenge = 2 };
                var hardcoreHero = new CharacterProfile { name = "Smoke hardcore hero", challenge = 5 };
                var library = ProfileLibrary.Load();
                library.worlds.Add(world);
                library.characters.Add(hero);
                library.characters.Add(hardcoreHero);
                ProfileLibrary.Save(library);

                Application.runInBackground = true;
                var runner = new GameObject("Windows smoke test").AddComponent<WindowsSmoke>();
                runner.worldProfile = world;
                runner.character = hero;
                runner.hardcore = hardcoreHero;
                runner.legacyJourney = legacy;
                DontDestroyOnLoad(runner.gameObject);
                Debug.Log($"WINDOWS SMOKE: Isolated saves at {ProfileLibrary.Root}");
            }
            catch (Exception error) { Fail(error); }
        }

        private async void Start()
        {
            try
            {
                Require(SceneManager.GetActiveScene().name == "Menu", "The build must start in Menu.");
                var menu = FindAnyObjectByType<FrontEndMenu>();
                Require(menu != null && menu.Page == "main" && !SessionContext.Active, "The lobby must open before the world.");
                if (legacyJourney.world != null) await CheckLegacyJourney();
                FindAnyObjectByType<FrontEndMenu>().Launch(worldProfile, character);
                await WaitForScene("Game");

                var world = FindAnyObjectByType<BlockWorld>();
                var inventory = FindAnyObjectByType<PlayerInventory>();
                var save = FindAnyObjectByType<SaveSystem>();
                Require(world != null && world.Seed == worldProfile.Seed && inventory != null &&
                    save != null && save.Status == "Save loaded", "The selected fresh world must load.");
                Debug.Log("WINDOWS SMOKE: Lobby and fresh world passed.");

                // Save a world edit and a hero change through the usual exit action.
                var cell = new Vector2Int(12, world.Generator.Surface(12) + 4);
                string block = world.Store.Get(cell) == null ? "dirt" : null;
                Require(world.Store.Set(cell, block), "Could not edit the smoke test world.");
                int selected = inventory.HotbarSize - 1;
                inventory.Select(selected);
                Require(FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(true), save.Status);
                await WaitForScene("Menu");
                Require(!SessionContext.Active && ProfileLibrary.Load().journeys.Exists(
                    journey => journey.worldId == worldProfile.id && journey.characterId == character.id),
                    "Save and exit must record the journey and clear the session.");

                FindAnyObjectByType<FrontEndMenu>().Launch(worldProfile, character);
                await WaitForScene("Game");
                Require(FindAnyObjectByType<BlockWorld>().Store.Get(cell) == block &&
                    FindAnyObjectByType<PlayerInventory>().SelectedIndex == selected,
                    "Reopening the world must restore the terrain edit and selected slot.");
                await CheckAutosaves();
                await CheckDeaths();
                Require(FindAnyObjectByType<FrontEndMenu>().Page == "main" && !SessionContext.Active,
                    "The game must return to the lobby after exit.");
                Debug.Log("WINDOWS SMOKE: PASS");
                Application.Quit(0);
            }
            catch (Exception error) { Fail(error); }
        }

        private static (WorldProfile world, CharacterProfile hero, int generation) ImportLegacyFixture(
            string[] arguments, string smokeRoot)
        {
            const string option = "-broken-heart-legacy-fixture";
            int index = Array.IndexOf(arguments, option);
            if (index < 0) return default;
            Require(index + 1 < arguments.Length, $"Supply a legacy save directory after {option}.");
            string source = Path.GetFullPath(arguments[index + 1]);
            Require(File.Exists(Path.Combine(source, "library.json")), "The legacy fixture needs library.json.");
            string sourceWorlds = Path.Combine(source, "worlds");
            Require(Directory.Exists(sourceWorlds), "The legacy fixture needs a worlds directory.");
            string copyRoot = Path.Combine(smokeRoot, "Legacy");
            Directory.CreateDirectory(Path.Combine(copyRoot, "worlds"));
            var files = new[] { "library.json", "library.json.bak" }
                .Where(name => File.Exists(Path.Combine(source, name)))
                .Concat(Directory.EnumerateFiles(sourceWorlds, "*.json*").Select(path =>
                    Path.Combine("worlds", Path.GetFileName(path))));
            var copies = new List<(string path, byte[] contents)>();
            foreach (string name in files)
            {
                byte[] contents = File.ReadAllBytes(Path.Combine(source, name));
                string copy = Path.Combine(copyRoot, name);
                File.WriteAllBytes(copy, contents);
                copies.Add((copy, contents));
            }

            Require(SaveTransactionStore.Import(ProfileLibrary.Root, copyRoot), "The legacy fixture must import.");
            Require(copies.All(copy => File.ReadAllBytes(copy.path).SequenceEqual(copy.contents)),
                "Import must preserve the legacy fixture files.");
            var imported = SaveTransactionStore.Read(ProfileLibrary.Root);
            foreach (var journey in imported.Library.journeys)
            {
                var world = imported.Library.worlds.Find(profile => profile.id == journey.worldId);
                var hero = imported.Library.characters.Find(profile => profile.id == journey.characterId && !profile.dead);
                var data = world == null ? null : imported.ReadWorld(world.id);
                if (world != null && hero != null && data != null) return (world, hero, data.generatorVersion);
            }
            throw new InvalidDataException("The legacy fixture has no saved journey for a living hero.");
        }

        private async Awaitable CheckLegacyJourney()
        {
            FindAnyObjectByType<FrontEndMenu>().Launch(legacyJourney.world, legacyJourney.hero);
            await WaitForScene("Game");
            var world = FindAnyObjectByType<BlockWorld>();
            var inventory = FindAnyObjectByType<PlayerInventory>();
            var save = FindAnyObjectByType<SaveSystem>();
            Require(save.Status == "Save loaded" && world.Seed == legacyJourney.world.Seed &&
                world.Generator.Version == legacyJourney.generation &&
                inventory.Progress.TotalExperience == legacyJourney.hero.experience,
                "The imported journey must open with its seed, generator version, and hero progress.");
            Require(FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(false), "Could not exit the imported journey.");
            await WaitForScene("Menu");
            Debug.Log("WINDOWS SMOKE: Copied legacy journey imported and loaded.");
        }

        private async Awaitable CheckAutosaves()
        {
            var inventory = FindAnyObjectByType<PlayerInventory>();
            var sleep = inventory.GetComponent<SleepSystem>();
            var motor = inventory.GetComponent<PlayerMotor>();
            await WaitUntil(() => motor.Grounded, 10, "The hero must reach safe ground before resting.");
            float normalSpeed = Time.timeScale;
            // Keep the physics step so accelerated play still uses the normal collision checks.
            Time.timeScale = 5;
            try
            {
                inventory.GetComponent<PlayerVitals>().Restore(65);
                inventory.GetComponent<SanitySystem>().Restore(36, 45, false);
                inventory.Select(1);
                Require(sleep.TryRest(), sleep.Status);
                await WaitUntil(() => !sleep.Resting, 15, "The hero must finish an uninterrupted rest.");
                var rested = ReadHero(character.id);
                Require(rested.selected == 1 && rested.health >= 79 && rested.sanity >= 83 && rested.awakeSeconds == 0,
                    "Finishing rest must save the recovered health, sanity, and selected slot.");
                Debug.Log("WINDOWS SMOKE: Autosave after rest passed.");

                double restSavedAt = Time.timeAsDouble;
                inventory.Select(2);
                await WaitUntil(() => SessionContext.Character.selected == 2, 55, "Timed autosave did not update the hero.");
                double interval = Time.timeAsDouble - restSavedAt;
                Require(interval >= 118 && interval <= 123 && ReadHero(character.id).selected == 2,
                    $"Timed autosave must persist the hero after 120 seconds of play. Observed {interval:F2}.");
                Debug.Log("WINDOWS SMOKE: Timed autosave passed.");
            }
            finally { Time.timeScale = normalSpeed; }
        }

        private async Awaitable CheckDeaths()
        {
            var inventory = FindAnyObjectByType<PlayerInventory>();
            var dirt = inventory.Catalog.Find("dirt");
            inventory.Model.Remove(dirt, inventory.Model.Count(dirt));
            Require(inventory.Model.Add(dirt, 5) == 0, "Could not supply the normal death fixture.");
            var vitals = KillHero(inventory, 6.5f);
            Require(vitals.Health == 100 && vitals.transform.position == new Vector3(0.5f, 4, 0) &&
                inventory.Model.Count(dirt) == 2 && SessionContext.Active,
                "Normal death must respawn the hero with half the starting material stack dropped.");
            Require(FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(true), "Could not save the respawned hero.");
            await WaitForScene("Menu");
            var normalSave = ReadHero(character.id);
            Require(!normalSave.dead && normalSave.health == 100 && normalSave.slots.Where(slot => slot.id == dirt.Id)
                .Sum(slot => slot.count) == 2 && SavedDropCount(dirt.Id) == 3,
                "Normal death must persist the surviving hero, remaining items, and dropped items.");
            Debug.Log("WINDOWS SMOKE: Normal death and save passed.");

            FindAnyObjectByType<FrontEndMenu>().Launch(worldProfile, hardcore);
            await WaitForScene("Game");
            inventory = FindAnyObjectByType<PlayerInventory>();
            var stone = inventory.Catalog.Find("stone");
            Require(inventory.Model.Add(stone, 5) == 0, "Could not supply the hardcore death fixture.");
            int expectedStoneDrops = SavedDropCount(stone.Id) + inventory.Model.Count(stone);
            KillHero(inventory, 8.5f);
            await WaitForScene("Menu");
            var dead = ReadHero(hardcore.id);
            Require(dead.dead && dead.slots.All(slot => slot.count == 0) && SavedDropCount(stone.Id) == expectedStoneDrops,
                "Hardcore death must commit permadeath, clear the backpack, and persist drops.");
            FindAnyObjectByType<FrontEndMenu>().Launch(worldProfile, dead);
            await Awaitable.NextFrameAsync();
            Require(SceneManager.GetActiveScene().name == "Menu" && !SessionContext.Active,
                "The lobby must prevent a dead hardcore hero from reentering the world.");
            Debug.Log("WINDOWS SMOKE: Hardcore permadeath passed.");
        }

        private static PlayerVitals KillHero(PlayerInventory inventory, float x)
        {
            var body = inventory.GetComponent<Rigidbody2D>();
            var position = new Vector3(x, 4, 0);
            inventory.transform.position = position;
            body.position = position;
            body.linearVelocity = Vector2.zero;
            var vitals = inventory.GetComponent<PlayerVitals>();
            vitals.Damage(10000);
            return vitals;
        }

        private static CharacterProfile ReadHero(string id) => ProfileLibrary.Load().characters.Find(hero => hero.id == id);

        private int SavedDropCount(string itemId) => SaveTransactionStore.Read(ProfileLibrary.Root).ReadWorld(worldProfile.id)
            .drops.Where(drop => drop.id == itemId).Sum(drop => drop.count);

        private static async Awaitable WaitForScene(string scene)
        {
            await WaitUntil(() => SceneManager.GetActiveScene().name == scene, 50, $"The {scene} scene did not load.");
            // SessionGameBootstrap restores the save after the scene starts.
            await Awaitable.NextFrameAsync();
            await Awaitable.NextFrameAsync();
        }

        private static async Awaitable WaitUntil(Func<bool> condition, double seconds, string message)
        {
            double timeout = Time.realtimeSinceStartupAsDouble + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartupAsDouble > timeout) throw new TimeoutException(message);
                await Awaitable.NextFrameAsync();
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Fail(Exception error)
        {
            Debug.LogError($"WINDOWS SMOKE: FAIL {error}");
            Application.Quit(1);
        }
    }
}
