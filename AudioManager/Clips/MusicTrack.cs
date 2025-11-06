using UnityEngine;

namespace Snog.Audio.Clips
{
    [CreateAssetMenu(fileName = "MusicTrack", menuName = "AudioManager/MusicTrack")]
    public class MusicTrack : ScriptableObject
    {
        [Header("Identification")]
        public string trackName;

        [Header("Audio")]
        public AudioClip clip;
        public bool loop = true;

        [Header("Metadata")]
        [TextArea] public string description;
    }
}