using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

namespace Snog.Audio.Demo
{
    /// <summary>
    /// Builds fully styled track buttons in code — no prefab required.
    /// Matches the dark neon aesthetic of the Audio Manager demo UI.
    ///
    /// Each button has:
    ///   • Dark background panel with a subtle left-edge accent bar
    ///   • Music note icon  |  Track name  |  Play/Stop state indicator
    ///   • Smooth hover + active colour transitions via EventTrigger
    ///   • Purple glow border that brightens when the track is active
    /// </summary>
    public static class TrackButtonFactory
    {
        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color BgNormal      = new Color(0.055f, 0.063f, 0.141f, 0.95f); // deep navy
        private static readonly Color BgHover       = new Color(0.090f, 0.100f, 0.200f, 1.00f);
        private static readonly Color BgActive      = new Color(0.180f, 0.070f, 0.320f, 1.00f); // purple tint
        private static readonly Color AccentIdle     = new Color(0.380f, 0.150f, 0.700f, 1.00f); // muted purple
        private static readonly Color AccentPlaying  = new Color(0.780f, 0.380f, 1.000f, 1.00f); // bright violet
        private static readonly Color TextPrimary    = new Color(0.900f, 0.880f, 1.000f, 1.00f); // near-white
        private static readonly Color TextMuted      = new Color(0.500f, 0.470f, 0.620f, 1.00f); // soft grey-purple
        private static readonly Color DividerColor   = new Color(0.220f, 0.180f, 0.380f, 0.60f);

        internal static float ButtonHeight = 36f; // set by AudioManagerDemoUI before building
        private const float ACCENT_BAR_W   =  3f;
        private const float ICON_SIZE       = 24f;
        private const float ANIM_SPEED      = 8f;

        // ─────────────────────────────────────────────────────────────────────
        //  Public factory method
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a styled track button and parents it to <paramref name="container"/>.
        /// </summary>
        /// <param name="trackName">Raw track name from MusicLibrary.</param>
        /// <param name="displayName">Human-readable label shown on the button.</param>
        /// <param name="container">Parent RectTransform (Track List panel).</param>
        /// <param name="onClick">Callback invoked when the button is pressed.</param>
        /// <returns>The <see cref="TrackButtonController"/> attached to the new button.</returns>
        public static TrackButtonController Create(
            string trackName,
            string displayName,
            Transform container,
            Action<string> onClick)
        {
            // ── Root ──────────────────────────────────────────────────────────
            GameObject root = new GameObject($"TrackBtn_{trackName}");
            root.transform.SetParent(container, false);

            RectTransform rootRect = root.AddComponent<RectTransform>();
            // Explicitly set the height on the RectTransform — VerticalLayoutGroup
            // with childControlHeight=false reads sizeDelta.y directly, not LayoutElement.
            rootRect.sizeDelta = new Vector2(0f, ButtonHeight);

            LayoutElement le = root.AddComponent<LayoutElement>();
            le.flexibleWidth   = 1f;
            le.minHeight       = ButtonHeight;
            le.preferredHeight = ButtonHeight;

            // Background image (dark panel)
            Image bg = root.AddComponent<Image>();
            bg.color = BgNormal;
            bg.raycastTarget = true;

            // Button component
            Button btn = root.AddComponent<Button>();
            btn.targetGraphic = bg;

            // Disable built-in colour transitions — we drive them manually
            ColorBlock cb = btn.colors;
            cb.normalColor      = BgNormal;
            cb.highlightedColor = BgHover;
            cb.pressedColor     = BgActive;
            cb.selectedColor    = BgNormal;
            cb.fadeDuration     = 0.1f;
            btn.colors = cb;

            // ── Left accent bar ───────────────────────────────────────────────
            GameObject barGO = new GameObject("AccentBar");
            barGO.transform.SetParent(root.transform, false);

            RectTransform barRect = barGO.AddComponent<RectTransform>();
            barRect.anchorMin  = new Vector2(0f, 0f);
            barRect.anchorMax  = new Vector2(0f, 1f);
            barRect.offsetMin  = Vector2.zero;
            barRect.offsetMax  = new Vector2(ACCENT_BAR_W, 0f);

            Image barImg = barGO.AddComponent<Image>();
            barImg.color = AccentIdle;
            barImg.raycastTarget = false;

            // ── Bottom divider ────────────────────────────────────────────────
            GameObject divGO = new GameObject("Divider");
            divGO.transform.SetParent(root.transform, false);

            RectTransform divRect = divGO.AddComponent<RectTransform>();
            divRect.anchorMin = new Vector2(0f, 0f);
            divRect.anchorMax = new Vector2(1f, 0f);
            divRect.offsetMin = new Vector2(ACCENT_BAR_W + 8f, -1f);
            divRect.offsetMax = new Vector2(-8f, 0f);

            Image divImg = divGO.AddComponent<Image>();
            divImg.color = DividerColor;
            divImg.raycastTarget = false;

            // ── Icon (music note ♪) ───────────────────────────────────────────
            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(root.transform, false);

            RectTransform iconRect = iconGO.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot     = new Vector2(0f, 0.5f);
            iconRect.sizeDelta = new Vector2(ICON_SIZE + 16f, ICON_SIZE);
            iconRect.anchoredPosition = new Vector2(ACCENT_BAR_W + 10f, 0f);

            TMP_Text iconText = iconGO.AddComponent<TextMeshProUGUI>();
            iconText.text      = "♪";
            iconText.fontSize  = 16f;
            iconText.color     = AccentIdle;
            iconText.alignment = TextAlignmentOptions.Center;
            iconText.raycastTarget = false;

            // ── Track name label ──────────────────────────────────────────────
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(root.transform, false);

            RectTransform labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(ACCENT_BAR_W + 10f + ICON_SIZE + 16f, 0f);
            labelRect.offsetMax = new Vector2(-48f, 0f);

            TMP_Text label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text               = displayName;
            label.fontSize           = 13f;
            label.fontStyle          = FontStyles.Normal;
            label.color              = TextPrimary;
            label.alignment          = TextAlignmentOptions.MidlineLeft;
            label.overflowMode       = TextOverflowModes.Ellipsis;
            label.maxVisibleLines    = 1;
            label.raycastTarget      = false;

            // ── Status indicator (PLAY / STOP) ───────────────────────────────────
            GameObject statusGO = new GameObject("Status");
            statusGO.transform.SetParent(root.transform, false);

            RectTransform statusRect = statusGO.AddComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(1f, 0.5f);
            statusRect.anchorMax = new Vector2(1f, 0.5f);
            statusRect.pivot     = new Vector2(1f, 0.5f);
            statusRect.sizeDelta = new Vector2(40f, 32f);
            statusRect.anchoredPosition = new Vector2(-10f, 0f);

            TMP_Text statusText = statusGO.AddComponent<TextMeshProUGUI>();
            statusText.text      = "PLAY";
            statusText.fontSize  = 9f;
            statusText.color     = TextMuted;
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.raycastTarget = false;

            // ── Hover & press animations via EventTrigger ─────────────────────
            EventTrigger trigger = root.AddComponent<EventTrigger>();

            AddTrigger(trigger, EventTriggerType.PointerEnter, _ =>
            {
                if (bg != null) bg.color = BgHover;
            });
            AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
            {
                // Controller will restore the correct colour after the lambda runs
            });
            AddTrigger(trigger, EventTriggerType.PointerDown, _ =>
            {
                if (bg != null) bg.color = BgActive;
            });

            // ── Wire up click ─────────────────────────────────────────────────
            btn.onClick.AddListener(() => onClick?.Invoke(trackName));

            // ── Attach controller ─────────────────────────────────────────────
            TrackButtonController ctrl = root.AddComponent<TrackButtonController>();
            ctrl.Init(bg, barImg, iconText, statusText, label,
                      BgNormal, BgHover, BgActive,
                      AccentIdle, AccentPlaying,
                      TextPrimary, TextMuted);

            root.SetActive(true);
            return ctrl;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static void AddTrigger(EventTrigger et, EventTriggerType type,
                                       Action<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => action(data));
            et.triggers.Add(entry);
        }
    }


    /// <summary>
    /// Attached to every track button. Manages the playing / idle visual state
    /// and smooth colour interpolation each frame.
    /// </summary>
    public class TrackButtonController : MonoBehaviour
    {
        // Visual refs
        private Image    _bg;
        private Image    _accentBar;
        private TMP_Text _icon;
        private TMP_Text _status;
        private TMP_Text _label;

        // Colours
        private Color _bgNormal, _bgHover, _bgActive;
        private Color _accentIdle, _accentPlaying;
        private Color _textPrimary, _textMuted;

        // State
        private bool  _isPlaying;
        private bool  _isHovered;
        private Color _bgTarget;
        private const float SPEED = 10f;

        // ── Initialisation ────────────────────────────────────────────────────

        public void Init(Image bg, Image accentBar, TMP_Text icon,
                         TMP_Text status, TMP_Text label,
                         Color bgNormal, Color bgHover, Color bgActive,
                         Color accentIdle, Color accentPlaying,
                         Color textPrimary, Color textMuted)
        {
            _bg           = bg;
            _accentBar    = accentBar;
            _icon         = icon;
            _status       = status;
            _label        = label;
            _bgNormal     = bgNormal;
            _bgHover      = bgHover;
            _bgActive     = bgActive;
            _accentIdle   = accentIdle;
            _accentPlaying = accentPlaying;
            _textPrimary  = textPrimary;
            _textMuted    = textMuted;
            _bgTarget     = bgNormal;

            // Hover tracking
            var trigger = GetComponent<EventTrigger>() ?? gameObject.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { _isHovered = true;  UpdateBgTarget(); });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => { _isHovered = false; UpdateBgTarget(); });
            trigger.triggers.Add(exit);
        }

        // ── State ─────────────────────────────────────────────────────────────

        public void SetPlaying(bool playing)
        {
            _isPlaying = playing;
            UpdateBgTarget();

            if (_accentBar != null)
                _accentBar.color = playing ? _accentPlaying : _accentIdle;

            if (_icon != null)
                _icon.color = playing ? _accentPlaying : _accentIdle;

            if (_status != null)
            {
                _status.text  = playing ? "STOP" : "PLAY";
                _status.color = playing ? _accentPlaying : _textMuted;
            }

            if (_label != null)
                _label.color = playing ? Color.white : _textPrimary;
        }

        // ── Unity ─────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_bg == null) return;
            _bg.color = Color.Lerp(_bg.color, _bgTarget, SPEED * Time.unscaledDeltaTime);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void UpdateBgTarget()
        {
            if (_isPlaying)
                _bgTarget = _bgActive;
            else if (_isHovered)
                _bgTarget = _bgHover;
            else
                _bgTarget = _bgNormal;
        }
    }
}