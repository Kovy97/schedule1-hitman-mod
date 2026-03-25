using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;

namespace HitmanMod;

/// <summary>
/// Simple config reader for THM_config.cfg.
/// Reads key = value pairs, ignores lines starting with # or empty lines.
/// The file is never overwritten — users have full control.
/// </summary>
public static class HitmanConfig
{
    private const string ConfigPath = "UserData/THM_config.cfg";

    // ── Values (set from config or defaults) ────────────────
    public static float RewardVeryEasy_Min = 100f;
    public static float RewardVeryEasy_Max = 300f;
    public static float RewardEasy_Min = 250f;
    public static float RewardEasy_Max = 600f;
    public static float RewardMedium_Min = 500f;
    public static float RewardMedium_Max = 1200f;
    public static float RewardHard_Min = 1000f;
    public static float RewardHard_Max = 2500f;
    public static float RewardVeryHard_Min = 2000f;
    public static float RewardVeryHard_Max = 5000f;

    public static float KnockoutPayMultiplier = 0.65f;

    public static float OfferInterval_Min = 90f;
    public static float OfferInterval_Max = 300f;
    public static float OfferTimeout = 300f;

    public static float CooldownAfterComplete = 480f;
    public static float CooldownAfterDecline = 240f;
    public static float CooldownAfterFail = 720f;
    public static float CooldownAfterAbort = 720f;

    public static float HackCost_VeryEasy = 50f;
    public static float HackCost_Easy = 100f;
    public static float HackCost_Medium = 200f;
    public static float HackCost_Hard = 350f;
    public static float HackCost_VeryHard = 500f;

    public static int XP_VeryEasy = 25;
    public static int XP_Easy = 50;
    public static int XP_Medium = 100;
    public static int XP_Hard = 200;
    public static int XP_VeryHard = 400;

    public static bool AppUnlocked = false;

    public static void Initialize()
    {
        if (!File.Exists(ConfigPath))
        {
            WriteDefaultConfig();
            MelonLogger.Msg("[THM] Default config created at UserData/THM_config.cfg");
        }

        LoadConfig();
        MelonLogger.Msg("[THM] Config loaded.");
    }

    private static void LoadConfig()
    {
        try
        {
            var lines = File.ReadAllLines(ConfigPath);
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("["))
                    continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                ApplyValue(key, val);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[THM] Config read failed: {ex.Message}");
        }
    }

    private static void ApplyValue(string key, string val)
    {
        switch (key)
        {
            case "RewardVeryEasy_Min": TryFloat(val, ref RewardVeryEasy_Min); break;
            case "RewardVeryEasy_Max": TryFloat(val, ref RewardVeryEasy_Max); break;
            case "RewardEasy_Min": TryFloat(val, ref RewardEasy_Min); break;
            case "RewardEasy_Max": TryFloat(val, ref RewardEasy_Max); break;
            case "RewardMedium_Min": TryFloat(val, ref RewardMedium_Min); break;
            case "RewardMedium_Max": TryFloat(val, ref RewardMedium_Max); break;
            case "RewardHard_Min": TryFloat(val, ref RewardHard_Min); break;
            case "RewardHard_Max": TryFloat(val, ref RewardHard_Max); break;
            case "RewardVeryHard_Min": TryFloat(val, ref RewardVeryHard_Min); break;
            case "RewardVeryHard_Max": TryFloat(val, ref RewardVeryHard_Max); break;
            case "KnockoutPayMultiplier": TryFloat(val, ref KnockoutPayMultiplier); break;
            case "OfferInterval_Min": TryFloat(val, ref OfferInterval_Min); break;
            case "OfferInterval_Max": TryFloat(val, ref OfferInterval_Max); break;
            case "OfferTimeout": TryFloat(val, ref OfferTimeout); break;
            case "CooldownAfterComplete": TryFloat(val, ref CooldownAfterComplete); break;
            case "CooldownAfterDecline": TryFloat(val, ref CooldownAfterDecline); break;
            case "CooldownAfterFail": TryFloat(val, ref CooldownAfterFail); break;
            case "CooldownAfterAbort": TryFloat(val, ref CooldownAfterAbort); break;
            case "HackCost_VeryEasy": TryFloat(val, ref HackCost_VeryEasy); break;
            case "HackCost_Easy": TryFloat(val, ref HackCost_Easy); break;
            case "HackCost_Medium": TryFloat(val, ref HackCost_Medium); break;
            case "HackCost_Hard": TryFloat(val, ref HackCost_Hard); break;
            case "HackCost_VeryHard": TryFloat(val, ref HackCost_VeryHard); break;
            case "XP_VeryEasy": TryInt(val, ref XP_VeryEasy); break;
            case "XP_Easy": TryInt(val, ref XP_Easy); break;
            case "XP_Medium": TryInt(val, ref XP_Medium); break;
            case "XP_Hard": TryInt(val, ref XP_Hard); break;
            case "XP_VeryHard": TryInt(val, ref XP_VeryHard); break;
            case "AppUnlocked": TryBool(val, ref AppUnlocked); break;
        }
    }

    private static void TryFloat(string val, ref float field)
    {
        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            field = result;
    }

    private static void TryInt(string val, ref int field)
    {
        if (int.TryParse(val, out int result))
            field = result;
    }

    private static void TryBool(string val, ref bool field)
    {
        field = val.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    // ── Helper Methods ──────────────────────────────────────

    public static (float min, float max) GetRewardRange(ContractDifficulty difficulty)
    {
        return difficulty switch
        {
            ContractDifficulty.VeryEasy => (RewardVeryEasy_Min, RewardVeryEasy_Max),
            ContractDifficulty.Easy     => (RewardEasy_Min, RewardEasy_Max),
            ContractDifficulty.Medium   => (RewardMedium_Min, RewardMedium_Max),
            ContractDifficulty.Hard     => (RewardHard_Min, RewardHard_Max),
            ContractDifficulty.VeryHard => (RewardVeryHard_Min, RewardVeryHard_Max),
            _ => (100f, 300f)
        };
    }

    public static float GetHackCost(ContractDifficulty difficulty)
    {
        return difficulty switch
        {
            ContractDifficulty.VeryEasy => HackCost_VeryEasy,
            ContractDifficulty.Easy     => HackCost_Easy,
            ContractDifficulty.Medium   => HackCost_Medium,
            ContractDifficulty.Hard     => HackCost_Hard,
            ContractDifficulty.VeryHard => HackCost_VeryHard,
            _ => 200f
        };
    }

    public static int GetXP(ContractDifficulty difficulty)
    {
        return difficulty switch
        {
            ContractDifficulty.VeryEasy => XP_VeryEasy,
            ContractDifficulty.Easy     => XP_Easy,
            ContractDifficulty.Medium   => XP_Medium,
            ContractDifficulty.Hard     => XP_Hard,
            ContractDifficulty.VeryHard => XP_VeryHard,
            _ => 25
        };
    }

    public static void ResetToDefaults()
    {
        WriteDefaultConfig();
        LoadConfig();
        MelonLogger.Msg("[THM] Config reset to defaults.");
    }

    private static void WriteDefaultConfig()
    {
        try
        {
            Directory.CreateDirectory("UserData");
            File.WriteAllText(ConfigPath, @"# ═══════════════════════════════════════════════════════════════
# T.H.M — The Hitman Mod Configuration
# by Kowyx
#
# Edit these values to customize your experience.
# Lines starting with # are comments and will be ignored.
# Changes take effect on next game start.
# ═══════════════════════════════════════════════════════════════


# ── KILL REWARDS ──────────────────────────────────────────────
# Base dollar rewards for kill contracts.
# Knockout rewards = Kill Reward × KnockoutPayMultiplier

RewardVeryEasy_Min = 100
RewardVeryEasy_Max = 300

RewardEasy_Min = 250
RewardEasy_Max = 600

RewardMedium_Min = 500
RewardMedium_Max = 1200

RewardHard_Min = 1000
RewardHard_Max = 2500

RewardVeryHard_Min = 2000
RewardVeryHard_Max = 5000


# ── KNOCKOUT MULTIPLIER ──────────────────────────────────────
# What fraction of the kill reward a knockout contract pays.
# 0.65 = knockout pays 65% of the kill reward.

KnockoutPayMultiplier = 0.65


# ── CONTRACT FREQUENCY ───────────────────────────────────────
# Time between automatic contract offers (in real seconds).
# A random value between min and max is picked each time.

OfferInterval_Min = 90
OfferInterval_Max = 300

# How long a pending offer stays before it expires.

OfferTimeout = 300


# ── COOLDOWNS ─────────────────────────────────────────────────
# Wait time after various outcomes (in real seconds).
# 60 seconds is roughly 1 in-game hour.

CooldownAfterComplete = 480
CooldownAfterDecline = 240
CooldownAfterFail = 720
CooldownAfterAbort = 720


# ── SIGNAL TRACE COSTS ───────────────────────────────────────
# Bank account cost to enable live distance tracking.
# Paid once per contract. Scales with difficulty.

HackCost_VeryEasy = 50
HackCost_Easy = 100
HackCost_Medium = 200
HackCost_Hard = 350
HackCost_VeryHard = 500


# ── REPUTATION (XP) ──────────────────────────────────────────
# Points earned per completed contract.
# Higher reputation unlocks harder contracts.

XP_VeryEasy = 25
XP_Easy = 50
XP_Medium = 100
XP_Hard = 200
XP_VeryHard = 400


# ── APP UNLOCK ────────────────────────────────────────────────
# Set to true to skip finding the Stranger NPC.
# The app will be unlocked from the start on all saves.

AppUnlocked = false
");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"[THM] Failed to write config: {ex.Message}");
        }
    }
}
