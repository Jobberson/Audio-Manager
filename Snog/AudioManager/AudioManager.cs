﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Snog.Audio
{
	/// <summary>
	/// Central audio manager: music, ambient, SFX (2D + pooled 3D).
	/// Refactored for clarity, safety and asset-store readiness.
	/// Keep this as a single instance (inherits from your existing Singleton&lt;T&gt;).
	/// </summary>
	[RequireComponent(typeof(SoundLibrary))]
	[RequireComponent(typeof(MusicLibrary))]
	[RequireComponent(typeof(AmbientLibrary))]
	[DisallowMultipleComponent]
	public class AudioManager : Singleton<AudioManager>
	{
		#region Nested Types & Constants

     	public enum FadeCurveType { Linear, EaseInOut, Exponential }

 	 	public enum AudioChannel { Master, Music, Ambient, SFX }

 	 	public enum SnapshotType { Default, Combat, Stealth, Underwater }

 	 	private static class MixerParams
 	 	{
			public const string Master = "MasterVolume";
			public const string Music = "MusicVolume";
			public const string Ambient = "AmbientVolume";
			public const string Sfx = "FXVolume";
 	 	}

 	 	private const float SILENCE_DB = -80f;

#if UNITY_EDITOR
 	 	private const float SFX_MAX_LENGTH = 30f;
 	 	private const float AMBIENT_MIN_LENGTH = 30f;
 	 	private const float MUSIC_MIN_LENGTH = 60f;
#endif

 	 	#endregion

 	 	#region Inspector Fields

 	 	[Header("Paths")]
 	 	[Tooltip("Project-relative folder inside Assets used by Scan/Generate (e.g. Assets/Audio/).")]
 	 	[SerializeField] private string audioFolderPath = "Assets/Audio";

 	 	[Header("Volumes (0..1)")]
 	 	[Range(0f, 1f), SerializeField] private float masterVolume = 1f;
 	 	[Range(0f, 1f), SerializeField] private float musicVolume = 1f;
 	 	[Range(0f, 1f), SerializeField] private float ambientVolume = 1f;
 	 	[Range(0f, 1f), SerializeField] private float sfxVolume = 1f;

 	 	[Header("Looping")]
 	 	[SerializeField] private bool musicIsLooping = true;
 	 	[SerializeField] private bool ambientIsLooping = true;

 	 	[Header("Mixers & Groups")]
 	 	[SerializeField] private AudioMixer mainMixer;
 	 	[SerializeField] private AudioMixerGroup musicGroup;
 	 	[SerializeField] private AudioMixerGroup ambientGroup;
 	 	[SerializeField] private AudioMixerGroup sfxGroup;

 	 	[Header("Snapshots")]
 	 	[SerializeField] private AudioMixerSnapshot defaultSnapshot;
 	 	[SerializeField] private AudioMixerSnapshot combatSnapshot;
 	 	[SerializeField] private AudioMixerSnapshot stealthSnapshot;
 	 	[SerializeField] private AudioMixerSnapshot underwaterSnapshot;

 	 	[Header("SFX Pool")]
 	 	[Tooltip("Pool used for spatialized SFX playback. If null, manager will create one at runtime.")]
 	 	[SerializeField] private AudioSourcePool sfxPool;
 	 	[SerializeField, Min(1)] private int poolSize = 10;

     	#endregion

 	 	#region Runtime State (private)

 	 	// Libraries (required by RequireComponent)
 	 	private SoundLibrary soundLibrary;
 	 	private MusicLibrary musicLibrary;
 	 	private AmbientLibrary ambientLibrary;

 	 	// Playback sources (owned children)
 	 	private AudioSource musicSource;
 	 	private AudioSource ambientSource;
 	 	private AudioSource sfx2DSource;

 	 	// Temporary scan containers (editor only)
 	 	public List<AudioClip> scannedMusicClips = new List<AudioClip>();
 	 	public List<AudioClip> scannedAmbientClips = new List<AudioClip>();
 	 	public List<AudioClip> scannedSFXClips = new List<AudioClip>();

 	 	#endregion

 	 	#region Unity Lifecycle

 	 	protected override void Awake()
 	 	{
			base.Awake();

			// Ensure library refs
			soundLibrary = GetComponent<SoundLibrary>();
			musicLibrary = GetComponent<MusicLibrary>();
			ambientLibrary = GetComponent<AmbientLibrary>();

			if (soundLibrary == null) Debug.LogWarning("Missing SoundLibrary on AudioManager GameObject.", this);
			if (musicLibrary == null) Debug.LogWarning("Missing MusicLibrary on AudioManager GameObject.", this);
			if (ambientLibrary == null) Debug.LogWarning("Missing AmbientLibrary on AudioManager GameObject.", this);

			AutoAssignMixerAndGroups();
			CreateAudioSourcesIfMissing();
			AssignMixerGroups();

			// Apply last-saved volumes (or inspector defaults)
			ApplyMixerVolumes();

			EnsureSfxPoolInitialized();
        }

        private void Start()
        {
			LoadVolumeSettings(); // read persisted volumes and apply them
        }

        #endregion

        #region Initialization Helpers

        private void CreateChild(string name, out GameObject go, out AudioSource source)
        {
			go = new GameObject(name);
			go.transform.SetParent(transform, false);
			source = go.AddComponent<AudioSource>();
			source.playOnAwake = false;
        }

        private void CreateAudioSourcesIfMissing()
        {
			if (musicSource == null)
			{
			    CreateChild($"{name} - Music", out GameObject mgo, out musicSource);
			    musicSource.loop = musicIsLooping;
			    musicSource.spatialBlend = 0f; // 2D music
			}

			if (ambientSource == null)
			{
			    CreateChild($"{name} - Ambient", out GameObject ago, out ambientSource);
			    ambientSource.loop = ambientIsLooping;
			    ambientSource.spatialBlend = 0f; // ambient non-spatial by default
			}

			if (sfx2DSource == null)
			{
			    CreateChild($"{name} - SFX2D", out GameObject sgo, out sfx2DSource);
			    sfx2DSource.spatialBlend = 0f;
			}
        }

        private void AssignMixerGroups()
        {
			if (musicSource != null && musicGroup != null) musicSource.outputAudioMixerGroup = musicGroup;
			if (ambientSource != null && ambientGroup != null) ambientSource.outputAudioMixerGroup = ambientGroup;
			if (sfx2DSource != null && sfxGroup != null) sfx2DSource.outputAudioMixerGroup = sfxGroup;
        }

        private void EnsureSfxPoolInitialized()
        {
			if (sfxPool != null) return;

			// Create a simple pool GameObject as child
			GameObject poolGO = new GameObject($"{name} - SFX Pool");
			poolGO.transform.SetParent(transform, false);
			sfxPool = poolGO.AddComponent<AudioSourcePool>();

			// Try to call an Initialize method if the pool exposes it, otherwise set fields directly
			MethodInfo init = sfxPool.GetType().GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (init != null)
			{
			    try { init.Invoke(sfxPool, new object[] { poolSize, sfxGroup }); }
			    catch
			    {
			        // fallback assignment
			        TryAssignPoolFieldsFallback();
			    }
			}
			else
			{
			    TryAssignPoolFieldsFallback();
			}
        }

        private void TryAssignPoolFieldsFallback()
        {
			// best effort: set common field/property names used by simple pools
			var t = sfxPool.GetType();
			var f = t.GetField("poolSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			f?.SetValue(sfxPool, poolSize);

			var fg = t.GetField("fxGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			fg?.SetValue(sfxPool, sfxGroup);

			var propPool = t.GetProperty("PoolSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (propPool?.CanWrite == true) propPool.SetValue(sfxPool, poolSize);

			var propGroup = t.GetProperty("FxGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (propGroup?.CanWrite == true) propGroup.SetValue(sfxPool, sfxGroup);
        }

        private void AutoAssignMixerAndGroups()
        {
			if (mainMixer != null)
			{
			    TryAssignMissingGroupsAndSnapshots();
			    return;
			}

#if UNITY_EDITOR
			// Editor-only automatic assignment: pick the first AudioMixer asset, then try to auto-assign groups/snapshots
			try
			{
			    string[] guids = AssetDatabase.FindAssets("t:AudioMixer");
			    if (guids != null && guids.Length > 0)
			    {
			        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
			        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
			        if (mixer != null) mainMixer = mixer;
			    }
			}
			catch (Exception ex)
			{
			    Debug.LogWarning($"Auto-assign mixer failed: {ex.Message}", this);
			}
#endif

			if (mainMixer != null) TryAssignMissingGroupsAndSnapshots();
        }

        private void TryAssignMissingGroupsAndSnapshots()
        {
			if (mainMixer == null) return;

			TryAssignGroup(ref musicGroup, new[] { "Music", "Master/Music", "MusicGroup" });
			TryAssignGroup(ref ambientGroup, new[] { "Ambient", "Master/Ambient", "Ambience" });
			TryAssignGroup(ref sfxGroup, new[] { "FX", "SFX", "Master/FX" });

			defaultSnapshot ??= mainMixer.FindSnapshot("Default");
			combatSnapshot ??= mainMixer.FindSnapshot("Combat");
			stealthSnapshot ??= mainMixer.FindSnapshot("Stealth");
			underwaterSnapshot ??= mainMixer.FindSnapshot("Underwater");
        }

        private void TryAssignGroup(ref AudioMixerGroup groupRef, string[] candidates)
        {
			if (groupRef != null || mainMixer == null) return;

			foreach (var name in candidates)
			{
			    try
			    {
			        var found = mainMixer.FindMatchingGroups(name);
			        if (found != null && found.Length > 0)
			        {
		groupRef = found[0];
		return;
			        }
			    }
			    catch { /* ignore weird names */ }
			}

			// fallback to Master if present
			try
			{
			    var m = mainMixer.FindMatchingGroups("Master");
			    if (m != null && m.Length > 0) groupRef = m[0];
			}
			catch { }
        }

        #endregion

        #region Volume & Mixer

        private void ApplyMixerVolumes()
        {
			SetVolume(masterVolume, AudioChannel.Master, applyToField: false);
			SetVolume(sfxVolume, AudioChannel.SFX, applyToField: false);
			SetVolume(musicVolume, AudioChannel.Music, applyToField: false);
			SetVolume(ambientVolume, AudioChannel.Ambient, applyToField: false);
        }

        /// <summary>
        /// Set a volume (0..1) on a channel and immediately apply to the mixer in decibels.
        /// </summary>
        public void SetVolume(float normalizedVolume, AudioChannel channel, bool applyToField = true)
        {
			normalizedVolume = Mathf.Clamp01(normalizedVolume);

			if (applyToField)
			{
			    switch (channel)
			    {
			        case AudioChannel.Master: masterVolume = normalizedVolume; break;
			        case AudioChannel.Music: musicVolume = normalizedVolume; break;
			        case AudioChannel.Ambient: ambientVolume = normalizedVolume; break;
			        case AudioChannel.SFX: sfxVolume = normalizedVolume; break;
			    }
			}

			if (mainMixer == null)
			{
			    Debug.LogWarning("Main mixer not assigned; cannot set volume parameter.", this);
			    return;
			}

			float dB = (normalizedVolume <= 0f) ? SILENCE_DB : Mathf.Log10(Mathf.Clamp(normalizedVolume, 0.0001f, 1f)) * 20f;
			switch (channel)
			{
			    case AudioChannel.Master: mainMixer.SetFloat(MixerParams.Master, dB); break;
			    case AudioChannel.Music: mainMixer.SetFloat(MixerParams.Music, dB); break;
			    case AudioChannel.Ambient: mainMixer.SetFloat(MixerParams.Ambient, dB); break;
			    case AudioChannel.SFX: mainMixer.SetFloat(MixerParams.Sfx, dB); break;
			}
        }

        public float GetMixerParameterDB(string parameter)
        {
			if (mainMixer == null) return SILENCE_DB;
			return mainMixer.GetFloat(parameter, out float val) ? val : SILENCE_DB;
        }

        public void SetMixerParameter(string parameterName, float value)
        {
			if (mainMixer == null)
			{
			    Debug.LogWarning($"Mixer not assigned; cannot set '{parameterName}'.", this);
			    return;
			}
			if (!mainMixer.SetFloat(parameterName, value))
			{
			    Debug.LogWarning($"Mixer parameter '{parameterName}' not found.", this);
			}
        }

        public float GetMixerParameter(string parameterName)
        {
			if (mainMixer == null)
			{
			    Debug.LogWarning($"Mixer not assigned; cannot read '{parameterName}'.", this);
			    return -1f;
			}
			if (mainMixer.GetFloat(parameterName, out float value)) return value;
			Debug.LogWarning($"Mixer parameter '{parameterName}' not found.", this);
			return -1f;
        }

        #endregion

        #region Music & Ambient APIs

        public void PlayMusic(string name, float delay = 0f)
        {
			if (musicLibrary == null || musicSource == null) return;
			var clip = musicLibrary.GetClipFromName(name);
			if (clip == null) { Debug.LogWarning($"Music '{name}' not found.", this); return; }
			musicSource.clip = clip;
			musicSource.loop = musicIsLooping;
			musicSource.outputAudioMixerGroup = musicGroup;
			if (delay <= 0f) musicSource.Play();
			else musicSource.PlayDelayed(delay);
        }

        public IEnumerator PlayMusicFade(string name, float duration)
        {
			if (musicSource == null || musicLibrary == null) yield break;
			var clip = musicLibrary.GetClipFromName(name);
			if (clip == null) { Debug.LogWarning($"Music '{name}' not found.", this); yield break; }

			float target = musicVolume * masterVolume;
			musicSource.clip = clip;
			musicSource.volume = 0f;
			musicSource.loop = musicIsLooping;
			musicSource.Play();

			yield return StartCoroutine(FadeIn(musicSource, target, duration));
        }

        public void StopMusic() => musicSource?.Stop();

        public IEnumerator StopMusicFade(float duration)
        {
			if (musicSource == null) yield break;
			float prev = musicSource.volume;
			yield return StartCoroutine(FadeOut(musicSource, duration));
			musicSource.volume = prev;
        }

        public void PlayAmbient(string name, float delay = 0f)
        {
			if (ambientLibrary == null || ambientSource == null) return;
			var clip = ambientLibrary.GetClipFromName(name);
			if (clip == null) { Debug.LogWarning($"Ambient '{name}' not found.", this); return; }
			ambientSource.clip = clip;
			ambientSource.loop = ambientIsLooping;
			ambientSource.outputAudioMixerGroup = ambientGroup;
			if (delay <= 0f) ambientSource.Play();
			else ambientSource.PlayDelayed(delay);
        }

        public IEnumerator PlayAmbientFade(string name, float duration)
        {
			if (ambientSource == null || ambientLibrary == null) yield break;
			var clip = ambientLibrary.GetClipFromName(name);
			if (clip == null) { Debug.LogWarning($"Ambient '{name}' not found.", this); yield break; }

			float target = ambientVolume * masterVolume;
			ambientSource.clip = clip;
			ambientSource.volume = 0f;
			ambientSource.loop = ambientIsLooping;
			ambientSource.Play();

			yield return StartCoroutine(FadeIn(ambientSource, target, duration));
        }

        public void StopAmbient() => ambientSource?.Stop();

        public IEnumerator StopAmbientFade(float duration)
        {
			if (ambientSource == null) yield break;
			float prev = ambientSource.volume;
			yield return StartCoroutine(FadeOut(ambientSource, duration));
			ambientSource.volume = prev;
        }

        #endregion

        #region SFX (2D & 3D)

        public void PlaySound2D(string soundName, float volumeMultiplier = 1f)
        {
			if (soundLibrary == null) return;
			var clip = soundLibrary.GetClipFromName(soundName);
			if (clip == null) { Debug.LogWarning($"SFX '{soundName}' not found.", this); return; }
			if (sfx2DSource == null) return;
			sfx2DSource.outputAudioMixerGroup = sfxGroup;
			sfx2DSource.PlayOneShot(clip, sfxVolume * masterVolume * volumeMultiplier);
        }

        public void PlaySound3D(string soundName, Vector3 worldPos, float volumeMultiplier = 1f)
        {
			if (soundLibrary == null) return;
			var clip = soundLibrary.GetClipFromName(soundName);
			if (clip == null) { Debug.LogWarning($"SFX '{soundName}' not found.", this); return; }
			if (sfxPool == null) { Debug.LogWarning("SFX pool missing.", this); return; }

			sfxPool.PlayClip(clip, worldPos, sfxVolume * masterVolume * volumeMultiplier);
        }

        #endregion

        #region Crossfade & Snapshot

        public IEnumerator CrossfadeAudio(AudioSource from, AudioSource to, AudioClip newClip, float duration, FadeCurveType curve = FadeCurveType.Linear, bool waitForBar = false, float bpm = 120f)
        {
			if (from == null || to == null || newClip == null) yield break;

			if (waitForBar)
			{
			    float secondsPerBeat = 60f / bpm;
			    float timeToNextBar = secondsPerBeat * Mathf.Ceil(Time.time / secondsPerBeat) - Time.time;
			    if (timeToNextBar > 0f) yield return new WaitForSeconds(timeToNextBar);
			}

			to.clip = newClip;
			to.volume = 0f;
			to.Play();

			float elapsed = 0f;
			float startVol = from.volume;
			while (elapsed < duration)
			{
			    elapsed += Time.deltaTime;
			    float t = Mathf.Clamp01(elapsed / duration);
			    float ct = ApplyCurve(t, curve);
			    from.volume = Mathf.Lerp(startVol, 0f, ct);
			    to.volume = Mathf.Lerp(0f, startVol, ct);
			    yield return null;
			}

			from.Stop();
			from.volume = startVol;
        }

        public IEnumerator BlendSnapshots(SnapshotType from, SnapshotType to, float duration, FadeCurveType curve = FadeCurveType.Linear)
        {
			if (mainMixer == null) { Debug.LogWarning("Main mixer missing for snapshot blend.", this); yield break; }
			var fromSnap = GetSnapshot(from);
			var toSnap = GetSnapshot(to);
			if (fromSnap == null || toSnap == null) { Debug.LogWarning("Snapshot(s) not found.", this); yield break; }

			float time = 0f;
			while (time < duration)
			{
			    time += Time.deltaTime;
			    float t = Mathf.Clamp01(time / duration);
			    float ct = ApplyCurve(t, curve);
			    mainMixer.TransitionToSnapshots(new[] { fromSnap, toSnap }, new[] { 1f - ct, ct }, 0f);
			    yield return null;
			}

			toSnap.TransitionTo(0.01f);
        }

        #endregion

        #region Async Loading

        public void LoadAndPlaySFXAsyncFromResources(string path, float volume = 1f, bool loop = false, float delay = 0f)
        {
			StartCoroutine(LoadAndPlayClipAsync(path, AudioType.SFX, volume, loop, delay));
        }

        public void LoadAndPlayMusicAsyncFromResources(string path, float volume = 1f, bool loop = true, float fadeDuration = 2f, float delay = 0f)
        {
			StartCoroutine(LoadAndPlayClipAsync(path, AudioType.Music, volume, loop, delay, fadeDuration));
        }

        public void LoadAndPlayAmbientAsyncFromResources(string path, float volume = 1f, bool loop = true, float fadeDuration = 2f, float delay = 0f)
        {
			StartCoroutine(LoadAndPlayClipAsync(path, AudioType.Ambient, volume, loop, delay, fadeDuration));
        }

        private IEnumerator LoadAndPlayClipAsync(string path, AudioType type, float volume, bool loop, float delay, float fadeDuration = 0f)
        {
			var request = Resources.LoadAsync<AudioClip>(path);
			yield return request;

			var clip = request.asset as AudioClip;
			if (clip == null) { Debug.LogWarning($"Audio clip not found at path: {path}", this); yield break; }

			if (delay > 0f) yield return new WaitForSeconds(delay);

			switch (type)
			{
			    case AudioType.SFX:
			        if (sfxPool == null) { Debug.LogWarning("SFX pool missing for Resources SFX playback.", this); yield break; }
			        sfxPool.PlayClip(clip, Vector3.zero, volume * sfxVolume * masterVolume);
			        break;

			    case AudioType.Music:
			        if (musicSource == null) yield break;
			        musicSource.clip = clip;
			        musicSource.loop = loop;
			        musicSource.outputAudioMixerGroup = musicGroup;
			        if (fadeDuration > 0f)
			        {
						musicSource.volume = 0f;
						musicSource.Play();
						yield return StartCoroutine(FadeIn(musicSource, volume * musicVolume * masterVolume, fadeDuration));
			        }
			        else
			        {
						musicSource.volume = volume * musicVolume * masterVolume;
						musicSource.Play();
			        }
			        break;

			    case AudioType.Ambient:
			        if (ambientSource == null) yield break;
				       ambientSource.clip = clip;
				       ambientSource.loop = loop;
				       ambientSource.outputAudioMixerGroup = ambientGroup;
			        if (fadeDuration > 0f)
			        {
						ambientSource.volume = 0f;
						ambientSource.Play();
						yield return StartCoroutine(FadeIn(ambientSource, volume * ambientVolume * masterVolume, fadeDuration));
			        }
			        else
			        {
						ambientSource.volume = volume * ambientVolume * masterVolume;
						ambientSource.Play();
			        }
			        break;
			}
        }

        #endregion

        #region Utility & Helpers

        public bool MusicIsPlaying() => musicSource != null && musicSource.isPlaying;
        public string GetCurrentMusicName() => musicSource?.clip?.name ?? "None";
        public bool AmbientIsPlaying() => ambientSource != null && ambientSource.isPlaying;
        public string GetCurrentAmbientName() => ambientSource?.clip?.name ?? "None";

        private AudioMixerSnapshot GetSnapshot(SnapshotType t)
        {
			return t switch
			{
			    SnapshotType.Default => defaultSnapshot,
			    SnapshotType.Combat => combatSnapshot,
			    SnapshotType.Stealth => stealthSnapshot,
			    SnapshotType.Underwater => underwaterSnapshot,
			    _ => null,
			};
        }

        private static float ApplyCurve(float t, FadeCurveType c)
        {
			return c switch
			{
			    FadeCurveType.EaseInOut => Mathf.SmoothStep(0f, 1f, t),
			    FadeCurveType.Exponential => Mathf.Pow(t, 2f),
			    _ => t
			};
        }

        private IEnumerator FadeIn(AudioSource s, float target, float duration)
        {
			if (s == null) yield break;
			float time = 0f;
			while (time < duration)
			{
			    time += Time.deltaTime;
			    s.volume = Mathf.Lerp(0f, target, time / duration);
			    yield return null;
			}
			s.volume = target;
        }

        private IEnumerator FadeOut(AudioSource s, float duration)
        {
			if (s == null) yield break;
			float start = s.volume;
			float time = 0f;
			while (time < duration)
			{
			    time += Time.deltaTime;
			    s.volume = Mathf.Lerp(start, 0f, time / duration);
			    yield return null;
			}
			s.Stop();
        }

        #endregion

        #region PlayerPrefs Save/Load

        public void SaveVolumeSettings()
        {
			PlayerPrefs.SetFloat("Volume_Master", masterVolume);
			PlayerPrefs.SetFloat("Volume_Music", musicVolume);
			PlayerPrefs.SetFloat("Volume_Ambient", ambientVolume);
			PlayerPrefs.SetFloat("Volume_SFX", sfxVolume);
			PlayerPrefs.Save();
        }

        public void LoadVolumeSettings()
        {
			masterVolume = PlayerPrefs.GetFloat("Volume_Master", masterVolume);
			musicVolume = PlayerPrefs.GetFloat("Volume_Music", musicVolume);
			ambientVolume = PlayerPrefs.GetFloat("Volume_Ambient", ambientVolume);
			sfxVolume = PlayerPrefs.GetFloat("Volume_SFX", sfxVolume);
			ApplyMixerVolumes();
        }

        #endregion

        #region Helper Query Methods

        public bool TryGetSoundNames(out string[] names)
        {
			names = soundLibrary?.GetAllClipNames();
			return names != null && names.Length > 0;
        }

        public bool TryGetMusicNames(out string[] m)
        {
			m = musicLibrary?.GetAllClipNames();
			return m != null && m.Length > 0;
        }

        public bool TryGetAmbientNames(out string[] a)
        {
			a = ambientLibrary?.GetAllClipNames();
			return a != null && a.Length > 0;
        }

        #endregion

#if UNITY_EDITOR
        #region Editor: Scanning, Generating & Assigning Assets

        public void SetAudioFolderPath()
        {
			string selected = EditorUtility.OpenFolderPanel("Select Audio Folder", "Assets", "");
			if (string.IsNullOrEmpty(selected)) return;
			if (!selected.StartsWith(Application.dataPath))
			{
			    Debug.LogWarning("[AudioManager] Selected folder must be inside the project's Assets folder.");
			    return;
			}
			audioFolderPath = "Assets" + selected.Substring(Application.dataPath.Length).Replace("\\", "/").TrimEnd('/');
			Debug.Log($"[AudioManager] audioFolderPath set to: {audioFolderPath}");
        }

        public void ScanFolders()
        {
			scannedMusicClips = scannedMusicClips ?? new List<AudioClip>();
			scannedAmbientClips = scannedAmbientClips ?? new List<AudioClip>();
			scannedSFXClips = scannedSFXClips ?? new List<AudioClip>();

			scannedMusicClips.Clear(); scannedAmbientClips.Clear(); scannedSFXClips.Clear();

			if (string.IsNullOrEmpty(audioFolderPath))
			{
			    Debug.LogWarning("[AudioManager] audioFolderPath not set. Call SetAudioFolderPath() first.");
			    return;
			}

			string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolderPath });
			int total = guids.Length;
			for (int i = 0; i < total; i++)
			{
			    EditorUtility.DisplayProgressBar("Scanning Audio", $"Processing clip {i + 1}/{total}", (float)i / Math.Max(1, total));
			    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
			    if (clip == null) continue;

			    string lowerPath = path.ToLower().Replace("\\", "/");
			    string fileName = Path.GetFileNameWithoutExtension(path).ToLower();

			    if (lowerPath.Contains("/music/") || fileName.Contains("music") || fileName.Contains("bgm") || clip.length >= MUSIC_MIN_LENGTH)
			    {
			        scannedMusicClips.Add(clip);
			        continue;
			    }

			    if (lowerPath.Contains("/ambient/") || fileName.Contains("ambient") || (clip.length >= AMBIENT_MIN_LENGTH && clip.length < MUSIC_MIN_LENGTH))
			    {
			        scannedAmbientClips.Add(clip);
			        continue;
			    }

			    // fallback -> sfx
			    scannedSFXClips.Add(clip);
			}

			EditorUtility.ClearProgressBar();
			Debug.Log($"[AudioManager] Scan complete: {scannedMusicClips.Count} music, {scannedAmbientClips.Count} ambient, {scannedSFXClips.Count} sfx.");
        }

        // Generate ScriptableObject assets under GeneratedTracks (Music/Ambient/SFX)
        public void GenerateScriptableObjects()
        {
			if (string.IsNullOrEmpty(audioFolderPath))
			{
			    Debug.LogWarning("[AudioManager] audioFolderPath not set. Call SetAudioFolderPath() first.");
			    return;
			}

			string generatedFolder = audioFolderPath.TrimEnd('/') + "/GeneratedTracks";
			if (!AssetDatabase.IsValidFolder(generatedFolder)) AssetDatabase.CreateFolder(audioFolderPath.TrimEnd('/'), "GeneratedTracks");

			string musicFolder = EnsureSubfolder(generatedFolder, "Music");
			string ambientFolder = EnsureSubfolder(generatedFolder, "Ambient");
			string sfxFolder = EnsureSubfolder(generatedFolder, "SFX");

			string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolderPath });
			int total = guids.Length;

			Dictionary<string, List<AudioClip>> sfxGroups = new Dictionary<string, List<AudioClip>>();

			int createdMusic = 0, createdAmbient = 0, createdSfxGroups = 0;
			for (int i = 0; i < total; i++)
			{
			    string clipPath = AssetDatabase.GUIDToAssetPath(guids[i]);
			    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
			    if (clip == null) continue;

			    string fileName = Path.GetFileNameWithoutExtension(clipPath);
			    string normalized = clipPath.ToLower().Replace("\\", "/");

			    EditorUtility.DisplayProgressBar("Generating Assets", $"Processing {i + 1}/{total}: {fileName}", (float)i / Math.Max(1, total));

			    bool isMusic = normalized.Contains("/music/") || fileName.ToLower().Contains("music") || clip.length >= MUSIC_MIN_LENGTH;
			    bool isAmbient = !isMusic && (normalized.Contains("/ambient/") || fileName.ToLower().Contains("ambient") || (clip.length >= AMBIENT_MIN_LENGTH && clip.length < MUSIC_MIN_LENGTH));
			    bool isSfx = !isMusic && !isAmbient;

			    if (isMusic)
			    {
			        string assetPath = $"{musicFolder}/{SanitizeAssetName(fileName)}.asset";
			        if (AssetDatabase.LoadAssetAtPath<MusicTrack>(assetPath) == null)
			        {
						var mt = ScriptableObject.CreateInstance<MusicTrack>();
						mt.trackName = fileName;
						mt.clip = clip;
						mt.description = $"Generated from {clipPath}";
						AssetDatabase.CreateAsset(mt, assetPath);
						createdMusic++;
			        }
			        continue;
			    }

			    if (isAmbient)
			    {
			        string assetPath = $"{ambientFolder}/{SanitizeAssetName(fileName)}.asset";
			        if (AssetDatabase.LoadAssetAtPath<AmbientTrack>(assetPath) == null)
			        {
						var at = ScriptableObject.CreateInstance<AmbientTrack>();
						at.trackName = fileName;
						at.clip = clip;
						at.description = $"Generated from {clipPath}";
						AssetDatabase.CreateAsset(at, assetPath);
						createdAmbient++;
			        }
			        continue;
			    }

			    // SFX grouping by parent folder or filename prefix
			    string parent = Path.GetFileName(Path.GetDirectoryName(clipPath)) ?? fileName;
			    string lowerParent = parent.ToLower();
			    bool genericParent = lowerParent == "sfx" || lowerParent == "sounds" || lowerParent == "audio" || lowerParent == "clips";
			    string groupKey;
			    if (!genericParent) groupKey = SanitizeAssetName(parent);
			    else
			    {
			        var fileLower = fileName.ToLower();
			        int idx = fileLower.IndexOfAny(new char[] { '_', '-' });
			        groupKey = SanitizeAssetName(idx > 0 ? fileName.Substring(0, idx) : fileName);
			    }

			    if (!sfxGroups.TryGetValue(groupKey, out var list)) { list = new List<AudioClip>(); sfxGroups[groupKey] = list; }
			    list.Add(clip);
			}

			// create SoundClipData assets for groups
			int groupIndex = 0, groupsTotal = sfxGroups.Count;
			foreach (var kv in sfxGroups)
			{
			    groupIndex++;
			    EditorUtility.DisplayProgressBar("Generating SFX", $"Creating SFX {groupIndex}/{groupsTotal}: {kv.Key}", (float)groupIndex / Math.Max(1, groupsTotal));
			    string assetPath = $"{sfxFolder}/{kv.Key}.asset";
			    if (AssetDatabase.LoadAssetAtPath<SoundClipData>(assetPath) != null) continue;

			    var sd = ScriptableObject.CreateInstance<SoundClipData>();
			    sd.soundName = kv.Key;
			    sd.clips = kv.Value.ToArray();
			    AssetDatabase.CreateAsset(sd, assetPath);
			    createdSfxGroups++;
			}

			EditorUtility.ClearProgressBar();
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"[AudioManager] Generated: Music={createdMusic}, Ambient={createdAmbient}, SFX groups={createdSfxGroups}");
        }

        public void AssignToLibraries()
        {
			if (string.IsNullOrEmpty(audioFolderPath))
			{
			    Debug.LogWarning("[AudioManager] audioFolderPath not set. Call SetAudioFolderPath() first.");
			    return;
			}

			string generatedFolder = audioFolderPath.TrimEnd('/') + "/GeneratedTracks";
			if (!AssetDatabase.IsValidFolder(generatedFolder))
			{
			    Debug.LogWarning("[AudioManager] GeneratedTracks folder not found. Run GenerateScriptableObjects() first.");
			    return;
			}

			string musicFolder = generatedFolder + "/Music";
			string ambientFolder = generatedFolder + "/Ambient";
			string sfxFolder = generatedFolder + "/SFX";

			var musicLib = GetComponent<MusicLibrary>();
			var ambientLib = GetComponent<AmbientLibrary>();
			var sfxLib = GetComponent<SoundLibrary>();

			if (musicLib == null || ambientLib == null || sfxLib == null)
			{
			    Debug.LogWarning("[AudioManager] Missing library components for assignment.");
			    return;
			}

			int addedMusic = 0, addedAmbient = 0, addedSfx = 0;

			if (AssetDatabase.IsValidFolder(musicFolder))
			{
			    foreach (var g in AssetDatabase.FindAssets("t:MusicTrack", new[] { musicFolder }))
			    {
			        var p = AssetDatabase.GUIDToAssetPath(g);
			        var mt = AssetDatabase.LoadAssetAtPath<MusicTrack>(p);
			        if (mt != null && !musicLib.tracks.Contains(mt)) { musicLib.tracks.Add(mt); addedMusic++; }
			    }
			}

			if (AssetDatabase.IsValidFolder(ambientFolder))
			{
			    foreach (var g in AssetDatabase.FindAssets("t:AmbientTrack", new[] { ambientFolder }))
			    {
			        var p = AssetDatabase.GUIDToAssetPath(g);
			        var at = AssetDatabase.LoadAssetAtPath<AmbientTrack>(p);
			        if (at != null && !ambientLib.tracks.Contains(at)) { ambientLib.tracks.Add(at); addedAmbient++; }
			    }
			}

			if (AssetDatabase.IsValidFolder(sfxFolder))
			{
			    foreach (var g in AssetDatabase.FindAssets("t:SoundClipData", new[] { sfxFolder }))
			    {
			        var p = AssetDatabase.GUIDToAssetPath(g);
			        var sd = AssetDatabase.LoadAssetAtPath<SoundClipData>(p);
			        if (sd != null && !sfxLib.tracks.Contains(sd)) { sfxLib.tracks.Add(sd); addedSfx++; }
			    }
			}

			EditorUtility.SetDirty(musicLib);
			EditorUtility.SetDirty(ambientLib);
			EditorUtility.SetDirty(sfxLib);
			AssetDatabase.SaveAssets();

			Debug.Log($"[AudioManager] Assigned to libraries — Music: {addedMusic}, Ambient: {addedAmbient}, SFX: {addedSfx}");
        }

        private string EnsureSubfolder(string parentFolder, string subfolderName)
        {
			parentFolder = parentFolder.TrimEnd('/');
			string candidate = parentFolder + "/" + subfolderName;
			if (!AssetDatabase.IsValidFolder(candidate))
			{
			    AssetDatabase.CreateFolder(parentFolder, subfolderName);
			}
			return candidate;
        }

        private string SanitizeAssetName(string raw)
        {
			if (string.IsNullOrEmpty(raw)) return "unnamed";
			var clean = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
			return clean.Trim().Replace(' ', '_').ToLower();
        }

        private void OnValidate()
        {
			if (!Application.isPlaying)
			{
			    if (mainMixer == null)
			    {
			        string[] guids = AssetDatabase.FindAssets("t:AudioMixer");
			        if (guids.Length > 0)
			        {
			string path = AssetDatabase.GUIDToAssetPath(guids[0]);
			mainMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
			        }
			    }
			    if (mainMixer != null) TryAssignMissingGroupsAndSnapshots();
			    soundLibrary = GetComponent<SoundLibrary>();
			    musicLibrary = GetComponent<MusicLibrary>();
			    ambientLibrary = GetComponent<AmbientLibrary>();
			}
        }

        #endregion
#endif
    }
}