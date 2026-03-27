using System;
using System.Collections;
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
/// Handles the poison syringe mechanic.
/// Approach from behind, inject instantly (no mini-game), target dies after a delay.
/// Witnesses are checked at injection time only.
/// </summary>
public static class PoisonHandler
{
    private const float MaxRayDist       = 3.0f;
    private const float BehindDot        = 0.2f;
    private const float PoisonDelay      = 20f;
    private const float WitnessRadius    = 15f;
    private const string CrimeStateLabel = "THM_PoisonInjection";

    // Poison state
    private static bool     _poisonActive;
    private static bool     _poisonCancelled;
    private static NPC?     _poisonedNpc;
    private static GameNPC? _poisonedGameNpc;

    // HUD overlay
    private static GameObject? _hudRoot;
    private static UIText?      _hudText;
    private static bool         _hudVisible;

    /// <summary>Whether a poison coroutine is currently running.</summary>
    public static bool IsPoisonActive => _poisonActive;

    // ── Public API ────────────────────────────────────────────────────────────

    public static void Update(float dt)
    {
        // Don't process input while strangle mini-game is active
        if (StrangleMiniGame.IsActive) return;

        // If poison is active (waiting for delayed kill), just keep visual updated
        // Don't toggle visibility based on equip status — the item gets consumed after the drop
        if (_poisonActive)
        {
            SyringeVisual.Update();
            SetHudVisible(false);
            return;
        }

        bool syringeEquipped = false;
        bool syringeReady = false;
        try
        {
            syringeEquipped = IsSyringeEquipped();
            if (syringeEquipped)
            {
                var gp = GamePlayer.Local;
                if (gp != null && gp.Crouched)
                    syringeReady = true;
            }
        }
        catch { }

        SyringeVisual.SetVisible(syringeEquipped);
        if (syringeEquipped)
        {
            SyringeVisual.SetInjecting(false);
            SyringeVisual.Update();
        }

        SetHudVisible(syringeReady);
        if (!syringeReady) return;

        // E pressed → raycast for target
        try
        {
            if (Il2CppScheduleOne.GameInput.GetButtonDown(Il2CppScheduleOne.GameInput.ButtonCode.Interact))
            {
                var gameNpc = RaycastForNpc();
                if (gameNpc != null)
                {
                    var gp = GamePlayer.Local;
                    var playerPos = gp.transform.position;
                    var npcPos = gameNpc.transform.position;
                    var npcFwd = gameNpc.transform.forward;
                    var npcToPlayer = (playerPos - npcPos).normalized;
                    float dot = Vector3.Dot(npcFwd, npcToPlayer);

                    if (dot <= BehindDot)
                    {
                        BeginInjection(gameNpc);
                    }
                }
            }
        }
        catch { }
    }

    public static void Reset()
    {
        _poisonCancelled = true;
        SyringeVisual.Destroy();
        CleanupPoisonState();
        SetHudVisible(false);
    }

    /// <summary>Cancel an active poison effect (e.g. contract aborted).</summary>
    public static void CancelPoison()
    {
        if (_poisonActive)
        {
            _poisonCancelled = true;
            Melon<HitmanModMain>.Logger.Msg("[THM] Poison: effect cancelled.");
        }
    }

    public static bool IsSyringeEquipped()
    {
        try
        {
            var gp = GamePlayer.Local;
            if (gp == null) return false;
            var item = gp.GetEquippedItem();
            return item != null && item.Definition?.ID == PoisonSyringe.ItemId;
        }
        catch { return false; }
    }

    // ── Injection sequence ────────────────────────────────────────────────────

    private static void BeginInjection(GameNPC gameNpc)
    {
        // Resolve S1API wrapper
        _poisonedNpc = ResolveS1ApiNpc(gameNpc);
        _poisonedGameNpc = gameNpc;
        _poisonActive = true;
        _poisonCancelled = false;

        string name = "unknown";
        try { name = gameNpc.fullName; } catch { try { name = gameNpc.gameObject.name; } catch { } }

        // Report to ContractManager at injection time
        var mgr = HitmanModMain.Instance?.ContractManager;
        if (mgr != null)
        {
            mgr.SetKillUsedPoison();
            mgr.SnapshotKillConditions();
        }

        // Witness check at injection time
        ApplyCrimeVisibility(gameNpc);

        // Check pursuit level for witness flag
        if (mgr != null)
        {
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
        }

        // Remove crime visibility quickly — injection is brief
        RemoveCrimeVisibility();

        // Consume the syringe (one-time use) and hide visual
        ConsumeSyringe();
        SyringeVisual.SetVisible(false);

        Melon<HitmanModMain>.Logger.Msg($"[THM] Poison: injected {name}, death in {PoisonDelay}s.");

        // Start delayed kill coroutine
        MelonCoroutines.Start(PoisonDelayCoroutine());
    }

    private static void ConsumeSyringe()
    {
        try
        {
            var gp = GamePlayer.Local;
            if (gp == null) return;
            var item = gp.GetEquippedItem();
            if (item != null && item.Definition?.ID == PoisonSyringe.ItemId)
            {
                item.ChangeQuantity(-1);
                Melon<HitmanModMain>.Logger.Msg("[THM] Poison Syringe consumed.");
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] ConsumeSyringe failed: {ex.Message}");
        }
    }

    // ── Delayed kill coroutine ────────────────────────────────────────────────

    private static IEnumerator PoisonDelayCoroutine()
    {
        // Wait briefly before poison delay starts
        yield return new WaitForSeconds(0.5f);

        // Wait for poison to take effect
        float elapsed = 0f;
        while (elapsed < PoisonDelay)
        {
            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;

            if (_poisonCancelled)
            {
                Melon<HitmanModMain>.Logger.Msg("[THM] Poison: coroutine cancelled.");
                CleanupPoisonState();
                yield break;
            }

            // Check if target already dead (killed by other means)
            bool alreadyDead = false;
            try { alreadyDead = _poisonedGameNpc == null || _poisonedGameNpc.Health.IsDead; }
            catch { alreadyDead = true; }

            if (alreadyDead)
            {
                Melon<HitmanModMain>.Logger.Msg("[THM] Poison: target already dead before poison took effect.");
                CleanupPoisonState();
                yield break;
            }
        }

        // Poison takes effect — kill target
        TryKillPoisonedTarget();
        CleanupPoisonState();
    }

    private static void TryKillPoisonedTarget()
    {
        // Re-enable NPC behaviour so death animation plays
        try
        {
            if (_poisonedGameNpc?.Behaviour != null)
                _poisonedGameNpc.Behaviour.enabled = true;
        }
        catch { }

        // Stop distract follow if active
        DistractFollow.StopFollowing();

        // Try S1API Kill first
        if (_poisonedNpc != null)
        {
            try
            {
                _poisonedNpc.Kill();
                Melon<HitmanModMain>.Logger.Msg($"[THM] Poison: {_poisonedNpc.FullName} eliminated (S1API).");
                return;
            }
            catch (Exception ex)
            {
                Melon<HitmanModMain>.Logger.Warning($"[THM] Poison S1API Kill failed: {ex.Message}");
            }
        }

        // Fallback: direct health damage
        if (_poisonedGameNpc != null)
        {
            try
            {
                var health = _poisonedGameNpc.Health;
                if (health != null)
                {
                    health.TakeDamage(health.MaxHealth * 10f, true);
                    string name = "?";
                    try { name = _poisonedGameNpc.fullName; } catch { }
                    Melon<HitmanModMain>.Logger.Msg($"[THM] Poison: {name} eliminated (direct health).");
                }
            }
            catch (Exception ex)
            {
                Melon<HitmanModMain>.Logger.Warning($"[THM] Poison direct kill failed: {ex.Message}");
            }
        }
    }

    private static void CleanupPoisonState()
    {
        _poisonActive = false;
        _poisonCancelled = false;
        _poisonedNpc = null;
        _poisonedGameNpc = null;
    }

    // ── Target detection via camera raycast ──────────────────────────────────

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

            try { if (npc.Health.IsDead) return null; } catch { }

            return npc;
        }
        catch { return null; }
    }

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

    // ── Crime / Witness system ──────────────────────────────────────────────

    private static void ApplyCrimeVisibility(GameNPC targetNpc)
    {
        try
        {
            var gp = GamePlayer.Local;
            if (gp == null) return;

            // Passive detection — very brief, injection is instant
            gp.Visibility.ApplyState(
                CrimeStateLabel,
                Il2CppScheduleOne.Vision.EVisualState.Brandishing,
                0.5f);

            // Active witness check
            var playerPos = gp.transform.position + Vector3.up;
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
                        if (npc == targetNpc) continue;
                        try { if (npc.Health.IsDead) continue; } catch { }

                        var npcPos = npc.transform.position + Vector3.up;
                        float dist = Vector3.Distance(npcPos, playerPos);
                        if (dist > WitnessRadius) continue;

                        string npcName = "?";
                        try { npcName = npc.fullName; } catch { try { npcName = npc.gameObject.name; } catch { } }
                        inRange++;

                        // NPC must be roughly facing the player
                        var npcFwd = npc.transform.forward;
                        var dirToPlayer = (playerPos - npcPos).normalized;
                        float dot = Vector3.Dot(npcFwd, dirToPlayer);
                        if (dot < -0.1f) continue;

                        // Line-of-sight check
                        var ray = new Ray(npcPos, dirToPlayer);
                        bool blocked = Physics.Raycast(ray, out var hit, dist) &&
                                       hit.collider.gameObject.GetComponent<GameNPC>() == null &&
                                       hit.collider.gameObject.GetComponentInParent<GameNPC>() == null &&
                                       hit.distance < dist - 0.5f;
                        if (blocked) continue;

                        // Witness — trigger police call
                        try
                        {
                            npc.Actions.SetCallPoliceBehaviourCrime(new Il2CppScheduleOne.Law.DeadlyAssault());
                            npc.Actions.CallPolice_Networked(gp.NetworkObject);
                            Melon<HitmanModMain>.Logger.Msg($"[THM] Poison witness: {npcName} at {dist:F1}m — calling police!");
                        }
                        catch (Exception ex)
                        {
                            Melon<HitmanModMain>.Logger.Warning($"[THM] Poison CallPolice failed on {npcName}: {ex.Message}");
                        }

                        witnessed++;
                    }
                    catch { }
                }
            }

            Melon<HitmanModMain>.Logger.Msg($"[THM] Poison witness check: {inRange} NPCs in range, {witnessed} witnessed.");
            if (witnessed > 0)
            {
                try
                {
                    gp.Visibility.ApplyState(
                        "THM_Wanted",
                        Il2CppScheduleOne.Vision.EVisualState.Wanted,
                        120f);

                    var cd = gp.CrimeData;
                    if (cd != null)
                    {
                        var method = cd.GetType().GetMethod("SetPursuitLevel");
                        if (method != null)
                        {
                            method.Invoke(cd, new object[] { 2, true });
                            Melon<HitmanModMain>.Logger.Msg("[THM] Pursuit escalated — poison injection witnessed.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Melon<HitmanModMain>.Logger.Warning($"[THM] Poison pursuit escalation failed: {ex.Message}");
                }
            }
            else
            {
                Melon<HitmanModMain>.Logger.Msg("[THM] Poison: no witnesses — clean injection.");
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Poison ApplyCrimeVisibility failed: {ex.Message}");
        }
    }

    private static void RemoveCrimeVisibility()
    {
        try
        {
            var gp = GamePlayer.Local;
            gp?.Visibility.RemoveState(CrimeStateLabel, 0f);
        }
        catch { }
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
        _hudRoot = new GameObject("THM_PoisonHUD");
        UnityEngine.Object.DontDestroyOnLoad(_hudRoot);
        var canvas = _hudRoot.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        _hudRoot.AddComponent<UICanvasScaler>();
        _hudRoot.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        _hudText = UIFactory.Text("hud", "[ INJECT — Press E ]",
            _hudRoot.transform, 16, TextAnchor.MiddleCenter, FontStyle.Bold);
        _hudText.color = new Color(0.2f, 0.85f, 0.5f); // green tint to distinguish from garrote
        var rect = _hudText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.08f);
        rect.anchorMax = new Vector2(1f, 0.14f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
