using UnityEngine;
namespace Snog.Audio.Demo
{
    public interface IInteractable
    {
        void Interact(Vector3 hitPoint);
    }
}