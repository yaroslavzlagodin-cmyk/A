using System.Collections.Generic;
using BrokenHeart.World;
using UnityEngine;
using UnityEngine.InputSystem;
using BrokenHeart.Inventory;
using BrokenHeart.UI;
using BrokenHeart.Systems.Energy;
using BrokenHeart.Combat;
using BrokenHeart.Crafting;

namespace BrokenHeart.Player
{
    public sealed class BlockInteraction : MonoBehaviour
    {
        [SerializeField] private BlockWorld world;
        [SerializeField] private Camera view;
        [SerializeField] private Collider2D playerShape;
        [SerializeField] private LineRenderer outline;
        [SerializeField] private float reach = 5f;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private DropSpawner drops;
        [SerializeField] private PrototypeHud hud;
        [SerializeField] private EnergyNetwork energy;
        [SerializeField] private WorkstationSystem stations;
        private const float TreeHardnessFactor = 0.15f;
        private readonly List<(Vector2Int Cell, BlockData Block)> treeParts = new();
        private Vector2Int miningCell;
        private float miningTime, treeHardness;
        private PlayerMotor motor;
        private EnemySystem enemies;
        private ItemData miningTool;
        private SubsurfaceShadows shadows;
        private bool mining, ctrlMining;
        public BlockData Selected => inventory.Selected.Item != null ? inventory.Selected.Item.PlaceableBlock : null;
        public float Progress { get; private set; }
        public bool InReach { get; private set; }
        public Workstation TargetStation { get; private set; }

        private void Start()
        {
            motor = GetComponent<PlayerMotor>();
            enemies = world.GetComponent<EnemySystem>();
            shadows = world.GetComponent<SubsurfaceShadows>();
        }

        private void Update()
        {
            TargetStation = null;
            var mouse = Mouse.current;
            if (Time.timeScale == 0 || motor.InputSuppressed || inventory.IsOpen || inventory.IsMapOpen ||
                (hud != null && hud.BlocksWorldPointer) || mouse == null || !Application.isFocused)
            {
                ClearPointer();
                return;
            }
            Vector2 screen = mouse.position.ReadValue();
            if (!view.pixelRect.Contains(screen)) { ClearPointer(); return; }
            var point = view.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -view.transform.position.z));
            var cell = world.CellAt(point);
            if (IsInReach(cell)) TargetStation = stations?.At(cell);
            if (mouse.rightButton.wasPressedThisFrame && stations != null && stations.Open(cell))
            {
                ClearPointer();
                return;
            }
            var selected = inventory.Selected.Item;
            if (selected?.Category == ItemCategory.Weapon)
            {
                ClearPointer();
                if (mouse.leftButton.isPressed) enemies?.TryAttack(point - playerShape.bounds.center);
                return;
            }
            var center = world.Center(cell);
            InReach = IsInReach(cell);
            bool placingStation = stations != null && selected != null && selected.Station != StationKind.None;
            if (placingStation) stations.ShowPreview(selected, cell);
            else stations?.HidePreview();
            outline.enabled = !placingStation && world.Store.Contains(cell) && (shadows == null || shadows.OpacityAt(cell) < 245);
            outline.transform.position = new Vector3(center.x, center.y, -0.1f);
            outline.startColor = outline.endColor = InReach ? new Color(1f, 0.86f, 0.42f) : new Color(1f, 0.3f, 0.3f);
            var keyboard = Keyboard.current;
            bool control = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            if (mouse.leftButton.isPressed)
            {
                Mine(cell, Time.deltaTime, control);
                return;
            }
            ResetMining();
            bool placingBlock = selected != null && selected.PlaceableBlock != null &&
                selected.Station == StationKind.None && selected.Device == null;
            if ((mouse.rightButton.wasPressedThisFrame || (placingBlock && mouse.rightButton.isPressed)) && TryPlace(cell))
                stations?.HidePreview();
        }

        public bool IsInReach(Vector2Int cell) => world.Store.Contains(cell) &&
            Vector2.Distance(playerShape.bounds.center, world.Center(cell)) <= reach;

        public bool TryPlace(Vector2Int cell)
        {
            var item = inventory.Selected.Item;
            if (item == null || !IsInReach(cell)) return false;
            if (item.Station == StationKind.None && !CanPlace(world.Center(cell))) return false;
            if (item.Station != StationKind.None)
            {
                if (stations == null || stations.Place(item, cell) == null) return false;
            }
            else if (item.Device != null)
            {
                if (energy == null || !energy.CanPlace(cell)) return false;
                energy.Spawn(item, world.Center(cell));
            }
            else if (item.PlaceableBlock == null || world.Get(cell) != null || !world.Set(cell, item.PlaceableBlock)) return false;
            inventory.Model.RemoveAt(inventory.SelectedIndex, 1);
            return true;
        }

        public bool Mine(Vector2Int cell, float seconds, bool fellTree = false)
        {
            var tool = inventory.Selected.Item;
            if (!IsInReach(cell) || tool?.Category == ItemCategory.Weapon) { ResetMining(); return false; }
            var block = world.Get(cell);
            var station = block == null ? stations?.At(cell) : null;
            var target = station?.Cell ?? cell;
            if (!mining || target != miningCell || tool != miningTool || fellTree != ctrlMining)
            {
                ResetMining();
                mining = true;
                miningCell = target;
                miningTool = tool;
                ctrlMining = fellTree;
                if (fellTree && block != null && block.Id is ("wood" or "leaves")) PrepareTree(cell);
            }
            float elapsed = Mathf.Max(0, seconds);
            if (block == null)
            {
                var device = energy?.At(cell);
                if (station == null && device == null) { ResetMining(); return false; }
                miningTime += elapsed;
                Progress = Mathf.Clamp01(miningTime);
                if (Progress < 1) return false;
                if (station != null) stations.Remove(station, true);
                else energy.Remove(device, true);
                ResetMining();
                return true;
            }
            if (!block.Breakable) { ResetMining(); return false; }
            miningTime += elapsed * (tool != null ? tool.MiningPowerFor(block) : 1);
            Progress = Mathf.Clamp01(miningTime / (treeParts.Count > 0 ? treeHardness : block.Hardness));
            if (Progress < 1) return false;
            if (treeParts.Count > 0) return FellTree(cell);
            bool changed = world.Set(cell, null);
            if (changed) drops.Spawn(block.Drop ?? inventory.Catalog.ForBlock(block), 1, world.Center(cell), playerShape);
            ResetMining();
            return changed;
        }

        private void PrepareTree(Vector2Int target)
        {
            if (world.Generator == null) return;
            bool targetBelongsToTree = false;
            foreach (var (cell, id) in world.Generator.TreeAt(target))
            {
                var block = world.Get(cell);
                if (block == null || !block.Breakable || block.Id != id) continue;
                treeParts.Add((cell, block));
                treeHardness += block.Hardness;
                if (cell == target) targetBelongsToTree = true;
            }
            if (!targetBelongsToTree) { treeParts.Clear(); treeHardness = 0; }
            treeHardness *= TreeHardnessFactor;
        }

        private bool FellTree(Vector2Int target)
        {
            int wood = 0, leaves = 0;
            foreach (var part in treeParts)
            {
                if (world.Get(part.Cell) != part.Block || !world.Set(part.Cell, null)) continue;
                if (part.Block.Id == "wood") wood++;
                else leaves++;
            }
            var position = world.Center(target);
            DropTreeBlocks(inventory.Catalog.Find("wood"), wood, position);
            DropTreeBlocks(inventory.Catalog.Find("leaves"), leaves, position);
            ResetMining();
            return wood + leaves > 0;
        }

        private void DropTreeBlocks(ItemData item, int count, Vector3 position)
        {
            if (item == null) return;
            while (count > 0)
            {
                int stack = Mathf.Min(count, item.MaxStack);
                drops.Spawn(item, stack, position, playerShape);
                count -= stack;
            }
        }

        public bool CanPlace(Vector3 center)
        {
            var cell = world.CellAt(center);
            if (energy?.At(cell) != null || stations?.At(cell) != null) return false;
            var cellBounds = new Bounds(center, new Vector3(1f, 1f, 2));
            return !cellBounds.Intersects(playerShape.bounds);
        }
        private void ResetMining()
        {
            mining = false;
            miningTime = 0;
            Progress = 0;
            treeHardness = 0;
            treeParts.Clear();
        }

        private void ClearPointer()
        {
            ResetMining();
            outline.enabled = false;
            stations?.HidePreview();
        }
    }
}
