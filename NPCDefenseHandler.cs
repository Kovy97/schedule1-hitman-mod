using System;
using System.Collections.Generic;
using S1API.Entities;
using S1API.Entities.Equippables;

namespace HitmanMod;

public static class NPCDefenseHandler
{
    private static readonly Dictionary<string, (float maxHealth, float aggressiveness)> OriginalStats = new();
    private static readonly Dictionary<string, string> PendingWeapons = new();
    private static readonly Dictionary<string, float> InitialHealth = new();
    private static readonly HashSet<string> ArmedTargets = new();

    // Cached FullName of the active target — avoids repeated string allocation in CheckAndArm
    private static string? _activeTargetKey;

    public static void PrepareTarget(NPC target, ContractDifficulty difficulty)
    {
        string key = target.FullName;
        _activeTargetKey = key;

        // Prevent re-entry: if stats already stored for this NPC, skip to avoid compounding multipliers
        if (OriginalStats.ContainsKey(key)) return;
        OriginalStats[key] = (target.MaxHealth, target.Aggressiveness);

        ArmedTargets.Remove(key);
        string weapon = "";

        switch (difficulty)
        {
            case ContractDifficulty.VeryEasy:
                target.Aggressiveness = 0.1f;
                break;

            case ContractDifficulty.Easy:
                target.Aggressiveness = 0.4f;
                weapon = Weapon.BrokenBottle;
                break;

            case ContractDifficulty.Medium:
                target.MaxHealth *= 1.5f;
                target.Heal((int)Math.Ceiling(target.MaxHealth - target.CurrentHealth));
                target.Aggressiveness = 0.6f;
                weapon = Weapon.Knife;
                {
                    var combat = target.CombatBehaviour;
                    combat.GiveUpRange = 40f;
                    combat.GiveUpTime = 30f;
                }
                break;

            case ContractDifficulty.Hard:
                target.MaxHealth *= 2f;
                target.Heal((int)Math.Ceiling(target.MaxHealth - target.CurrentHealth));
                target.Aggressiveness = 0.85f;
                weapon = Weapon.M1911;
                {
                    var combat = target.CombatBehaviour;
                    combat.GiveUpRange = 60f;
                    combat.GiveUpTime = 45f;
                }
                break;

            case ContractDifficulty.VeryHard:
                target.MaxHealth *= 3f;
                target.Heal((int)Math.Ceiling(target.MaxHealth - target.CurrentHealth));
                target.Aggressiveness = 1.0f;
                weapon = Weapon.PumpShotgun;
                {
                    var combat = target.CombatBehaviour;
                    combat.GiveUpRange = 80f;
                    combat.GiveUpTime = 60f;
                }
                break;
        }

        if (!string.IsNullOrEmpty(weapon))
            PendingWeapons[key] = weapon;

        InitialHealth[key] = target.CurrentHealth;
    }

    /// <summary>
    /// Call every frame while contract is active.
    /// Arms the target only after they take damage.
    /// </summary>
    public static void CheckAndArm(NPC target)
    {
        string key = _activeTargetKey ?? target.FullName;
        if (ArmedTargets.Contains(key)) return;

        if (!PendingWeapons.TryGetValue(key, out string? weapon)) return;
        if (!InitialHealth.TryGetValue(key, out float initialHp)) return;

        if (target.CurrentHealth < initialHp)
        {
            try
            {
                target.CombatBehaviour.SetCurrentWeapon(weapon);
            }
            catch { /* Weapon assignment failed — target fights unarmed */ }
            PendingWeapons.Remove(key);
            InitialHealth.Remove(key);
            ArmedTargets.Add(key);
        }
    }

    public static void ClearAll()
    {
        OriginalStats.Clear();
        PendingWeapons.Clear();
        InitialHealth.Clear();
        ArmedTargets.Clear();
        _activeTargetKey = null;
    }

    public static void CleanupTarget(NPC target)
    {
        string key = target.FullName;
        _activeTargetKey = null;

        PendingWeapons.Remove(key);
        InitialHealth.Remove(key);
        ArmedTargets.Remove(key);

        if (OriginalStats.TryGetValue(key, out var saved))
        {
            target.MaxHealth = saved.maxHealth;
            target.Aggressiveness = saved.aggressiveness;
            OriginalStats.Remove(key);
        }

        if (!target.IsDead && !target.IsKnockedOut)
        {
            try
            {
                float healAmount = target.MaxHealth - target.CurrentHealth;
                if (healAmount > 0)
                    target.Heal((int)Math.Ceiling(healAmount));
            }
            catch { /* Heal failed — NPC in invalid state */ }

            try
            {
                target.StopPanicking();
            }
            catch { /* StopPanicking not supported or NPC invalid */ }

            try
            {
                target.CombatBehaviour.SetCurrentWeapon("");
            }
            catch { /* Weapon reset not supported */ }
        }
    }
}
