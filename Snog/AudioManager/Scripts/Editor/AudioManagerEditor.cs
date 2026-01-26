#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Reflection;

using Snog.Audio.Libraries;
using Snog.Audio.Utils;

namespace Snog.Audio.Editor
{
    [CustomEditor(typeof(AudioManager))]
    public class AudioManagerEditor : UnityEditor.Editor
    {
        private AudioManager manager;

        private string[] snapshotOptions;
        private int selectedSnapshotIndex;

        private string[] soundNames;
        private int selectedSoundIndex;
        private Vector3 soundPosition;

        private string[] musicNames;
        private int selectedMusicIndex;

        private AmbientProfile ambientProfileReplace;
        private AmbientProfile ambientProfileStack;
        private int ambientPriority = 0;
        private int lastAmbientToken = -1;

        private float fadeDuration = 2f;
        private float playDelay = 0f;
        private float musicFadeIn = 0f;
        private float musicFadeOut = 0f;

        private bool showUtilitiesSection = true;
        private bool showSfxSection = true;
        private bool showMusicSection = true;
        private bool showAmbientSection = true;
        private bool showEmittersSection = true;
        private bool showSnapshotSection = true;
        private bool showInfoSection = true;

        private SerializedProperty audioFolderPathProp;

        private AmbientEmitter[] cachedEmitters = new AmbientEmitter[0];
        private int selectedEmitterIndex = 0;

        private static System.Type audioUtilType;
        private static MethodInfo playPreviewMethod;
        private static MethodInfo stopAllPreviewMethod;

        private void OnEnable()
        {
            manager = (AudioManager)target;

            snapshotOptions = System.Enum.GetNames(typeof(AudioManager.SnapshotType));
            audioFolderPathProp = serializedObject.FindProperty("audioFolderPath");

            RefreshClipLists();
            Repaint();
            RefreshEmitters();
        }

        void OnValidate()
        {
        }

        private void RefreshClipLists()
        {
            // Ensure serialized state is up-to-date
            serializedObject.Update();

            // Force manager to rebuild its internal caches
            manager.RebuildDictionaries();

            // Try populate now
            bool gotSfx = manager.TryGetSoundNames(out soundNames) 
                        && soundNames != null && soundNames.Length > 0;

            bool gotMusic = manager.TryGetMusicNames(out musicNames) 
                            && musicNames != null && musicNames.Length > 0;

            // Set sensible defaults immediately so UI never breaks
            if (!gotSfx)
            {
                soundNames = new[] { "No SFX Found" };
                selectedSoundIndex = 0;
            }
            else
            {
                selectedSoundIndex = Mathf.Clamp(selectedSoundIndex, 0, soundNames.Length - 1);
            }

            if (!gotMusic)
            {
                musicNames = new[] { "No Music Found" };
                selectedMusicIndex = 0;
            }
            else
            {
                selectedMusicIndex = Mathf.Clamp(selectedMusicIndex, 0, musicNames.Length - 1);
            }

            // If either failed, refresh AssetDatabase and retry once next editor frame
            if (!gotSfx || !gotMusic)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorApplication.delayCall += () =>
                {
                    // Rebuild again after AssetDatabase refresh
                    manager.RebuildDictionaries();

                    if (manager.TryGetSoundNames(out var sNames) && sNames != null && sNames.Length > 0)
                    {
                        soundNames = sNames;
                        selectedSoundIndex = Mathf.Clamp(selectedSoundIndex, 0, soundNames.Length - 1);
                    }

                    if (manager.TryGetMusicNames(out var mNames) && mNames != null && mNames.Length > 0)
                    {
                        musicNames = mNames;
                        selectedMusicIndex = Mathf.Clamp(selectedMusicIndex, 0, musicNames.Length - 1);
                    }

                    Repaint();
                };
            }

            Repaint();
        }

        private void RefreshEmitters()
        {
#if UNITY_2023_1_OR_NEWER
            cachedEmitters = FindObjectsByType<AmbientEmitter>(FindObjectsSortMode.None);
#else
            cachedEmitters = FindObjectsOfType<AmbientEmitter>();
#endif
            selectedEmitterIndex = Mathf.Clamp(selectedEmitterIndex, 0, Mathf.Max(0, cachedEmitters.Length - 1));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("🔧 Runtime Tools", EditorStyles.boldLabel);

            DrawUtilitiesSection();

            EditorGUILayout.Space(6);

            DrawSfxSection();
            DrawMusicSection();
            DrawAmbientSection();
            DrawEmittersSection();
            DrawSnapshotSection();
            DrawInfoSection();

            serializedObject.ApplyModifiedProperties();
        }

        #region Sections

        private void DrawUtilitiesSection()
        {
            showUtilitiesSection = EditorGUILayout.BeginFoldoutHeaderGroup(showUtilitiesSection, "🧰 Utilities (Editor)");
            if (showUtilitiesSection)
            {
                EditorGUILayout.Space(4);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Asset Tools", EditorStyles.boldLabel);

                    using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                    {
                        if (GUILayout.Button(new GUIContent("📁 Set Root Audio Folder", "Choose a folder inside Assets")))
                        {
                            manager.SetAudioFolderPath();
                            serializedObject.Update();
                        }

                        string folderValue = audioFolderPathProp != null ? audioFolderPathProp.stringValue : "(field not found)";
                        EditorGUILayout.LabelField("Current Folder:", string.IsNullOrEmpty(folderValue) ? "Not set" : folderValue);

                        EditorGUILayout.Space(2);

                        if (GUILayout.Button(new GUIContent("🔍 Scan → Generate → Assign", "Scans folder, generates ScriptableObjects, and assigns them into libraries")))
                        {
                            // Run manager pipeline
                            manager.ScanFolders();
                            manager.GenerateScriptableObjects();
                            manager.AssignToLibraries();

                            // Make Unity persist new assets and schedule a re-query after import completes
                            EditorUtility.SetDirty(manager);
                            AssetDatabase.SaveAssets();
                            AssetDatabase.Refresh();

                            // Wait one editor loop so assets finish importing, then refresh lists & emitters
                            EditorApplication.delayCall += () =>
                            {
                                serializedObject.Update();
                                RefreshClipLists();
                                RefreshEmitters();
                                Repaint();
                                AssetDatabase.SaveAssets();
                                AssetDatabase.Refresh();
                            };
                        }
                    }

                    EditorGUILayout.Space(2);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("🔄 Refresh Clip Lists"))
                        {
                            RefreshClipLists();
                        }

                        if (GUILayout.Button("🔄 Refresh Emitters"))
                        {
                            RefreshEmitters();
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSfxSection()
        {
            showSfxSection = EditorGUILayout.BeginFoldoutHeaderGroup(showSfxSection, "🧪 Sound Effects (SFX)");
            if (showSfxSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.Space(4);

                    bool hasSfx = soundNames != null && soundNames.Length > 0 && soundNames[0] != "No SFX Found";
                    using (new EditorGUI.DisabledScope(!hasSfx))
                    {
                        selectedSoundIndex = EditorGUILayout.Popup("SFX Name", selectedSoundIndex, soundNames);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                            {
                                if (GUILayout.Button("▶ Play 2D (Runtime)"))
                                {
                                    manager.PlaySfx2D(soundNames[selectedSoundIndex]);
                                }
                            }

                            if (GUILayout.Button("🎧 Preview (Editor)"))
                            {
                                PlayPreviewFromSoundName(soundNames[selectedSoundIndex]);
                            }
                        }

                        soundPosition = EditorGUILayout.Vector3Field("3D Position", soundPosition);
                        
                        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                        {
                            if (GUILayout.Button("📍 Play 3D (Runtime)"))
                            {
                                manager.PlaySfx3D(soundNames[selectedSoundIndex], soundPosition);
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawMusicSection()
        {
            showMusicSection = EditorGUILayout.BeginFoldoutHeaderGroup(showMusicSection, "🎶 Music");
            if (showMusicSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.Space(4);

                    bool hasMusic = musicNames != null && musicNames.Length > 0 && musicNames[0] != "No Music Found";
                    using (new EditorGUI.DisabledScope(!hasMusic))
                    {
                        selectedMusicIndex = EditorGUILayout.Popup("Music Name", selectedMusicIndex, musicNames);

                        playDelay = EditorGUILayout.FloatField("Delay (sec)", playDelay);
                        musicFadeIn = EditorGUILayout.FloatField("Fade In (sec)", musicFadeIn);
                        musicFadeOut = EditorGUILayout.FloatField("Fade Out (sec)", musicFadeOut);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                            {
                                if (GUILayout.Button("▶ Play (Runtime)"))
                                {
                                    manager.PlayMusic(musicNames[selectedMusicIndex], playDelay, musicFadeIn);
                                }
                            }

                            if (GUILayout.Button("🎧 Preview (Editor)"))
                            {
                                PlayPreviewFromMusicName(musicNames[selectedMusicIndex]);
                            }
                        }
                        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                        {
                            if (GUILayout.Button("⏹ Stop (Runtime)"))
                            {
                                manager.StopMusic(musicFadeOut);
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAmbientSection()
        {
            showAmbientSection = EditorGUILayout.BeginFoldoutHeaderGroup(showAmbientSection, "🌲 Ambient (Profiles + Stack)");
            if (showAmbientSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.Space(4);

                    fadeDuration = EditorGUILayout.FloatField("Fade (sec)", fadeDuration);

                    EditorGUILayout.LabelField("Replace Mode", EditorStyles.boldLabel);

                    ambientProfileReplace = (AmbientProfile)EditorGUILayout.ObjectField(
                        "Profile",
                        ambientProfileReplace,
                        typeof(AmbientProfile),
                        false
                    );

                    using (new EditorGUI.DisabledScope(ambientProfileReplace == null))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                            {
                                if (GUILayout.Button("✅ Set Profile"))
                                {
                                    manager.SetAmbientProfile(ambientProfileReplace, fadeDuration);
                                }

                                if (GUILayout.Button("🧹 Clear Ambient"))
                                {
                                    manager.ClearAmbient(fadeDuration);
                                }
                            }
                        }
                    }

                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("Stack Mode", EditorStyles.boldLabel);

                    ambientProfileStack = (AmbientProfile)EditorGUILayout.ObjectField(
                        "Stack Profile",
                        ambientProfileStack,
                        typeof(AmbientProfile),
                        false
                    );

                    ambientPriority = EditorGUILayout.IntField("Priority", ambientPriority);

                    using (new EditorGUI.DisabledScope(ambientProfileStack == null))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("⬆ Push"))
                            {
                                lastAmbientToken = manager.PushAmbientProfile(ambientProfileStack, ambientPriority, fadeDuration);
                            }

                            if (GUILayout.Button("⬇ Pop (Profile)"))
                            {
                                manager.PopAmbientProfile(ambientProfileStack, fadeDuration);
                            }
                        }
                    }

                    EditorGUILayout.Space(2);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Last Token:", lastAmbientToken.ToString());

                        using (new EditorGUI.DisabledScope(lastAmbientToken < 0))
                        {
                            if (GUILayout.Button("⬇ Pop (Token)"))
                            {
                                manager.PopAmbientToken(lastAmbientToken, fadeDuration);
                                lastAmbientToken = -1;
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawEmittersSection()
        {
            showEmittersSection = EditorGUILayout.BeginFoldoutHeaderGroup(showEmittersSection, "📡 Ambient Emitters (Scene)");
            if (showEmittersSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.Space(4);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Found:", cachedEmitters != null ? cachedEmitters.Length.ToString() : "0");
                        if (GUILayout.Button("🔄 Refresh", GUILayout.Width(90)))
                        {
                            RefreshEmitters();
                        }
                    }

                    if (cachedEmitters == null || cachedEmitters.Length == 0)
                    {
                        EditorGUILayout.HelpBox("No AmbientEmitter components found in the scene. Place AmbientEmitter objects to enable 3D ambience.", MessageType.Info);
                    }
                    else
                    {
                        string[] emitterLabels = new string[cachedEmitters.Length];
                        for (int i = 0; i < cachedEmitters.Length; i++)
                        {
                            AmbientEmitter em = cachedEmitters[i];
                            string name = em != null ? em.name : "(null)";
                            string track = (em != null && em.Track != null) ? em.Track.trackName : "No Track";
                            emitterLabels[i] = $"{name}  •  {track}";
                        }

                        selectedEmitterIndex = EditorGUILayout.Popup("Emitter", selectedEmitterIndex, emitterLabels);
                        selectedEmitterIndex = Mathf.Clamp(selectedEmitterIndex, 0, cachedEmitters.Length - 1);

                        AmbientEmitter selected = cachedEmitters[selectedEmitterIndex];
                        using (new EditorGUI.DisabledScope(selected == null))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                if (GUILayout.Button("📌 Ping"))
                                {
                                    EditorGUIUtility.PingObject(selected.gameObject);
                                    Selection.activeGameObject = selected.gameObject;
                                }

                                if (GUILayout.Button("🎧 Preview Clip"))
                                {
                                    AudioClip clip = (selected != null && selected.Track != null) ? selected.Track.clip : null;
                                    PlayPreview(clip);
                                }
                            }

                            if (selected != null && selected.Track == null)
                            {
                                EditorGUILayout.HelpBox("Selected emitter has no AmbientTrack assigned.", MessageType.Warning);
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSnapshotSection()
        {
            showSnapshotSection = EditorGUILayout.BeginFoldoutHeaderGroup(showSnapshotSection, "🎚 Mixer Snapshots");
            if (showSnapshotSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    selectedSnapshotIndex = EditorGUILayout.Popup("Snapshot", selectedSnapshotIndex, snapshotOptions);
                    using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                    {
                        if (GUILayout.Button("🔀 Switch Snapshot"))
                        {
                            manager.TransitionToSnapshot((AudioManager.SnapshotType)selectedSnapshotIndex, 1f);
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawInfoSection()
        {
            showInfoSection = EditorGUILayout.BeginFoldoutHeaderGroup(showInfoSection, "📊 Debug Info");
            if (showInfoSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (manager.TryGetCurrentAmbientStack(out int stackCount))
                    {
                        EditorGUILayout.LabelField("🌲 Ambient Stack Count:", stackCount.ToString());
                    }
                    else
                    {
                        EditorGUILayout.LabelField("🌲 Ambient Stack Count:", "N/A");
                    }

                    EditorGUILayout.Space(4);

                    DrawMixerMeter("MasterVolume", "Master");
                    DrawMixerMeter("MusicVolume", "Music");
                    DrawMixerMeter("AmbientVolume", "Ambient");
                    DrawMixerMeter("FXVolume", "FX");
                }
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

        #region Editor Audio Preview

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
                playPreviewMethod = audioUtilType.GetMethod(
                    "PlayPreviewClip",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                );
                stopAllPreviewMethod = audioUtilType.GetMethod(
                    "StopAllPreviewClips",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                );
            }

            stopAllPreviewMethod?.Invoke(null, null);
            playPreviewMethod?.Invoke(null, new object[] { clip, 0, false });
        }

        private void PlayPreviewFromSoundName(string soundName)
        {
            SoundLibrary lib = manager.GetComponent<SoundLibrary>();
            if (lib == null)
            {
                Debug.LogWarning("SoundLibrary missing on AudioManager.");
                return;
            }

            AudioClip clip = lib.GetClipFromName(soundName);
            PlayPreview(clip);
        }

        private void PlayPreviewFromMusicName(string musicName)
        {
            MusicLibrary lib = manager.GetComponent<MusicLibrary>();
            if (lib == null)
            {
                Debug.LogWarning("MusicLibrary missing on AudioManager.");
                return;
            }

            AudioClip clip = lib.GetClipFromName(musicName);
            PlayPreview(clip);
        }

        #endregion
    }
}

#endif
