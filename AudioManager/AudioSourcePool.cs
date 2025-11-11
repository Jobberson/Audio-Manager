using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioSourcePool : MonoBehaviour
{
    public static AudioSourcePool Instance { get; private set; }

    public int poolSize = 10;
    public AudioMixerGroup fxGroup;

    private Queue<AudioSource> Pool = new();
    private int recycledCount = 0;
    private int totalPlayed = 0;

    void Awake()
    {
        Singleton();
        InitializePool(poolSize);
    }

    private void InitializePool(int size)
    {
        for (int i = 0; i < size; i++)
        {
            GameObject go = new("PooledAudioSource");
            go.transform.parent = transform;
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = fxGroup;
            Pool.Enqueue(source);
        }
    }

    public void ResizePool(int newSize)
    {
        if (newSize <= 0) return;

        int currentSize = Pool.Count;
        if (newSize > currentSize)
        {
            InitializePool(newSize - currentSize);
        }
        else if (newSize < currentSize)
        {
            int toRemove = currentSize - newSize;
            for (int i = 0; i < toRemove; i++)
            {
                if (Pool.Count > 0)
                {
                    AudioSource source = Pool.Dequeue();
                    Destroy(source.gameObject);
                }
            }
        }

        poolSize = newSize;
    }

    public void RecycleInactiveSources()
    {
        foreach (var source in Pool)
        {
            if (!source.isPlaying)
            {
                source.Stop();
                source.clip = null;
            }
        }
    }

    public void PlayClip(AudioClip clip, Vector3 position, float volume)
    {
        if (Pool.Count == 0)
        {
            Debug.LogWarning("AudioSourcePool exhausted!");
            return;
        }

        AudioSource source = Pool.Dequeue();
        totalPlayed++;

        source.transform.position = position;
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 1f;
        source.Play();

        StartCoroutine(ReturnToPool(source, clip.length));
    }

    IEnumerator ReturnToPool(AudioSource source, float delay)
    {
        yield return new WaitForSeconds(delay);
        Pool.Enqueue(source);
        recycledCount++;
    }

    public int GetActiveSourceCount()
    {
        int count = 0;
        foreach (var source in Pool)
        {
            if (source.isPlaying) count++;
        }
        return count;
    }

    public int GetRecycledCount() => recycledCount;
    public int GetTotalPlayed() => totalPlayed;

    private void Singleton()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}