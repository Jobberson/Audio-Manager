using UnityEngine;

public enum AudioType
{
    SFX, 
    Ambient,
    Music
}

public class AudioTrigger : MonoBehaviour
{
    [SerializeField][Tooltip("What audio to play")] private string clip;
    public [Tooltip("What tag should be used to check")] private string tagToCompare;
    public AudioType selectedAudioType;
    [SerializeField][Tooltip("Optional delay before playing (in seconds)")] private float playDelay = 0f;
    [SerializeField][Tooltip("ONLY FOR AMBIENT AND MUSIC")] private bool playWithFade;
    [SerializeField][Tooltip("Fade duration (only used if playWithFade is true)")] private float fadeDuration = 2f;

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

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