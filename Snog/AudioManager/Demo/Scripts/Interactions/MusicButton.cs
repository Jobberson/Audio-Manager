using UnityEngine;

namespace Snog.Audio.Demo
{
    public class MusicButton : MonoBehaviour, IInteractable
    {
        [Header("SFX Settings")]
        [SerializeField] private string musicName = "MusicSample";
        [SerializeField] private bool stopMusic = true;

        public void Interact(Vector3 hitPoint)
        {
            Debug.Log("MusicButton interacted with at point: " + hitPoint);
            if (AudioManager.Instance == null)
                return;

            if (!stopMusic)
                AudioManager.Instance.PlayMusic(musicName, 0f, 2f);
            else
                AudioManager.Instance.StopMusic(2f);
        }
    }
}
