using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrokenHeart.Combat;
using BrokenHeart.Crafting;
using BrokenHeart.Inventory;
using BrokenHeart.Player;
using BrokenHeart.Systems;
using BrokenHeart.Systems.Energy;
using BrokenHeart.UI;
using BrokenHeart.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BrokenHeart.Core.Profiles
{
    internal sealed class WindowsSmoke : MonoBehaviour
    {
        private static readonly Vector2Int ChestCell = new(-3, 2), FurnaceCell = new(2, 2), LampCell = new(18, 2);
        private static readonly Vector3 FirstPosition = new(-0.5f, 4, 0), SecondPosition = new(7.5f, 4, 0);
        private const float PartialSmelt = 1.5f, LampCharge = 6.25f;
        private WorldProfile worldProfile;
        private CharacterProfile character;
        private CharacterProfile companion;
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
                var secondHero = new CharacterProfile { name = "Smoke companion" };
                var hardcoreHero = new CharacterProfile { name = "Smoke hardcore hero", challenge = 5 };
                var library = ProfileLibrary.Load();
                library.worlds.Add(world);
                library.characters.Add(hero);
                library.characters.Add(secondHero);
                library.characters.Add(hardcoreHero);
                ProfileLibrary.Save(library);

                Application.runInBackground = true;
                var runner = new GameObject("Windows smoke test").AddComponent<WindowsSmoke>();
                runner.worldProfile = world;
                runner.character = hero;
                runner.companion = secondHero;
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

                CheckWardChance();
                await CheckSharedWorld(world, inventory);
                await CheckAutosaves();
                await CheckDeaths();
                Require(FindAnyObjectByType<FrontEndMenu>().Page == "main" && !SessionContext.Active,
                    "The game must return to the lobby after exit.");
                Debug.Log("WINDOWS SMOKE: PASS");
                Application.Quit(0);
            }
            catch (Exception error) { Fail(error); }
        }

        private static void CheckWardChance()
        {
            int admitted = 0;
            for (int seed = 0; seed < 512; seed++)
            {
                DemonEvent Trial(bool warded) => new DemonEventScheduler(seed).Choose(SanityStage.Manifestation,
                    true, false, false, true, 1, DemonEvent.Intruder, warded);
                Require(Trial(false) == DemonEvent.Intruder, "An unwarded research encounter should proceed.");
                if (Trial(true) == DemonEvent.Intruder) admitted++;
            }
            Require(admitted >= 200 && admitted <= 312, "A ward must repel about half the visitors and still allow encounters.");
            Debug.Log($"WINDOWS SMOKE: Ward admitted {admitted} of 512 eligible invasions.");
        }

        private async Awaitable CheckSharedWorld(BlockWorld world, PlayerInventory inventory)
        {
            var cell = new Vector2Int(12, world.Generator.Surface(12) + 4);
            string block = world.Store.Get(cell) == null ? "dirt" : null;
            Require(world.Store.Set(cell, block), "Could not edit the smoke test world.");
            SetUpWorkshop(world, inventory.Catalog);

            int selected = inventory.HotbarSize - 1;
            int backpackSlot = PlayerInventory.EquipmentCount - PlayerInventory.BackpackCount;
            var backpack = inventory.Catalog.Find("trail_backpack");
            Require(inventory.Model.Add(backpack, 1) == 0, "Could not supply the first hero's backpack.");
            int packIndex = Enumerable.Range(0, inventory.Model.Capacity).First(index => inventory.Model[index].Item == backpack);
            Require(inventory.Equip(packIndex, backpackSlot), "Could not equip the first hero's backpack.");
            var copper = inventory.Catalog.Find("copper_ingot");
            inventory.Model.SetSlot(inventory.Model.Capacity - 1, new ItemStack(copper, 4));
            inventory.Select(selected);
            var mind = inventory.GetComponent<SanitySystem>();
            var notes = inventory.GetComponent<MemorySystem>();
            notes.Record(Discovery.Observe, cell);
            notes.Record(Discovery.Examine, cell);
            notes.Record(Discovery.EndureDarkness, cell);
            mind.Restore(30, 0, false);
            mind.Adjust(1);
            MoveHero(inventory, FirstPosition);
            var save = world.GetComponent<SaveSystem>();
            Require(FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(true), save.Status);
            await WaitForScene("Menu");
            Require(!SessionContext.Active, "Save and exit must clear the session.");

            FindAnyObjectByType<FrontEndMenu>().Launch(worldProfile, companion);
            await WaitForScene("Game");
            world = FindAnyObjectByType<BlockWorld>();
            inventory = FindAnyObjectByType<PlayerInventory>();
            save = world.GetComponent<SaveSystem>();
            Require(save.Status == "Save loaded" && world.Store.Get(cell) == block &&
                inventory.Model.Count(inventory.Catalog.Find("copper_ingot")) == 0 &&
                !inventory.GetComponent<MemorySystem>().HasKnowledge("lore.outline") &&
                inventory.GetComponent<MemorySystem>().TrailStep == 0 &&
                inventory.Model.Capacity == inventory.BaseCapacity && inventory.GetComponent<SanitySystem>().Stage == SanityStage.Calm &&
                Mathf.Approximately(inventory.transform.position.x, 0.5f),
                "A new hero must see the world edit and receive an independent inventory and spawn.");
            // Reload within one frame so simulation cannot advance the furnace or consume charge before inspection.
            Require(save.Load(), save.Status);
            CheckRestoredWorkshop(world, inventory.Catalog);

            Require(inventory.Model.Add(inventory.Catalog.Find("iron_ingot"), 2) == 0,
                "Could not supply the second hero's items.");
            inventory.Select(3);
            MoveHero(inventory, SecondPosition);
            Require(FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(true), save.Status);
            await WaitForScene("Menu");
            var journeys = ProfileLibrary.Load().journeys;
            Require(journeys.Exists(journey => journey.worldId == worldProfile.id &&
                    journey.characterId == character.id && journey.position == FirstPosition) &&
                journeys.Exists(journey => journey.worldId == worldProfile.id &&
                    journey.characterId == companion.id && journey.position == SecondPosition),
                "The same world must remember both heroes' positions independently.");

            FindAnyObjectByType<FrontEndMenu>().Launch(worldProfile, companion);
            await WaitForScene("Game");
            inventory = FindAnyObjectByType<PlayerInventory>();
            Require(FindAnyObjectByType<SaveSystem>().Status == "Save loaded" &&
                inventory.Model.Count(inventory.Catalog.Find("iron_ingot")) == 2 &&
                inventory.Model.Count(inventory.Catalog.Find("copper_ingot")) == 0 &&
                inventory.SelectedIndex == 3 && Mathf.Approximately(inventory.transform.position.x, SecondPosition.x),
                "Reopening the second hero must restore only that hero's items and location.");
            Require(FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(false), "Could not exit the second hero's session.");
            await WaitForScene("Menu");

            var otherWorld = new WorldProfile { name = "Second research site", seed = "150", difficulty = 2 };
            var library = ProfileLibrary.Load();
            library.worlds.Add(otherWorld);
            ProfileLibrary.Save(library);
            FindAnyObjectByType<FrontEndMenu>().Launch(otherWorld, character);
            await WaitForScene("Game");
            var otherNotes = FindAnyObjectByType<MemorySystem>();
            Require(otherNotes.HasKnowledge("lore.darkness") && otherNotes.TrailStep == 0,
                "A returning hero keeps research in a new world and starts a separate trail.");
            Require(FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(true), "Could not save the other world.");
            await WaitForScene("Menu");
            FindAnyObjectByType<FrontEndMenu>().Launch(worldProfile, character);
            await WaitForScene("Game");
            inventory = FindAnyObjectByType<PlayerInventory>();
            var overflowSlot = inventory.Model[inventory.Model.Capacity - 1];
            Require(FindAnyObjectByType<SaveSystem>().Status == "Save loaded" &&
                FindAnyObjectByType<BlockWorld>().Store.Get(cell) == block && inventory.SelectedIndex == selected &&
                Mathf.Approximately(inventory.transform.position.x, FirstPosition.x) &&
                inventory.GetComponent<MemorySystem>().TrailStep == 2 && inventory.GetComponent<MemorySystem>().HasKnowledge("lore.darkness") &&
                inventory.GetComponent<SanitySystem>().Stage == SanityStage.Pursued &&
                Mathf.Approximately(inventory.GetComponent<SanitySystem>().Value, 31) &&
                inventory.Equipped(backpackSlot).Item == inventory.Catalog.Find("trail_backpack") &&
                overflowSlot.Item == inventory.Catalog.Find("copper_ingot") && overflowSlot.Count == 4 &&
                inventory.Model.Count(inventory.Catalog.Find("iron_ingot")) == 0,
                "Returning to the first hero must restore the shared edit and that hero's own backpack and position.");
            Debug.Log("WINDOWS SMOKE: Shared world and independent hero positions and backpacks passed.");
        }

        private static void SetUpWorkshop(BlockWorld world, ItemCatalog catalog)
        {
            var stations = world.GetComponent<WorkstationSystem>();
            var stone = catalog.Find("stone");
            PrepareSpace(ChestCell, false);
            PrepareSpace(FurnaceCell, true);
            PrepareSpace(LampCell, false);
            var chest = stations.Place(catalog.Find("chest"), ChestCell);
            var furnace = stations.Place(catalog.Find("furnace"), FurnaceCell);
            Require(chest != null && furnace != null, "Could not place a chest and furnace.");
            chest.Contents.SetSlot(chest.Contents.Capacity - 1, new ItemStack(stone, 3));
            furnace.Contents.SetSlot(Workstation.OreSlot, new ItemStack(catalog.Find("copper_ore"), 2));
            furnace.Contents.SetSlot(Workstation.FuelSlot, new ItemStack(catalog.Find("wood"), 1));
            stations.Tick(PartialSmelt);
            var energy = world.GetComponent<EnergyNetwork>();
            Require(energy.CanPlace(LampCell), "Could not place an isolated energy device.");
            energy.Spawn(catalog.Find("crystal_lamp"), world.Center(LampCell), LampCharge);

            void PrepareSpace(Vector2Int anchor, bool fullSizeFurnace)
            {
                foreach (var offset in Workstation.Footprint(fullSizeFurnace))
                {
                    var cell = anchor + offset;
                    world.Set(cell, null);
                    if (offset.y == 0) world.Set(cell + Vector2Int.down, stone.PlaceableBlock);
                }
            }
        }

        private static void CheckRestoredWorkshop(BlockWorld world, ItemCatalog catalog)
        {
            var stations = world.GetComponent<WorkstationSystem>();
            var chest = stations.At(ChestCell);
            var furnace = stations.At(FurnaceCell);
            Require(chest?.Kind == StationKind.Chest && furnace?.Kind == StationKind.Furnace,
                "Saving must preserve both workstations.");
            var chestSlot = chest.Contents[chest.Contents.Capacity - 1];
            var ore = furnace.Contents[Workstation.OreSlot];
            Require(chestSlot.Item == catalog.Find("stone") && chestSlot.Count == 3,
                "The restored chest must preserve its items and slot.");
            Require(furnace.FullSizeFurnace && ore.Item == catalog.Find("copper_ore") && ore.Count == 2 &&
                furnace.Contents[Workstation.FuelSlot].Empty && furnace.Contents[Workstation.OutputSlot].Empty &&
                Mathf.Approximately(furnace.SmeltSeconds, PartialSmelt) &&
                Mathf.Approximately(furnace.BurnRemaining, catalog.Find("wood").FuelSeconds - PartialSmelt),
                "The restored furnace must keep its ore, progress, and remaining burn time.");
            stations.Tick(Workstation.SecondsPerIngot - PartialSmelt);
            var product = furnace.Contents[Workstation.OutputSlot];
            Require(product.Item == catalog.Find("copper_ingot") && product.Count == 1,
                "The restored furnace must finish the partial smelt.");
            var lamp = world.GetComponent<EnergyNetwork>().At(LampCell);
            Require(lamp != null && lamp.Item == catalog.Find("crystal_lamp") &&
                Mathf.Approximately(lamp.Stored, LampCharge), "The restored energy device must keep its charge.");
            Debug.Log("WINDOWS SMOKE: Chest contents, partial smelting, and stored energy passed.");
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
            MoveHero(inventory, new Vector3(x, 4, 0));
            var vitals = inventory.GetComponent<PlayerVitals>();
            vitals.Damage(10000);
            return vitals;
        }

        private static void MoveHero(PlayerInventory inventory, Vector3 position)
        {
            var body = inventory.GetComponent<Rigidbody2D>();
            inventory.transform.position = position;
            body.position = position;
            body.linearVelocity = Vector2.zero;
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
