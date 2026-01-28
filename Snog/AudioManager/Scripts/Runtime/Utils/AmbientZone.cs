using System.Collections.Generic;
using UnityEngine;

namespace Snog.Audio.Utils
{
    [AddComponentMenu("Snog/AudioManager/Ambient Zone")]
    [RequireComponent(typeof(Collider))]
    public class AmbientZone : MonoBehaviour
    {
        public enum ZoneMode { Replace, Stack }
        public enum ExitAction { None, StopFade, StopImmediate }

        [Header("Zone")]
        [Tooltip("Which profile should be activated when the player enters this zone.")]
        [SerializeField] private AmbientProfile profile;

        [Tooltip("Only colliders with this tag will trigger the zone. Leave empty to accept any tag.")]
        [SerializeField] private string tagToCompare = "Player";

        [Header("Mode")]
        [SerializeField] private ZoneMode mode = ZoneMode.Stack;
        [Tooltip("Used only in Stack mode. Higher priority wins when voice budget is exceeded.")]
        [SerializeField] private int stackPriority = 0;

        [Header("Enter Behavior")]
        [SerializeField] private bool fadeOnEnter = true;
        [SerializeField] private float enterFadeDuration = 2f;

        [Header("Exit Behavior")]
        [Tooltip(
            "Replace mode:\n" +
            "  None - do nothing\n" +
            "  StopFade - ClearAmbient with exit fade\n" +
            "  StopImmediate - ClearAmbient instantly\n\n" +
            "Stack mode:\n" +
            "  None - Pop token instantly\n" +
            "  StopFade - Pop token with exit fade\n" +
            "  StopImmediate - Pop token instantly"
        )]
        [SerializeField] private ExitAction exitAction = ExitAction.None;
        [SerializeField] private float exitFadeDuration = 2f;

        [Header("Gizmos")]
        [SerializeField] private Color gizmoColor = new(0.2f, 0.7f, 0.4f, 0.25f);
        [SerializeField] private Color gizmoWireColor = new(0.2f, 0.7f, 0.4f, 1f);

        private readonly HashSet<Collider> inside = new HashSet<Collider>();
        private int ambientToken = -1;

        private void Reset()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Ensure collider is a trigger for zone behavior while editing
            var c = GetComponent<Collider>();
            if (c != null && !c.isTrigger)
            {
                c.isTrigger = true;
            }
        }
#endif

        private void OnTriggerEnter(Collider other)
        {
            // If tagToCompare is set, require it. If empty, accept any collider.
            if (!string.IsNullOrEmpty(tagToCompare) && !other.CompareTag(tagToCompare))
            {
                return;
            }
            

            // Add collider; if already present, ignore
            if (!inside.Add(other)) return;

            // Only react on first entering collider (supports multi-collider characters)
            if (inside.Count > 1) return;

            var manager = AudioManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[AmbientZone] No AudioManager.Instance found.", this);
                return;
            }

            if (profile == null)
            {
                Debug.LogWarning("[AmbientZone] No AmbientProfile assigned.", this);
                return;
            }

            float fade = fadeOnEnter ? GetEnterFade() : 0f;

            switch (mode)
            {
                case ZoneMode.Replace:
                    manager.SetAmbientProfile(profile, fade);
                    break;

                case ZoneMode.Stack:
                    // Ensure we don't leak tokens if re-enter happens oddly
                    if (ambientToken >= 0)
                    {
                        manager.PopAmbientToken(ambientToken, 0f);
                        ambientToken = -1;
                    }

                    ambientToken = manager.PushAmbientProfile(profile, stackPriority, fade);
                    break;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!string.IsNullOrEmpty(tagToCompare) && !other.CompareTag(tagToCompare)) return;

            if (!inside.Remove(other)) return;

            // Only react when the last collider exits
            if (inside.Count > 0) return;

            var manager = AudioManager.Instance;
            if (manager == null) return;

            HandleExit(manager, exitAction);
        }

        private void OnDisable()
        {
            // Safety: if zone is disabled while player is inside, avoid leaving ambience stuck.
            if (inside.Count == 0) return;

            inside.Clear();

            var manager = AudioManager.Instance;
            if (manager == null) return;

            // Disable should never leave stack entries around.
            HandleExit(manager, ExitAction.StopImmediate);
        }

        private void HandleExit(AudioManager manager, ExitAction action)
        {
            float fade = Mathf.Max(0f, exitFadeDuration);

            switch (mode)
            {
                case ZoneMode.Replace:
                    switch (action)
                    {
                        case ExitAction.None:
                            break;
                        case ExitAction.StopFade:
                            manager.ClearAmbient(fade);
                            break;
                        case ExitAction.StopImmediate:
                            manager.ClearAmbient(0f);
                            break;
                    }
                    break;

                case ZoneMode.Stack:
                    if (ambientToken < 0) break;

                    switch (action)
                    {
                        case ExitAction.None:
                            // Stack mode should always clean up its own token.
                            manager.PopAmbientToken(ambientToken, 0f);
                            break;
                        case ExitAction.StopFade:
                            manager.PopAmbientToken(ambientToken, fade);
                            break;
                        case ExitAction.StopImmediate:
                            manager.PopAmbientToken(ambientToken, 0f);
                            break;
                    }

                    ambientToken = -1;
                    break;
            }
        }

        private float GetEnterFade()
        {
            if (profile != null && profile.defaultFade > 0f) return profile.defaultFade;
            return Mathf.Max(0f, enterFadeDuration);
        }

        private void OnDrawGizmos()
        {
            var prevMatrix = Gizmos.matrix;
            var prevColor = Gizmos.color;

            Gizmos.color = gizmoColor;

            Collider col = GetComponent<Collider>();
            if (col == null)
            {
                Gizmos.color = gizmoWireColor;
                Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
            }
            else
            {
                var t = col.transform;
                // Use lossyScale to compute sizes, and set matrix to transform so shapes draw in local space
                var lossy = t.lossyScale;

                if (col is BoxCollider box)
                {
                    Vector3 size = Vector3.Scale(box.size, lossy);
                    Gizmos.matrix = Matrix4x4.TRS(t.TransformPoint(box.center), t.rotation, Vector3.one);
                    Gizmos.DrawCube(Vector3.zero, size);

                    Gizmos.color = gizmoWireColor;
                    Gizmos.DrawWireCube(Vector3.zero, size);
                }
                else if (col is SphereCollider sphere)
                {
                    float radius = sphere.radius * Mathf.Max(lossy.x, Mathf.Max(lossy.y, lossy.z));
                    Gizmos.matrix = Matrix4x4.TRS(t.TransformPoint(sphere.center), t.rotation, Vector3.one);
                    Gizmos.DrawSphere(Vector3.zero, radius);

                    Gizmos.color = gizmoWireColor;
                    Gizmos.DrawWireSphere(Vector3.zero, radius);
                }
                else if (col is CapsuleCollider capsule)
                {
                    // This is a simple visual approximation that works for most cases.
                    float radius = capsule.radius * Mathf.Max(lossy.x, lossy.z);
                    float height = capsule.height * lossy.y;

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

            Gizmos.matrix = prevMatrix;
            Gizmos.color = prevColor;
        }
    }
}
