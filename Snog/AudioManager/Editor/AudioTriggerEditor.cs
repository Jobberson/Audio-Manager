
using UnityEditor;
using UnityEngine;
using Snog.Audio;
using Snog.Audio.Clips;

[CustomEditor(typeof(AudioTrigger))]
public class AudioTriggerEditor : Editor
{
    private AudioTrigger trigger;

    // Serialized properties
    private SerializedProperty tagToCompareProp;
    private SerializedProperty fireOnEnterProp;
    private SerializedProperty fireOnExitProp;

    private SerializedProperty selectedAudioTypeProp;
    private SerializedProperty actionProp;

    private SerializedProperty sfxClipProp;
    private SerializedProperty musicTrackProp;
    private SerializedProperty ambientTrackProp;

    private SerializedProperty playDelayProp;
    private SerializedProperty fadeDurationProp;
    private SerializedProperty override3DPositionProp;

    private int sfxSelectedIndex;
    private int musicSelectedIndex;
    private int ambientSelectedIndex;

    private static System.Type audioUtilType;
    private static System.Reflection.MethodInfo playPreviewMethod;
    private static System.Reflection.MethodInfo stopAllPreviewMethod;

    private void OnEnable()
    {
        trigger = (AudioTrigger)target;

        // General
        tagToCompareProp       = serializedObject.FindProperty("TagToCompare");
        fireOnEnterProp        = serializedObject.FindProperty("fireOnEnter");
        fireOnExitProp         = serializedObject.FindProperty("fireOnExit");

        // Selection
        selectedAudioTypeProp  = serializedObject.FindProperty("selectedAudioType");
        actionProp             = serializedObject.FindProperty("action");

        // Typed references
        sfxClipProp            = serializedObject.FindProperty("sfxClip");
        musicTrackProp         = serializedObject.FindProperty("musicTrack");
        ambientTrackProp       = serializedObject.FindProperty("ambientTrack");

        // Playback params
        playDelayProp          = serializedObject.FindProperty("playDelay");
        fadeDurationProp       = serializedObject.FindProperty("fadeDuration");

        // 3D SFX
        override3DPositionProp = serializedObject.FindProperty("override3DPosition");

        // Initialize local indices
        sfxSelectedIndex       = 0;
        musicSelectedIndex     = 0;
        ambientSelectedIndex   = 0;
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
        var manager = FindAnyObjectByType<AudioManager>();
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            bool hasManager = manager != null;
            EditorGUILayout.LabelField("Audio Manager", hasManager ? "Found in scene" : "Not found",
                hasManager ? EditorStyles.label : EditorStyles.boldLabel);

            if (hasManager)
            {
                EditorGUILayout.LabelField("Music:", manager.MusicIsPlaying() ? manager.GetCurrentMusicName() : "None");
                EditorGUILayout.LabelField("Ambient:", manager.AmbientIsPlaying() ? manager.GetCurrentAmbientName() : "None");
            }
            else
            {
                EditorGUILayout.HelpBox("No AudioManager in the scene. Runtime test buttons will be limited.", MessageType.Info);
            }
        }
    }

    private void DrawGeneralSection()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(tagToCompareProp, new GUIContent("Tag to Compare"));

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(fireOnEnterProp, new GUIContent("Fire On Enter"));
                EditorGUILayout.PropertyField(fireOnExitProp, new GUIContent("Fire On Exit"));
            }
        }
    }

    private void DrawSelectionSection()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(selectedAudioTypeProp, new GUIContent("Audio Type"));
            EditorGUILayout.PropertyField(actionProp, new GUIContent("Action"));

            var type   = (AudioType)selectedAudioTypeProp.enumValueIndex;
            var action = (AudioAction)actionProp.enumValueIndex;

            switch (type)
            {
                case AudioType.SFX:
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.PropertyField(sfxClipProp, new GUIContent("SFX (SoundClipData)"));

                    var so = sfxClipProp.objectReferenceValue as SoundClipData;

                    if (so == null)
                    {
                        EditorGUILayout.HelpBox("Assign a SoundClipData to choose/preview its clips.", MessageType.Info);
                        break;
                    }

                    var clips = so.clips;
                    if (clips == null || clips.Length == 0)
                    {
                        EditorGUILayout.HelpBox("This SoundClipData has no clips.", MessageType.Warning);
                    }
                    else
                    {
                        string[] names = new string[clips.Length];
                        for (int i = 0; i < clips.Length; i++)
                        {
                            names[i] = clips[i] != null ? clips[i].name : "(null)";
                        }

                        sfxSelectedIndex = EditorGUILayout.Popup("Variant", Mathf.Clamp(sfxSelectedIndex, 0, names.Length - 1), names);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("🎧 Preview Variant"))
                            {
                                PlayPreviewSafe(clips[sfxSelectedIndex]);
                            }

                            var manager = FindAnyObjectByType<AudioManager>();
                            if (manager != null)
                            {
                                if (action == AudioAction.Play2D && GUILayout.Button("▶ Play 2D (Runtime)"))
                                {
                                    manager.PlaySound2D(so.soundName); 
                                }

                                if (action == AudioAction.Play3D && GUILayout.Button("📍 Play 3D (Runtime)"))
                                {
                                    Vector3 pos = override3DPositionProp.vector3Value != Vector3.zero
                                        ? override3DPositionProp.vector3Value
                                        : trigger.transform.position;

                                    manager.PlaySound3D(so.soundName, pos);
                                }
                            }
                        }

                        if (action == AudioAction.Play3D)
                        {
                            EditorGUILayout.PropertyField(override3DPositionProp, new GUIContent("Override 3D Position"));
                            EditorGUILayout.HelpBox("Leave (0,0,0) to use the trigger object's position.", MessageType.None);
                        }
                    }

                    break;
                }

                case AudioType.Music:
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.PropertyField(musicTrackProp, new GUIContent("Music (MusicTrack)"));

                    var mt = musicTrackProp.objectReferenceValue as MusicTrack;

                    if (mt == null)
                    {
                        EditorGUILayout.HelpBox("Assign a MusicTrack to preview/play.", MessageType.Info);
                        break;
                    }

                    string[] names = new string[] { mt.clip != null ? mt.clip.name : "(null)" };
                    musicSelectedIndex = EditorGUILayout.Popup("Clip", 0, names);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("🎧 Preview"))
                        {
                            PlayPreviewSafe(mt.clip);
                        }

                        var manager = FindAnyObjectByType<AudioManager>();
                        if (manager != null)
                        {
                            if (action == AudioAction.Play && GUILayout.Button("▶ Play (Runtime)"))
                            {
                                manager.PlayMusic(mt.trackName, playDelayProp.floatValue);
                            }

                            if (action == AudioAction.PlayFadeIn && GUILayout.Button("🌅 Fade In (Runtime)"))
                            {
                                manager.StartCoroutine(manager.PlayMusicFade(mt.trackName, fadeDurationProp.floatValue));
                            }

                            if (action == AudioAction.StopMusic && GUILayout.Button("⏹ Stop"))
                            {
                                manager.StopMusic();
                            }

                            if (action == AudioAction.StopMusicFadeOut && GUILayout.Button("🌄 Fade Out"))
                            {
                                manager.StartCoroutine(manager.StopMusicFade(fadeDurationProp.floatValue));
                            }
                        }
                    }

                    break;
                }

                case AudioType.Ambient:
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.PropertyField(ambientTrackProp, new GUIContent("Ambient (AmbientTrack)"));

                    var at = ambientTrackProp.objectReferenceValue as AmbientTrack;

                    if (at == null)
                    {
                        EditorGUILayout.HelpBox("Assign an AmbientTrack to preview/play.", MessageType.Info);
                        break;
                    }

                    string[] names = new string[] { at.clip != null ? at.clip.name : "(null)" };
                    ambientSelectedIndex = EditorGUILayout.Popup("Clip", 0, names);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("🎧 Preview"))
                        {
                            PlayPreviewSafe(at.clip);
                        }

                        var manager = FindAnyObjectByType<AudioManager>();
                        if (manager != null)
                        {
                            if (action == AudioAction.PlayAmbient && GUILayout.Button("▶ Play (Runtime)"))
                            {
                                manager.PlayAmbient(at.trackName, playDelayProp.floatValue);
                            }

                            if (action == AudioAction.PlayAmbientFadeIn && GUILayout.Button("🌅 Fade In (Runtime)"))
                            {
                                manager.StartCoroutine(manager.PlayAmbientFade(at.trackName, fadeDurationProp.floatValue));
                            }

                            if (action == AudioAction.StopAmbient && GUILayout.Button("⏹ Stop"))
                            {
                                manager.StopAmbient();
                            }

                            if (action == AudioAction.StopAmbientFadeOut && GUILayout.Button("🌄 Fade Out"))
                            {
                                manager.StartCoroutine(manager.StopAmbientFade(fadeDurationProp.floatValue));
                            }

                            if (action == AudioAction.CrossfadeAmbient && GUILayout.Button("🔀 Crossfade"))
                            {
                                manager.StartCoroutine(manager.CrossfadeAmbient(at.trackName, fadeDurationProp.floatValue));
                            }
                        }
                    }

                    break;
                }
            }
        }
    }

    private void DrawPlaybackSection()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Playback Parameters", EditorStyles.boldLabel);

            var type   = (AudioType)selectedAudioTypeProp.enumValueIndex;
            var action = (AudioAction)actionProp.enumValueIndex;

            // Show delay only for "Play" style actions
            bool needsDelay =
                (type == AudioType.SFX && (action == AudioAction.Play2D || action == AudioAction.Play3D)) ||
                (type == AudioType.Music && action == AudioAction.Play) ||
                (type == AudioType.Ambient && action == AudioAction.PlayAmbient);

            if (needsDelay)
            {
                EditorGUILayout.PropertyField(playDelayProp, new GUIContent("Play Delay (sec)"));
            }

            bool needsFade =
                action == AudioAction.PlayFadeIn ||
                action == AudioAction.StopMusicFadeOut ||
                action == AudioAction.PlayAmbientFadeIn ||
                action == AudioAction.StopAmbientFadeOut ||
                action == AudioAction.CrossfadeAmbient;

            if (needsFade)
            {
                EditorGUILayout.PropertyField(fadeDurationProp, new GUIContent("Fade Duration (sec)"));
            }

            if (action == AudioAction.TransitionSnapshotDefault ||
                action == AudioAction.TransitionSnapshotCombat ||
                action == AudioAction.TransitionSnapshotStealth ||
                action == AudioAction.TransitionSnapshotUnderwater)
            {
                EditorGUILayout.HelpBox("This action switches mixer snapshots at runtime.", MessageType.None);
            }
        }
    }

    // ---------- Preview helpers ----------
    private void PlayPreviewSafe(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogWarning("[AudioTriggerEditor] No clip assigned for preview.");
            return;
        }

        // Cache reflection lookups once
        if (audioUtilType == null)
        {
            audioUtilType       = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            playPreviewMethod   = audioUtilType.GetMethod("PlayPreviewClip",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            stopAllPreviewMethod = audioUtilType.GetMethod("StopAllPreviewClips",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        }

        // Stop previous previews, then play
        stopAllPreviewMethod?.Invoke(null, null);
        playPreviewMethod?.Invoke(null, new object[] { clip, 0, false });
    }
}