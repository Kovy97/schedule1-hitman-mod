using System;
using Il2CppScheduleOne.Dialogue;
using MelonLoader;
using S1API.Entities;
using UnityEngine;
using GamePlayer = Il2CppScheduleOne.PlayerScripts.Player;

namespace HitmanMod;

/// <summary>
/// Handles the "Distract" mechanic: makes the contract target follow the player
/// for up to 15 seconds, then shows worldspace text and returns them to where they started.
/// </summary>
public static class DistractFollow
{
    public const string ChoiceLabel = "THM_DISTRACT_FOLLOW";
    public const string ChoiceText = "Help! Please follow me quick! (Distract)";

    private const float FollowTimeout       = 15f;
    private const float UpdateInterval      = 0.25f;  // check interval
    private const float MoveThresholdSq     = 2.25f;  // 1.5m² — only re-route when player moved this far

    private static NPC? _target;
    private static float _timeout;
    private static float _updateTimer;
    private static Vector3 _originPosition;
    private static Vector3 _lastDestination;
    private static bool _hasOrigin;

    public static bool IsActive => _target != null;

    public static void StartFollowing(NPC npc)
    {
        _target = npc;
        _timeout = FollowTimeout;
        _updateTimer = 0f;
        _lastDestination = Vector3.zero;

        _hasOrigin = false;
        try
        {
            // Only save origin if NPC is outdoors with a valid world position.
            // Interior scenes sit at world-origin — positions near (0,0,0) are unreliable.
            if (!npc.IsInBuilding)
            {
                var pos = npc.gameObject.transform.position;
                if (pos.sqrMagnitude > 1f) // sanity check: not at/near world origin
                {
                    _originPosition = pos;
                    _hasOrigin = true;
                }
            }
        }
        catch { }

        Melon<HitmanModMain>.Logger.Msg("[THM] Distract: target is now following the player.");
    }

    public static void StopFollowing()
    {
        if (_target != null)
        {
            try { _target.Movement.Stop(); } catch { }
            _target = null;
        }
        _timeout = 0f;
        _updateTimer = 0f;
        _hasOrigin = false;
        _lastDestination = Vector3.zero;
    }

    public static void Update(float dt)
    {
        if (_target == null) return;

        // Auto-cancel on death or KO
        try
        {
            if (_target.IsDead || _target.IsKnockedOut)
            {
                _target = null;
                return;
            }
        }
        catch
        {
            _target = null;
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

            // Only re-route when the player has moved far enough from the last destination.
            // This prevents continuous pathfinding resets that cause jittery movement.
            if (Vector3.SqrMagnitude(playerPos - _lastDestination) >= MoveThresholdSq)
            {
                _target.Movement.SetDestination(playerPos);
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
        if (_target == null) return;

        var npc = _target;
        var origin = _originPosition;
        var hasOrigin = _hasOrigin;

        _target = null;
        _timeout = 0f;
        _updateTimer = 0f;
        _hasOrigin = false;

        // Show worldspace text above NPC's head
        try
        {
            var handler = npc.gameObject.GetComponent<DialogueHandler>();
            if (handler == null)
                handler = npc.gameObject.GetComponentInChildren<DialogueHandler>();
            handler?.ShowWorldspaceDialogue("Ok, I'm leavin'..", 4f);
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Distract worldspace text failed: {ex.Message}");
        }

        // Return NPC to original position
        try
        {
            if (hasOrigin)
                npc.Movement.SetDestination(origin);
            else
                npc.Movement.Stop();
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Distract return-to-origin failed: {ex.Message}");
        }

        Melon<HitmanModMain>.Logger.Msg("[THM] Distract timeout — NPC returning to start position.");
    }
}
