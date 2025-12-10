using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioSourcePool : Singleton<AudioSourcePool>
{
    public static AudioSourcePool Instance { get; private set; }
    public int poolSize = 10;
    public AudioMixerGroup fxGroup;

    private readonly Queue<AudioSource> available = new();
    private readonly HashSet<AudioSource> inUse = new();

    void Awake()
    {
        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject("PooledAudioSource");
            go.transform.parent = transform;
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.outputAudioMixerGroup = fxGroup;
            src.spatialBlend = 1f;
            available.Enqueue(src);
        }
    }

    public void PlayClip(AudioClip clip, Vector3 pos, float volume)
    {
        if (clip == null) return;
        if (available.Count == 0)
        {
            // Option A: expand
            var go = new GameObject("PooledAudioSource");
            go.transform.parent = transform;
            var extra = go.AddComponent<AudioSource>();
            extra.playOnAwake = false;
            extra.outputAudioMixerGroup = fxGroup;
            extra.spatialBlend = 1f;
            available.Enqueue(extra);
        }

        var src = available.Dequeue();
        inUse.Add(src);

        src.transform.position = pos;
        src.clip = clip;
        src.volume = volume;
        src.Play();

        StartCoroutine(ReturnWhenFinished(src));
    }

    private IEnumerator ReturnWhenFinished(AudioSource src)
    {
        while (src != null && src.isPlaying)
            yield return null;

        if (src == null) yield break;
        inUse.Remove(src);
        available.Enqueue(src);
    }

    public int GetActiveSourceCount() => inUse.Count;
}
