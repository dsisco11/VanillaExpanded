# VanillaExpanded

## Overview

VanillaExpanded is a mod for VintageStory that aims to add quality-of-life enhancements, missing functionality, and minor bug fixes while remaining true to the vanilla game and with minimal impacts to game balance.

![Game Version](https://img.shields.io/badge/Vintage%20Story-1.21.5+-blue)
![Version](https://img.shields.io/github/v/release/dsisco11/VanillaExpanded?label=Version&color=green)

## Features

### Auto-Stash

You can now bulk transfer matching items from your inventory into storage containers!

1. When interacting with a container, hold the `interact` button (default: right-click).
2. If you have items in your inventory which match items already in the container, a brief progress-bar will be shown at the center of the screen.
3. After the progress-bar completes, all matching items from your inventory will be moved into the container.

<https://github.com/user-attachments/assets/5be8da04-435e-4200-9ecb-41ea837d2a25>

### New Hotkeys

- Hotkey for quickly swapping a light source into the off-hand (default: `F`) or hotbar (default: `Shift + F`) when available (press again to swap the light source back into its prior slot).

### Alloy Calculator

When opening a firepit with a crucible, an Alloy Calculator dialog automatically appears alongside the firepit UI. This tool helps you calculate the exact metal ratios needed for creating alloys:

- **Select an alloy** from the dropdown to see its ingredient requirements
- **Adjust the target units** to specify how much metal you want to produce
- **Fine-tune ingredient ratios** using the sliders which automatically stay within valid alloy ranges
- **See required amounts** as item stacks for easy reference
- **Deposit button** automatically transfers the required ingredients from your inventory into the crucible, spreading them evenly across slots

The calculator remembers your settings per crucible, so your preferred alloy and ratios are restored when you reopen the dialog (currently not remembered across restarts).

### Quality of Life Additions

- Player respawn point appears as a glowing gear symbol on the ground.
- Handbook search prioritizes full-word matches (searching "iron" ranks "Iron Ingot" higher than "Ironwood").

### Visual Effects

- **Unstable Block Particles** — Rock blocks in caves emit crumbling dust particles from their undersides when unstable. Particle frequency increases as blocks become more unstable, providing visual warning of potential cave-ins. Requires cave-ins to be enabled in world settings.

### Implemented Missing Functionalities

- Ignitable things (firepits, etc) can now be ignited using lanterns (_oillamps & candles pending_).

### New Recipes

_Note: for decrafting recipes_  
_A low-tier tool (e.g. knife) yields ~50% of the original materials._  
_A high-tier tool (e.g. saw or shears) yields ~70% of the original materials._

- Planks & Firewood can be cut into sticks using a knife or saw (saw yields more).
- Linen & Leather bags can be de-crafted back into their crafting components using a knife or shears (shears yield more).
- Wattle fences/gates can be de-crafted back into sticks and wattle using a knife or saw (saw yields more).
- Metal tool-heads can be de-crafted back into metal-bits using a chisel.
- Metal arrow-heads can be de-crafted back into metal-bits using a chisel.

## Configuration

VanillaExpanded supports [ConfigLib](https://mods.vintagestory.at/configlib) for in-game configuration. If ConfigLib is installed, a "Mod Settings" button appears in the pause menu where you can toggle features on or off.

Without ConfigLib, settings can be edited manually in `ModConfig/VanillaExpanded.json`.

### Available Settings

| Setting                            | Default | Description                                                    |
| ---------------------------------- | ------- | -------------------------------------------------------------- |
| EnableAutoStash                    | true    | Enable auto-stashing items into containers by holding interact |
| EnableIgnitionTools                | true    | Enable lighting fires using lanterns, candles, and oil lamps   |
| EnableSpawnDecal                   | true    | Show a glowing decal at the player's respawn point             |
| EnableAlloyCalculator              | true    | Enable the alloy calculator GUI for crucibles                  |
| EnableEquipLightHotkey             | true    | Enable the hotkey to equip light sources to offhand/hotbar     |
| EnableUnstableParticles            | true    | Show crumbling particles on unstable rock blocks               |
| EnableHandbookSearchPrioritization | true    | Prioritize full-word matches in handbook search results        |
| EnableBackpackDecraft              | true    | Enable decrafting backpacks into leather                       |
| EnableLinenSackDecraft             | true    | Enable decrafting linen sacks into flax fibers                 |
| EnableMetalBitsRecycling           | true    | Enable recycling metal tool heads into metal bits              |
| EnableStickRecipes                 | true    | Enable crafting sticks from planks and firewood                |
| EnableWattleDecraft                | true    | Enable decrafting wattle blocks into sticks                    |
| AutoStashDelay                     | 0.5     | Time in seconds to hold interact before auto-stashing begins   |
| IgnitionDelay                      | 0.5     | Time in seconds to hold interact before igniting a fire        |
| SpawnDecalSize                     | 0.4     | Size of the spawn point decal (0.2 to 1.0)                     |

**Note:** Feature toggles (Enable\*) require a world reload to take effect.

## Testing

The project includes comprehensive unit and end-to-end tests organized by namespace within `VanillaExpanded.Tests`.

### Test Organization

- **Unit Tests** (`VanillaExpanded.Tests/Unit/`): Fast, isolated tests for individual components
  - `AutoStashing/`: Tests for item matching, stashable items detection, and timing constants
  - `EquipLightSource/`: Tests for light source detection logic
- **E2E Tests** (`VanillaExpanded.Tests/E2E/`): Integration tests for system interactions
  - `AutoStashing/`: Tests for network packet handling and client-server communication

### Running Tests

```bash
# Run all tests
dotnet test

# Run only unit tests (fast feedback)
dotnet test --filter "Category=Unit"

# Run only E2E tests (integration validation)
dotnet test --filter "Category=E2E"

# Run tests for a specific feature
dotnet test --filter "FullyQualifiedName~AutoStashing"
```

### Mock Infrastructure

The test project includes reusable mock wrappers in `VanillaExpanded.Tests/Mocks/`:

- `MockItem`: Mock collectible items with configurable IDs and light values
- `MockInventory`: Mock inventory with slot management
- `MockPlayer`: Mock player with inventory manager setup
- `MockClientNetworkChannel`: Captures sent packets for verification
- `MockServerNetworkChannel`: Simulates server-side packet handling

## License

This project is licensed under the Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International Public License for all users except Anego Studios.

Additional grant to Anego Studios:
Anego Studios and its affiliates are granted a perpetual, worldwide, non-exclusive, royalty-free license to use, modify, sublicense, and distribute this code, or derivative works, as part of the official VintageStory game or related products, under any terms of their choosing, without the obligations of Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International Public License, provided that attribution to the original author (“David Sisco”) is given in the game credits or documentation.
