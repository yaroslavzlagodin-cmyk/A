using System;
using System.IO;
using System.Linq;
using BrokenHeart.Inventory;
using BrokenHeart.World;
using UnityEngine;
using UnityEngine.InputSystem;
using BrokenHeart.Systems.Energy;
using BrokenHeart.Combat;
using BrokenHeart.Core.Profiles;
using BrokenHeart.Crafting;
using System.Collections.Generic;

namespace BrokenHeart.Systems
{
    [Serializable] public struct SavedSlot { public string id; public int count; }
    [Serializable] public struct SavedDrop { public string id; public int count; public Vector3 position; }
    [Serializable] public struct SavedWorkstation
    {
        public string itemId;
        public Vector2Int cell;
        public SavedSlot[] slots;
        public float burn, smelt;
        public bool fullSizeFurnace;
    }
    [Serializable] public sealed class SaveData
    {
        public int version = 1, generatorVersion = 1, seed, selected;
        public Vector3 position;
        public BlockEdit[] blocks;
        public Vector2Int[] explored;
        public SavedSlot[] slots, equipment;
        public string[] memories, studyItems, knownMaterials;
        public int experience;
        public SavedDrop[] drops;
        public SavedDevice[] devices;
        public SavedWorkstation[] stations;
        public float elapsed, health = 100;
        public WeatherKind weather;
        public float sanity = 100, awakeSeconds;
        public bool collapsed;
        internal SaveData Copy() => (SaveData)MemberwiseClone();
    }
    public sealed class SaveSystem : MonoBehaviour
    {
        private const float AutosaveInterval = 120f;
        [SerializeField] private BlockWorld world;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private MemorySystem memory;
        private SleepSystem sleep;
        private SaveData startingState;
        private bool sessionReady, writing;
        private float activeSeconds;
        public string Status { get; private set; } = "F5 save / F9 load";
        private static string SandboxPath => Path.Combine(ProfileLibrary.Root, "world.json");

        private void OnEnable()
        {
            sleep = inventory != null ? inventory.GetComponent<SleepSystem>() : null;
            if (sleep != null) sleep.RestCompleted += SaveAfterRest;
        }

        private void OnDisable()
        {
            if (sleep != null) sleep.RestCompleted -= SaveAfterRest;
        }

        private void SaveAfterRest()
        {
            if (SessionContext.Active && sessionReady) Save();
        }

        private void Update()
        {
            if (Time.timeScale == 0) return;
            if (SessionContext.Active && sessionReady)
            {
                // Count play time only. A transient failure retries at the next save event.
                activeSeconds += Time.deltaTime;
                if (activeSeconds >= AutosaveInterval)
                {
                    activeSeconds = 0;
                    Save();
                    return;
                }
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f5Key.wasPressedThisFrame) Save();
            if (keyboard.f9Key.wasPressedThisFrame) Load();
        }
        public SaveData Capture() => new SaveData {
            seed = world.Seed, generatorVersion = world.Generator.Version, position = inventory.transform.position,
            selected = inventory.SelectedIndex, blocks = ((ChunkBlockStore)world.Store).ExportEdits(),
            explored = world.ExploredCells.ToArray(),
            slots = WriteSlots(inventory.Model),
            equipment = Enumerable.Range(0, PlayerInventory.EquipmentCount).Select(i => inventory.Equipped(i))
                .Select(WriteStack).ToArray(),
            memories = memory.Unlocked.ToArray(),
            studyItems = memory.StudyItems.ToArray(),
            knownMaterials = memory.KnownMaterials.ToArray(),
            experience = inventory.Progress.TotalExperience,
            elapsed = world.GetComponent<DayNightSystem>() != null ? world.GetComponent<DayNightSystem>().Elapsed : 0,
            weather = world.GetComponent<WorldWeather>() != null ? world.GetComponent<WorldWeather>().Current : WeatherKind.Clear,
            health = inventory.GetComponent<PlayerVitals>() != null ? inventory.GetComponent<PlayerVitals>().Health : 100,
            sanity = inventory.GetComponent<SanitySystem>() != null ? inventory.GetComponent<SanitySystem>().Value : 100,
            awakeSeconds = inventory.GetComponent<SanitySystem>() != null ? inventory.GetComponent<SanitySystem>().AwakeSeconds : 0,
            collapsed = inventory.GetComponent<SanitySystem>() != null && inventory.GetComponent<SanitySystem>().HasReachedZero,
            devices = world.GetComponent<EnergyNetwork>()?.Devices.Select(d => new SavedDevice {
                itemId = d.Item.Id, position = d.transform.position, stored = d.Stored }).ToArray() ?? Array.Empty<SavedDevice>(),
            stations = world.GetComponent<WorkstationSystem>()?.Stations.Select(station => new SavedWorkstation
            {
                itemId = station.Item.Id, cell = station.Cell, slots = WriteSlots(station.Contents),
                burn = station.BurnRemaining, smelt = station.SmeltSeconds, fullSizeFurnace = station.FullSizeFurnace
            }).ToArray() ?? Array.Empty<SavedWorkstation>(),
            drops = UnityEngine.Object.FindObjectsByType<ItemDrop>().Where(d => d.Count > 0 && world.Bounds.Contains(world.CellAt(d.transform.position)))
                .Select(d => new SavedDrop { id = d.Item.Id, count = d.Count, position = d.transform.position }).ToArray()
        };
        public bool Save()
        {
            if (writing || SessionContext.Active && !sessionReady) return false;
            writing = true;
            try
            {
                var data = Capture();
                ValidateSave(data);
                if (SessionContext.Active) SessionContext.Save(data);
                else SaveFileStore.WriteAtomic(SandboxPath, JsonUtility.ToJson(data, true));
                activeSeconds = 0;
                Status = "Saved world, backpack and memories";
                return true;
            }
            catch (Exception error) { Status = "Save failed: " + error.Message; return false; }
            finally { writing = false; }
        }

        public bool Load()
        {
            try
            {
                bool recovered = false;
                if (SessionContext.Active)
                {
                    PreparedSave prepared = default;
                    startingState ??= Capture();
                    var data = SessionContext.Load(startingState, candidate => prepared = ValidateSave(candidate), out recovered);
                    ApplySave(data, prepared);
                    sessionReady = true;
                }
                else
                {
                    Restore(JsonUtility.FromJson<SaveData>(SaveFileStore.Read(SandboxPath)));
                }
                activeSeconds = 0;
                Status = recovered ? "Loaded previous complete save" : "Save loaded";
                return true;
            }
            catch (Exception error) { Status = "Load failed: " + error.Message; return false; }
        }
        public void Restore(SaveData data)
        {
            ApplySave(data, ValidateSave(data));
        }

        private readonly struct PreparedSave
        {
            public readonly ItemStack[] Slots, Equipped;
            public readonly List<(SavedWorkstation Saved, ItemData Item, ItemStack[] Contents)> Stations;

            public PreparedSave(ItemStack[] slots, ItemStack[] equipped,
                List<(SavedWorkstation Saved, ItemData Item, ItemStack[] Contents)> stations)
            {
                Slots = slots;
                Equipped = equipped;
                Stations = stations;
            }
        }

        private PreparedSave ValidateSave(SaveData data)
        {
            // Validate the complete payload before changing any live state.
            if (data == null || data.version != 1 ||
                (data.generatorVersion < WorldGenerationData.LegacyVersion || data.generatorVersion > WorldGenerationData.CurrentVersion) ||
                data.slots == null || data.blocks == null || data.memories == null || data.drops == null ||
                data.selected < 0 || data.selected >= inventory.HotbarSize || data.experience < 0 ||
                !float.IsFinite(data.position.x) || !float.IsFinite(data.position.y) || !float.IsFinite(data.position.z) ||
                !world.Bounds.Contains(world.CellAt(data.position)))
                throw new InvalidDataException("Invalid or incompatible world save.");
            var equipped = new ItemStack[PlayerInventory.EquipmentCount];
            if (data.equipment != null && data.equipment.Length != 0)
            {
                if (data.equipment.Length != equipped.Length) throw new InvalidDataException("Invalid equipment slot count.");
                for (int i = 0; i < equipped.Length; i++)
                {
                    equipped[i] = ReadStack(data.equipment[i]);
                    if (!equipped[i].Empty && (equipped[i].Count != 1 || equipped[i].Item.EquipmentSlot != PlayerInventory.SlotType(i)))
                        throw new InvalidDataException("Item does not fit the equipment slot.");
                }
            }
            int capacity = inventory.CapacityWith(equipped);
            int oldCount = data.slots.Length;
            bool legacy = capacity == inventory.BaseCapacity && capacity >= 27 && (oldCount == 24 || oldCount == 27);
            if (oldCount != capacity && !legacy) throw new InvalidDataException("Invalid inventory slot count.");
            var slots = new ItemStack[capacity];
            if (!float.IsFinite(data.sanity) || data.sanity < 0 || data.sanity > 100 || !float.IsFinite(data.awakeSeconds) || data.awakeSeconds < 0)
                throw new InvalidDataException("Invalid sanity state.");
            if (!float.IsFinite(data.elapsed) || data.elapsed < 0 || !float.IsFinite(data.health) || data.health < 1 || data.health > 100 ||
                (int)data.weather < 0 || (int)data.weather > 3) throw new InvalidDataException("Invalid world clock or health.");
            for (int i = 0; i < oldCount; i++)
                slots[legacy && oldCount == 24 ? (i / 8) * 9 + i % 8 : i] = ReadStack(data.slots[i]);
            foreach (var edit in data.blocks)
                if (!world.Bounds.Contains(new Vector2Int(edit.x, edit.y)) || !world.KnowsId(edit.id)) throw new InvalidDataException("Unknown block or invalid cell.");
            if (data.explored != null && (data.explored.LongLength > (long)world.Bounds.width * world.Bounds.height ||
                Array.Exists(data.explored, cell => !world.Bounds.Contains(cell))))
                throw new InvalidDataException("Invalid explored cells.");
            foreach (var drop in data.drops)
                if (inventory.Catalog.Find(drop.id) == null || drop.count <= 0 || drop.count > inventory.Catalog.Find(drop.id).MaxStack ||
                    !float.IsFinite(drop.position.x) || !float.IsFinite(drop.position.y) || !world.Bounds.Contains(world.CellAt(drop.position)))
                    throw new InvalidDataException("Invalid dropped item.");
            var network = world.GetComponent<EnergyNetwork>();
            var deviceCells = new HashSet<Vector2Int>();
            foreach (var device in data.devices ?? Array.Empty<SavedDevice>())
            {
                var item = inventory.Catalog.Find(device.itemId);
                if (network == null || item == null || item.Device == null || !float.IsFinite(device.stored) || device.stored < 0 ||
                    device.stored > item.Device.capacity || !float.IsFinite(device.position.x) || !float.IsFinite(device.position.y) ||
                    !world.Bounds.Contains(world.CellAt(device.position)) || !deviceCells.Add(world.CellAt(device.position)) || deviceCells.Count > 64)
                    throw new InvalidDataException("Invalid energy device.");
            }
            return new PreparedSave(slots, equipped, ValidateStations(data, world.GetComponent<WorkstationSystem>(), deviceCells));
        }

        private void ApplySave(SaveData data, PreparedSave prepared)
        {
            var stations = world.GetComponent<WorkstationSystem>();
            var network = world.GetComponent<EnergyNetwork>();
            inventory.RestoreLoadout(prepared.Slots, prepared.Equipped);
            memory.Restore(data.memories, data.studyItems, data.knownMaterials);
            inventory.Progress.Restore(data.experience);
            inventory.Select(data.selected);
            inventory.GetComponent<SleepSystem>()?.CancelRest();
            inventory.GetComponent<SanitySystem>()?.Restore(data.sanity,data.awakeSeconds,data.collapsed);
            world.GetComponent<EnemySystem>()?.Clear();
            inventory.GetComponent<PlayerVitals>()?.Restore(data.health);
            world.GetComponent<DayNightSystem>()?.Restore(data.elapsed);
            world.GetComponent<WorldWeather>()?.SetWeather(data.weather);
            inventory.transform.position = data.position;
            var body = inventory.GetComponent<Rigidbody2D>(); body.position = data.position; body.linearVelocity = Vector2.zero;
            stations?.Clear();
            world.ResetWorld(data.seed, data.generatorVersion);
            foreach (var edit in data.blocks) world.Store.Set(new Vector2Int(edit.x, edit.y), string.IsNullOrEmpty(edit.id) ? null : edit.id);
            world.RefreshVisible(true);
            world.RestoreExploration(data.explored);
            world.Map.GetComponent<UnityEngine.Tilemaps.TilemapCollider2D>().ProcessTilemapChanges();
            foreach (var drop in UnityEngine.Object.FindObjectsByType<ItemDrop>()) { drop.gameObject.SetActive(false); Destroy(drop.gameObject); }
            var spawner = world.GetComponent<DropSpawner>();
            foreach (var drop in data.drops) spawner.Spawn(inventory.Catalog.Find(drop.id), drop.count, drop.position, inventory.GetComponent<Collider2D>());
            if (network != null)
            {
                network.Clear();
                foreach (var device in data.devices ?? Array.Empty<SavedDevice>()) network.Spawn(inventory.Catalog.Find(device.itemId), device.position, device.stored);
            }
            foreach (var (saved, item, contents) in prepared.Stations)
                stations.Spawn(item, saved.cell, saved.fullSizeFurnace).Restore(contents, saved.burn, saved.smelt);
        }

        private List<(SavedWorkstation Saved, ItemData Item, ItemStack[] Contents)> ValidateStations(
            SaveData data, WorkstationSystem system, HashSet<Vector2Int> occupied)
        {
            var result = new List<(SavedWorkstation, ItemData, ItemStack[])>();
            if (data.stations == null || data.stations.Length == 0) return result;
            if (system == null || data.stations.Length > WorkstationSystem.MaxStations)
                throw new InvalidDataException("Invalid workstation count.");

            var terrain = new TerrainGenerator(data.seed, world.Generation, data.generatorVersion);
            var edited = new Dictionary<Vector2Int, string>();
            foreach (var block in data.blocks) edited[new Vector2Int(block.x, block.y)] = block.id;
            string BlockAt(Vector2Int cell) => edited.TryGetValue(cell, out string id) ? id : terrain.Generate(cell);
            float maxFuel = inventory.Catalog.Items.Max(item => item.FuelSeconds);
            foreach (var saved in data.stations)
            {
                var item = inventory.Catalog.Find(saved.itemId);
                if (item == null || item.Station == StationKind.None || (saved.fullSizeFurnace && item.Station != StationKind.Furnace))
                    throw new InvalidDataException("Invalid workstation placement.");
                foreach (var offset in Workstation.Footprint(saved.fullSizeFurnace))
                {
                    var cell = saved.cell + offset;
                    // Placement needs support. An existing station may have been dug out from below.
                    if (!world.Bounds.Contains(cell) || !occupied.Add(cell) || !string.IsNullOrEmpty(BlockAt(cell)))
                        throw new InvalidDataException("Invalid workstation placement.");
                }
                int capacity = Workstation.CapacityFor(item.Station);
                if ((saved.slots?.Length ?? 0) != capacity || !float.IsFinite(saved.burn) || !float.IsFinite(saved.smelt) ||
                    saved.burn < 0 || saved.burn > maxFuel || saved.smelt < 0 || saved.smelt >= Workstation.SecondsPerIngot ||
                    item.Station != StationKind.Furnace && (saved.burn != 0 || saved.smelt != 0))
                    throw new InvalidDataException("Invalid workstation contents.");
                var contents = new ItemStack[capacity];
                for (int i = 0; i < contents.Length; i++) contents[i] = ReadStack(saved.slots[i]);
                if (item.Station == StationKind.Furnace)
                {
                    var ore = contents[Workstation.OreSlot].Item;
                    var fuel = contents[Workstation.FuelSlot].Item;
                    var output = contents[Workstation.OutputSlot].Item;
                    if (ore != null && ore.SmeltsTo == null || fuel != null && fuel.FuelSeconds <= 0 ||
                        saved.smelt > 0 && ore == null ||
                        output != null && !inventory.Catalog.Items.Any(source => source.SmeltsTo == output))
                        throw new InvalidDataException("Invalid furnace slots.");
                }
                result.Add((saved, item, contents));
            }
            return result;
        }

        private static SavedSlot WriteStack(ItemStack stack) =>
            new() { id = stack.Item != null ? stack.Item.Id : "", count = stack.Count };

        private static SavedSlot[] WriteSlots(InventoryModel model) => model == null ? Array.Empty<SavedSlot>() :
            Enumerable.Range(0, model.Capacity).Select(index => WriteStack(model[index])).ToArray();

        private ItemStack ReadStack(SavedSlot saved)
        {
            var item = string.IsNullOrEmpty(saved.id) ? null : inventory.Catalog.Find(saved.id);
            if (item == null ? !string.IsNullOrEmpty(saved.id) || saved.count != 0 : saved.count <= 0 || saved.count > item.MaxStack)
                throw new InvalidDataException("Unknown item or invalid stack.");
            return new ItemStack(item, saved.count);
        }
    }
}

