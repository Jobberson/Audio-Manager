
using UnityEngine;
using Snog.Audio;
using Snog.Audio.Clips;

public enum AudioType
{
    SFX,
    Ambient,
    Music
}

public enum AudioAction
{
    // SFX
    Play2D,
    Play3D,

    // Music
    Play,
    PlayFadeIn,
    StopMusic,
    StopMusicFadeOut,

    // Ambient
    PlayAmbient,
    PlayAmbientFadeIn,
    StopAmbient,
    StopAmbientFadeOut,

    // Crossfades
    CrossfadeAmbient,
    // (Optional) CrossfadeMusic — implement when dual-source structure exists

    // Mixer snapshots (optional convenience)
    TransitionSnapshotDefault,
    TransitionSnapshotCombat,
    TransitionSnapshotStealth,
    TransitionSnapshotUnderwater
}

public class AudioTrigger : MonoBehaviour
{
    [Header("General")]
    public string TagToCompare = "Player";
    [Tooltip("When should the action fire?")]
    public bool fireOnEnter = true;
    public bool fireOnExit = false;

    [Header("Selection")]
    public AudioType selectedAudioType = AudioType.SFX;
    public AudioAction action = AudioAction.Play2D;

    [Header("Typed References")]
    [Tooltip("For SFX actions")]
    public SoundClipData sfxClip;

    [Tooltip("For Music actions")]
    public MusicTrack musicTrack;

    [Tooltip("For Ambient actions and crossfades")]
    public AmbientTrack ambientTrack;

    [Header("Playback Params")]
    [SerializeField][Tooltip("Delay before playing")]
    private float playDelay = 0f;

    [SerializeField][Tooltip("Fade duration for fade-in/out/crossfade")]
    private float fadeDuration = 2f;

    [Header("3D SFX")]
    [Tooltip("If using Play3D, this overrides the sound position; leave zero to use trigger position")]
    public Vector3 override3DPosition;

    private void OnTriggerEnter(Collider other)
    {
        if (!fireOnEnter) return;
        if (!other.CompareTag(TagToCompare)) return;

        ExecuteAudioAction();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!fireOnExit) return;
        if (!other.CompareTag(TagToCompare)) return;

        ExecuteAudioAction();
    }

    private void ExecuteAudioAction()
    {
        var manager = AudioManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("[AudioTrigger] No AudioManager.Instance found.", this);
            return;
        }

        switch (selectedAudioType)
        {
            case AudioType.SFX:
                ExecuteSfx(manager);
                break;

            case AudioType.Music:
                ExecuteMusic(manager);
                break;

            case AudioType.Ambient:
                ExecuteAmbient(manager);
                break;
        }

        // Optional: snapshot utilities bound to action enum
        ExecuteSnapshotsIfRequested(manager);
    }

    private void ExecuteSfx(AudioManager manager)
    {
        switch (action)
        {
            case AudioAction.Play2D:
            {
                if (sfxClip != null && sfxClip.clips != null && sfxClip.clips.Length > 0)
                {
                    var clipToPlay = sfxClip.clips[Random.Range(0, sfxClip.clips.Length)];
                    manager.GetSoundLibrary()?.Editor_RebuildDictionary(); // safe in editor; no-op in player
                    manager.PlaySound2D(sfxClip.soundName);
                }
                break;
            }

            case AudioAction.Play3D:
            {
                Vector3 pos = override3DPosition != Vector3.zero ? override3DPosition : transform.position;
                if (sfxClip != null && sfxClip.clips != null && sfxClip.clips.Length > 0)
                {
                    manager.PlaySound3D(sfxClip.soundName, pos);
                }
                break;
            }

            default:
                // Non-SFX actions are ignored in SFX mode
                break;
        }
    }

    private void ExecuteMusic(AudioManager manager)
    {
        switch (action)
        {
            case AudioAction.Play:
            {
                if (musicTrack != null && musicTrack.clip != null)
                {
                    manager.PlayMusic(musicTrack.trackName, playDelay);
                }
                break;
            }

            case AudioAction.PlayFadeIn:
            {
                if (musicTrack != null && musicTrack.clip != null)
                {
                    manager.StartCoroutine(manager.PlayMusicFade(musicTrack.trackName, fadeDuration));
                }
                break;
            }

            case AudioAction.StopMusic:
            {
                manager.StopMusic();
                break;
            }

            case AudioAction.StopMusicFadeOut:
            {
                manager.StartCoroutine(manager.StopMusicFade(fadeDuration));
                break;
            }

            default:
                // Other actions ignored in Music mode
                break;
        }
    }

    private void ExecuteAmbient(AudioManager manager)
    {
        switch (action)
        {
            case AudioAction.PlayAmbient:
            {
                if (ambientTrack != null && ambientTrack.clip != null)
                {
                    manager.PlayAmbient(ambientTrack.trackName, playDelay);
                }
                break;
            }

            case AudioAction.PlayAmbientFadeIn:
            {
                if (ambientTrack != null && ambientTrack.clip != null)
                {
                    manager.StartCoroutine(manager.PlayAmbientFade(ambientTrack.trackName, fadeDuration));
                }
                break;
            }

            case AudioAction.StopAmbient:
            {
                manager.StopAmbient();
                break;
            }

            case AudioAction.StopAmbientFadeOut:
            {
                manager.StartCoroutine(manager.StopAmbientFade(fadeDuration));
                break;
            }

            case AudioAction.CrossfadeAmbient:
            {
                if (ambientTrack != null && ambientTrack.clip != null)
                {
                    manager.StartCoroutine(manager.CrossfadeAmbient(ambientTrack.trackName, fadeDuration));
                }
                break;
            }

            default:
                // Other actions ignored in Ambient mode
                break;
        }
    }

    private void ExecuteSnapshotsIfRequested(AudioManager manager)
    {
        switch (action)
        {
            case AudioAction.TransitionSnapshotDefault:
                manager.TransitionToSnapshot(AudioManager.SnapshotType.Default, 1f);
                break;

            case AudioAction.TransitionSnapshotCombat:
                manager.TransitionToSnapshot(AudioManager.SnapshotType.Combat, 1f);
                break;

            case AudioAction.TransitionSnapshotStealth:
                manager.TransitionToSnapshot(AudioManager.SnapshotType.Stealth, 1f);
                break;

            case AudioAction.TransitionSnapshotUnderwater:
                manager.TransitionToSnapshot(AudioManager.SnapshotType.Underwater, 1f);
                break;

            default:
                // Not a snapshot action
                break;
        }
    }
}
