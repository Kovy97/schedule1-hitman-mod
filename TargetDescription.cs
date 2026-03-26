using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MelonLoader;

namespace HitmanMod;

/// <summary>
/// Generates a human-readable description of an NPC's appearance
/// from their Avatar settings (hair, clothing, accessories, etc.)
/// </summary>
public static class TargetDescription
{
    public static string GenerateFromGameNpc(Il2CppScheduleOne.NPCs.NPC? gameNpc)
    {
        try
        {
            if (gameNpc?.Avatar == null) return "No description available.";
            var avatar = gameNpc.Avatar;
            var settings = avatar.CurrentSettings;
            if (settings == null) return "No description available.";

            var sb = new StringBuilder();

            bool isMale = avatar.IsMale();
            sb.Append(isMale ? "Male" : "Female");

            string hairPath = settings.HairPath ?? "";
            string hairStyle = ExtractHairStyle(hairPath);
            string hairColor = ColorToName(settings.HairColor);

            if (hairStyle != "bald" && !avatar.wearingHairBlockingAccessory && !string.IsNullOrEmpty(hairStyle))
                sb.Append($" with {hairColor} {hairStyle}");

            sb.Append(". ");

            var clothing = new List<string>();
            try
            {
                var bodyLayers = settings.BodyLayerSettings;
                if (bodyLayers != null)
                {
                    for (int i = 0; i < bodyLayers.Count; i++)
                    {
                        var layer = bodyLayers[i];
                        string path = layer.layerPath ?? "";
                        string itemName = ExtractClothingName(path);
                        if (string.IsNullOrEmpty(itemName) || path.Contains("/Face/")) continue;
                        clothing.Add($"{ColorToName(layer.layerTint)} {itemName}");
                    }
                }
            }
            catch { }

            string headwear = "";
            try
            {
                var accSettings = settings.AccessorySettings;
                if (accSettings != null)
                {
                    for (int i = 0; i < accSettings.Count; i++)
                    {
                        var acc = accSettings[i];
                        string path = acc.path ?? "";
                        string itemName = ExtractAccessoryName(path);
                        if (string.IsNullOrEmpty(itemName)) continue;
                        string color = ColorToName(acc.color);

                        if (path.Contains("/Head/")) headwear = $"{color} {itemName}";
                        else if (path.Contains("/Feet/")) clothing.Add($"{color} {itemName}");
                        else if (path.Contains("/Chest/")) clothing.Insert(0, $"{color} {itemName}");
                    }
                }
            }
            catch { }

            if (clothing.Count > 0) { sb.Append("Wearing "); sb.Append(JoinList(clothing)); sb.Append(". "); }
            if (!string.IsNullOrEmpty(headwear)) sb.Append($"Wearing a {headwear}. ");

            return sb.ToString().Trim();
        }
        catch (System.Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] TargetDescription failed: {ex.Message}");
            return "No description available.";
        }
    }

    public static string Generate(S1API.Entities.NPC npc)
    {
        var gameNpc = npc.gameObject?.GetComponent<Il2CppScheduleOne.NPCs.NPC>();
        return GenerateFromGameNpc(gameNpc);
    }

    // ── Hair style mapping ──────────────────────────────────

    private static string ExtractHairStyle(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";

        string lower = path.ToLower();
        if (lower.Contains("bald")) return "bald";
        if (lower.Contains("buzzcut")) return "short hair";
        if (lower.Contains("spiky")) return "spiky hair";
        if (lower.Contains("shoulderlength")) return "shoulder-length hair";
        if (lower.Contains("longslicked")) return "slicked-back hair";
        if (lower.Contains("ponytail")) return "ponytail";
        if (lower.Contains("mohawk")) return "mohawk";
        if (lower.Contains("afro")) return "afro";
        if (lower.Contains("curly")) return "curly hair";
        if (lower.Contains("dreadlock")) return "dreadlocks";

        // Fallback: extract last segment
        string[] parts = path.Split('/');
        if (parts.Length > 0)
            return parts[^1].ToLower() + " hair";
        return "hair";
    }

    // ── Clothing name extraction ─────────────────────────────

    private static string ExtractClothingName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";

        // "Avatar/Layers/Top/Tucked T-Shirt" → "tucked t-shirt"
        // "Avatar/Layers/Bottom/Jeans" → "jeans"
        string[] parts = path.Split('/');
        if (parts.Length < 2) return "";

        string item = parts[^1];

        // Skip non-clothing items
        string lower = item.ToLower();
        if (lower.Contains("face") || lower.Contains("tattoo") || lower.Contains("facial") ||
            lower.Contains("nipple") || lower.Contains("chesthair") || lower.Contains("underwear") ||
            lower.Contains("freckle") || lower.Contains("wrinkle") || lower.Contains("eyeshadow"))
            return "";

        return item.ToLower();
    }

    private static string ExtractAccessoryName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";

        // "Avatar/Accessories/Head/Beanie/Beanie" → "beanie"
        // "Avatar/Accessories/Feet/DressShoes/DressShoes" → "dress shoes"
        string[] parts = path.Split('/');
        if (parts.Length < 2) return "";

        string item = parts[^1];

        // Make readable
        var readable = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Sneakers", "sneakers" },
            { "DressShoes", "dress shoes" },
            { "CombatBoots", "combat boots" },
            { "Flats", "flats" },
            { "Sandals", "sandals" },
            { "Beanie", "beanie" },
            { "Cap", "cap" },
            { "Cap_FastFood", "fast food cap" },
            { "BucketHat", "bucket hat" },
            { "CowboyHat", "cowboy hat" },
            { "FlatCap", "flat cap" },
            { "PorkpieHat", "porkpie hat" },
            { "CollarJacket", "collar jacket" },
            { "OpenVest", "open vest" },
            { "Blazer", "blazer" },
            { "BulletproofVest", "bulletproof vest" },
            { "LegendSunglasses", "sunglasses" },
            { "Oakleys", "sunglasses" },
            { "RectangleFrameGlasses", "glasses" },
            { "SmallRoundGlasses", "round glasses" },
            { "GoldChain", "gold chain" },
            { "Polex", "watch" },
            { "Respirator", "respirator" },
        };

        return readable.TryGetValue(item, out string name) ? name : item.ToLower();
    }

    // ── Color to human-readable name ────────────────────────

    private static string ColorToName(Color c)
    {
        float h, s, v;
        Color.RGBToHSV(c, out h, out s, out v);

        // Very dark → black
        if (v < 0.15f) return "black";

        // Very light → white
        if (v > 0.85f && s < 0.15f) return "white";

        // Gray (low saturation)
        if (s < 0.15f)
        {
            if (v < 0.4f) return "dark gray";
            if (v < 0.7f) return "gray";
            return "light gray";
        }

        // Colored
        // h is 0-1: 0=red, 0.08=orange, 0.12=yellow/blonde, 0.25=green, 0.5=cyan, 0.58=blue, 0.75=purple, 0.9=pink
        if (h < 0.04f || h > 0.95f) return v < 0.4f ? "dark red" : "red";
        if (h < 0.1f) return v < 0.5f ? "brown" : "orange";
        if (h < 0.18f) return s < 0.5f ? "blonde" : "yellow";
        if (h < 0.42f) return v < 0.4f ? "dark green" : "green";
        if (h < 0.55f) return "teal";
        if (h < 0.7f) return v < 0.4f ? "dark blue" : "blue";
        if (h < 0.82f) return "purple";
        return "pink";
    }

    // ── List joining ────────────────────────────────────────

    private static string JoinList(List<string> items)
    {
        if (items.Count == 0) return "";
        if (items.Count == 1) return items[0];
        if (items.Count == 2) return $"{items[0]} and {items[1]}";

        var sb = new StringBuilder();
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0 && i == items.Count - 1)
                sb.Append(" and ");
            else if (i > 0)
                sb.Append(", ");
            sb.Append(items[i]);
        }
        return sb.ToString();
    }
}
