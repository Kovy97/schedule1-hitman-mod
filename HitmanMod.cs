using System;
using MelonLoader;
using S1API.Entities;
using GameNPC = Il2CppScheduleOne.NPCs.NPC;

[assembly: MelonInfo(typeof(HitmanMod.HitmanModMain), "T.H.M - The Hitman Mod", "3.0.2", "Kowyx")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace HitmanMod;

public class HitmanModMain : MelonMod
{
    public static HitmanModMain Instance { get; private set; } = null!;
    public ContractManager ContractManager { get; private set; } = null!;

    private bool _initialized;
    private bool _npcsReady;
    private bool _cableRegistered;
    private float _cableRetryTimer;
    private float _appRefreshTimer = 2f;
    private float _initCheckTimer = 5f;
    private int _npcStableChecks;
    private float _npcRecheckTimer;
    private GameNPC? _sentinelNpc;

    private const int RequiredStableChecks = 3;
    private const int MinNpcCount = 3;

    public override void OnInitializeMelon()
    {
        Instance = this;
        HitmanConfig.Initialize();
        ContractManager = new ContractManager();
        HarmonyInstance.PatchAll(typeof(HitmanModMain).Assembly);
        LoggerInstance.Msg("T.H.M - The Hitman Mod v4.0.0 loaded. Waiting for NPCs...");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        // Register custom items ASAP so the game can deserialize them from saved inventory.
        // Must happen BEFORE the game loads player inventory data.
        if (!_cableRegistered)
            TryRegisterCable();
    }

    public override void OnUpdate()
    {
        // ── Phase 0: Register custom items ASAP (retry until Registry is ready) ──
        if (!_cableRegistered)
        {
            _cableRetryTimer += UnityEngine.Time.deltaTime;
            if (_cableRetryTimer >= 3f)
            {
                _cableRetryTimer = 0f;
                TryRegisterCable();
            }
            if (!_cableRegistered) return;
        }

        // ── Phase 1: Wait for NPCs to be ready ──
        if (!_initialized)
        {
            _initCheckTimer -= UnityEngine.Time.deltaTime;
            if (_initCheckTimer > 0f) return;
            _initCheckTimer = 2f;

            if (!_npcsReady)
            {
                try
                {
                    bool customReady = false;
                    try { customReady = NPC.CustomNpcsReady; } catch { }

                    int s1apiCount = 0;
                    try { s1apiCount = NPC.All?.Count ?? 0; } catch { }

                    int gameNpcCount = 0;
                    try
                    {
                        var gameNpcs = UnityEngine.Object.FindObjectsOfType<GameNPC>();
                        gameNpcCount = gameNpcs?.Length ?? 0;
                        // Cache first NPC as sentinel for later cheap checks
                        if (gameNpcCount > 0 && _sentinelNpc == null)
                            _sentinelNpc = gameNpcs![0];
                    }
                    catch (Exception ex)
                    {
                        LoggerInstance.Warning($"Direct FindObjectsOfType<NPC> failed: {ex.Message}");
                    }

                    LoggerInstance.Msg($"NPC check: S1API={s1apiCount}, Il2Cpp_FindObjects={gameNpcCount}, CustomReady={customReady}, stable={_npcStableChecks}/{RequiredStableChecks}");

                    // S1API NPC.All is what the mod actually uses — require it specifically
                    if (s1apiCount < MinNpcCount)
                    {
                        _npcStableChecks = 0;
                        return;
                    }

                    _npcStableChecks++;
                    if (_npcStableChecks < RequiredStableChecks)
                        return;

                    _npcsReady = true;
                    LoggerInstance.Msg($"NPCs confirmed stable! S1API={s1apiCount}, Il2Cpp={gameNpcCount}");
                }
                catch (System.Exception ex)
                {
                    LoggerInstance.Warning($"NPC check failed: {ex.Message}");
                    _npcStableChecks = 0;
                    return;
                }
            }

            ContractManager.Initialize();
            ContractManager.TryResolveActiveContract();
            ContractManager.HookGameSaveEvents();
            _initialized = true;
            LoggerInstance.Msg($"HitmanMod ready! {NPC.All?.Count ?? 0} S1API NPCs available.");
            return;
        }

        // Sentinel check — only every 30s to minimize performance impact
        _npcRecheckTimer -= UnityEngine.Time.deltaTime;
        if (_npcRecheckTimer <= 0f)
        {
            _npcRecheckTimer = 30f;
            try
            {
                // Check if sentinel NPC still exists (Il2Cpp == null means destroyed)
                bool sentinelValid = false;
                try { sentinelValid = _sentinelNpc != null && _sentinelNpc.gameObject != null; }
                catch { sentinelValid = false; }

                if (!sentinelValid)
                {
                    // Sentinel gone — try to find a new one before reverting
                    _sentinelNpc = null;
                    try
                    {
                        // Use S1API NPC list instead of expensive FindObjectsOfType
                        int count = NPC.All?.Count ?? 0;
                        if (count > 0)
                        {
                            // Get any game NPC as new sentinel via S1API
                            try { _sentinelNpc = NPC.All[0].gameObject.GetComponent<GameNPC>(); } catch { }
                        }
                        else
                        {
                            LoggerInstance.Warning("No game NPCs found — reverting to waiting state.");
                            ContractManager?.UnhookGameSaveEvents();
                            ContractManager?.ForceResetAll();
                            _initialized = false;
                            _npcsReady = false;
                            _npcStableChecks = 0;
                            _initCheckTimer = 3f;
                            return;
                        }
                    }
                    catch
                    {
                        ContractManager?.UnhookGameSaveEvents();
                        ContractManager?.ForceResetAll();
                        _initialized = false;
                        _npcsReady = false;
                        _npcStableChecks = 0;
                        _initCheckTimer = 3f;
                        return;
                    }
                }
            }
            catch
            {
                ContractManager?.UnhookGameSaveEvents();
                ContractManager?.ForceResetAll();
                _initialized = false;
                _npcsReady = false;
                _npcStableChecks = 0;
                _initCheckTimer = 3f;
                return;
            }
        }

        ContractManager.Update(UnityEngine.Time.deltaTime);
        StrangleHandler.Update(UnityEngine.Time.deltaTime);

        // Lightweight timer updates (no UI rebuild)
        if (_appRefreshTimer > 0f)
            _appRefreshTimer -= UnityEngine.Time.deltaTime;
        if (_appRefreshTimer <= 0f && HitmanApp.Instance != null)
        {
            _appRefreshTimer = 1f;
            HitmanApp.Instance.UpdateTimerDisplays();
        }
    }

    private void TryRegisterCable()
    {
        try
        {
            // Check if Registry is ready by looking up a known vanilla item
            var test = Il2CppScheduleOne.Registry.GetItem("cuke");
            if (test == null)
            {
                LoggerInstance.Msg("[THM] Registry not ready yet, retrying cable registration...");
                return;
            }

            FibreGlassCable.Register();
            _cableRegistered = true;
            LoggerInstance.Msg("[THM] Fibre Glass Cable registered early — inventory can now deserialize it.");
        }
        catch (Exception ex)
        {
            LoggerInstance.Warning($"[THM] TryRegisterCable failed: {ex.Message}");
        }
    }

    public override void OnApplicationQuit()
    {
        ContractManager?.UnhookGameSaveEvents();
    }

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        StrangleHandler.Reset();
        if (_initialized)
        {
            ContractManager?.UnhookGameSaveEvents();
            ContractManager?.ForceResetAll();
        }
        _initialized = false;
        _npcsReady = false;
        _npcStableChecks = 0;
        _cableRegistered = false;
        _cableRetryTimer = 0f;
        _initCheckTimer = 5f;
        _npcRecheckTimer = 0f;
        _appRefreshTimer = 2f;
        _sentinelNpc = null;
    }
}
