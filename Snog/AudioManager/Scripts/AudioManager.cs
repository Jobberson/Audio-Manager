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
        #region Types

        public enum AudioChannel
        {
            Master,
            Music,
            Ambient,
            FX
        }

        public enum SnapshotType
        {
            Default,
            Combat,
            Stealth,
            Underwater
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

        #endregion

        #region Mixer / Volumes

        [Header("Mixer")]
        [SerializeField] private AudioMixer mainMixer;
        [SerializeField] private AudioMixerGroup musicGroup;
        [SerializeField] private AudioMixerGroup ambientGroup;
        [SerializeField] private AudioMixerGroup fxGroup;

        [Header("Snapshots")]
        [SerializeField] private AudioMixerSnapshot defaultSnapshot;
        [SerializeField] private AudioMixerSnapshot combatSnapshot;
        [SerializeField] private AudioMixerSnapshot stealthSnapshot;
        [SerializeField] private AudioMixerSnapshot underwaterSnapshot;

        [Header("Volumes (0..1)")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float ambientVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float fxVolume = 1f;

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

        private readonly List<ScoredEmitter> scoredCache = new List<ScoredEmitter>(64);
        private readonly HashSet<AmbientEmitter> allowedCache = new HashSet<AmbientEmitter>();
        private readonly HashSet<AmbientTrack> usedTracksCache = new HashSet<AmbientTrack>();

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

        private readonly List<AmbientEmitter> emitters = new List<AmbientEmitter>();
        private readonly List<AmbientStackEntry> ambientStack = new List<AmbientStackEntry>();

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

            StartAmbientLoopIfNeeded();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Ensure library component references exist while editing
            GetLibraries();
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

        public void TransitionToSnapshot(SnapshotType snapshot, float transitionTime)
        {
            switch (snapshot)
            {
                case SnapshotType.Default:
                    if (defaultSnapshot != null) defaultSnapshot.TransitionTo(transitionTime);
                    break;
                case SnapshotType.Combat:
                    if (combatSnapshot != null) combatSnapshot.TransitionTo(transitionTime);
                    break;
                case SnapshotType.Stealth:
                    if (stealthSnapshot != null) stealthSnapshot.TransitionTo(transitionTime);
                    break;
                case SnapshotType.Underwater:
                    if (underwaterSnapshot != null) underwaterSnapshot.TransitionTo(transitionTime);
                    break;
            }
        }

        #endregion

        #region SFX API

        public void PlaySfx2D(string soundName)
        {
            if (soundLibrary == null || fx2DSource == null) 
            {
                Debug.LogWarning("AudioManager: SoundLibrary or fx2DSource is null. Cannot play SFX 2D.");
                GetLibraries();
                return;
            }

            InitializeFxPoolIfNeeded();

            AudioClip clip = soundLibrary.GetClipFromName(soundName);
            if (clip == null) return;

            fx2DSource.PlayOneShot(clip);
        }

        public void PlaySfx3D(string soundName, Vector3 position, float volume)
        {
            if (soundLibrary == null)
            {
                return;
            }

            InitializeFxPoolIfNeeded();

            AudioClip clip = soundLibrary.GetClipFromName(soundName);
            if (clip == null)
            {
                return;
            }

            fxPool.PlayClip(clip, position, Mathf.Clamp01(volume));
        }

        // two argument overload for PlaySfx3D with default volume of 1f
        public void PlaySfx3D(string soundName, Vector3 position)
        {
            PlaySfx3D(soundName, position, 1f);
        }

        #endregion

        #region Music API
     
        public void PlayMusic(string trackName, float delay = 0f, float fadeIn = 0f)
        {
            if (musicLibrary == null || musicSource == null)
            {
                return;
            }

            MusicTrack track = musicLibrary.GetTrackFromName(trackName);
            if (track == null || track.clip == null)
            {
                return;
            }

            musicSource.clip = track.clip;
            musicSource.loop = track.loop;

            ApplyMixerRouting();

            if (fadeIn > 0f)
            {
                StartCoroutine(MusicFadeInCo(delay, fadeIn));
            }
            else
            {
                musicSource.volume = 1f;
                musicSource.PlayDelayed(Mathf.Max(0f, delay));
            }
        }

        public void StopMusic(float fadeOut = 0f)
        {
            if (musicSource == null) return;

            if (fadeOut > 0f)
            {
                StartCoroutine(MusicFadeOutCo(fadeOut));
            }
            else
            {
                musicSource.Stop();
                musicSource.clip = null;
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
            Debug.Log($"[AudioManager] SetAmbientProfile called -> {(profile != null ? profile.name : "null")}", profile);

            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);

            ambientStack.Clear();

            if (profile != null)
            {
                AmbientStackEntry e = new AmbientStackEntry();
                e.token = ambientTokenCounter++;
                e.profile = profile;
                e.priority = 0;
                e.fadeSeconds = f;
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

            AmbientStackEntry e = new AmbientStackEntry();
            e.token = ambientTokenCounter++;
            e.profile = profile;
            e.priority = priority;
            e.fadeSeconds = f;
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
            while (true)
            {
                CleanupEmitters();

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

        private void CleanupEmitters()
        {
            for (int i = emitters.Count - 1; i >= 0; i--)
            {
                if (emitters[i] == null)
                {
                    emitters.RemoveAt(i);
                }
            }
        }

        private Transform GetListenerTransform()
        {
            if (listenerOverride != null) return listenerOverride;

#if UNITY_2023_1_OR_NEWER
            AudioListener listener = FindFirstObjectByType<AudioListener>();
#else
          AudioListener listener = FindObjectOfType<AudioListener>();
#endif

            if (listener != null) return listener.transform;

            if (Camera.main != null) return Camera.main.transform;

            return null;
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
                        Debug.LogWarning("[AudioManager] AmbientLayer is null in profile.", entry.profile);
                        continue;
                    }

                    if (layer.track == null)
                    {
                        Debug.LogWarning($"[AudioManager] AmbientLayer has no track in profile '{entry.profile.name}'.", entry.profile);
                        continue;
                    }

                    if (layer.track.clip == null)
                    {
                        Debug.LogWarning($"[AudioManager] AmbientTrack '{layer.track.name}' has no clip assigned.", layer.track);
                        continue;
                    }

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
                    ScoredEmitter se = new ScoredEmitter();
                    se.emitter = em;
                    se.score = float.NegativeInfinity;
                    se.targetVolume01 = 0f;
                    scoredCache.Add(se);
                    continue;
                }

                int trackPriority = desiredPriorityByTrack.TryGetValue(em.Track, out int p) ? p : 0;
                float dist = listener != null ? Vector3.Distance(listenerPos, em.transform.position) : 0f;
                float audibility = 1f / (1f + dist);

                float score = (trackPriority * 1000f) + (em.EmitterPriority * 100f) + (baseVol01 * 10f) + audibility;

                ScoredEmitter sEntry = new ScoredEmitter();
                sEntry.emitter = em;
                sEntry.score = score;
                sEntry.targetVolume01 = baseVol01;
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

                if (defaultSnapshot == null) defaultSnapshot = mainMixer.FindSnapshot("Default");
                if (combatSnapshot == null) combatSnapshot = mainMixer.FindSnapshot("Combat");
                if (stealthSnapshot == null) stealthSnapshot = mainMixer.FindSnapshot("Stealth");
                if (underwaterSnapshot == null) underwaterSnapshot = mainMixer.FindSnapshot("Underwater");
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
