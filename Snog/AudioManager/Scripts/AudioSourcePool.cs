using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioSourcePool : Singleton<AudioSourcePool>
{
    public int poolSize = 10;
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

    private AudioSource CreateSource()
    {
        var go = new GameObject("PooledAudioSource");
        go.transform.parent = transform;

        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.outputAudioMixerGroup = fxGroup;
        src.spatialBlend = 1f;

        src.rolloffMode = rolloffMode;
        src.minDistance = minDistance;
        src.maxDistance = maxDistance;
        src.dopplerLevel = dopplerLevel;

        return src;
    }

    public void PlayClip(AudioClip clip, Vector3 pos, float volume)
    {
        if (clip == null) return;

        if (available.Count == 0)
        {
            available.Enqueue(CreateSource());
        }

        var src = available.Dequeue();
        inUse.Add(src);

        src.transform.position = pos;
        src.clip = clip;
        src.volume = volume;
        src.loop = false;
        src.Play();

        StartCoroutine(ReturnWhenFinished(src));
    }

    private IEnumerator ReturnWhenFinished(AudioSource src)
    {
        while (src != null && src.isPlaying)
        {
            yield return null;
        }

        if (src == null) yield break;

        src.Stop();
        src.clip = null;

        inUse.Remove(src);
        available.Enqueue(src);
    }

    public int GetActiveSourceCount()
    {
        return inUse.Count;
    }
}
