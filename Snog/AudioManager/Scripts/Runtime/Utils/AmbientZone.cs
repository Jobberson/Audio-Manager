using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

using Snog.Audio.Attribute;

namespace Snog.Audio.Utils
{
    [AddComponentMenu("Snog/AudioManager/Ambient Zone")]
    [RequireComponent(typeof(Collider))]
    public class AmbientZone : MonoBehaviour
    {
        public enum ZoneMode
        {
            Replace,
            Stack
        }

        // Fix 9: Old `None` in Stack mode popped the token immediately — contradicting its name.
        // The enum is now split into self-documenting values:
        //   None        = truly do nothing. In Stack mode this intentionally leaves the token
        //                 on the stack (the developer is responsible for popping it later).
        //                 Use this if you want the ambient to persist until a manual Pop call.
        //   AutoPop     = Stack mode only: pop the token with the exit fade. Replaces the old
        //                 `None` behaviour in Stack mode (most developers want this).
        //   StopFade    = Replace: ClearAmbient with fade. Stack: pop with exit fade.
        //   StopImmediate = Replace: ClearAmbient instantly. Stack: pop instantly.
        public enum ExitAction
        {
            /// <summary>
            /// Replace mode: do nothing — the current profile stays active.
            /// Stack mode: do nothing — the token remains on the stack. You are responsible
            /// for popping it manually (e.g. via AudioManager.PopAmbientToken).
            /// </summary>
            None,

            /// <summary>
            /// Stack mode only: automatically pop the zone's token when the player exits,
            /// using the exit fade duration. This is the recommended default for Stack mode.
            /// (Has no special meaning in Replace mode — use StopFade or StopImmediate there.)
            /// </summary>
            AutoPop,

            /// <summary>
            /// Replace mode: ClearAmbient with exit fade.
            /// Stack mode: pop the token with exit fade.
            /// </summary>
            StopFade,

            /// <summary>
            /// Replace mode: ClearAmbient instantly.
            /// Stack mode: pop the token instantly.
            /// </summary>
            StopImmediate
        }

        [Header("Zone")]
        [Tooltip("Which profile should be activated when the player enters this zone.")]
        [SerializeField] private AmbientProfile profile;

        [Tooltip("Only colliders with this tag will trigger the zone. Leave empty to accept any tag.")]
        [SerializeField, Tag] private string tagToCompare = "Player";

        [Header("Mode")]
        [SerializeField] private ZoneMode mode = ZoneMode.Stack;

        [Tooltip("Used only in Stack mode. Higher priority wins when voice budget is exceeded.")]
        [SerializeField] private int stackPriority = 0;

        [Header("Enter Behavior")]
        [SerializeField] private bool fadeOnEnter = true;
        [SerializeField] private float enterFadeDuration = 2f;

        [Header("Exit Behavior")]
        [SerializeField] private ExitAction exitAction = ExitAction.AutoPop;
        [SerializeField] private float exitFadeDuration = 2f;

        [Header("Events")]
        [Tooltip("Fired when the first qualifying collider enters this zone.")]
        public UnityEvent onZoneEntered;
        [Tooltip("Fired when the last qualifying collider leaves this zone.")]
        public UnityEvent onZoneExited;

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
            var c = GetComponent<Collider>();
            if (c != null && !c.isTrigger)
                c.isTrigger = true;

            enterFadeDuration = Mathf.Max(0f, enterFadeDuration);
            exitFadeDuration = Mathf.Max(0f, exitFadeDuration);

            // Warn developer if None in Stack mode — likely unintentional token leak.
            if (mode == ZoneMode.Stack && exitAction == ExitAction.None)
            {
                Debug.LogWarning(
                    $"[AmbientZone] '{name}': ExitAction.None in Stack mode means the ambient token " +
                    "will NEVER be automatically cleaned up. If this is intentional, ignore this warning. " +
                    "Otherwise, change Exit Action to AutoPop.",
                    this);
            }
        }
#endif

        private void OnTriggerEnter(Collider other)
        {
            if (!string.IsNullOrEmpty(tagToCompare) && !other.CompareTag(tagToCompare)) return;
            if (!inside.Add(other)) return;
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
                    if (ambientToken >= 0)
                    {
                        manager.PopAmbientToken(ambientToken, 0f);
                        ambientToken = -1;
                    }
                    ambientToken = manager.PushAmbientProfile(profile, stackPriority, fade);
                    break;
            }

            onZoneEntered?.Invoke();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!string.IsNullOrEmpty(tagToCompare) && !other.CompareTag(tagToCompare)) return;
            if (!inside.Remove(other)) return;
            if (inside.Count > 0) return;

            var manager = AudioManager.Instance;
            if (manager == null) return;

            HandleExit(manager, exitAction);
            onZoneExited?.Invoke();
        }

        private void OnDisable()
        {
            if (inside.Count == 0) return;
            inside.Clear();

            var manager = AudioManager.Instance;
            if (manager == null) return;

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
                        case ExitAction.AutoPop:
                            break; // AutoPop is Stack-mode concept; in Replace mode, do nothing.

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
                            // Intentionally left on stack. Developer must pop manually.
                            break;

                        case ExitAction.AutoPop:
                        case ExitAction.StopFade:
                            manager.PopAmbientToken(ambientToken, fade);
                            ambientToken = -1;
                            break;

                        case ExitAction.StopImmediate:
                            manager.PopAmbientToken(ambientToken, 0f);
                            ambientToken = -1;
                            break;
                    }
                    break;
            }
        }

        private float GetEnterFade()
        {
            if (profile != null && profile.defaultFade > 0f)
                return profile.defaultFade;

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
