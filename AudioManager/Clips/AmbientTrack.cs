using UnityEngine;

namespace Snog.Audio.Clips
{
    [CreateAssetMenu(fileName = "AmbientTrack", menuName = "AudioManager/AmbientTrack")]
    public class AmbientTrack : ScriptableObject
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