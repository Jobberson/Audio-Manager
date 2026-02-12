﻿using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Snog.Audio.Clips;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Snog.Audio.Libraries
{
    [Serializable]
    public class InlineSoundData
    {
        public string soundName;
        public AudioClip[] clips;
    }

    public class SoundLibrary : MonoBehaviour
    {
        [Header("ScriptableObject sound data ")]
        public List<SoundClipData> tracks = new();

        [Header("Inline sound data (quick edit)")]
        public InlineSoundData[] inlineSounds;

        private Dictionary<string, AudioClip[]> soundDict = new(StringComparer.OrdinalIgnoreCase);
        private bool built = false;

        private void Awake()
        {
            BuildDictionary();
            built = true;
        }

        private void EnsureBuilt()
        {
            if (built)
                return;

            BuildDictionary();

#if UNITY_EDITOR
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:SoundClipData");
                foreach (var g in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var so = AssetDatabase.LoadAssetAtPath<SoundClipData>(path);

                    if (so == null)
                        continue;

                    if (string.IsNullOrEmpty(so.soundName))
                        continue;

                    if (so.clips == null || so.clips.Length == 0)
                        continue;

                    string key = NormalizeKey(so.soundName);

                    if (!soundDict.ContainsKey(key))
                        soundDict[key] = so.clips;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
#endif

            built = true;
        }

        private void BuildDictionary()
        {
            soundDict.Clear();

            if (tracks != null)
            {
                foreach (var s in tracks)
                {
                    if (s == null)
                        continue;

                    if (string.IsNullOrEmpty(s.soundName))
                        continue;

                    if (s.clips == null || s.clips.Length == 0)
                        continue;

                    string key = NormalizeKey(s.soundName);

                    if (soundDict.ContainsKey(s.soundName))
                        Debug.LogWarning($"[SoundLibrary] Duplicate soundName '{s.soundName}' from ScriptableObject list. Overwriting previous entry.", this);

                    soundDict[s.soundName] = s.clips;
                }
            }

            if (inlineSounds != null)
            {
                foreach (var i in inlineSounds)
                {
                    if (i == null)
                        continue;

                    if (string.IsNullOrEmpty(i.soundName))
                        continue;

                    if(i.clips == null || i.clips.Length == 0)
                        continue;

                    string key = NormalizeKey(i.soundName);

                    if (soundDict.ContainsKey(i.soundName))
                        Debug.LogWarning($"[SoundLibrary] Inline sound '{i.soundName}' is overriding an existing entry (likely from ScriptableObjects).", this);

                    soundDict[i.soundName] = i.clips;
                }
            }
        }

        public AudioClip GetClipFromName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            EnsureBuilt();

            string key = NormalizeKey(name);

            if(soundDict.TryGetValue(key, out var clips) && clips != null && clips.Length > 0)
                return clips[UnityEngine.Random.Range(0, clips.Length)];

            return null;
        }

        public string[] GetAllClipNames()
        {
            EnsureBuilt();
            return soundDict.Keys.OrderBy(k => k).ToArray();
        }

        public void RebuildDictionaries()
        {
            built = false;
            EnsureBuilt();

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        private string NormalizeKey(string raw)
        {
            return raw.Trim().ToLowerInvariant();
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild Sound Dictionary")]
        public void Editor_RebuildDictionary()
        {
            RebuildDictionaries();
        }

        private void OnValidate()
        {
            built = false;
        }
#endif
    }
}