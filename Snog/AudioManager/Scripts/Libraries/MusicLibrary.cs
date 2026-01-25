﻿// MusicLibrary.cs (patched)
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
        [Header("ScriptableObject Music Clips")]
        public List<MusicTrack> tracks = new();

        private Dictionary<string, AudioClip> musicDictionary = new();
        private bool built = false;

        private void Awake()
        {
            // Build now for runtime usage
            BuildDictionary();
            built = true;
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

        // -------------------------
        // Public rebuild helpers
        // -------------------------
        /// <summary>
        /// Public API to force the library to rebuild (safe to call from editor code).
        /// Call this after creating/assigning new MusicTrack assets.
        /// </summary>
        public void RebuildDictionaries()
        {
            built = false;
            EnsureBuilt();

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            Debug.Log($"[MusicLibrary] RebuildDictionaries: found {musicDictionary.Count} tracks.");
#endif
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild Music Dictionary")]
        public void Editor_RebuildDictionary()
        {
            RebuildDictionaries();
        }

        // Run when edited in inspector — mark cache dirty so next query rebuilds.
        private void OnValidate()
        {
            built = false;
        }
#endif
    }
}
