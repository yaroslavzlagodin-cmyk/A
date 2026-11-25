using System.Collections.Generic;
using BrokenHeart.Player;
using UnityEngine;

namespace BrokenHeart.UI
{
    internal static class HudSkin
    {
        public static readonly Color Ink = new Color32(21, 18, 28, 255);
        public static readonly Color Panel = new Color32(33, 27, 43, 255);
        public static readonly Color Line = new Color32(83, 68, 96, 255);
        public static readonly Color Soft = new Color32(173, 157, 186, 255);
        public static readonly Color Text = new Color32(242, 233, 241, 255);
        public static readonly Color Gold = new Color32(231, 186, 105, 255);
        public static readonly Color Mint = new Color32(143, 210, 160, 255);
        public static readonly Color Red = new Color32(225, 141, 123, 255);
        private static readonly Dictionary<(int, Color, TextAnchor, bool), GUIStyle> labels = new();
        public static float Scale => Mathf.Min(Screen.width / 960f, Screen.height / 540f);
        public static float Width => Screen.width / Scale;
        public static float Height => Screen.height / Scale;
        public static string L(string text) => Localization.T(text);

        public static Rect Inset(Rect rect, float pad) =>
            new Rect(rect.x + pad, rect.y + pad, rect.width - 2 * pad, rect.height - 2 * pad);

        public static void Plate(Rect rect, Color edge, Color fill)
        {
            MenuSkin.Fill(rect, edge);
            MenuSkin.Fill(Inset(rect, 1.5f), fill);
        }

        public static void Frame(Rect rect)
        {
            MenuSkin.Fill(new Rect(rect.x + 3, rect.y + 4, rect.width, rect.height), new Color(0, 0, 0, 0.4f));
            Plate(rect, Line, Panel);
            MenuSkin.Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, 1), Soft * new Color(0.64f, 0.64f, 0.64f));
        }

        public static void Label(Rect rect, string value, int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft, bool bold = false)
        {
            var key = (size, color, alignment, bold);
            if (!labels.TryGetValue(key, out var style))
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = size,
                    fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                    alignment = alignment,
                    padding = new RectOffset(),
                    clipping = TextClipping.Clip
                };
                style.normal.textColor = color;
                labels.Add(key, style);
            }
            GUI.Label(rect, value, style);
        }

        public static bool Button(Rect rect, string value, bool selected = false, bool enabled = true)
        {
            selected &= enabled;
            bool hover = enabled && rect.Contains(Event.current.mousePosition);
            Plate(rect, selected ? Gold : hover ? Soft : Line, selected ? new Color32(62, 50, 36, 255) : hover ? Panel : Ink);
            Label(rect, value, 12, !enabled ? Soft : selected ? Gold : Text, TextAnchor.MiddleCenter, selected);
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            GUI.enabled = previous;
            return clicked;
        }

        public static void Meter(Rect rect, float fraction, Color fill)
        {
            Plate(rect, Line, Ink);
            var track = Inset(rect, 1);
            track.width = Mathf.Round(track.width * Mathf.Clamp01(fraction));
            MenuSkin.Fill(track, fill);
        }

        public static void ExperienceBar(Rect rect, PlayerProgress progress)
        {
            int current = progress.ExperienceInLevel;
            int threshold = PlayerProgress.ExperiencePerLevel;
            var label = new Rect(rect.x, rect.y, rect.width, 14);
            Label(label, $"{L("Level")} {progress.Level}", 11, Gold, bold: true);
            Label(label, $"{current}/{threshold} {L("XP")}", 10, Soft, TextAnchor.MiddleRight);
            Meter(new Rect(rect.x, rect.y + 15, rect.width, 6), (float)current / threshold, Gold);
        }

        public static float Scroll(Rect viewport, float offset, float contentHeight, ref bool dragging)
        {
            float overflow = contentHeight - viewport.height;
            if (overflow <= 0)
            {
                dragging = false;
                return 0;
            }
            var track = new Rect(viewport.xMax - 9, viewport.y, 9, viewport.height);
            float handleHeight = Mathf.Max(14, viewport.height * viewport.height / contentHeight);
            float travel = Mathf.Max(1, track.height - handleHeight - 4);
            var evt = Event.current;
            if (evt.type == EventType.ScrollWheel && viewport.Contains(evt.mousePosition))
            {
                offset += evt.delta.y * 17;
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDown && evt.button == 0 && track.Contains(evt.mousePosition)) ||
                (evt.type == EventType.MouseDrag && dragging))
            {
                dragging = true;
                offset = (evt.mousePosition.y - track.y - 2 - handleHeight / 2) * overflow / travel;
                evt.Use();
            }
            else if (dragging && evt.type == EventType.MouseUp && evt.button == 0)
            {
                dragging = false;
                evt.Use();
            }
            offset = Mathf.Clamp(offset, 0, overflow);
            Plate(track, Line, Ink);
            var handle = new Rect(track.x + 2, track.y + 2 + travel * offset / overflow, 5, handleHeight);
            MenuSkin.Fill(handle, Soft);
            return offset;
        }

        public static void Icon(Rect rect, Sprite sprite, float opacity = 1)
        {
            if (sprite == null) return;
            var source = sprite.textureRect;
            var texture = sprite.texture;
            float scale = Mathf.Min(rect.width / source.width, rect.height / source.height);
            var size = source.size * scale;
            var previous = GUI.color;
            GUI.color = new Color(1, 1, 1, opacity);
            GUI.DrawTextureWithTexCoords(new Rect(rect.center - size / 2, size), texture,
                new Rect(source.x / texture.width, source.y / texture.height, source.width / texture.width, source.height / texture.height));
            GUI.color = previous;
        }
    }
}
