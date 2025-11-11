using UnityEngine;

namespace Snog.Audio.Clips
{
    [CreateAssetMenu(fileName = "SoundClipData", menuName = "AudioManager/SoundClipData")]
    public class SoundClipData : ScriptableObject
    {
        public string soundName;
        public AudioClip[] clips;
    }
}