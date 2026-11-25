using UnityEngine;

namespace BrokenHeart.UI
{
    internal sealed class PauseBackdrop
    {
        private static readonly int Axis = Shader.PropertyToID("_BlurAxis");
        private static readonly int FlipY = Shader.PropertyToID("_FlipY");
        private Material blur;
        private RenderTexture image;

        public void Capture()
        {
            var shader = Resources.Load<Shader>("PauseBlur");
            if (shader == null || !shader.isSupported) return;
            if (blur == null) blur = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            ReleaseImage();
            int width = Mathf.Max(1, Screen.width / 2);
            int height = Mathf.Max(1, Screen.height / 2);
            var source = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            var horizontal = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                source.filterMode = horizontal.filterMode = FilterMode.Bilinear;
                source.wrapMode = horizontal.wrapMode = TextureWrapMode.Clamp;
                ScreenCapture.CaptureScreenshotIntoRenderTexture(source);
                image = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Paused world",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                // Screen capture uses the graphics backbuffer's origin.
                blur.SetFloat(FlipY, SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
                blur.SetVector(Axis, new Vector4(2.4f, 0, 0, 0));
                Graphics.Blit(source, horizontal, blur);
                blur.SetFloat(FlipY, 0);
                blur.SetVector(Axis, new Vector4(0, 1.2f, 0, 0));
                Graphics.Blit(horizontal, image, blur);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(horizontal);
                RenderTexture.ReleaseTemporary(source);
            }
        }

        public void Draw(Rect viewport, bool settings)
        {
            if (image != null) GUI.DrawTexture(viewport, image, ScaleMode.ScaleAndCrop, false);
            MenuSkin.Fill(viewport, new Color(0.025f, 0.045f, 0.08f, settings ? 0.49f : 0.24f));
        }

        public void ReleaseImage()
        {
            if (image == null) return;
            image.Release();
            Object.Destroy(image);
            image = null;
        }

        public void Dispose()
        {
            ReleaseImage();
            if (blur != null) Object.Destroy(blur);
        }
    }
}
