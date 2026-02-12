﻿using UnityEngine;
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
        private Dictionary<string, MusicTrack> musicDictionary = new();
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
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(asset.trackName))
                    {
                        continue;
                    }

                    if (asset.clip == null)
                    {
                        continue;
                    }

                    if (!musicDictionary.ContainsKey(asset.trackName))
                    {
                        musicDictionary[asset.trackName] = asset;
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
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(m.trackName))
                    {
                        continue;
                    }

                    if (m.clip == null)
                    {
                        continue;
                    }

                    musicDictionary[m.trackName] = m;
                }
            }
        }

        public MusicTrack GetTrackFromName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            EnsureBuilt();

            if (musicDictionary.TryGetValue(name, out var track))
            {
                return track;
            }

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