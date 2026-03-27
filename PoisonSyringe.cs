using System;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using S1API.Items;

namespace HitmanMod;

/// <summary>
/// Registers the "Poison Syringe" equippable item and provides a helper
/// to deliver one to the local player's inventory.
/// </summary>
public static class PoisonSyringe
{
    public const string ItemId   = "thm_poison_syringe";
    public const string ItemName = "Poison Syringe";
    public const float  Price    = 750f;

    public static void Register()
    {
        try
        {
            var equippable = ItemCreator.CreateEquippableBuilder()
                .CreateBasicEquippable()
                .WithInteraction(canInteract: true, canPickup: true)
                .Build();

            var icon = IconLoader.Load("HitmanMod.Resources.poison_syringe_icon.png");

            ItemCreator.CreateItem(
                id:                ItemId,
                name:              ItemName,
                description:       "A concealed syringe loaded with a slow-acting toxin. One prick is all it takes.",
                category:          ItemCategory.Equipment,
                stackLimit:        1,
                basePurchasePrice: Price,
                resellMultiplier:  0f,
                legalStatus:       LegalStatus.Illegal,
                icon:              icon,
                equippable:        equippable
            );

            Melon<HitmanModMain>.Logger.Msg("[THM] Poison Syringe registered.");
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] PoisonSyringe.Register failed: {ex.Message}");
        }
    }

    public static void GiveToPlayer()
    {
        try
        {
            var definition = Registry.GetItem(ItemId);
            if (definition == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] PoisonSyringe: definition not found in registry.");
                return;
            }

            var instance = definition.GetDefaultInstance(1);
            if (instance == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] PoisonSyringe: could not create item instance.");
                return;
            }

            var inv = PlayerSingleton<PlayerInventory>.Instance;
            if (inv == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] PoisonSyringe: PlayerInventory not available.");
                return;
            }

            inv.AddItemToInventory(instance);
            Melon<HitmanModMain>.Logger.Msg("[THM] Poison Syringe given to player.");
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] PoisonSyringe.GiveToPlayer failed: {ex.Message}");
        }
    }
}
