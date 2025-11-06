using UnityEngine;

public enum AudioType
{
    SFX, 
    Ambient,
    Music
}

public class AudioTrigger : MonoBehaviour
{
    [SerializeField][Tooltip("What audio to play")] private string audio;
    public AudioType selectedAudioType;
    [SerializeField][Tooltip("Optional delay before playing (in seconds)")] private float playDelay = 0f;
    [SerializeField][Tooltip("ONLY FOR AMBIENT AND MUSIC")] private bool playWithFade;
    [SerializeField][Tooltip("Fade duration (only used if playWithFade is true)")] private float fadeDuration = 2f;

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var manager = FindAnyObjectByType<AudioManager>();
        if (manager == null)
        {
            Debug.LogWarning("AudioManager not found in scene.");
            return;
        }

        switch (selectedAudioType)
        {
            case AudioType.SFX:
                manager.PlaySound2D(audio);
                break;

            case AudioType.Music:
                if (playWithFade)
                    manager.StartCoroutine(manager.PlayMusicFade(audio, fadeDuration));
                else
                    manager.PlayMusic(audio, playDelay);
                break;

            case AudioType.Ambient:
                if (playWithFade)
                    manager.StartCoroutine(manager.PlayAmbientFade(audio, fadeDuration));
                else
                    manager.PlayAmbient(audio, playDelay);
                break;
        }
    }
}