using System;
using S1API.PhoneApp;
using S1API.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HitmanMod;

public class HitmanApp : PhoneApp
{
    public static HitmanApp? Instance { get; private set; }

    protected override string AppName => "HitmanMod";
    protected override string AppTitle => "Hitman";
    protected override string IconLabel => "Hitman";
    protected override string IconFileName => "Hitman_App_Icon.png";

    private static readonly Color BgDark = new(0.08f, 0.08f, 0.12f, 1f);
    private static readonly Color BgCard = new(0.14f, 0.15f, 0.20f, 1f);
    private static readonly Color BgHeader = new(0.06f, 0.06f, 0.10f, 1f);
    private static readonly Color AccentRed = new(0.91f, 0.27f, 0.38f, 1f);
    private static readonly Color BgLevelBar = new(0.12f, 0.12f, 0.18f, 1f);
    private static readonly Color BgXpBar = new(0.2f, 0.2f, 0.25f, 1f);
    private static readonly Color Divider = new(0.25f, 0.25f, 0.30f, 1f);
    private static readonly Color AbortRed = new(0.45f, 0.12f, 0.12f, 1f);
    private static readonly Color AcceptGreen = new(0.13f, 0.55f, 0.33f, 1f);

    private GameObject? _container;
    private Text? _cooldownText;
    private Text? _pendingTimerText;
    private Text? _errorText;
    private Text? _distanceText;
    private bool _uiDirty;
    private Action? _confirmAction;

    protected override void OnCreated()
    {
        base.OnCreated();
        Instance = this;
        var mgr = HitmanModMain.Instance?.ContractManager;
        if (mgr != null)
        {
            mgr.OnStateChanged -= OnExternalStateChanged;
            mgr.OnStateChanged += OnExternalStateChanged;
        }
    }

    protected override void OnDestroyed()
    {
        var mgr = HitmanModMain.Instance?.ContractManager;
        if (mgr != null)
            mgr.OnStateChanged -= OnExternalStateChanged;
        Instance = null;
        base.OnDestroyed();
    }

    private void OnExternalStateChanged()
    {
        if (_container != null)
            RefreshUI();
        else
            _uiDirty = true;
    }

    protected override void OnCreatedUI(GameObject container)
    {
        _container = container;
        _uiDirty = false;
        RefreshUI();
    }

    public void RefreshUI()
    {
        if (_container == null) return;
        ClearChildren(_container);
        _cooldownText = null;
        _pendingTimerText = null;
        _errorText = null;
        _distanceText = null;

        var mgr = HitmanModMain.Instance?.ContractManager;
        if (mgr == null)
        {
            ShowInitializing();
            return;
        }

        // Show locked screen if Hitman career not yet activated
        if (!ContractManager.IsHitmanUnlocked)
        {
            ShowLocked();
            return;
        }

        if (mgr.ActiveContract != null)
            ShowActiveContract(mgr);
        else if (mgr.PendingOffer != null)
            ShowPendingOffer(mgr);
        else
            ShowDashboard(mgr);
    }

    /// <summary>
    /// Lightweight timer updates without full UI rebuild.
    /// </summary>
    public void UpdateTimerDisplays()
    {
        var mgr = HitmanModMain.Instance?.ContractManager;
        if (mgr == null) return;

        // Refresh stale UI when app is reopened after a state change
        if (_uiDirty)
        {
            _uiDirty = false;
            RefreshUI();
            return;
        }

        if (_cooldownText != null && mgr.IsOnCooldown)
        {
            int mins = (int)(mgr.CooldownRemaining / 60);
            int secs = (int)(mgr.CooldownRemaining % 60);
            _cooldownText.text = $"<color=#E6A040>Next contract available in {mins}m {secs:D2}s</color>";
        }

        if (_pendingTimerText != null && mgr.PendingOffer != null)
        {
            float remaining = mgr.PendingOfferTimeRemaining;
            int mins = (int)(remaining / 60);
            int secs = (int)(remaining % 60);
            string color = remaining < 60f ? "E64050" : "E6A040";
            _pendingTimerText.text = $"<color=#{color}>Offer expires in {mins}m {secs:D2}s</color>";
        }

        // Live distance update
        if (mgr.ActiveContract != null)
        {
            string distText = mgr.GetFormattedDistance();
            if (!string.IsNullOrEmpty(distText))
            {
                if (_distanceText != null)
                {
                    string distColor = mgr.TargetDistance < 50f ? "00C878" : mgr.TargetDistance < 150f ? "E6C040" : "E6A040";
                    _distanceText.text = $"<color=#{distColor}>{distText}</color>";
                }
                else
                {
                    // Distance became available but UI element doesn't exist yet — rebuild
                    RefreshUI();
                }
            }
            else if (_distanceText != null)
            {
                _distanceText.text = "<color=#888888>Signal lost...</color>";
            }
        }
    }

    // ===================== INITIALIZING STATE =====================
    private void ShowInitializing()
    {
        var root = MakePanel("Root", _container!.transform, BgDark);
        StretchFull(root);
        PlaceText("Init", "<color=#888888>Initializing...</color>", root, 18, FontStyle.Italic, TextAnchor.MiddleCenter, 0, 0.4f, 1, 0.6f);
    }

    private void ShowLocked()
    {
        var root = MakePanel("Root", _container!.transform, BgDark);
        StretchFull(root);

        var header = MakePanel("Header", root.transform, new Color(0.15f, 0.05f, 0.05f));
        AnchorRect(header, 0, 0.88f, 1, 1);
        PlaceText("Title", "HITMAN", header, 22, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);

        PlaceText("Lock", "<color=#ff4444>LOCKED</color>", root, 24, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0.55f, 1, 0.7f);
        PlaceText("Hint", "<color=#888888>Find the right person in town\nto unlock this career path.</color>",
            root, 14, FontStyle.Italic, TextAnchor.MiddleCenter, 0.05f, 0.35f, 0.95f, 0.55f);
    }

    // ===================== DASHBOARD =====================
    private void ShowDashboard(ContractManager mgr)
    {
        var root = MakePanel("Root", _container!.transform, BgDark);
        StretchFull(root);

        // Header
        var header = MakePanel("Header", root.transform, BgHeader);
        AnchorRect(header, 0, 0.88f, 1, 1);
        PlaceText("Title", "HITMAN", header, 32, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);

        // Level bar
        var lvlBar = MakePanel("LvlBar", root.transform, BgLevelBar);
        AnchorRect(lvlBar, 0, 0.82f, 1, 0.88f);
        PlaceText("LvlTxt", $"LEVEL {mgr.Level}", lvlBar, 15, FontStyle.Bold, TextAnchor.MiddleLeft, 0.08f, 0, 0.4f, 1);

        int xpCurrent = mgr.Experience;
        int xpNeeded = mgr.ExperienceForNextLevel;
        string xpText = mgr.Level >= 10 ? "MAX" : $"{xpCurrent} / {xpNeeded} REP";
        PlaceText("XpTxt", xpText, lvlBar, 14, FontStyle.Normal, TextAnchor.MiddleRight, 0.5f, 0, 0.92f, 1);

        // XP progress bar
        var xpBg = MakePanel("XpBg", root.transform, BgXpBar);
        AnchorRect(xpBg, 0.08f, 0.80f, 0.92f, 0.815f);

        if (mgr.Level < 10 && xpNeeded > 0)
        {
            float progress = Mathf.Clamp01((float)xpCurrent / xpNeeded);
            var xpFill = MakePanel("XpFill", xpBg.transform, AccentRed);
            AnchorRect(xpFill, 0, 0, progress, 1);
        }

        // Stats card
        var statsCard = MakePanel("Stats", root.transform, BgCard);
        AnchorRect(statsCard, 0.05f, 0.55f, 0.95f, 0.78f);

        PlaceText("StatsTitle", "YOUR RECORD", statsCard, 16, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0.70f, 1, 0.95f);

        PlaceText("Completed", $"Contracts Completed", statsCard, 14, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.40f, 0.65f, 0.65f);
        PlaceText("CompVal", $"{mgr.CompletedCount}", statsCard, 22, FontStyle.Bold, TextAnchor.MiddleRight, 0.65f, 0.40f, 0.92f, 0.65f);

        PlaceText("Earned", $"Total Earned", statsCard, 14, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.10f, 0.65f, 0.35f);
        string earnColor = mgr.TotalEarnings > 0 ? "00C878" : "AAAAAA";
        PlaceText("EarnVal", $"<color=#{earnColor}>${mgr.TotalEarnings:N0}</color>", statsCard, 22, FontStyle.Bold, TextAnchor.MiddleRight, 0.65f, 0.10f, 0.92f, 0.35f);

        // Cooldown or Status + Request button
        if (mgr.IsOnCooldown)
        {
            int mins = (int)(mgr.CooldownRemaining / 60);
            int secs = (int)(mgr.CooldownRemaining % 60);
            _cooldownText = PlaceText("Cooldown", $"<color=#E6A040>Next contract available in {mins}m {secs:D2}s</color>", root, 15, FontStyle.Italic, TextAnchor.MiddleCenter, 0.05f, 0.42f, 0.95f, 0.52f);
        }
        else
        {
            // Error feedback from last failed request
            if (!string.IsNullOrEmpty(mgr.LastRequestError))
            {
                _errorText = PlaceText("Error", $"<color=#E64050>{mgr.LastRequestError}</color>", root, 13, FontStyle.Italic, TextAnchor.MiddleCenter, 0.05f, 0.44f, 0.95f, 0.52f);
            }
            else
            {
                PlaceText("Status", "<color=#AAAAAA>Waiting for contract...</color>", root, 16, FontStyle.Italic, TextAnchor.MiddleCenter, 0.05f, 0.44f, 0.95f, 0.52f);
            }

            // Request button
            var btnPanel = MakePanel("ReqBtn", root.transform, AccentRed);
            AnchorRect(btnPanel, 0.15f, 0.30f, 0.85f, 0.40f);
            PlaceText("ReqTxt", "REQUEST CONTRACT", btnPanel, 18, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);

            var btn = btnPanel.AddComponent<Button>();
            ConfigureButtonFeedback(btn, AccentRed);
            btn.onClick.AddListener((UnityAction)(() =>
            {
                mgr.RequestContract();
                RefreshUI();
            }));
        }

        // Tip
        PlaceText("Tip", "Contracts are also sent automatically via SMS", root, 12, FontStyle.Italic, TextAnchor.MiddleCenter, 0, 0.22f, 1, 0.28f);

        // Reset Progress button
        var resetPanel = MakePanel("ResetBtn", root.transform, new Color(0.3f, 0.15f, 0.15f));
        AnchorRect(resetPanel, 0.05f, 0.04f, 0.48f, 0.12f);
        PlaceText("ResetTxt", "RESET PROGRESS", resetPanel, 11, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);
        resetPanel.AddComponent<Button>().onClick.AddListener((UnityAction)(() =>
        {
            _confirmAction = () =>
            {
                ContractManager.IsHitmanUnlocked = false;
                mgr.ForceResetAll();
                mgr.Save();
                RefreshUI();
            };
            ShowConfirm("Reset all progress? Level, earnings and unlock status will be lost.");
        }));

        // Reset Config button
        var configPanel = MakePanel("ConfigBtn", root.transform, new Color(0.15f, 0.15f, 0.3f));
        AnchorRect(configPanel, 0.52f, 0.04f, 0.95f, 0.12f);
        PlaceText("ConfigTxt", "RESET CONFIG", configPanel, 11, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);
        configPanel.AddComponent<Button>().onClick.AddListener((UnityAction)(() =>
        {
            _confirmAction = () =>
            {
                HitmanConfig.ResetToDefaults();
                RefreshUI();
            };
            ShowConfirm("Reset config to default values? Your custom settings will be overwritten.");
        }));
    }

    // ===================== PENDING OFFER =====================
    private void ShowPendingOffer(ContractManager mgr)
    {
        var offer = mgr.PendingOffer;
        if (offer == null) return;

        var root = MakePanel("Root", _container!.transform, BgDark);
        StretchFull(root);

        // Header
        var header = MakePanel("Header", root.transform, BgHeader);
        AnchorRect(header, 0, 0.90f, 1, 1);
        PlaceText("Title", "NEW CONTRACT OFFER", header, 24, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);

        // Expiry timer
        float remaining = mgr.PendingOfferTimeRemaining;
        int mins = (int)(remaining / 60);
        int secs = (int)(remaining % 60);
        string timerColor = remaining < 60f ? "E64050" : "E6A040";
        _pendingTimerText = PlaceText("Timer", $"<color=#{timerColor}>Offer expires in {mins}m {secs:D2}s</color>", root, 13, FontStyle.Italic, TextAnchor.MiddleCenter, 0, 0.86f, 1, 0.90f);

        // Offer card
        var card = MakePanel("Card", root.transform, BgCard);
        AnchorRect(card, 0.05f, 0.38f, 0.95f, 0.85f);

        // Target
        PlaceText("TgtLabel", "TARGET", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.84f, 0.5f, 0.97f);
        PlaceText("TgtName", offer.TargetName, card, 22, FontStyle.Bold, TextAnchor.MiddleLeft, 0.08f, 0.72f, 0.92f, 0.85f);

        var div1 = MakePanel("Div1", card.transform, Divider);
        AnchorRect(div1, 0.08f, 0.71f, 0.92f, 0.715f);

        // Objective
        PlaceText("ObjLabel", "OBJECTIVE", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.56f, 0.5f, 0.70f);
        string objColor = offer.Type == ContractType.Kill ? "E64050" : "E6A040";
        PlaceText("ObjVal", $"<color=#{objColor}>{offer.TypeLabel}</color>", card, 18, FontStyle.Bold, TextAnchor.MiddleRight, 0.5f, 0.56f, 0.92f, 0.70f);

        // Difficulty
        PlaceText("DifLabel", "DIFFICULTY", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.41f, 0.5f, 0.55f);
        string difColor = GetDifficultyColor(offer.Difficulty);
        PlaceText("DifVal", $"<color=#{difColor}>{offer.DifficultyLabel}</color>", card, 18, FontStyle.Bold, TextAnchor.MiddleRight, 0.5f, 0.41f, 0.92f, 0.55f);

        var div2 = MakePanel("Div2", card.transform, Divider);
        AnchorRect(div2, 0.08f, 0.40f, 0.92f, 0.405f);

        // Reward
        PlaceText("RewLabel", "REWARD", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.22f, 0.5f, 0.39f);
        PlaceText("RewVal", $"<color=#00C878>${offer.Reward:N0}</color>", card, 24, FontStyle.Bold, TextAnchor.MiddleRight, 0.5f, 0.22f, 0.92f, 0.39f);

        // Warnings
        if (offer.Difficulty >= ContractDifficulty.Hard)
        {
            PlaceText("Warn", "<color=#E64050>WARNING: Target may be armed!</color>", card, 13, FontStyle.Bold, TextAnchor.MiddleCenter, 0.05f, 0.08f, 0.95f, 0.20f);
        }
        if (offer.Type == ContractType.Knockout)
        {
            float warnY = offer.Difficulty >= ContractDifficulty.Hard ? 0.01f : 0.08f;
            PlaceText("KOWarn", "<color=#E6A040>Target must survive - DO NOT KILL!</color>", card, 12, FontStyle.Bold, TextAnchor.MiddleCenter, 0.05f, warnY, 0.95f, warnY + 0.10f);
        }

        // Accept button
        var acceptPanel = MakePanel("AcceptBtn", root.transform, AcceptGreen);
        AnchorRect(acceptPanel, 0.08f, 0.24f, 0.48f, 0.35f);
        PlaceText("AcceptTxt", "ACCEPT", acceptPanel, 18, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);
        var acceptBtn = acceptPanel.AddComponent<Button>();
        ConfigureButtonFeedback(acceptBtn, AcceptGreen);
        acceptBtn.onClick.AddListener((UnityAction)(() => { mgr.AcceptPendingOffer(); }));

        // Decline button
        var declinePanel = MakePanel("DeclineBtn", root.transform, AbortRed);
        AnchorRect(declinePanel, 0.52f, 0.24f, 0.92f, 0.35f);
        PlaceText("DeclineTxt", "DECLINE", declinePanel, 18, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);
        var declineBtn = declinePanel.AddComponent<Button>();
        ConfigureButtonFeedback(declineBtn, AbortRed);
        declineBtn.onClick.AddListener((UnityAction)(() => { mgr.DeclinePendingOffer(); }));
    }

    // ===================== ACTIVE CONTRACT =====================
    private void ShowActiveContract(ContractManager mgr)
    {
        var contract = mgr.ActiveContract;
        if (contract == null) return;

        var root = MakePanel("Root", _container!.transform, BgDark);
        StretchFull(root);

        // Header
        var header = MakePanel("Header", root.transform, BgHeader);
        AnchorRect(header, 0, 0.90f, 1, 1);
        PlaceText("Title", "ACTIVE CONTRACT", header, 26, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);

        // Contract card
        var card = MakePanel("Card", root.transform, BgCard);
        AnchorRect(card, 0.05f, 0.42f, 0.95f, 0.89f);

        // Target
        PlaceText("TgtLabel", "TARGET", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.85f, 0.5f, 0.97f);
        PlaceText("TgtName", contract.TargetName, card, 22, FontStyle.Bold, TextAnchor.MiddleLeft, 0.08f, 0.73f, 0.92f, 0.86f);

        var div1 = MakePanel("Div1", card.transform, Divider);
        AnchorRect(div1, 0.08f, 0.72f, 0.92f, 0.725f);

        // Objective
        PlaceText("ObjLabel", "OBJECTIVE", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.60f, 0.5f, 0.71f);
        string objColor = contract.Type == ContractType.Kill ? "E64050" : "E6A040";
        PlaceText("ObjVal", $"<color=#{objColor}>{contract.TypeLabel}</color>", card, 18, FontStyle.Bold, TextAnchor.MiddleRight, 0.5f, 0.60f, 0.92f, 0.71f);

        // Difficulty
        PlaceText("DifLabel", "DIFFICULTY", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.48f, 0.5f, 0.59f);
        string difColor = GetDifficultyColor(contract.Difficulty);
        PlaceText("DifVal", $"<color=#{difColor}>{contract.DifficultyLabel}</color>", card, 18, FontStyle.Bold, TextAnchor.MiddleRight, 0.5f, 0.48f, 0.92f, 0.59f);

        var div2 = MakePanel("Div2", card.transform, Divider);
        AnchorRect(div2, 0.08f, 0.47f, 0.92f, 0.475f);

        // Reward
        PlaceText("RewLabel", "REWARD", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.35f, 0.5f, 0.46f);
        PlaceText("RewVal", $"<color=#00C878>${contract.Reward:N0}</color>", card, 24, FontStyle.Bold, TextAnchor.MiddleRight, 0.5f, 0.34f, 0.92f, 0.46f);

        var div3 = MakePanel("Div3", card.transform, Divider);
        AnchorRect(div3, 0.08f, 0.33f, 0.92f, 0.335f);

        if (mgr.TrackingEnabled)
        {
            // Live distance tracker (paid)
            PlaceText("SigLabel", "PHONE SIGNAL", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.24f, 0.92f, 0.32f);
            string distText = mgr.GetFormattedDistance();
            string locText = string.IsNullOrEmpty(mgr.LastKnownLocation) ? "Locating..." : mgr.LastKnownLocation;
            if (!string.IsNullOrEmpty(distText))
            {
                string distColor = mgr.TargetDistance < 50f ? "00C878" : mgr.TargetDistance < 150f ? "E6C040" : "E6A040";
                _distanceText = PlaceText("DistVal", $"<color=#{distColor}>{distText}</color>", card, 22, FontStyle.Bold, TextAnchor.MiddleRight, 0.5f, 0.14f, 0.92f, 0.24f);
                PlaceText("LocVal", $"<color=#888888>{locText}</color>", card, 14, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.14f, 0.5f, 0.24f);
            }
            else
            {
                PlaceText("LocVal", $"<color=#E6A040>{locText}</color>", card, 16, FontStyle.Bold, TextAnchor.MiddleLeft, 0.08f, 0.14f, 0.92f, 0.24f);
            }
        }
        else
        {
            // Default: region + description (no tracking)
            string locText = string.IsNullOrEmpty(mgr.LastKnownLocation) ? "Unknown" : mgr.LastKnownLocation;
            PlaceText("LocLabel", "LAST SEEN", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.24f, 0.92f, 0.32f);
            PlaceText("LocVal", $"<color=#E6A040>Usually seen around {locText}</color>", card, 14, FontStyle.Bold, TextAnchor.MiddleLeft, 0.08f, 0.16f, 0.92f, 0.24f);
        }

        // Target description (always shown)
        if (!string.IsNullOrEmpty(contract.TargetDescription))
        {
            PlaceText("DescLabel", "DESCRIPTION", card, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0.08f, 0.08f, 0.92f, 0.16f);
            PlaceText("DescVal", $"<color=#CCCCCC><i>{contract.TargetDescription}</i></color>", card, 13, FontStyle.Normal, TextAnchor.UpperLeft, 0.08f, 0.01f, 0.92f, 0.08f);
        }

        // Warnings
        if (contract.Type == ContractType.Knockout)
        {
            PlaceText("KOWarn", "<color=#E6A040>DO NOT KILL the target!</color>", card, 13, FontStyle.Bold, TextAnchor.MiddleCenter, 0.05f, 0.02f, 0.95f, 0.13f);
        }
        else if (contract.Difficulty >= ContractDifficulty.Hard)
        {
            PlaceText("Warn", "<color=#E64050>WARNING: Target is armed!</color>", card, 13, FontStyle.Bold, TextAnchor.MiddleCenter, 0.05f, 0.02f, 0.95f, 0.13f);
        }

        // Hack location button (only if tracking not yet active)
        if (!mgr.TrackingEnabled)
        {
            float hackCost = HitmanConfig.GetHackCost(contract.Difficulty);
            var hackPanel = MakePanel("HackBtn", root.transform, new Color(0.15f, 0.25f, 0.45f));
            AnchorRect(hackPanel, 0.1f, 0.30f, 0.9f, 0.40f);
            PlaceText("HackTxt", $"TRACE SIGNAL (${hackCost:N0})", hackPanel, 14, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);
            var hackBtn = hackPanel.AddComponent<Button>();
            hackBtn.onClick.AddListener((UnityAction)(() =>
            {
                string error = mgr.HackTargetLocation();
                if (!string.IsNullOrEmpty(error))
                    MelonLoader.MelonLogger.Warning($"[THM] Hack failed: {error}");
                RefreshUI();
            }));
        }

        // Abort button
        var abortPanel = MakePanel("AbortBtn", root.transform, AbortRed);
        AnchorRect(abortPanel, 0.1f, 0.20f, 0.9f, 0.28f);
        PlaceText("AbortTxt", "ABORT CONTRACT", abortPanel, 16, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);

        var abortBtn = abortPanel.AddComponent<Button>();
        ConfigureButtonFeedback(abortBtn, AbortRed);
        abortBtn.onClick.AddListener((UnityAction)(() =>
        {
            mgr.AbortContract();
        }));
    }

    // ===================== CONFIRM POPUP =====================

    private void ShowConfirm(string message)
    {
        if (_container == null) return;
        ClearChildren(_container);

        var root = MakePanel("Root", _container.transform, BgDark);
        StretchFull(root);

        // Darken background
        var overlay = MakePanel("Overlay", root.transform, new Color(0f, 0f, 0f, 0.7f));
        StretchFull(overlay);

        // Dialog box
        var dialog = MakePanel("Dialog", root.transform, BgCard);
        AnchorRect(dialog, 0.08f, 0.30f, 0.92f, 0.70f);

        PlaceText("Title", "ARE YOU SURE?", dialog, 20, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0.70f, 1, 0.95f);
        PlaceText("Msg", message, dialog, 14, FontStyle.Normal, TextAnchor.MiddleCenter, 0.08f, 0.35f, 0.92f, 0.70f);

        // YES button
        var yesPanel = MakePanel("Yes", dialog.transform, AcceptGreen);
        AnchorRect(yesPanel, 0.08f, 0.08f, 0.46f, 0.28f);
        PlaceText("YesTxt", "YES", yesPanel, 16, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);
        yesPanel.AddComponent<Button>().onClick.AddListener((UnityAction)(() =>
        {
            _confirmAction?.Invoke();
            _confirmAction = null;
        }));

        // NO button
        var noPanel = MakePanel("No", dialog.transform, AbortRed);
        AnchorRect(noPanel, 0.54f, 0.08f, 0.92f, 0.28f);
        PlaceText("NoTxt", "NO", noPanel, 16, FontStyle.Bold, TextAnchor.MiddleCenter, 0, 0, 1, 1);
        noPanel.AddComponent<Button>().onClick.AddListener((UnityAction)(() =>
        {
            _confirmAction = null;
            RefreshUI();
        }));
    }

    // ===================== HELPERS =====================

    private static string GetDifficultyColor(ContractDifficulty difficulty)
    {
        return difficulty switch
        {
            ContractDifficulty.VeryEasy => "40C878", // Lighter green to distinguish from Easy
            ContractDifficulty.Easy => "00C878",
            ContractDifficulty.Medium => "E6C040",
            ContractDifficulty.Hard => "E68040",
            ContractDifficulty.VeryHard => "E64050",
            _ => "CCCCCC"
        };
    }

    private static void ConfigureButtonFeedback(Button btn, Color normalColor)
    {
        var img = btn.gameObject.GetComponent<Image>();
        if (img != null)
        {
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = new Color(
                Mathf.Min(normalColor.r * 1.15f, 1f),
                Mathf.Min(normalColor.g * 1.15f, 1f),
                Mathf.Min(normalColor.b * 1.15f, 1f), 1f);
            colors.pressedColor = new Color(
                normalColor.r * 0.7f,
                normalColor.g * 0.7f,
                normalColor.b * 0.7f, 1f);
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            btn.colors = colors;
        }
    }

    private Text PlaceText(string name, string content, GameObject parent, int fontSize, FontStyle style,
        TextAnchor anchor, float xMin, float yMin, float xMax, float yMax, float padLeft = 0, float padRight = 0)
    {
        var txt = UIFactory.Text(name, content, parent.transform, fontSize, anchor, style);
        var rt = txt.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = new Vector2(padLeft, 0);
            rt.offsetMax = new Vector2(-padRight, 0);
        }
        return txt;
    }

    private GameObject MakePanel(string name, Transform parent, Color color)
    {
        return UIFactory.Panel(name, parent, color);
    }

    private void AnchorRect(GameObject go, float xMin, float yMin, float xMax, float yMax)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void StretchFull(GameObject go)
    {
        if (go == null) return;
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void ClearChildren(GameObject go)
    {
        for (int i = go.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject);
    }
}
