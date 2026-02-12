using System.Collections;
using UnityEngine;
using Snog.Audio;
using Snog.Audio.Clips;
using Snog.Audio.Utils;
using Snog.Audio.Attribute;

namespace Snog.Scripts
{
    [AddComponentMenu("Snog/AudioManager/Audio Trigger")]
    [RequireComponent(typeof(Collider))]
    public class AudioTrigger : MonoBehaviour
    {
        public enum TriggerAudioType
        {
            SFX,
            Music,
            Ambient
        }

        public enum TriggerAudioAction
        {
            Play2D,
            Play3D,
            Play,
            PlayFadeIn,
            Stop,
            StopMusic,
            PopAmbient,
            Snapshot
        }

        [Header("Trigger")]
        [SerializeField, Tag] private string TagToCompare = "Player";
        [SerializeField] private bool fireOnEnter = true;
        [SerializeField] private bool fireOnExit = false;

        [Header("Audio")]
        [SerializeField] private TriggerAudioType audioType = TriggerAudioType.SFX;
        [SerializeField] private TriggerAudioAction action = TriggerAudioAction.Play2D;

        [Header("SFX")]
        [SerializeField] private SoundClipData sfxClip;

        [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

        [Header("Music")]
        [SerializeField] private MusicTrack musicTrack;

        [Header("Ambient")]
        [SerializeField] private AmbientTrack ambientTrack;

        [Header("Snapshots")]
        [SerializeField] private string snapshotName = "Default";

        [Tooltip("Optional: Assign a reusable AmbientProfile asset to avoid runtime allocations. If null, a runtime profile will be cached and used.")]
        [SerializeField] private AmbientProfile ambientProfileAsset;

        [Header("Timing")]
        [SerializeField] private float playDelay = 0f;
        [SerializeField] private float fadeDuration = 1f;

        [Header("3D")]
        [SerializeField] private bool useOverride3DPosition = false;
        [SerializeField] private Vector3 override3DPosition = Vector3.zero;

        private int ambientToken = -1;
        private Coroutine routine;
        private AmbientProfile cachedRuntimeProfile;

        private void Reset()
        {
            if (TryGetComponent<Collider>(out var c))
                c.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!fireOnEnter)
                return;

            if (!IsValidTarget(other))
                return;

            Trigger(other.transform.position);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!fireOnExit)
                return;

            if (!IsValidTarget(other))
                return;

            Trigger(other.transform.position);
        }

        private bool IsValidTarget(Collider other)
        {
            if (string.IsNullOrEmpty(TagToCompare))
                return true;

            return other.CompareTag(TagToCompare);
        }

        private void Trigger(Vector3 otherPosition)
        {
            if (routine != null)
                StopCoroutine(routine);

            routine = StartCoroutine(Execute(otherPosition));
        }

        private IEnumerator Execute(Vector3 otherPosition)
        {
            if (playDelay > 0f)
                yield return new WaitForSeconds(playDelay);

            AudioManager manager = AudioManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[AudioTrigger] AudioManager not found.");
                yield break;
            }

            switch (audioType)
            {
                case TriggerAudioType.SFX:
                    HandleSfx(manager, otherPosition);
                    break;

                case TriggerAudioType.Music:
                    HandleMusic(manager);
                    break;

                case TriggerAudioType.Ambient:
                    HandleAmbient(manager);
                    break;
            }
        }

        private void HandleSfx(AudioManager manager, Vector3 otherPosition)
        {
            if(sfxClip == null || string.IsNullOrEmpty(sfxClip.soundName))
                return;

            float v = Mathf.Clamp01(sfxVolume);

            switch (action)
            {
                case TriggerAudioAction.Play2D:
                    manager.PlaySfx2D(sfxClip.soundName, v);
                    break;

                case TriggerAudioAction.Play3D:
                    Vector3 pos = useOverride3DPosition
                        ? transform.TransformPoint(override3DPosition)
                        : transform.position;

                    manager.PlaySfx3D(sfxClip.soundName, pos, v);
                    break;
            }
        }

        private void HandleMusic(AudioManager manager)
        {
            switch (action)
            {
                case TriggerAudioAction.Play:
                    if (musicTrack != null)
                        manager.PlayMusic(musicTrack.trackName, 0f, 0f);
                    break;

                case TriggerAudioAction.PlayFadeIn:
                    if (musicTrack != null)
                        manager.PlayMusic(musicTrack.trackName, 0f, fadeDuration);
                    break;

                case TriggerAudioAction.StopMusic:
                    manager.StopMusic(fadeDuration);
                    break;

                case TriggerAudioAction.Snapshot:
                    manager.TransitionToSnapshot(snapshotName, fadeDuration);
                    break;
            }
        }

        private void HandleAmbient(AudioManager manager)
        {
            if (ambientTrack == null)
                return;

            switch (action)
            {
                case TriggerAudioAction.Play:
                case TriggerAudioAction.PlayFadeIn:
                {
                    AmbientProfile profileToPush = GetOrCreateProfileForAmbientTrack();
                    if (profileToPush == null)
                        return;

                    ambientToken = manager.PushAmbientProfile(profileToPush, 0, fadeDuration);
                    break;
                }

                case TriggerAudioAction.Stop:
                case TriggerAudioAction.PopAmbient:
                {
                    if (ambientToken != -1)
                    {
                        manager.PopAmbientToken(ambientToken, fadeDuration);
                        ambientToken = -1;
                    }
                    break;
                }
            }
        }

        private AmbientProfile GetOrCreateProfileForAmbientTrack()
        {
            if (ambientProfileAsset != null)
                return ambientProfileAsset;

            if (cachedRuntimeProfile == null)
            {
                cachedRuntimeProfile = ScriptableObject.CreateInstance<AmbientProfile>();
                cachedRuntimeProfile.profileName = "AudioTrigger_Profile";
            }

            cachedRuntimeProfile.layers = new AmbientLayer[]
            {
                new() {
                    track = ambientTrack,
                    volume = 1f
                }
            };

            return cachedRuntimeProfile;
        }

        private void OnDisable()
        {
            if (ambientToken == -1)
                return;

            AudioManager manager = AudioManager.Instance;
            if (manager != null)
                manager.PopAmbientToken(ambientToken, fadeDuration);

            ambientToken = -1;
        }
    }
}