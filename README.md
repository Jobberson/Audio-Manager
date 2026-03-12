<div align="center">

<h1>♪ Snog's Audio Manager for Unity ♪</h1>

**A lightweight, production-oriented audio framework.**  
Centralized control · Pooled 3D SFX · Ambient zones · One-click import pipeline

---

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?style=flat-square&logo=unity&logoColor=white)](https://unity.com)
[![License](https://img.shields.io/badge/License-Asset%20Store%20EULA-informational?style=flat-square)](https://unity.com/legal/as-terms)
[![Asset Store](https://img.shields.io/badge/Unity%20Asset%20Store-Buy-success?style=flat-square&logo=unity)](https://assetstore.unity.com)
[![Support](https://img.shields.io/badge/Support-Email-orange?style=flat-square&logo=gmail&logoColor=white)](mailto:snogdev@gmail.com)

</div>

---

## Why Snog' Audio Manager?

Most Unity projects end up with the same audio spaghetti — `AudioSource` components scattered everywhere, magic strings throughout the codebase, and ambient sound that requires a programmer every time a level designer wants to change a zone.

Snog replaces all of that with a single `AudioManager` singleton and a set of inspector-driven components that non-programmers can own.

The highlight is the **one-click import pipeline**: point it at your audio folder, hit ```Scan → Generate → Assign```, and it classifies every clip, creates all your ScriptableObject assets, populates the libraries, and emits an `AudioNames.cs` constants file so you never type a sound name as a raw string again.

---

## Contents

- [Features](#features)
- [Quick Start](#quick-start)
- [AudioNames.cs — Compile-Time Safety](#audionamescs--compile-time-safety)
- [Ambient System](#ambient-system-deep-dive)
- [Events & Callbacks](#events--callbacks)
- [Requirements](#requirements)
- [Project Structure](#project-structure)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)

---

## Features

<details open>
<summary><strong>🎮 Core</strong></summary>
<br>

- Central `AudioManager` singleton — `DontDestroyOnLoad`, zero setup code required
- Mixer-routed audio with `MasterVolume`, `MusicVolume`, `AmbientVolume`, `FXVolume`
- Named mixer snapshot transitions via `TransitionToSnapshot`
- Thread-safe singleton base class with `#if UNITY_EDITOR || DEVELOPMENT_BUILD` log guards — no log spam in shipping builds

</details>

<details open>
<summary><strong>🔫 SFX</strong></summary>
<br>

- 2D oneshot SFX via `PlaySfx2D`
- 3D spatial SFX via a pooled `AudioSourcePool` (`PlaySfx3D`)
- Multi-variant `SoundClipData` — store multiple clip takes, randomised on every play
- Rate-limited pool exhaustion warning — dropped sounds never go silently missing

</details>

<details open>
<summary><strong>🎵 Music</strong></summary>
<br>

- `PlayMusic` with optional start delay and fade-in
- `StopMusic` with fade-out
- `CrossFadeMusic` — simultaneous fade-out + fade-in, **no silent gap between tracks**

</details>

<details open>
<summary><strong>🌿 Ambient System</strong></summary>
<br>

- `AmbientEmitter` — world-space source, self-registers with the manager
- `AmbientProfile` — ScriptableObject soundscape definition (layers of tracks + volumes)
- Priority-weighted voice budgeting with distance scoring (`maxAmbientVoices`)
- Profile stack: `PushAmbientProfile` / `PopAmbientToken` for layered zone transitions
- `AmbientZone` — zero-code trigger volumes, `Replace` and `Stack` modes
- Per-layer playback overrides (`randomStartTime`, `pitchRange`) opt-in per profile

</details>

<details open>
<summary><strong>🛠️ Editor Tooling</strong></summary>
<br>

- **Scan → Generate → Assign** one-click import pipeline
- Auto-classifies clips by folder name, filename hints, and duration thresholds
- Groups SFX variants by parent folder or filename prefix into `SoundClipData` assets
- Populates `SoundLibrary`, `MusicLibrary`, `AmbientLibrary` in one step
- Emits `AudioNames.cs` — compile-time constants for every sound, music, and ambient name
- Runtime inspector: playback testing, mixer dB meters, ambient stack browser, emitter list
- Editor clip preview via `UnityEditor.AudioUtil`

</details>

---

## Quick Start

### 1. Create the AudioManager

```
Assets/
└── Audio/
    ├── Music/
    ├── Ambient/
    └── SFX/
```

1. Create a GameObject called `Audio` in your persistent scene.
2. Add `AudioManager` — it will auto-require `SoundLibrary`, `MusicLibrary`, and `AmbientLibrary`.
3. Create an `AudioMixer` with four **exposed parameters**: `MasterVolume`, `MusicVolume`, `AmbientVolume`, `FXVolume`. Create `Music`, `Ambient`, and `FX` groups. Assign everything to AudioManager in the Inspector.

### 2. Import your audio

In the **AudioManager Inspector → Utilities**:

| Step | What it does |
|---|---|
| **Set Root Audio Folder** | Points the scanner at `Assets/Audio` |
| **Scan** | Finds and classifies all `AudioClip` assets |
| **Generate** | Creates `MusicTrack`, `AmbientTrack`, `SoundClipData` ScriptableObjects |
| **Assign** | Populates all libraries and writes `AudioNames.cs` |

### 3. Play sounds

```csharp
using Snog.Audio;
using Snog.Audio.Generated; // ← generated constants

// ── SFX ──────────────────────────────────────────────────────────────
AudioManager.Instance.PlaySfx2D(SoundNames.ButtonClick);
AudioManager.Instance.PlaySfx3D(SoundNames.Explosion, transform.position);

// ── Music ─────────────────────────────────────────────────────────────
AudioManager.Instance.PlayMusic(MusicNames.MainTheme, fadeIn: 1f);
AudioManager.Instance.CrossFadeMusic(MusicNames.CombatLoop, crossFadeDuration: 1.5f);
AudioManager.Instance.StopMusic(fadeOut: 1f);

// ── Volume & Snapshots ────────────────────────────────────────────────
AudioManager.Instance.SetVolume(0.8f, AudioManager.AudioChannel.Master);
AudioManager.Instance.TransitionToSnapshot("Combat", transitionTime: 0.5f);
```

### 4. Set up ambient zones

1. Add `AmbientEmitter` to each ambient sound source in your scene. Assign an `AmbientTrack`.
2. Create an `AmbientProfile` asset. Add layers referencing those tracks and set target volumes.
3. Drop an `AmbientZone` onto a trigger collider in your level. Assign the profile.

No code. The manager handles scoring, voice budgeting, and fading automatically.

---

## AudioNames.cs — Compile-Time Safety

After running the import pipeline, `AudioNames.cs` is written to your audio root:

```csharp
// AUTO-GENERATED — re-run Assign to update. Do not edit manually.
namespace Snog.Audio.Generated
{
    public static class SoundNames
    {
        public const string Footstep = "Footstep";
        public const string Explosion = "Explosion";
        public const string ButtonClick = "ButtonClick";
    }

    public static class MusicNames
    {
        public const string MainTheme = "MainTheme";
        public const string CombatLoop = "CombatLoop";
    }

    public static class AmbientNames
    {
        public const string ForestWind = "ForestWind";
        public const string CaveDrips = "CaveDrips";
    }
}
```

> ✅ Use these constants everywhere instead of raw strings.  
> Rename a sound → re-run Assign → the compiler flags every broken call site immediately.

You can also regenerate the file independently at any time via **AudioManager Inspector → Utilities → Generate Names Class**.

---

## Ambient System Deep Dive

<details open>
<summary><strong>The four components</strong></summary>
<br>

| Component | Role |
|---|---|
| `AmbientTrack` | ScriptableObject — the clip data asset |
| `AmbientEmitter` | MonoBehaviour — world-space source, self-registers globally with the manager |
| `AmbientProfile` | ScriptableObject — soundscape definition: layers of (track + volume + priority) |
| `AudioManager` | Conductor: scores emitters, applies voice budget, fades volumes |

</details>

<details open>
<summary><strong>How scoring works</strong></summary>
<br>

Every `ambientRescoreInterval` seconds the manager:

1. Builds a `desiredVolume` per track from all profiles currently on the stack
2. Scores each registered `AmbientEmitter`:

```
score = (stackPriority × 1000) + (emitterPriority × 100) + (volume × 10) + (1 / distance)
```

3. Selects the top `maxAmbientVoices` emitters
4. Fades allowed emitters toward their target volumes; all others fade out

Because emitters register globally (not per-zone), one riverside emitter can be heard across multiple overlapping zones without any extra setup.

</details>

<details open>
<summary><strong>Profile stack</strong></summary>
<br>

```csharp
// Enter a cave — layers over the existing forest ambient
int token = AudioManager.Instance.PushAmbientProfile(caveProfile, priority: 1, fade: 2f);

// Leave the cave — forest ambient returns
AudioManager.Instance.PopAmbientToken(token, fade: 2f);
```

Profiles stack — pushing a cave profile over a forest profile blends both until the voice budget forces a cutoff. Higher-priority layers survive. `AmbientZone` in Stack mode handles push/pop automatically.

</details>

<details open>
<summary><strong>AmbientZone exit actions</strong></summary>
<br>

| ExitAction | Replace mode | Stack mode |
|---|---|---|
| `None` | Profile stays active | Token stays on stack — **you must pop manually.** Editor shows a warning. |
| `AutoPop` *(recommended)* | — | Pops token with exit fade. Best default for most zones. |
| `StopFade` | `ClearAmbient(exitFade)` | Pops token with exit fade |
| `StopImmediate` | `ClearAmbient(0)` | Pops token instantly |

</details>

---

## Events & Callbacks

All hooks are `UnityEvent` — wire them in the Inspector, or subscribe in code.

```csharp
AudioManager.Instance.onMusicFinished.AddListener(trackName =>
{
    Debug.Log($"Track finished: {trackName}");
    ShowNextLevelUI();
});
```

| Event | Signature | When it fires |
|---|---|---|
| `onMusicStarted` | `UnityEvent<string>` | A track begins playing |
| `onMusicStopped` | `UnityEvent<string>` | Music is manually stopped |
| `onMusicFinished` | `UnityEvent<string>` | A non-looping track reaches its natural end |
| `onSfxPlayed` | `UnityEvent<string>` | A 2D or 3D SFX is successfully played |
| `onAmbientProfilePushed` | `UnityEvent<AmbientProfile>` | A profile is pushed onto the stack |
| `onAmbientProfilePopped` | `UnityEvent<AmbientProfile>` | A profile is popped from the stack |

`AmbientZone` also exposes `onZoneEntered` and `onZoneExited` for driving UI, animations, or other non-audio systems from the same trigger volume.

---

## Requirements

| Requirement | Notes |
|---|---|
| **Unity 2022.3 LTS+** | Tested on 2022.3, 2023.1, 6000.0 |
| **AudioMixer** | Required — manager routes to groups and writes to exposed parameters |
| `FindAnyObjectByType<T>()` | Available from Unity 2023.1+. For 2022.3, replace the handful of call sites with `FindObjectOfType<T>()` |

No third-party dependencies. No Package Manager entries. Import and go.

---

## Project Structure

```
Snog/
├── AudioManager/
│ ├── Scripts/
│ │ ├── Runtime/
│ │ │ ├── AudioManager.cs ← singleton hub (partial class)
│ │ │ ├── AudioSourcePool.cs ← pooled 3D AudioSources
│ │ │ ├── AudioTrigger.cs ← zero-code gameplay trigger
│ │ │ ├── Clips/
│ │ │ │ ├── SoundClipData.cs ← multi-variant SFX asset
│ │ │ │ ├── MusicTrack.cs ← music track asset
│ │ │ │ └── AmbientTrack.cs ← ambient track asset
│ │ │ ├── Libraries/
│ │ │ │ ├── SoundLibrary.cs
│ │ │ │ ├── MusicLibrary.cs
│ │ │ │ └── AmbientLibrary.cs
│ │ │ └── Utils/
│ │ │ ├── AmbientEmitter.cs ← world-space ambient source
│ │ │ ├── AmbientProfile.cs ← includes AmbientLayer
│ │ │ ├── AmbientZone.cs ← trigger volume
│ │ │ └── AudioManagerAssetTools.Editor.cs ← import pipeline + AudioNames generator
│ │ └── Editor/
│ │ ├── AudioManagerEditor.cs ← runtime tools, meters, emitter browser
│ │ └── AudioTriggerEditor.cs ← context-sensitive trigger inspector
│ └── Demo/
│ ├── Scripts/
│ └── Documentation/
└── Shared/
    └── Singleton.cs ← thread-safe MonoBehaviour singleton base
```

---

## Troubleshooting

<details>
<summary><strong>Volume sliders do nothing</strong></summary>
<br>

Ensure your `AudioMixer` has exposed float parameters named **exactly**: `MasterVolume`, `MusicVolume`, `AmbientVolume`, `FXVolume`. Check that `mainMixer` is assigned in the AudioManager inspector.

</details>

<details>
<summary><strong>"No clips found" / empty dropdowns</strong></summary>
<br>

Run the pipeline: **Inspector → Utilities → Scan → Generate → Assign**. Or right-click each library component → "Rebuild Dictionary".

</details>

<details>
<summary><strong>Sounds dropping in busy scenes</strong></summary>
<br>

Check the console for `[AudioSourcePool] Pool exhausted` warnings — they're rate-limited so they won't flood the log. Increase `fxPoolSize` (base pool) or `maxExtraSources` (overflow budget) on AudioManager until warnings stop. You can also call `fxPool.GetPoolStats(out active, out available, out total)` to instrument this in a debug UI.

</details>

<details>
<summary><strong>Ambient not playing</strong></summary>
<br>

Check in order:
- Are there `AmbientEmitter` components in the scene? The inspector shows the count.
- Does each emitter have an `AmbientTrack` with a clip assigned?
- Is the ambient stack empty? The inspector shows the current stack depth.
- Is the `AmbientZone` collider set to **Is Trigger**? Does the entering object's tag match `Tag To Compare`?

</details>

<details>
<summary><strong>Ambient token leak — zone ambience never stops</strong></summary>
<br>

In Stack mode, `Exit Action = None` **intentionally** leaves the token on the stack for manual management. Change it to `AutoPop`. The Inspector shows a warning whenever `None` is selected in Stack mode.

</details>

<details>
<summary><strong>Editor clip preview not working</strong></summary>
<br>

Preview uses reflection into Unity's internal `UnityEditor.AudioUtil`. This can break when Unity updates internal APIs — the `previewSupported` flag prevents crashes. Runtime playback is always unaffected.

</details>

<details>
<summary><strong>Snapshots not transitioning</strong></summary>
<br>

The snapshot must exist in the `AudioMixer` **and** be added to AudioManager → Snapshots list with a unique name. The string passed to `TransitionToSnapshot()` must match the name field exactly (case-insensitive, whitespace is trimmed automatically).

</details>

---

## FAQ

<details>
<summary><strong>Does this require an AudioMixer?</strong></summary>
<br>

Yes. `AudioManager` routes all sources to `AudioMixerGroup` references and writes dB values to exposed mixer parameters for volume control. An `AudioMixer` is also required for snapshot transitions.

</details>

<details>
<summary><strong>Can I use this without running the import pipeline?</strong></summary>
<br>

Yes — create `SoundClipData`, `MusicTrack`, and `AmbientTrack` ScriptableObjects manually and assign them to the library lists on AudioManager. The pipeline automates this and generates `AudioNames.cs`, but everything works without it.

</details>

<details>
<summary><strong>CrossFadeMusic vs StopMusic + PlayMusic — what's the difference?</strong></summary>
<br>

`StopMusic` → `PlayMusic` creates a silent gap unless you perfectly sequence the fade durations yourself. `CrossFadeMusic` runs both fades simultaneously on two internal `AudioSource` components — no gap, no timing math required. Use `CrossFadeMusic` for in-game transitions; `StopMusic` + `PlayMusic` is fine when a deliberate silence is intentional (e.g. a menu-to-gameplay cut).

</details>

<details>
<summary><strong>Can I layer multiple ambient soundscapes?</strong></summary>
<br>

Yes — `PushAmbientProfile` stacks profiles. The manager merges desired volumes (max wins per track) and distributes the voice budget across all active layers. `AmbientZone` in Stack mode handles push/pop automatically based on the player's position.

</details>

<details>
<summary><strong>How do I respond to a music track finishing?</strong></summary>
<br>

Subscribe to `AudioManager.Instance.onMusicFinished` (`UnityEvent<string>`). It fires when a **non-looping** track reaches its natural end. For manual stops, `onMusicStopped` fires instead. Both can be wired in the Inspector with no code.

</details>

<details>
<summary><strong>Can I have multiple emitters for the same ambient track?</strong></summary>
<br>

Yes — enable `allowMultipleEmittersPerTrack` on AudioManager. The voice budgeting system scores each one independently and keeps the closest / highest-priority ones playing. Disable it to enforce a strict one-emitter-per-track rule.

</details>

---

<div align="center">

---

Made with ♥ by **Snog / Pedro Schenegoski**

[![Email](https://img.shields.io/badge/snogdev%40gmail.com-EA4335?style=flat-square&logo=gmail&logoColor=white)](mailto:snogdev@gmail.com)

*If Snog saves you time on a project, a review on the Asset Store makes a real difference — thank you.*

</div>
