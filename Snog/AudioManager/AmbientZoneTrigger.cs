using UnityEngine;

namespace Snog.Audio
{
    public class AmbientZoneTrigger : MonoBehaviour
    {
        [Header("Ambient Clip Name (must match library or Resources path)")]
        public string ambientClipName;

        [Header("Fade Settings")]
        public float fadeDuration = 2f;
        public bool useResources = false;

        [Header("Trigger Settings")]
        public string tagToCompare = "Player";

        private bool isInside = false;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(tagToCompare) || isInside) return;
            isInside = true;

            if (useResources)
            {
                AudioManager.Instance.LoadAndPlayAmbientAsyncFromResources(ambientClipName);
            }
            else
            {
                AudioManager.Instance.StartCoroutine(AudioManager.Instance.PlayAmbientFade(ambientClipName, fadeDuration));
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(tagToCompare) || !isInside) return;
            isInside = false;

            AudioManager.Instance.StartCoroutine(AudioManager.Instance.StopAmbientFade(fadeDuration));
        }
    }
}