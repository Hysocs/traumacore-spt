using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace TraumaCore
{
    internal static class EffectIconLoader
    {
        private const string ResourcePrefix = "TraumaCore.Icons.";
        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>();

        internal static Sprite LoadEffectIcon(string fileName)
        {
            if (Cache.TryGetValue(fileName, out Sprite cached)) return cached;
            Assembly assembly = typeof(EffectIconLoader).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(
                ResourcePrefix + fileName))
            {
                if (stream == null) return null;
                byte[] bytes = new byte[stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) return null;
                    offset += read;
                }

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32,
                    false, false);
                texture.name = "TraumaCore " + fileName;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                if (!texture.LoadImage(bytes, false))
                {
                    Object.Destroy(texture);
                    return null;
                }
                Sprite sprite = Sprite.Create(texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                sprite.name = texture.name;
                Cache[fileName] = sprite;
                return sprite;
            }
        }
    }
}
