﻿using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Audio;

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
        private const float SCORE_WEIGHT_EMITTER = 100f;
        private const float SCORE_WEIGHT_VOLUME = 10f;
        private const float SCORE_WEIGHT_DISTANCE = 1f;
        private const float VOLUME_EPSILON = 0.0001f;
        
        #region Types

        public enum AudioChannel
        {
            Master,
            Music,
            Ambient,
            FX
        }

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
        [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float ambientVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float fxVolume = 1f;

        [Header("Snapshots")]
        [SerializeField] private List<MixerSnapshotEntry> snapshots = new List<MixerSnapshotEntry>();
        private Dictionary<string, AudioMixerSnapshot> snapshotLookup;

        #endregion

        #region Music

        private Coroutine musicFadeCo;

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

        private readonly Dictionary<AmbientTrack, float> desiredVolumeByTrack = new();
        private readonly Dictionary<AmbientTrack, int> desiredPriorityByTrack = new();

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
        }

        private void ApplyMixerRouting()
        {
            if (fx2DSource != null) fx2DSource.outputAudioMixerGroup = fxGroup;
            if (musicSource != null) musicSource.outputAudioMixerGroup = musicGroup;
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
            SetVolume(musicVolume, AudioChannel.Music);
            SetVolume(ambientVolume, AudioChannel.Ambient);
            SetVolume(fxVolume, AudioChannel.FX);
        }

        public void SetVolume(float volume01, AudioChannel channel)
        {
            if (mainMixer == null) return;

            float db = Mathf.Log10(Mathf.Clamp(volume01, 0.0001f, 1f)) * 20f;

            switch (channel)
            {
                case AudioChannel.Master:
                    mainMixer.SetFloat("MasterVolume", db);
                    break;
                case AudioChannel.Music:
                    mainMixer.SetFloat("MusicVolume", db);
                    break;
                case AudioChannel.Ambient:
                    mainMixer.SetFloat("AmbientVolume", db);
                    break;
                case AudioChannel.FX:
                    mainMixer.SetFloat("FXVolume", db);
                    break;
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

            if (snapshotLookup == null)
                BuildSnapshotLookup();

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

            if (snapshots == null)
                return;

            for (int i = 0; i < snapshots.Count; i++)
            {
                MixerSnapshotEntry entry = snapshots[i];

                if (entry == null)
                    continue;

                if (string.IsNullOrWhiteSpace(entry.name))
                    continue;

                if (entry.snapshot == null)
                    continue;

                string key = entry.name.Trim();

                if (snapshotLookup.ContainsKey(key))
                    Debug.LogWarning($"AudioManager: Duplicate snapshot name '{key}'. The later entry will overwrite the earlier one.", this);

                snapshotLookup[key] = entry.snapshot;
            }
        }

        public string[] GetSnapshotNames()
        {
            if (snapshots == null || snapshots.Count == 0)
                return Array.Empty<string>();

            List<string> names = new List<string>(snapshots.Count);

            for (int i = 0; i < snapshots.Count; i++)
            {
                MixerSnapshotEntry entry = snapshots[i];

                if (entry == null)
                    continue;

                if (string.IsNullOrWhiteSpace(entry.name))
                    continue;

                if (entry.snapshot == null)
                    continue;

                names.Add(entry.name.Trim());
            }

            return names.ToArray();
        }

        #endregion

        #region SFX API

        public void PlaySfx2D(string soundName)
        {
            PlaySfx2D(soundName, 1f);
        }

        /// <summary>
        /// Plays a 2D sound effect.
        /// </summary>
        /// <param name="soundName">Name of the sound as defined in SoundLibrary.</param>
        /// <param name="volume">Volume multiplier (0-1). Uses clip's default if -1.</param>
        /// <returns>True if sound was found and played, false otherwise.</returns>
        public bool PlaySfx2D(string soundName, float volume = -1f)
        {
            if (soundLibrary == null)
            {
                Debug.LogWarning("[AudioManager] SoundLibrary not found. Cannot play SFX.");
                return false;
            }

            if (fx2DSource == null)
            {
                Debug.LogError("[AudioManager] FX 2D AudioSource is null. Cannot play sound.");
                return false;
            }

            AudioClip clip = soundLibrary.GetClipFromName(soundName);
            
            // ADDED: Null check (GetClipFromName already logs warning)
            if (clip == null)
                return false;

            float vol = volume < 0f ? 1f : Mathf.Clamp01(volume);
            fx2DSource.PlayOneShot(clip, vol);
            return true;
        }

        /// <summary>
        /// Plays a 3D sound effect at a world position.
        /// </summary>
        /// <param name="soundName">Name of the sound as defined in SoundLibrary.</param>
        /// <param name="position">World position to play the sound.</param>
        /// <param name="volume">Volume multiplier (0-1). Uses clip's default if -1.</param>
        /// <returns>True if sound was found and played, false otherwise.</returns>
        public bool PlaySfx3D(string soundName, Vector3 position, float volume = -1f)
        {
            if (soundLibrary == null)
            {
                Debug.LogWarning("[AudioManager] SoundLibrary not found. Cannot play SFX.");
                return false;
            }

            if (fxPool == null)
            {
                Debug.LogError("[AudioManager] FX Pool is null. Cannot play 3D sound.");
                return false;
            }

            AudioClip clip = soundLibrary.GetClipFromName(soundName);
            
            // ADDED: Null check
            if (clip == null)
                return false;

            float vol = volume < 0f ? 1f : Mathf.Clamp01(volume);
            fxPool.PlayClip(clip, position, vol);
            return true;
        }

        public void PlaySfx3D(string soundName, Vector3 position)
        {
            PlaySfx3D(soundName, position, 1f);
        }

        #endregion

        #region Music API
     
        /// <summary>
        /// Plays a music track with optional delay and fade-in.
        /// </summary>
        /// <param name="trackName">Name of the music track as defined in MusicLibrary.</param>
        /// <param name="startDelay">Delay before starting playback (seconds).</param>
        /// <param name="fadeDuration">Fade-in duration (seconds). 0 = instant.</param>
        /// <returns>True if music started successfully, false otherwise.</returns>
        public bool PlayMusic(string trackName, float startDelay = 0f, float fadeDuration = 0f)
        {
            if (musicLibrary == null)
            {
                Debug.LogWarning("[AudioManager] MusicLibrary not found. Cannot play music.");
                return false;
            }

            MusicTrack track = musicLibrary.GetTrackFromName(trackName);

            // ADDED: Null checks
            if (track == null)
            {
                Debug.LogWarning($"[AudioManager] Music track '{trackName}' not found in library.");
                return false;
            }

            if (track.clip == null)
            {
                Debug.LogWarning($"[AudioManager] Music track '{trackName}' has no AudioClip assigned.");
                return false;
            }

            if (musicSource == null)
            {
                Debug.LogError("[AudioManager] Music AudioSource is null. Cannot play music.");
                return false;
            }

            StopMusicFadeCoroutine();

            musicSource.clip = track.clip;
            musicSource.loop = track.loop;
            musicSource.volume = 0f;

            if (fadeDuration > 0f)
            {
                musicFadeCo = StartCoroutine(FadeInMusic(startDelay, fadeDuration));
            }
            else
            {
                if (startDelay > 0f)
                {
                    musicFadeCo = StartCoroutine(PlayAfterDelay(startDelay));
                }
                else
                {
                    musicSource.volume = 1f;
                    musicSource.Play();
                }
            }

            return true;
        }

        public void StopMusic(float fadeSeconds = 0f)
        {
            StopMusicFadeCoroutine();  // Clean up any running fade

            if (fadeSeconds <= 0f)
            {
                if (musicSource != null)
                {
                    musicSource.Stop();
                    musicSource.clip = null;
                }
            }
            else
            {
                musicFadeCo = StartCoroutine(FadeOutAndStop(fadeSeconds));
            }
        }

        private IEnumerator MusicFadeInCo(float delay, float duration)
        {
            duration = Mathf.Max(0.0001f, duration);

            musicSource.volume = 0f;
            musicSource.PlayDelayed(Mathf.Max(0f, delay));

            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / duration);
                musicSource.volume = t;
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
                float t = Mathf.Clamp01(time / duration);
                musicSource.volume = Mathf.Lerp(start, 0f, t);
                yield return null;
            }

            musicSource.Stop();
            musicSource.clip = null;
            musicSource.volume = start;
        }

        private void StopMusicFadeCoroutine()
        {
            if (musicFadeCo != null)
            {
                StopCoroutine(musicFadeCo);
                musicFadeCo = null;
            }
        }

        private IEnumerator FadeInMusic(float startDelay, float fadeDuration)
        {
            yield return MusicFadeInCo(startDelay, fadeDuration);
        }

        private IEnumerator PlayAfterDelay(float delay)
        {
            if (musicSource == null) yield break;
            if (delay > 0f) yield return new WaitForSeconds(delay);
            musicSource.volume = 1f;
            musicSource.Play();
        }

        private IEnumerator FadeOutAndStop(float fadeSeconds)
        {
            yield return MusicFadeOutCo(fadeSeconds);
        }

        #endregion

        #region Ambient API

        public void RegisterEmitter(AmbientEmitter emitter)
        {
            if (emitter == null) return;

            if (!emitters.Contains(emitter))
            {
                emitters.Add(emitter);
            }

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
                AmbientStackEntry e = new()
                {
                    token = ambientTokenCounter++,
                    profile = profile,
                    priority = 0,
                    fadeSeconds = f
                };
                ambientStack.Add(e);
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
                token = ambientTokenCounter++,
                profile = profile,
                priority = priority,
                fadeSeconds = f
            };
            ambientStack.Add(e);

            ambientCurrentFade = f;
            ForceRescoreNow();

            return e.token;
        }

        public void PopAmbientToken(int token, float fade = -1f)
        {
            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);

            for (int i = ambientStack.Count - 1; i >= 0; i--)
            {
                if (ambientStack[i].token == token)
                {
                    ambientStack.RemoveAt(i);
                    break;
                }
            }

            ambientCurrentFade = f;
            ForceRescoreNow();
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
            const float CLEANUP_INTERVAL = 5f;  // Clean up every 5 seconds

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
                    if (emitters[i] == null)
                    {
                        continue;
                    }

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

        private void CleanupNullEmitters()
        {
            emitters.RemoveAll(e => e == null);
        }

        private Transform GetListenerTransform()
        {
            if (listenerOverride != null)
                return listenerOverride;

            // Re-check periodically in case listener changed
            float now = Time.time;
            if (cachedListener == null || now - lastListenerCheckTime > LISTENER_RECHECK_INTERVAL)
            {
                cachedListener = FindAnyObjectByType<AudioListener>();
                cachedListenerTransform = cachedListener != null ? cachedListener.transform : null;
                lastListenerCheckTime = now;
            }

            return cachedListenerTransform;
        }

        private void ProcessAmbientStack()
        {
            desiredVolumeByTrack.Clear();
            desiredPriorityByTrack.Clear();

            for (int i = 0; i < ambientStack.Count; i++)
            {
                AmbientStackEntry entry = ambientStack[i];

                // ADDED: Null and empty validation
                if (entry == null || entry.profile == null)
                    continue;

                if (entry.profile.layers == null || entry.profile.layers.Length == 0)
                    continue;

                for (int j = 0; j < entry.profile.layers.Length; j++)
                {
                    AmbientLayer layer = entry.profile.layers[j];

                    // ADDED: Layer validation
                    if (layer == null || layer.track == null)
                        continue;

                    int combinedPriority = entry.priority + layer.priority;

                    if (!desiredPriorityByTrack.ContainsKey(layer.track))
                        desiredPriorityByTrack[layer.track] = combinedPriority;
                    else
                        desiredPriorityByTrack[layer.track] = Mathf.Max(desiredPriorityByTrack[layer.track], combinedPriority);

                    float v = Mathf.Clamp01(layer.volume);

                    if (!desiredVolumeByTrack.ContainsKey(layer.track))
                    {
                        desiredVolumeByTrack[layer.track] = v;
                        desiredPriorityByTrack[layer.track] = entry.priority;
                    }
                    else
                    {
                        desiredVolumeByTrack[layer.track] = Mathf.Max(desiredVolumeByTrack[layer.track], v);
                        desiredPriorityByTrack[layer.track] = Mathf.Max(desiredPriorityByTrack[layer.track], entry.priority);
                    }
                }
            }
        }

        private void ApplyAmbientTargets()
        {
            desiredVolumeByTrack.Clear();
            desiredPriorityByTrack.Clear();

            for (int s = 0; s < ambientStack.Count; s++)
            {
                AmbientStackEntry entry = ambientStack[s];

                if (entry == null || entry.profile == null || entry.profile.layers == null)
                {
                    continue;
                }

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

                    if (!desiredPriorityByTrack.ContainsKey(layer.track))
                        desiredPriorityByTrack[layer.track] = combinedPriority;
                    else
                        desiredPriorityByTrack[layer.track] = Mathf.Max(desiredPriorityByTrack[layer.track], combinedPriority);

                    float v = Mathf.Clamp01(layer.volume);

                    if (!desiredVolumeByTrack.ContainsKey(layer.track))
                    {
                        desiredVolumeByTrack[layer.track] = v;
                        desiredPriorityByTrack[layer.track] = entry.priority;
                    }
                    else
                    {
                        desiredVolumeByTrack[layer.track] = Mathf.Max(desiredVolumeByTrack[layer.track], v);
                        desiredPriorityByTrack[layer.track] = Mathf.Max(desiredPriorityByTrack[layer.track], entry.priority);
                    }
                }
            }

            Transform listener = GetListenerTransform();
            Vector3 listenerPos = listener != null ? listener.position : Vector3.zero;

            scoredCache.Clear();
            scoredCache.Capacity = Mathf.Max(scoredCache.Capacity, emitters.Count);

            for (int i = 0; i < emitters.Count; i++)
            {
                AmbientEmitter em = emitters[i];

                if (em == null || em.Track == null)
                {
                    continue;
                }

                if (!desiredVolumeByTrack.TryGetValue(em.Track, out float baseVol01))
                {
                    ScoredEmitter se = new()
                    {
                        emitter = em,
                        score = float.NegativeInfinity,
                        targetVolume01 = 0f
                    };
                    scoredCache.Add(se);
                    continue;
                }

                int trackPriority = desiredPriorityByTrack.TryGetValue(em.Track, out int p) ? p : 0;
                float dist = listener != null ? Vector3.Distance(listenerPos, em.transform.position) : 0f;
                float audibility = 1f / (1f + dist);

                float score = (trackPriority * SCORE_WEIGHT_PRIORITY) 
                    + (em.EmitterPriority * SCORE_WEIGHT_EMITTER) 
                    + (baseVol01 * SCORE_WEIGHT_VOLUME) 
                    + audibility;

                ScoredEmitter sEntry = new()
                {
                    emitter = em,
                    score = score,
                    targetVolume01 = baseVol01
                };
                scoredCache.Add(sEntry);
            }

            scoredCache.Sort((a, b) => b.score.CompareTo(a.score));

            int cap = Mathf.Clamp(maxAmbientVoices, 1, 128);

            allowedCache.Clear();

            if (allowMultipleEmittersPerTrack)
            {
                for (int i = 0; i < scoredCache.Count && allowedCache.Count < cap; i++)
                {
                    if (scoredCache[i].score == float.NegativeInfinity)
                    {
                        continue;
                    }

                    allowedCache.Add(scoredCache[i].emitter);
                }
            }
            else
            {
                usedTracksCache.Clear();

                for (int i = 0; i < scoredCache.Count && allowedCache.Count < cap; i++)
                {
                    if (scoredCache[i].score == float.NegativeInfinity)
                    {
                        continue;
                    }

                    AmbientTrack t = scoredCache[i].emitter.Track;

                    if (t == null)
                    {
                        continue;
                    }

                    if (usedTracksCache.Contains(t))
                    {
                        continue;
                    }

                    usedTracksCache.Add(t);
                    allowedCache.Add(scoredCache[i].emitter);
                }
            }

            for (int i = 0; i < scoredCache.Count; i++)
            {
                AmbientEmitter em = scoredCache[i].emitter;

                if (em == null)
                {
                    continue;
                }

                if (allowedCache.Contains(em))
                {
                    em.EnsurePlaying(ambientGroup);
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

                if (em == null)
                {
                    continue;
                }

                if (!allowedCache.Contains(em) && !desiredVolumeByTrack.ContainsKey(em.Track))
                {
                    em.SetTargetVolume01(0f);
                }
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
                catch
                {
                }
            }

            if (mainMixer != null)
            {
                if (musicGroup == null) musicGroup = FindGroup(mainMixer, new string[] { "Music", "Master/Music" });
                if (ambientGroup == null) ambientGroup = FindGroup(mainMixer, new string[] { "Ambient", "Master/Ambient", "Ambience" });
                if (fxGroup == null) fxGroup = FindGroup(mainMixer, new string[] { "FX", "SFX", "Master/FX", "Master/SFX" });
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
                catch
                {
                }
            }

            try
            {
                var master = mixer.FindMatchingGroups("Master");
                if (master != null && master.Length > 0) return master[0];
            }
            catch
            {
            }

            return null;
        }
#endif

        #endregion

        #region Helpers

        private void GetLibraries()
        {
            if (soundLibrary == null) soundLibrary = GetComponent<SoundLibrary>();
            if (musicLibrary == null) musicLibrary = GetComponent<MusicLibrary>();
            if (ambientLibrary == null) ambientLibrary = GetComponent<AmbientLibrary>();
        }

        #endregion
    }
}
