using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Snog.Shared;

namespace Snog.Audio
{
    public class AudioSourcePool : MonoBehaviour
    {
        public int poolSize = 10;
        public int maxExtraSources = 10;

        public AudioMixerGroup fxGroup;

        [Header("Defaults")]
        public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
        public float minDistance = 1f;
        public float maxDistance = 30f;
        public float dopplerLevel = 0f;

        private readonly Queue<AudioSource> available = new();
        private readonly HashSet<AudioSource> inUse = new();
        private bool initialized;

        // --- Fix 7: rate-limit the pool-exhaustion warning so it doesn't spam every frame.
        private float _nextExhaustionWarnTime = 0f;
        private const float EXHAUSTION_WARN_INTERVAL = 2f;

        protected void Awake()
        {
            if (!initialized)
                Initialize(poolSize, fxGroup);
        }

        public void Initialize(int size, AudioMixerGroup group)
        {
            poolSize = Mathf.Max(1, size);
            fxGroup = group;

            if (fxGroup == null)
                Debug.LogWarning("[AudioSourcePool] FX mixer group not assigned. Sounds will be unrouted.");

            RebuildPool();
            initialized = true;
        }

        private void RebuildPool()
        {
            while (available.Count > 0)
            {
                var src = available.Dequeue();
                if (src != null) Destroy(src.gameObject);
            }

            inUse.Clear();

            for (int i = 0; i < poolSize; i++)
                available.Enqueue(CreateSource());
        }

        private void ApplyDefaults(AudioSource src)
        {
            src.playOnAwake = false;
            src.loop = false;
            src.outputAudioMixerGroup = fxGroup;
            src.spatialBlend = 1f;
            src.rolloffMode = rolloffMode;
            src.minDistance = minDistance;
            src.maxDistance = maxDistance;
            src.dopplerLevel = dopplerLevel;
            src.pitch = 1f;
            src.panStereo = 0f;
        }

        private AudioSource CreateSource()
        {
            var go = new GameObject("PooledAudioSource");
            go.transform.parent = transform;
            var src = go.AddComponent<AudioSource>();
            ApplyDefaults(src);
            return src;
        }

        public void PlayClip(AudioClip clip, Vector3 pos, float volume)
        {
            if (clip == null) return;

            int totalCapacity = poolSize + Mathf.Max(0, maxExtraSources);

            if (available.Count == 0)
            {
                if ((available.Count + inUse.Count) >= totalCapacity)
                {
                    // Fix 7: warn the developer so they can tune pool sizes — but throttled.
                    float now = Time.unscaledTime;
                    if (now >= _nextExhaustionWarnTime)
                    {
                        Debug.LogWarning(
                            $"[AudioSourcePool] Pool exhausted ({inUse.Count}/{totalCapacity} active). " +
                            $"Sound '{clip.name}' was dropped. Increase fxPoolSize or maxExtraSources on AudioManager.",
                            this);
                        _nextExhaustionWarnTime = now + EXHAUSTION_WARN_INTERVAL;
                    }
                    return;
                }

                available.Enqueue(CreateSource());
            }

            var src = available.Dequeue();
            inUse.Add(src);

            src.transform.position = pos;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.loop = false;
            src.Play();

            StartCoroutine(ReturnWhenFinished(src));
        }

        private IEnumerator ReturnWhenFinished(AudioSource src)
        {
            while (src != null && src.isPlaying)
                yield return null;

            if (src != null)
            {
                src.Stop();
                src.clip = null;
                ApplyDefaults(src);
            }

            if (inUse.Contains(src))
                inUse.Remove(src);

            if (src != null)
                available.Enqueue(src);
            else
                Debug.LogWarning("[AudioSourcePool] AudioSource was destroyed externally. This may indicate a scene unload or manual destruction.");
        }

        public int GetActiveSourceCount() => inUse.Count;

        public void GetPoolStats(out int active, out int poolAvailable, out int total)
        {
            active = inUse.Count;
            poolAvailable = this.available.Count;
            total = active + poolAvailable;
        }
    }
}
