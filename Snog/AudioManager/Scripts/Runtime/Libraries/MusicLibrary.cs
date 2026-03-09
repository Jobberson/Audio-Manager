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
    public class MusicLibrary : MonoBehaviour
    {
        [Header("ScriptableObject Music Clips")]
        public List<MusicTrack> tracks = new();
        
        // FIXED: Case-insensitive dictionary
        private Dictionary<string, MusicTrack> musicDictionary = new(StringComparer.OrdinalIgnoreCase);
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
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:MusicTrack");
                foreach (var g in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(g);
                    var asset = AssetDatabase.LoadAssetAtPath<MusicTrack>(path);

                    if (asset == null)
                        continue;

                    if (string.IsNullOrEmpty(asset.trackName))
                        continue;

                    if (asset.clip == null)
                        continue;

                    // FIXED: Use normalized key
                    string key = NormalizeKey(asset.trackName);
                    
                    if (!musicDictionary.ContainsKey(key))
                    {
                        musicDictionary[key] = asset;
                    }
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
            musicDictionary.Clear();

            if (tracks != null)
            {
                foreach (var m in tracks)
                {
                    if (m == null)
                        continue;

                    if (string.IsNullOrEmpty(m.trackName))
                        continue;

                    if (m.clip == null)
                        continue;

                    // FIXED: Use normalized key for both check and insertion
                    string key = NormalizeKey(m.trackName);

                    if (musicDictionary.ContainsKey(key))
                    {
                        Debug.LogWarning($"[MusicLibrary] Duplicate trackName '{m.trackName}' found. Overwriting previous entry.", this);
                    }

                    musicDictionary[key] = m;
                }
            }
        }

        public MusicTrack GetTrackFromName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            EnsureBuilt();

            // FIXED: Use normalized key for lookup
            string key = NormalizeKey(name);

            if (musicDictionary.TryGetValue(key, out var track))
            {
                return track;
            }

            // ADDED: Warning when track not found
            Debug.LogWarning($"[MusicLibrary] Music track '{name}' not found in library.");
            return null;
        }

        public AudioClip GetClipFromName(string name)
        {
            MusicTrack track = GetTrackFromName(name);
            return track != null ? track.clip : null;
        }

        public string[] GetAllClipNames()
        {
            EnsureBuilt();
            return musicDictionary.Keys.OrderBy(k => k).ToArray();
        }

        public void RebuildDictionaries()
        {
            built = false;
            EnsureBuilt();
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            Debug.Log($"[MusicLibrary] RebuildDictionaries: found {musicDictionary.Count} tracks.");
#endif
        }

        // ADDED: Key normalization method
        private string NormalizeKey(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            return raw.Trim().ToLowerInvariant();
        }

        // ADDED: Helper to check if track exists without warnings
        public bool HasTrack(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            EnsureBuilt();
            string key = NormalizeKey(name);
            return musicDictionary.ContainsKey(key);
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild Music Dictionary")]
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