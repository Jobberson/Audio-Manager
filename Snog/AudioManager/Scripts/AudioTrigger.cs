using System.Collections;
using UnityEngine;
using Snog.Audio;
using Snog.Audio.Clips;
using Snog.Audio.Layers;

[AddComponentMenu("Snog/AudioManager/Audio Trigger")]
[RequireComponent(typeof(Collider))]
public class AudioTrigger : MonoBehaviour
{
    public enum TriggerAudioType
    {
        SFX,
        Music,
        Ambient
    }

    public enum TriggerAudioAction
    {
        Play2D,
        Play3D,

        Play,
        PlayFadeIn,
        Stop,

        StopMusic,

        PopAmbient,

        SnapshotCombat,
        SnapshotStealth,
        SnapshotUnderwater
    }

    [Header("Trigger")]
    public string TagToCompare = "Player";
    public bool fireOnEnter = true;
    public bool fireOnExit = false;

    [Header("Audio")]
    public TriggerAudioType audioType = TriggerAudioType.SFX;
    public TriggerAudioAction action = TriggerAudioAction.Play2D;

    [Header("SFX")]
    public SoundClipData sfxClip;

    [Header("Music")]
    public MusicTrack musicTrack;

    [Header("Ambient")]
    public AmbientTrack ambientTrack;

    [Header("Timing")]
    public float playDelay = 0f;
    public float fadeDuration = 1f;

    [Header("3D")]
    public Vector3 override3DPosition = Vector3.zero;

    private int ambientToken = -1;
    private Coroutine routine;

    private void Reset()
    {
        Collider c = GetComponent<Collider>();
        if (c != null)
            c.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!fireOnEnter) return;
        if (!IsValidTarget(other)) return;
        Trigger(other.transform.position);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!fireOnExit) return;
        if (!IsValidTarget(other)) return;
        Trigger(other.transform.position);
    }

    private bool IsValidTarget(Collider other)
    {
        if (string.IsNullOrEmpty(TagToCompare)) return true;
        return other.CompareTag(TagToCompare);
    }

    private void Trigger(Vector3 otherPosition)
    {
        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(Execute(otherPosition));
    }

    private IEnumerator Execute(Vector3 otherPosition)
    {
        if (playDelay > 0f)
            yield return new WaitForSeconds(playDelay);

        AudioManager manager = AudioManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("[AudioTrigger] AudioManager not found.");
            yield break;
        }

        switch (audioType)
        {
            case TriggerAudioType.SFX:
                HandleSfx(manager, otherPosition);
                break;

            case TriggerAudioType.Music:
                HandleMusic(manager);
                break;

            case TriggerAudioType.Ambient:
                HandleAmbient(manager);
                break;
        }
    }

    private void HandleSfx(AudioManager manager, Vector3 otherPosition)
    {
        if (sfxClip == null || string.IsNullOrEmpty(sfxClip.soundName))
            return;

        switch (action)
        {
            case TriggerAudioAction.Play2D:
                manager.PlaySfx2D(sfxClip.soundName);
                break;

            case TriggerAudioAction.Play3D:
                Vector3 pos = override3DPosition == Vector3.zero
                    ? transform.position
                    : transform.TransformPoint(override3DPosition);

                manager.PlaySfx3D(sfxClip.soundName, pos);
                break;
        }
    }

    private void HandleMusic(AudioManager manager)
    {
        switch (action)
        {
            case TriggerAudioAction.Play:
                if (musicTrack != null)
                    manager.PlayMusic(musicTrack.trackName, 0f, 0f);
                break;

            case TriggerAudioAction.PlayFadeIn:
                if (musicTrack != null)
                    manager.PlayMusic(musicTrack.trackName, 0f, fadeDuration);
                break;

            case TriggerAudioAction.StopMusic:
                manager.StopMusic(fadeDuration);
                break;

            case TriggerAudioAction.SnapshotCombat:
                manager.TransitionToSnapshot(AudioManager.SnapshotType.Combat, fadeDuration);
                break;

            case TriggerAudioAction.SnapshotStealth:
                manager.TransitionToSnapshot(AudioManager.SnapshotType.Stealth, fadeDuration);
                break;

            case TriggerAudioAction.SnapshotUnderwater:
                manager.TransitionToSnapshot(AudioManager.SnapshotType.Underwater, fadeDuration);
                break;
        }
    }

    private void HandleAmbient(AudioManager manager)
    {
        if (ambientTrack == null)
            return;

        switch (action)
        {
            case TriggerAudioAction.Play:
            case TriggerAudioAction.PlayFadeIn:
            {
                AmbientProfile profile = ScriptableObject.CreateInstance<AmbientProfile>();
                profile.profileName = "AudioTrigger_Profile";

                AmbientLayer layer = new AmbientLayer
                {
                    track = ambientTrack,
                    volume = 1f
                };

                profile.layers = new AmbientLayer[] { layer };
                ambientToken = manager.PushAmbientProfile(profile, 0, fadeDuration);
                break;
            }

            case TriggerAudioAction.Stop:
            case TriggerAudioAction.PopAmbient:
            {
                if (ambientToken != -1)
                {
                    manager.PopAmbientToken(ambientToken, fadeDuration);
                    ambientToken = -1;
                }
                break;
            }
        }
    }

    private void OnDisable()
    {
        if (ambientToken != -1)
        {
            AudioManager manager = AudioManager.Instance;
            if (manager != null)
                manager.PopAmbientToken(ambientToken, fadeDuration);

            ambientToken = -1;
        }
    }
}
