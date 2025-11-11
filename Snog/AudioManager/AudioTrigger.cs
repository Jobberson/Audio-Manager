using UnityEngine;
using Snog.Audio;

public enum AudioType
{
    SFX, 
    Ambient,
    Music
}

public class AudioTrigger : MonoBehaviour
{
    [SerializeField][Tooltip("What audio to play")] private string clip;
    public AudioType selectedAudioType;
    [SerializeField][Tooltip("Optional delay before playing (in seconds)")] private float playDelay = 0f;
    [Tooltip("ONLY FOR AMBIENT AND MUSIC")] public bool playWithFade;
    [SerializeField][Tooltip("Fade duration (only used if playWithFade is true)")] private float fadeDuration = 2f;
    public string TagToCompare = "Player";

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(TagToCompare)) return;

        switch (selectedAudioType)
        {
            case AudioType.SFX:
                AudioManager.Instance.PlaySound2D(clip);
                break;

            case AudioType.Music:
                if (playWithFade)
                    AudioManager.Instance.StartCoroutine(AudioManager.Instance.PlayMusicFade(clip, fadeDuration));
                else
                    AudioManager.Instance.PlayMusic(clip, playDelay);
                break;

            case AudioType.Ambient:
                if (playWithFade)
                    AudioManager.Instance.StartCoroutine(AudioManager.Instance.PlayAmbientFade(clip, fadeDuration));
                else
                    AudioManager.Instance.PlayAmbient(clip, playDelay);
                break;
        }
    }
}