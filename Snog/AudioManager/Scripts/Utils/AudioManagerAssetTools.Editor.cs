
#if UNITY_EDITOR

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
        [SerializeField] private List<AudioClip> scannedMusicClips = new();
        [SerializeField] private List<AudioClip> scannedAmbientClips = new();
        [SerializeField] private List<AudioClip> scannedSFXClips = new();

        private const float SFX_MAX_LENGTH = 30f;
        private const float AMBIENT_MIN_LENGTH = 30f;
        private const float MUSIC_MIN_LENGTH = 60f;

        #endregion

        #region Public Editor API

        /// <summary>
        /// Let user pick a folder inside Assets. Stores a project-relative path: Assets/...
        /// </summary>
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

            if (!selectedPath.StartsWith(Application.dataPath))
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

        /// <summary>
        /// Scans the configured folder for AudioClips and fills the scanned lists.
        /// </summary>
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

                string normalizedPath = path.ToLower().Replace("\\", "/");
                string fileName = Path.GetFileNameWithoutExtension(path).ToLower();

                // Folder/name hints first
                if (normalizedPath.Contains("/music/") ||
                    normalizedPath.Contains("/bgm/") ||
                    fileName.Contains("music") ||
                    fileName.Contains("bgm") ||
                    fileName.Contains("theme"))
                {
                    scannedMusicClips.Add(clip);
                    continue;
                }

                if (normalizedPath.Contains("/ambient/") ||
                    normalizedPath.Contains("/ambience/") ||
                    normalizedPath.Contains("/environment/") ||
                    fileName.Contains("ambient") ||
                    fileName.Contains("amb"))
                {
                    scannedAmbientClips.Add(clip);
                    continue;
                }

                if (normalizedPath.Contains("/sfx/") ||
                    normalizedPath.Contains("/fx/") ||
                    normalizedPath.Contains("/soundeffects/") ||
                    fileName.Contains("sfx") ||
                    fileName.Contains("fx"))
                {
                    scannedSFXClips.Add(clip);
                    continue;
                }

                // Length heuristics fallback
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

            EditorUtility.ClearProgressBar();

            Debug.Log(
                $"Scan complete: {scannedMusicClips.Count} music, {scannedAmbientClips.Count} ambient, {scannedSFXClips.Count} sfx.",
                this
            );

            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Generates ScriptableObjects into: {audioFolderPath}/GeneratedTracks/{Music|Ambient|SFX}
        /// Music => MusicTrack, Ambient => AmbientTrack, SFX => SoundClipData (grouped).
        /// </summary>
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

            string[] clipGuids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolderPath });
            int total = clipGuids.Length;

            // SFX grouping: group by parent folder (preferred), or filename prefix (fallback)
            Dictionary<string, List<AudioClip>> sfxGroups = new();

            int createdMusic = 0;
            int createdAmbient = 0;
            int createdSfx = 0;

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

                string normalizedPath = clipPath.ToLower().Replace("\\", "/");
                string fileName = Path.GetFileNameWithoutExtension(clipPath);
                string fileLower = fileName.ToLower();

                bool isMusic = normalizedPath.Contains("/music/") ||
                               fileLower.Contains("music") ||
                               fileLower.Contains("bgm") ||
                               fileLower.Contains("theme") ||
                               clip.length >= MUSIC_MIN_LENGTH;

                bool isAmbient = !isMusic && (
                               normalizedPath.Contains("/ambient/") ||
                               normalizedPath.Contains("/ambience/") ||
                               fileLower.Contains("ambient") ||
                               (clip.length >= AMBIENT_MIN_LENGTH && clip.length < MUSIC_MIN_LENGTH));

                if (isMusic)
                {
                    string assetName = SanitizeAssetName(fileName);
                    string assetPath = $"{musicFolder}/{assetName}.asset";

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
                    string assetName = SanitizeAssetName(fileName);
                    string assetPath = $"{ambientFolder}/{assetName}.asset";

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

                // SFX grouping
                string parentFolder = Path.GetFileName(Path.GetDirectoryName(clipPath));
                parentFolder = string.IsNullOrEmpty(parentFolder) ? fileName : parentFolder;

                string lowerParent = parentFolder.ToLower();
                bool parentIsGeneric = lowerParent == "sfx" ||
                                       lowerParent == "sounds" ||
                                       lowerParent == "audio" ||
                                       lowerParent == "clips";

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

            // Create SoundClipData assets (one per group)
            int groupIndex = 0;
            foreach (var kv in sfxGroups)
            {
                groupIndex++;

                EditorUtility.DisplayProgressBar(
                    "Generating SFX Groups",
                    $"Creating {groupIndex}/{sfxGroups.Count}: {kv.Key}",
                    (float)groupIndex / Mathf.Max(1, sfxGroups.Count)
                );

                string assetPath = $"{sfxFolder}/{kv.Key}.asset";

                if (AssetDatabase.LoadAssetAtPath<SoundClipData>(assetPath) != null)
                {
                    continue;
                }

                var sd = ScriptableObject.CreateInstance<SoundClipData>();
                sd.soundName = kv.Key;
                sd.clips = kv.Value.ToArray();
                AssetDatabase.CreateAsset(sd, assetPath);
                createdSfx++;
            }

            EditorUtility.ClearProgressBar();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Generated assets — Music: {createdMusic}, Ambient: {createdAmbient}, SFX groups: {createdSfx}", this);
        }

        /// <summary>
        /// Assigns generated assets from GeneratedTracks/* into the libraries on this AudioManager GameObject.
        /// </summary>
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

            if (musicLib == null || ambientLib == null || sfxLib == null)
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

            var clean = new string(raw
                .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
                .ToArray());

            return clean.Trim().Replace(' ', '_').ToLower();
        }

        #endregion
    }
}

#endif