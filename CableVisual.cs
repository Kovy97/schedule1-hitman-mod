using System;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using Il2CppAssetBundle = UnityEngine.Il2CppAssetBundleManager;

namespace HitmanMod;

/// <summary>
/// Loads the garrote model from an AssetBundle using MelonLoader's
/// Il2CppAssetBundleManager (same pattern as T.R.E.E. mod).
/// Steals the game's URP shader to fix pink/invisible materials.
/// </summary>
public static class CableVisual
{
    private static GameObject? _instance;
    private static GameObject? _prefab;
    private static Shader?     _gameShader;
    private static bool _bundleLoaded;
    private static bool _bundleFailed;
    private static bool _visible;
    private static bool _strangling;

    // Camera-relative positioning
    private static readonly Vector3 IdleOffset       = new(0f, -0.22f, 0.35f);
    private static readonly Vector3 StrangleOffset   = new(0f, -0.10f, 0.40f);
    private static readonly Vector3 IdleRotation     = new(0f, 0f, 0f);
    private static readonly Vector3 StrangleRotation = new(10f, 0f, 0f);

    public static void SetStrangling(bool strangling) => _strangling = strangling;

    public static void SetVisible(bool visible)
    {
        if (visible && !_bundleLoaded && !_bundleFailed) LoadBundle();
        if (_bundleFailed) return;

        if (visible && _instance == null) CreateInstance();
        if (_instance == null) return;

        if (visible == _visible) return;
        _visible = visible;
        try { _instance.SetActive(visible); } catch { }
    }

    public static void Show() => SetVisible(true);
    public static void Hide() => SetVisible(false);

    public static void Destroy()
    {
        _visible = false;
        try { if (_instance != null) UnityEngine.Object.Destroy(_instance); } catch { }
        _instance = null;
    }

    public static void Update()
    {
        if (!_visible || _instance == null) return;

        try
        {
            var cam = Camera.main;
            if (cam == null) return;
            var camT = cam.transform;

            var off = _strangling ? StrangleOffset : IdleOffset;
            var rot = _strangling ? StrangleRotation : IdleRotation;

            _instance.transform.position = camT.position
                + camT.right   * off.x
                + camT.up      * off.y
                + camT.forward * off.z;

            _instance.transform.rotation = camT.rotation * Quaternion.Euler(rot);
        }
        catch { }
    }

    // ── Bundle Loading (same pattern as T.R.E.E. mod) ──────────────────────

    private static void LoadBundle()
    {
        try
        {
            byte[]? bytes = null;

            // 1. Try embedded resource first (baked into DLL)
            try
            {
                var assembly = typeof(HitmanModMain).Assembly;
                using var stream = assembly.GetManifestResourceStream("HitmanMod.Resources.garrote");
                if (stream != null)
                {
                    bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    Melon<HitmanModMain>.Logger.Msg($"[THM] Loaded garrote from embedded resource: {bytes.Length:N0} bytes.");
                }
            }
            catch { }

            // 2. Fallback: load from file in Mods folder
            if (bytes == null)
            {
                string[] candidates = new[]
                {
                    Path.Combine(MelonLoader.Utils.MelonEnvironment.ModsDirectory, "garrote"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "garrote"),
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        bytes = File.ReadAllBytes(c);
                        Melon<HitmanModMain>.Logger.Msg($"[THM] Loaded garrote from file: {bytes.Length:N0} bytes.");
                        break;
                    }
                }
            }

            if (bytes == null || bytes.Length == 0)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] Garrote bundle not found (embedded or file).");
                _bundleFailed = true;
                return;
            }

            Il2CppStructArray<byte> il2cppBytes = bytes;
            var bundle = Il2CppAssetBundle.LoadFromMemory(il2cppBytes);
            if (bundle == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] Il2CppAssetBundleManager.LoadFromMemory returned null!");
                _bundleFailed = true;
                return;
            }

            Melon<HitmanModMain>.Logger.Msg("[THM] Bundle loaded. Listing assets...");

            // List all assets
            try
            {
                var names = bundle.GetAllAssetNames();
                foreach (var n in names)
                    Melon<HitmanModMain>.Logger.Msg($"[THM]   Asset: {n}");
            }
            catch { }

            // Load the prefab
            _prefab = bundle.LoadAsset<GameObject>("cablewire");
            if (_prefab == null)
                _prefab = bundle.LoadAsset<GameObject>("Assets/Garrote/cablewire.fbx");
            if (_prefab == null)
            {
                // Try loading any GameObject
                var all = bundle.LoadAllAssets<GameObject>();
                if (all != null && all.Length > 0)
                    _prefab = all[0];
            }

            if (_prefab == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] No GameObject found in bundle.");
                _bundleFailed = true;
                bundle.Unload(false);
                return;
            }

            _bundleLoaded = true;
            Melon<HitmanModMain>.Logger.Msg($"[THM] Garrote prefab loaded: '{_prefab.name}'");

            // Steal game shader for material fix
            StealGameShader();

            bundle.Unload(false);
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Bundle load failed: {ex.Message}");
            _bundleFailed = true;
        }
    }

    // ── Instance Creation ──────────────────────────────────────────────────

    private static void CreateInstance()
    {
        if (_prefab == null) return;

        try
        {
            _instance = UnityEngine.Object.Instantiate(_prefab);
            _instance.name = "THM_GarroteModel";
            UnityEngine.Object.DontDestroyOnLoad(_instance);

            // Remove colliders, cameras, and lights that came from the FBX
            foreach (var col in _instance.GetComponentsInChildren<Collider>())
                try { UnityEngine.Object.Destroy(col); } catch { }
            foreach (var cam in _instance.GetComponentsInChildren<Camera>())
                try { UnityEngine.Object.Destroy(cam.gameObject); } catch { }
            foreach (var light in _instance.GetComponentsInChildren<Light>())
                try { UnityEngine.Object.Destroy(light.gameObject); } catch { }

            // Fix materials with game shader
            if (_gameShader != null)
                FixMaterials(_instance);

            _visible = true;
            Melon<HitmanModMain>.Logger.Msg("[THM] Garrote model instantiated.");
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Instantiate failed: {ex.Message}");
        }
    }

    // ── Shader Stealing (from T.R.E.E. mod pattern) ────────────────────────

    private static void StealGameShader()
    {
        if (_gameShader != null) return;

        try
        {
            var allRenderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var goName = allRenderers[i].gameObject.name;
                if (goName.StartsWith("THM_") || goName.StartsWith("Kowyx_")) continue;

                var mats = allRenderers[i].sharedMaterials;
                if (mats == null || mats.Length == 0) continue;

                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null || mats[m].shader == null) continue;
                    string shaderName = mats[m].shader.name;
                    if (shaderName.Contains("Universal Render Pipeline/Lit") || shaderName.Contains("URP/Lit"))
                    {
                        _gameShader = mats[m].shader;
                        Melon<HitmanModMain>.Logger.Msg($"[THM] Acquired game shader: '{shaderName}'");
                        return;
                    }
                }
            }

            // Fallback: any shader
            for (int i = 0; i < allRenderers.Length; i++)
            {
                try
                {
                    var mats = allRenderers[i].sharedMaterials;
                    if (mats != null && mats.Length > 0 && mats[0]?.shader != null)
                    {
                        _gameShader = mats[0].shader;
                        Melon<HitmanModMain>.Logger.Msg($"[THM] Fallback shader: '{mats[0].shader.name}'");
                        return;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] StealGameShader failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Replace all materials on the model with the game's URP shader,
    /// preserving textures from the bundle.
    /// </summary>
    private static void FixMaterials(GameObject root)
    {
        try
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var mats = renderer.sharedMaterials;
                if (mats == null) continue;

                var newMats = new Il2CppReferenceArray<Material>(mats.Length);
                for (int i = 0; i < mats.Length; i++)
                {
                    var oldMat = mats[i];
                    var newMat = new Material(_gameShader!);
                    newMat.name = $"THM_{oldMat?.name ?? "mat"}";

                    // Copy textures from original material
                    if (oldMat != null)
                    {
                        Texture? tex = null;
                        if (oldMat.HasProperty("_BaseMap"))   tex = oldMat.GetTexture("_BaseMap");
                        if (tex == null && oldMat.HasProperty("_MainTex")) tex = oldMat.GetTexture("_MainTex");

                        if (tex != null)
                        {
                            if (newMat.HasProperty("_BaseMap")) newMat.SetTexture("_BaseMap", tex);
                            if (newMat.HasProperty("_MainTex")) newMat.SetTexture("_MainTex", tex);
                        }

                        // Copy color
                        Color col = Color.white;
                        if (oldMat.HasProperty("_BaseColor")) col = oldMat.GetColor("_BaseColor");
                        else if (oldMat.HasProperty("_Color")) col = oldMat.GetColor("_Color");

                        if (newMat.HasProperty("_BaseColor")) newMat.SetColor("_BaseColor", col);
                        if (newMat.HasProperty("_Color"))     newMat.SetColor("_Color", col);

                        // Metallic/roughness
                        if (oldMat.HasProperty("_Metallic") && newMat.HasProperty("_Metallic"))
                            newMat.SetFloat("_Metallic", oldMat.GetFloat("_Metallic"));
                        if (oldMat.HasProperty("_Smoothness") && newMat.HasProperty("_Smoothness"))
                            newMat.SetFloat("_Smoothness", oldMat.GetFloat("_Smoothness"));
                    }

                    newMats[i] = newMat;
                }

                renderer.materials = newMats;
            }

            Melon<HitmanModMain>.Logger.Msg("[THM] Materials fixed with game shader.");
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] FixMaterials failed: {ex.Message}");
        }
    }
}
