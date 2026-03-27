using System;
using System.Collections;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using Il2CppAssetBundle = UnityEngine.Il2CppAssetBundleManager;

namespace HitmanMod;

/// <summary>
/// Loads the syringe model from an AssetBundle (embedded or file).
/// Simplified version of CableVisual — two poses: idle and inject.
/// </summary>
public static class SyringeVisual
{
    private static GameObject? _instance;
    private static GameObject? _prefab;
    private static Shader?     _gameShader;
    private static bool _bundleLoaded;
    private static bool _bundleFailed;
    private static bool _visible;
    private static bool _injecting;

    // Camera-relative positioning
    private static readonly Vector3 IdleOffset     = new(0.15f, -0.20f, 0.30f);
    private static readonly Vector3 InjectOffset   = new(0.15f, -0.15f, 0.45f);
    private static readonly Vector3 IdleRotation   = new(0f, 0f, 0f);
    private static readonly Vector3 InjectRotation = new(15f, 0f, 0f);

    public static void SetInjecting(bool injecting) => _injecting = injecting;

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

    public static void Destroy()
    {
        _visible = false;
        _injecting = false;
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

            if (_instance.transform.parent != cam.transform)
                _instance.transform.SetParent(cam.transform, false);

            var off = _injecting ? InjectOffset : IdleOffset;
            var rot = _injecting ? InjectRotation : IdleRotation;

            _instance.transform.localPosition = off;
            _instance.transform.localRotation = Quaternion.Euler(rot);
        }
        catch { }
    }

    /// <summary>
    /// Detach the syringe from the camera, let it drop with physics,
    /// and destroy it after a few seconds. Used after injection (consumed).
    /// </summary>
    public static void DropSyringe()
    {
        if (_instance == null) return;

        try
        {
            var dropped = _instance;
            _instance = null;
            _visible = false;
            _injecting = false;

            // Detach from camera → world space
            dropped.transform.SetParent(null, true);

            // Remove "Poison_Liquid" child to show empty syringe
            foreach (var t in dropped.GetComponentsInChildren<Transform>())
            {
                try
                {
                    if (t != null && t.gameObject.name.Contains("Poison"))
                    {
                        t.gameObject.SetActive(false);
                        break;
                    }
                }
                catch { }
            }

            // Add physics so it falls
            var rb = dropped.AddComponent<Rigidbody>();
            rb.mass = 0.1f;
            rb.angularDrag = 0.5f;

            // Small random spin for natural look
            rb.AddTorque(
                UnityEngine.Random.Range(-2f, 2f),
                UnityEngine.Random.Range(-2f, 2f),
                UnityEngine.Random.Range(-2f, 2f),
                ForceMode.Impulse);

            // Add a collider so it bounces off the ground
            var col = dropped.AddComponent<BoxCollider>();
            var renderers = dropped.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                col.center = dropped.transform.InverseTransformPoint(bounds.center);
                col.size = dropped.transform.InverseTransformVector(bounds.size);
            }

            // Destroy after 15 seconds
            MelonCoroutines.Start(DestroyAfterDelay(dropped, 15f));

            Melon<HitmanModMain>.Logger.Msg("[THM] Syringe dropped (empty).");
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] DropSyringe failed: {ex.Message}");
        }
    }

    private static IEnumerator DestroyAfterDelay(GameObject obj, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        try { if (obj != null) UnityEngine.Object.Destroy(obj); } catch { }
    }

    // ── Bundle Loading ──────────────────────────────────────────────────────

    private static void LoadBundle()
    {
        try
        {
            byte[]? bytes = null;

            // 1. Try embedded resource
            try
            {
                var assembly = typeof(HitmanModMain).Assembly;
                using var stream = assembly.GetManifestResourceStream("HitmanMod.Resources.syringe");
                if (stream != null)
                {
                    bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                }
            }
            catch { }

            // 2. Fallback: file in Mods folder
            if (bytes == null)
            {
                string[] candidates = new[]
                {
                    Path.Combine(MelonLoader.Utils.MelonEnvironment.ModsDirectory, "syringe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "syringe"),
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        bytes = File.ReadAllBytes(c);
                        break;
                    }
                }
            }

            if (bytes == null || bytes.Length == 0)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] Syringe bundle not found (embedded or file). Visual will be disabled.");
                _bundleFailed = true;
                return;
            }

            Il2CppStructArray<byte> il2cppBytes = bytes;
            var bundle = Il2CppAssetBundle.LoadFromMemory(il2cppBytes);
            if (bundle == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] Syringe bundle LoadFromMemory returned null!");
                _bundleFailed = true;
                return;
            }

            // Load prefab — try common names
            _prefab = bundle.LoadAsset<GameObject>("syringe");
            if (_prefab == null)
                _prefab = bundle.LoadAsset<GameObject>("Assets/Syringe/syringe.fbx");
            if (_prefab == null)
            {
                var all = bundle.LoadAllAssets<GameObject>();
                if (all != null && all.Length > 0)
                    _prefab = all[0];
            }

            if (_prefab == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] No GameObject found in syringe bundle.");
                _bundleFailed = true;
                bundle.Unload(false);
                return;
            }

            _bundleLoaded = true;
            Melon<HitmanModMain>.Logger.Msg($"[THM] Syringe prefab loaded: '{_prefab.name}'");

            StealGameShader();
            bundle.Unload(false);
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Syringe bundle load failed: {ex.Message}");
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
            _instance.name = "THM_SyringeModel";
            UnityEngine.Object.DontDestroyOnLoad(_instance);

            foreach (var col in _instance.GetComponentsInChildren<Collider>())
                try { UnityEngine.Object.Destroy(col); } catch { }
            foreach (var cam in _instance.GetComponentsInChildren<Camera>())
                try { UnityEngine.Object.Destroy(cam.gameObject); } catch { }
            foreach (var light in _instance.GetComponentsInChildren<Light>())
                try { UnityEngine.Object.Destroy(light.gameObject); } catch { }

            // Double the model size
            _instance.transform.localScale = Vector3.one * 2f;

            if (_gameShader != null)
                FixMaterials(_instance);

            _visible = true;
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Syringe instantiate failed: {ex.Message}");
        }
    }

    // ── Shader Stealing ──────────────────────────────────────────────────

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
                        Melon<HitmanModMain>.Logger.Msg($"[THM] Syringe acquired game shader: '{shaderName}'");
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
                        Melon<HitmanModMain>.Logger.Msg($"[THM] Syringe acquired shader: '{mats[0].shader.name}' (fallback)");
                        return;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Syringe StealGameShader failed: {ex.Message}");
        }
    }

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

                        Color col = Color.white;
                        if (oldMat.HasProperty("_BaseColor")) col = oldMat.GetColor("_BaseColor");
                        else if (oldMat.HasProperty("_Color")) col = oldMat.GetColor("_Color");

                        if (newMat.HasProperty("_BaseColor")) newMat.SetColor("_BaseColor", col);
                        if (newMat.HasProperty("_Color"))     newMat.SetColor("_Color", col);

                        if (oldMat.HasProperty("_Metallic") && newMat.HasProperty("_Metallic"))
                            newMat.SetFloat("_Metallic", oldMat.GetFloat("_Metallic"));
                        if (oldMat.HasProperty("_Smoothness") && newMat.HasProperty("_Smoothness"))
                            newMat.SetFloat("_Smoothness", oldMat.GetFloat("_Smoothness"));
                    }

                    newMats[i] = newMat;
                }

                renderer.materials = newMats;
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Syringe FixMaterials failed: {ex.Message}");
        }
    }
}
