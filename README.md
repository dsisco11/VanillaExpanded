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

### Quick Tools

Hold the Quick Tools hotkey (assign it in Controls) to open a radial menu of available tools, weapons, healing items, and light sources from your hotbar and backpack. Select an item to equip it instantly; use the center **Unequip** action to restore your previous item or stow the item currently held.

### New Hotkeys

- Hotkey for quickly swapping a light source into the off-hand (default: `F`) or hotbar (default: `Shift + F`) when available (press again to swap the light source back into its prior slot).

- Hotkey for quickly opening the Quick Tools radial menu (default: `R`).

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

### Implemented Missing Functionalities

- Ignitable things (firepits, etc) can now be ignited using lanterns, oil lamps, and candles.

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

The sections and setting order below match the ConfigLib menu. Setting names are the keys used in `ModConfig/VanillaExpanded.json`; ranges and choices are those offered by ConfigLib.

#### Auto Stash

| Setting | Default | Range / choices | Description |
| --- | --- | --- | --- |
| `EnableAutoStash` * | `true` | `true`, `false` | Enable auto-stashing items into containers by holding the interact key. |
| `AutoStashDelay` | `0.5` | 0.1–2 | Time in seconds to hold the interact key before auto-stashing begins. |
| `AutoStashGuiRendersTopmost` | `true` | `true`, `false` | Render the auto-stash progress bar above dialogs and other game UI. |

#### Ignition Tools

| Setting | Default | Range / choices | Description |
| --- | --- | --- | --- |
| `EnableIgnitionTools` * | `true` | `true`, `false` | Enable lighting fires using lanterns, candles, and oil lamps. |
| `IgnitionDelay` | `0.5` | 0.1–2 | Time in seconds to hold the interact key before igniting a fire. |

#### Spawn Decal

| Setting | Default | Range / choices | Description |
| --- | --- | --- | --- |
| `EnableSpawnDecal` * | `true` | `true`, `false` | Show a glowing decal at the player's respawn point. |
| `SpawnDecalSize` | `0.4` | 0.2–1 | Size of the spawn point decal. |

#### Alloy Calculator

| Setting | Default | Range / choices | Description |
| --- | --- | --- | --- |
| `EnableAlloyCalculator` | `true` | `true`, `false` | Enable the alloy calculator GUI for crucibles. |
| `DisableAlloyCalculatorPatch` | `false` | `true`, `false` | Disable firepit GUI detection for the alloy calculator. Requires a world reload or restart; prevents automatic opening while disabled. |

#### Gameplay

| Setting | Default | Range / choices | Description |
| --- | --- | --- | --- |
| `EnableBucketSourceProtectionPatch` | `true` | `true`, `false` | Protect existing water blocks from bucket spills when liquid source transport is disabled in world options. |
| `EnableEquipLightHotkey` | `true` | `true`, `false` | Enable the hotkey to equip light sources to offhand/hotbar. |

#### Recipe Toggles

| Setting | Default | Range / choices | Description |
| --- | --- | --- | --- |
| `EnableBackpackDecraft` | `true` | `true`, `false` | Enable decrafting backpacks into leather using knife or shears. |
| `EnableLinenSackDecraft` | `true` | `true`, `false` | Enable decrafting linen sacks into flax fibers using knife or shears. |
| `EnableMetalBitsRecycling` | `true` | `true`, `false` | Enable recycling metal tool heads into metal bits using a chisel. |
| `EnableStickRecipes` | `true` | `true`, `false` | Enable crafting sticks from planks and firewood. |
| `EnableWattleDecraft` | `true` | `true`, `false` | Enable decrafting wattle blocks into sticks. |

#### Item Slot Indicators

| Setting | Default | Range / choices | Description |
| --- | --- | --- | --- |
| `EnablePerishableItemFreshnessIndicators` | `true` | `true`, `false` | Show freshness on perishable item slots using the selected indicator style. |
| `EnablePreparationIndicators` | `true` | `true`, `false` | Show preparation progress on item slots. |
| `EnableClothingIndicators` | `true` | `true`, `false` | Show clothing condition on item slots. |
| `EnableLiquidContainerIndicators` | `true` | `true`, `false` | Show liquid volume on liquid containers and watering cans. |
| `EnableCrucibleIndicators` | `true` | `true`, `false` | Show metal amount in filled crucibles and firepit input crucibles. |
| `EnableNightVisionFuelIndicators` | `true` | `true`, `false` | Show night-vision fuel on item slots. |
| `PerishableItemFreshnessIndicatorStyle` | `freshness-background` | `freshness-background`, `freshness-outline`, `freshness-bar` | Choose a bottom-up background fill, colored outline, or horizontal progress bar. |
| `PerishableItemFreshnessIndicatorIntensity` | `0.35` | 0.05–1 | Opacity of freshness backgrounds and outlines; progress bars remain fully opaque. |
| `FoodLevelIndicatorStyle` | `progress-bar` | `progress-bar`, `slot-background` | Show food amounts as a progress bar or animated particles in the slot background. Freshness has its own style. |
| `EnableLiquidSloshEffect` | `true` | `true`, `false` | Animate liquid volume indicators; disabling retains plain fills and pauses the liquid simulation. |
| `EnableCrucibleEffect` | `true` | `true`, `false` | Animate solid and molten metal; disabling retains a plain amount indicator. |
| `CrucibleIndicatorCapacityUnits` | `2560` | 100–25600 | Metal units represented by a full indicator; a visual reference, not a container capacity limit. |

#### Radial Menus

| Setting | Default | Range / choices | Description |
| --- | --- | --- | --- |
| `RadialMenuBackdropOpacity` | `0.2` | 0–1 | Opacity of the screen-darkening backdrop behind radial menus; 0 disables it. |
| `RadialMenuOpacity` | `0.5` | 0–1 | Background and border opacity of radial menus. Icons and labels retain their opacity. |
| `EnableToolModeRadialMenu` * | `true` | `true`, `false` | Replace base-game tool-mode grids with a radial menu. |
| `ToolModeMenuSize` | `1` | 0.15–2.5 | Scale the tool-mode radial menu; 1 keeps its default size. |
| `ToolModeRingSize` | `1` | 0.15–2.5 | Scale tool-mode option-ring thickness while preserving the center circle and gaps. |
| `ToolModeCenterSize` | `1` | 0.15–2.5 | Scale the tool-mode center circle; 1 keeps its default size. |
| `ToolModeIconSize` | `0.75` | 0.15–2.5 | Scale icons within tool-mode wedges; 1 fills the available icon space. Larger values may overlap wedges. |
| `QuickToolSelectOnRelease` | `true` | `true`, `false` | Select the hovered quick-tool entry when the activation key is released. |
| `QuickSwapMenuSize` | `1` | 0.15–2.5 | Scale the quick-swap radial menu; 1 keeps its default size. |

#### Animal Sex Indicators

| Setting | Default | Range / choices | Description |
| --- | --- | --- | --- |
| `EnableAnimalSexIndicators` | `true` | `true`, `false` | Show terrain-occluded sex symbols above living generation 1+ animals, with a heart for pregnant females. |
| `AnimalSexIndicatorSize` | `0.25` | 0.05–1 | Base size in blocks, rendered at half this value (default visible size: 0.125 blocks). |
| `AnimalSexIndicatorRange` | `8` | 1–8 | Viewing distance in blocks; symbols fade over the farther half of this range. |
| `AnimalSexIndicatorOpacity` | `0.75` | 0–1 | Maximum opacity before distance fading; 0.75 means 75%. |
| `AnimalMaleIconColor` | `5089023` | 0–16777215 | Packed RGB decimal color; default blue (#4DA6FF). |
| `AnimalFemaleIconColor` | `16741813` | 0–16777215 | Packed RGB decimal color for female and pregnant-female icons; default pink (#FF75B5). |

* Settings marked with an asterisk require a world reload when enabling a feature that was disabled when the world loaded, because its systems or patches were not installed.

Recipe-toggle changes require a world reload to rebuild the available recipes. `DisableAlloyCalculatorPatch` also requires a world reload or restart. The normal `EnableAlloyCalculator` toggle can change live when its patch is installed.

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

## Developer documentation

See [item-slot indicator effects](docs/ItemSlotIndicatorEffects.md) for internal registration, shader and geometry contracts, resource ownership, and validation requirements.
