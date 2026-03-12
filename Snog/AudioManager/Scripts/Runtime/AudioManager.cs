using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;

using Snog.Audio.Libraries;
using Snog.Audio.Clips;
using Snog.Audio.Utils;
using Snog.Shared;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Snog.Audio
{
    [RequireComponent(typeof(SoundLibrary))]
    [RequireComponent(typeof(MusicLibrary))]
    [RequireComponent(typeof(AmbientLibrary))]
    public sealed partial class AudioManager : Singleton<AudioManager>
    {
        private const float SCORE_WEIGHT_PRIORITY = 1000f;
        private const float SCORE_WEIGHT_EMITTER  = 100f;
        private const float SCORE_WEIGHT_VOLUME   = 10f;
        private const float SCORE_WEIGHT_DISTANCE = 1f;
        private const float VOLUME_EPSILON        = 0.0001f;

        #region Types

        public enum AudioChannel { Master, Music, Ambient, FX }

        [Serializable]
        private sealed class AmbientStackEntry
        {
            public int token;
            public AmbientProfile profile;
            public int priority;
            public float fadeSeconds;
        }

        private sealed class ScoredEmitter
        {
            public AmbientEmitter emitter;
            public float score;
            public float targetVolume01;
            // Fix 4: carry the winning layer so we can forward its playback overrides.
            public AmbientLayer sourceLayer;
        }

        [Serializable]
        public sealed class MixerSnapshotEntry
        {
            [Tooltip("Unique name used to reference this snapshot at runtime.")]
            public string name;
            public AudioMixerSnapshot snapshot;
        }

        #endregion

        #region Mixer / Volumes

        [Header("Mixer")]
        [SerializeField] private AudioMixer mainMixer;
        [SerializeField] private AudioMixerGroup musicGroup;
        [SerializeField] private AudioMixerGroup ambientGroup;
        [SerializeField] private AudioMixerGroup fxGroup;

        [Header("Volume")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float musicVolume  = 1f;
        [SerializeField, Range(0f, 1f)] private float ambientVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float fxVolume     = 1f;

        [Header("Snapshots")]
        [SerializeField] private List<MixerSnapshotEntry> snapshots = new List<MixerSnapshotEntry>();
        private Dictionary<string, AudioMixerSnapshot> snapshotLookup;

        #endregion

        // ─── Fix 5: UnityEvent callbacks ────────────────────────────────────
        #region Events

        [Header("Events — Music")]
        [Tooltip("Fired when a music track starts playing. Argument: track name.")]
        public UnityEvent<string> onMusicStarted = new();

        [Tooltip("Fired when music is manually stopped (fade or instant). Argument: track name that was playing.")]
        public UnityEvent<string> onMusicStopped = new();

        [Tooltip("Fired when the current music track reaches its natural end (non-looping only).")]
        public UnityEvent<string> onMusicFinished = new();

        [Header("Events — SFX")]
        [Tooltip("Fired each time a 2D or 3D SFX is successfully played. Argument: sound name.")]
        public UnityEvent<string> onSfxPlayed = new();

        [Header("Events — Ambient")]
        [Tooltip("Fired when a profile is pushed onto the ambient stack. Argument: profile.")]
        public UnityEvent<AmbientProfile> onAmbientProfilePushed = new();

        [Tooltip("Fired when a profile is popped off the ambient stack. Argument: profile.")]
        public UnityEvent<AmbientProfile> onAmbientProfilePopped = new();

        #endregion

        #region Music

        private Coroutine musicFadeCo;
        // Fix 8: second source used only during cross-fades.
        private AudioSource musicSourceAlt;
        private Coroutine crossFadeCo;
        private Coroutine musicFinishWatchCo;
        private string _currentMusicTrackName;

        #endregion

        #region SFX

        [Header("3D FX Pool")]
        [SerializeField] private int fxPoolSize = 10;
        [SerializeField] private AudioSourcePool fxPool;

        #endregion

        #region Ambient

        [Header("Ambient (Emitters)")]
        [Tooltip("Maximum number of AmbientEmitters allowed to play simultaneously.")]
        [SerializeField] private int maxAmbientVoices = 16;

        [Tooltip("Default fade time used by ambient operations when not specified.")]
        [SerializeField] private float defaultAmbientFade = 2f;

        [Tooltip("How often the system re-scores emitters (seconds). Lower = more responsive, higher = cheaper.")]
        [SerializeField] private float ambientRescoreInterval = 0.25f;

        [Tooltip("If false, only the best emitter per AmbientTrack can play at a time.")]
        [SerializeField] private bool allowMultipleEmittersPerTrack = true;

        [Tooltip("Optional override for listener transform. If null, auto-detects AudioListener.")]
        [SerializeField] private Transform listenerOverride;

        private readonly List<ScoredEmitter> scoredCache = new(64);
        private readonly HashSet<AmbientEmitter> allowedCache = new();
        private readonly HashSet<AmbientTrack> usedTracksCache = new();

        // Fix 4: track which AmbientLayer "won" for each track so we can forward its overrides.
        private readonly Dictionary<AmbientTrack, AmbientLayer> bestLayerByTrack = new();

        private AudioListener cachedListener;
        private Transform cachedListenerTransform;
        private float lastListenerCheckTime;
        private const float LISTENER_RECHECK_INTERVAL = 1f;

        #endregion

        #region Editor Tools State

        [Header("Folder Paths (Editor Tools)")]
        [SerializeField] private string audioFolderPath;

        #endregion

        #region Libraries

        private SoundLibrary soundLibrary;
        private MusicLibrary musicLibrary;
        private AmbientLibrary ambientLibrary;

        #endregion

        #region Core Sources

        private AudioSource musicSource;
        private AudioSource fx2DSource;

        #endregion

        #region Ambient Stack / Emitters

        private readonly List<AmbientEmitter> emitters = new();
        private readonly List<AmbientStackEntry> ambientStack = new();

        private Coroutine ambientLoopCo;
        private int ambientTokenCounter = 1;

        private float ambientCurrentFade = 0f;
        private float ambientNextRescoreTime = 0f;

        private readonly Dictionary<AmbientTrack, float> desiredVolumeByTrack   = new();
        private readonly Dictionary<AmbientTrack, int>   desiredPriorityByTrack = new();

        #endregion

        #region Unity

        protected override void Awake()
        {
            base.Awake();
            GetLibraries();

#if UNITY_EDITOR
            AutoAssignMixerAndGroups_EditorOnly();
#endif

            CreateCoreSources();
            ApplyMixerRouting();
            ApplyAllMixerVolumes();
            InitializeFxPoolIfNeeded();
            BuildSnapshotLookup();
            StartAmbientLoopIfNeeded();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            GetLibraries();
            BuildSnapshotLookup();
        }
#endif

        #endregion

        #region Initialization

        private void CreateCoreSources()
        {
            GameObject fxGO = new GameObject("FX 2D");
            fxGO.transform.parent = transform;
            fx2DSource = fxGO.AddComponent<AudioSource>();
            fx2DSource.playOnAwake = false;
            fx2DSource.spatialBlend = 0f;

            GameObject musicGO = new GameObject("Music");
            musicGO.transform.parent = transform;
            musicSource = musicGO.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;

            // Fix 8: alt source for cross-fading
            GameObject musicAltGO = new GameObject("Music (Alt)");
            musicAltGO.transform.parent = transform;
            musicSourceAlt = musicAltGO.AddComponent<AudioSource>();
            musicSourceAlt.playOnAwake = false;
            musicSourceAlt.spatialBlend = 0f;
        }

        private void ApplyMixerRouting()
        {
            if (fx2DSource != null)     fx2DSource.outputAudioMixerGroup    = fxGroup;
            if (musicSource != null)    musicSource.outputAudioMixerGroup   = musicGroup;
            if (musicSourceAlt != null) musicSourceAlt.outputAudioMixerGroup = musicGroup;
        }

        private void InitializeFxPoolIfNeeded()
        {
            if (fxPool != null) return;

            GameObject poolGO = new GameObject("FX Pool");
            poolGO.transform.parent = transform;
            fxPool = poolGO.AddComponent<AudioSourcePool>();
            fxPool.Initialize(fxPoolSize, fxGroup);
        }

        #endregion

        #region Mixer Volumes / Snapshots

        private void ApplyAllMixerVolumes()
        {
            SetVolume(masterVolume, AudioChannel.Master);
            SetVolume(musicVolume,  AudioChannel.Music);
            SetVolume(ambientVolume, AudioChannel.Ambient);
            SetVolume(fxVolume,     AudioChannel.FX);
        }

        public void SetVolume(float volume01, AudioChannel channel)
        {
            if (mainMixer == null) return;

            float db = Mathf.Log10(Mathf.Clamp(volume01, 0.0001f, 1f)) * 20f;

            switch (channel)
            {
                case AudioChannel.Master:  mainMixer.SetFloat("MasterVolume",  db); break;
                case AudioChannel.Music:   mainMixer.SetFloat("MusicVolume",   db); break;
                case AudioChannel.Ambient: mainMixer.SetFloat("AmbientVolume", db); break;
                case AudioChannel.FX:      mainMixer.SetFloat("FXVolume",      db); break;
            }
        }

        public float GetMixerVolumeDB(string parameterName)
        {
            if (mainMixer == null) return -80f;
            if (mainMixer.GetFloat(parameterName, out float v)) return v;
            return -80f;
        }

        public void TransitionToSnapshot(string snapshotName, float transitionTime)
        {
            if (string.IsNullOrWhiteSpace(snapshotName))
            {
                Debug.LogWarning("AudioManager: snapshotName is null/empty.", this);
                return;
            }

            if (snapshotLookup == null) BuildSnapshotLookup();

            string key = snapshotName.Trim();

            if (snapshotLookup == null || snapshotLookup.Count == 0)
            {
                Debug.LogWarning("AudioManager: No snapshots configured in the snapshots list.", this);
                return;
            }

            if (!snapshotLookup.TryGetValue(key, out AudioMixerSnapshot snapshot) || snapshot == null)
            {
                Debug.LogWarning($"AudioManager: Snapshot '{key}' not found (check AudioManager > Snapshots list).", this);
                return;
            }

            snapshot.TransitionTo(transitionTime);
        }

        private void BuildSnapshotLookup()
        {
            snapshotLookup = new Dictionary<string, AudioMixerSnapshot>(StringComparer.OrdinalIgnoreCase);
            if (snapshots == null) return;

            for (int i = 0; i < snapshots.Count; i++)
            {
                MixerSnapshotEntry entry = snapshots[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.name) || entry.snapshot == null)
                    continue;

                string key = entry.name.Trim();
                if (snapshotLookup.ContainsKey(key))
                    Debug.LogWarning($"AudioManager: Duplicate snapshot name '{key}'. The later entry will overwrite the earlier one.", this);

                snapshotLookup[key] = entry.snapshot;
            }
        }

        public string[] GetSnapshotNames()
        {
            if (snapshots == null || snapshots.Count == 0) return Array.Empty<string>();

            List<string> names = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                MixerSnapshotEntry entry = snapshots[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.name) || entry.snapshot == null) continue;
                names.Add(entry.name.Trim());
            }

            return names.ToArray();
        }

        #endregion

        #region SFX API

        public void PlaySfx2D(string soundName) => PlaySfx2D(soundName, 1f);

        /// <summary>Plays a 2D sound effect.</summary>
        public bool PlaySfx2D(string soundName, float volume = -1f)
        {
            if (soundLibrary == null) { Debug.LogWarning("[AudioManager] SoundLibrary not found."); return false; }
            if (fx2DSource == null)   { Debug.LogError("[AudioManager] FX 2D AudioSource is null."); return false; }

            AudioClip clip = soundLibrary.GetClipFromName(soundName);
            if (clip == null) return false;

            float vol = volume < 0f ? 1f : Mathf.Clamp01(volume);
            fx2DSource.PlayOneShot(clip, vol);

            // Fix 5
            onSfxPlayed?.Invoke(soundName);
            return true;
        }

        /// <summary>Plays a 3D sound effect at a world position.</summary>
        public bool PlaySfx3D(string soundName, Vector3 position, float volume = -1f)
        {
            if (soundLibrary == null) { Debug.LogWarning("[AudioManager] SoundLibrary not found."); return false; }
            if (fxPool == null)       { Debug.LogError("[AudioManager] FX Pool is null."); return false; }

            AudioClip clip = soundLibrary.GetClipFromName(soundName);
            if (clip == null) return false;

            float vol = volume < 0f ? 1f : Mathf.Clamp01(volume);
            fxPool.PlayClip(clip, position, vol);

            // Fix 5
            onSfxPlayed?.Invoke(soundName);
            return true;
        }

        public void PlaySfx3D(string soundName, Vector3 position) => PlaySfx3D(soundName, position, 1f);

        #endregion

        #region Music API

        /// <summary>Plays a music track with optional delay and fade-in.</summary>
        public bool PlayMusic(string trackName, float startDelay = 0f, float fadeDuration = 0f)
        {
            if (musicLibrary == null) { Debug.LogWarning("[AudioManager] MusicLibrary not found."); return false; }

            MusicTrack track = musicLibrary.GetTrackFromName(trackName);
            if (track == null)       { Debug.LogWarning($"[AudioManager] Music track '{trackName}' not found in library."); return false; }
            if (track.clip == null)  { Debug.LogWarning($"[AudioManager] Music track '{trackName}' has no AudioClip assigned."); return false; }
            if (musicSource == null) { Debug.LogError("[AudioManager] Music AudioSource is null."); return false; }

            StopMusicFadeCoroutine();
            StopMusicFinishWatch();

            _currentMusicTrackName = trackName;

            musicSource.clip = track.clip;
            musicSource.loop = track.loop;
            musicSource.volume = 0f;

            if (fadeDuration > 0f)
                musicFadeCo = StartCoroutine(FadeInMusic(startDelay, fadeDuration));
            else if (startDelay > 0f)
                musicFadeCo = StartCoroutine(PlayAfterDelay(startDelay));
            else
            {
                musicSource.volume = 1f;
                musicSource.Play();
            }

            // Fix 5: fire event and start natural-end watcher for non-looping tracks.
            onMusicStarted?.Invoke(trackName);
            if (!track.loop)
                musicFinishWatchCo = StartCoroutine(WatchMusicFinish(trackName, track.clip.length));

            return true;
        }

        public void StopMusic(float fadeSeconds = 0f)
        {
            StopMusicFadeCoroutine();
            StopMusicFinishWatch();

            string stoppedTrack = _currentMusicTrackName;
            _currentMusicTrackName = null;

            if (fadeSeconds <= 0f)
            {
                if (musicSource != null) { musicSource.Stop(); musicSource.clip = null; }
            }
            else
            {
                musicFadeCo = StartCoroutine(FadeOutAndStop(fadeSeconds));
            }

            // Fix 5
            if (!string.IsNullOrEmpty(stoppedTrack))
                onMusicStopped?.Invoke(stoppedTrack);
        }

        // ─── Fix 8: CrossFadeMusic ──────────────────────────────────────────
        /// <summary>
        /// Smoothly transitions from the currently playing music to a new track by
        /// simultaneously fading out the active source and fading in the new one.
        /// </summary>
        /// <param name="trackName">Track to transition to (must be in MusicLibrary).</param>
        /// <param name="crossFadeDuration">Total duration of the cross-fade in seconds.</param>
        public bool CrossFadeMusic(string trackName, float crossFadeDuration = 1f)
        {
            if (musicLibrary == null) { Debug.LogWarning("[AudioManager] MusicLibrary not found."); return false; }

            MusicTrack track = musicLibrary.GetTrackFromName(trackName);
            if (track == null)       { Debug.LogWarning($"[AudioManager] Music track '{trackName}' not found in library."); return false; }
            if (track.clip == null)  { Debug.LogWarning($"[AudioManager] Music track '{trackName}' has no AudioClip assigned."); return false; }
            if (musicSource == null || musicSourceAlt == null) { Debug.LogError("[AudioManager] Music AudioSources are null."); return false; }

            // Stop any in-progress cross-fade or manual fade.
            StopMusicFadeCoroutine();
            StopMusicFinishWatch();
            if (crossFadeCo != null) { StopCoroutine(crossFadeCo); crossFadeCo = null; }

            string previousTrack = _currentMusicTrackName;
            _currentMusicTrackName = trackName;

            crossFadeCo = StartCoroutine(CrossFadeCo(track, crossFadeDuration, previousTrack));

            // Fix 5
            onMusicStarted?.Invoke(trackName);
            if (!track.loop)
                musicFinishWatchCo = StartCoroutine(WatchMusicFinish(trackName, track.clip.length + crossFadeDuration));

            return true;
        }

        private IEnumerator CrossFadeCo(MusicTrack newTrack, float duration, string previousTrack)
        {
            duration = Mathf.Max(0.05f, duration);

            // Swap roles: alt becomes the new track, primary fades out.
            musicSourceAlt.clip = newTrack.clip;
            musicSourceAlt.loop = newTrack.loop;
            musicSourceAlt.volume = 0f;
            musicSourceAlt.Play();

            float startVolume = musicSource.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                musicSource.volume    = Mathf.Lerp(startVolume, 0f, t);
                musicSourceAlt.volume = Mathf.Lerp(0f, 1f, t);
                yield return null;
            }

            musicSource.Stop();
            musicSource.clip = null;
            musicSource.volume = 0f;

            // Swap references: alt is now the primary.
            (musicSource, musicSourceAlt) = (musicSourceAlt, musicSource);
            musicSource.volume = 1f;

            if (!string.IsNullOrEmpty(previousTrack))
                onMusicStopped?.Invoke(previousTrack);

            crossFadeCo = null;
        }

        // ─── Natural end watcher ────────────────────────────────────────────
        private IEnumerator WatchMusicFinish(string trackName, float clipLength)
        {
            // Wait for approximately the clip length (+ a small buffer for scheduling jitter).
            yield return new WaitForSeconds(clipLength + 0.1f);

            // If the source stopped on its own (not manually) fire the finish event.
            if (musicSource != null && !musicSource.isPlaying)
            {
                onMusicFinished?.Invoke(trackName);
                _currentMusicTrackName = null;
            }

            musicFinishWatchCo = null;
        }

        private void StopMusicFinishWatch()
        {
            if (musicFinishWatchCo != null) { StopCoroutine(musicFinishWatchCo); musicFinishWatchCo = null; }
        }

        // ─── Internal fade helpers ──────────────────────────────────────────
        private IEnumerator MusicFadeInCo(float delay, float duration)
        {
            duration = Mathf.Max(0.0001f, duration);
            musicSource.volume = 0f;
            musicSource.PlayDelayed(Mathf.Max(0f, delay));

            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                musicSource.volume = Mathf.Clamp01(time / duration);
                yield return null;
            }
            musicSource.volume = 1f;
        }

        private IEnumerator MusicFadeOutCo(float duration)
        {
            duration = Mathf.Max(0.0001f, duration);
            float start = musicSource.volume;
            float time = 0f;

            while (time < duration)
            {
                time += Time.deltaTime;
                musicSource.volume = Mathf.Lerp(start, 0f, Mathf.Clamp01(time / duration));
                yield return null;
            }

            musicSource.Stop();
            musicSource.clip = null;
            musicSource.volume = start;
        }

        private void StopMusicFadeCoroutine()
        {
            if (musicFadeCo != null) { StopCoroutine(musicFadeCo); musicFadeCo = null; }
        }

        private IEnumerator FadeInMusic(float startDelay, float fadeDuration)
            => MusicFadeInCo(startDelay, fadeDuration);

        private IEnumerator PlayAfterDelay(float delay)
        {
            if (musicSource == null) yield break;
            if (delay > 0f) yield return new WaitForSeconds(delay);
            musicSource.volume = 1f;
            musicSource.Play();
        }

        private IEnumerator FadeOutAndStop(float fadeSeconds)
            => MusicFadeOutCo(fadeSeconds);

        #endregion

        #region Ambient API

        public void RegisterEmitter(AmbientEmitter emitter)
        {
            if (emitter == null) return;
            if (!emitters.Contains(emitter)) emitters.Add(emitter);
            StartAmbientLoopIfNeeded();
        }

        public void UnregisterEmitter(AmbientEmitter emitter)
        {
            if (emitter == null) return;
            emitters.Remove(emitter);
        }

        public void SetAmbientProfile(AmbientProfile profile, float fade = -1f)
        {
            Debug.Log($"AudioManager: SetAmbientProfile called -> {(profile != null ? profile.name : "null")}", profile);

            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);
            ambientStack.Clear();

            if (profile != null)
            {
                ambientStack.Add(new AmbientStackEntry
                {
                    token      = ambientTokenCounter++,
                    profile    = profile,
                    priority   = 0,
                    fadeSeconds = f
                });
            }

            ambientCurrentFade = f;
            ForceRescoreNow();
        }

        public void ClearAmbient(float fade = -1f)
        {
            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);
            ambientStack.Clear();
            ambientCurrentFade = f;
            ForceRescoreNow();
        }

        public int PushAmbientProfile(AmbientProfile profile, int priority = 0, float fade = -1f)
        {
            if (profile == null) return -1;

            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);

            AmbientStackEntry e = new()
            {
                token       = ambientTokenCounter++,
                profile     = profile,
                priority    = priority,
                fadeSeconds = f
            };
            ambientStack.Add(e);
            ambientCurrentFade = f;
            ForceRescoreNow();

            // Fix 5
            onAmbientProfilePushed?.Invoke(profile);
            return e.token;
        }

        public void PopAmbientToken(int token, float fade = -1f)
        {
            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);

            AmbientProfile popped = null;
            for (int i = ambientStack.Count - 1; i >= 0; i--)
            {
                if (ambientStack[i].token == token)
                {
                    popped = ambientStack[i].profile;
                    ambientStack.RemoveAt(i);
                    break;
                }
            }

            ambientCurrentFade = f;
            ForceRescoreNow();

            // Fix 5
            if (popped != null) onAmbientProfilePopped?.Invoke(popped);
        }

        public void PopAmbientProfile(AmbientProfile profile, float fade = -1f)
        {
            if (profile == null) return;

            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);

            for (int i = ambientStack.Count - 1; i >= 0; i--)
            {
                if (ambientStack[i].profile == profile)
                {
                    ambientStack.RemoveAt(i);
                    break;
                }
            }

            ambientCurrentFade = f;
            ForceRescoreNow();

            // Fix 5
            onAmbientProfilePopped?.Invoke(profile);
        }

        public bool TryGetCurrentAmbientStack(out int count)
        {
            count = ambientStack.Count;
            return true;
        }

        private void StartAmbientLoopIfNeeded()
        {
            if (ambientLoopCo != null) return;
            ambientLoopCo = StartCoroutine(AmbientLoopCo());
        }

        private IEnumerator AmbientLoopCo()
        {
            float nextCleanupTime = 0f;
            const float CLEANUP_INTERVAL = 5f;

            while (true)
            {
                float now = Time.time;

                if (now >= nextCleanupTime)
                {
                    CleanupNullEmitters();
                    nextCleanupTime = now + CLEANUP_INTERVAL;
                }

                if (emitters.Count == 0 && ambientStack.Count == 0)
                {
                    ambientLoopCo = null;
                    yield break;
                }

                if (Time.unscaledTime >= ambientNextRescoreTime)
                {
                    ApplyAmbientTargets();
                    ambientNextRescoreTime = Time.unscaledTime + Mathf.Max(0.05f, ambientRescoreInterval);
                }

                float globalGain = Mathf.Clamp01(masterVolume) * Mathf.Clamp01(ambientVolume);

                for (int i = 0; i < emitters.Count; i++)
                {
                    if (emitters[i] == null) continue;
                    emitters[i].StepVolume(Time.deltaTime, ambientCurrentFade, globalGain);
                }

                yield return null;
            }
        }

        private void ForceRescoreNow()
        {
            StartAmbientLoopIfNeeded();
            ambientNextRescoreTime = 0f;
        }

        private void CleanupNullEmitters() => emitters.RemoveAll(e => e == null);

        private Transform GetListenerTransform()
        {
            if (listenerOverride != null) return listenerOverride;

            float now = Time.time;
            if (cachedListener == null || now - lastListenerCheckTime > LISTENER_RECHECK_INTERVAL)
            {
                cachedListener = FindAnyObjectByType<AudioListener>();
                cachedListenerTransform = cachedListener != null ? cachedListener.transform : null;
                lastListenerCheckTime = now;
            }

            return cachedListenerTransform;
        }

        private void ApplyAmbientTargets()
        {
            desiredVolumeByTrack.Clear();
            desiredPriorityByTrack.Clear();
            // Fix 4: also track the best layer per track so we can forward overrides.
            bestLayerByTrack.Clear();

            for (int s = 0; s < ambientStack.Count; s++)
            {
                AmbientStackEntry entry = ambientStack[s];
                if (entry == null || entry.profile == null || entry.profile.layers == null) continue;

                AmbientLayer[] layers = entry.profile.layers;

                for (int i = 0; i < layers.Length; i++)
                {
                    AmbientLayer layer = layers[i];

                    if (layer == null)
                    {
                        Debug.LogWarning("AudioManager: AmbientLayer is null in profile.", entry.profile);
                        continue;
                    }

                    if (layer.track == null)
                    {
                        Debug.LogWarning($"AudioManager: AmbientLayer has no track in profile '{entry.profile.name}'.", entry.profile);
                        continue;
                    }

                    if (layer.track.clip == null)
                    {
                        Debug.LogWarning($"AudioManager: AmbientTrack '{layer.track.name}' has no clip assigned.", layer.track);
                        continue;
                    }

                    int combinedPriority = entry.priority + layer.priority;
                    float v = Mathf.Clamp01(layer.volume);

                    if (!desiredVolumeByTrack.ContainsKey(layer.track))
                    {
                        desiredVolumeByTrack[layer.track]   = v;
                        desiredPriorityByTrack[layer.track] = combinedPriority;
                        bestLayerByTrack[layer.track]       = layer;       // Fix 4
                    }
                    else
                    {
                        // Higher volume / priority wins.
                        if (v > desiredVolumeByTrack[layer.track])
                        {
                            desiredVolumeByTrack[layer.track] = v;
                            bestLayerByTrack[layer.track]     = layer;     // Fix 4
                        }

                        if (combinedPriority > desiredPriorityByTrack[layer.track])
                            desiredPriorityByTrack[layer.track] = combinedPriority;
                    }
                }
            }

            Transform listener   = GetListenerTransform();
            Vector3 listenerPos  = listener != null ? listener.position : Vector3.zero;

            scoredCache.Clear();
            scoredCache.Capacity = Mathf.Max(scoredCache.Capacity, emitters.Count);

            for (int i = 0; i < emitters.Count; i++)
            {
                AmbientEmitter em = emitters[i];
                if (em == null || em.Track == null) continue;

                if (!desiredVolumeByTrack.TryGetValue(em.Track, out float baseVol01))
                {
                    scoredCache.Add(new ScoredEmitter
                    {
                        emitter       = em,
                        score         = float.NegativeInfinity,
                        targetVolume01 = 0f,
                        sourceLayer   = null
                    });
                    continue;
                }

                int   trackPriority = desiredPriorityByTrack.TryGetValue(em.Track, out int p) ? p : 0;
                float dist          = listener != null ? Vector3.Distance(listenerPos, em.transform.position) : 0f;
                float audibility    = 1f / (1f + dist);

                float score = (trackPriority * SCORE_WEIGHT_PRIORITY)
                            + (em.EmitterPriority * SCORE_WEIGHT_EMITTER)
                            + (baseVol01 * SCORE_WEIGHT_VOLUME)
                            + audibility;

                // Fix 4: look up the best layer for this track so overrides can be applied.
                bestLayerByTrack.TryGetValue(em.Track, out AmbientLayer bestLayer);

                scoredCache.Add(new ScoredEmitter
                {
                    emitter        = em,
                    score          = score,
                    targetVolume01 = baseVol01,
                    sourceLayer    = bestLayer     // Fix 4
                });
            }

            scoredCache.Sort((a, b) => b.score.CompareTo(a.score));

            int cap = Mathf.Clamp(maxAmbientVoices, 1, 128);
            allowedCache.Clear();

            if (allowMultipleEmittersPerTrack)
            {
                for (int i = 0; i < scoredCache.Count && allowedCache.Count < cap; i++)
                {
                    if (scoredCache[i].score == float.NegativeInfinity) continue;
                    allowedCache.Add(scoredCache[i].emitter);
                }
            }
            else
            {
                usedTracksCache.Clear();
                for (int i = 0; i < scoredCache.Count && allowedCache.Count < cap; i++)
                {
                    if (scoredCache[i].score == float.NegativeInfinity) continue;
                    AmbientTrack t = scoredCache[i].emitter.Track;
                    if (t == null || usedTracksCache.Contains(t)) continue;
                    usedTracksCache.Add(t);
                    allowedCache.Add(scoredCache[i].emitter);
                }
            }

            for (int i = 0; i < scoredCache.Count; i++)
            {
                AmbientEmitter em = scoredCache[i].emitter;
                if (em == null) continue;

                if (allowedCache.Contains(em))
                {
                    // Fix 4: forward layer playback overrides when the layer has them enabled.
                    AmbientLayer layer = scoredCache[i].sourceLayer;
                    bool?    randomStartOverride = (layer != null && layer.overridePlayback) ? layer.randomStartTime : (bool?)null;
                    Vector2? pitchOverride       = (layer != null && layer.overridePlayback) ? layer.pitchRange     : (Vector2?)null;

                    em.EnsurePlaying(ambientGroup, randomStartOverride, pitchOverride);
                    em.SetTargetVolume01(scoredCache[i].targetVolume01);
                }
                else
                {
                    em.SetTargetVolume01(0f);
                }
            }

            for (int i = 0; i < emitters.Count; i++)
            {
                AmbientEmitter em = emitters[i];
                if (em == null) continue;
                if (!allowedCache.Contains(em) && !desiredVolumeByTrack.ContainsKey(em.Track))
                    em.SetTargetVolume01(0f);
            }
        }

        #endregion

        #region Clip Name Helpers

        public bool TryGetSoundNames(out string[] names)
        {
            names = soundLibrary != null ? soundLibrary.GetAllClipNames() : null;
            return names != null && names.Length > 0;
        }

        public bool TryGetMusicNames(out string[] names)
        {
            names = musicLibrary != null ? musicLibrary.GetAllClipNames() : null;
            return names != null && names.Length > 0;
        }

        public bool TryGetAmbientNames(out string[] names)
        {
            names = ambientLibrary != null ? ambientLibrary.GetAllClipNames() : null;
            return names != null && names.Length > 0;
        }

        public void RebuildDictionaries()
        {
            soundLibrary?.RebuildDictionaries();
            musicLibrary?.RebuildDictionaries();
            ambientLibrary?.RebuildDictionaries();
        }

        #endregion

        #region Editor Auto-Assign

#if UNITY_EDITOR
        private void AutoAssignMixerAndGroups_EditorOnly()
        {
            if (mainMixer == null)
            {
                try
                {
                    string[] guids = AssetDatabase.FindAssets("t:AudioMixer");
                    if (guids != null && guids.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        mainMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
                    }
                }
                catch { }
            }

            if (mainMixer != null)
            {
                if (musicGroup   == null) musicGroup   = FindGroup(mainMixer, new[] { "Music",   "Master/Music" });
                if (ambientGroup == null) ambientGroup = FindGroup(mainMixer, new[] { "Ambient", "Master/Ambient", "Ambience" });
                if (fxGroup      == null) fxGroup      = FindGroup(mainMixer, new[] { "FX",      "SFX", "Master/FX", "Master/SFX" });
            }
        }

        private AudioMixerGroup FindGroup(AudioMixer mixer, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    var found = mixer.FindMatchingGroups(candidates[i]);
                    if (found != null && found.Length > 0) return found[0];
                }
                catch { }
            }

            try
            {
                var master = mixer.FindMatchingGroups("Master");
                if (master != null && master.Length > 0) return master[0];
            }
            catch { }

            return null;
        }
#endif

        #endregion

        #region Helpers

        private void GetLibraries()
        {
            if (soundLibrary   == null) soundLibrary   = GetComponent<SoundLibrary>();
            if (musicLibrary   == null) musicLibrary   = GetComponent<MusicLibrary>();
            if (ambientLibrary == null) ambientLibrary = GetComponent<AmbientLibrary>();
        }

        #endregion
    }
}
