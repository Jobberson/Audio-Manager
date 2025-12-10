using UnityEngine;
using Snog.Audio.Clips;

namespace Snog.Audio.Layers
{
    [System.Serializable]
    public class AmbientLayer
    {
        [Header("Clip")]
        public AmbientTrack track;

        [Header("Mix")]
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0f, 1f)] public float spatialBlend = 0f;

        [Header("Playback")]
        public bool loop = true;
        public bool randomStartTime = true;

        [Header("Pitch (random)")]
        public Vector2 pitchRange = new Vector2(1f, 1f);
    }

    [CreateAssetMenu(fileName = "AmbientProfile", menuName = "AudioManager/AmbientProfile")]
    public class AmbientProfile : ScriptableObject
    {
        [Header("Identification")]
        public string profileName = "Ambient Profile";

        [Header("Layers")]
        public AmbientLayer[] layers;

        [Header("Defaults")]
        [Tooltip("Default fade used when not specified in calls")]
        public float defaultFade = 2f;
    }
}
