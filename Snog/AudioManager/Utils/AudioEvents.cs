using UnityEngine;

namespace Snog.Audio
{
    public static class AudioEvents
    {
        public static void PlaySFX(string clipName)
        {
            AudioManager.Instance?.PlaySound2D(clipName);
        }

        public static void PlaySFX3D(string clipname, Vector3 position)
        {
            AudioManager.Instance?.PlaySound3D(clipName, position);
        }

        public static void PlayMusic(string clipName, float delay = 0f)
        {
            AudioManager.Instance?.PlayMusic(clipName, delay);
        }

        public static void PlayAmbient(string clipName, float delay = 0f)
        {
            AudioManager.Instance?.PlayAmbient(clipName, delay);
        }

        public static void FadeInMusic(string clipName, float duration = 2f)
        {
            AudioManager.Instance?.StartCoroutine(AudioManager.Instance.PlayMusicFade(clipName, duration));
        }

        public static void FadeOutMusic(float duration = 2f)
        {
            AudioManager.Instance?.StartCoroutine(AudioManager.Instance.StopMusicFade(duration));
        }

        public static void FadeInAmbient(string clipName, float duration = 2f)
        {
            AudioManager.Instance?.StartCoroutine(AudioManager.Instance.PlayAmbientFade(clipName, duration));
        }

        public static void FadeOutAmbient(float duration = 2f)
        {
            AudioManager.Instance?.StartCoroutine(AudioManager.Instance.StopAmbientFade(duration));
        }

        public static void StopMusic()
        {
            AudioManager.Instance?.StopMusic();
        }

        public static void StopAmbient()
        {
            AudioManager.Instance?.StopAmbient();
        }
    }
}