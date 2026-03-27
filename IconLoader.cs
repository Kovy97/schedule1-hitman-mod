using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace HitmanMod;

/// <summary>
/// Loads embedded PNG resources as Unity Sprites for item icons.
/// </summary>
public static class IconLoader
{
    /// <summary>
    /// Load a PNG from embedded resources and convert to a Sprite.
    /// </summary>
    public static Sprite? Load(string resourceName)
    {
        try
        {
            var assembly = typeof(HitmanModMain).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Melon<HitmanModMain>.Logger.Warning($"[THM] Icon not found: {resourceName}");
                return null;
            }

            var managedBytes = new byte[stream.Length];
            stream.Read(managedBytes, 0, managedBytes.Length);
            Il2CppStructArray<byte> bytes = managedBytes;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes))
            {
                Melon<HitmanModMain>.Logger.Warning($"[THM] Failed to decode icon: {resourceName}");
                return null;
            }

            tex.filterMode = FilterMode.Bilinear;
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);

            Melon<HitmanModMain>.Logger.Msg($"[THM] Icon loaded: {resourceName} ({tex.width}x{tex.height})");
            return sprite;
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] IconLoader.Load failed for {resourceName}: {ex.Message}");
            return null;
        }
    }
}
