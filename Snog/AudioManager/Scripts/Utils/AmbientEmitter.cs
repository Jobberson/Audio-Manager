
using UnityEngine;
using UnityEngine.Audio;
using Snog.Audio.Clips;

namespace Snog.Audio.Utils
{
    [AddComponentMenu("Snog/Audio/Ambient Emitter")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class AmbientEmitter : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("The track this emitter represents. Profiles reference this track.")]
        [SerializeField] private AmbientTrack track;

        [Tooltip("Optional extra priority. Higher = kept more often when budget is exceeded.")]
        [SerializeField] private int emitterPriority = 0;

        [Header("3D Settings")]
        [Range(0f, 1f)]
        [SerializeField] private float spatialBlend = 1f;

        [SerializeField] private float minDistance = 1f;
        [SerializeField] private float maxDistance = 30f;

        [Header("Playback")]
        [SerializeField] private bool loop = true;
        [SerializeField] private bool randomStartTime = true;
        [SerializeField] private Vector2 pitchRange = new(1f, 1f);

        private AudioSource source;
        private float currentVolume01;
        private float targetVolume01;

        public AmbientTrack Track => track;
        public int EmitterPriority => emitterPriority;

        public float CurrentVolume01 => currentVolume01;
        public bool IsPlaying => source != null && source.isPlaying;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            ApplyStaticSettings();
            currentVolume01 = 0f;
            targetVolume01 = 0f;
            source.volume = 0f;
        }

        private void OnEnable()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.RegisterEmitter(this);
            }
        }

        private void OnDisable()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.UnregisterEmitter(this);
            }
        }

        private void ApplyStaticSettings()
        {
            if (source == null) return;

            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = Mathf.Clamp01(spatialBlend);
            source.minDistance = Mathf.Max(0.01f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, maxDistance);

            if (pitchRange.y < pitchRange.x)
            {
                float tmp = pitchRange.x;
                pitchRange.x = pitchRange.y;
                pitchRange.y = tmp;
            }

            if (pitchRange.x <= 0f) pitchRange.x = 0.01f;
            if (pitchRange.y <= 0f) pitchRange.y = 0.01f;
        }

        public void EnsurePlaying(AudioMixerGroup outputGroup)
        {
            if (source == null) return;

            if (track == null || track.clip == null)
            {
                StopImmediate();
                return;
            }

            source.outputAudioMixerGroup = outputGroup;

            if (source.clip != track.clip)
            {
                source.clip = track.clip;
            }

            source.loop = loop;
            source.spatialBlend = Mathf.Clamp01(spatialBlend);

            if (!source.isPlaying)
            {
                if (randomStartTime && source.clip != null && source.clip.length > 0f)
                {
                    source.time = Random.Range(0f, source.clip.length);
                }

                if (pitchRange.x != pitchRange.y)
                {
                    source.pitch = Random.Range(pitchRange.x, pitchRange.y);
                }
                else
                {
                    source.pitch = pitchRange.x;
                }

                source.Play();
            }
        }

        public void SetTargetVolume01(float v01)
        {
            targetVolume01 = Mathf.Clamp01(v01);
        }

        public void StepVolume(float dt, float fadeSeconds, float globalGain)
        {
            float speed = fadeSeconds <= 0f ? 1f : Mathf.Clamp01(dt / fadeSeconds);

            currentVolume01 = Mathf.Lerp(currentVolume01, targetVolume01, speed);

            if (source != null)
            {
                source.volume = currentVolume01 * Mathf.Clamp01(globalGain);

                if (currentVolume01 <= 0.0001f && targetVolume01 <= 0.0001f)
                {
                    if (source.isPlaying)
                    {
                        source.Stop();
                    }
                }
            }
        }

        public void StopImmediate()
        {
            targetVolume01 = 0f;
            currentVolume01 = 0f;

            if (source != null)
            {
                source.volume = 0f;
                if (source.isPlaying)
                {
                    source.Stop();
                }
            }
        }
    }
}
