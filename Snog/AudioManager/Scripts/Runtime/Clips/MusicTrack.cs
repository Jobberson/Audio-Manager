using UnityEngine;

namespace Snog.Audio.Clips
{
    [CreateAssetMenu(fileName = "MusicTrack", menuName = "Snog/AudioManager/MusicTrack")]
    public class MusicTrack : ScriptableObject
    {
        [Header("Identification")]
        [Tooltip("Unique key used to play this track via MusicLibrary/AudioManager.")]
        public string trackName;

        [Tooltip("Optional tag for filtering/grouping (e.g., Combat, Calm, Menu).")]
        public string moodTag;

        [Header("Audio")]
        public AudioClip clip;

        public bool loop = true;

        [Header("Defaults")]
        [Range(0f, 1f)]
        public float defaultVolume = 1f;

        [Header("Metadata")]
        [TextArea]
        public string description;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(trackName))
            {
                trackName = name;
            }

            defaultVolume = Mathf.Clamp01(defaultVolume);
        }
#endif
    }
}