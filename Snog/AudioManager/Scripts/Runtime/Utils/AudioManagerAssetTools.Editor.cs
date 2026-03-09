#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Snog.Audio.Clips;
using Snog.Audio.Libraries;

namespace Snog.Audio
{
    public sealed partial class AudioManager
    {
        #region Asset Tools - Serialized State

        [Header("Folder Paths (Editor Only)")]
        [Header("Scanned Clips (Editor Only)")]
        [SerializeField]
        private List<AudioClip> scannedMusicClips = new();

        [SerializeField]
        private List<AudioClip> scannedAmbientClips = new();

        [SerializeField]
        private List<AudioClip> scannedSFXClips = new();

        private const float SFX_MAX_LENGTH = 30f;
        private const float AMBIENT_MIN_LENGTH = 30f;
        private const float MUSIC_MIN_LENGTH = 60f;

        #endregion

        #region Public Editor API

        public void SetAudioFolderPath()
        {
            string selectedPath = EditorUtility.OpenFolderPanel(
                "Select Audio Folder (must be inside Assets)",
                "Assets",
                ""
            );

            if (string.IsNullOrEmpty(selectedPath))
            {
                return;
            }

            if (!selectedPath.StartsWith(Application.dataPath, StringComparison.Ordinal))
            {
                Debug.LogWarning("Selected folder must be inside the project's Assets folder.");
                return;
            }

            audioFolderPath = "Assets" + selectedPath.Substring(Application.dataPath.Length)
                .Replace("\\", "/")
                .TrimEnd('/');

            Debug.Log($"audioFolderPath set to: {audioFolderPath}", this);
            EditorUtility.SetDirty(this);
        }

        public void ScanFolders()
        {
            EnsureScannedLists();

            scannedMusicClips.Clear();
            scannedAmbientClips.Clear();
            scannedSFXClips.Clear();

            if (string.IsNullOrEmpty(audioFolderPath))
            {
                Debug.LogWarning("audioFolderPath not set. Call SetAudioFolderPath() first.", this);
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolderPath });
            int total = guids.Length;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip == null)
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        "Scanning Audio",
                        $"Processing {i + 1}/{total}: {clip.name}",
                        (float)i / Mathf.Max(1, total)
                    );

                    string normalizedPath = path.Replace("\\", "/").ToLowerInvariant();
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    string fileLower = fileName.ToLowerInvariant();

                    if
                    (
                        normalizedPath.Contains("/music/") ||
                        normalizedPath.Contains("/bgm/") ||
                        fileLower.Contains("music") ||
                        fileLower.Contains("bgm") ||
                        fileLower.Contains("theme")
                    )
                    {
                        scannedMusicClips.Add(clip);
                        continue;
                    }

                    if
                    (
                        normalizedPath.Contains("/ambient/") ||
                        normalizedPath.Contains("/ambience/") ||
                        normalizedPath.Contains("/environment/") ||
                        fileLower.Contains("ambient") ||
                        fileLower.Contains("ambience") ||
                        fileLower.Contains("amb_") ||
                        fileLower.Contains("amb-")
                    )
                    {
                        scannedAmbientClips.Add(clip);
                        continue;
                    }

                    if
                    (
                        normalizedPath.Contains("/sfx/") ||
                        normalizedPath.Contains("/fx/") ||
                        normalizedPath.Contains("/soundeffects/") ||
                        fileLower.Contains("sfx") ||
                        fileLower.Contains("fx")
                    )
                    {
                        scannedSFXClips.Add(clip);
                        continue;
                    }

                    if (clip.length >= MUSIC_MIN_LENGTH)
                    {
                        scannedMusicClips.Add(clip);
                    }
                    else if (clip.length >= AMBIENT_MIN_LENGTH)
                    {
                        scannedAmbientClips.Add(clip);
                    }
                    else
                    {
                        scannedSFXClips.Add(clip);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log(
                $"Scan complete: {scannedMusicClips.Count} music, {scannedAmbientClips.Count} ambient, {scannedSFXClips.Count} sfx.",
                this
            );

            EditorUtility.SetDirty(this);
        }

        public void GenerateScriptableObjects()
        {
            if (string.IsNullOrEmpty(audioFolderPath))
            {
                Debug.LogWarning("audioFolderPath not set. Call SetAudioFolderPath() first.", this);
                return;
            }

            string generatedFolder = audioFolderPath.TrimEnd('/') + "/GeneratedTracks";
            if (!AssetDatabase.IsValidFolder(generatedFolder))
            {
                AssetDatabase.CreateFolder(audioFolderPath, "GeneratedTracks");
            }

            string musicFolder = EnsureSubfolder(generatedFolder, "Music");
            string ambientFolder = EnsureSubfolder(generatedFolder, "Ambient");
            string sfxFolder = EnsureSubfolder(generatedFolder, "SFX");

            // ADDED: Load existing assets to check for duplicates
            var existingMusic = LoadExistingAssets<MusicTrack>(musicFolder);
            var existingAmbient = LoadExistingAssets<AmbientTrack>(ambientFolder);
            var existingSfx = LoadExistingAssets<SoundClipData>(sfxFolder);

            string[] clipGuids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolderPath });
            int total = clipGuids.Length;

            Dictionary<string, List<AudioClip>> sfxGroups = new();
            int createdMusic = 0;
            int createdAmbient = 0;
            int createdSfx = 0;
            int skippedMusic = 0;
            int skippedAmbient = 0;
            int skippedSfx = 0;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string clipPath = AssetDatabase.GUIDToAssetPath(clipGuids[i]);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                    if (clip == null)
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        "Generating Assets",
                        $"Analyzing {i + 1}/{total}: {clip.name}",
                        (float)i / Mathf.Max(1, total)
                    );

                    string normalizedPath = clipPath.Replace("\\", "/").ToLowerInvariant();
                    string fileName = Path.GetFileNameWithoutExtension(clipPath);
                    string fileLower = fileName.ToLowerInvariant();

                    bool isMusic =
                    (
                        normalizedPath.Contains("/music/") ||
                        normalizedPath.Contains("/bgm/") ||
                        fileLower.Contains("music") ||
                        fileLower.Contains("bgm") ||
                        fileLower.Contains("theme") ||
                        clip.length >= MUSIC_MIN_LENGTH
                    );

                    bool isAmbient =
                    (
                        !isMusic &&
                        (
                            normalizedPath.Contains("/ambient/") ||
                            normalizedPath.Contains("/ambience/") ||
                            normalizedPath.Contains("/environment/") ||
                            fileLower.Contains("ambient") ||
                            fileLower.Contains("ambience") ||
                            (
                                clip.length >= AMBIENT_MIN_LENGTH &&
                                clip.length < MUSIC_MIN_LENGTH
                            )
                        )
                    );

                    if (isMusic)
                    {
                        string assetName = SanitizeAssetName(fileName);
                        
                        // FIXED: Check if asset already exists with same clip
                        if (AssetExistsWithSameClip(existingMusic, assetName, clip))
                        {
                            skippedMusic++;
                            continue;
                        }

                        string assetPath = GetUniqueAssetPath(musicFolder, assetName);

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
                        string assetName = SanitizeAssetName(fileName);
                        
                        // FIXED: Check if asset already exists with same clip
                        if (AssetExistsWithSameClip(existingAmbient, assetName, clip))
                        {
                            skippedAmbient++;
                            continue;
                        }

                        string assetPath = GetUniqueAssetPath(ambientFolder, assetName);

                        var at = ScriptableObject.CreateInstance<AmbientTrack>();
                        at.trackName = fileName;
                        at.clip = clip;
                        at.description = $"Generated from {clipPath}";
                        AssetDatabase.CreateAsset(at, assetPath);

                        createdAmbient++;
                        continue;
                    }

                    string parentFolder = Path.GetFileName(Path.GetDirectoryName(clipPath));
                    parentFolder = string.IsNullOrEmpty(parentFolder) ? fileName : parentFolder;

                    string lowerParent = parentFolder.ToLowerInvariant();

                    bool parentIsGeneric =
                    (
                        lowerParent == "sfx" ||
                        lowerParent == "sounds" ||
                        lowerParent == "audio" ||
                        lowerParent == "clips"
                    );

                    string groupKey;
                    if (!parentIsGeneric)
                    {
                        groupKey = SanitizeAssetName(parentFolder);
                    }
                    else
                    {
                        int idx = fileLower.IndexOfAny(new[] { '_', '-' });
                        groupKey = idx > 0
                            ? SanitizeAssetName(fileName.Substring(0, idx))
                            : SanitizeAssetName(fileName);
                    }

                    if (!sfxGroups.TryGetValue(groupKey, out var list))
                    {
                        list = new List<AudioClip>();
                        sfxGroups[groupKey] = list;
                    }

                    list.Add(clip);
                }

                int groupIndex = 0;
                foreach (var kv in sfxGroups)
                {
                    groupIndex++;

                    EditorUtility.DisplayProgressBar(
                        "Generating SFX Groups",
                        $"Creating {groupIndex}/{sfxGroups.Count}: {kv.Key}",
                        (float)groupIndex / Mathf.Max(1, sfxGroups.Count)
                    );

                    // FIXED: Check if SFX group already exists with same clips
                    if (SfxGroupExistsWithSameClips(existingSfx, kv.Key, kv.Value))
                    {
                        skippedSfx++;
                        continue;
                    }

                    string assetPath = GetUniqueAssetPath(sfxFolder, kv.Key);

                    var sd = ScriptableObject.CreateInstance<SoundClipData>();
                    sd.soundName = kv.Key;
                    sd.clips = kv.Value.ToArray();
                    AssetDatabase.CreateAsset(sd, assetPath);

                    createdSfx++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"Generated assets — Music: {createdMusic} created, {skippedMusic} skipped | " +
                $"Ambient: {createdAmbient} created, {skippedAmbient} skipped | " +
                $"SFX: {createdSfx} created, {skippedSfx} skipped",
                this
            );
        }

        public void AssignToLibraries()
        {
            if (string.IsNullOrEmpty(audioFolderPath))
            {
                Debug.LogWarning("audioFolderPath not set. Call SetAudioFolderPath() first.", this);
                return;
            }

            string generatedFolder = audioFolderPath.TrimEnd('/') + "/GeneratedTracks";
            if (!AssetDatabase.IsValidFolder(generatedFolder))
            {
                Debug.LogWarning("GeneratedTracks folder not found. Run GenerateScriptableObjects() first.", this);
                return;
            }

            var musicLib = GetComponent<MusicLibrary>();
            var ambientLib = GetComponent<AmbientLibrary>();
            var sfxLib = GetComponent<SoundLibrary>();

            if
            (
                musicLib == null ||
                ambientLib == null ||
                sfxLib == null
            )
            {
                Debug.LogWarning("Missing one or more library components on this GameObject.", this);
                return;
            }

            string musicFolder = generatedFolder + "/Music";
            string ambientFolder = generatedFolder + "/Ambient";
            string sfxFolder = generatedFolder + "/SFX";

            int addedMusic = 0;
            int addedAmbient = 0;
            int addedSfx = 0;

            if (AssetDatabase.IsValidFolder(musicFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:MusicTrack", new[] { musicFolder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
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
                foreach (string guid in AssetDatabase.FindAssets("t:AmbientTrack", new[] { ambientFolder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
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
                foreach (string guid in AssetDatabase.FindAssets("t:SoundClipData", new[] { sfxFolder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
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

            Debug.Log($"Assigned to libraries — Music: {addedMusic}, Ambient: {addedAmbient}, SFX: {addedSfx}", this);
        }

        #endregion

        #region Helpers

        private void EnsureScannedLists()
        {
            scannedMusicClips ??= new List<AudioClip>();
            scannedAmbientClips ??= new List<AudioClip>();
            scannedSFXClips ??= new List<AudioClip>();
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
            if (string.IsNullOrEmpty(raw))
            {
                return "unnamed";
            }

            string clean = new string(
                raw
                    .Trim()
                    .Select(c => char.ToLowerInvariant(c))
                    .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    .ToArray()
            );

            return string.IsNullOrEmpty(clean) ? "unnamed" : clean;
        }

        private string GetUniqueAssetPath(string folder, string baseName)
        {
            folder = folder.TrimEnd('/');
            baseName = SanitizeAssetName(baseName);

            string path = $"{folder}/{baseName}.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
            {
                return path;
            }

            int suffix = 1;
            while (true)
            {
                string candidate = $"{folder}/{baseName}_{suffix:00}.asset";
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(candidate) == null)
                {
                    return candidate;
                }

                suffix++;
            }
        }

        /// <summary>
        /// Loads all existing assets of type T from the specified folder.
        /// Returns a dictionary mapping sanitized names to the assets.
        /// </summary>
        private Dictionary<string, T> LoadExistingAssets<T>(string folder) where T : UnityEngine.Object
        {
            var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

            if (!AssetDatabase.IsValidFolder(folder))
                return result;

            string typeFilter = typeof(T).Name;
            string[] guids = AssetDatabase.FindAssets($"t:{typeFilter}", new[] { folder });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);

                if (asset != null)
                {
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    string sanitized = SanitizeAssetName(fileName);
                    result[sanitized] = asset;
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if a MusicTrack or AmbientTrack already exists with the same clip reference.
        /// </summary>
        private bool AssetExistsWithSameClip<T>(Dictionary<string, T> existing, string assetName, AudioClip clip) where T : UnityEngine.Object
        {
            if (!existing.TryGetValue(assetName, out var existingAsset))
                return false;

            // Check if it's MusicTrack
            if (existingAsset is MusicTrack mt)
            {
                return mt.clip == clip;
            }

            // Check if it's AmbientTrack
            if (existingAsset is AmbientTrack at)
            {
                return at.clip == clip;
            }

            return false;
        }

        /// <summary>
        /// Checks if a SoundClipData already exists with the same clip array.
        /// </summary>
        private bool SfxGroupExistsWithSameClips(Dictionary<string, SoundClipData> existing, string groupKey, List<AudioClip> clips)
        {
            if (!existing.TryGetValue(groupKey, out var existingData))
                return false;

            // Check if the clip arrays are identical
            if (existingData.clips == null || existingData.clips.Length != clips.Count)
                return false;

            // Compare clips (order-independent)
            var existingSet = new HashSet<AudioClip>(existingData.clips);
            var newSet = new HashSet<AudioClip>(clips);

            return existingSet.SetEquals(newSet);
        }

        #endregion
    }
}
#endif