using UnityEngine;
using UnityEngine.Events;

namespace Snog.Audio
{
    public class AudioEventTrigger : MonoBehaviour
    {
        [Header("Audio Clip Name")]
        public string clipName;

        [Header("Audio Type")]
        public AudioType audioType = AudioType.SFX;

        [Header("Optional Settings")]
        public bool stopInsteadOfPlay = false;
        public bool fadeOutInsteadOfPlay = false;
        public float delay = 0f;
        public float fadeDuration = 2f;
        public bool useFade = false;

        [Header("Trigger")]
        public UnityEvent onTrigger;

        public void TriggerAudio()
        {
            if (stopInsteadOfPlay)
            {
                if (audioType == AudioType.Music) AudioEvents.StopMusic();
                else if (audioType == AudioType.Ambient) AudioEvents.StopAmbient();
                return;
            }

            if (fadeOutInsteadOfPlay)
            {
                if (audioType == AudioType.Music) AudioEvents.FadeOutMusic(fadeDuration);
                else if (audioType == AudioType.Ambient) AudioEvents.FadeOutAmbient(fadeDuration);
                return;
            }

            switch (audioType)
            {
                case AudioType.SFX:
                    AudioEvents.PlaySFX(clipName);
                    break;
                case AudioType.Music:
                    if (useFade)
                        AudioEvents.FadeInMusic(clipName, fadeDuration);
                    else
                        AudioEvents.PlayMusic(clipName, delay);
                    break;
                case AudioType.Ambient:
                    if (useFade)
                        AudioEvents.FadeInAmbient(clipName, fadeDuration);
                    else
                        AudioEvents.PlayAmbient(clipName, delay);
                    break;
            }

            onTrigger?.Invoke();
        }
    }
}