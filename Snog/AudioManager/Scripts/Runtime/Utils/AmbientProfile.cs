using UnityEngine;
using Snog.Audio.Clips;

namespace Snog.Audio.Utils
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

        // --- Fix 4: Playback fields now gate behind overridePlayback so they actually do something.
        //     When false (default), the AmbientEmitter's own settings are used.
        //     When true, these values are passed to the emitter each time it is activated by this layer.
        [Header("Playback Override")]
        [Tooltip(
            "When enabled, the values below override the AmbientEmitter's own playback settings " +
            "for as long as this layer is the winning layer for that emitter's track. " +
            "When disabled (default), the emitter's own Playback settings are used.")]
        public bool overridePlayback = false;

        [Tooltip("Override: randomise the AudioSource start time so loops don't all begin at 0.")]
        public bool randomStartTime = true;

        [Tooltip("Override: random pitch range applied when the emitter starts playing.")]
        public Vector2 pitchRange = new(1f, 1f);

        public void Validate()
        {
            volume = Mathf.Clamp01(volume);

            if (float.IsNaN(pitchRange.x) || float.IsInfinity(pitchRange.x)) pitchRange.x = 1f;
            if (float.IsNaN(pitchRange.y) || float.IsInfinity(pitchRange.y)) pitchRange.y = 1f;
            if (pitchRange.x <= 0f) pitchRange.x = 0.01f;
            if (pitchRange.y <= 0f) pitchRange.y = 0.01f;

            if (pitchRange.y < pitchRange.x)
                (pitchRange.y, pitchRange.x) = (pitchRange.x, pitchRange.y);
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
        [Min(0f)]
        public float defaultFade = 2f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            defaultFade = Mathf.Max(0f, defaultFade);

            if (layers == null)
            {
                layers = new AmbientLayer[0];
                return;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null) continue;
                layers[i].Validate();
            }
        }
#endif
    }
}
