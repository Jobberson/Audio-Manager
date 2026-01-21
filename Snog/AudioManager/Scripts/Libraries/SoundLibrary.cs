﻿// SoundLibrary.cs
using UnityEngine;
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

        private Dictionary<string, AudioClip[]> soundDict = new();
        private bool built = false;

        private void Awake()
        {
            BuildDictionary(); 
        }

        /// <summary>
        /// Ensure the internal dictionary is built. Safe to call in editor or runtime.
        /// </summary>
        private void EnsureBuilt()
        {
            if (built) return;
            BuildDictionary();

#if UNITY_EDITOR
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:SoundClipData");
                foreach (var g in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var so = AssetDatabase.LoadAssetAtPath<SoundClipData>(path);
                    if (so == null) continue;
                    if (string.IsNullOrEmpty(so.soundName)) continue;
                    if (so.clips == null || so.clips.Length == 0) continue;

                    if (!soundDict.ContainsKey(so.soundName))
                        soundDict[so.soundName] = so.clips;
                }
            }
            catch
            {
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
                    if (s == null) continue;
                    if (string.IsNullOrEmpty(s.soundName)) continue;
                    if (s.clips == null || s.clips.Length == 0) continue;
                    soundDict[s.soundName] = s.clips;
                }
            }

            if (inlineSounds != null)
            {
                foreach (var i in inlineSounds)
                {
                    if (i == null) continue;
                    if (string.IsNullOrEmpty(i.soundName)) continue;
                    if (i.clips == null || i.clips.Length == 0) continue;
                    soundDict[i.soundName] = i.clips;
                }
            }
        }

        public AudioClip GetClipFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureBuilt();

            if (soundDict.TryGetValue(name, out var clips) && clips != null && clips.Length > 0)
            {
                return clips[UnityEngine.Random.Range(0, clips.Length)];
            }

            return null;
        }

        public string[] GetAllClipNames()
        {
            EnsureBuilt();
            return soundDict.Keys.OrderBy(k => k).ToArray();
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild Sound Dictionary")]
        public void Editor_RebuildDictionary()
        {
            built = false;
            EnsureBuilt();
            EditorUtility.SetDirty(this);
        }
#endif
    }
}
