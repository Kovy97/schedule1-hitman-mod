using System;
using Il2CppScheduleOne.Dialogue;
using MelonLoader;
using S1API.Entities;
using UnityEngine;
using GamePlayer = Il2CppScheduleOne.PlayerScripts.Player;
using GameNPC = Il2CppScheduleOne.NPCs.NPC;

namespace HitmanMod;

/// <summary>
/// Handles the "Distract" mechanic: makes the contract target follow the player
/// for up to 15 seconds, then shows worldspace text and returns them to where they started.
/// Works with both S1API NPC and Il2Cpp GameNPC.
/// </summary>
public static class DistractFollow
{
    public const string ChoiceLabel = "THM_DISTRACT_FOLLOW";
    public const string ChoiceText = "Help! Please follow me quick! (Distract)";

    private const float FollowTimeout       = 15f;
    private const float UpdateInterval      = 0.25f;
    private const float MoveThresholdSq     = 2.25f;

    private static NPC? _target;
    private static GameNPC? _targetGame;
    private static float _timeout;
    private static float _updateTimer;
    private static Vector3 _originPosition;
    private static Vector3 _lastDestination;
    private static bool _hasOrigin;

    public static bool IsActive => _target != null || _targetGame != null;

    public static void StartFollowing(NPC npc)
    {
        _target = npc;
        _targetGame = npc.gameObject?.GetComponent<GameNPC>();
        StartFollowingInternal(npc.gameObject.transform.position, npc.IsInBuilding);
    }

    public static void StartFollowingGameNpc(GameNPC gameNpc)
    {
        _target = null;
        _targetGame = gameNpc;
        bool inBuilding = false;
        try { inBuilding = gameNpc.transform.position.sqrMagnitude < 1f; } catch { }
        StartFollowingInternal(gameNpc.transform.position, inBuilding);
    }

    private static void StartFollowingInternal(Vector3 npcPos, bool inBuilding)
    {
        _timeout = FollowTimeout;
        _updateTimer = 0f;
        _lastDestination = Vector3.zero;
        _hasOrigin = false;

        try
        {
            if (!inBuilding && npcPos.sqrMagnitude > 1f)
            {
                _originPosition = npcPos;
                _hasOrigin = true;
            }
        }
        catch { }

        // Disable the NPC's AI behaviour so it doesn't override our movement
        try
        {
            if (_targetGame?.Behaviour != null)
                _targetGame.Behaviour.enabled = false;
        }
        catch { }

        Melon<HitmanModMain>.Logger.Msg("[THM] Distract: target is now following the player.");
    }

    public static void StopFollowing()
    {
        if (_target != null)
            try { _target.Movement.Stop(); } catch { }
        else if (_targetGame?.Movement != null)
            try { _targetGame.Movement.Stop(); } catch { }

        // Re-enable NPC AI behaviour
        try
        {
            if (_targetGame?.Behaviour != null)
                _targetGame.Behaviour.enabled = true;
        }
        catch { }

        _target = null;
        _targetGame = null;
        _timeout = 0f;
        _updateTimer = 0f;
        _hasOrigin = false;
        _lastDestination = Vector3.zero;
    }

    public static void Update(float dt)
    {
        if (_target == null && _targetGame == null) return;

        // Auto-cancel on death or KO
        try
        {
            bool dead = false;
            if (_target != null) dead = _target.IsDead || _target.IsKnockedOut;
            else if (_targetGame != null) dead = _targetGame.Health.IsDead;
            if (dead)
            {
                _target = null;
                _targetGame = null;
                return;
            }
        }
        catch
        {
            _target = null;
            _targetGame = null;
            return;
        }

        _timeout -= dt;
        if (_timeout <= 0f)
        {
            OnFollowTimeout();
            return;
        }

        _updateTimer -= dt;
        if (_updateTimer > 0f) return;
        _updateTimer = UpdateInterval;

        try
        {
            var player = GamePlayer.Local;
            if (player == null) return;

            var playerPos = player.transform.position;

            if (Vector3.SqrMagnitude(playerPos - _lastDestination) >= MoveThresholdSq)
            {
                if (_target != null)
                    _target.Movement.SetDestination(playerPos);
                else if (_targetGame?.Movement != null)
                    _targetGame.Movement.SetDestination(playerPos);
                _lastDestination = playerPos;
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Distract follow update failed: {ex.Message}");
            StopFollowing();
        }
    }

    private static void OnFollowTimeout()
    {
        if (_target == null && _targetGame == null) return;

        var gameNpc = _targetGame;
        var origin = _originPosition;
        var hasOrigin = _hasOrigin;

        // Re-enable NPC AI behaviour FIRST so death animations etc. work
        try
        {
            if (gameNpc?.Behaviour != null)
                gameNpc.Behaviour.enabled = true;
        }
        catch { }

        // Show worldspace text above NPC's head
        try
        {
            var go = _target?.gameObject ?? gameNpc?.gameObject;
            if (go != null)
            {
                var handler = go.GetComponent<DialogueHandler>() ?? go.GetComponentInChildren<DialogueHandler>();
                handler?.ShowWorldspaceDialogue("Ok, I'm leavin'..", 4f);
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Distract worldspace text failed: {ex.Message}");
        }

        // Return NPC to original position or re-enable schedule
        try
        {
            if (hasOrigin)
            {
                if (_target != null)
                    _target.Movement.SetDestination(origin);
                else if (gameNpc?.Movement != null)
                    gameNpc.Movement.SetDestination(origin);
            }
            else
            {
                if (_target != null) _target.Movement.Stop();
                else if (gameNpc?.Movement != null) gameNpc.Movement.Stop();
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Distract return-to-origin failed: {ex.Message}");
        }

        _target = null;
        _targetGame = null;
        _timeout = 0f;
        _updateTimer = 0f;
        _hasOrigin = false;

        Melon<HitmanModMain>.Logger.Msg("[THM] Distract timeout — NPC returning to start position.");
    }
}
