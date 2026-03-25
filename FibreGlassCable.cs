using System;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using S1API.Items;

namespace HitmanMod;

/// <summary>
/// Registers the "Fibre Glass Cable" equippable item and provides a helper
/// to deliver one to the local player's inventory.
/// </summary>
public static class FibreGlassCable
{
    public const string ItemId   = "thm_fibre_glass_cable";
    public const string ItemName = "Fibre Glass Cable";
    public const float  Price    = 500f;

    public static void Register()
    {
        try
        {
            var equippable = ItemCreator.CreateEquippableBuilder()
                .CreateBasicEquippable()
                .WithInteraction(canInteract: true, canPickup: true)
                .Build();

            ItemCreator.CreateItem(
                id:                ItemId,
                name:              ItemName,
                description:       "A thin steel wire with wooden handles. Silent and lethal.",
                category:          ItemCategory.Equipment,
                stackLimit:        1,
                basePurchasePrice: Price,
                resellMultiplier:  0f,
                legalStatus:       LegalStatus.Illegal,
                icon:              null,
                equippable:        equippable
            );

            Melon<HitmanModMain>.Logger.Msg("[THM] Fibre Glass Cable registered.");
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] FibreGlassCable.Register failed: {ex.Message}");
        }
    }

    public static void GiveToPlayer()
    {
        try
        {
            // Look up the definition via the game's registry (works for S1API custom items)
            var definition = Registry.GetItem(ItemId);
            if (definition == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] FibreGlassCable: definition not found in registry.");
                return;
            }

            var instance = definition.GetDefaultInstance(1);
            if (instance == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] FibreGlassCable: could not create item instance.");
                return;
            }

            var inv = PlayerSingleton<PlayerInventory>.Instance;
            if (inv == null)
            {
                Melon<HitmanModMain>.Logger.Warning("[THM] FibreGlassCable: PlayerInventory not available.");
                return;
            }

            inv.AddItemToInventory(instance);
            Melon<HitmanModMain>.Logger.Msg("[THM] Fibre Glass Cable given to player.");
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] FibreGlassCable.GiveToPlayer failed: {ex.Message}");
        }
    }
}
