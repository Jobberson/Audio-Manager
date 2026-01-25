using UnityEngine;

namespace Snog.Audio.Demo
{
    public class DemoInteractor : MonoBehaviour
    {
        public float interactDistance = 3f;
        public KeyCode interactKey = KeyCode.E;

        void Update()
        {
            if (Input.GetKeyDown(interactKey))
            {
                TryInteract();
            }
        }

        void TryInteract()
        {
            Ray ray = new(transform.position, transform.forward);

            if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
            {
                var interactable = hit.collider.GetComponent<IInteractable>();

                interactable?.Interact(hit.point);
            }
        }
    }
}