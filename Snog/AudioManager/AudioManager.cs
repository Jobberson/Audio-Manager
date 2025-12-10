﻿using UnityEngine;
using System.Collections;
using UnityEngine.Audio;
using Snog.Audio.Libraries;
using Snog.Audio.Clips;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Snog.Audio
{
	[RequireComponent(typeof(SoundLibrary))]
	[RequireComponent(typeof(MusicLibrary))]
	[RequireComponent(typeof(AmbientLibrary))]
	public class AudioManager : Singleton<AudioManager>
	{
		#region Variables
		public enum AudioChannel
		{
			Master,
			Music,
			Ambient,
			fx
		};
		public enum SnapshotType
		{
			Default,
			Combat,
			Stealth,
			Underwater
		}

		[Header("Folder Paths")]
		public string audioFolderPath;

		[Header("Volume")]
		[SerializeField, Range(0, 1)] private float masterVolume = 1; // Overall volume
		[SerializeField, Range(0, 1)] private float musicVolume = 1f; // Music volume
		[SerializeField, Range(0, 1)] private float ambientVolume = 1; // Ambient volume
		[SerializeField, Range(0, 1)] private float fxVolume = 1; // FX volume

		[SerializeField] private bool MusicIsLooping = true;
		[SerializeField] private bool AmbientIsLooping = true;

		[Header("Mixers")]
		[SerializeField] private AudioMixer mainMixer;
		[SerializeField] private AudioMixerGroup musicGroup;
		[SerializeField] private AudioMixerGroup ambientGroup;
		[SerializeField] private AudioMixerGroup fxGroup;

		[Header("Snapshots")]
		[SerializeField] private AudioMixerSnapshot defaultSnapshot;
		[SerializeField] private AudioMixerSnapshot combatSnapshot;
		[SerializeField] private AudioMixerSnapshot stealthSnapshot;
		[SerializeField] private AudioMixerSnapshot underwaterSnapshot;

		[Header("SFX Pool")]
		public AudioSourcePool fxPool;
		[SerializeField] private int poolSize = 10;

		[Header("Scanned Clips")]
		public List<AudioClip> scannedMusicClips = new();
		public List<AudioClip> scannedAmbientClips = new();
		public List<AudioClip> scannedSFXClips = new();

		// Seperate audiosources
		[SerializeField] private List<AudioSource> ambientLayerSources = new();
		private AudioSource musicSource;
		private AudioSource ambientSource;
		private AudioSource fxSource;

		// Sound libraries. All your audio clips
		private SoundLibrary soundLibrary;
		private MusicLibrary musicLibrary;
		private AmbientLibrary ambientLibrary;
		#endregion

		#region Unity Methods
		protected override void Awake()
		{
			base.Awake();

			// Ensure library refs exist at runtime 
			if (soundLibrary == null) soundLibrary = GetComponent<SoundLibrary>();
			if (musicLibrary == null) musicLibrary = GetComponent<MusicLibrary>();
			if (ambientLibrary == null) ambientLibrary = GetComponent<AmbientLibrary>();

			if (soundLibrary == null) Debug.LogWarning("SoundLibrary component missing on this GameObject.", this);
			if (musicLibrary == null) Debug.LogWarning("MusicLibrary component missing on this GameObject.", this);
			if (ambientLibrary == null) Debug.LogWarning("AmbientLibrary component missing on this GameObject.", this);

			// Try to auto-assign mixer/groups/snapshots 
#if UNITY_EDITOR					
			AutoAssignMixerAndGroups();
#endif
			// Create audio sources
			CreateAudioSources();

			// Assign groups
			if (fxSource != null) fxSource.outputAudioMixerGroup = fxGroup;
			if (musicSource != null) musicSource.outputAudioMixerGroup = musicGroup;
			if (ambientSource != null) ambientSource.outputAudioMixerGroup = ambientGroup;

			SetChannelVolumes();
			InitFXPool();
		}
		#endregion

		#region Auto-assign helpers
		private void AutoAssignMixerAndGroups()
		{
			if (mainMixer != null)
			{
				TryAssignMissingGroupsAndSnapshots();
				return;
			}

#if UNITY_EDITOR
			try
			{
				// Find the first AudioMixer asset in the project
				string[] guids = AssetDatabase.FindAssets("t:AudioMixer");
				if (guids != null && guids.Length > 0)
				{
					string path = AssetDatabase.GUIDToAssetPath(guids[0]);
					var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
					if (mixer != null)
					{
						mainMixer = mixer;
						Debug.Log($"Auto-assigned mainMixer: {mixer.name}", this);
					}
				}
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"AutoAssign mixer failed: {ex.Message}", this);
			}
#endif
			if (mainMixer != null)
			{
				TryAssignMissingGroupsAndSnapshots();
			}
			else
			{
				Debug.LogWarning("No AudioMixer assigned and none found automatically. Assign mainMixer manually in inspector or place a mixer asset in the project.", this);
			}
		}

		private void TryAssignMissingGroupsAndSnapshots()
		{
			if (mainMixer == null) return;

			TryAssignGroup(ref musicGroup, new string[] { "Music", "Master/Music", "MusicGroup", "Music_Group" });
			TryAssignGroup(ref ambientGroup, new string[] { "Ambient", "Master/Ambient", "AmbientGroup", "Ambience" });
			TryAssignGroup(ref fxGroup, new string[] { "FX", "SFX", "Master/FX", "Master/SFX", "FXGroup" });

			if (defaultSnapshot == null) defaultSnapshot = mainMixer.FindSnapshot("Default");
			if (combatSnapshot == null) combatSnapshot = mainMixer.FindSnapshot("Combat");
			if (stealthSnapshot == null) stealthSnapshot = mainMixer.FindSnapshot("Stealth");
			if (underwaterSnapshot == null) underwaterSnapshot = mainMixer.FindSnapshot("Underwater");
		}

		private void TryAssignGroup(ref AudioMixerGroup groupRef, string[] candidateNames)
		{
			if (groupRef != null || mainMixer == null) return;

			foreach (var name in candidateNames)
			{
				try
				{
					var found = mainMixer.FindMatchingGroups(name);
					if (found != null && found.Length > 0)
					{
						groupRef = found[0];
						Debug.Log($"Auto-assigned group '{groupRef.name}' for candidate '{name}'.", this);
						return;
					}
				}
				catch { }
			}

			try
			{
				var master = mainMixer.FindMatchingGroups("Master");
				if (master != null && master.Length > 0)
				{
					groupRef = master[0];
					Debug.Log($"Fallback assigned group '{groupRef.name}'.", this);
				}
			}
			catch { }
		}
		#endregion

		#region Volume Controls
		private void SetChannelVolumes()
		{
			SetVolume(masterVolume, AudioChannel.Master);
			SetVolume(fxVolume, AudioChannel.fx);
			SetVolume(musicVolume, AudioChannel.Music);
			SetVolume(ambientVolume, AudioChannel.Ambient);
		}

		public void SetVolume(float volumePercent, AudioChannel channel)
		{
			if (mainMixer == null)
			{
				Debug.LogWarning("Cannot SetVolume: mainMixer is not assigned.", this);
				return;
			}

			float volumeDB = Mathf.Log10(Mathf.Clamp(volumePercent, 0.0001f, 1f)) * 20;

			switch (channel)
			{
				case AudioChannel.Master:
					mainMixer.SetFloat("MasterVolume", volumeDB);
					break;
				case AudioChannel.fx:
					mainMixer.SetFloat("FXVolume", volumeDB);
					break;
				case AudioChannel.Music:
					mainMixer.SetFloat("MusicVolume", volumeDB);
					break;
				case AudioChannel.Ambient:
					mainMixer.SetFloat("AmbientVolume", volumeDB);
					break;
			}
		}
	
		public void SetAmbientLayerVolume(int index, float normalized)
		{
			if (index < 0 || index >= ambientLayerSources.Count) return;
			ambientLayerSources[index].volume = Mathf.Clamp01(normalized) * ambientVolume * masterVolume;
		}

		public bool TryGetCurrentAmbientProfileName(out string name)
		{
			name = currentAmbientProfile != null ? currentAmbientProfile.profileName : "None";
			return currentAmbientProfile != null;
		}
		#endregion

		#region Music controls
		// Play music with delay.
		public void PlayMusic(string musicName, float delay)
		{
			var clip = musicLibrary.GetClipFromName(musicName);
			if (clip == null)
			{
				Debug.LogWarning($"Music clip '{musicName}' not found.", this);
				return;
			}
			if (musicSource == null) return;
			musicSource.clip = clip;
			musicSource.PlayDelayed(delay);
		}

		// Play music fade in
		public IEnumerator PlayMusicFade(string musicName, float duration)
		{
			if (musicSource == null) yield break;

			float startVolume = 0;
			float targetVolume = musicSource.volume;
			float currentTime = 0;

			var clip = musicLibrary.GetClipFromName(musicName);
			if (clip == null)
			{
				Debug.LogWarning($"Music clip '{musicName}' not found.", this);
				yield break;
			}
			musicSource.clip = clip;
			musicSource.Play();

			while (currentTime < duration)
			{
				currentTime += Time.deltaTime;
				musicSource.volume = Mathf.Lerp(startVolume, targetVolume, currentTime / duration);
				yield return null;
			}
		}

		// Stop music
		public void StopMusic()
		{
			if (musicSource == null) return;
			musicSource.Stop();
		}

		// Stop music fading out
		public IEnumerator StopMusicFade(float duration)
		{
			if (musicSource == null) yield break;

			float currentVolume = musicSource.volume;
			float startVolume = musicSource.volume;
			float targetVolume = 0;
			float currentTime = 0;

			while (currentTime < duration)
			{
				currentTime += Time.deltaTime;
				musicSource.volume = Mathf.Lerp(startVolume, targetVolume, currentTime / duration);
				yield return null;
			}
			musicSource.Stop();
			musicSource.volume = currentVolume;
		}
		#endregion

		#region Ambient controls
		// Play ambient sound with delay
		public void PlayAmbient(string ambientName, float delay)
		{
			var clip = ambientLibrary.GetClipFromName(ambientName);
			if (clip == null)
			{
				Debug.LogWarning($"ambient clip '{ambientName}' not found.", this);
				return;
			}
			if (ambientSource == null) return;
			ambientSource.clip = clip;
			ambientSource.PlayDelayed(delay);
		}

		public IEnumerator PlayAmbientFade(string ambientName, float duration)
		{
			if (ambientSource == null) yield break;

			float startVolume = 0;
			float targetVolume = ambientSource.volume;
			float currentTime = 0;

			var clip = ambientLibrary.GetClipFromName(ambientName);
			if (clip == null)
			{
				Debug.LogWarning($"ambient clip '{ambientName}' not found.", this);
				yield break;
			}
			ambientSource.clip = clip;
			ambientSource.Play();

			while (currentTime < duration)
			{
				currentTime += Time.deltaTime;
				ambientSource.volume = Mathf.Lerp(startVolume, targetVolume, currentTime / duration);
				yield return null;
			}
		}

		// Stop ambient sound fading out
		public IEnumerator StopAmbientFade(float duration)
		{
			if (ambientSource == null) yield break;

			float currentVolume = ambientSource.volume;
			float startVolume = ambientSource.volume;
			float targetVolume = 0;
			float currentTime = 0;

			while (currentTime < duration)
			{
				currentTime += Time.deltaTime;
				ambientSource.volume = Mathf.Lerp(startVolume, targetVolume, currentTime / duration);
				yield return null;
			}

			ambientSource.Stop();
			ambientSource.volume = currentVolume;
		}

		// Crossfade ambient sound
		public IEnumerator CrossfadeAmbient(string newClipName, float duration)
		{
			AudioClip newClip = ambientLibrary.GetClipFromName(newClipName);
			if (newClip == null) yield break;

			AudioSource tempSource = gameObject.AddComponent<AudioSource>();
			tempSource.clip = newClip;
			tempSource.outputAudioMixerGroup = ambientGroup;
			tempSource.loop = AmbientIsLooping;
			tempSource.volume = 0;
			tempSource.Play();

			float time = 0;
			float startVolume = ambientSource != null ? ambientSource.volume : 1f;

			while (time < duration)
			{
				time += Time.deltaTime;
				float t = time / duration;
				if (ambientSource != null) ambientSource.volume = Mathf.Lerp(startVolume, 0, t);
				tempSource.volume = Mathf.Lerp(0, startVolume, t);
				yield return null;
			}

			if (ambientSource != null) ambientSource.Stop();
			if (ambientSource != null) Destroy(ambientSource);
			ambientSource = tempSource;
		}

		// Stop ambient sound
		public void StopAmbient()
		{
			if (ambientSource == null) return;
			ambientSource.Stop();
		}
		
		public IEnumerator StopAmbientProfileFade(float duration)
		{
			duration = Mathf.Max(0f, duration);

			float time = 0f;
			var start = new float[ambientLayerSources.Count];

			for (int i = 0; i < ambientLayerSources.Count; i++)
			{
				start[i] = ambientLayerSources[i].volume;
			}

			while (time < duration)
			{
				time += Time.deltaTime;
				float t = duration <= 0f ? 1f : Mathf.Clamp01(time / duration);

				for (int i = 0; i < ambientLayerSources.Count; i++)
				{
					ambientLayerSources[i].volume = Mathf.Lerp(start[i], 0f, t);
				}

				yield return null;
			}

			for (int i = 0; i < ambientLayerSources.Count; i++)
			{
				ambientLayerSources[i].Stop();
				ambientLayerSources[i].clip = null;
				ambientLayerSources[i].volume = 0f;
			}

			currentAmbientProfile = null;
		}

		public void StopAmbientProfile()
		{
			for (int i = 0; i < ambientLayerSources.Count; i++)
			{
				ambientLayerSources[i].Stop();
				ambientLayerSources[i].clip = null;
				ambientLayerSources[i].volume = 0f;
			}
			currentAmbientProfile = null;
		}

		public IEnumerator CrossfadeAmbientProfile(Snog.Audio.Layers.AmbientProfile next, float duration)
		{
			if (next == null || next.layers == null || next.layers.Length == 0)
				yield break;

			duration = Mathf.Max(0f, duration);

			EnsureAmbientLayerSources(next.layers.Length);

			for (int i = 0; i < next.layers.Length; i++)
			{
				var layer = next.layers[i];
				var src = ambientLayerSources[i];

				if (layer == null || layer.track == null || layer.track.clip == null)
				{
					src.Stop();
					src.clip = null;
					src.volume = 0f;
					continue;
				}

				src.clip = layer.track.clip;
				src.loop = layer.loop;
				src.spatialBlend = layer.spatialBlend;

				if (layer.randomStartTime && src.clip.length > 0f)
					src.time = Random.Range(0f, src.clip.length);

				src.pitch = (layer.pitchRange.x != layer.pitchRange.y)
					? Random.Range(layer.pitchRange.x, layer.pitchRange.y)
					: layer.pitchRange.x;

				src.volume = 0f;
				src.Play();
			}

			float time = 0f;
			var fromVolumes = new List<float>(ambientLayerSources.Count);
			var toVolumes   = new List<float>(ambientLayerSources.Count);

			for (int i = 0; i < ambientLayerSources.Count; i++)
			{
				float targetTo = 0f;
				if (i < next.layers.Length && next.layers[i] != null)
				{
					targetTo = Mathf.Clamp01(next.layers[i].volume) * ambientVolume * masterVolume;
				}
				toVolumes.Add(targetTo);

				float from = ambientLayerSources[i].clip != null ? ambientLayerSources[i].volume : 0f;
				fromVolumes.Add(from);
			}

			// Fade
			while (time < duration)
			{
				time += Time.deltaTime;
				float t = duration <= 0f ? 1f : Mathf.Clamp01(time / duration);

				for (int i = 0; i < ambientLayerSources.Count; i++)
				{
					float from = fromVolumes[i];
					float to   = toVolumes[i];

					ambientLayerSources[i].volume = Mathf.Lerp(from, to, t);
				}

				yield return null;
			}

			for (int i = 0; i < ambientLayerSources.Count; i++)
			{
				if (toVolumes[i] <= 0f)
				{
					ambientLayerSources[i].Stop();
					ambientLayerSources[i].clip = null;
				}
			}

			currentAmbientProfile = next;
		}

		public void PlayAmbientProfile(Snog.Audio.Layers.AmbientProfile profile)
		{
			if (profile == null || profile.layers == null || profile.layers.Length == 0)
			{
				Debug.LogWarning("AmbientProfile is empty or null.", this);
				return;
			}

			EnsureAmbientLayerSources(profile.layers.Length);

			for (int i = 0; i < profile.layers.Length; i++)
			{
				var layer = profile.layers[i];
				var src = ambientLayerSources[i];

				if (layer == null || layer.track == null || layer.track.clip == null)
				{
					src.Stop();
					src.clip = null;
					src.volume = 0f;
					continue;
				}

				src.clip = layer.track.clip;
				src.loop = layer.loop;
				src.spatialBlend = layer.spatialBlend;

				// Random start, random pitch
				if (layer.randomStartTime && src.clip.length > 0f)
				{
					src.time = Random.Range(0f, src.clip.length);
				}
				if (layer.pitchRange.x != layer.pitchRange.y)
				{
					src.pitch = Random.Range(layer.pitchRange.x, layer.pitchRange.y);
				}
				else
				{
					src.pitch = layer.pitchRange.x;
				}

				src.volume = Mathf.Clamp01(layer.volume) * ambientVolume * masterVolume;
				src.Play();
			}

			currentAmbientProfile = profile;
		}

		#endregion

		#region Sfx Controls
		// FX Audio
		public void PlaySound2D(string soundName)
		{
			var clip = soundLibrary.GetClipFromName(soundName);
			if (clip == null)
			{
				Debug.LogWarning($"Sound clip '{soundName}' not found.", this);
				return;
			}
			if (fxSource == null) return;
			fxSource.PlayOneShot(clip, fxVolume * masterVolume);
		}

		public void PlaySound3D(string soundName, Vector3 soundPosition)
		{
			var clip = soundLibrary.GetClipFromName(soundName);
			if (clip == null)
			{
				Debug.LogWarning($"Sound clip '{soundName}' not found.", this);
				return;
			}
			if (fxPool == null)
			{
				Debug.LogWarning("FX Pool not initialized.", this);
				return;
			}
			fxPool.PlayClip(clip, soundPosition, fxVolume * masterVolume);
		}
		#endregion

		#region Misc Methods
		private void InitFXPool()
		{
			if (fxPool != null) return;

			GameObject poolObj = new("FX Pool");
			poolObj.transform.parent = transform;
			fxPool = poolObj.AddComponent<AudioSourcePool>();

			MethodInfo initMethod = fxPool.GetType().GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (initMethod != null)
			{
				try
				{
					initMethod.Invoke(fxPool, new object[] { poolSize, fxGroup });
				}
				catch
				{
					fxPool.fxGroup = fxGroup;
					fxPool.poolSize = poolSize;
				}
			}
			else
			{
				fxPool.fxGroup = fxGroup;
				fxPool.poolSize = poolSize;
			}
		}

		// Snapshot Transitions
		public void TransitionToSnapshot(SnapshotType snapshot, float transitionTime)
		{
			switch (snapshot)
			{
				case SnapshotType.Default:
					if (defaultSnapshot != null) defaultSnapshot.TransitionTo(transitionTime);
					break;
				case SnapshotType.Combat:
					if (combatSnapshot != null) combatSnapshot.TransitionTo(transitionTime);
					break;
				case SnapshotType.Stealth:
					if (stealthSnapshot != null) stealthSnapshot.TransitionTo(transitionTime);
					break;
				case SnapshotType.Underwater:
					if (underwaterSnapshot != null) underwaterSnapshot.TransitionTo(transitionTime);
					break;
				default:
					Debug.LogWarning($"Snapshot '{snapshot}' not found.", this);
					break;
			}
		}

		private void CreateAudioSources()
		{
			GameObject newfxSource = new("2D fx source");
			fxSource = newfxSource.AddComponent<AudioSource>();
			newfxSource.transform.parent = transform;
			fxSource.playOnAwake = false;

			GameObject newMusicSource = new("Music source");
			musicSource = newMusicSource.AddComponent<AudioSource>();
			newMusicSource.transform.parent = transform;
			musicSource.loop = MusicIsLooping; 
			musicSource.playOnAwake = false;

			GameObject newAmbientsource = new("Ambient source");
			ambientSource = newAmbientsource.AddComponent<AudioSource>();
			newAmbientsource.transform.parent = transform;
			ambientSource.loop = AmbientIsLooping; 
			ambientSource.playOnAwake = false;
		}
		#endregion

		#region Helper Methods
		public bool MusicIsPlaying() => musicSource != null && musicSource.isPlaying;
		public string GetCurrentMusicName() => musicSource != null && musicSource.clip != null ? musicSource.clip.name : "None";

		public bool AmbientIsPlaying() => ambientSource != null && ambientSource.isPlaying;
		public string GetCurrentAmbientName() => ambientSource != null && ambientSource.clip != null ? ambientSource.clip.name : "None";

		public SoundLibrary GetSoundLibrary() => soundLibrary;
		public MusicLibrary GetMusicLibrary() => musicLibrary;
		public AmbientLibrary GetAmbientLibrary() => ambientLibrary;

		private void EnsureAmbientLayerSources(int needed)
		{
			if (ambientLayerSources == null)
				ambientLayerSources = new List<AudioSource>();

			while (ambientLayerSources.Count < needed)
			{
				GameObject go = new GameObject($"Ambient Layer {ambientLayerSources.Count}");
				go.transform.parent = transform;

				var src = go.AddComponent<AudioSource>();
				src.playOnAwake = false;
				src.loop = true;
				src.outputAudioMixerGroup = ambientGroup; 

				ambientLayerSources.Add(src);
			}

			for (int i = needed; i < ambientLayerSources.Count; i++)
			{
				ambientLayerSources[i].Stop();
				ambientLayerSources[i].clip = null;
				ambientLayerSources[i].volume = 0f;
			}
		}

		public bool TryGetSoundNames(out string[] names)
		{
			if (soundLibrary == null)
			{
				names = null;
				return false;
			}

			names = soundLibrary.GetAllClipNames();
			return names != null && names.Length > 0;
		}

		public bool TryGetMusicNames(out string[] names)
		{
			if (musicLibrary == null)
			{
				names = null;
				return false;
			}

			names = musicLibrary.GetAllClipNames();
			return names != null && names.Length > 0;
		}

		public bool TryGetAmbientNames(out string[] names)
		{
			if (ambientLibrary == null)
			{
				names = null;
				return false;
			}

			names = ambientLibrary.GetAllClipNames();
			return names != null && names.Length > 0;
		}

		public float GetMixerVolumeDB(string parameter)
		{
			if (mainMixer == null) return -80f;
			if (mainMixer.GetFloat(parameter, out float value))
				return value;
			return -80f; // Silence
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
			if (mainMixer.GetFloat(parameterName, out float value))
				return value;

			Debug.LogWarning($"Mixer parameter '{parameterName}' not found.", this);
			return -1f;
		}

#if UNITY_EDITOR
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
						Debug.Log($"Auto-assigned mainMixer: {mainMixer.name}");
					}
				}

				if (mainMixer != null)
				{
					TryAssignMissingGroupsAndSnapshots();
					EditorUtility.SetDirty(this);
				}
			}

			soundLibrary = GetComponent<SoundLibrary>();
			musicLibrary = GetComponent<MusicLibrary>();
			ambientLibrary = GetComponent<AmbientLibrary>();
		}
#endif
		#endregion

		#region Folder Scan
#if UNITY_EDITOR
		private const float SFX_MAX_LENGTH = 30f;     // <= this -> SFX candidate
		private const float AMBIENT_MIN_LENGTH = 30f; // ambient if between AMBIENT_MIN_LENGTH and MUSIC_MIN_LENGTH
		private const float MUSIC_MIN_LENGTH = 60f;   // >= this -> music candidate

		/// <summary>
		/// Let user pick a folder inside Assets. Forces project-relative path (Assets/...)
		/// </summary>
		public void SetAudioFolderPath()
		{
			string selectedPath = EditorUtility.OpenFolderPanel("Select Audio Folder (must be inside Assets)", "Assets", "");
			if (string.IsNullOrEmpty(selectedPath)) return;

			if (!selectedPath.StartsWith(Application.dataPath))
			{
				Debug.LogWarning("Selected folder must be inside the project's Assets folder.");
				return;
			}

			// Convert to project-relative path and normalize slashes
			audioFolderPath = "Assets" + selectedPath.Substring(Application.dataPath.Length).Replace("\\", "/").TrimEnd('/');
			Debug.Log($"audioFolderPath set to: {audioFolderPath}");
		}

		/// <summary>
		/// Scans the configured folder for AudioClips and fills the temporary scanned lists:
		/// scannedMusicClips, scannedAmbientClips, scannedSFXClips
		/// </summary>
		public void ScanFolders()
		{
			// Ensure lists exist
			scannedMusicClips = scannedMusicClips ?? new List<AudioClip>();
			scannedAmbientClips = scannedAmbientClips ?? new List<AudioClip>();
			scannedSFXClips = scannedSFXClips ?? new List<AudioClip>();

			scannedMusicClips.Clear();
			scannedAmbientClips.Clear();
			scannedSFXClips.Clear();

			if (string.IsNullOrEmpty(audioFolderPath))
			{
				Debug.LogWarning("audioFolderPath not set. Call SetAudioFolderPath() first.");
				return;
			}

			string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolderPath });
			int total = guids.Length;
			for (int i = 0; i < total; i++)
			{
				EditorUtility.DisplayProgressBar("Scanning Audio", $"Processing clip {i + 1}/{total}", (float)i / Mathf.Max(1, total));
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
				if (clip == null) continue;

				string normalizedPath = path.ToLower().Replace("\\", "/");
				string fileName = Path.GetFileNameWithoutExtension(path).ToLower();

				// Folder-based hints (highest priority)
				if (normalizedPath.Contains("/music/") || normalizedPath.Contains("/bgm/") || fileName.Contains("music") || fileName.Contains("bgm") || fileName.Contains("theme"))
				{
					scannedMusicClips.Add(clip);
					continue;
				}
				if (normalizedPath.Contains("/ambient/") || normalizedPath.Contains("/ambience/") || normalizedPath.Contains("/environment/") || fileName.Contains("ambient") || fileName.Contains("amb"))
				{
					scannedAmbientClips.Add(clip);
					continue;
				}
				if (normalizedPath.Contains("/sfx/") || normalizedPath.Contains("/fx/") || normalizedPath.Contains("/soundeffects/") || fileName.Contains("sfx") || fileName.Contains("fx"))
				{
					scannedSFXClips.Add(clip);
					continue;
				}

				// Filename heuristics
				if (fileName.Contains("music") || fileName.Contains("bgm") || fileName.Contains("theme") || clip.length >= MUSIC_MIN_LENGTH)
				{
					scannedMusicClips.Add(clip);
				}
				else if (fileName.Contains("ambient") || fileName.Contains("amb") || clip.length >= AMBIENT_MIN_LENGTH)
				{
					scannedAmbientClips.Add(clip);
				}
				else
				{
					scannedSFXClips.Add(clip);
				}
			}

			EditorUtility.ClearProgressBar();
			Debug.Log($"Scan complete: {scannedMusicClips.Count} music, {scannedAmbientClips.Count} ambient, {scannedSFXClips.Count} sfx.");
		}

		/// <summary>
		/// Ensure a folder exists under parent (creates it if needed) and returns the full path.
		/// </summary>
		private string EnsureSubfolder(string parentFolder, string subfolderName)
		{
			parentFolder = parentFolder.TrimEnd('/');
			var candidate = parentFolder + "/" + subfolderName;
			if (!AssetDatabase.IsValidFolder(candidate))
			{
				AssetDatabase.CreateFolder(parentFolder, subfolderName);
			}
			return candidate;
		}

		/// <summary>
		/// Generate ScriptableObjects under a GeneratedTracks folder with structured subfolders:
		/// GeneratedTracks/Music, GeneratedTracks/Ambient, GeneratedTracks/SFX
		/// For SFX we group variants by folder name (preferred) or filename prefix (fallback).
		/// </summary>
		public void GenerateScriptableObjects()
		{
			if (string.IsNullOrEmpty(audioFolderPath))
			{
				Debug.LogWarning("audioFolderPath not set. Call SetAudioFolderPath() first.");
				return;
			}

			// Ensure GeneratedTracks and subfolders exist
			string generatedFolder = audioFolderPath.TrimEnd('/') + "/GeneratedTracks";
			if (!AssetDatabase.IsValidFolder(generatedFolder))
			{
				AssetDatabase.CreateFolder(audioFolderPath, "GeneratedTracks");
			}

			string musicFolder = EnsureSubfolder(generatedFolder, "Music");
			string ambientFolder = EnsureSubfolder(generatedFolder, "Ambient");
			string sfxFolder = EnsureSubfolder(generatedFolder, "SFX");

			// Find clips deterministically
			string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolderPath });
			int total = guids.Length;
			EditorUtility.DisplayProgressBar("Generating Assets", $"Analyzing {total} clips", 0f);

			// Prepare SFX grouping map
			Dictionary<string, List<AudioClip>> sfxGroups = new();

			int createdMusic = 0, createdAmbient = 0, createdSfx = 0;

			for (int i = 0; i < total; i++)
			{
				string clipPath = AssetDatabase.GUIDToAssetPath(guids[i]);
				var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
				if (clip == null) continue;

				string fileName = Path.GetFileNameWithoutExtension(clipPath);
				string sanitizedFileName = SanitizeAssetName(fileName);
				string normalizedPath = clipPath.ToLower().Replace("\\", "/");

				EditorUtility.DisplayProgressBar("Generating Assets", $"Processing {i + 1}/{total}: {fileName}", (float)i / Mathf.Max(1, total));

				// Decide type and target folder
				bool isMusic = normalizedPath.Contains("/music/") || fileName.ToLower().Contains("music") || clip.length >= MUSIC_MIN_LENGTH;
				bool isAmbient = !isMusic && (normalizedPath.Contains("/ambient/") || fileName.ToLower().Contains("ambient") || (clip.length >= AMBIENT_MIN_LENGTH && clip.length < MUSIC_MIN_LENGTH));
				bool isSfx = !isMusic && !isAmbient || normalizedPath.Contains("/sfx/") || normalizedPath.Contains("/fx/") || clip.length <= SFX_MAX_LENGTH;

				if (isMusic)
				{
					string assetPath = $"{musicFolder}/{sanitizedFileName}.asset";
					if (AssetDatabase.LoadAssetAtPath<MusicTrack>(assetPath) != null) continue;

					var mt = ScriptableObject.CreateInstance<MusicTrack>();
					mt.trackName = fileName;
					mt.clip = clip;
					mt.description = $"Generated from {clipPath}";
					AssetDatabase.CreateAsset(mt, assetPath);
					createdMusic++;
					continue;
				}

				if (isAmbient)
				{
					string assetPath = $"{ambientFolder}/{sanitizedFileName}.asset";
					if (AssetDatabase.LoadAssetAtPath<AmbientTrack>(assetPath) != null) continue;

					var at = ScriptableObject.CreateInstance<AmbientTrack>();
					at.trackName = fileName;
					at.clip = clip;
					at.description = $"Generated from {clipPath}";
					AssetDatabase.CreateAsset(at, assetPath);
					createdAmbient++;
					continue;
				}

				// SFX candidate: group by folder or prefix
				string parentFolder = Path.GetFileName(Path.GetDirectoryName(clipPath));
				parentFolder = string.IsNullOrEmpty(parentFolder) ? fileName : parentFolder;

				string lowerParent = parentFolder.ToLower();
				bool parentIsGeneric = lowerParent == "sfx" || lowerParent == "sounds" || lowerParent == "audio" || lowerParent == "clips";
				string groupKey;
				if (!parentIsGeneric)
				{
					groupKey = SanitizeAssetName(parentFolder);
				}
				else
				{
					string fileLower = fileName.ToLower();
					int idx = fileLower.IndexOfAny(new char[] { '_', '-' });
					if (idx > 0) groupKey = SanitizeAssetName(fileName.Substring(0, idx));
					else groupKey = SanitizeAssetName(fileName);
				}

				if (!sfxGroups.TryGetValue(groupKey, out var list))
				{
					list = new List<AudioClip>();
					sfxGroups[groupKey] = list;
				}
				list.Add(clip);
			}

			// Create SFX assets (one SoundClipData per group) under the SFX subfolder
			int processedGroups = 0;
			int groupsTotal = sfxGroups.Count;
			foreach (var kv in sfxGroups)
			{
				processedGroups++;
				EditorUtility.DisplayProgressBar("Generating SFX", $"Creating SFX {processedGroups}/{groupsTotal}: {kv.Key}", (float)processedGroups / Mathf.Max(1, groupsTotal));
				string assetPath = $"{sfxFolder}/{kv.Key}.asset";

				// skip if exists
				if (AssetDatabase.LoadAssetAtPath<SoundClipData>(assetPath) != null) continue;

				var sd = ScriptableObject.CreateInstance<SoundClipData>();
				sd.soundName = kv.Key;
				sd.clips = kv.Value.ToArray();
				AssetDatabase.CreateAsset(sd, assetPath);
				createdSfx++;
			}

			EditorUtility.ClearProgressBar();
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log($"Generated assets — Music: {createdMusic}, Ambient: {createdAmbient}, SFX groups: {createdSfx}");
		}

		/// <summary>
		/// Assigns generated assets to the appropriate runtime libraries on this GameObject.
		/// Uses the structured GeneratedTracks subfolders.
		/// </summary>
		public void AssignToLibraries()
		{
			if (string.IsNullOrEmpty(audioFolderPath))
			{
				Debug.LogWarning("audioFolderPath not set. Call SetAudioFolderPath() first.");
				return;
			}

			string generatedFolder = audioFolderPath.TrimEnd('/') + "/GeneratedTracks";
			if (!AssetDatabase.IsValidFolder(generatedFolder))
			{
				Debug.LogWarning("GeneratedTracks folder not found. Run GenerateScriptableObjects() first.");
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
				Debug.LogWarning("Missing one or more library components on this GameObject.");
				return;
			}

			int addedMusic = 0, addedAmbient = 0, addedSfx = 0;

			if (AssetDatabase.IsValidFolder(musicFolder))
			{
				foreach (var guid in AssetDatabase.FindAssets("t:MusicTrack", new[] { musicFolder }))
				{
					var p = AssetDatabase.GUIDToAssetPath(guid);
					var mt = AssetDatabase.LoadAssetAtPath<MusicTrack>(p);
					if (mt != null && !musicLib.tracks.Contains(mt))
					{
						musicLib.tracks.Add(mt);
						addedMusic++;
					}
				}
			}

			if (AssetDatabase.IsValidFolder(ambientFolder))
			{
				foreach (var guid in AssetDatabase.FindAssets("t:AmbientTrack", new[] { ambientFolder }))
				{
					var p = AssetDatabase.GUIDToAssetPath(guid);
					var at = AssetDatabase.LoadAssetAtPath<AmbientTrack>(p);
					if (at != null && !ambientLib.tracks.Contains(at))
					{
						ambientLib.tracks.Add(at);
						addedAmbient++;
					}
				}
			}

			if (AssetDatabase.IsValidFolder(sfxFolder))
			{
				foreach (var guid in AssetDatabase.FindAssets("t:SoundClipData", new[] { sfxFolder }))
				{
					var p = AssetDatabase.GUIDToAssetPath(guid);
					var sd = AssetDatabase.LoadAssetAtPath<SoundClipData>(p);
					if (sd != null && !sfxLib.tracks.Contains(sd))
					{
						sfxLib.tracks.Add(sd);
						addedSfx++;
					}
				}
			}

			EditorUtility.SetDirty(musicLib);
			EditorUtility.SetDirty(ambientLib);
			EditorUtility.SetDirty(sfxLib);
			AssetDatabase.SaveAssets();

			Debug.Log($"Assigned to libraries — Music: {addedMusic}, Ambient: {addedAmbient}, SFX: {addedSfx}");
		}

		/// <summary>
		/// Helper: sanitize a folder or filename into a safe asset-friendly string.
		/// </summary>
		private string SanitizeAssetName(string raw)
		{
			if (string.IsNullOrEmpty(raw)) return "unnamed";
			// remove invalid characters and trim
			var clean = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
			return clean.Trim().Replace(' ', '_').ToLower();
		}
#endif
		#endregion
	}
}
