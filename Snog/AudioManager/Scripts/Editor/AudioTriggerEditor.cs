#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Reflection;

using Snog.Audio.Clips;
using Snog.Scripts;

namespace Snog.Audio.Editor
{
    [CustomEditor(typeof(AudioTrigger))]
    public class AudioTriggerEditor : UnityEditor.Editor
    {
        private AudioTrigger trigger;

        // General
        private SerializedProperty tagToCompareProp;
        private SerializedProperty fireOnEnterProp;
        private SerializedProperty fireOnExitProp;

        // Selection
        private SerializedProperty audioTypeProp;
        private SerializedProperty actionProp;

        // References
        private SerializedProperty sfxClipProp;
        private SerializedProperty musicTrackProp;
        private SerializedProperty ambientTrackProp;

        // Params
        private SerializedProperty playDelayProp;
        private SerializedProperty fadeDurationProp;
        private SerializedProperty override3DPositionProp;

        private static System.Type audioUtilType;
        private static MethodInfo playPreviewMethod;
        private static MethodInfo stopAllPreviewMethod;

        private void OnEnable()
        {
            trigger = (AudioTrigger)target;

            // Try both capitalization variants to be resilient to field-name differences
            tagToCompareProp = serializedObject.FindProperty("tagToCompare") ?? serializedObject.FindProperty("TagToCompare");
            fireOnEnterProp  = serializedObject.FindProperty("fireOnEnter") ?? serializedObject.FindProperty("FireOnEnter");
            fireOnExitProp   = serializedObject.FindProperty("fireOnExit") ?? serializedObject.FindProperty("FireOnExit");

            audioTypeProp = serializedObject.FindProperty("audioType") ?? serializedObject.FindProperty("AudioType");
            actionProp    = serializedObject.FindProperty("action") ?? serializedObject.FindProperty("Action");

            sfxClipProp      = serializedObject.FindProperty("sfxClip") ?? serializedObject.FindProperty("SfxClip");
            musicTrackProp   = serializedObject.FindProperty("musicTrack") ?? serializedObject.FindProperty("MusicTrack");
            ambientTrackProp = serializedObject.FindProperty("ambientTrack") ?? serializedObject.FindProperty("AmbientTrack");

            playDelayProp          = serializedObject.FindProperty("playDelay") ?? serializedObject.FindProperty("PlayDelay");
            fadeDurationProp       = serializedObject.FindProperty("fadeDuration") ?? serializedObject.FindProperty("FadeDuration");
            override3DPositionProp = serializedObject.FindProperty("override3DPosition") ?? serializedObject.FindProperty("Override3DPosition");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeaderStatus();
            EditorGUILayout.Space(6);

            DrawGeneralSection();
            EditorGUILayout.Space(6);

            DrawSelectionSection();
            EditorGUILayout.Space(6);

            DrawPlaybackSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeaderStatus()
        {
#if UNITY_2023_1_OR_NEWER
            var manager = FindAnyObjectByType<AudioManager>();
#else
#pragma warning disable CS0618
            var manager = FindObjectOfType<AudioManager>();
#pragma warning restore CS0618
#endif

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (manager != null)
                {
                    EditorGUILayout.LabelField("Audio Manager", "Found in scene", EditorStyles.label);
                }
                else
                {
                    EditorGUILayout.LabelField("Audio Manager", "Not found", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("AudioManager is not present in the scene. Playback buttons will be disabled.", MessageType.Warning);
                }
            }
        }

        private void DrawGeneralSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("General", EditorStyles.boldLabel);

                if (tagToCompareProp != null)
                    EditorGUILayout.PropertyField(tagToCompareProp, new GUIContent("Tag To Compare"));
                else
                    EditorGUILayout.HelpBox("Field 'tagToCompare' not found on AudioTrigger. Inspector cannot edit tag filter.", MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (fireOnEnterProp != null) EditorGUILayout.PropertyField(fireOnEnterProp);
                    if (fireOnExitProp != null) EditorGUILayout.PropertyField(fireOnExitProp);
                }
            }
        }

        private void DrawSelectionSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);

                if (audioTypeProp != null)
                    EditorGUILayout.PropertyField(audioTypeProp, new GUIContent("Audio Type"));
                if (actionProp != null)
                    EditorGUILayout.PropertyField(actionProp, new GUIContent("Action"));

                // Defensive: if either prop is missing, bail out gracefully
                if (audioTypeProp == null || actionProp == null)
                {
                    EditorGUILayout.HelpBox("AudioType or Action property not found on AudioTrigger. Cannot show selection UI.", MessageType.Info);
                    return;
                }

                var type = (AudioTrigger.TriggerAudioType)audioTypeProp.enumValueIndex;
                var act  = (AudioTrigger.TriggerAudioAction)actionProp.enumValueIndex;

                EditorGUILayout.Space(4);

                switch (type)
                {
                    case AudioTrigger.TriggerAudioType.SFX:
                        DrawSfxSection(act);
                        break;

                    case AudioTrigger.TriggerAudioType.Music:
                        DrawMusicSection(act);
                        break;

                    case AudioTrigger.TriggerAudioType.Ambient:
                        DrawAmbientSection(act);
                        break;
                }
            }
        }

        private void DrawSfxSection(AudioTrigger.TriggerAudioAction action)
        {
            if (sfxClipProp != null)
                EditorGUILayout.PropertyField(sfxClipProp, new GUIContent("SFX (SoundClipData)"));
            else
            {
                EditorGUILayout.HelpBox("Field 'sfxClip' not found on AudioTrigger.", MessageType.Info);
                return;
            }

            var so = sfxClipProp.objectReferenceValue as SoundClipData;
            if (so == null || so.clips == null || so.clips.Length == 0)
            {
                EditorGUILayout.HelpBox("Assign a SoundClipData with at least one clip.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("🎧 Preview First Variant"))
            {
                PlayPreviewSafe(so.clips[0]);
            }

            if (action == AudioTrigger.TriggerAudioAction.Play3D)
            {
                if (override3DPositionProp != null)
                    EditorGUILayout.PropertyField(override3DPositionProp, new GUIContent("Override 3D Position"));
            }
        }

        private void DrawMusicSection(AudioTrigger.TriggerAudioAction action)
        {
            if (musicTrackProp != null)
                EditorGUILayout.PropertyField(musicTrackProp, new GUIContent("Music Track"));
            else
            {
                EditorGUILayout.HelpBox("Field 'musicTrack' not found on AudioTrigger.", MessageType.Info);
                return;
            }

            var mt = musicTrackProp.objectReferenceValue as MusicTrack;
            if (mt == null || mt.clip == null)
            {
                EditorGUILayout.HelpBox("Assign a MusicTrack with a clip.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("🎧 Preview"))
                PlayPreviewSafe(mt.clip);

#if UNITY_2023_1_OR_NEWER
            var manager = FindAnyObjectByType<AudioManager>();
#else
#pragma warning disable CS0618
            var manager = FindObjectOfType<AudioManager>();
#pragma warning restore CS0618
#endif

            if (manager == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                {
                    if (action == AudioTrigger.TriggerAudioAction.Play && GUILayout.Button("▶ Play"))
                        manager.PlayMusic(mt.trackName, 0f, 0f);

                    if (action == AudioTrigger.TriggerAudioAction.PlayFadeIn && GUILayout.Button("🌅 Fade In"))
                        manager.PlayMusic(mt.trackName, 0f, fadeDurationProp != null ? fadeDurationProp.floatValue : 0f);

                    if (action == AudioTrigger.TriggerAudioAction.StopMusic && GUILayout.Button("⏹ Stop"))
                        manager.StopMusic(fadeDurationProp != null ? fadeDurationProp.floatValue : 0f);
                }
            }
        }

        private void DrawAmbientSection(AudioTrigger.TriggerAudioAction action)
        {
            if (ambientTrackProp != null)
                EditorGUILayout.PropertyField(ambientTrackProp, new GUIContent("Ambient Track"));
            else
            {
                EditorGUILayout.HelpBox("Field 'ambientTrack' not found on AudioTrigger.", MessageType.Info);
                return;
            }

            var at = ambientTrackProp.objectReferenceValue as AmbientTrack;
            if (at == null || at.clip == null)
            {
                EditorGUILayout.HelpBox("Assign an AmbientTrack with a clip.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("🎧 Preview"))
                PlayPreviewSafe(at.clip);
        }

        private void DrawPlaybackSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Playback Parameters", EditorStyles.boldLabel);

                if (playDelayProp != null)
                    EditorGUILayout.PropertyField(playDelayProp, new GUIContent("Play Delay (sec)"));
                else
                    EditorGUILayout.HelpBox("Field 'playDelay' not found on AudioTrigger.", MessageType.Info);

                if (fadeDurationProp != null)
                    EditorGUILayout.PropertyField(fadeDurationProp, new GUIContent("Fade Duration (sec)"));
            }
        }

        private void PlayPreviewSafe(AudioClip clip)
        {
            if (clip == null) return;

            try
            {
                if (audioUtilType == null)
                {
                    audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
                    if (audioUtilType != null)
                    {
                        playPreviewMethod = audioUtilType.GetMethod(
                            "PlayPreviewClip",
                            BindingFlags.Static |
                            BindingFlags.Public |
                            BindingFlags.NonPublic
                        );
                        stopAllPreviewMethod = audioUtilType.GetMethod(
                            "StopAllPreviewClips",
                            BindingFlags.Static |
                            BindingFlags.Public |
                            BindingFlags.NonPublic
                        );
                    }
                }

                stopAllPreviewMethod?.Invoke(null, null);
                playPreviewMethod?.Invoke(null, new object[] { clip, 0, false });
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Audio preview failed: {ex.Message}");
            }
        }
    }
}
#endif
