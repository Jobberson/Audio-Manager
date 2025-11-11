
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Snog.Audio;

public class AudioManagerSetupWizard : EditorWindow
{
    private AudioManager manager;
    private string mixerName;
    private AudioMixer selectedMixer;
    private bool mixerCreated = false;
    private bool librariesCreated = false;
    private bool assigned = false;
    private bool scanned = false;

    [MenuItem("Tools/AudioManager Setup Wizard")]
    public static void ShowWindow()
    {
        GetWindow<AudioManagerSetupWizard>("AudioManager Setup Wizard");
    }

    private void OnGUI()
    {
        GUILayout.Label("AudioManager Setup Wizard", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("This wizard helps you set up the AudioManager with mixer, groups, and audio folders.", MessageType.Info);
        GUILayout.Space(10);

        manager = (AudioManager)EditorGUILayout.ObjectField("AudioManager Prefab", manager, typeof(AudioManager), true);
        
        GUILayout.Space(10);
        GUILayout.Label("Step 1: Assign Audio Mixer", EditorStyles.boldLabel);

        selectedMixer = (AudioMixer)EditorGUILayout.ObjectField("Assign Existing Mixer", selectedMixer, typeof(AudioMixer), false);
        mixerName = EditorGUILayout.TextField("Mixer Name", mixerName);
        if (GUILayout.Button("Assign Audio Mixer"))
        {
            if (manager != null && selectedMixer != null)
            {
                manager.mainMixer = selectedMixer;
                EditorUtility.SetDirty(manager);
                mixerCreated = true;
            }
        }
        EditorGUILayout.LabelField("Status:", mixerCreated ? "✔ Mixer Created" : "✖ Not Created");
        
        GUILayout.Space(10);
        GUILayout.Label("Step 2: Assign Mixer and Libraries", EditorStyles.boldLabel);
        if (GUILayout.Button("Auto-Assign Mixer & Groups"))
        {
            if (manager != null)
            {
                manager.AutoAssignMixerAndGroups();
                manager.AssignToLibraries();
                assigned = true;
                EditorUtility.SetDirty(manager);
                Debug.Log("Mixer and groups assigned.");
            }
            else
            {
                Debug.LogWarning("Assign an AudioManager prefab or instance first.");
            }
        }
        EditorGUILayout.LabelField("Status:", assigned ? "✔ Assigned" : "✖ Not Assigned");

        GUILayout.Space(10);
        GUILayout.Label("Step 3: Scan/Create Audio Folder", EditorStyles.boldLabel);
        if(GUILayout.Button("Scan/Create Folder"))
        {
            if (manager != null)
            {
                manager.ScanFolders();
                scanned = true;
                EditorUtility.SetDirty(manager);
                Debug.Log("Audio Folder Scanned");
            }
            else
            {
                Debug.LogWarning("Assign an AudioManager prefab or instance first.");
            }
        }
        EditorGUILayout.LabelField("Status:", scanned ? "✔ Scanned" : "✖ Not Scanned");
    }
}
