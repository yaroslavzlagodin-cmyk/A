using System.Collections.Generic;
using BrokenHeart.Crafting;
using BrokenHeart.Inventory;
using BrokenHeart.Player;
using BrokenHeart.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using static BrokenHeart.UI.HudSkin;

namespace BrokenHeart.UI
{
    public sealed class PrototypeHud : MonoBehaviour
    {
        [SerializeField] private BlockInteraction interaction;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private CraftingSystem crafting;
        [SerializeField] private MemorySystem memory;
        [SerializeField] private WorkstationSystem stations;

        private const int Columns = 9;
        private const int SlotPitch = 58;
        private const int RowPitch = 52;
        private const int SlotSize = 49;
        private InventorySlotActions slots;
        private SlotTarget? hover, drag;
        private bool journalOpen;
        private static readonly string[] TrailSteps =
            { "See the shadow", "Study the anomaly", "Craft mental protection", "Find the giant's trail" };
        private bool split;
        private float storageScroll, recipeScroll;
        private bool draggingStorageScroll, draggingRecipeScroll;
        private RecipeCategory? category;
        private RecipeData selectedRecipe;
        private readonly List<RecipeData> visibleRecipes = new();
        private readonly Sprite[] equipmentIcons = new Sprite[PlayerInventory.EquipmentCount];
        private SpriteRenderer portrait;
        private WorldStatusHud worldHud;
        private string inventoryNotice;
        private float noticeUntil;
        private Workstation shownStation;
        private SlotKind? sweepSource;
        private Vector2 lastSweepPoint;
        private bool trackSweepSlots;
        private bool sweepFromHotbar, sweepAcrossBag, sweepInventoryOpen;
        private readonly HashSet<int> sweptSlots = new();
        private readonly List<(Rect Bounds, SlotTarget Target)> sweepSlots = new();
        private readonly List<(float Entry, SlotTarget Target)> sweepHits = new();

        private static readonly (RecipeCategory? Category, string Name, string ShortName, string UkrainianShort)[] Tabs =
        {
            (null, "All recipes", "All", "Усі"),
            (RecipeCategory.Blocks, "Blocks", "Blk", "Бл"),
            (RecipeCategory.Armor, "Armor", "Arm", "Бр"),
            (RecipeCategory.Mechanisms, "Workbenches and mechanisms", "Mech", "Верст"),
            (RecipeCategory.Tools, "Tools", "Tool", "Інст"),
            (RecipeCategory.Magic, "Magic", "Mag", "Маг"),
            (RecipeCategory.Backpacks, "Backpacks", "Pack", "Рюк")
        };
        private static readonly string[] SlotNames =
        {
            "Helmet", "Chestplate", "Leggings", "Boots", "Lamp", "Shield", "Backpack"
        };

        private Rect InventoryPanel => new Rect((Width - 214 - 620) / 2 + 48, (Height - 472) / 2, 568, 408);
        private Rect Hotbar => inventory.IsOpen
            ? new Rect(InventoryPanel.x, InventoryPanel.yMax - 1, 536, 64)
            : new Rect((Width - 536) / 2, Height - 73, 536, 62);

        public bool BlocksWorldPointer
        {
            get
            {
                if (inventory == null) return false;
                if (inventory.IsOpen || inventory.IsMapOpen || drag.HasValue) return true;
                if (Mouse.current == null) return false;
                Vector2 mouse = Mouse.current.position.ReadValue();
                mouse = new Vector2(mouse.x, Screen.height - mouse.y) / Scale;
                return Hotbar.Contains(mouse) || new Rect((Width - 230) / 2, Hotbar.y - 22, 230, 22).Contains(mouse) ||
                    worldHud != null && worldHud.HasWorld && WorldStatusHud.PanelArea.Contains(mouse);
            }
        }

        private void Start()
        {
            if (inventory == null) return;
            slots = new InventorySlotActions(inventory, stations);
            portrait = inventory.GetComponent<SpriteRenderer>();
            worldHud = GetComponent<WorldStatusHud>();
            foreach (var item in inventory.Catalog.Items)
            {
                if (item.EquipmentSlot == EquipmentSlot.None) continue;
                for (int i = 0; i < equipmentIcons.Length; i++)
                    if (PlayerInventory.SlotType(i) == item.EquipmentSlot) equipmentIcons[i] ??= item.Icon;
            }
        }

        private void Update()
        {
            if (!inventory.IsOpen) journalOpen = false;
            var station = stations?.OpenStation;
            if (station != shownStation)
            {
                shownStation = station;
                drag = null;
                EndSweep();
                category = null;
                selectedRecipe = null;
                recipeScroll = 0;
            }
            bool canInteract = inventory != null && !inventory.IsMapOpen && Application.isFocused && Time.timeScale > 0;
            if (!canInteract || (sweepSource.HasValue && sweepInventoryOpen != inventory.IsOpen)) EndSweep();
            if (!canInteract || !inventory.IsOpen ||
                (drag.HasValue && drag.Value.Kind == SlotKind.Bag && drag.Value.Index >= inventory.Model.Capacity))
            {
                drag = null;
                draggingStorageScroll = draggingRecipeScroll = false;
            }
        }

        private void OnGUI()
        {
            if (Time.timeScale == 0 || inventory == null || inventory.Model == null || inventory.IsMapOpen) return;

            var oldMatrix = GUI.matrix;
            int oldDepth = GUI.depth;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            GUI.depth = -2;
            hover = null;
            sweepSlots.Clear();
            trackSweepSlots = sweepSource.HasValue && Event.current.rawType is EventType.MouseDrag or EventType.MouseUp;
            if (inventory.IsOpen)
            {
                ShadeWorld();
                DrawInventory(InventoryPanel);
            }
            DrawHotbar(Hotbar);
            HandleSlotInput();
            DrawPointerDetails();
            if (!inventory.IsOpen && memory != null && !string.IsNullOrEmpty(memory.ActiveNotification))
            {
                var notice = new Rect((Width - 432) / 2, 16, 432, 28);
                Frame(notice);
                Label(Inset(notice, 4), L(memory.ActiveNotification), 12, Gold, TextAnchor.MiddleCenter);
            }
            GUI.depth = oldDepth;
            GUI.matrix = oldMatrix;
        }

        private static Rect At(Rect start, float x, float y, float width, float height) =>
            new Rect(start.x + x, start.y + y, width, height);

        private void ShadeWorld()
        {
            var shade = new Color(0.045f, 0.025f, 0.055f, 0.45f);
            if (worldHud == null || !worldHud.HasWorld)
            {
                MenuSkin.Fill(new Rect(0, 0, Width, Height), shade);
                return;
            }
            var map = WorldStatusHud.PanelArea;
            float left = Mathf.Round(map.x * Scale) / Scale;
            float right = Mathf.Round(map.xMax * Scale) / Scale;
            float top = Mathf.Round(map.y * Scale) / Scale;
            float bottom = Mathf.Round(map.yMax * Scale) / Scale;
            MenuSkin.Fill(new Rect(0, 0, left, Height), shade);
            MenuSkin.Fill(new Rect(right, 0, Width - right, Height), shade);
            MenuSkin.Fill(new Rect(left, 0, right - left, top), shade);
            MenuSkin.Fill(new Rect(left, bottom, right - left, Height - bottom), shade);
        }

        private void DrawInventory(Rect panel)
        {
            Frame(panel);
            var station = stations?.OpenStation;
            string title = station != null ? station.Item.DisplayName : "Inventory";
            Label(At(panel, 12, 5, 350, 28), L(title), 19, Text, bold: true);
            if (Button(At(panel, panel.width - 101, 8, 88, 22), L("Close") + "  [Esc]"))
            {
                inventory.SetOpen(false);
                drag = null;
                EndSweep();
            }
            if (Button(At(panel, panel.width - 235, 8, 128, 22), L(journalOpen ? "Back to items" : "Field journal")))
                ToggleJournal();
            if (journalOpen)
            {
                DrawJournal(panel);
                return;
            }
            if (station?.Kind == StationKind.Chest) DrawChest(At(panel, 10, 38, panel.width - 20, 176), station);
            else if (station?.Kind == StationKind.Furnace) DrawFurnace(At(panel, 10, 38, panel.width - 20, 176), station);
            else
            {
                DrawEquipment(At(panel, 10, 38, 195, 176));
                DrawCrafting(At(panel, 212, 38, panel.width - 222, 176));
            }
            DrawStorage(At(panel, 10, 221, panel.width - 20, 182));
        }

        public bool ToggleJournal()
        {
            if (!inventory.IsOpen) return false;
            journalOpen = !journalOpen;
            drag = null;
            EndSweep();
            return journalOpen;
        }

        private void DrawJournal(Rect panel)
        {
            Label(At(panel, 14, 40, 540, 24), L("The giant's trail"), 18, Gold, bold: true);
            for (int i = 0; i < TrailSteps.Length; i++)
                Label(At(panel, 18, 68 + i * 19, 535, 19), (i < memory.TrailStep ? "✓  " : "·  ") + L(TrailSteps[i]), 13,
                    i < memory.TrailStep ? Mint : i == memory.TrailStep ? Text : Soft);
            string hint = memory.TrailStep switch
            {
                0 => "At night, press R near a moving silhouette at 51–70 sanity.",
                1 => "At 31–50 sanity, press R within five blocks of a false stone.",
                2 when !memory.HasKnowledge("lore.darkness") => "At 16–30 sanity, press R near a dimmed powered lamp. Stay until the light returns.",
                2 => "At a workbench, craft a Mind Amulet or Hearth Ward.",
                3 => "At 15 sanity or less, defeat a physical shadow in darkness. Choose a non-peaceful world.",
                _ => "The tracks begin here"
            };
            string trail = memory.TrailStep == 4 ? $"  ({memory.GiantTrace.x}, {memory.GiantTrace.y})" : "";
            JournalText(At(panel, 18, 147, 532, 38), L(hint) + trail);
            Label(At(panel, 14, 190, 530, 21), L("Field notes"), 16, Gold, bold: true);
            if (memory.Journal.Count == 0 || !memory.HasKnowledge("lore.outline"))
            {
                Label(At(panel, 18, 214, 530, 28), L("Your first observation will appear here."), 13, Soft);
                return;
            }
            float row = 213;
            foreach (var entry in memory.Journal)
            {
                if (!memory.HasKnowledge(entry.id)) continue;
                JournalText(At(panel, 18, row, 531, 32), L(entry.title) + " — " + L(entry.description));
                row += 32;
            }
        }

        private GUIStyle journalStyle;
        private void JournalText(Rect rect, string value)
        {
            journalStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            journalStyle.normal.textColor = Text;
            GUI.Label(rect, value, journalStyle);
        }

        private void DrawChest(Rect pane, Workstation chest)
        {
            Plate(pane, Line, Ink);
            Label(At(pane, 9, 5, 86, 22), $"{chest.Contents.Capacity} {L("slots")}", 12, Soft);
            if (Button(At(pane, 99, 5, 100, 22), L("Sort"))) chest.Contents.SortFrom(0);
            if (Button(At(pane, 207, 5, 160, 22), L("Deposit matching"))) stations.TransferMatchingChestItems(false);
            if (Button(At(pane, 375, 5, 162, 22), L("Take matching"))) stations.TransferMatchingChestItems(true);
            for (int i = 0; i < chest.Contents.Capacity; i++)
                DrawSlot(At(pane, 9 + i % Columns * SlotPitch, 33 + i / Columns * RowPitch, SlotSize, SlotSize),
                    new SlotTarget(i, SlotKind.Station));
            Label(At(pane, 9, 140, pane.width - 18, 25), L("Hold Ctrl + LMB and sweep slots to move stacks"), 12, Soft);
        }

        private void DrawFurnace(Rect pane, Workstation furnace)
        {
            Plate(pane, Line, Ink);
            string[] labels = { "Ore input", "Fuel", "Ingot output" };
            for (int i = 0; i < labels.Length; i++)
            {
                float x = 69 + i * 140;
                Label(At(pane, x, 21, 130, 22), L(labels[i]), 13, Soft, TextAnchor.MiddleCenter);
                DrawSlot(At(pane, x + 35, 52, 60, 60), new SlotTarget(i, SlotKind.Station));
            }
            Meter(At(pane, 126, 125, pane.width - 252, 6), furnace.Progress, Gold);
            string status = furnace.BurnRemaining > 0 ? $"{L("Heat")}  {Mathf.CeilToInt(furnace.BurnRemaining)}s" :
                L("Add ore and wood or planks");
            Label(At(pane, 8, 138, pane.width - 16, 23), status, 12, Soft, TextAnchor.MiddleCenter);
        }

        private void DrawEquipment(Rect pane)
        {
            Plate(pane, Line, Ink);
            Label(At(pane, 7, 3, 178, 20), L("Equipment"), 13, Soft, bold: true);
            for (int i = 0; i < 4; i++)
                DrawSlot(At(pane, 7, 25 + i * 33, 29, 29), new SlotTarget(i, SlotKind.Equipment));

            var portraitRect = At(pane, 45, 25, 90, 124);
            Plate(portraitRect, Line, Panel);
            MenuSkin.Fill(At(portraitRect, 10, 103, 70, 1), Line);
            Icon(At(portraitRect, 16, 9, 58, 94), portrait != null ? portrait.sprite : null);
            DrawSlot(At(pane, 145, 25, 29, 29), new SlotTarget(4, SlotKind.Equipment));
            DrawSlot(At(pane, 145, 58, 29, 29), new SlotTarget(5, SlotKind.Equipment));

            ExperienceBar(At(pane, 7, 151, 181, 22), inventory.Progress);
        }

        private void RefreshRecipes()
        {
            if (crafting != null) crafting.ListVisible(category, visibleRecipes);
            if (!visibleRecipes.Contains(selectedRecipe))
                selectedRecipe = visibleRecipes.Count > 0 ? visibleRecipes[0] : null;
        }

        private void DrawCrafting(Rect pane)
        {
            Plate(pane, Line, Ink);
            for (int i = 0; i < Tabs.Length; i++)
            {
                var tab = Tabs[i];
                string shortName = Localization.Language == "uk" ? tab.UkrainianShort : tab.ShortName;
                var tabRect = At(pane, 5, 5 + i * 24, 39, 22);
                if (!Button(tabRect, shortName, category == tab.Category) || category == tab.Category) continue;
                category = tab.Category;
                selectedRecipe = null;
                recipeScroll = 0;
            }
            RefreshRecipes();
            string title = crafting != null && crafting.AtWorkbench ? "All recipes" : "Hand crafting";
            if (category.HasValue)
                foreach (var tab in Tabs)
                    if (tab.Category == category) { title = tab.Name; break; }
            Label(At(pane, 53, 3, pane.width - 61, 20), L(title), 13, Soft, bold: true);
            var shelf = At(pane, 53, 25, pane.width - 61, 101);
            Plate(shelf, Line, Panel);
            if (selectedRecipe == null)
            {
                Label(shelf, L("No recipes discovered in this category"), 12, Soft, TextAnchor.MiddleCenter);
                string hint = crafting != null && crafting.AtWorkbench ? "Find materials to discover recipes" : "More recipes at a workbench";
                Label(At(pane, 54, 128, pane.width - 62, 18), L(hint), 11, Text);
                Label(At(pane, 54, 147, pane.width - 62, 18), L("Study a found item with Shift + LMB"), 11, Soft);
                return;
            }
            const int recipeColumns = 7;
            const int recipePitch = 32;
            int recipeRows = Mathf.CeilToInt(visibleRecipes.Count / (float)recipeColumns);
            var viewport = Inset(shelf, 3);
            recipeScroll = Scroll(viewport, recipeScroll, recipeRows * recipePitch - 5, ref draggingRecipeScroll);
            viewport.width -= 12;
            bool pointerInside = viewport.Contains(Event.current.mousePosition);
            GUI.BeginGroup(viewport);
            for (int i = 0; i < visibleRecipes.Count; i++)
            {
                var recipe = visibleRecipes[i];
                var rect = new Rect(2 + (i % recipeColumns) * 36, 1 + (i / recipeColumns) * recipePitch - recipeScroll, 29, 29);
                if (rect.yMax <= 0 || rect.y >= viewport.height) continue;
                if (Button(rect, "", recipe == selectedRecipe) && pointerInside) selectedRecipe = recipe;
                Icon(Inset(rect, 4), recipe.Output.Icon);
            }
            GUI.EndGroup();

            bool canCraft = crafting.CanCraft(selectedRecipe, out string reason);
            Label(At(pane, 54, 129, pane.width - 143, 18),
                $"{L(selectedRecipe.Output.DisplayName)}  ×{selectedRecipe.OutputCount}", 12, Text, bold: true);
            float costX = 54;
            foreach (var ingredient in selectedRecipe.Ingredients)
            {
                if (!memory.KnowsMaterial(ingredient.item)) continue;
                Icon(At(pane, costX, 152, 14, 14), ingredient.item.Icon);
                int own = inventory.Model.Count(ingredient.item);
                Label(At(pane, costX + 18, 150, 36, 17), $"{own}/{ingredient.count}", 10, own >= ingredient.count ? Mint : Red);
                costX += 54;
            }
            if (!canCraft)
                Label(At(pane, pane.width - 116, 151, 108, 17), L(reason), 10, Soft, TextAnchor.MiddleRight);
            if (Button(At(pane, pane.width - 82, 129, 74, 20), L("Craft"), true, canCraft))
                crafting.Craft(selectedRecipe, out _);
        }

        private void DrawStorage(Rect pane)
        {
            Plate(pane, Line, Ink);
            var backpacks = At(pane, -51, 23, 42, 159);
            Frame(backpacks);
            for (int i = 0; i < PlayerInventory.BackpackCount; i++)
                DrawSlot(At(backpacks, 4, 6 + i * 38, 34, 34),
                    new SlotTarget(PlayerInventory.EquipmentCount - PlayerInventory.BackpackCount + i, SlotKind.Equipment));

            Label(At(pane, 6, 2, 250, 20), L("Storage"), 13, Soft, bold: true);
            int capacity = inventory.Model.Capacity - inventory.HotbarSize;
            Label(At(pane, pane.width - 210, 2, 98, 20), $"{capacity} {L("slots")}", 11, Soft, TextAnchor.MiddleRight);
            if (Button(At(pane, pane.width - 106, 2, 98, 20), L("Sort")))
                inventory.Model.SortFrom(inventory.HotbarSize);
            var viewport = At(pane, 6, 24, pane.width - 12, 156);
            int rows = Mathf.CeilToInt(capacity / (float)Columns);
            storageScroll = Scroll(viewport, storageScroll, rows * RowPitch, ref draggingStorageScroll);
            viewport.width -= 12;
            bool pointerInside = viewport.Contains(Event.current.mousePosition);
            GUI.BeginGroup(viewport);
            for (int i = 0; i < capacity; i++)
            {
                var rect = new Rect(1 + (i % Columns) * SlotPitch, (i / Columns) * RowPitch - storageScroll, SlotSize, SlotSize);
                if (rect.yMax <= 0 || rect.y >= viewport.height) continue;
                DrawSlot(rect, new SlotTarget(i + inventory.HotbarSize), pointerInside, viewport);
            }
            GUI.EndGroup();
        }

        private void DrawHotbar(Rect bar)
        {
            Frame(bar);
            for (int i = 0; i < inventory.HotbarSize; i++)
            {
                var rect = At(bar, 12 + i * SlotPitch, 6, SlotSize, SlotSize);
                DrawSlot(rect, new SlotTarget(i));
                Label(At(rect, 3, 2, 13, 12), (i + 1).ToString(), 10, i == inventory.SelectedIndex ? Gold : Soft);
                if (i == inventory.SelectedIndex)
                    Meter(At(rect, 1, rect.height + 1, rect.width - 2, 4), 1, Gold);
            }
            if (inventory.IsOpen) return;
            var label = new Rect(bar.center.x - 115, bar.y - 21, 230, 20);
            if (interaction != null && interaction.Progress > 0)
                Meter(At(label, 0, -10, label.width, 6), interaction.Progress, Mint);
            if (interaction != null && interaction.TargetStation is Workstation target)
            {
                Label(label, $"{L(target.Item.DisplayName)}   ·   {L("RMB to open")}", 12, Gold, TextAnchor.MiddleCenter);
                return;
            }
            string itemName = inventory.Selected.Item != null ? inventory.Selected.Item.DisplayName : "Empty hand";
            if (Button(label, $"{L(itemName)}   ·   E / Tab")) inventory.SetOpen(true);
        }

        private void DrawSlot(Rect rect, SlotTarget target, bool interactive = true, Rect? group = null)
        {
            if (trackSweepSlots && target.Kind == sweepSource &&
                (target.Kind != SlotKind.Bag || sweepAcrossBag || (target.Index < inventory.HotbarSize) == sweepFromHotbar))
            {
                var bounds = rect;
                if (group is Rect clip)
                {
                    bounds.position += clip.position;
                    bounds = Rect.MinMaxRect(Mathf.Max(bounds.xMin, clip.xMin), Mathf.Max(bounds.yMin, clip.yMin),
                        Mathf.Min(bounds.xMax, clip.xMax), Mathf.Min(bounds.yMax, clip.yMax));
                }
                if (bounds.width > 0 && bounds.height > 0) sweepSlots.Add((bounds, target));
            }
            bool over = interactive && rect.Contains(Event.current.mousePosition);
            if (over) hover = target;
            bool active = target.Kind == SlotKind.Bag && target.Index == inventory.SelectedIndex;
            bool validGear = target.Equipment && drag.HasValue && slots.StackAt(drag.Value).Item != null &&
                slots.StackAt(drag.Value).Item.EquipmentSlot == PlayerInventory.SlotType(target.Index);
            var stack = slots.StackAt(target);
            Plate(rect, active ? Gold : validGear ? Mint : over ? Soft : Line, Ink);
            MenuSkin.Fill(At(rect, 2, 2, rect.width - 4, 1), Line);
            if (stack.Empty)
            {
                if (target.Equipment) Icon(Inset(rect, 5), equipmentIcons[target.Index], 0.4f);
                return;
            }
            Icon(Inset(rect, rect.width > 40 ? 8 : 4), stack.Item.Icon, drag.HasValue && drag.Value.Matches(target) ? 0.45f : 1);
            if (stack.Count > 1) Label(At(rect, 2, rect.height - 17, rect.width - 6, 15), stack.Count.ToString(), 11, Text, TextAnchor.LowerRight, true);
        }

        private void HandleSlotInput()
        {
            var evt = Event.current;
            if (HandleQuickTransfer(evt)) return;
            if (evt.type == EventType.MouseDown && hover is SlotTarget source && (evt.button == 0 || evt.button == 1))
            {
                var stack = slots.StackAt(source);
                if (inventory.IsOpen && !source.Station && evt.button == 0 && evt.shift && !stack.Empty && memory != null && memory.Study(stack.Item))
                {
                    drag = null;
                    inventoryNotice = memory.ActiveNotification;
                    noticeUntil = Time.unscaledTime + 3;
                    evt.Use();
                    return;
                }
                slots.Select(source);
                if (inventory.IsOpen && !stack.Empty) { drag = source; split = evt.button == 1; }
                evt.Use();
            }
            if (evt.rawType != EventType.MouseUp || !drag.HasValue || evt.button != (split ? 1 : 0)) return;
            if (hover is SlotTarget destination && !destination.Matches(drag.Value) &&
                !slots.MoveStack(drag.Value, destination, split) && drag.Value.Equipment && destination.Kind == SlotKind.Bag &&
                PlayerInventory.SlotType(drag.Value.Index) == EquipmentSlot.Backpack)
            {
                inventoryNotice = "Free the last row and move the backpack to an empty slot";
                noticeUntil = Time.unscaledTime + 3;
            }
            drag = null;
            if (evt.type == EventType.MouseUp) evt.Use();
        }

        private bool HandleQuickTransfer(Event evt)
        {
            if (evt.button == 0 && evt.type == EventType.MouseDown && evt.control &&
                hover is SlotTarget first)
            {
                EndSweep();
                sweepSource = first.Kind;
                sweepInventoryOpen = inventory.IsOpen;
                sweepAcrossBag = stations?.OpenStation?.Contents != null;
                sweepFromHotbar = first.Kind == SlotKind.Bag && first.Index < inventory.HotbarSize;
                lastSweepPoint = evt.mousePosition;
                drag = null;
                TransferSweptSlot(first);
                evt.Use();
                return true;
            }

            if (!sweepSource.HasValue) return false;
            if (evt.type == EventType.KeyUp && !evt.control &&
                evt.keyCode is KeyCode.LeftControl or KeyCode.RightControl)
                EndSweep();
            if (evt.button != 0 || evt.rawType is not (EventType.MouseDrag or EventType.MouseUp)) return false;
            if (evt.control && evt.type != EventType.Used) TransferAlongSweep(evt.mousePosition);
            if (evt.rawType == EventType.MouseUp || !evt.control) EndSweep();
            if (evt.type != EventType.Used) evt.Use();
            return true;
        }

        private void TransferAlongSweep(Vector2 endpoint)
        {
            sweepHits.Clear();
            Vector2 delta = endpoint - lastSweepPoint;
            foreach (var (bounds, target) in sweepSlots)
            {
                if (sweptSlots.Contains(target.Index)) continue;
                float entry = 0, exit = 1;
                if (ClipAxis(lastSweepPoint.x, delta.x, bounds.xMin, bounds.xMax, ref entry, ref exit) &&
                    ClipAxis(lastSweepPoint.y, delta.y, bounds.yMin, bounds.yMax, ref entry, ref exit))
                    sweepHits.Add((entry, target));
            }
            sweepHits.Sort((left, right) => left.Entry.CompareTo(right.Entry));
            foreach (var (_, target) in sweepHits) TransferSweptSlot(target);
            lastSweepPoint = endpoint;
        }

        private static bool ClipAxis(float origin, float delta, float min, float max, ref float entry, ref float exit)
        {
            if (Mathf.Abs(delta) < 0.0001f) return origin >= min && origin <= max;
            float first = (min - origin) / delta;
            float last = (max - origin) / delta;
            entry = Mathf.Max(entry, Mathf.Min(first, last));
            exit = Mathf.Min(exit, Mathf.Max(first, last));
            return entry <= exit;
        }

        private void TransferSweptSlot(SlotTarget target)
        {
            if (!sweptSlots.Add(target.Index)) return;
            slots.QuickTransfer(target);
        }

        private void EndSweep()
        {
            sweepSource = null;
            sweptSlots.Clear();
        }

        private void DrawPointerDetails()
        {
            Vector2 pointer = Event.current.mousePosition;
            if (drag is SlotTarget source && !slots.StackAt(source).Empty)
                Icon(new Rect(pointer.x - 15, pointer.y - 15, 30, 30), slots.StackAt(source).Item.Icon);
            else if (hover is SlotTarget slot && inventory.IsOpen)
            {
                var stack = slots.StackAt(slot);
                string name = !stack.Empty ? stack.Item.DisplayName
                    : slot.Equipment ? SlotNames[(int)PlayerInventory.SlotType(slot.Index) - 1] : "";
                if (name.Length > 0)
                {
                    string detail = slot.Equipment && PlayerInventory.SlotType(slot.Index) == EquipmentSlot.Backpack
                        ? $"  +{PlayerInventory.SlotsPerBackpack} {L("slots")}" : "";
                    if (!stack.Empty && stack.Item.Protection > 0)
                        detail = $"  −{stack.Item.Protection:P0} {L("damage")}";
                    bool canStudy = memory != null && memory.CanStudy(stack.Item);
                    string usage = stack.Item?.Id switch
                    {
                        "mind_amulet" => "Shield slot · 15% less sanity loss",
                        "hearth_ward" => "Powered · 50% fewer intrusions within 6 blocks",
                        "sealed_lamp" => "Dimming lasts 2 seconds",
                        _ => null
                    };
                    int height = canStudy || usage != null ? 42 : 23;
                    var tip = new Rect(Mathf.Min(pointer.x + 14, Width - 232), Mathf.Min(pointer.y + 14, Height - height - 3), 220, height);
                    Plate(tip, Line, Ink);
                    Label(At(tip, 6, 0, 210, 23), L(name) + detail, 12, Text);
                    if (usage != null) Label(At(tip, 6, 22, 210, 17), L(usage), 10, Mint);
                    else if (canStudy)
                        Label(At(tip, 6, 22, 210, 17), L("Shift + LMB · Study"), 11, Gold);
                }
            }
            if (inventory.IsOpen && Time.unscaledTime < noticeUntil)
            {
                var tip = At(InventoryPanel, 12, -29, 544, 25);
                Plate(tip, Gold, Ink);
                Label(Inset(tip, 4), L(inventoryNotice), 12, Gold, TextAnchor.MiddleCenter);
            }
        }
    }
}
