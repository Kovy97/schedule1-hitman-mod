using System;
using Il2CppScheduleOne.Dialogue;
using MelonLoader;
using S1API.Entities;
using UnityEngine.Events;
using DialogueChoice = Il2CppScheduleOne.Dialogue.DialogueController.DialogueChoice;

namespace HitmanMod;

/// <summary>
/// Handles injection of the "Distract" dialogue choice into the active contract target's
/// DialogueController.Choices list. Uses the game's native choice system so the option
/// appears naturally in the NPC's conversation menu when the player talks to them.
/// </summary>
public static class DistractChoiceInjector
{
    private static DialogueController? _cachedController;
    private static DialogueChoice? _injectedChoice;

    public static void InjectChoice(NPC targetNpc)
    {
        RemoveChoice();

        try
        {
            var go = targetNpc.gameObject;

            // Try root first, then children (some NPCs have controller on child object)
            var controller = go.GetComponent<DialogueController>();
            if (controller == null)
                controller = go.GetComponentInChildren<DialogueController>();
            if (controller == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] No DialogueController on target — Distract choice not available.");
                return;
            }

            var choice = new DialogueChoice();
            choice.ChoiceText = DistractFollow.ChoiceText;
            choice.Enabled = true;
            choice.onChoosen.AddListener((UnityAction)OnDistractChosen);

            controller.Choices.Add(choice);
            _cachedController = controller;
            _injectedChoice = choice;

            Melon<HitmanModMain>.Logger.Msg($"[THM] Distract choice injected into {targetNpc.FullName}'s dialogue.");
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] InjectChoice failed: {ex.Message}");
        }
    }

    public static void RemoveChoice()
    {
        try
        {
            if (_cachedController != null && _injectedChoice != null)
                _cachedController.Choices.Remove(_injectedChoice);
        }
        catch { }
        _cachedController = null;
        _injectedChoice = null;
    }

    private static void OnDistractChosen()
    {
        try
        {
            var mgr = HitmanModMain.Instance?.ContractManager;
            if (mgr?.TargetNpc != null)
                DistractFollow.StartFollowing(mgr.TargetNpc);
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] OnDistractChosen failed: {ex.Message}");
        }
    }
}
