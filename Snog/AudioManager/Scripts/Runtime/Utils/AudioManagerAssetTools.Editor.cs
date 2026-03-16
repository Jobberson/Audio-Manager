#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        [SerializeField] private List<AudioClip> scannedMusicClips   = new();
        [SerializeField] private List<AudioClip> scannedAmbientClips = new();
        [SerializeField] private List<AudioClip> scannedSFXClips     = new();

        private const float AMBIENT_MIN_LENGTH = 30f;
        private const float MUSIC_MIN_LENGTH   = 60f;

        #endregion

        #region Public Editor API

        public void SetAudioFolderPath()
        {
            string selectedPath = EditorUtility.OpenFolderPanel(
                "Select Audio Folder (must be inside Assets)",
                "Assets",
                "");

            if (string.IsNullOrEmpty(selectedPath)) return;

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
                    if (clip == null) continue;

                    EditorUtility.DisplayProgressBar(
                        "Scanning Audio",
                        $"Processing {i + 1}/{total}: {clip.name}",
                        (float)i / Mathf.Max(1, total));

                    string normalizedPath = path.Replace("\\", "/").ToLowerInvariant();
                    string fileName       = Path.GetFileNameWithoutExtension(path);
                    string fileLower      = fileName.ToLowerInvariant();

                    if (normalizedPath.Contains("/music/") || normalizedPath.Contains("/bgm/") ||
                        fileLower.Contains("music") || fileLower.Contains("bgm") || fileLower.Contains("theme"))
                    {
                        scannedMusicClips.Add(clip);
                        continue;
                    }

                    if (normalizedPath.Contains("/ambient/") || normalizedPath.Contains("/ambience/") ||
                        normalizedPath.Contains("/environment/") || fileLower.Contains("ambient") ||
                        fileLower.Contains("ambience") || fileLower.Contains("amb_") || fileLower.Contains("amb-"))
                    {
                        scannedAmbientClips.Add(clip);
                        continue;
                    }

                    if (normalizedPath.Contains("/sfx/") || normalizedPath.Contains("/fx/") ||
                        normalizedPath.Contains("/soundeffects/") || fileLower.Contains("sfx") || fileLower.Contains("fx"))
                    {
                        scannedSFXClips.Add(clip);
                        continue;
                    }

                    if (clip.length >= MUSIC_MIN_LENGTH)       scannedMusicClips.Add(clip);
                    else if (clip.length >= AMBIENT_MIN_LENGTH) scannedAmbientClips.Add(clip);
                    else                                        scannedSFXClips.Add(clip);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log(
                $"Scan complete: {scannedMusicClips.Count} music, {scannedAmbientClips.Count} ambient, {scannedSFXClips.Count} sfx.",
                this);

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
                AssetDatabase.CreateFolder(audioFolderPath, "GeneratedTracks");

            string musicFolder   = EnsureSubfolder(generatedFolder, "Music");
            string ambientFolder = EnsureSubfolder(generatedFolder, "Ambient");
            string sfxFolder     = EnsureSubfolder(generatedFolder, "SFX");

            var existingMusic   = LoadExistingAssets<MusicTrack>(musicFolder);
            var existingAmbient = LoadExistingAssets<AmbientTrack>(ambientFolder);
            var existingSfx     = LoadExistingAssets<SoundClipData>(sfxFolder);

            string[] clipGuids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolderPath });
            int total = clipGuids.Length;

            Dictionary<string, List<AudioClip>> sfxGroups = new();
            int createdMusic = 0, createdAmbient = 0, createdSfx = 0;
            int skippedMusic = 0, skippedAmbient = 0, skippedSfx = 0;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string clipPath = AssetDatabase.GUIDToAssetPath(clipGuids[i]);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                    if (clip == null) continue;

                    EditorUtility.DisplayProgressBar(
                        "Generating Assets",
                        $"Analyzing {i + 1}/{total}: {clip.name}",
                        (float)i / Mathf.Max(1, total));

                    string normalizedPath = clipPath.Replace("\\", "/").ToLowerInvariant();
                    string fileName       = Path.GetFileNameWithoutExtension(clipPath);
                    string fileLower      = fileName.ToLowerInvariant();

                    bool pathSaysMusic =
                        normalizedPath.Contains("/music/") || normalizedPath.Contains("/bgm/") ||
                        fileLower.Contains("music") || fileLower.Contains("bgm") || fileLower.Contains("theme");

                    bool pathSaysAmbient =
                        normalizedPath.Contains("/ambient/") || normalizedPath.Contains("/ambience/") ||
                        normalizedPath.Contains("/environment/") || fileLower.Contains("ambient") ||
                        fileLower.Contains("ambience") || fileLower.Contains("amb_") || fileLower.Contains("amb-");

                    bool pathSaysSFX =
                        normalizedPath.Contains("/sfx/") || normalizedPath.Contains("/fx/") ||
                        normalizedPath.Contains("/soundeffects/") || fileLower.Contains("sfx") || fileLower.Contains("fx_");

                    bool hasExplicitHint = pathSaysMusic || pathSaysAmbient || pathSaysSFX;

                    bool isMusic   = pathSaysMusic   || (!hasExplicitHint && clip.length >= MUSIC_MIN_LENGTH);
                    bool isAmbient = !isMusic && (pathSaysAmbient || (!hasExplicitHint && clip.length >= AMBIENT_MIN_LENGTH && clip.length < MUSIC_MIN_LENGTH));

                    if (isMusic)
                    {
                        string assetName = SanitizeAssetName(fileName);
                        if (AssetExistsWithSameClip(existingMusic, assetName, clip)) { skippedMusic++; continue; }

                        string assetPath = GetUniqueAssetPath(musicFolder, assetName);
                        var mt = ScriptableObject.CreateInstance<MusicTrack>();
                        mt.trackName   = fileName;
                        mt.clip        = clip;
                        mt.description = $"Generated from {clipPath}";
                        AssetDatabase.CreateAsset(mt, assetPath);
                        createdMusic++;
                        continue;
                    }

                    if (isAmbient)
                    {
                        string assetName = SanitizeAssetName(fileName);
                        if (AssetExistsWithSameClip(existingAmbient, assetName, clip)) { skippedAmbient++; continue; }

                        string assetPath = GetUniqueAssetPath(ambientFolder, assetName);
                        var at = ScriptableObject.CreateInstance<AmbientTrack>();
                        at.trackName   = fileName;
                        at.clip        = clip;
                        at.description = $"Generated from {clipPath}";
                        AssetDatabase.CreateAsset(at, assetPath);
                        createdAmbient++;
                        continue;
                    }

                    string parentFolder = Path.GetFileName(Path.GetDirectoryName(clipPath));
                    parentFolder = string.IsNullOrEmpty(parentFolder) ? fileName : parentFolder;
                    string lowerParent = parentFolder.ToLowerInvariant();

                    bool parentIsGeneric = lowerParent == "sfx" || lowerParent == "sounds" ||
                                           lowerParent == "audio" || lowerParent == "clips";

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
                        (float)groupIndex / Mathf.Max(1, sfxGroups.Count));

                    if (SfxGroupExistsWithSameClips(existingSfx, kv.Key, kv.Value)) { skippedSfx++; continue; }

                    string assetPath = GetUniqueAssetPath(sfxFolder, kv.Key);
                    var sd = ScriptableObject.CreateInstance<SoundClipData>();
                    sd.soundName = kv.Key;
                    sd.clips     = kv.Value.ToArray();
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
                this);
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

            var musicLib   = GetComponent<MusicLibrary>();
            var ambientLib = GetComponent<AmbientLibrary>();
            var sfxLib     = GetComponent<SoundLibrary>();

            if (musicLib == null || ambientLib == null || sfxLib == null)
            {
                Debug.LogWarning("Missing one or more library components on this GameObject.", this);
                return;
            }

            string musicFolder   = generatedFolder + "/Music";
            string ambientFolder = generatedFolder + "/Ambient";
            string sfxFolder     = generatedFolder + "/SFX";

            int addedMusic = 0, addedAmbient = 0, addedSfx = 0;

            if (AssetDatabase.IsValidFolder(musicFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:MusicTrack", new[] { musicFolder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    var mt = AssetDatabase.LoadAssetAtPath<MusicTrack>(p);
                    if (mt != null && !musicLib.tracks.Contains(mt)) { musicLib.tracks.Add(mt); addedMusic++; }
                }
            }

            if (AssetDatabase.IsValidFolder(ambientFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:AmbientTrack", new[] { ambientFolder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    var at = AssetDatabase.LoadAssetAtPath<AmbientTrack>(p);
                    if (at != null && !ambientLib.tracks.Contains(at)) { ambientLib.tracks.Add(at); addedAmbient++; }
                }
            }

            if (AssetDatabase.IsValidFolder(sfxFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:SoundClipData", new[] { sfxFolder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    var sd = AssetDatabase.LoadAssetAtPath<SoundClipData>(p);
                    if (sd != null && !sfxLib.tracks.Contains(sd)) { sfxLib.tracks.Add(sd); addedSfx++; }
                }
            }

            EditorUtility.SetDirty(musicLib);
            EditorUtility.SetDirty(ambientLib);
            EditorUtility.SetDirty(sfxLib);
            AssetDatabase.SaveAssets();

            Debug.Log($"Assigned to libraries — Music: {addedMusic}, Ambient: {addedAmbient}, SFX: {addedSfx}", this);

            // Fix 2: Generate the compile-time constants class after libraries are populated.
            GenerateNamesClass();
        }

        // ─── Fix 2: Compile-Time Constants Generator ─────────────────────────
        /// <summary>
        /// Generates a static C# constants class (AudioNames.cs) inside the Scripts folder
        /// of this package, wherever the user has moved it.
        /// Developers can then use <c>AudioNames.Sound.Footstep</c> instead of raw strings,
        /// giving them autocomplete, rename support, and compile-time safety.
        /// </summary>
        public void GenerateNamesClass()
        {
            var musicLib   = GetComponent<MusicLibrary>();
            var ambientLib = GetComponent<AmbientLibrary>();
            var sfxLib     = GetComponent<SoundLibrary>();

            string[] musicNames   = musicLib   != null ? musicLib.GetAllClipNames()   : Array.Empty<string>();
            string[] ambientNames = ambientLib != null ? ambientLib.GetAllClipNames() : Array.Empty<string>();
            string[] soundNames   = sfxLib     != null ? sfxLib.GetAllClipNames()     : Array.Empty<string>();

            var sb = new StringBuilder();
            sb.AppendLine("// ─────────────────────────────────────────────────────────────────────");
            sb.AppendLine("// AUTO-GENERATED — do not edit manually.");
            sb.AppendLine("// Re-generate via AudioManager Inspector > Utilities > Scan → Generate → Assign");
            sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("// ─────────────────────────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("namespace Snog.Audio.Generated");
            sb.AppendLine("{");

            // Sound constants
            sb.AppendLine("    /// <summary>Compile-time keys for SFX — use instead of raw strings.</summary>");
            sb.AppendLine("    public static class SoundNames");
            sb.AppendLine("    {");
            foreach (string name in soundNames)
                sb.AppendLine($"        public const string {ToConstantName(name)} = \"{name}\";");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Music constants
            sb.AppendLine("    /// <summary>Compile-time keys for music tracks — use instead of raw strings.</summary>");
            sb.AppendLine("    public static class MusicNames");
            sb.AppendLine("    {");
            foreach (string name in musicNames)
                sb.AppendLine($"        public const string {ToConstantName(name)} = \"{name}\";");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Ambient constants
            sb.AppendLine("    /// <summary>Compile-time keys for ambient tracks — use instead of raw strings.</summary>");
            sb.AppendLine("    public static class AmbientNames");
            sb.AppendLine("    {");
            foreach (string name in ambientNames)
                sb.AppendLine($"        public const string {ToConstantName(name)} = \"{name}\";");
            sb.AppendLine("    }");

            sb.AppendLine("}");

            // Locate the Scripts folder relative to this script file — works regardless of
            // where the user has moved the AudioManager package inside their project.
            string csPath = FindScriptsFolderPath();
            if (string.IsNullOrEmpty(csPath))
            {
                Debug.LogError(
                    "[AudioManager] Could not locate the Scripts folder. " +
                    "Make sure AudioManagerAssetTools_Editor.cs is inside a folder named 'Scripts'.",
                    this);
                return;
            }

            csPath = csPath.TrimEnd('/') + "/Runtime/AudioNames.cs";
            string fullOsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", csPath));

            File.WriteAllText(fullOsPath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(csPath, ImportAssetOptions.ForceUpdate);

            Debug.Log(
                $"[AudioManager] AudioNames.cs written to {csPath} " +
                $"({soundNames.Length} sounds, {musicNames.Length} music, {ambientNames.Length} ambient).",
                this);
        }

        /// <summary>
        /// Returns the Unity project-relative path (e.g. "Assets/Snog/AudioManager/Scripts")
        /// of the Scripts folder that contains this script file, no matter where the user
        /// has placed or renamed the parent folders.
        ///
        /// Strategy: locate this source file via MonoScript, then walk up the directory
        /// hierarchy until a folder named "Scripts" is found as a path component.
        /// </summary>
        private string FindScriptsFolderPath()
        {
            // MonoScript gives us the asset path of this exact .cs file.
            var mono = MonoScript.FromMonoBehaviour(this);
            if (mono == null)
            {
                Debug.LogWarning("[AudioManager] MonoScript.FromMonoBehaviour returned null.", this);
                return null;
            }

            string scriptAssetPath = AssetDatabase.GetAssetPath(mono)
                .Replace("\\", "/");

            // Walk up the directory chain looking for a segment named "Scripts".
            string dir = Path.GetDirectoryName(scriptAssetPath)?.Replace("\\", "/");

            while (!string.IsNullOrEmpty(dir) && dir != "." && dir != "Assets")
            {
                string folderName = Path.GetFileName(dir);

                if (string.Equals(folderName, "Scripts", StringComparison.OrdinalIgnoreCase))
                    return dir; // e.g. "Assets/Snog/AudioManager/Scripts"

                dir = Path.GetDirectoryName(dir)?.Replace("\\", "/");
            }

            // Fallback: the script isn't inside a "Scripts" folder — use its own directory.
            Debug.LogWarning(
                "[AudioManager] No 'Scripts' folder found in the path hierarchy. " +
                $"Falling back to the script's own directory: " +
                $"{Path.GetDirectoryName(scriptAssetPath)}",
                this);

            return Path.GetDirectoryName(scriptAssetPath)?.Replace("\\", "/");
        }

        #endregion

        #region Helpers

        private void EnsureScannedLists()
        {
            scannedMusicClips   ??= new List<AudioClip>();
            scannedAmbientClips ??= new List<AudioClip>();
            scannedSFXClips     ??= new List<AudioClip>();
        }

        private string EnsureSubfolder(string parentFolder, string subfolderName)
        {
            parentFolder = parentFolder.TrimEnd('/');
            string candidate = parentFolder + "/" + subfolderName;

            if (!AssetDatabase.IsValidFolder(candidate))
                AssetDatabase.CreateFolder(parentFolder, subfolderName);

            return candidate;
        }

        private string SanitizeAssetName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "unnamed";

            string clean = new string(
                raw.Trim()
                   .Select(c => char.ToLowerInvariant(c))
                   .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
                   .ToArray());

            return string.IsNullOrEmpty(clean) ? "unnamed" : clean;
        }

        private string GetUniqueAssetPath(string folder, string baseName)
        {
            folder   = folder.TrimEnd('/');
            baseName = SanitizeAssetName(baseName);

            string path = $"{folder}/{baseName}.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null) return path;

            int suffix = 1;
            while (true)
            {
                string candidate = $"{folder}/{baseName}_{suffix:00}.asset";
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(candidate) == null)
                    return candidate;
                suffix++;
            }
        }

        private Dictionary<string, T> LoadExistingAssets<T>(string folder) where T : UnityEngine.Object
        {
            var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            if (!AssetDatabase.IsValidFolder(folder)) return result;

            string typeFilter = typeof(T).Name;
            string[] guids    = AssetDatabase.FindAssets($"t:{typeFilter}", new[] { folder });

            foreach (string guid in guids)
            {
                string path  = AssetDatabase.GUIDToAssetPath(guid);
                var asset    = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null) continue;

                string fileName  = Path.GetFileNameWithoutExtension(path);
                string sanitized = SanitizeAssetName(fileName);
                result[sanitized] = asset;
            }

            return result;
        }

        private bool AssetExistsWithSameClip<T>(Dictionary<string, T> existing, string assetName, AudioClip clip) where T : UnityEngine.Object
        {
            if (!existing.TryGetValue(assetName, out var existingAsset)) return false;

            if (existingAsset is MusicTrack  mt) return mt.clip == clip;
            if (existingAsset is AmbientTrack at) return at.clip == clip;

            return false;
        }

        private bool SfxGroupExistsWithSameClips(Dictionary<string, SoundClipData> existing, string groupKey, List<AudioClip> clips)
        {
            if (!existing.TryGetValue(groupKey, out var existingData)) return false;
            if (existingData.clips == null || existingData.clips.Length != clips.Count) return false;

            var existingSet = new HashSet<AudioClip>(existingData.clips);
            var newSet      = new HashSet<AudioClip>(clips);
            return existingSet.SetEquals(newSet);
        }

        // ─── Fix 2: Helpers for constant name generation ─────────────────────
        /// <summary>
        /// Converts an arbitrary audio name (e.g. "boss_theme-01") into a valid PascalCase
        /// C# identifier (e.g. "BossTheme01") for use as a constant name.
        /// </summary>
        private static string ToConstantName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Unknown";

            // Split on any non-alphanumeric character and capitalise each word.
            var parts = raw.Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var sb    = new StringBuilder();

            foreach (string part in parts)
            {
                if (part.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(part[0]));
                sb.Append(part.Substring(1));
            }

            string result = sb.ToString();

            // If the identifier starts with a digit, prefix with an underscore.
            if (result.Length > 0 && char.IsDigit(result[0]))
                result = "_" + result;

            return string.IsNullOrEmpty(result) ? "Unknown" : result;
        }

        #endregion
    }
}
#endif