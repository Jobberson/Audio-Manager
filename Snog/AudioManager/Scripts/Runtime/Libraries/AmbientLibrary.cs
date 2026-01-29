﻿using UnityEngine;

namespace Snog.Audio.Clips
{
    [CreateAssetMenu(fileName = "AmbientTrack", menuName = "Snog/AudioManager/AmbientTrack")]
    public class AmbientTrack : ScriptableObject
    {
        [Header("Identification")]
        [Tooltip("Unique key used to reference this track in profiles and emitters.")]
        public string trackName;

        [Tooltip("Optional tag for filtering/grouping (e.g., Forest, Cave, Night).")]
        public string moodTag;

        [Header("Audio")]
        public AudioClip clip;

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
                trackName = name;

            trackName = trackName.Trim();

            if (!string.IsNullOrWhiteSpace(moodTag))
                moodTag = moodTag.Trim();

            defaultVolume = Mathf.Clamp01(defaultVolume);
        }
#endif
    }
}