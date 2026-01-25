using UnityEngine;

namespace Snog.Audio.Demo
{
    public class SfxButton : MonoBehaviour, IInteractable
    {
        [Header("SFX Settings")]
        [SerializeField] private string sfxName = "ButtonPressSample";
        [SerializeField] private bool playIn3D = true;

        public void Interact(Vector3 hitPoint)
        {
            Debug.Log("SfxButton interacted with at point: " + hitPoint);
            if (AudioManager.Instance == null)
                return;

            if (playIn3D)
                AudioManager.Instance.PlaySfx3D(sfxName, hitPoint);
            else
                AudioManager.Instance.PlaySfx2D(sfxName);
        }
    }
}
