using UnityEditor;
using UnityEngine;
using Snog.Audio.Libraries;
using System.Reflection;

namespace Snog.Audio
{
    [CustomEditor(typeof(AudioManager))]
    public class AudioManagerEditor : Editor
    {
        private AudioManager manager;

        private string[] snapshotOptions;
        private int selectedSnapshotIndex;

        private string[] soundNames;
        private int selectedSoundIndex;
        private Vector3 soundPosition;

        private string[] musicNames;
        private int selectedMusicIndex;

        private string[] ambientNames;
        private int selectedAmbientIndex;

        private float fadeDuration = 2f;
        private float playDelay = 0f;

        private bool showSFXSection = true;
        private bool showMusicSection = true;
        private bool showAmbientSection = true;
        private bool showSnapshotSection = true;
        private bool showInfoSection = true;
        private bool showUtilitiesSection = true;

        // Reflection cache for AudioUtil (editor preview playback)
        private static System.Type audioUtilType;
        private static MethodInfo playPreviewMethod;
        private static MethodInfo stopAllPreviewMethod;

        private void OnEnable()
        {
            manager = (AudioManager)target;
            snapshotOptions = System.Enum.GetNames(typeof(AudioManager.SnapshotType));
            RefreshClipLists();
        }

        private void RefreshClipLists()
        {
            if (manager.GetSoundLibrary() != null)
            {
                soundNames = manager.GetSoundLibrary().GetAllClipNames();
#if UNITY_EDITOR
                manager.GetSoundLibrary().Editor_RebuildDictionary();
#endif
            }
            else
                soundNames = new[] { "No SFX Available" };
                
#if UNITY_EDITOR
            manager.GetMusicLibrary().Editor_RebuildDictionary();
            manager.GetAmbientLibrary().Editor_RebuildDictionary();
#endif

            if (!manager.TryGetMusicNames(out musicNames) || musicNames.Length == 0)
                musicNames = new[] { "No music found" };

            if (!manager.TryGetAmbientNames(out ambientNames) || ambientNames.Length == 0)
                ambientNames = new[] { "No ambient found" };
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("🔧 Runtime Tools", EditorStyles.boldLabel);
            
            DrawUtilitiesSection();

            EditorGUILayout.Space(5);
            
            DrawSFXSection();
            DrawMusicSection();
            DrawAmbientSection();
            DrawSnapshotSection();
            DrawInfoSection();
        }

        #region Sections
        private void DrawUtilitiesSection()
        {
            showUtilitiesSection = EditorGUILayout.BeginFoldoutHeaderGroup(showUtilitiesSection, "🧰 Utilities");
            if (showUtilitiesSection)
            {
                EditorGUILayout.Space(4);

                if (GUILayout.Button(new GUIContent("📁 Set Root Audio Folder", "Choose the root folder where your audio clips are stored")))
                {
                    manager.SetAudioFolderPath();
                }

                EditorGUILayout.LabelField("Current Folder:", manager.audioFolderPath ?? "Not set");
                
                EditorGUILayout.Space(2);
                
                if (GUILayout.Button(new GUIContent("🔍 Scan Folder", "Scans the selected folder for AudioClips and automatically generates ScriptableObjects and assigns them to the appropriate libraries")))
                {
                    manager.ScanFolders();
                    manager.GenerateScriptableObjects();
                    manager.AssignToLibraries();
                    RefreshClipLists();
                }

                if (GUILayout.Button("Refresh Clip Lists"))
                    RefreshClipLists();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSFXSection()
        {
            showSFXSection = EditorGUILayout.BeginFoldoutHeaderGroup(showSFXSection, "🧪 Sound Effects (SFX)");
            if (showSFXSection)
            {
                EditorGUILayout.Space(4);
                selectedSoundIndex = EditorGUILayout.Popup("Sound Clip", selectedSoundIndex, soundNames);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("▶ Play 2D SFX (Runtime)"))
                    manager.PlaySound2D(soundNames[selectedSoundIndex]);

                if (GUILayout.Button("🎧 Preview in Editor"))
                    PlayPreviewFromLibrary(soundNames[selectedSoundIndex]);
                EditorGUILayout.EndHorizontal();

                soundPosition = EditorGUILayout.Vector3Field("3D Position", soundPosition);
                if (GUILayout.Button("📍 Play 3D SFX (Runtime)"))
                    manager.PlaySound3D(soundNames[selectedSoundIndex], soundPosition);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawMusicSection()
        {
            showMusicSection = EditorGUILayout.BeginFoldoutHeaderGroup(showMusicSection, "🎶 Music");
            if (showMusicSection)
            {
                EditorGUILayout.Space(4);
                selectedMusicIndex = EditorGUILayout.Popup("Music Clip", selectedMusicIndex, musicNames);
                playDelay = EditorGUILayout.FloatField("Play Delay (sec)", playDelay);
                fadeDuration = EditorGUILayout.FloatField("Fade Duration (sec)", fadeDuration);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("▶ Play Music (Runtime)"))
                    manager.PlayMusic(musicNames[selectedMusicIndex], playDelay);
                if (GUILayout.Button("🎧 Preview in Editor"))
                    PlayPreviewFromMusic(musicNames[selectedMusicIndex]);
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("🌅 Fade In Music"))
                    manager.StartCoroutine(manager.PlayMusicFade(musicNames[selectedMusicIndex], fadeDuration));

                if (GUILayout.Button("⏹ Stop Music"))
                    manager.StopMusic();

                if (GUILayout.Button("🌄 Fade Out Music"))
                    manager.StartCoroutine(manager.StopMusicFade(fadeDuration));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAmbientSection()
        {
            showAmbientSection = EditorGUILayout.BeginFoldoutHeaderGroup(showAmbientSection, "🌲 Ambient");
            if (showAmbientSection)
            {
                EditorGUILayout.Space(4);
                selectedAmbientIndex = EditorGUILayout.Popup("Ambient Clip", selectedAmbientIndex, ambientNames);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("▶ Play Ambient (Runtime)"))
                    manager.PlayAmbient(ambientNames[selectedAmbientIndex], playDelay);
                if (GUILayout.Button("🎧 Preview in Editor"))
                    PlayPreviewFromAmbient(ambientNames[selectedAmbientIndex]);
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("🌅 Fade In Ambient"))
                    manager.StartCoroutine(manager.PlayAmbientFade(ambientNames[selectedAmbientIndex], fadeDuration));

                if (GUILayout.Button("⏹ Stop Ambient"))
                    manager.StopAmbient();

                if (GUILayout.Button("🌄 Fade Out Ambient"))
                    manager.StartCoroutine(manager.StopAmbientFade(fadeDuration));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSnapshotSection()
        {
            showSnapshotSection = EditorGUILayout.BeginFoldoutHeaderGroup(showSnapshotSection, "🎚 Mixer Snapshots");
            if (showSnapshotSection)
            {
                selectedSnapshotIndex = EditorGUILayout.Popup("Snapshot", selectedSnapshotIndex, snapshotOptions);
                if (GUILayout.Button("🔄 Blend Snapshots"))
                {
                    manager.StartCoroutine(manager.BlendSnapshots(
                        AudioManager.SnapshotType.Default,
                        AudioManager.SnapshotType.Combat,
                        2f,
                        FadeCurveType.EaseInOut
                    ));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawInfoSection()
        {
            showInfoSection = EditorGUILayout.BeginFoldoutHeaderGroup(showInfoSection, "📊 Debug Info");
            if (showInfoSection)
            {
                EditorGUILayout.LabelField("🎵 Music:", manager.MusicIsPlaying() ? manager.GetCurrentMusicName() : "None");
                EditorGUILayout.LabelField("🌲 Ambient:", manager.AmbientIsPlaying() ? manager.GetCurrentAmbientName() : "None");

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("📦 FX Pool:", manager.fxPool != null ? "Active" : "Not Initialized");

                EditorGUILayout.LabelField("🔁 Pool Size:", manager.fxPool.poolSize.ToString());
                EditorGUILayout.LabelField("▶ Active Sources:", manager.fxPool.GetActiveSourceCount().ToString());
                EditorGUILayout.LabelField("♻ Recycled Count:", manager.fxPool.GetRecycledCount().ToString());
                EditorGUILayout.LabelField("🎯 Total Played:", manager.fxPool.GetTotalPlayed().ToString());

                if (GUILayout.Button("Resize Pool to 20"))
                {
                    manager.fxPool.ResizePool(20);
                }

                if (GUILayout.Button("Recycle Inactive Sources"))
                {
                    manager.fxPool.RecycleInactiveSources();
                }

                EditorGUILayout.Space(5);
                DrawMixerMeter("MasterVolume", "Master");
                DrawMixerMeter("MusicVolume", "Music");
                DrawMixerMeter("AmbientVolume", "Ambient");
                DrawMixerMeter("FXVolume", "FX");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        #endregion

        private void DrawMixerMeter(string parameterName, string label)
        {
            float db = manager.GetMixerVolumeDB(parameterName);
            float normalized = Mathf.InverseLerp(-80f, 0f, db);
            Rect rect = GUILayoutUtility.GetRect(18, 18, "TextField");
            EditorGUI.ProgressBar(rect, normalized, $"{label}: {db:F1} dB");
            GUILayout.Space(5);
        }

        #region Audio Preview (Editor-Only)
        private void PlayPreview(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogWarning("No clip assigned for preview.");
                return;
            }

            if (audioUtilType == null)
            {
                audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
                playPreviewMethod = audioUtilType.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                stopAllPreviewMethod = audioUtilType.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }

            stopAllPreviewMethod?.Invoke(null, null);
            playPreviewMethod?.Invoke(null, new object[] { clip, 0, false });
        }

        private void PlayPreviewFromLibrary(string clipName)
        {
            var clip = manager.GetSoundLibrary()?.GetClipFromName(clipName);
            PlayPreview(clip);
        }

        private void PlayPreviewFromMusic(string clipName)
        {
            var clip = manager.TryGetMusicNames(out var _) ? manager.GetComponent<MusicLibrary>().GetClipFromName(clipName) : null;
            PlayPreview(clip);
        }

        private void PlayPreviewFromAmbient(string clipName)
        {
            var clip = manager.TryGetAmbientNames(out var _) ? manager.GetComponent<AmbientLibrary>().GetClipFromName(clipName) : null;
            PlayPreview(clip);
        }
        #endregion
    }
}
