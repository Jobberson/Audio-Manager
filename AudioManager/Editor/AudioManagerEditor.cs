using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Snog.Audio
{
    [CustomEditor(typeof(AudioManager))]
    public class AudioManagerEditor : Editor
    {
        private AudioManager manager;

        // Serialized props (so this editor works with private serialized fields)
        private SerializedProperty sp_audioFolderPath;
        private SerializedProperty sp_poolSize;
        private SerializedProperty sp_sfxPool;

        // Clip lists + search
        private string[] soundNames = Array.Empty<string>();
        private string[] musicNames = Array.Empty<string>();
        private string[] ambientNames = Array.Empty<string>();
        private string soundSearch = "";
        private string musicSearch = "";
        private string ambientSearch = "";

        // UI selection indices
        private int selectedSoundIndex = 0;
        private int selectedMusicIndex = 0;
        private int selectedAmbientIndex = 0;
        private int selectedSnapshotIndex = 0;

        // preview reflection
        private static Type audioUtilType;
        private static MethodInfo playPreviewMethod;
        private static MethodInfo stopAllPreviewMethod;

        // foldouts
        private bool showSFXSection = true;
        private bool showMusicSection = true;
        private bool showAmbientSection = true;
        private bool showSnapshotSection = true;
        private bool showInfoSection = true;
        private bool showUtilitiesSection = true;

        // small UI state
        private float fadeDuration = 2f;
        private float playDelay = 0f;
        private Vector3 soundPosition = Vector3.zero;

        private void OnEnable()
        {
            manager = (AudioManager)target;
            sp_audioFolderPath = serializedObject.FindProperty("audioFolderPath");
            sp_poolSize = serializedObject.FindProperty("poolSize");
            sp_sfxPool = serializedObject.FindProperty("sfxPool");

            CacheAudioUtilMethods();
            RefreshClipLists();
        }

        public override void OnInspectorGUI()
        {
            // sync serialized properties
            serializedObject.Update();

            // Draw the default inspector but keep it neat
            EditorGUILayout.LabelField("AudioManager (Runtime settings)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            DrawDefaultInspectorExcept(new string[] { "audioFolderPath", "sfxPool", "poolSize" });

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("🔧 Runtime Tools", EditorStyles.boldLabel);

            DrawUtilitiesSection();

            EditorGUILayout.Space(6);
            DrawSFXSection();
            DrawMusicSection();
            DrawAmbientSection();
            DrawSnapshotSection();
            DrawInfoSection();

            // apply serialized changes
            serializedObject.ApplyModifiedProperties();
        }

        #region Inspector Helpers

        private void DrawDefaultInspectorExcept(string[] exclude)
        {
            // Show all serialized properties except the excluded ones
            var property = serializedObject.GetIterator();
            property.NextVisible(true); // skip script reference
            while (property.NextVisible(false))
            {
                if (exclude != null && exclude.Contains(property.name)) continue;
                EditorGUILayout.PropertyField(property, true);
            }
        }

        private void CacheAudioUtilMethods()
        {
            if (audioUtilType != null) return;
            try
            {
                audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
                if (audioUtilType != null)
                {
                    playPreviewMethod = audioUtilType.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    stopAllPreviewMethod = audioUtilType.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }
            catch
            {
                audioUtilType = null;
                playPreviewMethod = null;
                stopAllPreviewMethod = null;
            }
        }

        private void RefreshClipLists()
        {
            // Ask manager for names; fallbacks keep UI stable
            if (manager != null)
            {
                if (!manager.TryGetSoundNames(out var s) || s == null || s.Length == 0) soundNames = new[] { "No SFX Available" };
                else soundNames = s;

                if (!manager.TryGetMusicNames(out var m) || m == null || m.Length == 0) musicNames = new[] { "No Music Found" };
                else musicNames = m;

                if (!manager.TryGetAmbientNames(out var a) || a == null || a.Length == 0) ambientNames = new[] { "No Ambient Found" };
                else ambientNames = a;
            }
            else
            {
                soundNames = new[] { "No SFX Available" };
                musicNames = new[] { "No Music Found" };
                ambientNames = new[] { "No Ambient Found" };
            }

            // reset indices to safe range
            selectedSoundIndex = Mathf.Clamp(selectedSoundIndex, 0, Math.Max(0, soundNames.Length - 1));
            selectedMusicIndex = Mathf.Clamp(selectedMusicIndex, 0, Math.Max(0, musicNames.Length - 1));
            selectedAmbientIndex = Mathf.Clamp(selectedAmbientIndex, 0, Math.Max(0, ambientNames.Length - 1));
        }

        #endregion

        #region Sections

        private void DrawUtilitiesSection()
        {
            showUtilitiesSection = EditorGUILayout.BeginFoldoutHeaderGroup(showUtilitiesSection, "🧰 Utilities");
            if (!showUtilitiesSection) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

            EditorGUILayout.Space(4);

            // Folder selector
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(sp_audioFolderPath, new GUIContent("Audio Folder"));
            if (GUILayout.Button("Browse", GUILayout.ExpandWidth(false)))
            {
                manager.SetAudioFolderPath();
                serializedObject.Update();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔍 Scan (quick)"))
            {
                manager.ScanFolders();
                RefreshClipLists();
            }
            if (GUILayout.Button("Generate & Assign"))
            {
                manager.ScanFolders();
                manager.GenerateScriptableObjects();
                manager.AssignToLibraries();
                RefreshClipLists();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Refresh Clip Lists"))
            {
                RefreshClipLists();
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSFXSection()
        {
            showSFXSection = EditorGUILayout.BeginFoldoutHeaderGroup(showSFXSection, "🧪 Sound Effects (SFX)");
            if (!showSFXSection) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

            EditorGUILayout.Space(4);

            // Search + popup
            EditorGUILayout.BeginHorizontal();
            soundSearch = EditorGUILayout.TextField("Search", soundSearch);
            if (GUILayout.Button("⟳", GUILayout.Width(28))) { soundSearch = ""; RefreshClipLists(); }
            EditorGUILayout.EndHorizontal();

            var filteredSfx = soundNames.Where(n => string.IsNullOrEmpty(soundSearch) || n.IndexOf(soundSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (filteredSfx.Length == 0) filteredSfx = new[] { "No matches" };
            selectedSoundIndex = EditorGUILayout.Popup("Sound Clip", Mathf.Clamp(selectedSoundIndex, 0, filteredSfx.Length - 1), filteredSfx);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("▶ Play 2D SFX (Runtime)"))
            {
                var name = filteredSfx[selectedSoundIndex];
                manager.PlaySound2D(name);
            }
            GUI.enabled = true;

            if (GUILayout.Button("🎧 Preview (Editor)"))
            {
                var name = filteredSfx[selectedSoundIndex];
                PlayPreviewFromLibrary(name);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            soundPosition = EditorGUILayout.Vector3Field("3D Position", soundPosition);
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("📍 Play 3D SFX (Runtime)"))
            {
                var name = filteredSfx[selectedSoundIndex];
                manager.PlaySound3D(name, soundPosition);
            }
            GUI.enabled = true;

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawMusicSection()
        {
            showMusicSection = EditorGUILayout.BeginFoldoutHeaderGroup(showMusicSection, "🎶 Music");
            if (!showMusicSection) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

            EditorGUILayout.Space(4);

            // Search + popup
            EditorGUILayout.BeginHorizontal();
            musicSearch = EditorGUILayout.TextField("Search", musicSearch);
            if (GUILayout.Button("⟳", GUILayout.Width(28))) { musicSearch = ""; RefreshClipLists(); }
            EditorGUILayout.EndHorizontal();

            var filteredMusic = musicNames.Where(n => string.IsNullOrEmpty(musicSearch) || n.IndexOf(musicSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (filteredMusic.Length == 0) filteredMusic = new[] { "No matches" };
            selectedMusicIndex = EditorGUILayout.Popup("Music Clip", Mathf.Clamp(selectedMusicIndex, 0, filteredMusic.Length - 1), filteredMusic);

            playDelay = EditorGUILayout.FloatField("Play Delay (sec)", playDelay);
            fadeDuration = EditorGUILayout.FloatField("Fade Duration (sec)", fadeDuration);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("▶ Play Music (Runtime)"))
            {
                manager.PlayMusic(filteredMusic[selectedMusicIndex], playDelay);
            }
            GUI.enabled = true;

            if (GUILayout.Button("🎧 Preview (Editor)"))
            {
                PlayPreviewFromMusic(filteredMusic[selectedMusicIndex]);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("🌅 Fade In Music"))
            {
                manager.StartCoroutine(manager.PlayMusicFade(filteredMusic[selectedMusicIndex], fadeDuration));
            }
            if (GUILayout.Button("⏹ Stop Music"))
            {
                manager.StopMusic();
            }
            GUI.enabled = true;
            if (GUILayout.Button("🌄 Fade Out Music"))
            {
                manager.StartCoroutine(manager.StopMusicFade(fadeDuration));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAmbientSection()
        {
            showAmbientSection = EditorGUILayout.BeginFoldoutHeaderGroup(showAmbientSection, "🌲 Ambient");
            if (!showAmbientSection) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

            EditorGUILayout.Space(4);

            // Search + popup
            EditorGUILayout.BeginHorizontal();
            ambientSearch = EditorGUILayout.TextField("Search", ambientSearch);
            if (GUILayout.Button("⟳", GUILayout.Width(28))) { ambientSearch = ""; RefreshClipLists(); }
            EditorGUILayout.EndHorizontal();

            var filteredAmbient = ambientNames.Where(n => string.IsNullOrEmpty(ambientSearch) || n.IndexOf(ambientSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (filteredAmbient.Length == 0) filteredAmbient = new[] { "No matches" };
            selectedAmbientIndex = EditorGUILayout.Popup("Ambient Clip", Mathf.Clamp(selectedAmbientIndex, 0, filteredAmbient.Length - 1), filteredAmbient);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("▶ Play Ambient (Runtime)"))
            {
                manager.PlayAmbient(filteredAmbient[selectedAmbientIndex], playDelay);
            }
            GUI.enabled = true;

            if (GUILayout.Button("🎧 Preview (Editor)"))
            {
                PlayPreviewFromAmbient(filteredAmbient[selectedAmbientIndex]);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("🌅 Fade In Ambient"))
            {
                manager.StartCoroutine(manager.PlayAmbientFade(filteredAmbient[selectedAmbientIndex], fadeDuration));
            }
            if (GUILayout.Button("⏹ Stop Ambient"))
            {
                manager.StopAmbient();
            }
            GUI.enabled = true;
            if (GUILayout.Button("🌄 Fade Out Ambient"))
            {
                manager.StartCoroutine(manager.StopAmbientFade(fadeDuration));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSnapshotSection()
        {
            showSnapshotSection = EditorGUILayout.BeginFoldoutHeaderGroup(showSnapshotSection, "🎚 Mixer Snapshots");
            if (!showSnapshotSection) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

            string[] snapshotOptions = Enum.GetNames(typeof(AudioManager.SnapshotType));
            selectedSnapshotIndex = EditorGUILayout.Popup("Snapshot Preset", selectedSnapshotIndex, snapshotOptions);
            if (GUILayout.Button("🔄 Blend (Default -> Combat -> Selected)"))
            {
                // play quick demo blend from Default to selected snapshot
                var target = (AudioManager.SnapshotType)selectedSnapshotIndex;
                manager.StartCoroutine(manager.BlendSnapshots(AudioManager.SnapshotType.Default, target, 2f, AudioManager.FadeCurveType.EaseInOut));
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawInfoSection()
        {
            showInfoSection = EditorGUILayout.BeginFoldoutHeaderGroup(showInfoSection, "📊 Debug & Pool Info");
            if (!showInfoSection) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

            EditorGUILayout.LabelField("🎵 Music:", manager.MusicIsPlaying() ? manager.GetCurrentMusicName() : "None");
            EditorGUILayout.LabelField("🌲 Ambient:", manager.AmbientIsPlaying() ? manager.GetCurrentAmbientName() : "None");
            EditorGUILayout.Space(6);

            // show pool info reflectively (works whether pool API exposes these methods or not)
            object poolObj = GetSerializedObjectValue(sp_sfxPool);
            if (poolObj != null)
            {
                EditorGUILayout.LabelField("📦 SFX Pool: Active");
                TryDrawPoolStats(poolObj);
            }
            else
            {
                EditorGUILayout.LabelField("📦 SFX Pool: Not Initialized");
            }

            // volume meters (read DB, convert to linear)
            EditorGUILayout.Space(6);
            DrawMixerSlider("MasterVolume", "Master Volume");
            DrawMixerSlider("MusicVolume", "Music Volume");
            DrawMixerSlider("AmbientVolume", "Ambient Volume");
            DrawMixerSlider("FXVolume", "SFX Volume");

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("💾 Save Volume Settings")) manager.SaveVolumeSettings();
            if (GUILayout.Button("📥 Load Volume Settings")) manager.LoadVolumeSettings();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawMixerSlider(string parameterName, string label)
        {
            // Get current dB and convert to linear
            float db = manager.GetMixerVolumeDB(parameterName);
            float linear = Mathf.Pow(10f, db / 20f);
            float newLinear = EditorGUILayout.Slider(label, linear, 0f, 1f);
            if (!Mathf.Approximately(newLinear, linear))
            {
                // determine channel enum name mapping (best-effort)
                switch (parameterName)
                {
                    case "MasterVolume": manager.SetVolume(newLinear, AudioManager.AudioChannel.Master); break;
                    case "MusicVolume": manager.SetVolume(newLinear, AudioManager.AudioChannel.Music); break;
                    case "AmbientVolume": manager.SetVolume(newLinear, AudioManager.AudioChannel.Ambient); break;
                    case "FXVolume": manager.SetVolume(newLinear, AudioManager.AudioChannel.fx); break;
                }
            }
        }

        #endregion

        #region Pool Helpers 

        private void TryDrawPoolStats(object pool)
        {
            // Attempt to call GetActiveSourceCount, GetRecycledCount, GetTotalPlayed, ResizePool, RecycleInactiveSources if exposed.
            int active = TryInvokeIntMethod(pool, "GetActiveSourceCount");
            int recycled = TryInvokeIntMethod(pool, "GetRecycledCount");
            int totalPlayed = TryInvokeIntMethod(pool, "GetTotalPlayed");

            EditorGUILayout.LabelField("🔁 Pool Size (serialized)", sp_poolSize != null ? sp_poolSize.intValue.ToString() : "n/a");
            EditorGUILayout.LabelField("▶ Active Sources", active >= 0 ? active.ToString() : "n/a");
            EditorGUILayout.LabelField("♻ Recycled Count", recycled >= 0 ? recycled.ToString() : "n/a");
            EditorGUILayout.LabelField("🎯 Total Played", totalPlayed >= 0 ? totalPlayed.ToString() : "n/a");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Resize Pool to 20"))
            {
                // try public API first, then reflectively set fields
                bool ok = TryInvokeVoidMethod(pool, "ResizePool", 20);
                if (!ok && sp_poolSize != null)
                {
                    Undo.RecordObject(manager, "Resize SFX Pool Size");
                    sp_poolSize.intValue = 20;
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(manager);
                }
            }

            if (GUILayout.Button("Recycle Inactive Sources"))
            {
                TryInvokeVoidMethod(pool, "RecycleInactiveSources");
            }
            EditorGUILayout.EndHorizontal();
        }

        private int TryInvokeIntMethod(object target, string methodName)
        {
            if (target == null) return -1;
            try
            {
                var mi = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    var r = mi.Invoke(target, null);
                    if (r is int i) return i;
                }
            }
            catch { /* ignore */ }
            return -1;
        }

        private bool TryInvokeVoidMethod(object target, string methodName, params object[] args)
        {
            if (target == null) return false;
            try
            {
                var mi = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null) { mi.Invoke(target, args); return true; }
            }
            catch { /* ignore */ }
            return false;
        }

        private object GetSerializedObjectValue(SerializedProperty prop)
        {
            if (prop == null || prop.objectReferenceValue == null) return null;
            return prop.objectReferenceValue;
        }

        #endregion

        #region Editor Audio Preview

        private void PlayPreview(AudioClip clip)
        {
            if (clip == null) { Debug.LogWarning("No clip assigned for preview."); return; }
            if (audioUtilType == null) CacheAudioUtilMethods();
            try
            {
                stopAllPreviewMethod?.Invoke(null, null);
                playPreviewMethod?.Invoke(null, new object[] { clip, 0, false });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Preview failed: {e.Message}");
            }
        }

        private void PlayPreviewFromLibrary(string clipName)
        {
            var clip = manager.GetSoundLibrary()?.GetClipFromName(clipName);
            PlayPreview(clip);
        }

        private void PlayPreviewFromMusic(string clipName)
        {
            var clip = manager.GetMusicLibrary()?.GetClipFromName(clipName);
            PlayPreview(clip);
        }

        private void PlayPreviewFromAmbient(string clipName)
        {
            var clip = manager.GetAmbientLibrary()?.GetClipFromName(clipName);
            PlayPreview(clip);
        }

        #endregion
    }
}