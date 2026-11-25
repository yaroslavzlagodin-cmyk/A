using System;
using BrokenHeart.Core.Profiles;
using UnityEngine;
namespace BrokenHeart.UI
{
    public static class DisplaySettings
    {
        public static readonly Vector2Int[] Sizes={new(960,540),new(1280,720),new(1366,768),new(1600,900),new(1920,1080),new(2560,1440)};
        public static void Apply(LibraryData data)
        {
            QualitySettings.vSyncCount=0;
            Application.targetFrameRate=data.fps;
            // The Editor Game view is not a standalone OS window.
            if(Application.isEditor)return;
            if(data.fullscreen)
                Screen.SetResolution(Screen.currentResolution.width,Screen.currentResolution.height,FullScreenMode.FullScreenWindow);
            else
                Screen.SetResolution(data.windowWidth,data.windowHeight,FullScreenMode.Windowed);
        }
    }

    internal sealed class DisplaySettingsDraft
    {
        private static readonly int[] Rates = { 30, 60, 90, 120, 144, 240 };
        private int width, height, fps;
        private bool fullscreen;

        public DisplaySettingsDraft(LibraryData library)
        {
            width = library.windowWidth;
            height = library.windowHeight;
            fps = library.fps;
            fullscreen = library.fullscreen;
        }

        public void WriteTo(LibraryData library)
        {
            library.windowWidth = width;
            library.windowHeight = height;
            library.fps = fps;
            library.fullscreen = fullscreen;
        }

        public static void DrawLanguage(Vector2 origin, string language, Action<string> changeLanguage)
        {
            MenuSkin.Label(new Rect(origin.x, origin.y + 3, 90, 28), "МОВА", 17, MenuSkin.Cyan);
            if (MenuSkin.Button(new Rect(origin.x + 97, origin.y, 133, 44), "Українська", language != "en")) changeLanguage("uk");
            if (MenuSkin.Button(new Rect(origin.x + 244, origin.y, 133, 44), "English", language == "en")) changeLanguage("en");
        }

        public void Draw(Vector2 origin, Action apply)
        {
            Rect At(float x, float y, float w, float h) => new Rect(origin.x + x, origin.y + y, w, h);
            MenuSkin.Label(At(0, 0, 700, 28), "РЕЖИМ ВІДОБРАЖЕННЯ", 17, MenuSkin.Cyan);
            if (MenuSkin.Button(At(0, 44, 330, 48), "У вікні", !fullscreen)) fullscreen = false;
            if (MenuSkin.Button(At(345, 44, 330, 48), "Повний екран", fullscreen)) fullscreen = true;
            MenuSkin.Label(At(0, 115, 700, 28), "РОЗМІР ВІКНА", 17, MenuSkin.Cyan);
            for (int i = 0; i < DisplaySettings.Sizes.Length; i++)
            {
                var size = DisplaySettings.Sizes[i];
                if (MenuSkin.Button(At((i % 3) * 230, 157 + (i / 3) * 55, 215, 44), $"{size.x} × {size.y}",
                    width == size.x && height == size.y))
                {
                    width = size.x;
                    height = size.y;
                }
            }
            MenuSkin.Label(At(0, 278, 700, 28), "ОБМЕЖЕННЯ КАДРІВ", 17, MenuSkin.Cyan);
            for (int i = 0; i < Rates.Length; i++)
                if (MenuSkin.Button(At(i * 115, 319, 105, 42), $"{Rates[i]} FPS", fps == Rates[i])) fps = Rates[i];

            if (MenuSkin.Button(At(0, 400, 300, 51), "Застосувати", true)) apply();
            if (MenuSkin.Button(At(318, 400, 357, 51), "Скинути до 1280 × 720"))
            {
                width = 1280;
                height = 720;
                fullscreen = false;
                fps = 60;
            }
        }
    }
}
