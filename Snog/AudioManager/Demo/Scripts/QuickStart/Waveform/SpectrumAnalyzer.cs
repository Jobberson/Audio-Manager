using UnityEngine;
using System.Collections.Generic;
using Snog.Audio;

/// <summary>
/// Spectrum visualizer integrated with Snog's AudioManager.
///
/// Spectrum source priority:
///   1. If musicSourceOverride is assigned, samples that AudioSource directly
///      (most accurate – only reacts to the music channel).
///   2. Otherwise falls back to AudioListener.GetSpectrumData(), which captures
///      the full mixed output (works without any extra setup).
///
/// AudioManager integration:
///   • Visualization freezes and pillars smoothly collapse when no music is
///     playing, and resumes automatically when a track starts.
///   • Call PlayTrack(name) / StopTrack() from UI or other scripts to drive
///     both the AudioManager and the visualizer together.
/// </summary>
public class SpectrumAnalyzer : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────────────────────────────────

    public AnalyzerSettings settings;

    [Header("AudioManager Integration")]
    [Tooltip("Optional: assign the AudioManager's internal Music AudioSource for a " +
             "cleaner signal that only reacts to the music channel. " +
             "Leave empty to use the global AudioListener output instead.")]
    public AudioSource musicSourceOverride;

    [Tooltip("Seconds for pillars to collapse when music stops.")]
    public float collapseSpeed = 3f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Private state
    // ─────────────────────────────────────────────────────────────────────────

    private float[]          spectrum;
    private List<GameObject> pillars;
    private GameObject       folder;
    private bool             isBuilding;
    private bool             isMusicPlaying;

    // ─────────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        isBuilding = true;
        CreatePillarsByShapes();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) Rebuild();
        if (isBuilding) return;

        RefreshMusicState();

        if (isMusicPlaying)
            AnimatePillars();
        else
            CollapsePillars();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  AudioManager state
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether the AudioManager currently has a track playing.
    /// Falls back gracefully if AudioManager is not in the scene.
    /// </summary>
    private void RefreshMusicState()
    {
        if (AudioManager.Instance == null)
        {
            // No AudioManager – fall back to checking the override source directly
            isMusicPlaying = musicSourceOverride != null && musicSourceOverride.isPlaying;
            return;
        }

        // Use override source if available; otherwise rely on the AudioManager's
        // own knowledge of what's playing via TryGetMusicNames as a proxy.
        if (musicSourceOverride != null)
        {
            isMusicPlaying = musicSourceOverride.isPlaying;
        }
        else
        {
            // AudioListener will still receive signal even when looping silence,
            // so we ask the AudioManager directly whether a track is active.
            isMusicPlaying = IsMusicSourceActive();
        }
    }

    /// <summary>
    /// Heuristic: samples the listener output; if RMS > epsilon, music is playing.
    /// Override this if you expose an isMusicPlaying flag from AudioManager.
    /// </summary>
    private bool IsMusicSourceActive()
    {
        // Sample a small block and check RMS energy
        float[] probe = AudioListener.GetOutputData(256, 0);
        float sum = 0f;
        foreach (float s in probe) sum += s * s;
        return (sum / probe.Length) > 1e-6f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Public API – drive from AudioManagerDemoUI track buttons
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays a named music track through AudioManager and activates the visualizer.
    /// Safe to call directly or from UI buttons.
    /// </summary>
    public void PlayTrack(string trackName, float fadeIn = 1.5f)
    {
        if (AudioManager.Instance == null)
        {
            Debug.LogWarning("[SpectrumAnalyzer] AudioManager not found in scene.");
            return;
        }

        AudioManager.Instance.PlayMusic(trackName, startDelay: 0f, fadeDuration: fadeIn);
    }

    /// <summary>
    /// Stops the current music track and lets pillars collapse.
    /// </summary>
    public void StopTrack(float fadeOut = 1.5f)
    {
        if (AudioManager.Instance == null) return;
        AudioManager.Instance.StopMusic(fadeOut);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Pillar animation
    // ─────────────────────────────────────────────────────────────────────────

    private void AnimatePillars()
    {
        // Sample spectrum – prefer direct AudioSource, fall back to AudioListener
        if (musicSourceOverride != null)
            musicSourceOverride.GetSpectrumData(GetSpectrumBuffer(), 0, settings.spectrum.FffWindowType);
        else
            AudioListener.GetSpectrumData(GetSpectrumBuffer(), 0, settings.spectrum.FffWindowType);

        for (int i = 0; i < pillars.Count; i++)
        {
            float level = spectrum[i] * settings.pillar.sensitivity * Time.deltaTime * 1000f;

            Vector3 scale = pillars[i].transform.localScale;
            scale.y = Mathf.Lerp(scale.y, level, settings.pillar.speed * Time.deltaTime);
            pillars[i].transform.localScale = scale;

            Vector3 pos = pillars[i].transform.position;
            pos.y = scale.y * 0.5f;
            pillars[i].transform.position = pos;
        }
    }

    /// <summary>
    /// Smoothly collapses all pillars to zero height when music is not playing.
    /// </summary>
    private void CollapsePillars()
    {
        if (pillars == null) return;

        float step = collapseSpeed * Time.deltaTime;

        foreach (GameObject p in pillars)
        {
            if (p == null) continue;

            Vector3 scale = p.transform.localScale;
            scale.y = Mathf.MoveTowards(scale.y, 0f, step);
            p.transform.localScale = scale;

            Vector3 pos = p.transform.position;
            pos.y = scale.y * 0.5f;
            p.transform.position = pos;
        }
    }

    /// <summary>
    /// Returns the shared spectrum buffer, (re)allocating it to match the
    /// current sample rate if necessary.
    /// </summary>
    private float[] GetSpectrumBuffer()
    {
        int size = (int)settings.spectrum.sampleRate;
        if (spectrum == null || spectrum.Length != size)
            spectrum = new float[size];
        return spectrum;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Build / rebuild
    // ─────────────────────────────────────────────────────────────────────────

    private void CreatePillarsByShapes()
    {
        GameObject prefab = settings.pillar.type == PillarTypes.Cylinder
            ? settings.Prefabs.CylPrefab
            : settings.Prefabs.BoxPrefab;

        pillars = MathB.ShapesOfGameObjects(prefab, settings.pillar.radius,
            (int)settings.pillar.amount, settings.pillar.shape);

        folder = new GameObject("Pillars-" + pillars.Count);
        folder.transform.SetParent(transform);

        foreach (var pillar in pillars)
            pillar.transform.SetParent(folder.transform);

        isBuilding = false;
    }

    /// <summary>
    /// Destroys and re-creates all pillars with current settings.
    /// Called automatically on R keypress; also hookable from UI.
    /// </summary>
    public void Rebuild()
    {
        if (isBuilding) return;

        isBuilding = true;
        pillars.Clear();
        DestroyImmediate(folder);
        CreatePillarsByShapes();
    }

    private void Reset()
    {
        settings.pillar.Reset();
        settings.spectrum.Reset();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI Slider properties (unchanged API)
    // ─────────────────────────────────────────────────────────────────────────

    public float PillarShape
    {
        get => (int)settings.pillar.shape;
        set => settings.pillar.shape = (Shapes)(int)Mathf.Clamp(value, 0, 3);
    }

    public float PillarType
    {
        get => (int)settings.pillar.type;
        set => settings.pillar.type = (PillarTypes)(int)Mathf.Clamp(value, 0, 2);
    }

    public float Amount
    {
        get => settings.pillar.amount;
        set => settings.pillar.amount = Mathf.Clamp(value, 4, 128);
    }

    public float Radius
    {
        get => settings.pillar.radius;
        set => settings.pillar.radius = Mathf.Clamp(value, 2, 256);
    }

    public float Sensitivity
    {
        get => settings.pillar.sensitivity;
        set => settings.pillar.sensitivity = Mathf.Clamp(value, 1, 50);
    }

    public float PillarSpeed
    {
        get => settings.pillar.speed;
        set => settings.pillar.speed = Mathf.Clamp(value, 1, 30);
    }

    public float SampleMethod
    {
        get => (int)settings.spectrum.FffWindowType;
        set => settings.spectrum.FffWindowType = (FFTWindow)(int)Mathf.Clamp(value, 0, 6);
    }

    public float SampleRate
    {
        get => (int)settings.spectrum.sampleRate;
        set
        {
            int n = (int)Mathf.Pow(2, 7 + value); // 128, 256, 512, 1024, 2048
            settings.spectrum.sampleRate = (SampleRates)n;
        }
    }
}