using UnityEngine;

namespace Snog.Audio.Clips
{
    [CreateAssetMenu(fileName = "SoundClipData", menuName = "Snog/AudioManager/SoundClipData")]
    public class SoundClipData : ScriptableObject
    {
        public string soundName;
        public AudioClip[] clips;
    }
}