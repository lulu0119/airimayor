using System;
using UnityEngine;

namespace CS2MCP
{
    /// <summary>
    /// UI-only thumbnails for image tools. The model keeps the full-resolution
    /// PNG; the chat window gets a small JPEG data URI. Best-effort by design:
    /// any failure returns null and the tool result stays text-only.
    /// Must run on the main thread (Unity texture API).
    /// </summary>
    public static class ToolPreview
    {
        private const int MaxWidth = 480;
        private const int Quality = 60;

        public static byte[] EncodeThumbnail(Texture2D source, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (source == null || source.width <= 0 || source.height <= 0)
            {
                return null;
            }
            // The thumbnail keeps the source aspect, so the source size fully
            // describes the preview aspect for UI layout (Gameface exposes no
            // naturalWidth/naturalHeight on img).
            width = source.width;
            height = source.height;
            Texture2D thumbnail = null;
            try
            {
                int targetWidth = Math.Min(source.width, MaxWidth);
                int targetHeight = Math.Max(1, Mathf.RoundToInt((float)source.height * targetWidth / source.width));
                if (targetWidth == source.width && targetHeight == source.height)
                {
                    return ImageConversion.EncodeToJPG(source, Quality);
                }
                RenderTexture rented = RenderTexture.GetTemporary(targetWidth, targetHeight, 0);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(source, rented);
                    RenderTexture.active = rented;
                    thumbnail = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                    thumbnail.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                    thumbnail.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rented);
                }
                return ImageConversion.EncodeToJPG(thumbnail, Quality);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (thumbnail != null)
                {
                    UnityEngine.Object.Destroy(thumbnail);
                }
            }
        }
    }
}
