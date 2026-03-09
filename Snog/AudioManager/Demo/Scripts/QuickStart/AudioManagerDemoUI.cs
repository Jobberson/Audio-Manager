using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Snog.Audio;
using Snog.Audio.Utils;

namespace Snog.Audio.Demo
{
    /// <summary>
    /// UI controller for Snog's Audio Manager demo panel.
    ///
    /// Wires up:
    ///   • Audio Controls  – four volume sliders (Master / Music / SFX / Ambient)
    ///   • Track List      – buttons auto-built by TrackButtonFactory (no prefab needed)
    ///   • Log panel       – scrolling TMP_Text area at the bottom of the screen
    ///
    /// Inspector setup
    /// ────────────────
    /// 1. Assign the four Slider references under "Volume Sliders".
    /// 2. Assign trackListContainer — the RectTransform inside the Track List panel
    ///    (add a Vertical Layout Group + Content Size Fitter to it).
    /// 3. Assign logText — the TMP_Text inside the dark box at the bottom.
    /// 4. Optionally assign spectrumAnalyzer to keep the visualizer in sync.
    /// </summary>
    public class AudioManagerDemoUI : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Volume Sliders")]
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Slider ambientSlider;

        [Header("Track List")]
        [Tooltip("RectTransform inside the Track List panel. Add a Vertical Layout Group to it.")]
        [SerializeField] private Transform trackListContainer;

        [Tooltip("Height of each track button in pixels.")]
        [SerializeField] private float trackButtonHeight = 36f;

        [Tooltip("Fade-in duration when a track button is pressed (seconds).")]
        [SerializeField] private float trackFadeIn  = 1.5f;

        [Tooltip("Fade-out duration when a track is stopped (seconds).")]
        [SerializeField] private float trackFadeOut = 1.5f;

        [Header("Log Panel")]
        [Tooltip("TMP_Text component inside the log box at the bottom of the screen.")]
        [SerializeField] private TMP_Text logText;

        // Always exactly 7 lines — oldest is dropped when a new one arrives.
        private const int MAX_LOG_LINES = 7;

        [Header("Visualizer (optional)")]
        [Tooltip("Assign the SpectrumAnalyzer in the scene to keep it in sync with track buttons.")]
        [SerializeField] private SpectrumAnalyzer spectrumAnalyzer;

        [Header("Ambient (optional)")]
        [SerializeField] private AmbientProfile baseAmbientProfile;
        [SerializeField] private float ambientFadeDuration = 2f;

        // ─────────────────────────────────────────────────────────────────────
        //  Private state
        // ─────────────────────────────────────────────────────────────────────

        private readonly Queue<string>                               _logLines     = new Queue<string>();
        private readonly List<(TrackButtonController ctrl, string trackName)> _trackButtons = new List<(TrackButtonController, string)>();
        private string _activeTrackName;

        // ─────────────────────────────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Start()
        {
            InitSliders();

            if (baseAmbientProfile != null)
                AudioManager.Instance.SetAmbientProfile(baseAmbientProfile, ambientFadeDuration);

            Log("Audio Manager ready.");

            // Defer one frame so AudioManager.Awake() finishes building
            // its MusicLibrary dictionary before we query track names.
            StartCoroutine(BuildTrackListNextFrame());
        }

        private IEnumerator BuildTrackListNextFrame()
        {
            // Wait up to 2 seconds for AudioManager to finish building its library.
            // One frame is sometimes not enough if the AudioManager is on a
            // different GameObject with a later script execution order.
            float timeout = 2f;
            while (timeout > 0f)
            {
                yield return null;
                timeout -= Time.unscaledDeltaTime;

                if (AudioManager.Instance != null &&
                    AudioManager.Instance.TryGetMusicNames(out string[] check) &&
                    check != null && check.Length > 0)
                    break;
            }
            BuildTrackList();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Sliders
        // ─────────────────────────────────────────────────────────────────────

        private void InitSliders()
        {
            SetupSlider(masterSlider,  1.0f, v => OnVolumeChanged(AudioManager.AudioChannel.Master,  v, "Master"));
            SetupSlider(musicSlider,   0.8f, v => OnVolumeChanged(AudioManager.AudioChannel.Music,   v, "Music"));
            SetupSlider(sfxSlider,     1.0f, v => OnVolumeChanged(AudioManager.AudioChannel.FX,      v, "SFX"));
            SetupSlider(ambientSlider, 0.9f, v => OnVolumeChanged(AudioManager.AudioChannel.Ambient, v, "Ambient"));
        }

        private void SetupSlider(Slider slider, float defaultValue, UnityEngine.Events.UnityAction<float> callback)
        {
            if (slider == null) return;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value    = defaultValue;
            callback(defaultValue);
            slider.onValueChanged.AddListener(callback);
        }

        private void OnVolumeChanged(AudioManager.AudioChannel channel, float value, string label)
        {
            AudioManager.Instance.SetVolume(value, channel);
            Log($"{label} volume → {value:P0}");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Track list
        // ─────────────────────────────────────────────────────────────────────

        private void BuildTrackList()
        {
            if (trackListContainer == null)
            {
                Log("[UI] trackListContainer not assigned.");
                return;
            }

            // Remove any existing buttons
            foreach (Transform child in trackListContainer)
                Destroy(child.gameObject);
            _trackButtons.Clear();

            // Ensure the container has a VerticalLayoutGroup so buttons stack correctly
            VerticalLayoutGroup vlg = trackListContainer.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = trackListContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing            = 4f;
            vlg.padding            = new RectOffset(0, 0, 0, 0);
            vlg.childAlignment     = TextAnchor.LowerCenter;
            vlg.childControlWidth  = true;
            vlg.childControlHeight = false;  // height comes from LayoutElement.preferredHeight
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false; // never stretch buttons vertically

            // No ContentSizeFitter — the panel height is fixed in the Inspector.
            // Remove one if it was accidentally added by a previous run.
            ContentSizeFitter existingCsf = trackListContainer.GetComponent<ContentSizeFitter>();
            if (existingCsf != null) Destroy(existingCsf);

            if (!AudioManager.Instance.TryGetMusicNames(out string[] names) || names == null || names.Length == 0)
            {
                Log("[UI] No music tracks found in MusicLibrary.");
                return;
            }

            TrackButtonFactory.ButtonHeight = trackButtonHeight;
            Debug.Log($"[AudioManagerUI] Building {names.Length} track button(s): {string.Join(", ", names)}");

            foreach (string trackName in names)
            {
                string display = ToTitleCase(trackName);
                Debug.Log($"[AudioManagerUI] Creating button for: '{trackName}'");

                TrackButtonController ctrl = TrackButtonFactory.Create(
                    trackName,
                    display,
                    trackListContainer,
                    OnTrackNamePressed);

                _trackButtons.Add((ctrl, trackName));
            }

            // Recalculate layout — rebuild the container and its parent
            // so ScrollRect / outer panels also resize to fit all buttons.
            if (trackListContainer is RectTransform rt)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                if (rt.parent is RectTransform parentRT)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRT);
            }

            Log($"Track list built — {names.Length} track(s) found.");
        }

        private void OnTrackNamePressed(string trackName)
        {
            TrackButtonController pressedCtrl = null;
            foreach (var (ctrl, name) in _trackButtons)
                if (name == trackName) { pressedCtrl = ctrl; break; }

            OnTrackButtonPressed(trackName, pressedCtrl);
        }

        private void OnTrackButtonPressed(string trackName, TrackButtonController pressedCtrl)
        {
            if (_activeTrackName == trackName)
            {
                // ── Stop ──────────────────────────────────────────────────────
                if (spectrumAnalyzer != null)
                    spectrumAnalyzer.StopTrack(trackFadeOut);
                else
                    AudioManager.Instance.StopMusic(trackFadeOut);

                pressedCtrl?.SetPlaying(false);
                _activeTrackName = null;
                Log($"Stopped \"{ToTitleCase(trackName)}\".");
            }
            else
            {
                // ── Deactivate all buttons ────────────────────────────────────
                foreach (var (ctrl, _) in _trackButtons)
                    ctrl?.SetPlaying(false);

                // ── Play ──────────────────────────────────────────────────────
                if (spectrumAnalyzer != null)
                    spectrumAnalyzer.PlayTrack(trackName, trackFadeIn);
                else
                    AudioManager.Instance.PlayMusic(trackName, 0f, trackFadeIn);

                pressedCtrl?.SetPlaying(true);
                _activeTrackName = trackName;
                Log($"Playing \"{ToTitleCase(trackName)}\" (fade-in: {trackFadeIn}s).");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Log panel
        // ─────────────────────────────────────────────────────────────────────

        public void Log(string message)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            _logLines.Enqueue($"<color=#7755aa>[{ts}]</color>  {message}");
            while (_logLines.Count > MAX_LOG_LINES) _logLines.Dequeue();

            if (logText != null)
                logText.text = string.Join("\n", _logLines);

            Debug.Log($"[AudioManagerUI] {message}");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Sets a slider value programmatically (e.g. from a preset button).</summary>
        public void SetSliderValue(AudioManager.AudioChannel channel, float value01)
        {
            Slider target = channel switch
            {
                AudioManager.AudioChannel.Master  => masterSlider,
                AudioManager.AudioChannel.Music   => musicSlider,
                AudioManager.AudioChannel.FX      => sfxSlider,
                AudioManager.AudioChannel.Ambient => ambientSlider,
                _                                 => null
            };
            if (target != null) target.value = value01;
        }

        /// <summary>Rebuilds the track list — call this if tracks are added at runtime.</summary>
        public void RefreshTrackList() => BuildTrackList();

        // ─────────────────────────────────────────────────────────────────────
        //  Utilities
        // ─────────────────────────────────────────────────────────────────────

        private static string ToTitleCase(string s) =>
            string.IsNullOrEmpty(s) ? s :
            System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower());
    }
}