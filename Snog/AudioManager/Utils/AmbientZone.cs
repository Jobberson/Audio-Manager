
using UnityEngine;
using Snog.Audio;
using Snog.Audio.Layers;

[AddComponentMenu("Audio/Ambient Zone")]
public class AmbientZone : MonoBehaviour
{
    [Header("Zone")]
    [Tooltip("Which profile should be activated when the player enters this zone.")]
    [SerializeField] private AmbientProfile profile;

    [Tooltip("Only colliders with this tag will trigger the zone.")]
    [SerializeField] private string TagToCompare = "Player";

    [Header("Enter Behavior")]
    [Tooltip("If true, crossfades from the current profile to the target profile. Otherwise, switches instantly.")]
    [SerializeField] private bool crossfadeOnEnter = true;

    [Tooltip("Fade duration used on enter when crossfading.")]
    [SerializeField] private float enterFadeDuration = 2f;

    [Header("Exit Behavior")]
    [Tooltip("What happens on exit:\nNone - do nothing\nStopFade - fade out all ambient layers\nStopImmediate - stop all ambient layers instantly")]
    [SerializeField] private ExitAction exitAction = ExitAction.None;

    [Tooltip("Fade duration used on exit when StopFade is selected.")]
    [SerializeField] private float exitFadeDuration = 2f;

    [Header("Gizmos")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 0.7f, 0.4f, 0.25f);
    [SerializeField] private Color gizmoWireColor = new Color(0.2f, 0.7f, 0.4f, 1f);

    public enum ExitAction
    {
        None,
        StopFade,
        StopImmediate
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(TagToCompare))
            return;

        var manager = AudioManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("No AudioManager.Instance found.", this);
            return;
        }

        if (profile == null)
        {
            Debug.LogWarning("No AmbientProfile assigned.", this);
            return;
        }

        if (crossfadeOnEnter)
        {
            float duration = Mathf.Max(0f, GetEnterFade());
            manager.StartCoroutine(manager.CrossfadeAmbientProfile(profile, duration));
        }
        else
        {
            manager.PlayAmbientProfile(profile);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(TagToCompare))
            return;

        var manager = AudioManager.Instance;
        if (manager == null)
            return;

        switch (exitAction)
        {
            case ExitAction.None:
                break;

            case ExitAction.StopFade:
                manager.StartCoroutine(manager.StopAmbientProfileFade(Mathf.Max(0f, exitFadeDuration)));
                break;

            case ExitAction.StopImmediate:
                manager.StopAmbientProfileImmediate();
                break;
        }
    }

    private float GetEnterFade()
    {
        if (profile != null && profile.defaultFade > 0f)
            return profile.defaultFade;

        return enterFadeDuration;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        var col = GetComponent<Collider>();
        if (col is BoxCollider box)
        {
            var t = box.transform;
            var size = Vector3.Scale(box.size, t.lossyScale);
            Gizmos.matrix = Matrix4x4.TRS(t.TransformPoint(box.center), t.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, size);

            Gizmos.color = gizmoWireColor;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
        else if (col is SphereCollider sphere)
        {
            var t = sphere.transform;
            float radius = sphere.radius * Mathf.Max(t.lossyScale.x, Mathf.Max(t.lossyScale.y, t.lossyScale.z));
            Gizmos.matrix = Matrix4x4.TRS(t.TransformPoint(sphere.center), t.rotation, Vector3.one);
            Gizmos.DrawSphere(Vector3.zero, radius);

            Gizmos.color = gizmoWireColor;
            Gizmos.DrawWireSphere(Vector3.zero, radius);
        }
        else if (col is CapsuleCollider capsule)
        {
            var t = capsule.transform;
            float radius = capsule.radius * Mathf.Max(t.lossyScale.x, t.lossyScale.z);
            float height = capsule.height * t.lossyScale.y;

            Gizmos.matrix = Matrix4x4.TRS(t.TransformPoint(capsule.center), t.rotation, Vector3.one);
            Gizmos.color = gizmoColor;

            Gizmos.DrawCube(Vector3.zero, new Vector3(radius * 2f, Mathf.Max(0f, height - radius * 2f), radius * 2f));

            Gizmos.DrawSphere(new Vector3(0f, height * 0.5f - radius, 0f), radius);
            Gizmos.DrawSphere(new Vector3(0f, -height * 0.5f + radius, 0f), radius);

            Gizmos.color = gizmoWireColor;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(radius * 2f, Mathf.Max(0f, height - radius * 2f), radius * 2f));
            Gizmos.DrawWireSphere(new Vector3(0f, height * 0.5f - radius, 0f), radius);
            Gizmos.DrawWireSphere(new Vector3(0f, -height * 0.5f + radius, 0f), radius);
        }
        else
        {
            Gizmos.color = gizmoWireColor;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
        }
    }
}
