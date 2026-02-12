using UnityEngine;

namespace Snog.Audio.Clips
{
    [CreateAssetMenu(fileName = "SoundClipData", menuName = "Snog/AudioManager/SoundClipData")]
    public class SoundClipData : ScriptableObject
    {
        [Header("Identification")]
        [Tooltip("Unique key used to play this sound via SoundLibrary/AudioManager.")]
        public string soundName;

        [Header("Clips")]
        [Tooltip("One or more variants. SoundLibrary will pick a random one.")]
        public AudioClip[] clips;

        [Header("Defaults")]
        [Range(0f, 1f)]
        public float defaultVolume = 1f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(soundName))
                soundName = name;

            soundName = soundName.Trim();
            defaultVolume = Mathf.Clamp01(defaultVolume);

            if (clips != null && clips.Length > 0)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null)
                    {
                        break;
                    }
                }

            }
        }
#endif
    }
}