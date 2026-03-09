using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Snog.Shared;

namespace Snog.Audio
{
    public class AudioSourcePool : Singleton<AudioSourcePool>
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

        protected override void Awake()
        {
            base.Awake();

            if (!initialized)
            {
                Initialize(poolSize, fxGroup);
            }
        }

        public void Initialize(int size, AudioMixerGroup group)
        {
            poolSize = Mathf.Max(1, size);
            fxGroup = group;
            
            // ADDED: Validation warning
            if (fxGroup == null)
            {
                Debug.LogWarning("[AudioSourcePool] FX mixer group not assigned. Sounds will be unrouted.");
            }
            
            RebuildPool();
            initialized = true;
        }

        private void RebuildPool()
        {
            while (available.Count > 0)
            {
                var src = available.Dequeue();
                if (src != null)
                {
                    Destroy(src.gameObject);
                }
            }

            inUse.Clear();

            for (int i = 0; i < poolSize; i++)
            {
                available.Enqueue(CreateSource());
            }
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
            if (clip == null)
            {
                return;
            }

            int totalCapacity = poolSize + Mathf.Max(0, maxExtraSources);

            if (available.Count == 0)
            {
                if ((available.Count + inUse.Count) >= totalCapacity)
                {
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

        // FIXED: Memory leak when AudioSource is destroyed externally
        private IEnumerator ReturnWhenFinished(AudioSource src)
        {
            // Wait for playback to finish
            while (src != null && src.isPlaying)
            {
                yield return null;
            }

            // Clean up the source if it still exists
            if (src != null)
            {
                src.Stop();
                src.clip = null;
                ApplyDefaults(src);
            }

            // CRITICAL FIX: Always remove from inUse, even if source was destroyed
            if (inUse.Contains(src))
            {
                inUse.Remove(src);
            }

            // Only return to pool if source still exists
            if (src != null)
            {
                available.Enqueue(src);
            }
            else
            {
                // Source was destroyed externally - log for debugging
                Debug.LogWarning("[AudioSourcePool] AudioSource was destroyed externally. This may indicate a scene unload or manual destruction.");
            }
        }

        public int GetActiveSourceCount()
        {
            return inUse.Count;
        }
        
        // ADDED: Helper to get pool statistics
        public void GetPoolStats(out int active, out int available, out int total)
        {
            active = inUse.Count;
            available = this.available.Count;
            total = active + available;
        }
    }
}