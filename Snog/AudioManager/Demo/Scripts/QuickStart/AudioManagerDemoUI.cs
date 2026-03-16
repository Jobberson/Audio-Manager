using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Snog.Audio.Clips;
using Snog.Audio.Libraries;
using Snog.Audio.Utils;
using Snog.Audio.Generated;

namespace Snog.Audio.Demo
{
    /// <summary>
    /// UI controller for Snog's Audio Manager demo panel.
    ///
    /// Wires up:
    ///   • Audio Controls  – four volume sliders (Master / Music / SFX / Ambient)
    ///   • Music Tab       – track buttons auto-built from MusicLibrary
    ///   • SFX Tab         – one-shot play buttons auto-built from SoundLibrary
    ///   • Ambient Tab     – toggle buttons auto-built from AmbientLibrary
    ///   • Snapshots       – toggle buttons auto-built from AudioManager snapshot list
    ///   • Stop All        – single button that silences everything immediately
    ///   • Log panel       – scrolling TMP_Text area at the bottom of the screen
    ///
    /// Inspector setup
    /// ────────────────
    /// 1.  Assign the four Slider references under "Volume Sliders".
    /// 2.  Assign trackListContainer    — RectTransform inside the Music panel.
    /// 3.  Assign sfxListContainer      — RectTransform inside the SFX panel.
    /// 4.  Assign ambientListContainer  — RectTransform inside the Ambient panel.
    /// 5.  Assign snapshotContainer     — RectTransform inside the Snapshots panel.
    /// 6.  Assign snapshotTransitionSlider — optional slider that controls blend time.
    /// 7.  Assign stopAllButton         — the Stop All button in the Audio Controls panel.
    /// 8.  Assign logText               — TMP_Text inside the log box at the bottom.
    /// 9.  Optionally assign spectrumAnalyzer to keep the visualizer in sync.
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

        [Header("Track List — Music")]
        [Tooltip("RectTransform inside the Music panel. Add a Vertical Layout Group to it.")]
        [SerializeField] private Transform trackListContainer;

        [Tooltip("Height of each track button in pixels.")]
        [SerializeField] private float trackButtonHeight = 36f;

        [Tooltip("Fade-in duration when a music track button is pressed (seconds).")]
        [SerializeField] private float trackFadeIn  = 1.5f;

        [Tooltip("Fade-out duration when a music track is stopped (seconds).")]
        [SerializeField] private float trackFadeOut = 1.5f;

        [Header("Track List — SFX")]
        [Tooltip("RectTransform inside the SFX panel. Add a Vertical Layout Group to it.")]
        [SerializeField] private Transform sfxListContainer;

        [Tooltip("How long the SFX button stays highlighted after being pressed (seconds).")]
        [SerializeField] private float sfxFlashDuration = 0.4f;

        [Header("Track List — Ambient")]
        [Tooltip("RectTransform inside the Ambient panel. Add a Vertical Layout Group to it.")]
        [SerializeField] private Transform ambientListContainer;

        [Tooltip("Fade duration for ambient profile transitions (seconds).")]
        [SerializeField] private float ambientFadeDuration = 2f;

        [Header("Snapshots")]
        [Tooltip("RectTransform that will hold the auto-built snapshot buttons. Add a Vertical Layout Group to it.")]
        [SerializeField] private Transform snapshotContainer;

        [Tooltip("Optional slider controlling how long the mixer takes to blend into a snapshot (seconds).")]
        [SerializeField] private Slider snapshotTransitionSlider;

        [Tooltip("Default blend time when no transition slider is assigned.")]
        [SerializeField] private float defaultSnapshotTransition = 1f;

        [Header("Global Controls")]
        [Tooltip("Button that stops all music and ambient audio immediately.")]
        [SerializeField] private Button stopAllButton;

        [Header("Now Playing")]
        [Tooltip("Label showing the currently playing music track name.")]
        [SerializeField] private TMP_Text musicNowPlayingLabel;
        [Tooltip("Slider used as a read-only progress bar for the current music track.")]
        [SerializeField] private Slider musicProgressBar;
        [Tooltip("Label showing the active ambient track name.")]
        [SerializeField] private TMP_Text ambientNowPlayingLabel;
        [Tooltip("Label showing the last triggered SFX. Fades to idle after a short delay.")]
        [SerializeField] private TMP_Text sfxLastPlayedLabel;
        [Tooltip("How long the last-played SFX name stays visible before clearing (seconds).")]
        [SerializeField] private float sfxLabelLingerTime = 3f;

        [Header("Volume Labels")]
        [SerializeField] private TMP_Text masterVolumeLabel;
        [SerializeField] private TMP_Text musicVolumeLabel;
        [SerializeField] private TMP_Text sfxVolumeLabel;
        [SerializeField] private TMP_Text ambientVolumeLabel;

        [Header("Mute Toggles")]
        [SerializeField] private Button masterMuteButton;
        [SerializeField] private Button musicMuteButton;
        [SerializeField] private Button sfxMuteButton;
        [SerializeField] private Button ambientMuteButton;

        [Header("Log Panel")]
        [Tooltip("TMP_Text component inside the log box at the bottom of the screen.")]
        [SerializeField] private TMP_Text logText;

        // Always exactly 7 lines — oldest is dropped when a new one arrives.
        private const int MAX_LOG_LINES = 7;

        [Header("Visualizer (optional)")]
        [Tooltip("Assign the SpectrumAnalyzer in the scene to keep it in sync with track buttons.")]
        [SerializeField] private SpectrumAnalyzer spectrumAnalyzer;

        // ─────────────────────────────────────────────────────────────────────
        //  Private state
        // ─────────────────────────────────────────────────────────────────────

        private readonly Queue<string> _logLines = new Queue<string>();

        // Music
        private readonly List<(TrackButtonController ctrl, string trackName)> _trackButtons =
            new List<(TrackButtonController, string)>();
        private string _activeTrackName;

        // SFX
        private readonly List<(TrackButtonController ctrl, string soundName)> _sfxButtons =
            new List<(TrackButtonController, string)>();

        // Ambient
        private readonly List<(TrackButtonController ctrl, AmbientProfile profile)> _ambientButtons =
            new List<(TrackButtonController, AmbientProfile)>();
        private AmbientProfile _activeAmbientProfile;

        // Snapshots
        private readonly List<(TrackButtonController ctrl, string snapshotName)> _snapshotButtons =
            new List<(TrackButtonController, string)>();
        private string _activeSnapshotName;

        // Runtime ScriptableObjects created per session — destroyed on disable.
        private readonly List<AmbientProfile> _runtimeProfiles = new List<AmbientProfile>();

        // GameObjects spawned to host AmbientEmitter components — destroyed on disable.
        private readonly List<GameObject> _spawnedEmitters = new List<GameObject>();

        // Mute state — stores the pre-mute volume so unmuting restores it exactly.
        private readonly Dictionary<AudioManager.AudioChannel, float> _preMuteVolume =
            new Dictionary<AudioManager.AudioChannel, float>
            {
                { AudioManager.AudioChannel.Master,  1.0f },
                { AudioManager.AudioChannel.Music,   0.8f },
                { AudioManager.AudioChannel.FX,      1.0f },
                { AudioManager.AudioChannel.Ambient, 0.9f },
            };
        private readonly Dictionary<AudioManager.AudioChannel, bool> _muted =
            new Dictionary<AudioManager.AudioChannel, bool>
            {
                { AudioManager.AudioChannel.Master,  false },
                { AudioManager.AudioChannel.Music,   false },
                { AudioManager.AudioChannel.FX,      false },
                { AudioManager.AudioChannel.Ambient, false },
            };

        // Music progress tracking
        private float _musicStartTime   = -1f;
        private float _musicClipLength  = 0f;
        private bool  _musicIsLooping   = false;

        // SFX label clear coroutine handle
        private Coroutine _sfxLabelClearCo;

        // ─────────────────────────────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Start()
        {
            InitSliders();
            InitMuteButtons();
            InitNowPlaying();

            if (stopAllButton != null)
                stopAllButton.onClick.AddListener(StopAll);

            if (snapshotTransitionSlider != null)
            {
                snapshotTransitionSlider.minValue = 0f;
                snapshotTransitionSlider.maxValue = 5f;
                snapshotTransitionSlider.value    = defaultSnapshotTransition;
            }

            Log("Audio Manager ready.");
            StartCoroutine(BuildAllListsNextFrame());
        }

        private void Update()
        {
            UpdateMusicProgress();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Now Playing
        // ─────────────────────────────────────────────────────────────────────

        private void InitNowPlaying()
        {
            SetMusicNowPlaying(null);
            SetAmbientNowPlaying(null);
            SetSFXLastPlayed(null);

            if (musicProgressBar != null)
            {
                musicProgressBar.minValue      = 0f;
                musicProgressBar.maxValue      = 1f;
                musicProgressBar.value         = 0f;
                musicProgressBar.interactable  = false; // read-only
            }
        }

        private void SetMusicNowPlaying(string trackName)
        {
            if (musicNowPlayingLabel == null) return;
            musicNowPlayingLabel.text = string.IsNullOrEmpty(trackName)
                ? "—"
                : ToTitleCase(trackName);
        }

        private void SetAmbientNowPlaying(string trackName)
        {
            if (ambientNowPlayingLabel == null) return;
            ambientNowPlayingLabel.text = string.IsNullOrEmpty(trackName)
                ? "—"
                : ToTitleCase(trackName);
        }

        private void SetSFXLastPlayed(string soundName)
        {
            if (sfxLastPlayedLabel == null) return;
            sfxLastPlayedLabel.text = string.IsNullOrEmpty(soundName)
                ? "—"
                : ToTitleCase(soundName);
        }

        private void UpdateMusicProgress()
        {
            if (musicProgressBar == null) return;
            if (_musicStartTime < 0f || _musicClipLength <= 0f)
            {
                musicProgressBar.value = 0f;
                return;
            }

            float elapsed  = Time.time - _musicStartTime;
            float progress = _musicIsLooping
                ? (elapsed % _musicClipLength) / _musicClipLength
                : Mathf.Clamp01(elapsed / _musicClipLength);

            musicProgressBar.value = progress;
        }

        private IEnumerator ClearSFXLabelAfterDelay()
        {
            yield return new WaitForSecondsRealtime(sfxLabelLingerTime);
            SetSFXLastPlayed(null);
            _sfxLabelClearCo = null;
        }

        private void OnDisable()
        {
            // Clear ambient state so nothing keeps playing after the demo UI is gone
            if (AudioManager.Instance != null)
                AudioManager.Instance.ClearAmbient(0f);

            foreach (AmbientProfile p in _runtimeProfiles)
                if (p != null) Destroy(p);
            _runtimeProfiles.Clear();

            foreach (GameObject go in _spawnedEmitters)
                if (go != null) Destroy(go);
            _spawnedEmitters.Clear();
        }

        private IEnumerator BuildAllListsNextFrame()
        {
            // Wait up to 2 seconds for AudioManager to finish building its libraries.
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

            BuildMusicList();
            BuildSFXList();
            BuildAmbientList();
            BuildSnapshotList();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Sliders
        // ─────────────────────────────────────────────────────────────────────

        private void InitSliders()
        {
            SetupSlider(masterSlider,  1.0f, v => OnVolumeChanged(AudioManager.AudioChannel.Master,  v, "Master",  masterVolumeLabel));
            SetupSlider(musicSlider,   0.8f, v => OnVolumeChanged(AudioManager.AudioChannel.Music,   v, "Music",   musicVolumeLabel));
            SetupSlider(sfxSlider,     1.0f, v => OnVolumeChanged(AudioManager.AudioChannel.FX,      v, "SFX",     sfxVolumeLabel));
            SetupSlider(ambientSlider, 0.9f, v => OnVolumeChanged(AudioManager.AudioChannel.Ambient, v, "Ambient", ambientVolumeLabel));
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

        private void OnVolumeChanged(AudioManager.AudioChannel channel, float value, string label, TMP_Text volumeLabel)
        {
            // Moving a slider while muted unmutes the channel
            if (_muted.TryGetValue(channel, out bool isMuted) && isMuted)
            {
                _muted[channel] = false;
                RefreshMuteButtonVisual(channel);
            }

            _preMuteVolume[channel] = value;
            AudioManager.Instance.SetVolume(value, channel);

            if (volumeLabel != null)
                volumeLabel.text = $"{value:P0}";

            Log($"{label} volume → {value:P0}");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Mute toggles
        // ─────────────────────────────────────────────────────────────────────

        private void InitMuteButtons()
        {
            WireMuteButton(masterMuteButton,  AudioManager.AudioChannel.Master);
            WireMuteButton(musicMuteButton,   AudioManager.AudioChannel.Music);
            WireMuteButton(sfxMuteButton,     AudioManager.AudioChannel.FX);
            WireMuteButton(ambientMuteButton, AudioManager.AudioChannel.Ambient);
        }

        private void WireMuteButton(Button btn, AudioManager.AudioChannel channel)
        {
            if (btn == null) return;
            btn.onClick.AddListener(() => ToggleMute(channel));
            RefreshMuteButtonVisual(channel);
        }

        private void ToggleMute(AudioManager.AudioChannel channel)
        {
            if (AudioManager.Instance == null) return;

            bool nowMuted = !_muted[channel];
            _muted[channel] = nowMuted;

            if (nowMuted)
            {
                // Store current slider value before silencing
                Slider s = SliderForChannel(channel);
                if (s != null) _preMuteVolume[channel] = s.value;
                AudioManager.Instance.SetVolume(0f, channel);
            }
            else
            {
                // Restore previous volume — also sync the slider handle
                float restored = _preMuteVolume[channel];
                AudioManager.Instance.SetVolume(restored, channel);
                Slider s = SliderForChannel(channel);
                if (s != null) s.value = restored;
            }

            RefreshMuteButtonVisual(channel);
            Log($"{ChannelLabel(channel)} {(nowMuted ? "muted" : "unmuted")}.");
        }

        private void RefreshMuteButtonVisual(AudioManager.AudioChannel channel)
        {
            Button btn = MuteButtonForChannel(channel);
            if (btn == null) return;

            bool isMuted = _muted[channel];

            // Tint the button to make mute state obvious
            var img = btn.GetComponent<Image>();
            if (img != null)
                img.color = isMuted
                    ? new Color(0.8f, 0.2f, 0.2f, 1f)   // red = muted
                    : new Color(0.3f, 0.2f, 0.5f, 0.8f); // purple = live

            // Update label text if a child TMP_Text exists
            TMP_Text label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.text = isMuted ? "UM" : "M";
        }

        private Slider SliderForChannel(AudioManager.AudioChannel channel) => channel switch
        {
            AudioManager.AudioChannel.Master  => masterSlider,
            AudioManager.AudioChannel.Music   => musicSlider,
            AudioManager.AudioChannel.FX      => sfxSlider,
            AudioManager.AudioChannel.Ambient => ambientSlider,
            _                                 => null
        };

        private Button MuteButtonForChannel(AudioManager.AudioChannel channel) => channel switch
        {
            AudioManager.AudioChannel.Master  => masterMuteButton,
            AudioManager.AudioChannel.Music   => musicMuteButton,
            AudioManager.AudioChannel.FX      => sfxMuteButton,
            AudioManager.AudioChannel.Ambient => ambientMuteButton,
            _                                 => null
        };

        private static string ChannelLabel(AudioManager.AudioChannel channel) => channel switch
        {
            AudioManager.AudioChannel.Master  => "Master",
            AudioManager.AudioChannel.Music   => "Music",
            AudioManager.AudioChannel.FX      => "SFX",
            AudioManager.AudioChannel.Ambient => "Ambient",
            _                                 => channel.ToString()
        };

        // ─────────────────────────────────────────────────────────────────────
        //  Music tab
        // ─────────────────────────────────────────────────────────────────────

        private void BuildMusicList()
        {
            if (trackListContainer == null)
            {
                Log("[UI] trackListContainer not assigned.");
                return;
            }

            ClearContainer(trackListContainer);
            _trackButtons.Clear();

            EnsureVerticalLayout(trackListContainer);

            if (!AudioManager.Instance.TryGetMusicNames(out string[] names) || names == null || names.Length == 0)
            {
                Log("[UI] No music tracks found in MusicLibrary.");
                return;
            }

            TrackButtonFactory.ButtonHeight = trackButtonHeight;

            foreach (string trackName in names)
            {
                TrackButtonController ctrl = TrackButtonFactory.Create(
                    trackName,
                    ToTitleCase(trackName),
                    trackListContainer,
                    OnMusicNamePressed,
                    "♪");

                _trackButtons.Add((ctrl, trackName));
            }

            RebuildLayout(trackListContainer);
            Log($"Track list built — {names.Length} track(s) found.");
        }

        private void OnMusicNamePressed(string trackName)
        {
            TrackButtonController pressedCtrl = null;
            foreach (var (ctrl, name) in _trackButtons)
                if (name == trackName) { pressedCtrl = ctrl; break; }

            if (_activeTrackName == trackName)
            {
                // ── Stop ──────────────────────────────────────────────────────
                if (spectrumAnalyzer != null)
                    spectrumAnalyzer.StopTrack(trackFadeOut);
                else
                    AudioManager.Instance.StopMusic(trackFadeOut);

                pressedCtrl?.SetPlaying(false);
                _activeTrackName = null;
                _musicStartTime  = -1f;
                _musicClipLength = 0f;
                SetMusicNowPlaying(null);
                Log($"Stopped \"{ToTitleCase(trackName)}\".");
            }
            else
            {
                foreach (var (ctrl, _) in _trackButtons)
                    ctrl?.SetPlaying(false);

                if (spectrumAnalyzer != null)
                    spectrumAnalyzer.PlayTrack(trackName, trackFadeIn);
                else
                    AudioManager.Instance.PlayMusic(trackName, 0f, trackFadeIn);

                // Record timing for the progress bar
                var musicLib = AudioManager.Instance.GetComponent<MusicLibrary>();
                if (musicLib != null)
                {
                    MusicTrack mt = musicLib.GetTrackFromName(trackName);
                    if (mt != null && mt.clip != null)
                    {
                        _musicClipLength = mt.clip.length;
                        _musicIsLooping  = mt.loop;
                    }
                }
                _musicStartTime = Time.time + trackFadeIn; // account for fade-in delay

                pressedCtrl?.SetPlaying(true);
                _activeTrackName = trackName;
                SetMusicNowPlaying(trackName);
                Log($"Playing \"{ToTitleCase(trackName)}\" (fade-in: {trackFadeIn}s).");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SFX tab
        // ─────────────────────────────────────────────────────────────────────

        private void BuildSFXList()
        {
            if (sfxListContainer == null)
            {
                Log("[UI] sfxListContainer not assigned.");
                return;
            }

            ClearContainer(sfxListContainer);
            _sfxButtons.Clear();

            EnsureVerticalLayout(sfxListContainer);

            if (!AudioManager.Instance.TryGetSoundNames(out string[] names) || names == null || names.Length == 0)
            {
                Log("[UI] No SFX found in SoundLibrary.");
                return;
            }

            TrackButtonFactory.ButtonHeight = trackButtonHeight;

            foreach (string soundName in names)
            {
                TrackButtonController ctrl = TrackButtonFactory.Create(
                    soundName,
                    ToTitleCase(soundName),
                    sfxListContainer,
                    OnSFXNamePressed,
                    "▶");

                _sfxButtons.Add((ctrl, soundName));
            }

            RebuildLayout(sfxListContainer);
            Log($"SFX list built — {names.Length} sound(s) found.");
        }

        private void OnSFXNamePressed(string soundName)
        {
            Log($"SFX: \"{ToTitleCase(soundName)}\"...");

            if (AudioManager.Instance == null)
            {
                Log("[UI] AudioManager.Instance is null!");
                return;
            }

            bool played = AudioManager.Instance.PlaySfx2D(soundName, 1f);

            if (played)
            {
                // Update Now Playing strip
                SetSFXLastPlayed(soundName);
                if (_sfxLabelClearCo != null) StopCoroutine(_sfxLabelClearCo);
                _sfxLabelClearCo = StartCoroutine(ClearSFXLabelAfterDelay());

                TrackButtonController pressedCtrl = null;
                foreach (var (ctrl, name) in _sfxButtons)
                    if (name == soundName) { pressedCtrl = ctrl; break; }

                if (pressedCtrl != null)
                    StartCoroutine(FlashSFXButton(pressedCtrl));
            }
            else
            {
                Log($"[UI] Sound \"{soundName}\" not found in SoundLibrary.");
            }
        }

        /// <summary>
        /// Briefly lights up a SFX button to confirm playback, then returns it to idle.
        /// </summary>
        private IEnumerator FlashSFXButton(TrackButtonController ctrl)
        {
            ctrl.SetPlaying(true);
            yield return new WaitForSecondsRealtime(sfxFlashDuration);
            ctrl.SetPlaying(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Ambient tab
        // ─────────────────────────────────────────────────────────────────────

        private void BuildAmbientList()
        {
            if (ambientListContainer == null)
            {
                Log("[UI] ambientListContainer not assigned.");
                return;
            }

            ClearContainer(ambientListContainer);
            _ambientButtons.Clear();

            // Destroy emitters and profiles from a previous build
            foreach (GameObject go in _spawnedEmitters)
                if (go != null) Destroy(go);
            _spawnedEmitters.Clear();

            foreach (AmbientProfile p in _runtimeProfiles)
                if (p != null) Destroy(p);
            _runtimeProfiles.Clear();

            EnsureVerticalLayout(ambientListContainer);

            var ambientLib = AudioManager.Instance != null
                ? AudioManager.Instance.GetComponent<AmbientLibrary>()
                : null;

            if (ambientLib == null || ambientLib.tracks == null || ambientLib.tracks.Count == 0)
            {
                Log("[UI] No ambient tracks found in AmbientLibrary.");
                return;
            }

            // Cache reflection fields once — AmbientEmitter.track and spatialBlend are private.
            FieldInfo trackField    = typeof(AmbientEmitter).GetField("track",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo spatialField  = typeof(AmbientEmitter).GetField("spatialBlend",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo loopField     = typeof(AmbientEmitter).GetField("loop",
                BindingFlags.NonPublic | BindingFlags.Instance);

            TrackButtonFactory.ButtonHeight = trackButtonHeight;
            int built = 0;

            foreach (AmbientTrack track in ambientLib.tracks)
            {
                if (track == null || track.clip == null) continue;

                // ── Spawn a 2D AmbientEmitter on the AudioManager GameObject ────
                // OnEnable auto-calls RegisterEmitter, giving the system a real
                // AudioSource to route the clip through.
                GameObject emitterGO = new GameObject($"DemoAmbientEmitter_{track.trackName}");
                emitterGO.transform.SetParent(AudioManager.Instance.transform);

                AmbientEmitter emitter = emitterGO.AddComponent<AmbientEmitter>();

                // Set private fields via reflection (safe: this is a demo utility).
                // Must happen after AddComponent (Awake already ran), but the track
                // field is only read inside EnsurePlaying which fires later.
                trackField?.SetValue(emitter, track);
                spatialField?.SetValue(emitter, 0f);   // 2D — no positional audio in demo
                loopField?.SetValue(emitter, true);

                _spawnedEmitters.Add(emitterGO);

                // ── Build a single-layer AmbientProfile pointing at this track ──
                AmbientProfile profile = ScriptableObject.CreateInstance<AmbientProfile>();
                profile.name = track.trackName;
                profile.layers = new AmbientLayer[]
                {
                    new AmbientLayer { track = track, volume = 1f, priority = 0 }
                };
                _runtimeProfiles.Add(profile);

                // ── Build button ─────────────────────────────────────────────────
                AmbientProfile captured = profile;
                TrackButtonController ctrl = TrackButtonFactory.Create(
                    track.trackName,
                    ToTitleCase(track.trackName),
                    ambientListContainer,
                    _ => OnAmbientProfilePressed(captured),
                    "≋");

                _ambientButtons.Add((ctrl, profile));
                built++;
            }

            RebuildLayout(ambientListContainer);
            Log($"Ambient list built — {built} track(s) found.");
        }

        private void OnAmbientProfilePressed(AmbientProfile profile)
        {
            if (_activeAmbientProfile == profile)
            {
                AudioManager.Instance.ClearAmbient(ambientFadeDuration);

                foreach (var (ctrl, _) in _ambientButtons)
                    ctrl?.SetPlaying(false);

                _activeAmbientProfile = null;
                SetAmbientNowPlaying(null);
                Log($"Ambient cleared (fade: {ambientFadeDuration}s).");
            }
            else
            {
                foreach (var (ctrl, _) in _ambientButtons)
                    ctrl?.SetPlaying(false);

                AudioManager.Instance.SetAmbientProfile(profile, ambientFadeDuration);

                foreach (var (ctrl, p) in _ambientButtons)
                    if (p == profile) { ctrl?.SetPlaying(true); break; }

                _activeAmbientProfile = profile;
                SetAmbientNowPlaying(profile.name);
                Log($"Ambient: \"{ToTitleCase(profile.name)}\" (fade: {ambientFadeDuration}s).");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Snapshots tab
        // ─────────────────────────────────────────────────────────────────────

        private void BuildSnapshotList()
        {
            if (snapshotContainer == null)
            {
                Log("[UI] snapshotContainer not assigned — skipping snapshots.");
                return;
            }

            ClearContainer(snapshotContainer,
                snapshotTransitionSlider != null ? snapshotTransitionSlider.gameObject : null);
            _snapshotButtons.Clear();

            EnsureVerticalLayout(snapshotContainer);

            string[] names = AudioManager.Instance != null
                ? AudioManager.Instance.GetSnapshotNames()
                : null;

            if (names == null || names.Length == 0)
            {
                Log("[UI] No snapshots found in AudioManager.");
                return;
            }

            TrackButtonFactory.ButtonHeight = trackButtonHeight;

            foreach (string snapName in names)
            {
                string captured = snapName;
                TrackButtonController ctrl = TrackButtonFactory.Create(
                    snapName,
                    ToTitleCase(snapName),
                    snapshotContainer,
                    _ => OnSnapshotPressed(captured),
                    "◈");

                _snapshotButtons.Add((ctrl, snapName));
            }

            RebuildLayout(snapshotContainer);
            Log($"Snapshots built — {names.Length} found.");
        }

        private void OnSnapshotPressed(string snapName)
        {
            float t = snapshotTransitionSlider != null
                ? snapshotTransitionSlider.value
                : defaultSnapshotTransition;

            if (_activeSnapshotName == snapName)
            {
                // Pressing the active snapshot again has no meaningful "undo" in the
                // mixer API, so we just deactivate the button visually and log it.
                foreach (var (ctrl, _) in _snapshotButtons)
                    ctrl?.SetPlaying(false);

                _activeSnapshotName = null;
                Log($"Snapshot \"{ToTitleCase(snapName)}\" deselected.");
            }
            else
            {
                foreach (var (ctrl, _) in _snapshotButtons)
                    ctrl?.SetPlaying(false);

                AudioManager.Instance.TransitionToSnapshot(snapName, t);

                foreach (var (ctrl, n) in _snapshotButtons)
                    if (n == snapName) { ctrl?.SetPlaying(true); break; }

                _activeSnapshotName = snapName;
                Log($"Snapshot → \"{ToTitleCase(snapName)}\" ({t:F1}s blend).");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Stop All
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Stops all music and ambient audio and resets every track button to idle.
        /// Safe to call from a UnityEvent in the inspector.
        /// </summary>
        public void StopAll()
        {
            if (AudioManager.Instance == null) return;

            AudioManager.Instance.StopMusic(trackFadeOut);
            foreach (var (ctrl, _) in _trackButtons)
                ctrl?.SetPlaying(false);
            _activeTrackName = null;
            _musicStartTime  = -1f;
            _musicClipLength = 0f;
            SetMusicNowPlaying(null);

            AudioManager.Instance.ClearAmbient(ambientFadeDuration);
            foreach (var (ctrl, _) in _ambientButtons)
                ctrl?.SetPlaying(false);
            _activeAmbientProfile = null;
            SetAmbientNowPlaying(null);

            if (_sfxLabelClearCo != null) StopCoroutine(_sfxLabelClearCo);
            SetSFXLastPlayed(null);

            Log($"Stop All — music fading out ({trackFadeOut}s), ambient clearing ({ambientFadeDuration}s).");
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

        /// <summary>Rebuilds all track/sound/snapshot lists — call this if assets change at runtime.</summary>
        public void RefreshTrackList()
        {
            BuildMusicList();
            BuildSFXList();
            BuildAmbientList();
            BuildSnapshotList();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Shared list helpers
        // ─────────────────────────────────────────────────────────────────────

        private static void ClearContainer(Transform container, GameObject preserve = null)
        {
            foreach (Transform child in container)
            {
                if (preserve != null && child.gameObject == preserve) continue;
                Destroy(child.gameObject);
            }
        }

        private static void EnsureVerticalLayout(Transform container)
        {
            VerticalLayoutGroup vlg = container.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = container.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing               = 4f;
            vlg.padding               = new RectOffset(0, 0, 0, 0);
            vlg.childAlignment        = TextAnchor.LowerCenter;
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = false;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;

            // Remove any stray ContentSizeFitter
            ContentSizeFitter csf = container.GetComponent<ContentSizeFitter>();
            if (csf != null) Destroy(csf);
        }

        private static void RebuildLayout(Transform container)
        {
            if (container is RectTransform rt)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                if (rt.parent is RectTransform parentRT)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRT);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Utilities
        // ─────────────────────────────────────────────────────────────────────

        private static string ToTitleCase(string s) =>
            string.IsNullOrEmpty(s) ? s :
            System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower());
    }
}