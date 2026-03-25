using System;
using MelonLoader;
using S1API.UI;
using UnityEngine;
using UnityEngine.UI;

namespace HitmanMod;

/// <summary>
/// Screen-space mini-game overlay for the strangulation mechanic.
/// The player must stop a bouncing bar inside the green zone 3 times in a row.
/// Speed increases each round. Green zone size and position are randomised.
/// Missing any round = immediate fail.
/// </summary>
public static class StrangleMiniGame
{
    private const float BarHalfWidth = 0.015f;  // 3% total width
    private const int   RoundsToWin  = 3;
    private const float GreenSizeMin = 0.08f;  // 8% of track
    private const float GreenSizeMax = 0.25f;  // 25% of track

    // Speed ramps up each round
    private static readonly float[] RoundSpeeds = { 0.65f, 0.85f, 1.15f };

    public static bool IsActive { get; private set; }

    private static Action<bool>? _onComplete;
    private static GameObject?   _root;
    private static RectTransform? _indicator;
    private static RectTransform? _greenRect;
    private static Text?         _titleText;
    private static float _pos;
    private static float _dir = 1f;
    private static int   _round;        // 0-based: 0, 1, 2
    private static float _greenMin;     // current round green zone left edge
    private static float _greenMax;     // current round green zone right edge

    // ── Public API ────────────────────────────────────────────────────────────

    public static void Show(Action<bool> onComplete)
    {
        if (IsActive) return;

        IsActive    = true;
        _onComplete = onComplete;
        _round      = 0;

        BuildUI();
        RandomiseGreenZone();
        ResetBar();
        UpdateTitle();
    }

    public static void Update(float dt)
    {
        if (!IsActive || _indicator == null) return;

        float speed = _round < RoundSpeeds.Length ? RoundSpeeds[_round] : 1.2f;

        // Slide bar
        _pos += _dir * speed * dt;
        if (_pos >= 1f) { _pos = 1f; _dir = -1f; }
        if (_pos <= 0f) { _pos = 0f; _dir = 1f; }

        _indicator.anchorMin = new Vector2(Mathf.Max(0, _pos - BarHalfWidth), 0f);
        _indicator.anchorMax = new Vector2(Mathf.Min(1, _pos + BarHalfWidth), 1f);
        _indicator.offsetMin = Vector2.zero;
        _indicator.offsetMax = Vector2.zero;

        // Wait for E / Interact
        try
        {
            if (Il2CppScheduleOne.GameInput.GetButtonDown(Il2CppScheduleOne.GameInput.ButtonCode.Interact))
            {
                bool inGreen = _pos >= _greenMin && _pos <= _greenMax;

                if (!inGreen)
                {
                    Melon<HitmanModMain>.Logger.Msg($"[THM] StrangleMiniGame: missed round {_round + 1}/{RoundsToWin}.");
                    var cb = _onComplete;
                    Hide();
                    cb?.Invoke(false);
                    return;
                }

                _round++;
                Melon<HitmanModMain>.Logger.Msg($"[THM] StrangleMiniGame: hit round {_round}/{RoundsToWin}.");

                if (_round >= RoundsToWin)
                {
                    var cb = _onComplete;
                    Hide();
                    cb?.Invoke(true);
                    return;
                }

                // Next round — new random zone + reset bar
                RandomiseGreenZone();
                ResetBar();
                UpdateTitle();
            }
        }
        catch { }
    }

    public static void ForceClose()
    {
        Hide();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void ResetBar()
    {
        _pos = 0f;
        _dir = 1f;
    }

    private static void RandomiseGreenZone()
    {
        // Random size between 8% and 25% of the track
        float size = UnityEngine.Random.Range(GreenSizeMin, GreenSizeMax);

        // Random position — keep fully inside [0, 1]
        float maxStart = 1f - size;
        _greenMin = UnityEngine.Random.Range(0f, maxStart);
        _greenMax = _greenMin + size;

        // Update the visual rect
        if (_greenRect != null)
        {
            _greenRect.anchorMin = new Vector2(_greenMin, 0f);
            _greenRect.anchorMax = new Vector2(_greenMax, 1f);
            _greenRect.offsetMin = Vector2.zero;
            _greenRect.offsetMax = Vector2.zero;
        }
    }

    private static void UpdateTitle()
    {
        if (_titleText != null)
            _titleText.text = $"STRANGULATE  {_round + 1} / {RoundsToWin}";
    }

    private static void BuildUI()
    {
        _root = new GameObject("THM_StrangleMiniGame");
        UnityEngine.Object.DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _root.AddComponent<GraphicRaycaster>();

        // Semi-transparent background panel
        var bg = UIFactory.Panel("bg", _root.transform, new Color(0f, 0f, 0f, 0.80f));
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.28f, 0.40f);
        bgRect.anchorMax = new Vector2(0.72f, 0.60f);
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Title (shows round number)
        _titleText = UIFactory.Text("title", $"STRANGULATE  1 / {RoundsToWin}", bg.transform, 22, TextAnchor.MiddleCenter, FontStyle.Bold);
        _titleText.color = new Color(0.92f, 0.25f, 0.25f);
        var titleRect = _titleText.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 0.60f);
        titleRect.anchorMax = new Vector2(1f, 1.00f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        // Track background
        var track = UIFactory.Panel("track", bg.transform, new Color(0.18f, 0.18f, 0.18f));
        var trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0.05f, 0.38f);
        trackRect.anchorMax = new Vector2(0.95f, 0.60f);
        trackRect.offsetMin = Vector2.zero;
        trackRect.offsetMax = Vector2.zero;

        // Green zone (position set by RandomiseGreenZone)
        var green = UIFactory.Panel("green", track.transform, new Color(0.10f, 0.80f, 0.20f));
        _greenRect = green.GetComponent<RectTransform>();
        _greenRect.anchorMin = new Vector2(0f, 0f);
        _greenRect.anchorMax = new Vector2(0.25f, 1f);
        _greenRect.offsetMin = Vector2.zero;
        _greenRect.offsetMax = Vector2.zero;

        // Sliding indicator (white bar)
        var indicator = UIFactory.Panel("indicator", track.transform, Color.white);
        _indicator = indicator.GetComponent<RectTransform>();
        _indicator.anchorMin = new Vector2(0f, 0f);
        _indicator.anchorMax = new Vector2(BarHalfWidth * 2f, 1f);
        _indicator.offsetMin = Vector2.zero;
        _indicator.offsetMax = Vector2.zero;

        // Prompt
        var prompt = UIFactory.Text("prompt", "Press [E] to strike!", bg.transform, 14, TextAnchor.MiddleCenter);
        prompt.color = new Color(0.85f, 0.85f, 0.85f);
        var promptRect = prompt.GetComponent<RectTransform>();
        promptRect.anchorMin = new Vector2(0f, 0f);
        promptRect.anchorMax = new Vector2(1f, 0.35f);
        promptRect.offsetMin = Vector2.zero;
        promptRect.offsetMax = Vector2.zero;
    }

    private static void Hide()
    {
        IsActive    = false;
        _onComplete = null;
        _indicator  = null;
        _greenRect  = null;
        _titleText  = null;

        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }
}
