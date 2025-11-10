using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AudioTrigger))]
public class AudioTriggerEditor : Editor
{
    private AudioTrigger trigger;
    private string[] audioOptions;
    private int selectedIndex;

    private void OnEnable()
    {
        trigger = (AudioTrigger)target;
        RefreshAudioOptions();
    }

    private void RefreshAudioOptions()
    {
        RefreshAudioOptions();
        switch (trigger.selectedAudioType)
        {
            case AudioType.SFX:
                AudioManager.Instance.TryGetSoundNames(out audioOptions);
                break;
            case AudioType.Music:
                AudioManager.Instance.TryGetMusicNames(out audioOptions);
                break;
            case AudioType.Ambient:
                AudioManager.Instance.TryGetAmbientNames(out audioOptions);
                break;
        }

        if (audioOptions == null || audioOptions.Length == 0)
            audioOptions = new[] { "No clips available" };

        selectedIndex = Mathf.Max(0, System.Array.IndexOf(audioOptions, trigger.audio));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("selectedAudioType"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("tagToCompare"));

        RefreshAudioOptions();
        selectedIndex = EditorGUILayout.Popup("Audio", selectedIndex, audioOptions);
        trigger.audio = audioOptions[selectedIndex];

        EditorGUILayout.PropertyField(serializedObject.FindProperty("playDelay"));

        if (trigger.selectedAudioType == AudioType.Music || trigger.selectedAudioType == AudioType.Ambient)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("playWithFade"));

            if (trigger.playWithFade)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("fadeDuration"));
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}