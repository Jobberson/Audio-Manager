using UnityEngine;

namespace Snog.Audio.Demo
{
    public class MusicButton : MonoBehaviour, IInteractable
    {
        [Header("Music Settings")]
        [SerializeField] private string musicName = "MusicSample";
        [SerializeField] private bool isStopButton = true;

        [Header("Timing")]
        [Min(0f)]
        [SerializeField] private float fadeDuration = 2f;
        [Min(0f)]
        [SerializeField] private float playDelay = 0f;

        public void Interact(Vector3 hitPoint)
        {
            Debug.Log("MusicButton interacted with at point: " + hitPoint);

            if (AudioManager.Instance == null)
            {
                Debug.LogWarning("MusicButton: AudioManager.Instance is null.");
                return;
            }

            if (!isStopButton)
            {
                if (string.IsNullOrEmpty(musicName))
                {
                    Debug.LogWarning("MusicButton: musicName is empty.");
                    return;
                }

                AudioManager.Instance.PlayMusic(musicName, playDelay, fadeDuration);
            }
            else
            {
                AudioManager.Instance.StopMusic(fadeDuration);
            }
        }
    }
}
