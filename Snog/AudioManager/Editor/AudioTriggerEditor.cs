using UnityEditor;
using UnityEngine;
using Snog.Audio;

[CustomEditor(typeof(AudioTrigger))]
public class AudioTriggerEditor : Editor
{
    private AudioTrigger trigger;
    private string[] audioOptions = new string[0];
    private int selectedIndex;
    private bool optionsValid;

    private SerializedProperty selectedAudioTypeProp;
    private SerializedProperty tagToCompareProp;
    private SerializedProperty clipProp;
    private SerializedProperty playDelayProp;
    private SerializedProperty playWithFadeProp;
    private SerializedProperty fadeDurationProp;

    private void OnEnable()
    {
        trigger = (AudioTrigger)target;

        selectedAudioTypeProp = serializedObject.FindProperty("selectedAudioType");
        tagToCompareProp = serializedObject.FindProperty("TagToCompare");
        clipProp = serializedObject.FindProperty("clip"); // <-- match field name in AudioTrigger
        playDelayProp = serializedObject.FindProperty("playDelay");
        playWithFadeProp = serializedObject.FindProperty("playWithFade");
        fadeDurationProp = serializedObject.FindProperty("fadeDuration");

        RefreshAudioOptions();
    }

    private void RefreshAudioOptions()
    {
        var manager = FindAnyObjectByType<AudioManager>();
        optionsValid = false;

        if (manager == null)
        {
            audioOptions = new[] { "No AudioManager found" };
            selectedIndex = 0;
            return;
        }

        // Read selectedAudioType from the serialized property so changes in inspector are respected immediately
        AudioType selType = (AudioType)selectedAudioTypeProp.enumValueIndex;

        switch (selType)
        {
            case AudioType.SFX:
                manager.TryGetSoundNames(out audioOptions);
                break;
            case AudioType.Music:
                manager.TryGetMusicNames(out audioOptions);
                break;
            case AudioType.Ambient:
                manager.TryGetAmbientNames(out audioOptions);
                break;
            default:
                audioOptions = new string[0];
                break;
        }

        if (audioOptions == null || audioOptions.Length == 0)
        {
            audioOptions = new[] { "No clips available" };
            selectedIndex = 0;
            return;
        }

        optionsValid = true;

        string currentName = clipProp != null ? clipProp.stringValue : string.Empty;
        int found = System.Array.IndexOf(audioOptions, currentName);
        selectedIndex = Mathf.Clamp(found >= 0 ? found : 0, 0, audioOptions.Length - 1);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw and edit the enum via serialized property
        EditorGUILayout.PropertyField(selectedAudioTypeProp);
        EditorGUILayout.PropertyField(tagToCompareProp);

        // Refresh based on the (possibly changed) serialized enum
        RefreshAudioOptions();

        // Popup and write to serialized property (only when we have valid options)
        selectedIndex = EditorGUILayout.Popup("Audio", selectedIndex, audioOptions);

        if (clipProp != null && optionsValid)
        {
            clipProp.stringValue = audioOptions[selectedIndex];
        }
        // If options aren't valid (no manager or no clips) we avoid overwriting the stored clip.

        EditorGUILayout.PropertyField(playDelayProp);

        // Use the serialized enum and serialized playWithFade property for correct behavior
        int enumIdx = selectedAudioTypeProp.enumValueIndex;
        if (enumIdx == (int)AudioType.Music || enumIdx == (int)AudioType.Ambient)
        {
            EditorGUILayout.PropertyField(playWithFadeProp);
            if (playWithFadeProp.boolValue)
            {
                EditorGUILayout.PropertyField(fadeDurationProp);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
