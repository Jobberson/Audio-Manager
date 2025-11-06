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

    void Awake()
    {
        Singleton();
        
        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = new("PooledAudioSource");
            go.transform.parent = transform;
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = fxGroup;
            Pool.Enqueue(source);
        }
    }

    public void PlayClip(AudioClip clip, Vector3 position, float volume)
    {
        AudioSource source = Pool.Dequeue();
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