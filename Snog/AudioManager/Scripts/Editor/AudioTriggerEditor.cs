#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Snog.Audio;
using Snog.Audio.Clips;

[CustomEditor(typeof(AudioTrigger))]
public class AudioTriggerEditor : Editor
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
    private static System.Reflection.MethodInfo playPreviewMethod;
    private static System.Reflection.MethodInfo stopAllPreviewMethod;

    private void OnEnable()
    {
        trigger = (AudioTrigger)target;

        tagToCompareProp       = serializedObject.FindProperty("TagToCompare");
        fireOnEnterProp        = serializedObject.FindProperty("fireOnEnter");
        fireOnExitProp         = serializedObject.FindProperty("fireOnExit");

        audioTypeProp          = serializedObject.FindProperty("audioType");
        actionProp             = serializedObject.FindProperty("action");

        sfxClipProp            = serializedObject.FindProperty("sfxClip");
        musicTrackProp         = serializedObject.FindProperty("musicTrack");
        ambientTrackProp       = serializedObject.FindProperty("ambientTrack");

        playDelayProp          = serializedObject.FindProperty("playDelay");
        fadeDurationProp       = serializedObject.FindProperty("fadeDuration");
        override3DPositionProp = serializedObject.FindProperty("override3DPosition");
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
            EditorGUILayout.LabelField("Audio Manager",
                manager != null ? "Found in scene" : "Not found",
                manager != null ? EditorStyles.label : EditorStyles.boldLabel);
        }
    }

    private void DrawGeneralSection()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(tagToCompareProp, new GUIContent("Tag To Compare"));

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(fireOnEnterProp);
                EditorGUILayout.PropertyField(fireOnExitProp);
            }
        }
    }

    private void DrawSelectionSection()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(audioTypeProp, new GUIContent("Audio Type"));
            EditorGUILayout.PropertyField(actionProp, new GUIContent("Action"));

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
        EditorGUILayout.PropertyField(sfxClipProp, new GUIContent("SFX (SoundClipData)"));

        var so = sfxClipProp.objectReferenceValue as SoundClipData;
        if (so == null || so.clips == null || so.clips.Length == 0)
        {
            EditorGUILayout.HelpBox("Assign a SoundClipData with at least one clip.", MessageType.Info);
            return;
        }

        if (GUILayout.Button("🎧 Preview First Variant"))
            PlayPreviewSafe(so.clips[0]);

        if (action == AudioTrigger.TriggerAudioAction.Play3D)
        {
            EditorGUILayout.PropertyField(override3DPositionProp, new GUIContent("Override 3D Position"));
        }
    }

    private void DrawMusicSection(AudioTrigger.TriggerAudioAction action)
    {
        EditorGUILayout.PropertyField(musicTrackProp, new GUIContent("Music Track"));

        var mt = musicTrackProp.objectReferenceValue as MusicTrack;
        if (mt == null || mt.clip == null)
        {
            EditorGUILayout.HelpBox("Assign a MusicTrack with a clip.", MessageType.Info);
            return;
        }

        if (GUILayout.Button("🎧 Preview"))
            PlayPreviewSafe(mt.clip);

        var manager = FindAnyObjectByType<AudioManager>();
        if (manager == null) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (action == AudioTrigger.TriggerAudioAction.Play && GUILayout.Button("▶ Play"))
                manager.PlayMusic(mt.trackName, 0f, 0f);

            if (action == AudioTrigger.TriggerAudioAction.PlayFadeIn && GUILayout.Button("🌅 Fade In"))
                manager.PlayMusic(mt.trackName, 0f, fadeDurationProp.floatValue);

            if (action == AudioTrigger.TriggerAudioAction.StopMusic && GUILayout.Button("⏹ Stop"))
                manager.StopMusic(fadeDurationProp.floatValue);
        }
    }

    private void DrawAmbientSection(AudioTrigger.TriggerAudioAction action)
    {
        EditorGUILayout.PropertyField(ambientTrackProp, new GUIContent("Ambient Track"));

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

            EditorGUILayout.PropertyField(playDelayProp, new GUIContent("Play Delay (sec)"));
            EditorGUILayout.PropertyField(fadeDurationProp, new GUIContent("Fade Duration (sec)"));
        }
    }

    private void PlayPreviewSafe(AudioClip clip)
    {
        if (clip == null) return;

        if (audioUtilType == null)
        {
            audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            playPreviewMethod = audioUtilType.GetMethod(
                "PlayPreviewClip",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic
            );
            stopAllPreviewMethod = audioUtilType.GetMethod(
                "StopAllPreviewClips",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic
            );
        }

        stopAllPreviewMethod?.Invoke(null, null);
        playPreviewMethod?.Invoke(null, new object[] { clip, 0, false });
    }
}
#endif
