using BrokenHeart.Combat;
using BrokenHeart.Inventory;
using BrokenHeart.Systems;
using BrokenHeart.World;
using UnityEngine;
using static BrokenHeart.UI.HudSkin;

namespace BrokenHeart.UI
{
    public sealed class WorldStatusHud : MonoBehaviour
    {
        [SerializeField] private BlockWorld world;
        [SerializeField] private Transform player;
        [SerializeField] private SaveSystem save;
        [SerializeField] private DayNightSystem clock;
        [SerializeField] private WorldWeather weather;
        [SerializeField] private PlayerVitals vitals;
        [SerializeField] private SanitySystem sanity;
        [SerializeField] private SleepSystem sleep;

        private readonly TerrainMap minimap = new();
        private readonly TerrainMap fullMap = new();
        private PlayerInventory inventory;
        private Vector2 center;
        private float mapWidth;
        private bool overview, dragging, initialized;

        internal bool HasWorld => world != null && world.Generator != null;
        internal static Rect PanelArea => new Rect(Width - 210, 12, 198, 277);

        private void Awake()
        {
            if (player != null) inventory = player.GetComponent<PlayerInventory>();
        }

        private void Update()
        {
            if (inventory == null || !inventory.IsMapOpen)
            {
                initialized = false;
                dragging = false;
            }
        }

        private void OnGUI()
        {
            if (Time.timeScale == 0 || !HasWorld || inventory == null) return;

            var oldMatrix = GUI.matrix;
            int oldDepth = GUI.depth;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            GUI.depth = -3;
            if (inventory.IsMapOpen) DrawWorldMap();
            else DrawMinimap();
            GUI.depth = oldDepth;
            GUI.matrix = oldMatrix;
        }

        private void DrawMinimap()
        {
            var panel = PanelArea;
            Frame(panel);
            Label(new Rect(panel.x + 8, panel.y + 4, 100, 22), L("Minimap"), 14, Text, bold: true);
            if (Button(new Rect(panel.xMax - 78, panel.y + 5, 70, 20), L("Map") + "  [M]"))
                inventory.SetMapOpen(true);

            var mapRect = new Rect(panel.x + 8, panel.y + 30, panel.width - 16, 104);
            var view = ViewCentered(player.position, 76, 44);
            Plate(mapRect, Line, Ink);
            var image = Inset(mapRect, 1);
            minimap.Draw(world, image, view, 76, 44);
            DrawPlayerMarker(image, view);
            if (GUI.Button(mapRect, GUIContent.none, GUIStyle.none)) inventory.SetMapOpen(true);

            float x = panel.x + 9;
            float w = panel.width - 18;
            var cell = world.CellAt(player.position);
            Label(new Rect(x, panel.y + 139, w, 18), $"{L("Day")} {clock.Day}   ·   {clock.TimeLabel}", 13, Gold, bold: true);
            Label(new Rect(x, panel.y + 157, w, 17),
                $"{L(weather.Current.ToString())}  ·  {L(clock.IsNight ? "Nighttime" : "Daylight")}", 12, Text);
            Label(new Rect(x, panel.y + 174, w, 17), $"{L(world.Generator.Biome(cell.x))}   ·   {cell.x}, {cell.y}", 11, Soft);
            Label(new Rect(x, panel.y + 195, 68, 16), $"{L("Health")} {Mathf.CeilToInt(vitals.Health)}", 11, Red);
            if (sanity != null)
            {
                Label(new Rect(x + 75, panel.y + 195, 106, 16), $"{L("Sanity level")} {Mathf.CeilToInt(sanity.Value)}", 11, Mint, TextAnchor.MiddleRight);
                Meter(new Rect(x + 75, panel.y + 212, 105, 4), sanity.Value / 100, Mint);
            }
            Meter(new Rect(x, panel.y + 212, 65, 4), vitals.Health / 100, Red);
            ExperienceBar(new Rect(x, panel.y + 223, w, 22), inventory.Progress);
            var statusLine = new Rect(x, panel.y + 246, w, 23);
            string saveStatus = save != null && save.Status != "F5 save / F9 load" ? L(save.Status) : "";
            Label(statusLine, saveStatus.Length > 0 ? saveStatus : L("H  Rest   ·   F5 / F9  Save / Load"), 10, Soft);
            if (statusLine.Contains(Event.current.mousePosition) && saveStatus.Length > 0)
            {
                var status = new Rect(panel.xMax - 288, panel.yMax + 4, 288, 23);
                Plate(status, Line, Ink);
                Label(Inset(status, 4), saveStatus, 11, Text);
            }

            if (inventory.IsOpen) return;
            string message = sleep != null ? sleep.Status : "";
            if (!string.IsNullOrEmpty(message))
            {
                var notice = new Rect((Width - 590) / 2, Height - 133, 590, 27);
                Frame(notice);
                Label(Inset(notice, 4), L(message), 12, Text, TextAnchor.MiddleCenter);
            }
        }

        private void DrawWorldMap()
        {
            if (!initialized)
            {
                CenterOnPlayer();
                initialized = true;
            }

            MenuSkin.Fill(new Rect(0, 0, Width, Height), new Color(0.02f, 0.04f, 0.07f, 0.76f));
            var panel = new Rect((Width - 820) / 2, (Height - 456) / 2, 820, 456);
            Frame(panel);
            Label(new Rect(panel.x + 14, panel.y + 7, 300, 30), L("World map"), 21, Text, bold: true);
            if (Button(new Rect(panel.xMax - 372, panel.y + 10, 125, 27), L("Whole world"), overview)) overview = true;
            if (Button(new Rect(panel.xMax - 239, panel.y + 10, 125, 27), L("Find player"))) CenterOnPlayer();
            if (Button(new Rect(panel.xMax - 106, panel.y + 10, 92, 27), L("Close") + "  [M]"))
            {
                inventory.SetMapOpen(false);
                initialized = false;
                dragging = false;
            }

            var border = new Rect(panel.x + 13, panel.y + 50, panel.width - 26, 347);
            Plate(border, Line, Ink);
            var image = Inset(border, 2);
            var view = MapView(image);
            HandleMapInput(image, view);
            view = MapView(image);
            fullMap.Draw(world, image, view, 396, 174);
            DrawPlayerMarker(image, view);

            Label(new Rect(border.x, border.yMax + 2, 160, 17), $"{view.xMin} X", 10, Soft);
            Label(new Rect(border.xMax - 160, border.yMax + 2, 160, 17), $"{view.xMax - 1} X", 10, Soft, TextAnchor.MiddleRight);
            Label(new Rect(border.center.x - 180, border.yMax + 2, 360, 17),
                $"{L("Seed")} {world.Seed}   ·   {view.width} × {view.height}", 11, Soft, TextAnchor.MiddleCenter);
            DrawMarker(new Vector2(border.x + 7, panel.yMax - 19), 6);
            var cell = world.CellAt(player.position);
            Label(new Rect(border.x + 18, panel.yMax - 29, 250, 19), $"{L("Player")}  {cell.x}, {cell.y}", 12, Text);
            Label(new Rect(border.x + 255, panel.yMax - 29, border.width - 255, 19),
                L("Drag to pan   ·   Scroll to zoom"), 12, Soft, TextAnchor.MiddleRight);
        }

        private void CenterOnPlayer()
        {
            overview = false;
            mapWidth = 168;
            center = player.position;
            dragging = false;
        }

        private RectInt MapView(Rect rect) =>
            overview ? world.Bounds : ViewCentered(center, Mathf.RoundToInt(mapWidth),
                Mathf.RoundToInt(mapWidth * rect.height / rect.width));

        private RectInt ViewCentered(Vector2 target, int width, int height)
        {
            var bounds = world.Bounds;
            width = Mathf.Clamp(width, 1, bounds.width);
            height = Mathf.Clamp(height, 1, bounds.height);
            return new RectInt(
                Mathf.Clamp(Mathf.FloorToInt(target.x - width / 2f), bounds.xMin, bounds.xMax - width),
                Mathf.Clamp(Mathf.FloorToInt(target.y - height / 2f), bounds.yMin, bounds.yMax - height),
                width, height);
        }

        private void HandleMapInput(Rect rect, RectInt view)
        {
            var evt = Event.current;
            Vector2 pointer = evt.mousePosition;
            if (evt.type == EventType.ScrollWheel && rect.Contains(pointer))
            {
                Vector2 anchor = new Vector2((pointer.x - rect.x) / rect.width, (rect.yMax - pointer.y) / rect.height);
                Vector2 cell = (Vector2)view.min + Vector2.Scale(anchor, view.size);
                mapWidth = Mathf.Clamp(view.width * Mathf.Pow(1.28f, evt.delta.y), 34, world.Bounds.width);
                float nextHeight = Mathf.Min(Mathf.Round(mapWidth * rect.height / rect.width), world.Bounds.height);
                center = cell + Vector2.Scale(new Vector2(0.5f, 0.5f) - anchor, new Vector2(mapWidth, nextHeight));
                overview = mapWidth >= world.Bounds.width;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(pointer) && !overview)
            {
                center = (Vector2)view.min + (Vector2)view.size / 2;
                dragging = true;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && dragging)
            {
                center += new Vector2(-evt.delta.x * view.width / rect.width, evt.delta.y * view.height / rect.height);
                center.x = Mathf.Clamp(center.x, world.Bounds.xMin + view.width / 2f, world.Bounds.xMax - view.width / 2f);
                center.y = Mathf.Clamp(center.y, world.Bounds.yMin + view.height / 2f, world.Bounds.yMax - view.height / 2f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0 && dragging)
            {
                dragging = false;
                evt.Use();
            }
        }

        private void DrawPlayerMarker(Rect rect, RectInt view)
        {
            var position = player.position;
            var point = new Vector2(rect.x + (position.x - view.xMin) * rect.width / view.width,
                rect.yMax - (position.y - view.yMin) * rect.height / view.height);
            if (rect.Contains(point))
            {
                point.x = Mathf.Clamp(point.x, rect.x + 5, rect.xMax - 5);
                point.y = Mathf.Clamp(point.y, rect.y + 5, rect.yMax - 5);
                DrawMarker(point, 7);
            }
        }

        private static void DrawMarker(Vector2 point, float size)
        {
            float x = Mathf.Round(point.x - size / 2);
            float y = Mathf.Round(point.y - size / 2);
            Plate(new Rect(x - 1, y - 1, size + 2, size + 2), Ink, Gold);
            MenuSkin.Fill(new Rect(x + 2, y + 2, size - 4, size - 4), Text);
        }

        private void OnDestroy()
        {
            minimap.Dispose();
            fullMap.Dispose();
        }
    }
}
