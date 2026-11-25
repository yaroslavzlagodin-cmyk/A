using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrokenHeart.Player;
using BrokenHeart.World;
using BrokenHeart.Inventory;
using BrokenHeart.Crafting;
using BrokenHeart.Systems;
using BrokenHeart.Systems.Energy;
using BrokenHeart.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace BrokenHeart.Editor
{
    // Small editor-only integration runner. No runtime test components or player-build dependencies.
    [InitializeOnLoad]
    public static class PrototypeChecks
    {
        private const string CommandPath = "Library/BrokenHeart.command";
        private const string ReportPath = "Logs/BrokenHeartChecks.txt";
        private static readonly List<string> results = new();
        private static Keyboard keyboard;
        private static Mouse mouse;
        private static PlayerMotor player;
        private static double nextStep;
        private static int step = -1;
        private static float startX, startY;
        private static int ledgeX;
        private const int LedgeWidth = 3;
        private static int expectedAutoPickup;
        private static bool running;

        static PrototypeChecks()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying && !running)
                    OnPlayMode(PlayModeStateChange.EnteredPlayMode);
            };
        }

        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(CommandPath))
            {
                string command;
                try { command = File.ReadAllText(CommandPath).Trim(); File.Delete(CommandPath); }
                catch (IOException) { return; } // The editor may poll while the command is being written.
                try
                {
                    if (command == "build")
                    {
                        if (EditorSceneManager.GetActiveScene().isDirty)
                            throw new InvalidOperationException("Save the current scene before unattended setup.");
                        PrototypeBuilder.Build();
                    }
                    else if (command == "check") Run();
                    else if (command == "preview") PrototypeBuilder.RefreshTerrain();
                    else if (command == "inventory") FoundationUpgrade.InventoryStage();
                    else if (command == "visualupgrade") VisualUpgrade.Apply();
                    else if (command == "refresh") AssetDatabase.Refresh();
                    else if (command == "crafting") FoundationUpgrade.CraftingStage();
                    else if (command == "world") FoundationUpgrade.WorldStage();
                    else if (command == "energy") FoundationUpgrade.EnergyStage();
                    else if (command == "living") FoundationUpgrade.LivingStage();
                    else if (command == "sanity") FoundationUpgrade.SanityStage();
                    else if (command == "terrain") { EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath); PrototypeBuilder.RefreshTerrain(); }
                    else if (command == "menu") MenuSetup.Build();
                    else if (command == "menucheck") MenuChecks.Run();
                }
                catch (Exception error) { File.WriteAllText(ReportPath, "FAIL: " + error); Debug.LogException(error); }
            }
            if (!running || !EditorApplication.isPlaying || step < 0 || Time.time < nextStep) return;
            try { Tick(); }
            catch (Exception error) { results.Add("FAIL: " + error); Finish(); }
        }

        [MenuItem("Broken Heart/Run Prototype Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            results.Clear();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, "RUNNING\n");
            try
            {
                var bounds = new RectInt(-16, -8, 32, 24);
                var first = new BlockStore(bounds);
                var second = new BlockStore(bounds);
                TestTerrain.Fill(first, bounds, 42, "grass", "dirt", "stone");
                TestTerrain.Fill(second, bounds, 42, "grass", "dirt", "stone");
                foreach (var cell in bounds.allPositionsWithin)
                    if (first.Get(cell) != second.Get(cell)) throw new Exception("Seed is not deterministic.");
                Check(true, "Same seed generates identical block IDs including negative coordinates.");
                Check(!first.Set(new Vector2Int(100, 0), "stone"), "World bounds reject edits.");
                int events = 0;
                first.Changed += (_, _) => events++;
                Check(first.Set(Vector2Int.zero, null) && first.Get(Vector2Int.zero) == null,
                    "Mining clears logical data.");
                Check(!first.Set(Vector2Int.zero, null) && events == 1, "No-op edits do not emit change events.");
                EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
                var terrain = Object.FindAnyObjectByType<BlockWorld>();
                CheckInventoryModel();
                Check(terrain != null && terrain.Map != null && terrain.Palette.Length >= 3,
                    "World has a Tilemap and terrain palette.");
                foreach (var block in terrain.Palette)
                {
                    Check(block != null && block.Tile != null && block.Sprite != null, "Block resource and sprite references are assigned.");
                    var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(block.Sprite));
                    Check(Mathf.Approximately(importer.spritePixelsPerUnit, block.Sprite.rect.width) && importer.filterMode == FilterMode.Point &&
                        importer.textureCompression == TextureImporterCompression.Uncompressed && !importer.mipmapEnabled,
                        "Block sprite uses its native width per unit, Point filtering, and no compression or mipmaps.");
                }
                foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0,
                            "No missing scripts: " + transform.name);
                SessionState.SetBool("BrokenHeart.Checks", true);
                SessionState.SetString("BrokenHeart.Results", string.Join("\n", results));
                EditorSceneManager.playModeStartScene = null;
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { results.Add("FAIL: " + error); Finish(); }
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && !running && SessionState.GetBool("BrokenHeart.Checks", false))
            {
                results.Clear();
                results.Add(SessionState.GetString("BrokenHeart.Results", ""));
                running = true;
                Application.runInBackground = true;
                player = Object.FindAnyObjectByType<PlayerMotor>();
                keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                mouse = InputSystem.AddDevice<Mouse>();
                mouse.MakeCurrent();
                step = 0;
                nextStep = Time.time + 2;
                Application.logMessageReceived += CaptureError;
            }
            if (state == PlayModeStateChange.ExitingPlayMode && running)
            {
                results.Add("FAIL: Play mode interrupted before completion.");
                Finish();
            }
        }

        private static void CaptureError(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                results.Add("FAIL runtime: " + condition + "\n" + stack);
        }

        private static void Tick()
        {
            if (step == 0)
            {
                Check(player != null && player.Grounded, "Player falls and lands on Tilemap ground.");
                var inventory=player.GetComponent<PlayerInventory>();
                expectedAutoPickup=inventory.Model.Count(inventory.Catalog.Find("grass"))+1;
                Object.FindAnyObjectByType<DropSpawner>().Spawn(inventory.Catalog.Find("grass"),1,player.transform.position+Vector3.right*2,player.GetComponent<Collider2D>());
                startX = player.transform.position.x;
                InputSystem.QueueDeltaStateEvent(mouse.scroll, Vector2.up);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
                nextStep = Time.time + 0.6;
                step++;
            }
            else if (step == 1)
            {
                Check(player.transform.position.x > startX + 1, "D input moves Rigidbody2D right.");
                var inventory=player.GetComponent<PlayerInventory>();
                Check(inventory.SelectedIndex == inventory.HotbarSize - 1, "Scrolling up wraps from hotbar slot 1 to slot 9.");
                Check(inventory.Model.Count(inventory.Catalog.Find("grass"))==expectedAutoPickup,"Walking over a drop triggers automatic physical pickup.");
                startY = player.transform.position.y;
                var world = Object.FindAnyObjectByType<BlockWorld>();
                ledgeX = world.CellAt(player.transform.position).x + 2;
                SetTestLedge(world, inventory.Catalog.Find("stone").PlaceableBlock);
                InputSystem.QueueDeltaStateEvent(mouse.scroll, Vector2.down);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.Space));
                nextStep = Time.time + 0.18;
                step++;
            }
            else if (step == 2)
            {
                Check(player.GetComponent<PlayerInventory>().SelectedIndex == 0, "Scrolling down wraps back to hotbar slot 1.");
                Check(player.transform.position.y > startY + 0.3f, "Space input jumps from ground.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
                nextStep = Time.time + 0.45;
                step++;
            }
            else if (step == 3)
            {
                Check(player.Grounded && Mathf.Abs(player.transform.position.y - startY - 2) < 0.08f,
                    "Player lands on a two-block ledge using the normal jump.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                SetTestLedge(Object.FindAnyObjectByType<BlockWorld>(), null);
                player.GetComponent<PlayerInventory>().SetMapOpen(true);
                InputSystem.QueueDeltaStateEvent(mouse.scroll, Vector2.up);
                nextStep = Time.time + 0.8;
                step++;
            }
            else if (step == 4)
            {
                Check(player.Grounded, "Player lands after jump.");
                var world = Object.FindAnyObjectByType<BlockWorld>();
                var interaction = player.GetComponent<BlockInteraction>();
                var here = world.CellAt(player.transform.position);
                Check(!interaction.TryPlace(here), "Placement inside player is rejected.");
                Check(!interaction.TryPlace(here + Vector2Int.right * 10), "Distant placement is rejected.");
                var target = new Vector2Int(here.x + 2, 0);
                var inventory = player.GetComponent<PlayerInventory>();
                Check(inventory.SelectedIndex == 0, "An open map keeps wheel input away from the hotbar.");
                inventory.SetMapOpen(false);
                int beforeMining = inventory.Model.Count(inventory.Selected.Item);
                Check(!interaction.Mine(target, 0.01f) && world.Get(target) != null, "Hardness prevents instant mining.");
                Check(interaction.Mine(target, 2) && world.Get(target) == null &&
                    world.Map.GetTile((Vector3Int)target) == null, "Mining updates logical data and Tilemap.");
                var drop = Object.FindAnyObjectByType<ItemDrop>();
                Check(drop != null && drop.Count == 1, "Mining spawns one collectible item.");
                Check(drop.TryCollect(inventory) == 1 && inventory.Model.Count(inventory.Selected.Item) == beforeMining + 1,
                    "Pickup adds the drop exactly once.");
                Check(drop.TryCollect(inventory) == 0, "Collected drop cannot duplicate items.");
                Check(interaction.TryPlace(target) && world.Get(target) == interaction.Selected &&
                    world.Map.GetTile((Vector3Int)target) != null, "Placement updates logical data and Tilemap.");
                Check(inventory.Model.Count(inventory.Selected.Item) == beforeMining, "Placement consumes one inventory item.");
                var collider = world.Map.GetComponent<TilemapCollider2D>();
                collider.ProcessTilemapChanges();
                Check(Physics2D.OverlapPoint(world.Center(target), 1) != null, "Placed block has a physics collider.");
                Check(!interaction.TryPlace(target), "Occupied cells reject replacement.");
                Check(!interaction.Mine(new Vector2Int(40, 0), 10), "Distant mining is rejected.");
                CheckQuickPlayerTransfer(inventory);
                CheckWorkbench();
                CheckCrafting();
                CheckWorkshop();
                CheckWholeTree();
                CheckWorldSave();
                CheckEnergy();
                CheckLiving();
                CheckSanity();
                step = 5; nextStep = Time.time + 0.4f;
            }
            else if (step == 5)
            {
                var map = Object.FindAnyObjectByType<BlockWorld>().Map;
                var underground = new Vector3Int(0, -4, 0);
                Check(Object.FindAnyObjectByType<SubsurfaceShadows>() == null &&
                    map.HasTile(underground) && map.GetColor(underground) == Color.white,
                    "Terrain has no shadow mask or depth tint while terrain shadows are paused.");
                CapturePreview();
                var clock = Object.FindAnyObjectByType<DayNightSystem>();
                if (clock == null) { Finish(); return; }
                clock.SetHour(0); step=6; nextStep=Time.time+0.4f;
            }
            else if (step == 6)
            {
                CapturePreview("Logs/NightPreview.png");
                Object.FindAnyObjectByType<DayNightSystem>().SetHour(12);
                Object.FindAnyObjectByType<WorldWeather>().SetWeather(WeatherKind.Storm);
                step = 7;
                nextStep = Time.time + 3.4f;
            }
            else if (step == 7)
            {
                CapturePreview("Logs/StormPreview.png");
                var world = Object.FindAnyObjectByType<BlockWorld>();
                Vector2Int cave = FindCave(world);
                var body = player.GetComponent<Rigidbody2D>();
                body.position = world.Center(cave) + Vector3.up * 0.3f;
                body.linearVelocity = Vector2.zero;
                step = 8;
                nextStep = Time.time + 1.3f;
            }
            else
            {
                var world = Object.FindAnyObjectByType<BlockWorld>();
                Check(world.DepthAt(world.CellAt(player.transform.position)) >= 18 &&
                    Object.FindAnyObjectByType<WorldWeather>().RainRate == 0,
                    "A deep cave shelters the player from storm particles.");
                CapturePreview("Logs/CavePreview.png");
                Finish();
            }
        }

        private static void CheckQuickPlayerTransfer(PlayerInventory inventory)
        {
            var original = inventory.Model.Copy();
            var stone = inventory.Catalog.Find("stone");
            var shield = inventory.Catalog.Find("wooden_shield");
            int storage = inventory.HotbarSize;
            var fixture = new ItemStack[original.Capacity];
            fixture[0] = new ItemStack(stone, 10);
            fixture[storage] = new ItemStack(stone, 3);
            fixture[storage + 1] = new ItemStack(shield, 1);
            inventory.Model.Restore(fixture);
            Check(inventory.QuickTransfer(0) && inventory.Model[0].Empty && inventory.Model[storage].Count == 13,
                "Quick transfer from the hotbar merges into player storage.");
            inventory.SetOpen(true);
            Check(inventory.QuickTransfer(storage) && inventory.Model[storage].Empty && inventory.Model[0].Count == 13,
                "Quick transfer from storage returns a stack to the hotbar.");
            Check(inventory.QuickTransfer(storage + 1) && inventory.Equipped(5).Item == shield &&
                inventory.QuickUnequip(5) && inventory.Equipped(5).Empty && inventory.Model.Count(shield) == 1,
                "Quick transfer equips and removes equipment through the normal slots.");
            inventory.SetOpen(false);
            inventory.Model.ReplaceWith(original);
        }

        private static void SetTestLedge(BlockWorld world, BlockData block)
        {
            for (int x = ledgeX; x < ledgeX + LedgeWidth; x++)
                for (int y = 1; y <= 2; y++)
                    world.Set(new Vector2Int(x, y), block);
            world.Map.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            results.Add("PASS: " + message);
        }

        private static void CheckInventoryModel()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/Game/Data/ItemCatalog.asset");
            catalog.Validate();
            var grass = catalog.Find("grass");
            var dirt = catalog.Find("dirt");
            var model = new InventoryModel(3);
            Check(model.Add(grass, 105) == 0 && model[0].Count == 99 && model[1].Count == 6, "Stacks split at MaxStack.");
            Check(model.Move(1, 2, 3) && model[1].Count == 3 && model[2].Count == 3, "Split transfer conserves items.");
            Check(model.Move(2, 1) && model[1].Count == 6 && model[2].Empty, "Matching stacks merge.");
            model.Add(dirt, 10);
            Check(model.Move(0, 2) && model[0].Item == dirt && model[2].Item == grass, "Different items swap slots.");
            Check(model.Add(grass, 200) == 107 && model.Count(grass) == 198, "Full inventory returns overflow without item loss.");
            Check(!model.Remove(dirt, 11) && model.Count(dirt) == 10, "Insufficient removal is atomic.");
            Check(!model.Move(-1, 0) && !model.RemoveAt(99, 1), "Invalid slots are rejected.");

            var storage = new InventoryModel(45);
            var stacks = new ItemStack[storage.Capacity];
            stacks[0] = new ItemStack(grass, 1);
            stacks[10] = new ItemStack(grass, 67);
            stacks[13] = new ItemStack(dirt, 4);
            stacks[44] = new ItemStack(grass, 40);
            storage.Restore(stacks);
            storage.SortFrom(9);
            Check(storage[0].Count == 1 && storage[9].Item == dirt && storage[10].Count == 99 &&
                  storage[11].Count == 8 && storage[44].Empty, "Sorting merges backpack stacks and keeps the hotbar in place.");

            var crowded = new InventoryModel(2);
            crowded.Add(grass, 98);
            crowded.Add(dirt, 99);
            Check(storage.TransferStackTo(11, crowded) == 1 && storage[11].Count == 7 && crowded[0].Count == 99 &&
                  storage.TransferStackTo(11, crowded) == 0, "Quick transfers keep any overflow in its original slot.");
            var chest = new InventoryModel(4);
            var stone = catalog.Find("stone");
            chest.Add(grass, 1);
            chest.Add(stone, 12);
            var expanded = new InventoryModel(45);
            var expandedSlots = new ItemStack[expanded.Capacity];
            expandedSlots[44] = new ItemStack(stone, 1);
            expanded.Restore(expandedSlots);
            Check(chest.TransferMatchingTo(expanded) == 12 && expanded[44].Count == 13 &&
                  chest.Count(grass) == 1, "Taking matches considers even the last backpack row and leaves other types in the chest.");
            Check(expanded.TransferMatchingTo(chest) == 0 && expanded.Count(stone) == 13,
                "Depositing matches only uses types that are already in the chest.");
        }

        private static void CheckCrafting()
        {
            var crafting = player.GetComponent<CraftingSystem>();
            if (crafting == null) return;
            var inventory = player.GetComponent<PlayerInventory>();
            var memory = player.GetComponent<MemorySystem>();
            var recipe = crafting.Recipes[0];
            var primary = recipe.Ingredients[0];
            var secondary = recipe.Ingredients[1];
            inventory.Model.Remove(secondary.item, inventory.Model.Count(secondary.item));
            memory.Restore(Array.Empty<string>());
            Check(!crafting.Craft(recipe, out _), "A recipe with an undiscovered ingredient cannot be crafted.");
            int notifications = 0;
            memory.Remembered += _ => notifications++;
            inventory.Collect(primary.item, primary.count);
            Check(memory.CanView(recipe) && !memory.Knows(recipe), "One material reveals a recipe without completing its memory.");
            inventory.Collect(secondary.item, secondary.count);
            Check(memory.Knows(recipe) && notifications > 0, "Picking up the complete material set restores the recipe.");
            int previousNotifications = notifications;
            inventory.Collect(primary.item, 1);
            Check(notifications == previousNotifications, "Already known memories do not notify again.");
            int stoneBefore = inventory.Model.Count(primary.item);
            Check(crafting.Craft(recipe, out _) && inventory.Model.Count(recipe.Output) == recipe.OutputCount &&
                inventory.Model.Count(primary.item) == stoneBefore - primary.count, "Crafting consumes costs and produces output atomically.");
            inventory.Model.Remove(primary.item, inventory.Model.Count(primary.item));
            int dirtBefore = inventory.Model.Count(inventory.Catalog.Find("dirt"));
            Check(!crafting.Craft(recipe, out _) && inventory.Model.Count(inventory.Catalog.Find("dirt")) == dirtBefore,
                "Failed crafting does not consume other ingredients.");
            var original=inventory.Model.Copy();
            var full=new ItemStack[inventory.Model.Capacity];
            for(int i=0;i<full.Length;i++)full[i]=new ItemStack(inventory.Catalog.Find("grass"),99);
            full[0]=new ItemStack(primary.item,99); full[1]=new ItemStack(secondary.item,99);
            inventory.Model.Restore(full);
            Check(!crafting.Craft(recipe,out _) && inventory.Model[0].Count==99 && inventory.Model[1].Count==99,
                "No-space crafting preserves every ingredient.");
            var overflow=Object.FindAnyObjectByType<DropSpawner>().Spawn(inventory.Catalog.Find("stone"),2,player.transform.position+Vector3.up*4);
            Check(overflow.TryCollect(inventory)==0 && overflow.Count==2,"Full backpack leaves dropped items in the world.");
            overflow.gameObject.SetActive(false);Object.Destroy(overflow.gameObject);
            inventory.Model.ReplaceWith(original);
        }

        private static void CheckWorkbench()
        {
            var inventory = player.GetComponent<PlayerInventory>();
            var crafting = player.GetComponent<CraftingSystem>();
            Check(crafting.Recipes.Count(recipe => recipe.ByHand) == 2 &&
                !crafting.CanCraft(crafting.Recipes.First(recipe => recipe.Id == "furnace"), out string reason) &&
                reason == "Requires a workbench", "The hand menu offers only planks and the workbench.");
            var planks = crafting.Recipes.Single(recipe => recipe.Id == "oak_planks");
            inventory.Collect(inventory.Catalog.Find("wood"), 2);
            Check(crafting.Craft(planks, out _) && crafting.Craft(planks, out _), "The player can make planks by hand.");
            var bench = crafting.Recipes.Single(recipe => recipe.Id == "workbench");
            Check(crafting.Craft(bench, out _), "The player can build a workbench by hand.");
            var station = PlaceStation(bench.Output);
            Check(Object.FindAnyObjectByType<WorkstationSystem>().Open(station.Cell) && crafting.AtWorkbench,
                "Opening a placed workbench unlocks its crafting menu.");
        }

        private static Workstation PlaceStation(ItemData item)
        {
            var inventory = player.GetComponent<PlayerInventory>();
            var world = Object.FindAnyObjectByType<BlockWorld>();
            var interaction = player.GetComponent<BlockInteraction>();
            var stations = world.GetComponent<WorkstationSystem>();
            int slot = Enumerable.Range(0, inventory.Model.Capacity).First(index => inventory.Model[index].Item == item);
            if (slot != 0) inventory.Model.Move(slot, 0);
            inventory.Select(0);
            int x = world.CellAt(player.transform.position).x;
            for (int offset = 2; offset <= 4; offset++)
                foreach (int sign in new[] { 1, -1 })
                {
                    var cell = new Vector2Int(x + offset * sign, 1);
                    if (!stations.CanPlace(item, cell)) continue;
                    stations.ShowPreview(item, cell);
                    var preview = stations.transform.Find("Workstation Placement Preview").GetComponent<SpriteRenderer>();
                    Check(preview.enabled && preview.sprite == item.Icon && preview.color.g > preview.color.r,
                        $"A valid placement previews {item.Id}.");
                    if (item.Station == StationKind.Furnace) CapturePreview("Logs/FurnacePlacementPreview.png");
                    stations.HidePreview();
                    if (interaction.TryPlace(cell))
                    {
                        var station = stations.At(cell);
                        var visual = stations.GetComponentsInChildren<SpriteRenderer>()
                            .First(renderer => renderer != preview && renderer.sprite == item.Icon);
                        Check(visual.bounds.size == preview.bounds.size && visual.transform.position == preview.transform.position,
                            $"The {item.Id} preview matches its placed sprite.");
                        return station;
                    }
                }
            throw new Exception("No safe place for " + item.Id);
        }

        private static void CheckWorkshop()
        {
            var inventory = player.GetComponent<PlayerInventory>();
            var world = Object.FindAnyObjectByType<BlockWorld>();
            var stations = world.GetComponent<WorkstationSystem>();
            var bench = stations.Stations.Single(station => station.Kind == StationKind.Workbench);
            var crafting = player.GetComponent<CraftingSystem>();
            foreach (string id in new[] { "furnace", "chest" })
            {
                var recipe = crafting.Recipes.Single(candidate => candidate.Id == id);
                foreach (var cost in recipe.Ingredients) inventory.Collect(cost.item, cost.count);
                Check(crafting.Craft(recipe, out _), "The workbench crafts " + id + ".");
                PlaceStation(recipe.Output);
            }
            var unsupported = new Vector2Int(world.CellAt(player.transform.position).x + 2, 3);
            foreach (var station in stations.Stations)
            {
                stations.ShowPreview(station.Item, unsupported);
                var preview = stations.transform.Find("Workstation Placement Preview").GetComponent<SpriteRenderer>();
                Check(preview.color.r > preview.color.g && stations.Place(station.Item, unsupported) == null,
                    $"{station.Item.Id} has a red preview and cannot be placed without support.");
            }
            stations.HidePreview();
            inventory.SetOpen(false);
            var interaction = player.GetComponent<BlockInteraction>();
            var oreCell = new Vector2Int(world.CellAt(player.transform.position).x, -1);
            var oreBlock = inventory.Catalog.Find("copper").PlaceableBlock;
            world.Set(oreCell, oreBlock);
            Check(interaction.Mine(oreCell, 4), "A copper deposit can be mined through normal block interaction.");
            var raw = inventory.Catalog.Find("copper_ore");
            var drop = Object.FindObjectsByType<ItemDrop>().Single(candidate => candidate.Item == raw && candidate.Count > 0);
            Check(drop.TryCollect(inventory) == 1, "Mining copper drops raw ore instead of a placeable ore block.");
            inventory.Collect(raw, 1);
            var fuel = inventory.Catalog.Find("wood");
            inventory.Collect(fuel, 2);
            var furnace = stations.Stations.Single(station => station.Kind == StationKind.Furnace);
            Check(furnace.FullSizeFurnace && Workstation.Footprint(true).All(offset => stations.At(furnace.Cell + offset) == furnace) &&
                stations.At(furnace.Cell + new Vector2Int(2, 1)) != furnace,
                "The furnace occupies a three-block base and a two-block upper row.");
            int Find(ItemData item) => Enumerable.Range(0, inventory.Model.Capacity).First(index => inventory.Model[index].Item == item);
            Check(stations.Open(furnace.Cell + Vector2Int.up) &&
                !stations.Transfer(false, Find(fuel), true, Workstation.OutputSlot, 1) &&
                stations.QuickTransfer(false, Find(raw)) == 2 && stations.QuickTransfer(false, Find(fuel)) > 0 &&
                furnace.Contents[Workstation.OreSlot].Item == raw && furnace.Contents[Workstation.FuelSlot].Item == fuel,
                "Quick transfer sends ore and fuel to the correct furnace slots and protects the output.");
            stations.Tick(1.5f);
            var save = world.GetComponent<SaveSystem>();
            var time = save.Capture();
            save.Restore(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(time)));
            furnace = stations.At(furnace.Cell);
            Check(furnace.Contents[Workstation.OutputSlot].Empty && Mathf.Approximately(furnace.Progress, 0.5f),
                "A saved furnace resumes a partial smelt.");
            stations.Tick(4.5f);
            var ingot = inventory.Catalog.Find("copper_ingot");
            Check(furnace.Contents[Workstation.OutputSlot].Item == ingot &&
                furnace.Contents[Workstation.OutputSlot].Count == 2 && furnace.Contents[Workstation.OreSlot].Empty,
                "Fuel and ore produce two copper ingots over time.");
            var chest = stations.Stations.Single(station => station.Kind == StationKind.Chest);
            Check(stations.Open(furnace.Cell) && stations.QuickTransfer(true, Workstation.OutputSlot) == 2 &&
                stations.Open(chest.Cell) && stations.Transfer(false, Find(ingot), true, 17, 1),
                "Quick transfer collects furnace output, and ingots can then be placed in a chest.");
            save.Restore(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save.Capture())));
            Check(stations.At(chest.Cell).Contents[17].Item == ingot && stations.At(chest.Cell).Contents[17].Count == 1,
                "A chest preserves its inventory after a JSON roundtrip.");
            Check(stations.Open(chest.Cell), "The restored chest opens.");
            chest = stations.At(chest.Cell);
            chest.Contents.SortFrom(0);
            Check(chest.Contents[0].Item == ingot && chest.Contents[17].Empty &&
                  stations.TransferMatchingChestItems(false) == 1 && chest.Contents[0].Count == 2,
                "A chest can sort its slots and deposit only items that match its contents.");
            Check(stations.QuickTransfer(true, 0) == 2 && chest.Contents[0].Empty &&
                  stations.TransferMatchingChestItems(true) == 0 && inventory.Model.Count(ingot) == 2,
                "Opened chest slots support quick transfer back to the player's storage.");
            var restoreStations = save.Capture();
            var farSupport = furnace.Cell + new Vector2Int(2, -1);
            world.Set(farSupport, null);
            Check(stations.At(furnace.Cell) == null && !stations.CanPlace(furnace.Item, furnace.Cell),
                "A missing block under the far side detaches the furnace and prevents replacing it.");
            save.Restore(restoreStations);
            Check(stations.At(furnace.Cell + Vector2Int.one)?.FullSizeFurnace == true,
                "The expanded furnace footprint survives loading.");
            Check(stations.Open(bench.Cell), "The saved workbench remains usable.");
            var tool = crafting.Recipes.Single(recipe => recipe.Id == "copper_pickaxe");
            foreach (var cost in tool.Ingredients) inventory.Collect(cost.item, cost.count);
            Check(crafting.Craft(tool, out _) && tool.Output.MiningPower > inventory.Catalog.Find("stone_pickaxe").MiningPower,
                "Copper ingots craft a faster pickaxe.");
            inventory.SetOpen(false);
        }

        private static void CheckWholeTree()
        {
            var world = Object.FindAnyObjectByType<BlockWorld>();
            var inventory = player.GetComponent<PlayerInventory>();
            var interaction = player.GetComponent<BlockInteraction>();
            var save = world.GetComponent<SaveSystem>();
            var original = save.Capture();
            var trunk = Enumerable.Range(18, 100)
                .Select(x => new Vector2Int(x, world.Generator.Surface(x) + 1))
                .First(cell => world.Generator.Generate(cell) == "wood" && world.Generator.TreeAt(cell).Count() > 7);
            var parts = world.Generator.TreeAt(trunk).ToArray();
            var previousForest = new TerrainGenerator(world.Seed, world.Generation, WorldGenerationData.CaveVersion);
            var currentForest = world.Generator;
            int CountTrees(TerrainGenerator terrain) => Enumerable.Range(-200, 401)
                .Count(x => terrain.Generate(new Vector2Int(x, terrain.Surface(x) + 1)) == "wood");
            Check(CountTrees(currentForest) > CountTrees(previousForest) &&
                parts.Max(part => part.Cell.y) - trunk.y >= 8,
                "New forests have more trees and taller crowns than the previous generation.");
            var point = world.Center(trunk) + Vector3.left * 1.7f;
            player.transform.position = point;
            player.GetComponent<Rigidbody2D>().position = point;
            world.RefreshVisible(true);
            Physics2D.SyncTransforms();
            var camera = Camera.main;
            var previousCamera = camera.transform.position;
            try
            {
                camera.transform.position = world.Center(trunk) + new Vector3(4, 7, previousCamera.z);
                CapturePreview("Logs/ForestPreview.png");
            }
            finally { camera.transform.position = previousCamera; }
            var axe = inventory.Catalog.Find("iron_axe");
            var loadout = Enumerable.Range(0, inventory.Model.Capacity).Select(index => inventory.Model[index]).ToArray();
            loadout[0] = new ItemStack(axe, 1);
            inventory.Model.Restore(loadout);
            inventory.Select(0);
            var placed = trunk + Vector2Int.left;
            world.Set(placed, inventory.Catalog.Find("wood").PlaceableBlock);
            float required = parts.Sum(part => world.Get(part.Cell).Hardness) * 0.15f / axe.MiningPower;
            Check(!interaction.Mine(trunk, required * 0.99f, true) && world.Get(trunk) != null,
                "Ctrl waits for fifteen percent of total tree hardness at the axe's speed.");
            Check(interaction.Mine(trunk, required * 0.02f, true) &&
                parts.All(part => world.Get(part.Cell) == null) && world.Get(placed)?.Id == "wood",
                "Ctrl fells the whole generated tree and preserves separately placed wood.");
            save.Restore(original);
        }

        private static void CheckWorldSave()
        {
            var world = Object.FindAnyObjectByType<BlockWorld>();
            if (world.Generation == null) return;
            var first = new ChunkBlockStore(world.Bounds, 16, new TerrainGenerator(1207, world.Generation));
            var second = new ChunkBlockStore(world.Bounds, 16, new TerrainGenerator(1207, world.Generation));
            var ids = new HashSet<string>();
            for (int x = -100; x < 100; x++) for (int y = -60; y < 30; y++)
            {
                var cell = new Vector2Int(x,y); string id = first.Get(cell); ids.Add(id);
                if (id != second.Get(cell)) throw new Exception("Chunk generation is not deterministic.");
            }
            Check(ids.Contains("wood") && ids.Contains("crystal") && ids.Contains("copper") && ids.Contains("iron"),
                "Seed generates varied forest resources and ores deterministically.");
            foreach(int testSeed in new[] {1207,48291,-718,66743})
            {
                var generator=new TerrainGenerator(testSeed,world.Generation);
                int openings = 0, undergroundAir = 0;
                for(int x=-256;x<256;x++)
                {
                    int surface=generator.Surface(x);
                    if (generator.Generate(new Vector2Int(x, surface)) == null) openings++;
                    for (int y = -96; y < surface - 8; y++)
                        if (generator.Generate(new Vector2Int(x, y)) == null) undergroundAir++;
                    if(Mathf.Abs(surface-generator.Surface(x+1))>3)throw new Exception("Abrupt terrain seam.");
                }
                Check(openings > 0 && undergroundAir > 1500 && undergroundAir < 18000 &&
                    generator.Generate(new Vector2Int(0, 0)) == "grass",
                    $"Seed {testSeed} has cave entrances, deep chambers and a solid spawn.");
            }
            Check(Object.FindObjectsByType<AudioSource>().Length==0,"Scene creates no sound sources.");
            Check(first.ChunkAt(new Vector2Int(-1,-17)) == new Vector2Int(-1,-2), "Negative coordinates map to correct chunks.");
            var edited = new Vector2Int(-17,-12); first.Set(edited, "grass"); first.Unload(first.ChunkAt(edited));
            Check(first.Get(edited) == "grass", "Chunk unload preserves edits.");
            var save = world.GetComponent<SaveSystem>();
            var snapshot = save.Capture(); string json = JsonUtility.ToJson(snapshot);
            Check(snapshot.explored.Length > 0 && !world.IsExplored(new Vector2Int(0, -20)) &&
                !world.IsExplored(new Vector2Int(world.Bounds.xMax - 2, 0)),
                "Initial exploration reveals nearby terrain and hides buried and distant cells.");
            string testPath=Path.GetFullPath("Logs/SaveRoundtrip.json");
            SaveFileStore.WriteAtomic(testPath,json);SaveFileStore.WriteAtomic(testPath,json);
            Check(SaveFileStore.Read(testPath)==json && File.Exists(testPath+".bak"),"Atomic save file write/read and backup work on disk.");
            var savedCell = new Vector2Int(3,-2); string original = world.Store.Get(savedCell);
            world.Set(savedCell, null);
            save.Restore(JsonUtility.FromJson<SaveData>(json));
            Check(world.Store.Get(savedCell) == original, "JSON save restores seed and block state.");
            Check(snapshot.explored.All(world.IsExplored), "JSON load preserves explored cells.");
            var legacy = JsonUtility.FromJson<SaveData>(json);
            legacy.generatorVersion = WorldGenerationData.LegacyVersion;
            legacy.explored = null;
            save.Restore(legacy);
            Check(world.Generator.Version == WorldGenerationData.LegacyVersion &&
                Enumerable.Range(0, 75).All(depth =>
                    world.Generator.Generate(new Vector2Int(25, world.Generator.Surface(25) - depth)) != null),
                "Version 2 saves retain their original solid terrain.");
            save.Restore(JsonUtility.FromJson<SaveData>(json));
            Check(world.VisibleChunks == 25, "Only a bounded 5x5 chunk window is rendered.");
            var bad = JsonUtility.FromJson<SaveData>(json); bad.slots[0].id = "missing_item";
            bool rejected = false;
            try { save.Restore(bad); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && world.Seed == snapshot.seed, "Invalid save is rejected before mutating world.");
        }

        private static Vector2Int FindCave(BlockWorld world)
        {
            var walls = world.Map.transform.Find("Background Walls").GetComponent<Tilemap>();
            var origin = world.CellAt(player.transform.position);
            for (int x = origin.x - 24; x <= origin.x + 24; x++)
            for (int depth = 19; depth <= 25; depth++)
            {
                var cell = new Vector2Int(x, world.Generator.Surface(x) - depth);
                if (world.Get(cell) != null || world.Get(cell + Vector2Int.up) != null ||
                    world.Get(cell + Vector2Int.down)?.Solid != true) continue;
                Check(walls.HasTile((Vector3Int)cell) && walls.GetComponent<TilemapCollider2D>() == null,
                    "Open cave cells have a background wall without a collider.");
                return cell;
            }
            throw new Exception("No walkable cave found in the loaded chunks.");
        }
        private static void CheckEnergy()
        {
            var world = Object.FindAnyObjectByType<BlockWorld>(); var network = world.GetComponent<EnergyNetwork>();
            if (network == null) return;
            network.Clear();
            var catalog = player.GetComponent<PlayerInventory>().Catalog;
            var generator = network.Spawn(catalog.Find("crystal_generator"),new Vector3(-6.5f,1.5f,0));
            var lamp = network.Spawn(catalog.Find("crystal_lamp"),new Vector3(-10.5f,1.5f,0));
            network.Tick(2);
            Check(lamp.Powered && Mathf.Abs(generator.Stored + lamp.Stored - 4) < 0.001f, "Connected generator transfers energy and consumer spends it without duplication.");
            var isolated = network.Spawn(catalog.Find("crystal_lamp"),new Vector3(30.5f,10.5f,0));
            network.Tick(1);
            Check(!isolated.Powered && isolated.Stored == 0, "Disconnected device receives no energy.");
            network.Remove(isolated,false);
            var save = world.GetComponent<SaveSystem>(); var snapshot = save.Capture();
            float energy = generator.Stored + lamp.Stored;
            save.Restore(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(snapshot)));
            float restored = 0; foreach (var device in network.Devices) restored += device.Stored;
            Check(network.Devices.Count == 2 && Mathf.Abs(restored-energy) < 0.001f, "Save restores devices and stored energy.");
        }

        private static void CheckLiving()
        {
            var clock=Object.FindAnyObjectByType<DayNightSystem>(); if(clock==null)return;
            var weather=Object.FindAnyObjectByType<WorldWeather>(); var enemies=Object.FindAnyObjectByType<EnemySystem>();
            weather.SetWeather(WeatherKind.Clear); clock.SetHour(12); float noon=clock.Intensity;
            clock.SetHour(0);
            Check(clock.IsNight && clock.Intensity < noon * 0.12f, "Night sharply reduces ambient light.");
            Check(!enemies.Eligible(enemies.Types[0], new Vector2(0, 1)) &&
                !enemies.Eligible(enemies.Types[1], new Vector2(0, -20)),
                "Forest and cave monsters are paused even in their usual spawn conditions.");
            Check(enemies.Spawn(enemies.Types[0], (Vector2)player.transform.position + Vector2.right * 2) == null &&
                enemies.Actors.Count == 0 && !enemies.TryAttack(Vector2.right),
                "Direct monster spawning and attacks are paused.");
            weather.SetWeather(WeatherKind.Storm);
            Check(weather.RainRate>0,"Storm enables rain particles.");
            weather.SetWeather(WeatherKind.Clear); Check(weather.RainRate==0,"Clear weather disables rain.");
            CheckArsenal();
            var vitals=player.GetComponent<PlayerVitals>(); vitals.Damage(8); Check(vitals.Health==92,"Damage reduces player health.");
            vitals.Restore(100); clock.SetHour(10);
        }

        private static void CheckArsenal()
        {
            var inventory = player.GetComponent<PlayerInventory>();
            var catalog = inventory.Catalog;
            var crafting = player.GetComponent<CraftingSystem>();
            var stations = Object.FindAnyObjectByType<WorkstationSystem>();
            stations.Open(stations.Stations.Single(station => station.Kind == StationKind.Workbench).Cell);
            foreach (string id in new[] { "stone_sword", "iron_sword", "iron_pickaxe", "stone_axe", "stone_shovel" })
            {
                var recipe = crafting.Recipes.Single(candidate => candidate.Id == id);
                foreach (var cost in recipe.Ingredients) inventory.Collect(cost.item, cost.count);
                Check(crafting.Craft(recipe, out _) && inventory.HasItem(catalog.Find(id)) && recipe.Output.Icon != null,
                    $"The {id} recipe creates its item with a sprite.");
            }
            inventory.SetOpen(false);
            var axe = catalog.Find("stone_axe");
            var pick = catalog.Find("iron_pickaxe");
            var shovel = catalog.Find("stone_shovel");
            var wood = catalog.Find("wood").PlaceableBlock;
            var stone = catalog.Find("stone").PlaceableBlock;
            var dirt = catalog.Find("dirt").PlaceableBlock;
            Check(axe.MiningPowerFor(wood) > 1 && axe.MiningPowerFor(stone) == 1 &&
                  pick.MiningPowerFor(stone) > 1 && pick.MiningPowerFor(dirt) == 1 &&
                  shovel.MiningPowerFor(dirt) > 1 && shovel.MiningPowerFor(wood) == 1,
                "Tools speed up only the materials they are designed for.");
        }

        private static void CheckSanity()
        {
            var sanity=player.GetComponent<SanitySystem>();if(sanity==null)return;
            sanity.Restore(100,0,false);sanity.Advance(100,false,false);
            Check(sanity.Value<100 && sanity.Value>98,"Only overdue wakefulness drains sanity.");
            sanity.Restore(50,100,false);sanity.Advance(10,false,true);float protectedValue=sanity.Value;
            sanity.Restore(50,100,false);sanity.Advance(10,false,false);
            Check(sanity.Value<protectedValue,"Crystal focus reduces sanity loss.");
            float before=sanity.Value;sanity.Advance(4,true,false);
            Check(sanity.Value>before && sanity.AwakeSeconds<110,"Rest restores sanity and reduces wakefulness.");
            int collapses=0;sanity.FirstCollapse+=()=>collapses++;
            sanity.Adjust(-1000);sanity.Adjust(-1000);
            Check(sanity.Value==0 && collapses==1,"Sanity clamps at zero and first-collapse fires once.");
            bool memory=false;foreach(var id in player.GetComponent<MemorySystem>().Unlocked)if(id=="memory.mind_protection")memory=true;
            Check(memory,"First collapse unlocks mind-protection memory.");
            var eyes=Object.FindAnyObjectByType<SanityVisuals>();eyes.ShowEyes(player.transform.position+Vector3.right*8);eyes.RefreshEffects();
            Check(!eyes.EyesVisible && eyes.transform.Find("Moving shadow") == null &&
                eyes.transform.Find("False stone") == null, "Low sanity creates no apparitions while those effects are paused.");
            Check(Object.FindAnyObjectByType<NoxelDirector>() == null, "The game scene no longer runs Noxel's taunts.");
            var save=Object.FindAnyObjectByType<SaveSystem>();var snapshot=save.Capture();sanity.Adjust(80);save.Restore(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(snapshot)));
            Check(sanity.Value==0 && sanity.HasReachedZero,"Sanity and first-collapse state survive JSON roundtrip.");
            sanity.Restore(100,0,true);eyes.RefreshEffects();
            var sleep=player.GetComponent<SleepSystem>();
            Check(sleep.TryRest() && player.InputSuppressed,"Safe rest suppresses movement.");
            player.GetComponent<PlayerVitals>().Damage(1);
            Check(!sleep.Resting && !player.InputSuppressed,"Damage interrupts sleep and restores movement.");
            player.GetComponent<PlayerVitals>().Restore(100);sanity.Restore(100,0,true);
        }

        private static void CapturePreview(string path = "Logs/PrototypePreview.png")
        {
            var camera = Camera.main;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(960, 540, 24);
            var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(texture);
            }
        }

        private static void Finish()
        {
            running = false;
            step = -1;
            SessionState.SetBool("BrokenHeart.Checks", false);
            Application.logMessageReceived -= CaptureError;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            mouse = null;
            File.WriteAllText(ReportPath, string.Join("\n", results) + "\nCOMPLETE\n");
            Debug.Log("BROKEN HEART: Checks finished. See " + ReportPath);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}


