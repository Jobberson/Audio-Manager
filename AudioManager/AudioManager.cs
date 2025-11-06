﻿using UnityEngine;
using UnityEngine.Audio;
using System.IO;
using System.Reflection;
using System.Collections;
using Snog.Audio.Libraries;

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
		[SerializeField, Range(0, 1)] private float masterVolume = 1;
		[SerializeField, Range(0, 1)] private float musicVolume = 1f;
		[SerializeField, Range(0, 1)] private float ambientVolume = 1;
		[SerializeField, Range(0, 1)] private float fxVolume = 1;

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
		public List<AudioClip> scannedMusicClips = new List<AudioClip>();
		public List<AudioClip> scannedAmbientClips = new List<AudioClip>();
		public List<AudioClip> scannedSFXClips = new List<AudioClip>();


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

			// Try to assign groups
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

			// Try to assign groups
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
				catch {}
			}

			// Fallback
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

		// Stop music fading out to silence
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

		// Stop ambient sound fading out to silence
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
			GameObject newfxSource = new GameObject("2D fx source");
			fxSource = newfxSource.AddComponent<AudioSource>();
			newfxSource.transform.parent = transform;
			fxSource.playOnAwake = false;

			GameObject newMusicSource = new GameObject("Music source");
			musicSource = newMusicSource.AddComponent<AudioSource>();
			newMusicSource.transform.parent = transform;
			musicSource.loop = MusicIsLooping;
			musicSource.playOnAwake = false;

			GameObject newAmbientsource = new GameObject("Ambient source");
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
		#endregion

		#region Folder Scan
		public void SetAudioFolderPath()
		{
			string selectedPath = EditorUtility.OpenFolderPanel("Select Audio Folder", "Assets", "");
			if (!string.IsNullOrEmpty(selectedPath))
			{
				if (selectedPath.StartsWith(Application.dataPath))
				{
					audioFolderPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
					Debug.Log($"Audio folder set to: {audioFolderPath}");
				}
				else
				{
					Debug.LogWarning("Selected folder must be inside the Assets directory.");
				}
			}
		}

		public void ScanFolders()
		{
			scannedMusicClips.Clear();
			scannedAmbientClips.Clear();
			scannedSFXClips.Clear();

			if (string.IsNullOrEmpty(audioFolderPath))
			{
				Debug.LogWarning("Audio folder path is not set. Please set it first.");
				return;
			}

			string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolderPath });

			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
				if (clip == null) continue;

				string lowerPath = path.ToLower();
				string lowerName = clip.name.ToLower();

				if (lowerPath.Contains("music") || lowerName.Contains("music"))
				{
					scannedMusicClips.Add(clip);
				}
				else if (lowerPath.Contains("ambient") || lowerName.Contains("ambient") || lowerName.Contains("amb"))
				{
					scannedAmbientClips.Add(clip);
				}
				else if (lowerPath.Contains("sfx") || lowerName.Contains("sfx") || lowerName.Contains("fx") || lowerName.Contains("sound"))
				{
					scannedSFXClips.Add(clip);
				}
				else
				{
					// Fallback
					if (lowerName.Contains("loop") || lowerName.Contains("theme"))
						scannedMusicClips.Add(clip);
					else if (lowerName.Contains("wind") || lowerName.Contains("rain") || lowerName.Contains("forest"))
						scannedAmbientClips.Add(clip);
					else
						scannedSFXClips.Add(clip);
				}
			}

			Debug.Log($"Scan complete: {scannedMusicClips.Count} music, {scannedAmbientClips.Count} ambient, {scannedSFXClips.Count} SFX clips found.");
		}

		public void GenerateScriptableObjects()
		{
			if (string.IsNullOrEmpty(audioFolderPath))
			{
				Debug.LogWarning("Audio folder path is not set.");
				return;
			}

			string generatedFolder = Path.Combine(audioFolderPath, "GeneratedTracks");
			if (!AssetDatabase.IsValidFolder(generatedFolder))
			{
				AssetDatabase.CreateFolder(audioFolderPath, "GeneratedTracks");
			}

			string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolderPath });
			int musicCount = 0, ambientCount = 0, sfxCount = 0;

			foreach (string guid in guids)
			{
				string clipPath = AssetDatabase.GUIDToAssetPath(guid);
				AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
				if (clip == null) continue;

				string fileName = Path.GetFileNameWithoutExtension(clipPath);
				string assetPath = Path.Combine(generatedFolder, fileName + ".asset");

				if (File.Exists(assetPath)) continue;

				ScriptableObject track = null;
				string folderLower = audioFolderPath.ToLower();

				if (folderLower.Contains("music"))
				{
					track = ScriptableObject.CreateInstance<MusicTrack>();
					((MusicTrack)track).trackName = fileName;
					((MusicTrack)track).clip = clip;
					musicCount++;
				}
				else if (folderLower.Contains("ambient"))
				{
					track = ScriptableObject.CreateInstance<AmbientTrack>();
					((AmbientTrack)track).trackName = fileName;
					((AmbientTrack)track).clip = clip;
					ambientCount++;
				}
				else if (folderLower.Contains("sfx"))
				{
					track = ScriptableObject.CreateInstance<SFXTrack>();
					((SFXTrack)track).trackName = fileName;
					((SFXTrack)track).clip = clip;
					sfxCount++;
				}
				else
				{
					Debug.LogWarning($"Unknown track type for clip: {fileName}");
					continue;
				}

				AssetDatabase.CreateAsset(track, assetPath);
				Debug.Log($"Created ScriptableObject: {assetPath}");
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"Generated {musicCount} music, {ambientCount} ambient, and {sfxCount} SFX tracks.");
		}

		public void AssignToLibraries()
		{
			if (string.IsNullOrEmpty(audioFolderPath))
			{
				Debug.LogWarning("Audio folder path is not set.");
				return;
			}

			string generatedFolder = System.IO.Path.Combine(audioFolderPath, "GeneratedTracks");
			if (!AssetDatabase.IsValidFolder(generatedFolder))
			{
				Debug.LogWarning("GeneratedTracks folder not found.");
				return;
			}

			MusicLibrary musicLib = GetComponent<MusicLibrary>();
			AmbientLibrary ambientLib = GetComponent<AmbientLibrary>();
			SFXLibrary sfxLib = GetComponent<SFXLibrary>();
			if (musicLib == null || ambientLib == null || sfxLib == null)
			{
				Debug.LogWarning("One or more library components are missing.");
				return;
			}

			int musicCount = 0;
			int ambientCount = 0;
			int sfxCount = 0;

			string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { generatedFolder });
			foreach (string guid in guids)
			{
				string assetPath = AssetDatabase.GUIDToAssetPath(guid);
				ScriptableObject obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);

				if (obj is MusicTrack musicTrack && !musicLib.tracks.Contains(musicTrack))
				{
					musicLib.tracks.Add(musicTrack);
					musicCount++;
				}
				else if (obj is AmbientTrack ambientTrack && !ambientLib.tracks.Contains(ambientTrack))
				{
					ambientLib.tracks.Add(ambientTrack);
					ambientCount++;
				}
				else if (obj is SFXTrack sfxTrack && !sfxLib.tracks.Contains(sfxTrack))
				{
					sfxLib.tracks.Add(sfxTrack);
					sfxCount++;
				}
			}

			EditorUtility.SetDirty(musicLib);
			EditorUtility.SetDirty(ambientLib);
			EditorUtility.SetDirty(sfxLib);
			AssetDatabase.SaveAssets();

			Debug.Log($"Assigned {musicCount} music, {ambientCount} ambient, and {sfxCount} SFX tracks to libraries.");
		}
		#endif
		#endregion
	}
}
