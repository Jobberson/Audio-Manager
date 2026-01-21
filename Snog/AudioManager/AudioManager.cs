﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;
using Snog.Audio.Libraries;
using Snog.Audio.Clips;
using Snog.Audio.Layers;

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
        #region Public Types

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

        #region Music / FX

        [Header("Music")]
        [SerializeField] private bool musicLoop = true;

        [Header("3D FX Pool")]
        [SerializeField] private int fxPoolSize = 10;
        [SerializeField] private AudioSourcePool fxPool;

        #endregion

        #region Ambient System

        [Header("Ambient System")]
        [Tooltip("Maximum simultaneous ambient voices (AudioSources) used to render layers.")]
        [SerializeField] private int maxAmbientVoices = 16;

        [Tooltip("Default fade used when no fade time is supplied by caller.")]
        [SerializeField] private float defaultAmbientFade = 2f;

        #endregion

        #region Libraries

        private SoundLibrary soundLibrary;
        private MusicLibrary musicLibrary;
        private AmbientLibrary ambientLibrary;

        #endregion

        #region Sources

        private AudioSource musicSource;
        private AudioSource fx2DSource;

        #endregion

        #region Ambient Bus

        private AmbientBus ambientBus;

        #endregion

        #region Unity Methods

        protected override void Awake()
        {
            base.Awake();

            soundLibrary = GetComponent<SoundLibrary>();
            musicLibrary = GetComponent<MusicLibrary>();
            ambientLibrary = GetComponent<AmbientLibrary>();

#if UNITY_EDITOR
            AutoAssignMixerAndGroups_EditorOnly();
#endif

            CreateCoreSources();
            ApplyMixerRouting();
            ApplyAllMixerVolumes();

            InitializeFxPoolIfNeeded();
            InitializeAmbientBus();
        }

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
            musicSource.loop = musicLoop;
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

            // Asset-store friendly: try to call Initialize if present, otherwise just assign fields.
            // (Keeps this AudioManager standalone even if the pool changes.)
            var init = fxPool.GetType().GetMethod("Initialize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (init != null)
            {
                init.Invoke(fxPool, new object[] { fxPoolSize, fxGroup });
            }
            else
            {
                fxPool.poolSize = fxPoolSize;
                fxPool.fxGroup = fxGroup;
            }
        }

        private void InitializeAmbientBus()
        {
            if (ambientBus != null) return;

            GameObject go = new GameObject("Ambient Bus");
            go.transform.parent = transform;

            ambientBus = go.AddComponent<AmbientBus>();
            ambientBus.Configure(ambientGroup, Mathf.Clamp(maxAmbientVoices, 1, 128));
            ambientBus.SetGain(GetAmbientGain());
        }

        #endregion

        #region Mixer Volumes

        private void ApplyAllMixerVolumes()
        {
            SetVolume(masterVolume, AudioChannel.Master);
            SetVolume(musicVolume, AudioChannel.Music);
            SetVolume(ambientVolume, AudioChannel.Ambient);
            SetVolume(fxVolume, AudioChannel.FX);

            if (ambientBus != null)
            {
                ambientBus.SetGain(GetAmbientGain());
            }
        }

        private float GetAmbientGain()
        {
            return Mathf.Clamp01(masterVolume) * Mathf.Clamp01(ambientVolume);
        }

        public void SetVolume(float volume01, AudioChannel channel)
        {
            if (mainMixer == null)
            {
                return;
            }

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
                    if (ambientBus != null) ambientBus.SetGain(GetAmbientGain());
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
            if (soundLibrary == null) return;

            AudioClip clip = soundLibrary.GetClipFromName(soundName);
            if (clip == null) return;

            if (fx2DSource == null) return;

            fx2DSource.PlayOneShot(clip, Mathf.Clamp01(fxVolume) * Mathf.Clamp01(masterVolume));
        }

        public void PlaySfx3D(string soundName, Vector3 position)
        {
            if (soundLibrary == null) return;

            AudioClip clip = soundLibrary.GetClipFromName(soundName);
            if (clip == null) return;

            if (fxPool == null) return;

            fxPool.PlayClip(clip, position, Mathf.Clamp01(fxVolume) * Mathf.Clamp01(masterVolume));
        }

        #endregion

        #region Music API

        public void PlayMusic(string trackName, float delay = 0f, float fadeIn = 0f)
        {
            if (musicLibrary == null || musicSource == null) return;

            AudioClip clip = musicLibrary.GetClipFromName(trackName);
            if (clip == null) return;

            musicSource.loop = musicLoop;
            musicSource.clip = clip;

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

        /// <summary>
        /// Replace the entire ambient stack with a single profile (simple mode).
        /// </summary>
        public void SetAmbientProfile(AmbientProfile profile, float fade = -1f)
        {
            InitializeAmbientBus();

            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);
            ambientBus.SetSingleProfile(profile, f);
        }

        /// <summary>
        /// Clears all ambience (fades out whatever is active).
        /// </summary>
        public void ClearAmbient(float fade = -1f)
        {
            InitializeAmbientBus();

            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);
            ambientBus.Clear(f);
        }

        /// <summary>
        /// Push a profile onto the ambient stack. Higher priority wins voice allocation when budget is exceeded.
        /// Returns a token you can store to pop later.
        /// </summary>
        public int PushAmbientProfile(AmbientProfile profile, int priority = 0, float fade = -1f)
        {
            InitializeAmbientBus();

            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);
            return ambientBus.Push(profile, priority, f);
        }

        /// <summary>
        /// Pops a previously pushed profile token.
        /// </summary>
        public void PopAmbientToken(int token, float fade = -1f)
        {
            InitializeAmbientBus();

            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);
            ambientBus.Pop(token, f);
        }

        /// <summary>
        /// Convenience: remove the last stack entry matching this profile.
        /// </summary>
        public void PopAmbientProfile(AmbientProfile profile, float fade = -1f)
        {
            InitializeAmbientBus();

            float f = fade < 0f ? defaultAmbientFade : Mathf.Max(0f, fade);
            ambientBus.PopProfile(profile, f);
        }

        /// <summary>
        /// Debug helper.
        /// </summary>
        public bool TryGetCurrentAmbientStack(out int count)
        {
            if (ambientBus == null)
            {
                count = 0;
                return false;
            }

            count = ambientBus.StackCount;
            return true;
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
//
//
// ===========================================================================================
//
//
        #region AmbientBus Implementation

        private sealed class AmbientBus : MonoBehaviour
        {
            private sealed class StackEntry
            {
                public int token;
                public AmbientProfile profile;
                public int priority;
            }

            private struct LayerKey : IEquatable<LayerKey>
            {
                public int token;
                public int layerIndex;

                public bool Equals(LayerKey other)
                {
                    return token == other.token && layerIndex == other.layerIndex;
                }

                public override bool Equals(object obj)
                {
                    return obj is LayerKey other && Equals(other);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        return (token * 397) ^ layerIndex;
                    }
                }
            }

            private sealed class LayerState
            {
                public LayerKey key;
                public AudioClip clip;
                public bool loop;
                public float spatialBlend;
                public bool randomStart;
                public Vector2 pitchRange;

                public float baseVolume01;
                public float targetVolume;
                public float currentVolume;

                public int priority;
            }

            private sealed class Voice
            {
                public AudioSource source;
                public LayerKey boundKey;
                public bool bound;
            }

            public int StackCount => stack.Count;

            private AudioMixerGroup outputGroup;
            private int voiceCap;
            private float gain = 1f;

            private readonly List<StackEntry> stack = new();
            private readonly Dictionary<LayerKey, LayerState> layers = new();

            private readonly List<Voice> voices = new();

            private Coroutine transitionCo;
            private int tokenCounter = 1;

            public void Configure(AudioMixerGroup group, int maxVoices)
            {
                outputGroup = group;
                voiceCap = Mathf.Clamp(maxVoices, 1, 128);
                EnsureVoices(voiceCap);
            }

            public void SetGain(float newGain)
            {
                gain = Mathf.Clamp01(newGain);

                for (int i = 0; i < voices.Count; i++)
                {
                    if (!voices[i].bound) continue;
                    voices[i].source.volume = layers.TryGetValue(voices[i].boundKey, out var st) ? (st.currentVolume * gain) : 0f;
                }
            }

            public void SetSingleProfile(AmbientProfile profile, float fade)
            {
                stack.Clear();
                layers.Clear();

                if (profile != null)
                {
                    var e = new StackEntry();
                    e.token = tokenCounter++;
                    e.profile = profile;
                    e.priority = 0;
                    stack.Add(e);
                }

                RebuildLayersFromStack();
                StartTransition(fade);
            }

            public int Push(AmbientProfile profile, int priority, float fade)
            {
                if (profile == null)
                {
                    return -1;
                }

                var e = new StackEntry();
                e.token = tokenCounter++;
                e.profile = profile;
                e.priority = priority;
                stack.Add(e);

                RebuildLayersFromStack();
                StartTransition(fade);

                return e.token;
            }

            public void Pop(int token, float fade)
            {
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i].token == token)
                    {
                        stack.RemoveAt(i);
                        break;
                    }
                }

                RebuildLayersFromStack();
                StartTransition(fade);
            }

            public void PopProfile(AmbientProfile profile, float fade)
            {
                if (profile == null) return;

                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i].profile == profile)
                    {
                        stack.RemoveAt(i);
                        break;
                    }
                }

                RebuildLayersFromStack();
                StartTransition(fade);
            }

            public void Clear(float fade)
            {
                stack.Clear();
                RebuildLayersFromStack();
                StartTransition(fade);
            }

            private void RebuildLayersFromStack()
            {
                // Build the "desired" set of layer states from stack.
                // Keep existing LayerState objects where possible for smooth fades.
                var desiredKeys = new HashSet<LayerKey>();

                for (int s = 0; s < stack.Count; s++)
                {
                    var entry = stack[s];
                    var profile = entry.profile;
                    if (profile == null || profile.layers == null) continue;

                    for (int i = 0; i < profile.layers.Length; i++)
                    {
                        AmbientLayer layer = profile.layers[i];
                        if (layer == null || layer.track == null || layer.track.clip == null) continue;

                        var key = new LayerKey { token = entry.token, layerIndex = i };
                        desiredKeys.Add(key);

                        if (!layers.TryGetValue(key, out LayerState st))
                        {
                            st = new LayerState();
                            st.key = key;
                            st.currentVolume = 0f;
                            layers.Add(key, st);
                        }

                        st.clip = layer.track.clip;
                        st.loop = layer.loop;
                        st.spatialBlend = layer.spatialBlend;
                        st.randomStart = layer.randomStartTime;
                        st.pitchRange = layer.pitchRange;
                        st.baseVolume01 = Mathf.Clamp01(layer.volume);
                        st.priority = entry.priority;

                        // Set desired target. Voice allocation happens later.
                        st.targetVolume = st.baseVolume01;
                    }
                }

                // Any existing layer not desired should fade out
                var toFadeOut = new List<LayerKey>();

                foreach (var kv in layers)
                {
                    if (!desiredKeys.Contains(kv.Key))
                    {
                        kv.Value.targetVolume = 0f;
                        toFadeOut.Add(kv.Key);
                    }
                }
            }

            private void StartTransition(float fade)
            {
                if (transitionCo != null)
                {
                    StopCoroutine(transitionCo);
                    transitionCo = null;
                }

                transitionCo = StartCoroutine(TransitionCo(Mathf.Max(0f, fade)));
            }

            private IEnumerator TransitionCo(float fade)
            {
                EnsureVoices(voiceCap);

                // Choose which layers get voices (best N)
                var chosen = ChooseTopLayers(voiceCap);

                // Any layer not chosen should be forced to targetVolume 0 for this transition
                var chosenSet = new HashSet<LayerKey>();
                for (int i = 0; i < chosen.Count; i++)
                {
                    chosenSet.Add(chosen[i].key);
                }

                foreach (var kv in layers)
                {
                    if (!chosenSet.Contains(kv.Key))
                    {
                        kv.Value.targetVolume = 0f;
                    }
                    else
                    {
                        kv.Value.targetVolume = kv.Value.baseVolume01;
                    }
                }

                // Bind chosen layers to voices
                for (int i = 0; i < voices.Count; i++)
                {
                    if (i < chosen.Count)
                    {
                        BindVoice(voices[i], chosen[i]);
                    }
                    else
                    {
                        UnbindVoice(voices[i]);
                    }
                }

                // Fade
                if (fade <= 0f)
                {
                    SnapToTargets();
                    CleanupSilentLayers();
                    transitionCo = null;
                    yield break;
                }

                float time = 0f;
                while (time < fade)
                {
                    time += Time.deltaTime;
                    float t = Mathf.Clamp01(time / fade);

                    for (int i = 0; i < voices.Count; i++)
                    {
                        if (!voices[i].bound) continue;

                        if (layers.TryGetValue(voices[i].boundKey, out LayerState st))
                        {
                            st.currentVolume = Mathf.Lerp(st.currentVolume, st.targetVolume, t);
                            voices[i].source.volume = st.currentVolume * gain;
                        }
                    }

                    yield return null;
                }

                SnapToTargets();
                CleanupSilentLayers();
                transitionCo = null;
            }

            private void SnapToTargets()
            {
                foreach (var kv in layers)
                {
                    kv.Value.currentVolume = kv.Value.targetVolume;
                }

                for (int i = 0; i < voices.Count; i++)
                {
                    if (!voices[i].bound)
                    {
                        voices[i].source.volume = 0f;
                        continue;
                    }

                    if (layers.TryGetValue(voices[i].boundKey, out LayerState st))
                    {
                        voices[i].source.volume = st.currentVolume * gain;
                        if (st.currentVolume <= 0f)
                        {
                            UnbindVoice(voices[i]);
                        }
                    }
                    else
                    {
                        UnbindVoice(voices[i]);
                    }
                }
            }

            private void CleanupSilentLayers()
            {
                var remove = new List<LayerKey>();

                foreach (var kv in layers)
                {
                    if (kv.Value.currentVolume <= 0.0001f && kv.Value.targetVolume <= 0.0001f)
                    {
                        remove.Add(kv.Key);
                    }
                }

                for (int i = 0; i < remove.Count; i++)
                {
                    layers.Remove(remove[i]);
                }
            }

            private List<LayerState> ChooseTopLayers(int count)
            {
                var list = new List<LayerState>(layers.Values);

                list.Sort((a, b) =>
                {
                    int p = b.priority.CompareTo(a.priority);
                    if (p != 0) return p;
                    return b.baseVolume01.CompareTo(a.baseVolume01);
                });

                if (list.Count > count)
                {
                    list.RemoveRange(count, list.Count - count);
                }

                return list;
            }

            private void BindVoice(Voice voice, LayerState st)
            {
                if (voice.source == null) return;

                bool needsRebind = !voice.bound || !voice.boundKey.Equals(st.key) || voice.source.clip != st.clip;

                voice.bound = true;
                voice.boundKey = st.key;

                if (needsRebind)
                {
                    voice.source.outputAudioMixerGroup = outputGroup;
                    voice.source.playOnAwake = false;

                    voice.source.clip = st.clip;
                    voice.source.loop = st.loop;
                    voice.source.spatialBlend = st.spatialBlend;

                    if (st.randomStart && voice.source.clip != null && voice.source.clip.length > 0f)
                    {
                        voice.source.time = UnityEngine.Random.Range(0f, voice.source.clip.length);
                    }

                    if (st.pitchRange.x != st.pitchRange.y)
                    {
                        voice.source.pitch = UnityEngine.Random.Range(st.pitchRange.x, st.pitchRange.y);
                    }
                    else
                    {
                        voice.source.pitch = st.pitchRange.x;
                    }

                    if (!voice.source.isPlaying)
                    {
                        voice.source.volume = st.currentVolume * gain;
                        voice.source.Play();
                    }
                }
            }

            private void UnbindVoice(Voice voice)
            {
                if (voice.source == null) return;

                voice.source.Stop();
                voice.source.clip = null;
                voice.source.volume = 0f;

                voice.bound = false;
            }

            private void EnsureVoices(int needed)
            {
                needed = Mathf.Clamp(needed, 1, 128);

                while (voices.Count < needed)
                {
                    GameObject go = new GameObject($"AmbientVoice_{voices.Count}");
                    go.transform.parent = transform;

                    AudioSource src = go.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.loop = true;
                    src.volume = 0f;
                    src.outputAudioMixerGroup = outputGroup;

                    var v = new Voice();
                    v.source = src;
                    v.bound = false;

                    voices.Add(v);
                }

                for (int i = needed; i < voices.Count; i++)
                {
                    UnbindVoice(voices[i]);
                }
            }
        }

        #endregion
    }
}