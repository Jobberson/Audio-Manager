using UnityEngine;
using UnityEngine.UI;

namespace Snog.Audio.Demo
{
    public class AudioLibraryTabs : MonoBehaviour
    {
        [Header("Panels")]
        public GameObject musicPanel;
        public GameObject sfxPanel;
        public GameObject ambientPanel;
        public GameObject snapshotsPanel;

        [Header("Tab Buttons")]
        public Button musicButton;
        public Button sfxButton;
        public Button ambientButton;
        public Button snapshotsButton;

        [Header("Tab Visuals")]
        public Image musicTabImage;
        public Image sfxTabImage;
        public Image ambientTabImage;
        public Image snapshotsTabImage;

        public Color activeColor   = new Color(0.7f, 0.3f, 1f,  1f);
        public Color inactiveColor = new Color(0.3f, 0.2f, 0.5f, 0.6f);

        void Start()
        {
            musicButton.onClick.AddListener(ShowMusic);
            sfxButton.onClick.AddListener(ShowSFX);
            ambientButton.onClick.AddListener(ShowAmbient);

            if (snapshotsButton != null)
                snapshotsButton.onClick.AddListener(ShowSnapshots);

            ShowMusic(); // default tab
        }

        void ShowMusic()
        {
            SetPanels(music: true, sfx: false, ambient: false, snapshots: false);
            UpdateTabVisuals(musicTabImage);
        }

        void ShowSFX()
        {
            SetPanels(music: false, sfx: true, ambient: false, snapshots: false);
            UpdateTabVisuals(sfxTabImage);
        }

        void ShowAmbient()
        {
            SetPanels(music: false, sfx: false, ambient: true, snapshots: false);
            UpdateTabVisuals(ambientTabImage);
        }

        void ShowSnapshots()
        {
            SetPanels(music: false, sfx: false, ambient: false, snapshots: true);
            UpdateTabVisuals(snapshotsTabImage);
        }

        void SetPanels(bool music, bool sfx, bool ambient, bool snapshots)
        {
            if (musicPanel    != null) musicPanel.SetActive(music);
            if (sfxPanel      != null) sfxPanel.SetActive(sfx);
            if (ambientPanel  != null) ambientPanel.SetActive(ambient);
            if (snapshotsPanel != null) snapshotsPanel.SetActive(snapshots);
        }

        void UpdateTabVisuals(Image active)
        {
            if (musicTabImage     != null) musicTabImage.color     = inactiveColor;
            if (sfxTabImage       != null) sfxTabImage.color       = inactiveColor;
            if (ambientTabImage   != null) ambientTabImage.color   = inactiveColor;
            if (snapshotsTabImage != null) snapshotsTabImage.color = inactiveColor;

            if (active != null) active.color = activeColor;
        }
    }
}