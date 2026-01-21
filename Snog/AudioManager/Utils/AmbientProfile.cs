
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


        [Tooltip("Higher priority layers are kept when voice budget is exceeded.")]
        public int priority = 0;

        [Header("Playback")]
        public bool loop = true;
        public bool randomStartTime = true;

        [Header("Pitch (random)")]
        public Vector2 pitchRange = new Vector2(1f, 1f);

        public void Validate()
        {
            volume = Mathf.Clamp01(volume);

            if (pitchRange.x <= 0f) pitchRange.x = 0.01f;
            if (pitchRange.y <= 0f) pitchRange.y = 0.01f;

            if (pitchRange.y < pitchRange.x)
            {
                float tmp = pitchRange.x;
                pitchRange.x = pitchRange.y;
                pitchRange.y = tmp;
            }
        }
    }

    [CreateAssetMenu(fileName = "AmbientProfile", menuName = "Snog/AudioManager/AmbientProfile")]
    public class AmbientProfile : ScriptableObject
    {
        [Header("Identification")]
        public string profileName = "Ambient Profile";

        [Header("Layers")]
        public AmbientLayer[] layers;

        [Header("Defaults")]
        [Tooltip("Default fade used when not specified in calls")]
        [Min(0f)] public float defaultFade = 2f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            defaultFade = Mathf.Max(0f, defaultFade);

            if (layers == null) return;

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null) continue;
                layers[i].Validate();
            }
        }
#endif
    }
}
