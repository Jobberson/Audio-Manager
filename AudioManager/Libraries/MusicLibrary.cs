﻿// MusicLibrary.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Snog.Audio.Clips;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Snog.Audio.Libraries
{
    public class MusicLibrary : MonoBehaviour
    {
        [Header("Music Clips (ScriptableObjects preferred)")]
        public List<MusicTrack> tracks = new();

        private Dictionary<string, AudioClip> musicDictionary = new();
        private bool built = false;

        private void Awake()
        {
            BuildDictionary();
            built = true;
        }

        private void EnsureBuilt()
        {
            if (built) return;
            BuildDictionary();

#if UNITY_EDITOR
            // Editor: also include any MusicTrack assets in project
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:MusicTrack");
                foreach (var g in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(g);
                    var asset = AssetDatabase.LoadAssetAtPath<MusicTrack>(path);
                    if (asset == null) continue;
                    if (string.IsNullOrEmpty(asset.trackName)) continue;
                    if (asset.clip == null) continue;
                    if (!musicDictionary.ContainsKey(asset.trackName))
                        musicDictionary[asset.trackName] = asset.clip;
                }
            }
            catch { }
#endif

            built = true;
        }

        private void BuildDictionary()
        {
            musicDictionary.Clear();

            if (tracks != null)
            {
                foreach (var m in tracks)
                {
                    if (m == null) continue;
                    if (string.IsNullOrEmpty(m.trackName)) continue;
                    if (m.clip == null) continue;
                    musicDictionary[m.trackName] = m.clip;
                }
            }
        }

        public AudioClip GetClipFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureBuilt();
            if (musicDictionary.TryGetValue(name, out var clip)) return clip;
            return null;
        }

        public string[] GetAllClipNames()
        {
            EnsureBuilt();
            return musicDictionary.Keys.OrderBy(k => k).ToArray();
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild Music Dictionary (Editor)")]
        public void Editor_RebuildDictionary()
        {
            built = false;
            EnsureBuilt();
            EditorUtility.SetDirty(this);
        }
#endif
    }
}
