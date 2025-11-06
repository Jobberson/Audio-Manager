# Snog's Audio Manager

The Snog's Audio Manager is a complete, 2D and 3D, manager designed with ease of use and modularity in mind. 

Set it up with minimal effort with the custom automatic creation of assets and folders. 

This asset allows you to play any sounds, changing volume, mixer snapshots and preview in-editor audio with ease.

Works for any SFX, be it singular or grouped like footsteps, any music, ambiance or ambient noise. Preview the audio directly in the manager's inspector. 

## Features
- **Little Setup Required:** The asset works right out of the box. Just import, take the manager prefabs to the scene and it's ready to go.
- **In-Editor Sound Preview:** Listen to your audio clips directly from the editor, without needing to open the actual file.
- **No Code Necessary:** The asset needs no code for its use, simply use the audio triggers and import your audio clips.

## Table of Contents

1. [Core Concepts](#core-concepts)
    
2. [Initial Setup Guide](#initial-setup-guide)
    
3. [Creating Your First Interaction](#creating-your-first-interaction)
    
4. [Editing and Deleting Interactions](#editing-and-deleting-interactions)
    
5. [Writing Custom Interaction Logic](#writing-custom-interaction-logic)
    
6. [FAQ](#faq)
    

---

## Core Concepts

The system is built on a few key components that work together:

- **AudioManager:** The central part of the asset, the one where you'll spend the most amount of time using for everything.
- **AudioTrigger:** Use the audio triggers to trigger any audio for any reason you can imagine. 
- **Clips:** scriptable objects that are automatically created by the **AudioManager** for its own use.
- **Libraries:** Where all the clips data are stored. They also are created and assigned automatically 
    

---

## Initial Setup Guide

1. Import the asset to your project.

2. Take the **AudioManager prefab** to your scene or create an EmptyGameObject and assign the **AudioManager** script to it.

3. Import your audio clips to an organized folder. Preferrably in `Assets/Audio` with separate folders for music, SFX and Ambient.

4. Assign the audio folder path on the **AudioManager** inspector.

5. Click the **Scan Folders** button, and done! you can use the manager in any of your code or triggers.

---

## Using the **AudioManager** in code

To use the manager in your own code, simply use one of its method like this:

``` csharp
AudioManager.Instance.PlayMusic("MusicExample");
```

### Here's a list of all methods
#### Music:

```csharp 
PlayMusic(string musicName, float delay)
```

---

## Editing and Deleting Interactions

As your project grows, you can manage all your interactions from the same window.

- **To Edit**: Select an interaction from the **Select Interaction** dropdown. Its properties will appear, and you can change the prompt text or key. Click "Update Interaction" to save.
    
- **To Delete**: Select an interaction from the dropdown and click the "Delete Selected Interaction" button. 
    

---

## Writing Custom Interaction Logic

The system creates the template code for you, all you need to do is add the logic you want.

1. After creating the `ChangeColor` interaction, open the `ChangeColorInteraction.cs` script.
    
2. You will see an `Execute` method. This is where your game logic goes.
    

**Example: Change object's color**

C#

``` csharp
using UnityEngine;
using Snog.InteractionSystem.Core.Interfaces;

namespace Snog.InteractionSystem.Behaviors
{
    public class ChangeColorInteraction : MonoBehaviour, IInteractionBehavior
    {
        public void Execute(GameObject target)
        {
            // Try to get the MeshRenderer component from the interacted object.
            var meshRenderer = target.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                Debug.LogWarning($"'{target.name}' has no MeshRenderer component.");
                return;
            }

            // Generate a new random color.
            Color randomColor = new Color
            (
              Random.value, // Red channel
              Random.value, // Green channel
              Random.value  // Blue channel
            );

            // Apply color to the renderer
            meshRenderer.material.color = randomColor;
            Debug.Log($"Changed the color of {target.name} to {randomColor}");
        }
    }
}
```

---

## FAQ
        
- **Pressing the key does nothing?**
    
    - On the `InteractibleObj` component, double-check that the **Interaction Type Name** string exactly matches the name you gave it in the creator window (e.g., "ChangeColor"). It is case-sensitive.
        
