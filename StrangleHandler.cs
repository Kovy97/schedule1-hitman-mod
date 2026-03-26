using System;
using HarmonyLib;
using Il2CppScheduleOne.UI;
using MelonLoader;
using S1API.Entities;
using S1API.UI;
using UnityEngine;
using GameNPC       = Il2CppScheduleOne.NPCs.NPC;
using GamePlayer    = Il2CppScheduleOne.PlayerScripts.Player;
using UICanvasScaler = UnityEngine.UI.CanvasScaler;
using UIText         = UnityEngine.UI.Text;

namespace HitmanMod;

/// <summary>
/// Handles the strangulation mechanic.
/// Target detection: camera raycast (same approach the game uses for interactions).
/// Crime system: nearby NPCs witness the crime via awareness events + direct wanted level fallback.
/// </summary>
public static class StrangleHandler
{
    private const float MaxRayDist = 3.0f;   // max ray distance for target
    private const float BehindDot  = 0.2f;   // player must be behind NPC (dot ≤ this)
    private static float _debugTimer;
    private static bool  _crimeReported;

    private static NPC?     _pendingTarget;
    private static GameNPC? _pendingGameNpc;   // raw game NPC (for non-S1API NPCs)
    private static GameNPC? _frozenGameNpc;
    private static float    _savedMoveSpeed;
    private static float    _savedMoveScale;

    // HUD overlay
    private static GameObject? _hudRoot;
    private static UIText?      _hudText;
    private static bool         _hudVisible;

    // ── Public API ────────────────────────────────────────────────────────────

    public static void Update(float dt)
    {
        if (StrangleMiniGame.IsActive)
        {
            if (!_crimeReported)
                ApplyCrimeVisibility();
            CableVisual.SetStrangling(true);
            CableVisual.Update();
            StrangleMiniGame.Update(dt);
            return;
        }

        bool cableEquipped = false;
        bool cableReady = false;
        try
        {
            cableEquipped = IsCableEquipped();
            if (cableEquipped)
            {
                var gp = GamePlayer.Local;
                if (gp != null && gp.Crouched)
                    cableReady = true;
            }
        }
        catch { }

        // Show/hide cable visual when equipped (toggle visibility, don't recreate)
        CableVisual.SetVisible(cableEquipped);
        if (cableEquipped)
        {
            CableVisual.SetStrangling(false);
            CableVisual.Update();
        }

        SetHudVisible(cableReady);
        if (!cableReady) return;

        // Debug
        _debugTimer -= dt;
        if (_debugTimer <= 0f)
        {
            _debugTimer = 3f;
            DebugLogRaycast();
        }

        // E pressed → raycast for target
        try
        {
            if (Il2CppScheduleOne.GameInput.GetButtonDown(Il2CppScheduleOne.GameInput.ButtonCode.Interact))
            {
                var gameNpc = RaycastForNpc();
                if (gameNpc != null)
                {
                    // Check behind
                    var gp = GamePlayer.Local;
                    var playerPos = gp.transform.position;
                    var npcPos = gameNpc.transform.position;
                    var npcFwd = gameNpc.transform.forward;
                    var npcToPlayer = (playerPos - npcPos).normalized;
                    float dot = Vector3.Dot(npcFwd, npcToPlayer);

                    if (dot <= BehindDot)
                    {
                        // Resolve S1API wrapper (for Kill); null is OK — we use gameNpc fallback
                        _pendingTarget = ResolveS1ApiNpc(gameNpc);
                        _pendingGameNpc = gameNpc;
                        _crimeReported = false;
                        FreezeGameNpc(gameNpc);
                        StrangleMiniGame.Show(OnMiniGameComplete);

                        string name = "unknown";
                        try { name = gameNpc.fullName; } catch { try { name = gameNpc.gameObject.name; } catch { } }
                        Melon<HitmanModMain>.Logger.Msg($"[THM] Strangle: mini-game started on {name} (dot={dot:F2}).");
                    }
                    else
                    {
                        Melon<HitmanModMain>.Logger.Msg($"[THM] Strangle: NPC found but you're not behind them (dot={dot:F2}, need ≤{BehindDot}).");
                    }
                }
            }
        }
        catch { }
    }

    public static void Reset()
    {
        CableVisual.Destroy();
        UnfreezeNpc();
        _pendingTarget = null;
        _pendingGameNpc = null;
        StrangleMiniGame.ForceClose();
        SetHudVisible(false);
    }

    public static bool IsCableEquipped()
    {
        try
        {
            var gp = GamePlayer.Local;
            if (gp == null) return false;
            var item = gp.GetEquippedItem();
            return item != null && item.Definition?.ID == FibreGlassCable.ItemId;
        }
        catch { return false; }
    }

    // ── Harmony: block pickpocket when cable is equipped ────────────────────

    [HarmonyPatch(typeof(PickpocketScreen), "Open")]
    public static class PickpocketScreenOpenPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !IsCableEquipped();
    }

    // ── Target detection via camera raycast ──────────────────────────────────

    /// <summary>
    /// Casts a ray from the main camera forward. Returns the first GameNPC hit, or null.
    /// This is how the game detects interactions — works for ALL NPCs regardless of mod.
    /// </summary>
    private static GameNPC? RaycastForNpc()
    {
        try
        {
            var cam = Camera.main;
            if (cam == null) return null;

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, MaxRayDist))
                return null;

            var go = hit.collider.gameObject;
            var npc = go.GetComponent<GameNPC>();
            if (npc == null) npc = go.GetComponentInParent<GameNPC>();
            if (npc == null) return null;

            // Skip dead/KO
            try { if (npc.Health.IsDead) return null; } catch { }

            return npc;
        }
        catch { return null; }
    }

    /// <summary>Find the S1API NPC wrapper for a game NPC (may be null for non-S1API NPCs).</summary>
    private static NPC? ResolveS1ApiNpc(GameNPC gameNpc)
    {
        try
        {
            int id = gameNpc.gameObject.GetInstanceID();
            foreach (var n in NPC.All)
            {
                try { if (n.gameObject.GetInstanceID() == id) return n; } catch { }
            }
        }
        catch { }
        return null;
    }

    private static void DebugLogRaycast()
    {
        try
        {
            var cam = Camera.main;
            if (cam == null) { Melon<HitmanModMain>.Logger.Msg("[THM] Strangle debug: no main camera"); return; }

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (Physics.Raycast(ray, out var hit, MaxRayDist))
            {
                var go = hit.collider.gameObject;
                var npc = go.GetComponent<GameNPC>() ?? go.GetComponentInParent<GameNPC>();
                if (npc != null)
                {
                    var gp = GamePlayer.Local;
                    var npcFwd = npc.transform.forward;
                    var npcToPlayer = (gp.transform.position - npc.transform.position).normalized;
                    float dot = Vector3.Dot(npcFwd, npcToPlayer);
                    string name = "?";
                    try { name = npc.fullName; } catch { }
                    Melon<HitmanModMain>.Logger.Msg($"[THM] Strangle debug: looking at {name}, dist={hit.distance:F1}m, dot={dot:F2} (need ≤{BehindDot})");
                }
                else
                {
                    Melon<HitmanModMain>.Logger.Msg($"[THM] Strangle debug: looking at {go.name} (not an NPC), dist={hit.distance:F1}m");
                }
            }
            else
            {
                Melon<HitmanModMain>.Logger.Msg("[THM] Strangle debug: raycast hit nothing within 3m");
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Strangle debug failed: {ex.Message}");
        }
    }

    // ── Mini-game callback ────────────────────────────────────────────────────

    private static void OnMiniGameComplete(bool success)
    {
        UnfreezeNpc();
        // Remove passive visibility state now — witnesses were already notified directly
        // in ApplyCrimeVisibility(). This prevents late-arriving NPCs from reacting.
        RemoveCrimeVisibility();

        if (success)
        {
            // Report bonuses to ContractManager
            var mgr = HitmanModMain.Instance?.ContractManager;
            if (mgr != null)
            {
                mgr.SetKillUsedCable();

                // Check game's pursuit level — if police are alerted, there were witnesses
                try
                {
                    var gp = GamePlayer.Local;
                    if (gp?.CrimeData != null)
                    {
                        var level = gp.CrimeData.CurrentPursuitLevel;
                        if ((int)level > 0)
                            mgr.SetKillHadWitnesses();
                    }
                }
                catch { }

                // Snapshot weather & time for bonus calculation
                mgr.SnapshotKillConditions();
            }
            TryKillTarget();
        }
        else
        {
            MakeTargetAggressive();
            Melon<HitmanModMain>.Logger.Msg("[THM] Strangle: player missed — target fights back!");
        }

        _pendingTarget = null;
        _pendingGameNpc = null;
    }

    private static void TryKillTarget()
    {
        // Try S1API Kill first, then direct health damage
        if (_pendingTarget != null)
        {
            try
            {
                _pendingTarget.Kill();
                Melon<HitmanModMain>.Logger.Msg($"[THM] Strangle: {_pendingTarget.FullName} eliminated (S1API).");
                return;
            }
            catch (Exception ex)
            {
                Melon<HitmanModMain>.Logger.Warning($"[THM] S1API Kill failed: {ex.Message}");
            }
        }

        // Fallback: direct game NPC health
        if (_pendingGameNpc != null)
        {
            try
            {
                var health = _pendingGameNpc.Health;
                if (health != null)
                {
                    health.TakeDamage(health.MaxHealth * 10f, true);
                    string name = "?";
                    try { name = _pendingGameNpc.fullName; } catch { }
                    Melon<HitmanModMain>.Logger.Msg($"[THM] Strangle: {name} eliminated (direct health).");
                }
            }
            catch (Exception ex)
            {
                Melon<HitmanModMain>.Logger.Warning($"[THM] Direct kill failed: {ex.Message}");
            }
        }
    }

    private static void MakeTargetAggressive()
    {
        if (_pendingGameNpc == null) return;
        try
        {
            var gp = GamePlayer.Local;
            if (gp == null) return;

            // Max out aggression so the NPC wants to fight
            _pendingGameNpc.OverrideAggression(1f);

            // Set combat target to the player
            var combat = _pendingGameNpc.Behaviour?.CombatBehaviour;
            if (combat != null)
            {
                combat.SetTargetAndEnable_Server(gp.NetworkObject);
                Melon<HitmanModMain>.Logger.Msg("[THM] Target is now aggressive and attacking player.");
            }
            else
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] No CombatBehaviour on target NPC.");
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] MakeTargetAggressive failed: {ex.Message}");
        }
    }

    // ── Crime / Witness system ──────────────────────────────────────────────

    private const string CrimeStateLabel = "THM_Strangulation";

    private const float WitnessRadius = 15f;

    /// <summary>
    /// Two-pronged crime reporting:
    /// 1. Passive: Apply Brandishing visibility state so police VisionCones detect it.
    /// 2. Active: Directly notify nearby civilian NPCs via their Responses system,
    ///    bypassing VisionCone StatesOfInterest limitations.
    /// </summary>
    private static void ApplyCrimeVisibility()
    {
        _crimeReported = true;
        try
        {
            var gp = GamePlayer.Local;
            if (gp == null) return;

            // 1. Passive detection — police and NPCs with Brandishing in their VisionCone
            gp.Visibility.ApplyState(
                CrimeStateLabel,
                Il2CppScheduleOne.Vision.EVisualState.Brandishing,
                10f);

            // 2. Active witness check — simple distance + facing + raycast LOS
            var playerPos = gp.transform.position + Vector3.up; // eye height
            var allNpcs = UnityEngine.Object.FindObjectsOfType<GameNPC>();
            int inRange = 0;
            int witnessed = 0;

            if (allNpcs != null)
            {
                foreach (var npc in allNpcs)
                {
                    try
                    {
                        if (npc == null) continue;
                        if (npc == _pendingGameNpc) continue;
                        try { if (npc.Health.IsDead) continue; } catch { }

                        var npcPos = npc.transform.position + Vector3.up;
                        float dist = Vector3.Distance(npcPos, playerPos);
                        if (dist > WitnessRadius) continue;

                        string npcName = "?";
                        try { npcName = npc.fullName; } catch { try { npcName = npc.gameObject.name; } catch { } }
                        inRange++;

                        // Check 1: NPC must be roughly facing the player (within ~120° FOV)
                        var npcFwd = npc.transform.forward;
                        var dirToPlayer = (playerPos - npcPos).normalized;
                        float dot = Vector3.Dot(npcFwd, dirToPlayer);
                        if (dot < -0.1f) // facing away
                        {
                            Melon<HitmanModMain>.Logger.Msg($"[THM] {npcName} at {dist:F1}m — facing away (dot={dot:F2}), not a witness.");
                            continue;
                        }

                        // Check 2: Physics raycast — no wall between NPC and player
                        var ray = new Ray(npcPos, dirToPlayer);
                        bool blocked = Physics.Raycast(ray, out var hit, dist) &&
                                       hit.collider.gameObject.GetComponent<GameNPC>() == null &&
                                       hit.collider.gameObject.GetComponentInParent<GameNPC>() == null &&
                                       hit.distance < dist - 0.5f;
                        if (blocked)
                        {
                            Melon<HitmanModMain>.Logger.Msg($"[THM] {npcName} at {dist:F1}m — blocked by {hit.collider.gameObject.name}, not a witness.");
                            continue;
                        }

                        // This NPC is a witness — trigger native police call
                        try
                        {
                            npc.Actions.SetCallPoliceBehaviourCrime(new Il2CppScheduleOne.Law.DeadlyAssault());
                            npc.Actions.CallPolice_Networked(gp.NetworkObject);
                            Melon<HitmanModMain>.Logger.Msg($"[THM] Witness: {npcName} at {dist:F1}m (dot={dot:F2}) — calling police!");
                        }
                        catch (Exception ex)
                        {
                            Melon<HitmanModMain>.Logger.Warning($"[THM] CallPolice failed on {npcName}: {ex.Message}");
                        }

                        witnessed++;
                    }
                    catch { }
                }
            }

            Melon<HitmanModMain>.Logger.Msg($"[THM] Witness check: {inRange} NPCs in range, {witnessed} witnessed.");
            if (witnessed > 0)
            {
                // Murder witnessed — escalate beyond Investigating immediately
                try
                {
                    // Wanted state: nearby police react on sight without waiting for phone call
                    gp.Visibility.ApplyState(
                        "THM_Wanted",
                        Il2CppScheduleOne.Vision.EVisualState.Wanted,
                        120f);

                    // Escalate to Arresting via reflection (bypasses Investigating delay)
                    var cd = gp.CrimeData;
                    if (cd != null)
                    {
                        var method = cd.GetType().GetMethod("SetPursuitLevel");
                        if (method != null)
                        {
                            // 2 = Arresting (0=None, 1=Investigating, 2=Arresting, 3=NonLethal, 4=Lethal)
                            method.Invoke(cd, new object[] { 2, true });
                            Melon<HitmanModMain>.Logger.Msg("[THM] Pursuit escalated to Arresting — murder witnessed.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Melon<HitmanModMain>.Logger.Warning($"[THM] Pursuit escalation failed: {ex.Message}");
                }
            }
            else
            {
                Melon<HitmanModMain>.Logger.Msg("[THM] No witnesses — clean kill.");
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] ApplyCrimeVisibility failed: {ex.Message}");
        }
    }

    /// <summary>Remove the crime state early (e.g., if the mini-game ends quickly).</summary>
    private static void RemoveCrimeVisibility()
    {
        try
        {
            var gp = GamePlayer.Local;
            gp?.Visibility.RemoveState(CrimeStateLabel, 0f);
        }
        catch { }
    }

    // ── NPC freeze/unfreeze ──────────────────────────────────────────────────

    private static void FreezeGameNpc(GameNPC gameNpc)
    {
        try
        {
            if (gameNpc?.Movement == null) return;
            _savedMoveSpeed = gameNpc.Movement.MoveSpeedMultiplier;
            _savedMoveScale = gameNpc.Movement.MovementSpeedScale;
            _frozenGameNpc  = gameNpc;
            gameNpc.Movement.MoveSpeedMultiplier = 0f;
            gameNpc.Movement.MovementSpeedScale  = 0f;
            gameNpc.Movement.Stop();
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] FreezeNpc failed: {ex.Message}");
        }
    }

    private static void UnfreezeNpc()
    {
        try
        {
            if (_frozenGameNpc?.Movement != null)
            {
                _frozenGameNpc.Movement.MoveSpeedMultiplier = _savedMoveSpeed;
                _frozenGameNpc.Movement.MovementSpeedScale  = _savedMoveScale;
            }
        }
        catch { }
        _frozenGameNpc = null;
    }

    // ── HUD ───────────────────────────────────────────────────────────────────

    private static void SetHudVisible(bool visible)
    {
        if (visible == _hudVisible) return;
        _hudVisible = visible;
        if (visible)
        {
            EnsureHud();
            if (_hudRoot != null) _hudRoot.SetActive(true);
        }
        else
        {
            if (_hudRoot != null) _hudRoot.SetActive(false);
        }
    }

    private static void EnsureHud()
    {
        if (_hudRoot != null) return;
        _hudRoot = new GameObject("THM_StrangleHUD");
        UnityEngine.Object.DontDestroyOnLoad(_hudRoot);
        var canvas = _hudRoot.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        _hudRoot.AddComponent<UICanvasScaler>();
        _hudRoot.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        _hudText = UIFactory.Text("hud", "[ STRANGULATE — Press E ]",
            _hudRoot.transform, 16, TextAnchor.MiddleCenter, FontStyle.Bold);
        _hudText.color = new Color(0.92f, 0.25f, 0.25f);
        var rect = _hudText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.08f);
        rect.anchorMax = new Vector2(1f, 0.14f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
