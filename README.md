# Snog’s Audio Manager for Unity

[![Unity Version](https://img.shields.io/badge/Unity-2022.3%2B-blue.svg)](https://unity3d.com/get-unity/download)
[![Version](https://img.shields.io/badge/Version-1.0.0-green.svg)](version)
  

**Snog’s Audio Manager** is a complete Unity audio solution for **2D SFX**, **3D SFX**, **music**, and **ambient soundscapes**, built for **fast setup**, **clean workflows**, and **Asset Store-quality usability**.

  

It includes:

- **ScriptableObject-based libraries** for SFX, Music, and Ambient tracks.
- **Audio Mixer integration** with exposed volume parameters + snapshot transitions.
- **Editor tooling** to scan an audio folder, auto-generate assets, and assign them into libraries.
- A modern ambient system built around:
	-   **Ambient Profiles** (*WHAT* ambience should be active)
	*   **Ambient Zones** (*WHEN* profiles activate)
	*   **Ambient Emitters** placed in the scene (*WHERE* 3D ambience comes from) 

  

***

  

## Features

###  Quick setup

*   Drop the **AudioManager** prefab (or component) into the scene.
*   Use the **Utilities** panel to **Scan → Generate → Assign** audio assets automatically.

###  In-Editor Preview

*   Preview SFX/Music clips directly in the inspector (no need to open files manually).

###  Mixer & Snapshots

*   Built-in mixer volume controls (Master/Music/Ambient/FX) using exposed mixer parameters.
*   Snapshot transitions (Default/Combat/Stealth/Underwater).

### 2D + 3D SFX

*   2D SFX via a dedicated 2D source.
*   3D SFX via an **AudioSourcePool** for efficient voice reuse.

### Ambient System (Profiles + Zones + 3D Emitters)

*   **Profiles** define layered ambience (wind, hum, insects, etc.).
*   **Zones** push/replace profiles based on player position.
*   **Emitters** are placed in the scene to anchor 3D ambience to real locations *(new system)*.

***

## Table of Contents

1.  Core Concepts
2.  Quick Start
3.  Ambient Workflow (Profiles + Zones + Emitters)
4.  Using AudioManager in Code
5.  Audio Mixer Setup
6.  Troubleshooting / FAQ

***

## Core Concepts

### AudioManager

The central runtime singleton:

*   Plays SFX (2D/3D), plays/stops music, controls mixer volumes, transitions snapshots.

### Libraries

ScriptableObject-driven libraries store your audio references by name:

*   `SoundLibrary` (SFX name → variants)
*   `MusicLibrary` (trackName → clip)
*   `AmbientLibrary` (trackName → clip)

### Clips / Tracks

*   **SFX** use `SoundClipData` (name + multiple variants).
*   **Music** uses `MusicTrack` (name + clip).
*   **Ambient** uses `AmbientTrack` (name + clip).

### Ambient Profiles

`AmbientProfile` is a layered ambience recipe:

*   each `AmbientLayer` references an `AmbientTrack` and defines mix/playback settings (volume, spatialBlend, random start, pitch range).

### Ambient Zones

Zones activate ambience via profiles when the player enters/exits trigger volumes.

### Ambient Emitters (Scene-based 3D ambience)

Emitters are **placed objects** that represent 3D ambient sources in the world *(new system)*:

*   “River here”, “Generator hum here”, “Wind near cliff here”.
*   The ambient system selects which emitters are allowed to play based on active profiles and voice budget.

***

## Quick Start

### 1) Install

Import the package into your Unity project.

### 2) Add AudioManager

*   Drag the **AudioManager prefab** into your scene, **or**
*   Create an empty GameObject and add `AudioManager` to it.

### 3) Organize audio files

Recommended structure:

*   `Assets/Audio/SFX`
*   `Assets/Audio/Music`
*   `Assets/Audio/Ambient`

### 4) Auto-generate audio assets

In **AudioManager → Utilities**:

1.  **Set Root Audio Folder**
2.  **Scan → Generate → Assign**

This will:

*   scan audio clips in the chosen folder,
*   generate `SoundClipData`, `MusicTrack`, and `AmbientTrack` assets,
*   assign them into their libraries automatically.

### 5) Test quickly

Use the **AudioManager custom inspector** runtime tools to:

*   play SFX/Music
*   preview clips
*   set/push/pop ambient profiles
*   switch mixer snapshots

***

## Ambient Workflow (Profiles + Zones + Emitters)

### Step 1 — Place Ambient Emitters (3D locations)

1.  Create a new GameObject in your scene: `River Emitter`
2.  Add **AmbientEmitter** (new component).
3.  Assign an `AmbientTrack` (e.g., `RiverLoop`).
4.  Position it where the sound should originate.

Repeat for multiple points (forest wind emitters, cave hum emitters, etc.).

### Step 2 — Create Ambient Profiles (what should be active)

Create `AmbientProfile` assets such as:

*   **Forest Profile**
	*   WindLoop (volume 0.7)
	*   InsectsLoop (volume 0.5)
*   **River Profile**
	*   RiverLoop (volume 1.0)

Profiles reference tracks via `AmbientLayer.track`.

### Step 3 — Add Ambient Zones (when profiles activate)

Add `AmbientZone` trigger volumes:

*   On enter: push or replace profile
*   On exit: pop or clear profile

This enables overlapping ambience:

*   Forest zone + Rain zone + River zone can stack, and the system plays the best subset within your configured ambient voice budget.

***

## Using AudioManager in Code

### SFX

```csharp
using UnityEngine;
using Snog.Audio;

public class ExampleSfx : MonoBehaviour
{
    private void Start()
    {
        AudioManager.Instance.PlaySfx2D("ui_click");
        AudioManager.Instance.PlaySfx3D("explosion", transform.position);
    }
}
```

### Music

```csharp
using UnityEngine;
using Snog.Audio;

public class ExampleMusic : MonoBehaviour
{
    private void Start()
    {
        AudioManager.Instance.PlayMusic("MainTheme", 0f, 1.5f)
    }
    
    public void Stop()
    {
        AudioManager.Instance.StopMusic(1.0f);
    }
}
```

### Ambient (Replace mode)

```csharp
using UnityEngine;
using Snog.Audio;
using Snog.Audio.Layers;

public class ExampleAmbientReplace : MonoBehaviour
{
    [SerializeField] private AmbientProfile profile;

    private void Start()
    {
        AudioManager.Instance.SetAmbientProfile(profile, 2f);
    }

    public void Clear()
    {
        AudioManager.Instance.ClearAmbient(2f);
    }
}
```

### Ambient (Stack mode)

```csharp

using UnityEngine;
using Snog.Audio;
using Snog.Audio.Layers;

public class ExampleAmbientStack : MonoBehaviour
{
    [SerializeField] private AmbientProfile rainProfile;
    private int token = -1;
    
    public void StartRain()
    {
        token = AudioManager.Instance.PushAmbientProfile(rainProfile, 5, 2f);
    }
    
    public void StopRain()
    {
        if (token >= 0)
        {
            AudioManager.Instance.PopAmbientToken(token, 2f);
            token = -1;
        }
    }
}
```

***

## Audio Mixer Setup

To use volume controls and meters, your AudioMixer should expose these float parameters:

*   `MasterVolume`
*   `MusicVolume`
*   `AmbientVolume`
*   `FXVolume`

Snapshots supported by default:

*   `Default`, `Combat`, `Stealth`, `Underwater`

If you use different names, update the manager’s parameter strings accordingly.

***

## Troubleshooting / FAQ

### “I don’t hear 3D ambience”

Checklist:

*   Did you place at least one **AmbientEmitter** in the scene and assign an `AmbientTrack`? *(new system)*
*   Is an **AmbientProfile** currently active (via zone or code) referencing that same track name/asset?
*   Is `maxAmbientVoices` high enough (e.g., 8–16) to allow that emitter to be selected?

### “Scan → Generate → Assign didn’t create what I expected”

*   Make sure the folder you chose is inside `Assets/`.
*   Naming/folder heuristics matter: “Music” folders or long clips will be treated as music; “Ambient” folders or medium-length loops treated as ambient; short clips treated as SFX.

### “Mixer meters show no movement / volumes don’t change”

*   Confirm your AudioMixer exposes the correct parameters (`MasterVolume`, etc.).

### “I get no SFX or wrong SFX”

*   Ensure `SoundLibrary` contains `SoundClipData` assets with matching `soundName`.

***

##  Support

- For support, please contact [snogdev@gmail.com](mailto:snogdev@gmail.com)
