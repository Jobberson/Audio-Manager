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
		public enum FadeCurveType
		{
			Linear,
			EaseInOut,
			Exponential
		}

		public enum AudioChannel
		{
			Master,
			Music,
			Ambient,
			fx
		}

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

			// Ensure library refs exist at runtime (OnValidate only runs in editor)
			if (soundLibrary == null) soundLibrary = GetComponent<SoundLibrary>();
			if (musicLibrary == null) musicLibrary = GetComponent<MusicLibrary>();
			if (ambientLibrary == null) ambientLibrary = GetComponent<AmbientLibrary>();

			if (soundLibrary == null) Debug.LogWarning("SoundLibrary component missing on this GameObject.", this);
			if (musicLibrary == null) Debug.LogWarning("MusicLibrary component missing on this GameObject.", this);
			if (ambientLibrary == null) Debug.LogWarning("AmbientLibrary component missing on this GameObject.", this);

			// Try to auto-assign mixer/groups/snapshots (Editor-only search, runtime fallback)
			AutoAssignMixerAndGroups();

			// Create audio sources
			CreateAudioSources();

			// Assign groups (safe - group may be null)
			if (fxSource != null) fxSource.outputAudioMixerGroup = fxGroup;
			if (musicSource != null) musicSource.outputAudioMixerGroup = musicGroup;
			if (ambientSource != null) ambientSource.outputAudioMixerGroup = ambientGroup;

			// Set volume on all the channels
			SetChannelVolumes();

			// Initialize 3D SFX pool
			InitFXPool();
		}
		#endregion

		#region Auto-assign helpers
		private void AutoAssignMixerAndGroups()
		{
			// If already set, nothing to do
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
			// If we found a mixer, try to find groups and snapshots by common names
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

			// Try to assign groups by common names (case-sensitive matching performed by FindMatchingGroups)
			TryAssignGroup(ref musicGroup, new string[] { "Music", "Master/Music", "MusicGroup", "Music_Group" });
			TryAssignGroup(ref ambientGroup, new string[] { "Ambient", "Master/Ambient", "AmbientGroup", "Ambience" });
			TryAssignGroup(ref fxGroup, new string[] { "FX", "SFX", "Master/FX", "Master/SFX", "FXGroup" });

			// Snapshots — try common snapshot names
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
				catch { /* FindMatchingGroups can throw if the name is weird — ignore and continue */ }
			}

			// Fallback: try "Master" group if present
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
		#endregion

		#region Music controls
		// Play music with delay. 0 = No delay
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
		// Play ambient sound with delay 0 = No delay
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
			ambientSource.volume = currentVolume; // Reset volume for next playback
		}

		// Stop ambient sound
		public void StopAmbient()
		{
			if (ambientSource == null) return;
			ambientSource.Stop();
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

		#region Snapshot Controls
		public IEnumerator BlendSnapshots(
			SnapshotType from,
			SnapshotType to,
			float duration,
			FadeCurveType curveType = FadeCurveType.Linear
		)
		{
			AudioMixerSnapshot fromSnapshot = GetSnapshot(from);
			AudioMixerSnapshot toSnapshot = GetSnapshot(to);

			if (fromSnapshot == null || toSnapshot == null)
			{
				Debug.LogWarning("Snapshot not found.");
				yield break;
			}

			float time = 0f;
			while (time < duration)
			{
				time += Time.deltaTime;
				float t = Mathf.Clamp01(time / duration);
				float curveT = ApplyCurve(t, curveType);

				AudioMixerSnapshot[] snapshots = new[] { fromSnapshot, toSnapshot };
				float[] weights = new[] { 1f - curveT, curveT };

				mainMixer.TransitionToSnapshots(snapshots, weights, 0f); // 0f = instant because we control the blend manually

				yield return null;
			}

			toSnapshot.TransitionTo(0.01f); // finalize
		}

		#endregion

		#region Misc Methods
		public IEnumerator CrossfadeAudio(
			AudioSource fromSource,
			AudioSource toSource,
			AudioClip newClip,
			float duration,
			FadeCurveType curveType = FadeCurveType.Linear,
			bool waitForNextBar = false,
			float bpm = 120f // Optional, only for rhythm sync
		)
		{
			if (newClip == null || fromSource == null || toSource == null)
				yield break;

			if (waitForNextBar)
			{
				float secondsPerBeat = 60f / bpm;
				float timeToNextBar = secondsPerBeat * Mathf.Ceil(Time.time / secondsPerBeat) - Time.time;
				yield return new WaitForSeconds(timeToNextBar);
			}

			toSource.clip = newClip;
			toSource.volume = 0f;
			toSource.Play();

			float time = 0f;
			float startVolume = fromSource.volume;

			while (time < duration)
			{
				time += Time.deltaTime;
				float t = Mathf.Clamp01(time / duration);
				float curveT = ApplyCurve(t, curveType);

				fromSource.volume = Mathf.Lerp(startVolume, 0f, curveT);
				toSource.volume = Mathf.Lerp(0f, startVolume, curveT);

				yield return null;
			}

			fromSource.Stop();
			fromSource.volume = startVolume;
		}

		private void InitFXPool()
		{
			if (fxPool != null) return;

			GameObject poolObj = new("FX Pool");
			poolObj.transform.parent = transform;
			fxPool = poolObj.AddComponent<AudioSourcePool>();

			// Try to call Initialize(int, AudioMixerGroup) if present (supports older/newer pool implementations)
			MethodInfo initMethod = fxPool.GetType().GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (initMethod != null)
			{
				try
				{
					initMethod.Invoke(fxPool, new object[] { poolSize, fxGroup });
				}
				catch
				{
					// fallback to direct assignment if initialize invocation fails
					fxPool.fxGroup = fxGroup;
					fxPool.poolSize = poolSize;
				}
			}
			else
			{
				// fallback if Initialize not implemented
				fxPool.fxGroup = fxGroup;
				fxPool.poolSize = poolSize;
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
			musicSource.loop = MusicIsLooping; // Music is looping
			musicSource.playOnAwake = false;

			GameObject newAmbientsource = new("Ambient source");
			ambientSource = newAmbientsource.AddComponent<AudioSource>();
			newAmbientsource.transform.parent = transform;
			ambientSource.loop = AmbientIsLooping; // Ambient sound is looping
			ambientSource.playOnAwake = false;
		}
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
				Debug.LogWarning("[AudioManager] Selected folder must be inside the project's Assets folder.");
				return;
			}

			// Convert to project-relative path and normalize slashes
			audioFolderPath = "Assets" + selectedPath.Substring(Application.dataPath.Length).Replace("\\", "/").TrimEnd('/');
			Debug.Log($"[AudioManager] audioFolderPath set to: {audioFolderPath}");
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
				Debug.LogWarning("[AudioManager] audioFolderPath not set. Call SetAudioFolderPath() first.");
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
			Debug.Log($"[AudioManager] Scan complete: {scannedMusicClips.Count} music, {scannedAmbientClips.Count} ambient, {scannedSFXClips.Count} sfx.");
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
				Debug.LogWarning("[AudioManager] audioFolderPath not set. Call SetAudioFolderPath() first.");
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

			Debug.Log($"[AudioManager] Generated assets — Music: {createdMusic}, Ambient: {createdAmbient}, SFX groups: {createdSfx}");
		}

		/// <summary>
		/// Assigns generated assets to the appropriate runtime libraries on this GameObject.
		/// Uses the structured GeneratedTracks subfolders.
		/// </summary>
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
				Debug.LogWarning("[AudioManager] Missing one or more library components on this GameObject.");
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

			Debug.Log($"[AudioManager] Assigned to libraries — Music: {addedMusic}, Ambient: {addedAmbient}, SFX: {addedSfx}");
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

		#region Helper Methods
		public bool MusicIsPlaying() => musicSource != null && musicSource.isPlaying;
		public string GetCurrentMusicName() => musicSource != null && musicSource.clip != null ? musicSource.clip.name : "None";

		public bool AmbientIsPlaying() => ambientSource != null && ambientSource.isPlaying;
		public string GetCurrentAmbientName() => ambientSource != null && ambientSource.clip != null ? ambientSource.clip.name : "None";

		public SoundLibrary GetSoundLibrary() => soundLibrary;
		public MusicLibrary GetMusicLibrary() => musicLibrary;
		public AmbientLibrary GetAmbientLibrary() => ambientLibrary;

		private IEnumerator FadeIn(AudioSource source, float targetVolume, float duration)
		{
			float time = 0f;
			while (time < duration)
			{
				time += Time.deltaTime;
				source.volume = Mathf.Lerp(0f, targetVolume, time / duration);
				yield return null;
			}
			source.volume = targetVolume;
		}

		private IEnumerator FadeOut(AudioSource source, float duration)
		{
			float startVolume = source.volume;
			float time = 0f;
			while (time < duration)
			{
				time += Time.deltaTime;
				source.volume = Mathf.Lerp(startVolume, 0f, time / duration);
				yield return null;
			}
			source.Stop();
			source.volume = startVolume; // Reset for next playback
		}

		private IEnumerator Crossfade(AudioSource fromSource, AudioSource toSource, AudioClip newClip, float duration)
		{
			if (newClip == null || fromSource == null || toSource == null)
				yield break;

			toSource.clip = newClip;
			toSource.volume = 0f;
			toSource.Play();

			float time = 0f;
			float startVolume = fromSource.volume;

			while (time < duration)
			{
				time += Time.deltaTime;
				float t = Mathf.Clamp01(time / duration);
				fromSource.volume = Mathf.Lerp(startVolume, 0f, t);
				toSource.volume = Mathf.Lerp(0f, startVolume, t);
				yield return null;
			}

			fromSource.Stop();
			fromSource.volume = startVolume;
		}

		public void SaveVolumeSettings()
		{
			PlayerPrefs.SetFloat("Volume_Master", masterVolume);
			PlayerPrefs.SetFloat("Volume_Music", musicVolume);
			PlayerPrefs.SetFloat("Volume_Ambient", ambientVolume);
			PlayerPrefs.SetFloat("Volume_SFX", fxVolume);
			PlayerPrefs.Save();
		}

		public void LoadVolumeSettings()
		{
			masterVolume = PlayerPrefs.GetFloat("Volume_Master", 1f);
			musicVolume = PlayerPrefs.GetFloat("Volume_Music", 1f);
			ambientVolume = PlayerPrefs.GetFloat("Volume_Ambient", 1f);
			fxVolume = PlayerPrefs.GetFloat("Volume_SFX", 1f);
			SetChannelVolumes();
		}

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
			ResourceRequest request = Resources.LoadAsync<AudioClip>(path);
			yield return request;

			AudioClip clip = request.asset as AudioClip;
			if (clip == null)
			{
				Debug.LogWarning($"Audio clip not found at path: {path}");
				yield break;
			}

			if (delay > 0f)
				yield return new WaitForSeconds(delay);

			switch (type)
			{
				case AudioType.SFX:
					fxPool.PlayClip(clip, Vector3.zero, volume * fxVolume * masterVolume);
					break;

				case AudioType.Music:
					musicSource.clip = clip;
					musicSource.loop = loop;
					musicSource.outputAudioMixerGroup = musicGroup;
					if (fadeDuration > 0f)
					{
						musicSource.volume = 0f;
						musicSource.Play();
						StartCoroutine(FadeIn(musicSource, volume * musicVolume * masterVolume, fadeDuration));
					}
					else
					{
						musicSource.volume = volume * musicVolume * masterVolume;
						musicSource.Play();
					}
					break;

				case AudioType.Ambient:
					ambientSource.clip = clip;
					ambientSource.loop = loop;
					ambientSource.outputAudioMixerGroup = ambientGroup;
					if (fadeDuration > 0f)
					{
						ambientSource.volume = 0f;
						ambientSource.Play();
						StartCoroutine(FadeIn(ambientSource, volume * ambientVolume * masterVolume, fadeDuration));
					}
					else
					{
						ambientSource.volume = volume * ambientVolume * masterVolume;
						ambientSource.Play();
					}
					break;
			}
		}

		private AudioMixerSnapshot GetSnapshot(SnapshotType type)
		{
			return type switch
			{
				SnapshotType.Default => defaultSnapshot,
				SnapshotType.Combat => combatSnapshot,
				SnapshotType.Stealth => stealthSnapshot,
				SnapshotType.Underwater => underwaterSnapshot,
				_ => null
			};
		}

		private float ApplyCurve(float t, FadeCurveType type)
		{
			switch (type)
			{
				case FadeCurveType.EaseInOut:
					return Mathf.SmoothStep(0f, 1f, t);
				case FadeCurveType.Exponential:
					return Mathf.Pow(t, 2f);
				default:
					return t; // Linear
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
	}
}
