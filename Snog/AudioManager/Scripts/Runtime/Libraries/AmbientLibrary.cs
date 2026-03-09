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
    public class AmbientLibrary : MonoBehaviour
    {
        [Header("ScriptableObject Ambient Clips")]
        public List<AmbientTrack> tracks = new();
        
        // FIXED: Case-insensitive dictionary
        private Dictionary<string, AudioClip> ambientDictionary = new(StringComparer.OrdinalIgnoreCase);
        private bool built = false;

        private void Awake()
        {
            // Build now for runtime usage; mark built so runtime calls are fast.
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
                string[] guids = AssetDatabase.FindAssets("t:AmbientTrack");
                foreach (var g in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(g);
                    var asset = AssetDatabase.LoadAssetAtPath<AmbientTrack>(path);

                    if (asset == null)
                        continue;

                    if (string.IsNullOrEmpty(asset.trackName))
                        continue;

                    if (asset.clip == null)
                        continue;

                    // FIXED: Use normalized key
                    string key = NormalizeKey(asset.trackName);

                    if (!ambientDictionary.ContainsKey(key))
                    {
                        ambientDictionary[key] = asset.clip;
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
            ambientDictionary.Clear();

            if (tracks != null)
            {
                foreach (var a in tracks)
                {
                    if (a == null)
                        continue;

                    if (string.IsNullOrEmpty(a.trackName))
                        continue;

                    if (a.clip == null)
                        continue;

                    // FIXED: Use normalized key for both check and insertion
                    string key = NormalizeKey(a.trackName);

                    if (ambientDictionary.ContainsKey(key))
                    {
                        Debug.LogWarning($"[AmbientLibrary] Duplicate trackName '{a.trackName}' found. Overwriting previous entry.", this);
                    }

                    ambientDictionary[key] = a.clip;
                }
            }
        }

        public AudioClip GetClipFromName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            EnsureBuilt();

            // FIXED: Use normalized key for lookup
            string key = NormalizeKey(name);

            if (ambientDictionary.TryGetValue(key, out var clip))
            {
                return clip;
            }

            // ADDED: Warning when clip not found
            Debug.LogWarning($"[AmbientLibrary] Ambient track '{name}' not found in library.");
            return null;
        }

        public string[] GetAllClipNames()
        {
            EnsureBuilt();
            return ambientDictionary.Keys.OrderBy(k => k).ToArray();
        }

        // -------------------------
        // Public rebuild helpers
        // -------------------------
        /// <summary>
        /// Public API to force the library to rebuild (safe to call from editor code).
        /// Call this after creating/assigning new AmbientTrack assets.
        /// </summary>
        public void RebuildDictionaries()
        {
            built = false;
            EnsureBuilt();
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            Debug.Log($"[AmbientLibrary] RebuildDictionaries: found {ambientDictionary.Count} tracks.");
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
            return ambientDictionary.ContainsKey(key);
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild Ambient Dictionary")]
        public void Editor_RebuildDictionary()
        {
            RebuildDictionaries();
        }

        // Run when the component is edited in inspector — mark cache dirty so queries rebuild.
        private void OnValidate()
        {
            built = false;
        }
#endif
    }
}