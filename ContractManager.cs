using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader;
using S1API.Entities;
using S1API.Money;
using UnityEngine;
using UnityEngine.Events;
using Il2CppInterop.Runtime;
using GamePlayer = Il2CppScheduleOne.PlayerScripts.Player;
using GameNPC = Il2CppScheduleOne.NPCs.NPC;
using GameDealer = Il2CppScheduleOne.Economy.Dealer;
using GameEmployee = Il2CppScheduleOne.Employees.Employee;
using GameSupplier = Il2CppScheduleOne.Economy.Supplier;
using GameCartelGoon = Il2CppScheduleOne.Cartel.CartelGoon;
using GamePoliceOfficer = Il2CppScheduleOne.Police.PoliceOfficer;
using GameMap = Il2CppScheduleOne.Map.Map;
using SaveManager = Il2CppScheduleOne.Persistence.SaveManager;
using LoadManager = Il2CppScheduleOne.Persistence.LoadManager;
using DeadDrop = Il2CppScheduleOne.Economy.DeadDrop;
using MoneyManager = Il2CppScheduleOne.Money.MoneyManager;
using CompassManager = Il2CppScheduleOne.UI.Compass.CompassManager;

namespace HitmanMod;

public class ContractManager
{
    private static readonly System.Random Rng = new();
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    // State
    public Contract? ActiveContract { get; private set; }
    public Contract? PendingOffer { get; private set; }
    public int CompletedCount { get; private set; }
    public float TotalEarnings { get; private set; }

    // Unlock system — Hitman must be activated by talking to Mysterious Man
    public static bool IsHitmanUnlocked { get; set; }

    public static void UnlockHitman()
    {
        IsHitmanUnlocked = true;
        Melon<HitmanModMain>.Logger.Msg("[THM] Hitman career UNLOCKED!");

        // Refresh the app UI so it shows unlocked state
        try { HitmanApp.Instance?.RefreshUI(); } catch { }
    }

    // Level system
    public int Level { get; private set; } = 1;
    public int Experience { get; private set; }
    public int ExperienceForNextLevel => Level < MaxLevel ? LevelThresholds[Level] : LevelThresholds[MaxLevel - 1];
    public string LastKnownLocation { get; private set; } = "";
    public float TargetDistance { get; private set; } = -1f;

    // Request feedback for UI
    public string? LastRequestError { get; private set; }

    // Cooldown
    public float CooldownRemaining { get; private set; }
    public bool IsOnCooldown => CooldownRemaining > 0f;

    // Cooldowns are now read from HitmanConfig (see Settings section below)

    // Pending offer time remaining (exposed for UI)
    public float PendingOfferTimeRemaining => PendingOffer != null ? Math.Max(0f, PendingOfferTimeout - _pendingOfferTimer) : 0f;

    // Exposed for dialogue patches / distract follow
    public NPC? TargetNpc => _targetNpc;

    // Internal — target is tracked as both S1API NPC (if available) and raw GameNPC (always)
    private NPC? _targetNpc;
    private GameNPC? _targetGameNpc;
    private NPC? _clientNpc;
    private float _offerTimer;
    private float _nextOfferDelay;
    private float _pendingOfferTimer;
    private string _lastTrackedRegion = "";
    private bool _initialIntelSent;
    private float _npcCheckTimer;
    private float _periodicIntelTimer;
    private float? _savedTargetRelationship;

    // Paid tracking system
    public bool TrackingEnabled { get; private set; }

    // Dead Drop payment system
    private DeadDrop? _paymentDrop;
    private float _paymentReward;
    private CompassManager.Element? _compassElement;
    public bool IsAwaitingCollection => _paymentDrop != null;
    public string? PaymentDropName => _paymentDrop?.DeadDropName;
    public float PaymentReward => _paymentReward;

    // ── Kill Bonus Tracking ──
    private bool _killUsedCable;
    private bool _killHadWitnesses;
    private bool _killWasRaining;
    private bool _killWasNight;

    public void SetKillUsedCable() => _killUsedCable = true;
    public void SetKillHadWitnesses() => _killHadWitnesses = true;

    /// <summary>Snapshot weather and time conditions at the moment of the kill.</summary>
    public void SnapshotKillConditions()
    {
        try
        {
            var weather = Il2CppScheduleOne.Weather.EnvironmentManager.Instance?.CurrentWeatherConditions;
            _killWasRaining = weather != null && weather.Rainy > 0.1f;
        }
        catch { _killWasRaining = false; }

        try
        {
            var timeMgr = Il2CppScheduleOne.GameTime.TimeManager.Instance;
            if (timeMgr != null)
            {
                int time = timeMgr.CurrentTime;
                _killWasNight = time >= 1800 || time < 600; // 6 PM to 6 AM
            }
        }
        catch { _killWasNight = false; }

        Melon<HitmanModMain>.Logger.Msg($"[THM] Kill conditions: rain={_killWasRaining}, night={_killWasNight}, cable={_killUsedCable}, witnesses={_killHadWitnesses}");
    }

    public event Action? OnStateChanged;

    // Settings — all values read from HitmanConfig (MelonPreferences)
    private float MinOfferInterval => HitmanConfig.OfferInterval_Min;
    private float MaxOfferInterval => HitmanConfig.OfferInterval_Max;
    private float PendingOfferTimeout => HitmanConfig.OfferTimeout;
    private const int MaxLevel = 10;
    private float KnockoutRewardMultiplier => HitmanConfig.KnockoutPayMultiplier;

    private float CooldownAfterComplete => HitmanConfig.CooldownAfterComplete;
    private float CooldownAfterDecline => HitmanConfig.CooldownAfterDecline;
    private float CooldownAfterExpire => HitmanConfig.CooldownAfterFail;
    private float CooldownAfterFail => HitmanConfig.CooldownAfterFail;
    private float CooldownAfterAbort => HitmanConfig.CooldownAfterAbort;

    // Level thresholds (XP needed for level N+1)
    private static readonly int[] LevelThresholds = { 0, 100, 250, 500, 800, 1200, 1700, 2300, 3000, 4000 };

    // Intel messages (expanded pool)
    private static readonly string[] ExactIntel =
    {
        "Intel update: The target was last seen in <b>{0}</b>.",
        "Our guy just spotted the target around <b>{0}</b>.",
        "Fresh intel - target is currently in <b>{0}</b>.",
        "Eyes on the ground confirm the target is in <b>{0}</b>.",
        "Confirmed sighting in <b>{0}</b>. Move fast.",
        "Reliable source says target is in <b>{0}</b> right now.",
        "Target is definitely in <b>{0}</b>. Go get them.",
        "Just got word - target was seen walking through <b>{0}</b>."
    };

    private static readonly string[] VagueIntel =
    {
        "Someone matching the description was seen near <b>{0}</b>.",
        "Unconfirmed report - target might be around <b>{0}</b>.",
        "I'm hearing the target could be in <b>{0}</b>. Can't confirm.",
        "One of my guys thinks they saw the target near <b>{0}</b>. Take it with a grain of salt.",
        "Word on the street says <b>{0}</b>, but don't quote me.",
        "Got a tip about <b>{0}</b>. Could be old info though.",
        "Rumors point to <b>{0}</b>. Proceed with caution.",
        "Might be worth checking <b>{0}</b>. No guarantees."
    };

    private static readonly string[] StillInRegionIntel =
    {
        "No movement. Target appears to still be in <b>{0}</b>.",
        "Our source confirms the target is still around <b>{0}</b>.",
        "Target hasn't moved. Still in <b>{0}</b>.",
        "Update: target remains in <b>{0}</b>."
    };

    private bool IsNpcValid(NPC? npc)
    {
        if (npc == null) return false;
        try
        {
            _ = npc.IsDead; // Forces S1NPC Il2Cpp access to detect GC'd objects
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string? _cachedGameFolderPath;

    public void Initialize()
    {
        try { _cachedGameFolderPath = LoadManager.Instance?.LoadedGameFolderPath; }
        catch { _cachedGameFolderPath = null; }

        _saveEnabled = true;
        Load();

        _nextOfferDelay = RandomFloat(MinOfferInterval, MaxOfferInterval);
        _offerTimer = 0f;
        int s1apiValid = GetValidTargets().Count;
        int gameValid = s1apiValid == 0 ? GetValidGameTargets().Count : 0;
        int totalValid = s1apiValid + gameValid;
        Melon<HitmanModMain>.Logger.Msg($"ContractManager initialized. {totalValid} valid targets (S1API={s1apiValid}, GameNPC={gameValid}).");
    }

    /// <summary>
    /// After Initialize, try to re-resolve NPC references for a loaded active contract.
    /// </summary>
    public void TryResolveActiveContract()
    {
        if (ActiveContract == null) return;

        try
        {
            if (NPC.All == null)
            {
                Melon<HitmanModMain>.Logger.Warning("TryResolveActiveContract: NPC.All is null.");
            }
            else
            {
                foreach (var n in NPC.All)
                {
                    try
                    {
                        if (n == null || n.gameObject == null || !n.gameObject.activeInHierarchy) continue;
                        if (n.IsDead || n.IsKnockedOut) continue;
                        if (n.FullName == ActiveContract.TargetName)
                        {
                            _targetNpc = n;
                            break;
                        }
                    }
                    catch { continue; }
                }
            }
        }
        catch { /* NPC.All iteration failed */ }

        if (_targetNpc != null)
        {
            _clientNpc = GetRandomClient(_targetNpc);
            _initialIntelSent = false;
            _lastTrackedRegion = "";
            _periodicIntelTimer = 0f;
            LastKnownLocation = "";
            TargetDistance = -1f;

            try { NPCDefenseHandler.PrepareTarget(_targetNpc, ActiveContract.Difficulty); }
            catch (Exception ex) { Melon<HitmanModMain>.Logger.Warning($"Failed to re-arm target: {ex.Message}"); }

            try { DistractChoiceInjector.InjectChoice(_targetNpc); }
            catch (Exception ex) { Melon<HitmanModMain>.Logger.Warning($"Failed to inject distract choice (resolve): {ex.Message}"); }

            Melon<HitmanModMain>.Logger.Msg($"Resolved active contract target: {ActiveContract.TargetName}");
        }
        else
        {
            Melon<HitmanModMain>.Logger.Warning($"Could not find target '{ActiveContract.TargetName}' — contract lost.");
            ActiveContract = null;
            ResetContractState();
            CooldownRemaining = CooldownAfterFail;
            OnStateChanged?.Invoke();
        }
    }

    public void Update(float deltaTime)
    {
        // Check if player collected payment from dead drop
        if (_paymentDrop != null)
        {
            try
            {
                var storage = _paymentDrop.Storage;
                if (storage != null && storage.ItemCount == 0)
                {
                    // Player picked up the cash!
                    TotalEarnings += _paymentReward;
                    Melon<HitmanModMain>.Logger.Msg($"[THM] Payment collected from dead drop! +${_paymentReward}");
                    CleanupPaymentDrop();
                    OnStateChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Melon<HitmanModMain>.Logger.Warning($"[THM] Payment check failed: {ex.Message}");
                CleanupPaymentDrop();
            }
        }

        // Active contract check (throttled)
        if (ActiveContract != null && _targetNpc != null)
        {
            _periodicIntelTimer += deltaTime;

            _npcCheckTimer -= deltaTime;
            if (_npcCheckTimer <= 0f)
            {
                _npcCheckTimer = 0.25f;

                try
                {
                    if (IsTargetDead)
                    {
                        if (ActiveContract.Type == ContractType.Kill)
                        {
                            SnapshotKillConditions();
                            CompleteContract();
                        }
                        else
                            FailContract("I said knock them out, NOT kill them! Deal's off.");
                        return;
                    }
                    if (IsTargetKnockedOut && ActiveContract.Type == ContractType.Knockout)
                    {
                        CompleteContract();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    string exTypeName = ex.GetType().Name;
                    bool isDestroyedObject = ex is NullReferenceException
                        || ex is MissingReferenceException
                        || exTypeName == "ObjectCollectedException"
                        || (ex.Message != null && (ex.Message.Contains("destroy", StringComparison.OrdinalIgnoreCase)
                            || ex.Message.Contains("collected", StringComparison.OrdinalIgnoreCase)));

                    if (isDestroyedObject)
                    {
                        if (ActiveContract.Type == ContractType.Kill)
                        {
                            Melon<HitmanModMain>.Logger.Msg($"Target NPC destroyed ({ex.GetType().Name}: {ex.Message}), assuming kill — completing contract.");
                            CompleteContract();
                        }
                        else
                        {
                            FailContract("Lost track of the target. Contract failed.");
                        }
                    }
                    else
                    {
                        Melon<HitmanModMain>.Logger.Warning($"Unexpected exception checking target NPC ({ex.GetType().Name}: {ex.Message}) — failing contract.");
                        FailContract("Something went wrong. Contract failed.");
                    }
                    return;
                }

                if (!IsNpcValid(_targetNpc))
                {
                    FailContract("Lost track of the target. Contract failed.");
                    return;
                }

                NPCDefenseHandler.CheckAndArm(_targetNpc);
                EnsureClientValid();
                UpdateTracking();
            }
        }

        // Distract follow update
        DistractFollow.Update(deltaTime);

        // Pending offer timeout
        if (PendingOffer != null)
        {
            _pendingOfferTimer += deltaTime;
            if (_pendingOfferTimer >= PendingOfferTimeout)
            {
                Melon<HitmanModMain>.Logger.Msg("Pending offer expired.");
                ExpirePendingOffer();
            }
        }

        // Cooldown countdown
        if (CooldownRemaining > 0f)
        {
            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - deltaTime);
            if (CooldownRemaining <= 0f)
                OnStateChanged?.Invoke();
        }

        // Auto-offer new contracts (only if Hitman career is unlocked)
        if (IsHitmanUnlocked && ActiveContract == null && PendingOffer == null && !IsOnCooldown)
        {
            _offerTimer += deltaTime;
            if (_offerTimer >= _nextOfferDelay)
            {
                _offerTimer = 0f;
                _nextOfferDelay = RandomFloat(MinOfferInterval, MaxOfferInterval);
                OfferNewContract();
            }
        }
    }

    /// <summary>
    /// Returns null on success, or an error message string on failure (for UI display).
    /// </summary>
    public string? RequestContract()
    {
        LastRequestError = null;

        if (ActiveContract != null)
        {
            LastRequestError = "You already have an active contract.";
            return LastRequestError;
        }
        if (PendingOffer != null)
        {
            LastRequestError = "You have a pending offer. Check the app.";
            return LastRequestError;
        }
        if (IsOnCooldown)
        {
            int mins = (int)(CooldownRemaining / 60);
            int secs = (int)(CooldownRemaining % 60);
            LastRequestError = $"Cooldown active: {mins}m {secs:D2}s remaining.";
            return LastRequestError;
        }

        int s1apiCount = NPC.All?.Count ?? 0;
        int gameNpcCount = 0;
        try { gameNpcCount = UnityEngine.Object.FindObjectsOfType<GameNPC>()?.Length ?? 0; } catch { }
        Melon<HitmanModMain>.Logger.Msg($"RequestContract: S1API={s1apiCount}, GameNPC={gameNpcCount}");

        if (s1apiCount == 0 && gameNpcCount == 0)
        {
            LastRequestError = "No NPCs available yet. Try again later.";
            return LastRequestError;
        }

        OfferNewContract();

        if (PendingOffer == null)
        {
            LastRequestError = "No valid targets available right now.";
            return LastRequestError;
        }

        return null;
    }

    public void AbortContract()
    {
        if (ActiveContract == null || ActiveContract.Status != ContractStatus.Active) return;

        ActiveContract.Status = ContractStatus.Failed;

        // Restore target's relationship to pre-contract value
        if (_savedTargetRelationship.HasValue && IsNpcValid(_targetNpc))
        {
            try
            {
                var rel = _targetNpc!.Relationship;
                float currentDelta = rel.Delta;
                float correction = _savedTargetRelationship.Value - currentDelta;
                if (Math.Abs(correction) > 0.01f)
                {
                    rel.Add(correction);
                    Melon<HitmanModMain>.Logger.Msg($"Restored target relationship on abort: {currentDelta:F2} → {_savedTargetRelationship.Value:F2} (correction: {correction:+0.00;-0.00})");
                }
            }
            catch (Exception ex)
            {
                Melon<HitmanModMain>.Logger.Warning($"Could not restore target relationship on abort: {ex.Message}");
            }
            _savedTargetRelationship = null;
        }

        if (IsNpcValid(_targetNpc))
        {
            try { NPCDefenseHandler.CleanupTarget(_targetNpc!); }
            catch { /* NPC destroyed */ }
        }

        SendClientMessage("You're backing out? Fine. Don't waste my time again.");
        ResetContractState();
        CooldownRemaining = CooldownAfterAbort;
        OnStateChanged?.Invoke();
    }

    // Try to find a new client NPC if current one died/KO'd
    private void EnsureClientValid()
    {
        if (IsNpcValid(_clientNpc))
        {
            try { if (!_clientNpc!.IsDead && !_clientNpc.IsKnockedOut) return; }
            catch { /* fall through to reassign */ }
        }
        if (_targetNpc == null) return;

        var newClient = GetRandomClient(_targetNpc);
        if (newClient != null)
        {
            _clientNpc = newClient;
            Melon<HitmanModMain>.Logger.Msg("Client NPC reassigned (previous client invalid).");
        }
    }

    // ===================== LEVEL SYSTEM =====================

    private static readonly ContractDifficulty[] DiffLevel1 =
        { ContractDifficulty.VeryEasy, ContractDifficulty.Easy };
    private static readonly ContractDifficulty[] DiffLevel3 =
        { ContractDifficulty.VeryEasy, ContractDifficulty.Easy, ContractDifficulty.Medium };
    private static readonly ContractDifficulty[] DiffLevel5 =
        { ContractDifficulty.VeryEasy, ContractDifficulty.Easy, ContractDifficulty.Medium, ContractDifficulty.Hard };
    private static readonly ContractDifficulty[] DiffLevel8 =
        { ContractDifficulty.VeryEasy, ContractDifficulty.Easy, ContractDifficulty.Medium, ContractDifficulty.Hard, ContractDifficulty.VeryHard };

    private ContractDifficulty[] GetAvailableDifficulties()
    {
        if (Level >= 8) return DiffLevel8;
        if (Level >= 5) return DiffLevel5;
        if (Level >= 3) return DiffLevel3;
        return DiffLevel1;
    }

    // Weighted selection — harder difficulties more likely
    private ContractDifficulty PickWeightedDifficulty()
    {
        var available = GetAvailableDifficulties();
        int totalWeight = 0;
        for (int i = 0; i < available.Length; i++)
            totalWeight += i + 1;

        int roll = Rng.Next(totalWeight);
        int cumulative = 0;
        for (int i = 0; i < available.Length; i++)
        {
            cumulative += i + 1;
            if (roll < cumulative)
                return available[i];
        }
        return available[^1];
    }

    private void AddExperience(ContractDifficulty difficulty)
    {
        if (Level >= MaxLevel) return;

        int xp = HitmanConfig.GetXP(difficulty);
        Experience += xp;

        while (Level < MaxLevel && Experience >= LevelThresholds[Level])
        {
            Level++;
            Melon<HitmanModMain>.Logger.Msg($"Hitman Level Up! Now Level {Level}.");
            SendClientMessage($"Your reputation is growing. You're now <b>Reputation Level {Level}</b>.\n\n" +
                GetLevelUpMessage());
        }
    }

    private string GetLevelUpMessage()
    {
        return Level switch
        {
            3 => "Medium difficulty contracts are now available.",
            5 => "Hard contracts unlocked. Targets will be armed.",
            8 => "Very Hard contracts unlocked. Only for the best.",
            10 => "Max level reached. You're a legend.",
            _ => "More challenging contracts will come your way."
        };
    }

    // ===================== TRACKING SYSTEM =====================

    private void UpdateTracking()
    {
        if (ActiveContract == null || _targetNpc == null) return;
        if (!IsNpcValid(_targetNpc)) return;

        // Set initial region once
        if (!_initialIntelSent)
        {
            try
            {
                string currentRegion = GetRegionName(_targetNpc);
                LastKnownLocation = currentRegion;
                _lastTrackedRegion = currentRegion;
                _initialIntelSent = true;
                OnStateChanged?.Invoke();
            }
            catch { }
        }

        // Live distance tracking (only when paid for)
        if (TrackingEnabled)
        {
            UpdateTargetDistance();

            try
            {
                string currentRegion = GetRegionName(_targetNpc);
                if (currentRegion != _lastTrackedRegion)
                {
                    _lastTrackedRegion = currentRegion;
                    LastKnownLocation = currentRegion;
                    OnStateChanged?.Invoke();
                }
            }
            catch { }
        }
    }

    /// <summary>Paid hack: enables live distance tracking for this contract.</summary>
    public string HackTargetLocation()
    {
        if (ActiveContract == null || _targetNpc == null || !IsNpcValid(_targetNpc))
            return "No active contract.";

        if (TrackingEnabled)
            return "Tracking is already active.";

        float hackCost = HitmanConfig.GetHackCost(ActiveContract.Difficulty);

        try
        {
            float balance = Money.GetOnlineBalance();
            if (balance < hackCost)
                return $"Not enough funds. You need ${hackCost:N0}.";

            Money.CreateOnlineTransaction("Signal Trace", -hackCost, 1, "Hitman phone trace service");
        }
        catch (Exception ex)
        {
            return $"Payment failed: {ex.Message}";
        }

        TrackingEnabled = true;
        SendClientMessage("Signal hack active. Check the app for a live distance readout.");

        OnStateChanged?.Invoke();
        Melon<HitmanModMain>.Logger.Msg($"[THM] Tracking enabled (paid ${hackCost})");
        return "";
    }

    // GetHackCost moved to HitmanConfig.GetHackCost()

    private void SendTrackingIntel(string region, bool forceExact = false)
    {
        if (ActiveContract == null) return;
        LastKnownLocation = region;

        if (!IsNpcValid(_clientNpc))
        {
            OnStateChanged?.Invoke();
            return;
        }

        string message;
        if (forceExact || ActiveContract.Difficulty <= ContractDifficulty.Easy)
        {
            message = string.Format(ExactIntel[Rng.Next(ExactIntel.Length)], region);
        }
        else if (ActiveContract.Difficulty == ContractDifficulty.Medium)
        {
            var templates = Rng.Next(3) == 0 ? VagueIntel : ExactIntel;
            message = string.Format(templates[Rng.Next(templates.Length)], region);
        }
        else
        {
            message = string.Format(VagueIntel[Rng.Next(VagueIntel.Length)], region);
        }

        try { _clientNpc!.SendTextMessage(message); }
        catch { /* Client NPC destroyed */ }
        OnStateChanged?.Invoke();
    }

    private void SendPeriodicIntel(string region)
    {
        if (!IsNpcValid(_clientNpc) || ActiveContract == null) return;

        LastKnownLocation = region;
        string message = string.Format(StillInRegionIntel[Rng.Next(StillInRegionIntel.Length)], region);

        try { _clientNpc!.SendTextMessage(message); }
        catch { /* Client NPC destroyed */ }
        OnStateChanged?.Invoke();
    }

    private void UpdateTargetDistance()
    {
        try
        {
            var player = GamePlayer.Local;
            if (player == null || _targetNpc == null) { TargetDistance = -1f; return; }

            // NPCs inside buildings have their transform at world-origin (interior scenes at 0,0,0)
            // — use IsInBuilding to detect and skip distance calculation in that case
            if (_targetNpc.IsInBuilding) { TargetDistance = -2f; return; } // -2 = "inside building"

            var playerPos = player.transform.position;
            var targetPos = _targetNpc.gameObject.transform.position;

            // Sanity check: discard if position is at/near world origin (unloaded NPC)
            if (targetPos.sqrMagnitude < 1f) { TargetDistance = -1f; return; }

            TargetDistance = Vector3.Distance(playerPos, targetPos);
        }
        catch
        {
            TargetDistance = -1f;
        }
    }

    /// <summary>
    /// Formats the target distance for UI display, scaled by difficulty.
    /// </summary>
    public string GetFormattedDistance()
    {
        if (ActiveContract == null) return "";
        if (TargetDistance == -2f) return "Inside building";
        if (TargetDistance < 0f) return "";

        return ActiveContract.Difficulty switch
        {
            // Exact distance rounded to 5m
            ContractDifficulty.VeryEasy or ContractDifficulty.Easy =>
                TargetDistance < 5f ? "<5m" : $"~{(int)(Mathf.Round(TargetDistance / 5f) * 5f)}m",

            // Rounded to 25m
            ContractDifficulty.Medium =>
                TargetDistance < 25f ? "<25m" : $"~{(int)(Mathf.Round(TargetDistance / 25f) * 25f)}m",

            // Rough range only
            ContractDifficulty.Hard => TargetDistance switch
            {
                < 50f => "<50m",
                < 150f => "50-150m",
                < 300f => "150-300m",
                _ => ">300m"
            },

            // No tracking
            _ => ""
        };
    }

    private static readonly Dictionary<string, string> RegionNameMap = new()
    {
        { "Docks", "the Docks" },
        { "Downtown", "Downtown" },
        { "Northtown", "Northtown" },
        { "Suburbia", "Suburbia" },
        { "Uptown", "Uptown" },
        { "Westville", "Westville" }
    };

    private static readonly Dictionary<int, string> _regionIntCache = new();

    private string GetRegionName(NPC npc)
    {
        try
        {
            // Prefer npc.Region — it's the game's own region assignment, reliable for
            // both outdoor and indoor NPCs. Position-based lookup fails for NPCs inside
            // buildings (interior scenes placed at world-origin return wrong region).
            int regionInt = (int)npc.Region;
            if (_regionIntCache.TryGetValue(regionInt, out string? cached))
                return cached;

            string raw = npc.Region.ToString();
            string friendly = RegionNameMap.TryGetValue(raw, out string? mapped) ? mapped : raw;
            _regionIntCache[regionInt] = friendly;
            return friendly;
        }
        catch
        {
            return string.IsNullOrEmpty(_lastTrackedRegion) ? "an unknown area" : _lastTrackedRegion;
        }
    }

    // ===================== CONTRACT LIFECYCLE =====================

    private void OfferNewContract()
    {
        // Try S1API NPCs first, fall back to Il2Cpp GameNPCs
        var targets = GetValidTargets();
        string targetName;
        string targetDesc = "";

        if (targets.Count > 0)
        {
            var target = targets[Rng.Next(targets.Count)];
            _targetNpc = target;
            _targetGameNpc = target.gameObject?.GetComponent<GameNPC>();
            targetName = target.FullName;
            try { targetDesc = TargetDescription.Generate(target); } catch { }
        }
        else
        {
            // Fallback: use Il2Cpp GameNPCs directly
            var gameTargets = GetValidGameTargets();
            if (gameTargets.Count == 0)
            {
                Melon<HitmanModMain>.Logger.Warning("No valid targets (S1API or GameNPC).");
                return;
            }

            var gameTarget = gameTargets[Rng.Next(gameTargets.Count)];
            _targetNpc = null;
            _targetGameNpc = gameTarget;
            targetName = "?";
            try { targetName = gameTarget.fullName; } catch { }
            try { targetDesc = TargetDescription.GenerateFromGameNpc(gameTarget); } catch { }
        }

        var client = GetRandomClient(_targetNpc);
        // If no S1API client, use MysteriousMan as fallback messenger
        _clientNpc = client;

        var difficulty = PickWeightedDifficulty();
        var type = Rng.Next(2) == 0 ? ContractType.Kill : ContractType.Knockout;
        var reward = CalculateReward(difficulty, type);

        var contract = new Contract
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            TargetName = targetName,
            TargetDescription = targetDesc,
            Type = type,
            Difficulty = difficulty,
            Reward = reward,
            Status = ContractStatus.Offered
        };

        PendingOffer = contract;
        _pendingOfferTimer = 0f;

        string typeDesc = type == ContractType.Kill
            ? "I need them <b>eliminated permanently</b>"
            : "I need them <b>knocked out</b> - don't kill them";

        string message = $"I have a job for you.\n\n" +
                         $"{typeDesc}.\n\n" +
                         $"Difficulty: <b>{contract.DifficultyLabel}</b>\n" +
                         $"Reward: <b>${reward:N0}</b>" +
                         $"\n\nOpen the <b>Hitman</b> app to accept or decline.";

        try { SendClientMessage(message); }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"Failed to send contract offer message: {ex.Message}");
            PendingOffer = null;
            _targetNpc = null;
            _clientNpc = null;
            return;
        }
        OnStateChanged?.Invoke();
        Melon<HitmanModMain>.Logger.Msg($"Contract offered: {type} {targetName} ({difficulty}) for ${reward}");
    }

    private void OnContractAccepted()
    {
        if (PendingOffer == null) return;
        if (_targetNpc == null || !IsNpcValid(_targetNpc))
        {
            Melon<HitmanModMain>.Logger.Warning("OnContractAccepted: target NPC no longer valid.");
            SendClientMessage("Lost contact with the target. Deal's off.");
            ClearPendingState();
            CooldownRemaining = CooldownAfterDecline;
            OnStateChanged?.Invoke();
            return;
        }

        try
        {
            if (_targetNpc.IsDead || _targetNpc.IsKnockedOut)
            {
                SendClientMessage("Bad news - the target is already down. Deal's off.");
                ClearPendingState();
                CooldownRemaining = CooldownAfterDecline;
                OnStateChanged?.Invoke();
                return;
            }
        }
        catch
        {
            SendClientMessage("Lost contact with the target. Deal's off.");
            ClearPendingState();
            CooldownRemaining = CooldownAfterDecline;
            OnStateChanged?.Invoke();
            return;
        }

        ActiveContract = PendingOffer;
        ActiveContract.Status = ContractStatus.Active;
        PendingOffer = null;
        _pendingOfferTimer = 0f;
        _initialIntelSent = false;
        _lastTrackedRegion = "";
        _periodicIntelTimer = 0f;
        LastKnownLocation = "";
        TargetDistance = -1f;

        // Save target's relationship so we can restore it after the kill
        _savedTargetRelationship = null;
        try { _savedTargetRelationship = _targetNpc.Relationship.Delta; }
        catch { /* NPC may not have relationship data */ }

        try { NPCDefenseHandler.PrepareTarget(_targetNpc, ActiveContract.Difficulty); }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"Failed to arm target: {ex.Message}");
        }

        try { DistractChoiceInjector.InjectChoice(_targetNpc!); }
        catch (Exception ex) { Melon<HitmanModMain>.Logger.Warning($"Failed to inject distract choice: {ex.Message}"); }

        string warning = ActiveContract.Difficulty >= ContractDifficulty.Hard
            ? " <b>Be careful - the target may be armed.</b>"
            : "";

        string koWarning = ActiveContract.Type == ContractType.Knockout
            ? "\n<b>DO NOT KILL the target!</b>"
            : "";

        string region = GetRegionName(_targetNpc);

        SendClientMessage(
            $"Good. {ActiveContract.TypeLabel} them.{warning}{koWarning}\n\n" +
            $"Last seen in <b>{region}</b>.\n\n" +
            $"Payment of <b>${ActiveContract.Reward:N0}</b> upon completion.\n\n" +
            $"Check the app for target description."
        );

        OnStateChanged?.Invoke();
        Melon<HitmanModMain>.Logger.Msg($"Contract activated: {ActiveContract.TypeLabel} {_targetNpc.FullName}");
    }

    public void AcceptPendingOffer() => OnContractAccepted();
    public void DeclinePendingOffer() => OnContractDeclined();

    private void OnContractDeclined()
    {
        if (PendingOffer == null) return;
        SendClientMessage("Not interested.");
        ClearPendingState();
        CooldownRemaining = CooldownAfterDecline;
        OnStateChanged?.Invoke();
        Melon<HitmanModMain>.Logger.Msg("Contract declined by player.");
    }

    private void ExpirePendingOffer()
    {
        if (IsNpcValid(_clientNpc))
        {
            try { _clientNpc!.SendTextMessage("You took too long. Offer withdrawn."); }
            catch { /* Client NPC destroyed */ }
        }
        ClearPendingState();
        CooldownRemaining = CooldownAfterExpire;
        OnStateChanged?.Invoke();
    }

    private void ClearPendingState()
    {
        PendingOffer = null;
        _targetNpc = null;
        _clientNpc = null;
        _pendingOfferTimer = 0f;
        _offerTimer = 0f;
    }

    private void CompleteContract()
    {
        if (ActiveContract == null || ActiveContract.Status != ContractStatus.Active) return;

        ActiveContract.Status = ContractStatus.Completed;
        float baseReward = ActiveContract.Reward;

        // ── Calculate bonuses (percentage of base reward) ──
        float bonusTotal = 0f;
        string bonusText = "";

        // Stealthy: Fibre Wire kill + no witnesses (+15%)
        if (_killUsedCable && !_killHadWitnesses)
        {
            float bonus = baseReward * 0.15f;
            bonusTotal += bonus;
            bonusText += $"\n☠ <b>+${bonus:N0}</b> Stealthy Bonus";
            Melon<HitmanModMain>.Logger.Msg($"[THM] Bonus: Stealthy +${bonus:N0} (15%)");
        }
        // Silent Kill: Fibre Wire used (but had witnesses, so no Stealthy)
        else if (_killUsedCable)
        {
            float bonus = baseReward * 0.10f;
            bonusTotal += bonus;
            bonusText += $"\n🔪 <b>+${bonus:N0}</b> Silent Kill Bonus";
            Melon<HitmanModMain>.Logger.Msg($"[THM] Bonus: Silent Kill +${bonus:N0} (10%)");
        }
        // No witnesses but no cable
        else if (!_killHadWitnesses)
        {
            float bonus = baseReward * 0.10f;
            bonusTotal += bonus;
            bonusText += $"\n👻 <b>+${bonus:N0}</b> Ghost Bonus";
            Melon<HitmanModMain>.Logger.Msg($"[THM] Bonus: Ghost +${bonus:N0} (10%)");
        }

        // Rain Kill: it was raining during the kill (+10%)
        if (_killWasRaining)
        {
            float bonus = baseReward * 0.10f;
            bonusTotal += bonus;
            bonusText += $"\n🌧 <b>+${bonus:N0}</b> Rain Kill Bonus";
            Melon<HitmanModMain>.Logger.Msg($"[THM] Bonus: Rain Kill +${bonus:N0} (10%)");
        }

        // Night Owl: kill after 6 PM (+10%)
        if (_killWasNight)
        {
            float bonus = baseReward * 0.10f;
            bonusTotal += bonus;
            bonusText += $"\n🌙 <b>+${bonus:N0}</b> Night Owl Bonus";
            Melon<HitmanModMain>.Logger.Msg($"[THM] Bonus: Night Owl +${bonus:N0} (10%)");
        }

        float reward = baseReward + bonusTotal;

        // Reset bonus flags
        _killUsedCable = false;
        _killHadWitnesses = false;
        _killWasRaining = false;
        _killWasNight = false;

        CompletedCount++;
        AddExperience(ActiveContract.Difficulty);
        int xp = HitmanConfig.GetXP(ActiveContract.Difficulty);

        // Place reward in a random dead drop instead of direct payment
        bool dropPlaced = false;
        try
        {
            var player = GamePlayer.Local;
            var playerPos = player != null ? player.transform.position : Vector3.zero;
            var drop = DeadDrop.GetRandomEmptyDrop(playerPos);

            if (drop != null && drop.Storage != null)
            {
                var moneyMgr = MoneyManager.Instance;
                if (moneyMgr != null)
                {
                    var cashItem = moneyMgr.GetCashInstance(reward);
                    drop.Storage.InsertItem(cashItem, true);

                    _paymentDrop = drop;
                    _paymentReward = reward;
                    dropPlaced = true;

                    // Activate the dead drop's map POI so player can find it
                    string dropName = drop.DeadDropName ?? "a dead drop";
                    try
                    {
                        var poi = drop.PoI;
                        if (poi != null)
                        {
                            poi.SetMainText("$ Payment");
                            Melon<HitmanModMain>.Logger.Msg($"[THM] Dead drop POI activated: {dropName}");
                        }

                        // Also try compass marker
                        var compass = CompassManager.Instance;
                        if (compass != null && drop.transform != null)
                        {
                            var prefab = compass.ElementPrefab?.GetComponent<UnityEngine.RectTransform>();
                            if (prefab != null)
                            {
                                _compassElement = compass.AddElement(drop.transform, prefab, true);
                                Melon<HitmanModMain>.Logger.Msg($"[THM] Compass element added for dead drop.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Melon<HitmanModMain>.Logger.Warning($"[THM] Dead drop marker failed: {ex.Message}");
                    }

                    SendClientMessage(
                        $"Good work. <b>${reward:N0}</b> is waiting for you at <b>{dropName}</b>.\n" +
                        $"<b>+{xp} REP</b>" +
                        (bonusText.Length > 0 ? $"\n{bonusText}" : "") +
                        $"\n\nGo collect your payment."
                    );

                    Melon<HitmanModMain>.Logger.Msg($"[THM] Contract completed! ${reward} placed at dead drop '{dropName}'");
                }
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"[THM] Dead drop placement failed: {ex.Message}");
        }

        // Fallback: direct payment if dead drop failed
        if (!dropPlaced)
        {
            try
            {
                Money.CreateOnlineTransaction("Contract Payment", reward, 1, $"Hitman contract: {ActiveContract.TargetName}");
                TotalEarnings += reward;
            }
            catch
            {
                try { Money.ChangeCashBalance(reward, true, false); TotalEarnings += reward; }
                catch { }
            }

            SendClientMessage(
                $"Well done. <b>${reward:N0}</b> has been wired to your account.\n" +
                $"<b>+{xp} REP</b>" +
                (bonusText.Length > 0 ? $"\n{bonusText}" : "") +
                $"\n\nI'll be in touch when I have more work for you."
            );
            Melon<HitmanModMain>.Logger.Msg($"[THM] Contract completed! ${reward} paid directly (no dead drop available).");
        }

        // Restore target's relationship to pre-contract value (prevents rep loss for killing customers)
        if (_savedTargetRelationship.HasValue && IsNpcValid(_targetNpc))
        {
            try
            {
                var rel = _targetNpc!.Relationship;
                float currentDelta = rel.Delta;
                float correction = _savedTargetRelationship.Value - currentDelta;
                if (Math.Abs(correction) > 0.01f)
                {
                    rel.Add(correction);
                    Melon<HitmanModMain>.Logger.Msg($"Restored target relationship: {currentDelta:F2} → {_savedTargetRelationship.Value:F2} (correction: {correction:+0.00;-0.00})");
                }
            }
            catch (Exception ex)
            {
                Melon<HitmanModMain>.Logger.Warning($"Could not restore target relationship: {ex.Message}");
            }
            _savedTargetRelationship = null;
        }

        if (IsNpcValid(_targetNpc))
        {
            try { NPCDefenseHandler.CleanupTarget(_targetNpc!); }
            catch { /* NPC destroyed */ }
        }

        ResetContractState();
        CooldownRemaining = CooldownAfterComplete;
        OnStateChanged?.Invoke();
    }

    private void FailContract(string reason)
    {
        if (ActiveContract == null || ActiveContract.Status != ContractStatus.Active) return;

        ActiveContract.Status = ContractStatus.Failed;

        // Restore target's relationship (same as CompleteContract/AbortContract)
        if (_savedTargetRelationship.HasValue && IsNpcValid(_targetNpc))
        {
            try
            {
                var rel = _targetNpc!.Relationship;
                float currentDelta = rel.Delta;
                float correction = _savedTargetRelationship.Value - currentDelta;
                if (Math.Abs(correction) > 0.01f)
                    rel.Add(correction);
            }
            catch { /* NPC may not have relationship data */ }
            _savedTargetRelationship = null;
        }

        if (IsNpcValid(_targetNpc))
        {
            try { NPCDefenseHandler.CleanupTarget(_targetNpc!); }
            catch { /* NPC destroyed */ }
        }

        SendClientMessage(reason);
        Melon<HitmanModMain>.Logger.Msg($"Contract failed: {reason}");
        ResetContractState();
        CooldownRemaining = CooldownAfterFail;
        OnStateChanged?.Invoke();
    }

    private void ResetContractState()
    {
        DistractFollow.StopFollowing();
        DistractChoiceInjector.RemoveChoice();
        ActiveContract = null;
        _targetNpc = null;
        _clientNpc = null;
        _pendingOfferTimer = 0f;
        _offerTimer = 0f;
        _npcCheckTimer = 0f;
        _lastTrackedRegion = "";
        _initialIntelSent = false;
        _periodicIntelTimer = 0f;
        TrackingEnabled = false;
        _nextOfferDelay = RandomFloat(MinOfferInterval, MaxOfferInterval);
        LastKnownLocation = "";
        TargetDistance = -1f;
        _savedTargetRelationship = null;
        _killUsedCable = false;
        _killHadWitnesses = false;
    }

    private void CleanupPaymentDrop()
    {
        // Reset POI text on dead drop
        if (_paymentDrop != null)
        {
            try
            {
                var poi = _paymentDrop.PoI;
                if (poi != null)
                    poi.SetMainText(_paymentDrop.DeadDropName ?? "");
            }
            catch { }
        }

        // Remove compass marker
        if (_compassElement != null)
        {
            try
            {
                var compass = CompassManager.Instance;
                compass?.RemoveElement(_compassElement, true);
            }
            catch { }
            _compassElement = null;
        }
        _paymentDrop = null;
        _paymentReward = 0f;
    }

    public void ForceResetAll()
    {
        _saveEnabled = false;

        if (ActiveContract != null || PendingOffer != null)
            CooldownRemaining = CooldownAfterFail;

        if (ActiveContract != null && IsNpcValid(_targetNpc))
        {
            try { NPCDefenseHandler.CleanupTarget(_targetNpc!); } catch { }
        }
        NPCDefenseHandler.ClearAll();
        _typeIsExcluded.Clear();
        _regionIntCache.Clear();
        _reusableTargets.Clear();
        _reusableClients.Clear();
        ActiveContract = null;
        PendingOffer = null;
        _targetNpc = null;
        _clientNpc = null;
        _pendingOfferTimer = 0f;
        _offerTimer = 0f;
        _npcCheckTimer = 0f;
        _periodicIntelTimer = 0f;
        _nextOfferDelay = RandomFloat(MinOfferInterval, MaxOfferInterval);
        _lastTrackedRegion = "";
        _initialIntelSent = false;
        LastKnownLocation = "";
        TargetDistance = -1f;
        LastRequestError = null;
        _savedTargetRelationship = null;
        _cachedGameFolderPath = null;
        _saveDirCreated = false;

        // Cleanup dead drop payment if pending
        CleanupPaymentDrop();

        // Reset progression to defaults (will be overwritten by Load() if save exists)
        CompletedCount = 0;
        TotalEarnings = 0f;
        Level = 1;
        Experience = 0;
        CooldownRemaining = 0f;
        // NOTE: IsHitmanUnlocked is NOT reset here — only the Reset button in the app does that
        // Don't null OnStateChanged — HitmanApp re-subscribes in OnCreated/OnDestroyed;
        // nulling would orphan any surviving app instance across scene transitions
    }

    private void SendClientMessage(string message)
    {
        // Send all contract messages from the Mysterious Man NPC
        try
        {
            var mysteriousMan = FindMysteriousMan();
            if (mysteriousMan != null)
            {
                mysteriousMan.SendTextMessage(message);
                return;
            }
        }
        catch { }

        // Fallback: send from client NPC if Mysterious Man not found
        if (!IsNpcValid(_clientNpc)) return;
        try
        {
            if (_clientNpc!.IsDead || _clientNpc.IsKnockedOut) return;
            _clientNpc.SendTextMessage(message);
        }
        catch { }
    }

    private static Il2CppScheduleOne.NPCs.NPC? _cachedStranger;

    private static Il2CppScheduleOne.NPCs.NPC? FindMysteriousMan()
    {
        // Use cached reference if still valid
        try
        {
            if (_cachedStranger != null && _cachedStranger.gameObject != null)
                return _cachedStranger;
        }
        catch { _cachedStranger = null; }

        // Only search if cache is invalid
        try
        {
            var allNpcs = UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.NPCs.NPC>(true);
            if (allNpcs == null) return null;
            for (int i = 0; i < allNpcs.Length; i++)
            {
                var npc = allNpcs[i];
                if (npc?.FirstName == "Stranger")
                {
                    _cachedStranger = npc;
                    return npc;
                }
            }
        }
        catch { }
        return null;
    }

    // ===================== HELPERS =====================

    private static readonly Dictionary<System.Type, bool> _typeIsExcluded = new();

    private static readonly HashSet<string> _excludedTypeNames = new()
    {
        // Law enforcement (fallback for proxy type names)
        "PoliceOfficer", "Enforcer", "Guard",
        // Player employees
        "Dealer", "Employee", "Botanist", "Chemist", "Cleaner", "Packager",
        // Economy
        "Supplier", "CartelDealer",
        // Hostile/special encounters
        "CartelGoon", "SewerGoblin", "SchizoGoblin",
        // Unique story/quest/vendor NPCs
        "Oscar", "Ray", "Fixer", "UncleNelson", "SewerKing",
        "Billy", "Stan", "Thomas"
    };

    private static readonly HashSet<string> _excludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Uncle Nelson", "Thomas Benzies", "Fiona Hancock",
        "Sam Thompson", "Ming Chen", "Billy Kramer",
        "Dan Samwell", "Jen Monroe",
        "Arms Dealer", "Skateboard Seller", "Fixer", "The Fixer",
        "Grunk", "Stranger"
    };

    // Name substrings that indicate excluded NPC roles (case-insensitive)
    private static readonly string[] _excludedNamePatterns = new[]
    {
        "Dealer", "Goon", "Officer", "Supplier"
    };

    private static bool IsExcludedNpc(NPC npc)
    {
        try
        {
            // 1. Il2Cpp type hierarchy checks via TryCast (resolves real type, not proxy)
            var gameNpc = npc.gameObject?.GetComponent<GameNPC>();
            if (gameNpc == null) return true;

            try
            {
                if (gameNpc.TryCast<GameDealer>() != null) return true;
                if (gameNpc.TryCast<GameEmployee>() != null) return true;
                if (gameNpc.TryCast<GameSupplier>() != null) return true;
                if (gameNpc.TryCast<GameCartelGoon>() != null) return true;
                if (gameNpc.TryCast<GamePoliceOfficer>() != null) return true;
            }
            catch { /* TryCast failed — fall through to proxy type + name checks */ }

            // 2. Proxy type name check (fallback for types not covered above)
            var type = ((object)gameNpc).GetType();
            if (_typeIsExcluded.TryGetValue(type, out bool excluded))
            {
                if (excluded) return true;
                return IsExcludedByName(npc.FullName);
            }

            string typeName = type.Name;
            excluded = typeName.Contains("Officer") || typeName.Contains("Enforcer") ||
                       typeName.Contains("Guard") || typeName.Contains("Goon") ||
                       typeName.Contains("Dealer") || typeName.Contains("Employee") ||
                       typeName.Contains("Supplier") || _excludedTypeNames.Contains(typeName);
            _typeIsExcluded[type] = excluded;
            if (excluded) return true;

            // 3. Name-based exclusion (catches any remaining edge cases)
            return IsExcludedByName(npc.FullName);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsExcludedByName(string fullName)
    {
        if (_excludedNames.Contains(fullName)) return true;

        // Catch naming patterns like "Benzies Goon", "Benzies Dealers", "Molly" (caught by type), etc.
        foreach (var pattern in _excludedNamePatterns)
        {
            if (fullName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static readonly List<NPC> _reusableTargets = new(128);
    private static readonly List<NPC> _reusableClients = new(128);
    private static readonly List<GameNPC> _reusableGameTargets = new(128);

    // ── Helper properties for target access (works with both S1API and GameNPC) ──

    private bool IsTargetDead
    {
        get
        {
            try { if (_targetNpc != null) return _targetNpc.IsDead; }
            catch { }
            try { if (_targetGameNpc != null) return _targetGameNpc.Health.IsDead; }
            catch { }
            return false;
        }
    }

    private bool IsTargetKnockedOut
    {
        get
        {
            try { if (_targetNpc != null) return _targetNpc.IsKnockedOut; }
            catch { }
            try { if (_targetGameNpc != null) return !_targetGameNpc.Health.IsDead && _targetGameNpc.Health.IsKnockedOut; }
            catch { }
            return false;
        }
    }

    private string TargetFullName
    {
        get
        {
            try { if (_targetNpc != null) return _targetNpc.FullName; }
            catch { }
            try { if (_targetGameNpc != null) return _targetGameNpc.fullName; }
            catch { }
            return ActiveContract?.TargetName ?? "Unknown";
        }
    }

    private Vector3 TargetPosition
    {
        get
        {
            try { if (_targetNpc?.gameObject != null) return _targetNpc.gameObject.transform.position; }
            catch { }
            try { if (_targetGameNpc != null) return _targetGameNpc.transform.position; }
            catch { }
            return Vector3.zero;
        }
    }

    private bool IsNpcValidAny => IsNpcValid(_targetNpc) || IsGameNpcValid(_targetGameNpc);

    private static bool IsGameNpcValid(GameNPC? npc)
    {
        try { return npc != null && npc.gameObject != null; }
        catch { return false; }
    }

    private List<NPC> GetValidTargets()
    {
        _reusableTargets.Clear();
        if (NPC.All == null) return _reusableTargets;
        int total = 0, skippedInvalid = 0, skippedNotPhysical = 0, skippedDead = 0, skippedExcluded = 0;
        try
        {
            foreach (var n in NPC.All)
            {
                total++;
                try
                {
                    if (n == null) { skippedInvalid++; continue; }
                    if (n.gameObject == null || !n.gameObject.activeInHierarchy)
                    { skippedNotPhysical++; continue; }
                    if (n.IsDead || n.IsKnockedOut) { skippedDead++; continue; }
                    if (IsExcludedNpc(n)) { skippedExcluded++; continue; }
                    _reusableTargets.Add(n);
                }
                catch { skippedInvalid++; }
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"GetValidTargets: NPC.All failed after {total}: {ex.Message}");
        }
        if (_reusableTargets.Count == 0 && total > 0)
        {
            Melon<HitmanModMain>.Logger.Msg(
                $"GetValidTargets: 0/{total} S1API NPCs passed (excluded={skippedExcluded}). Falling back to GameNPC scan.");
        }
        return _reusableTargets;
    }

    /// <summary>
    /// Fallback: scan all Il2Cpp GameNPCs directly when S1API NPC.All has no valid targets.
    /// </summary>
    private List<GameNPC> GetValidGameTargets()
    {
        _reusableGameTargets.Clear();
        int total = 0, skipped = 0;
        try
        {
            var allNpcs = UnityEngine.Object.FindObjectsOfType<GameNPC>();
            if (allNpcs == null) return _reusableGameTargets;

            foreach (var npc in allNpcs)
            {
                total++;
                try
                {
                    if (npc == null) continue;
                    if (npc.gameObject == null || !npc.gameObject.activeInHierarchy) continue;
                    try { if (npc.Health.IsDead) continue; } catch { continue; }

                    // Exclude special NPC types
                    try
                    {
                        if (npc.TryCast<GameDealer>() != null) { skipped++; continue; }
                        if (npc.TryCast<GameEmployee>() != null) { skipped++; continue; }
                        if (npc.TryCast<GameSupplier>() != null) { skipped++; continue; }
                        if (npc.TryCast<GameCartelGoon>() != null) { skipped++; continue; }
                        if (npc.TryCast<GamePoliceOfficer>() != null) { skipped++; continue; }
                    }
                    catch { }

                    // Exclude by name patterns
                    string name = "";
                    try { name = npc.fullName ?? ""; } catch { }
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.Contains("Mysterious") || name.Contains("Goon") ||
                        name.Contains("Guard") || name.Contains("Officer"))
                    { skipped++; continue; }

                    _reusableGameTargets.Add(npc);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"GetValidGameTargets failed: {ex.Message}");
        }

        Melon<HitmanModMain>.Logger.Msg($"GetValidGameTargets: {_reusableGameTargets.Count}/{total} passed (skipped={skipped}).");
        return _reusableGameTargets;
    }

    private NPC? GetRandomClient(NPC excludeTarget)
    {
        _reusableClients.Clear();
        if (_reusableTargets.Count > 0)
        {
            foreach (var n in _reusableTargets)
            {
                try
                {
                    if (n != null && n != excludeTarget && n.gameObject != null
                        && n.gameObject.activeInHierarchy && !n.IsDead && !n.IsKnockedOut)
                        _reusableClients.Add(n);
                }
                catch { /* Il2Cpp destroyed object */ }
            }
        }
        else if (NPC.All != null)
        {
            try
            {
                foreach (var n in NPC.All)
                {
                    try
                    {
                        if (n == null) continue;
                        if (n != excludeTarget && n.gameObject != null && n.gameObject.activeInHierarchy && !n.IsDead && !n.IsKnockedOut && !IsExcludedNpc(n))
                            _reusableClients.Add(n);
                    }
                    catch { /* Il2Cpp destroyed object */ }
                }
            }
            catch { /* NPC.All iteration failed */ }
        }
        return _reusableClients.Count > 0 ? _reusableClients[Rng.Next(_reusableClients.Count)] : null;
    }

    private float CalculateReward(ContractDifficulty difficulty, ContractType type)
    {
        var (min, max) = HitmanConfig.GetRewardRange(difficulty);
        float raw = RandomFloat(min, max);
        float rounded = (float)(Math.Round(raw / 50) * 50);
        float reward = Math.Clamp(rounded, min, max);

        // Knockout contracts pay 20% more (harder to execute)
        if (type == ContractType.Knockout)
            reward *= KnockoutRewardMultiplier;

        return (float)(Math.Round(reward / 50) * 50);
    }

    private static float RandomFloat(float min, float max)
    {
        return (float)(Rng.NextDouble() * (max - min) + min);
    }

    // ===================== SAVE / LOAD =====================

    private bool _saveHooked;
    private bool _saveEnabled = true;
    private UnityAction? _saveDelegate;
    private bool _saveDirCreated;
    private const int CurrentSaveVersion = 1;

    public void HookGameSaveEvents()
    {
        if (_saveHooked) return;
        try
        {
            var sm = SaveManager.Instance;
            if (sm != null)
            {
                _saveDelegate = (UnityAction)OnGameSaved;
                sm.onSaveComplete.AddListener(_saveDelegate);
                _saveHooked = true;
                Melon<HitmanModMain>.Logger.Msg("Hooked into game SaveManager.");
            }
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Warning($"Could not hook SaveManager: {ex.Message}");
        }
    }

    public void UnhookGameSaveEvents()
    {
        if (!_saveHooked || _saveDelegate == null) return;
        try
        {
            var sm = SaveManager.Instance;
            if (sm != null)
                sm.onSaveComplete.RemoveListener(_saveDelegate);
        }
        catch { }
        _saveDelegate = null;
        _saveHooked = false;
    }

    private void OnGameSaved()
    {
        if (!_saveEnabled) return;
        Save();
    }

    private string? GetSaveFilePath()
    {
        try
        {
            string? folder = _cachedGameFolderPath;
            if (string.IsNullOrEmpty(folder)) return null;
            string modFolder = Path.Combine(folder, "Modded", "HitmanMod");
            if (!_saveDirCreated)
            {
                Directory.CreateDirectory(modFolder);
                _saveDirCreated = true;
            }
            return Path.Combine(modFolder, "progress.json");
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            string? path = GetSaveFilePath();
            if (path == null)
            {
                Melon<HitmanModMain>.Logger.Warning("Save skipped — no active game save folder.");
                return;
            }

            ContractSaveData? activeData = null;
            if (ActiveContract != null && ActiveContract.Status == ContractStatus.Active)
            {
                activeData = new ContractSaveData
                {
                    Id = ActiveContract.Id,
                    TargetName = ActiveContract.TargetName,
                    Type = (int)ActiveContract.Type,
                    Difficulty = (int)ActiveContract.Difficulty,
                    Reward = ActiveContract.Reward
                };
            }

            var data = new HitmanSaveData
            {
                Version = CurrentSaveVersion,
                CompletedCount = CompletedCount,
                TotalEarnings = TotalEarnings,
                Level = Level,
                Experience = Experience,
                CooldownRemaining = CooldownRemaining,
                ActiveContract = activeData,
                HitmanUnlocked = IsHitmanUnlocked
            };

            string json = JsonSerializer.Serialize(data, IndentedJsonOptions);

            // Atomic write: temp file then rename
            string tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, path, true);

            Melon<HitmanModMain>.Logger.Msg("HitmanMod progress saved (game save).");
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Error($"Failed to save HitmanMod: {ex.Message}");
        }
    }

    public void Load()
    {
        try
        {
            string? path = GetSaveFilePath();
            if (path == null)
            {
                Melon<HitmanModMain>.Logger.Warning("Load skipped — save path not available yet.");
                return;
            }
            if (!File.Exists(path))
            {
                Melon<HitmanModMain>.Logger.Msg("No existing save file — starting fresh.");
                return;
            }

            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<HitmanSaveData>(json);
            if (data == null) return;

            if (data.Version > CurrentSaveVersion)
            {
                Melon<HitmanModMain>.Logger.Warning(
                    $"Save file version {data.Version} is newer than mod version {CurrentSaveVersion}.");
            }

            CompletedCount = Math.Max(0, data.CompletedCount);
            TotalEarnings = Math.Max(0f, data.TotalEarnings);
            Level = Math.Clamp(data.Level, 1, MaxLevel);
            Experience = Math.Max(0, data.Experience);
            CooldownRemaining = Math.Clamp(data.CooldownRemaining, 0f, CooldownAfterFail);
            IsHitmanUnlocked = data.HitmanUnlocked || HitmanConfig.AppUnlocked;

            // Reconcile XP and Level after load (handles corrupted/edited saves)
            while (Level < MaxLevel && Experience >= LevelThresholds[Level])
                Level++;

            // Restore active contract if present (NPC refs resolved later in TryResolveActiveContract)
            if (data.ActiveContract != null
                && Enum.IsDefined(typeof(ContractType), data.ActiveContract.Type)
                && Enum.IsDefined(typeof(ContractDifficulty), data.ActiveContract.Difficulty))
            {
                ActiveContract = new Contract
                {
                    Id = data.ActiveContract.Id,
                    TargetName = data.ActiveContract.TargetName,
                    Type = (ContractType)data.ActiveContract.Type,
                    Difficulty = (ContractDifficulty)data.ActiveContract.Difficulty,
                    Reward = data.ActiveContract.Reward,
                    Status = ContractStatus.Active
                };
                Melon<HitmanModMain>.Logger.Msg($"Restored active contract: {ActiveContract.TypeLabel} {ActiveContract.TargetName}");
            }
            else if (data.ActiveContract != null)
            {
                Melon<HitmanModMain>.Logger.Warning("Discarded active contract with invalid type/difficulty from save.");
            }

            Melon<HitmanModMain>.Logger.Msg($"HitmanMod loaded: Level={Level}, Completed={CompletedCount}, Earned=${TotalEarnings:N0}, Cooldown={CooldownRemaining:F0}s");
        }
        catch (Exception ex)
        {
            Melon<HitmanModMain>.Logger.Error($"Failed to load HitmanMod: {ex.Message}");
        }
    }
}

public class ContractSaveData
{
    public string Id { get; set; } = "";
    public string TargetName { get; set; } = "";
    public int Type { get; set; }
    public int Difficulty { get; set; }
    public float Reward { get; set; }
}

public class HitmanSaveData
{
    public int Version { get; set; } = 1;
    public int CompletedCount { get; set; }
    public float TotalEarnings { get; set; }
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public float CooldownRemaining { get; set; }
    public ContractSaveData? ActiveContract { get; set; }
    public bool HitmanUnlocked { get; set; }
}
