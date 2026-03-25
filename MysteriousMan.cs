using System;
using S1API.Entities;
using S1API.Entities.Dialogue;
using S1API.Entities.Schedule;
using S1API.Map;
using S1API.Money;
using MelonLoader;
using UnityEngine;

namespace HitmanMod;

/// <summary>
/// "Mysterious Man" — static NPC that offers the player the Hitman job.
/// Stands at a fixed location. When spoken to, presents a dialogue choice
/// to register as a Hitman, which unlocks the Hitman phone app.
/// </summary>
public sealed class MysteriousMan : NPC
{
    public override bool IsPhysical => true;

    private static readonly Vector3 SpawnPosition = new Vector3(52.0f, 1.0f, 79.8f);

    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        builder.WithIdentity("mysterious_man", "Stranger", "")
            .WithAppearanceDefaults(av =>
            {
                // Male, tall, slim
                av.Gender = 0f;
                av.Height = 1.05f;
                av.Weight = 0.35f;

                // Skin
                av.SkinColor = new Color(0.72f, 0.58f, 0.48f);
                av.LeftEyeLidColor = av.SkinColor;
                av.RightEyeLidColor = av.SkinColor;

                // Dark eyes
                av.EyeBallTint = new Color(0.9f, 0.9f, 0.9f);
                av.PupilDilation = 0.7f;
                av.EyebrowScale = 1.0f;
                av.EyebrowThickness = 0.7f;
                av.EyebrowRestingHeight = 0.4f;

                // Dark buzzcut (under beanie)
                av.HairColor = new Color(0.08f, 0.08f, 0.08f);
                av.HairPath = "Avatar/Hair/Buzzcut/Buzzcut";

                // Neutral face + stubble
                av.WithFaceLayer("Avatar/Layers/Face/Face_Neutral", Color.black);
                av.WithFaceLayer("Avatar/Layers/Face/FacialHair_Stubble", new Color(0.08f, 0.08f, 0.08f));

                // Black tucked shirt
                av.WithBodyLayer("Avatar/Layers/Top/Tucked T-Shirt", new Color(0.05f, 0.05f, 0.05f));

                // Black jeans
                av.WithBodyLayer("Avatar/Layers/Bottom/Jeans", new Color(0.06f, 0.06f, 0.06f));

                // Black collar jacket (suit-like)
                av.WithAccessoryLayer("Avatar/Accessories/Chest/CollarJacket/CollarJacket", new Color(0.06f, 0.06f, 0.06f));

                // Brown belt
                av.WithAccessoryLayer("Avatar/Accessories/Waist/Belt/Belt", new Color(0.35f, 0.22f, 0.12f));

                // Brown dress shoes
                av.WithAccessoryLayer("Avatar/Accessories/Feet/DressShoes/DressShoes", new Color(0.3f, 0.18f, 0.08f));

                // Black beanie
                av.WithAccessoryLayer("Avatar/Accessories/Head/Beanie/Beanie", new Color(0.06f, 0.06f, 0.06f));
            })
            .WithSpawnPosition(SpawnPosition)
            .WithRelationshipDefaults(r =>
            {
                r.WithDelta(0f).SetUnlocked(true).SetUnlockType((S1API.Entities.NPCRelationship.UnlockType)1)
                    .WithConnectionsById(Array.Empty<string>());
            })
            .WithSchedule(plan =>
            {
                // Stand at spawn position all day
                plan.Add(new WalkToSpec
                {
                    Destination = SpawnPosition,
                    StartTime = 0,
                    FaceDestinationDirection = true
                });
            });
    }

    protected override void OnCreated()
    {
        try
        {
            base.OnCreated();
            Appearance.Build();
            Aggressiveness = 0f;
            Region = (Region)2; // Suburbia
            Schedule.Enable();

            // Build dialogue with choices
            Dialogue.BuildAndRegisterContainer("mysterious_man_intro", container =>
            {
                container.AddNode("ENTRY",
                    "Hey man, I'm looking for someone capable to take care of... some business. " +
                    "Of course, it's a paid position. Are you interested?",
                    choices =>
                    {
                        choices.Add("accept",       "Yes, I am. (Register as a Hitman)", null);
                        choices.Add("decline",      "No, sorry, I'm the wrong person for this.", null);
                        choices.Add("buy_cable",    $"Buy Fibre Glass Cable (${(int)FibreGlassCable.Price})", null);
                    });
            });

            // Set this container as the active dialogue when interacting
            Dialogue.UseContainerOnInteract("mysterious_man_intro");

            // Hook dialogue choices
            Dialogue.OnChoiceSelected("accept", () =>
            {
                MelonLogger.Msg("[THM] Player accepted Hitman job from Mysterious Man!");
                ContractManager.UnlockHitman();

                try
                {
                    var gameNpc = gameObject.GetComponent<Il2CppScheduleOne.NPCs.NPC>();
                    gameNpc?.SendTextMessage(
                        "Good choice. From now on, I'll send you contracts through this phone. " +
                        "Check your apps — you'll find everything you need there.\n\n" +
                        "Welcome to the business."
                    );
                }
                catch { }
            });

            Dialogue.OnChoiceSelected("decline", () =>
            {
                MelonLogger.Msg("[THM] Player declined Hitman job.");
                try
                {
                    var gameNpc = gameObject.GetComponent<Il2CppScheduleOne.NPCs.NPC>();
                    gameNpc?.SendTextMessage("Your loss. I'll be around if you change your mind.");
                }
                catch { }
            });

            Dialogue.OnChoiceSelected("buy_cable", () =>
            {
                try
                {
                    float balance = Money.GetCashBalance();
                    if (balance < FibreGlassCable.Price)
                    {
                        MelonLogger.Msg("[THM] Player can't afford Fibre Glass Cable.");
                        var gameNpc = gameObject.GetComponent<Il2CppScheduleOne.NPCs.NPC>();
                        gameNpc?.SendTextMessage($"You're short on cash. That cable costs ${(int)FibreGlassCable.Price}. Come back when you have the money.");
                        return;
                    }

                    Money.ChangeCashBalance(-FibreGlassCable.Price, true, false);
                    FibreGlassCable.GiveToPlayer();

                    MelonLogger.Msg("[THM] Player purchased Fibre Glass Cable.");
                    try
                    {
                        var gameNpc = gameObject.GetComponent<Il2CppScheduleOne.NPCs.NPC>();
                        gameNpc?.SendTextMessage("There you go. Use it wisely — and quietly.");
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[THM] buy_cable handler failed: {ex.Message}");
                }
            });

            // Freeze in place after spawn
            MelonCoroutines.Start(FreezeAfterSpawn());
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[THM] MysteriousMan OnCreated failed: {ex.Message}");
        }
    }

    private System.Collections.IEnumerator FreezeAfterSpawn()
    {
        yield return new WaitForSeconds(3f);
        try
        {
            var gameNpc = gameObject.GetComponent<Il2CppScheduleOne.NPCs.NPC>();
            if (gameNpc?.Movement != null)
            {
                gameNpc.Movement.MoveSpeedMultiplier = 0f;
                gameNpc.Movement.MovementSpeedScale = 0f;
                gameNpc.Movement.Stop();
            }
        }
        catch { }
    }
}
