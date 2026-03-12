using Snog.Audio;
using UnityEngine;

namespace Snog.Audio.Demo
{
    /// <summary>
    /// Thin bridge that drives both AudioManager and SpectrumAnalyzer together.
    ///
    /// Attach this to any GameObject in the scene alongside (or near) the
    /// SpectrumAnalyzer.  You do NOT need a separate AudioSource on this object —
    /// AudioManager handles all playback internally.
    ///
    /// The SpectrumAnalyzer reference is optional: if assigned, PlayTrack /
    /// StopTrack are routed through it so the visualizer stays in sync.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        [Header("Track")]
        [Tooltip("Name of the music track to play on Start (must exist in MusicLibrary).")]
        [SerializeField] private string startTrackName = "bach1";

        [Tooltip("Fade-in duration in seconds.")]
        [SerializeField] private float fadeIn = 1.5f;

        [Tooltip("Fade-out duration in seconds.")]
        [SerializeField] private float fadeOut = 1.5f;

        [Header("Visualizer (optional)")]
        [Tooltip("Assign the SpectrumAnalyzer in the scene to keep it in sync.")]
        [SerializeField] private SpectrumAnalyzer spectrumAnalyzer;

        // ─────────────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (!string.IsNullOrEmpty(startTrackName))
                PlayTrack(startTrackName);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Plays a named track through AudioManager and notifies the visualizer.
        /// Call this from UI buttons, game events, or AudioManagerDemoUI.
        /// </summary>
        public void PlayTrack(string trackName)
        {
            if (spectrumAnalyzer != null)
                spectrumAnalyzer.PlayTrack(trackName, fadeIn);
            else
                AudioManager.Instance.PlayMusic(trackName, startDelay: 0f, fadeDuration: fadeIn);
        }

        /// <summary>
        /// Stops the current track and lets the visualizer collapse.
        /// </summary>
        public void StopTrack()
        {
            if (spectrumAnalyzer != null)
                spectrumAnalyzer.StopTrack(fadeOut);
            else
                AudioManager.Instance.StopMusic(fadeOut);
        }
    }
}