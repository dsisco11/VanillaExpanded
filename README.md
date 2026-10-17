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

### Auto-Loot

Collect available corpse loot without shift-clicking each stack:

1. Harvest the corpse normally first. AutoLoot does not skin, butcher, generate loot, or bypass access restrictions.
2. Press and hold the right mouse button on a supported corpse. A normal click still opens the vanilla loot window; no extra hotkey or modifier is needed.
3. Keep holding while the radial **Auto Looting** indicator completes (0.5 seconds by default). It always renders above the loot window; moving the cursor over that window does not cancel the hold.
4. Everything that fits is transferred into your player storage. The loot window closes automatically if the corpse is empty. If any items remain, the window stays open with the overflow, and you must release before holding again for another pass.

Release, close the loot window, change world targets, move out of reach, or disable AutoLoot to cancel. Attached-container Auto-Stash takes precedence when its gesture is used.

AutoLoot is enabled by default. To turn it off or adjust the hold duration, open **Pause menu > Mod Settings > VanillaExpanded > Auto Loot** with ConfigLib installed. For manual configuration, see [Auto Loot settings](#auto-loot-settings).

AutoLoot runs entirely on the client: neither VanillaExpanded nor ConfigLib is required on the server for this feature. ConfigLib is optional for the client-side settings menu. AutoLoot and Auto-Stash can be enabled independently. Transfers use vanilla packets and server authorization; progress completion initiates collection, not a server-confirmed success notification.

**Supported targets:** dead, harvested, non-player corpses with the standard game loot window. Modded creatures using the same vanilla harvesting behavior are supported; custom looting systems, attached bags, player corpses, and general containers are not AutoLoot targets.

**If nothing moves:** check that the corpse has been harvested, still contains loot, and its normal loot window is open. Make room in your inventory, stay within interaction range, then release and hold right-click again. AutoLoot makes one collection attempt per hold; it does not keep retrying when your inventory is full.

Compatibility evidence currently covers inspection of the Vintage Story **1.22.7** native protocol and automated tests. In-game acceptance, including unmodified-server gameplay, latency, and simultaneous looters, remains pending; see the [AutoLoot proposal](docs/AutoLootProposal.md).

### Quick Tools

Hold the Quick Tools hotkey (assign it in Controls) to open a radial menu of available tools, weapons, healing items, and light sources from your hotbar and backpack. Select an item to equip it instantly; use the center **Unequip** action to restore your previous item or stow the item currently held.

### Movable HUD Indicators

Free the mouse (for example, by opening your inventory) and hover a visible indicator to reveal its titlebar. Choose **Movable** from the titlebar dropdown, then drag the indicator itself or its title area. A separate group titlebar moves the whole group; dragging an individual away detaches it. **Fixed** locks both the group and its members without changing their placement or membership. The close icon dismisses the controls, leaving the indicators enabled.

Snap an indicator beside another to create or join a group, or drag a whole group beside another to merge their arrangements. The preview shows the destination and insertion before release. All four sides support alignment selected by pointer position; snaps that would overlap visible indicators are rejected. Overlapping free drops are cancelled and restore the starting arrangement without saving. Hold Shift to permit intentional overlap. Screen, edge, and center guides align items without grouping them. Hold **Shift** to bypass snapping and grouping, or press **Escape** to cancel the drag.

Choose **Reset HUD layout** from any titlebar dropdown to restore all built-in groups and positions, including saved overrides for temporarily unavailable integrations. Layouts and Fixed/Movable permissions persist in native client settings across restarts and worlds. Existing saved group positions migrate automatically. An unreadable or newer layout is preserved, with defaults shown read-only until you explicitly reset it.

By default, time, environmental temperature, calendar, and weather form a column at the top-right, in that order. Hidden indicators release their space after fading out and return to their saved arrangement when visible again. Changing styles or display size preserves attachments and the group's top-left origin, subject to screen clamping. See [HUD overlay integration](docs/hud-overlays/HudOverlays.md) for layout and persistence details.

### Approximate Time

A workshop-style copper radial clock shows estimated time. Its 24 hourly segments are colored dynamically from the local calendar's day, dawn, dusk, and night phases. Noon is at the top and midnight at the bottom; a copper pointer follows the estimate. The immersive clock uses the shared 90 × 90 GUI-unit Clock size. Local skylight, including nighttime skylight, recalibrates the estimate; lack of skylight gradually introduces up to eight hours of error on a normal day. The server saves observation age across reconnects and world reloads. Multiplayer tracking requires the mod on the server. Toggle it live with `EnableApproximateTimeOverlay` and choose its presentation with `ApproximateTimeGaugeStyle`.

### Environmental Temperature

An independent radial red-liquid thermometer beneath the clock samples local climate every 300 ms. Its immersive copper instrument spans -40°C to +60°C, with ticks every 5°C and a normal-sized Celsius reading. The numeric reading remains unclamped. Missing climate hides it, and `EnableEnvironmentalTemperatureOverlay` controls it independently of the clock. `EnvironmentalTemperatureGaugeStyle` selects the presentation.

### Calendar Date

A compact calendar beneath the thermometer displays the three-letter localized month and native calendar day. Its immersive copper housing measures 96 × 36 GUI units and has separate month/day rollers. It refreshes every 300 ms independently of the time estimate. Use `EnableCalendarDateOverlay` to toggle it and `CalendarDateGaugeStyle` to select its presentation.

### Indicator Styles

Each indicator supports `icon` artwork and `text`, which replaces the artwork with a short name such as `AMMO`, `WET`, or `STORM` while retaining its readings and state. Time, environmental temperature, and calendar also support `immersive` copper instruments, their default. Other indicators default to `icon`. Style selectors apply live through ConfigLib and are independent of each indicator's enable toggle. Text uses the normal HUD font, preserves fades and storm urgency pulses, and expands its width for longer readings. See the HUD configuration table below for each selector.
### Weather Conditions

Weather and approximate time sit together at the top-right of the screen, with weather below environmental temperature. Weather occupies one 36 × 36 GUI-unit slot. Rounded clouds, wind strokes above the cloud, precipitation below it, fog bands, and lightning combine to show cloud coverage, precipitation type, and independent weather intensities. The indicator uses transparent 64 × 64 texture layers with no outer ring. Toggle it with `EnableWeatherConditionsOverlay`; changes apply immediately. See [weather conditions](docs/weather-conditions/WeatherConditions.md) for the data, artwork, and preview sheet.

### Temporal Rift Activity

A fissure icon beside the toolbar's bottom-left edge shows the current rift activity. The tear widens and branches from pale cyan at low activity through cyan at medium, amber at high or very high, and red-orange at apocalyptic. It hides during calm activity. Toggle it live with `EnableRiftActivityOverlay`.

### Temporal Storm Warning

A fractured iris icon beside the toolbar's bottom-left edge, between rift activity and the wetness/temperature indicators, warns of temporal storms. A narrow eye indicates an approaching storm, an open eye with separated portal fragments indicates an imminent storm, and a ruptured portal with outward spikes indicates an active storm. The imminent icon pulses in opacity, accelerating from one pulse every two seconds to three pulses per second as the storm nears. During waning, the active icon starts the same accelerating pulse until the storm ends. It fades out and releases its space between storms. Toggle it live with `EnableTemporalStormOverlay`.

### Body Temperature Warning

A compact thermometer appears just left of the toolbar, aligned with its bottom edge, when body temperature becomes too low or high. Its colored column rises with temperature, and the adjacent reading shows whole degrees (for example, `34°C`). The icon has an outline for contrast.

Two slim blue drops immediately left of temperature show player wetness, with a whole-number percentage centered beneath them. The drops stay fully filled; the percentage shows the game's synchronized wetness value. It uses the same height as the other status icons, refreshes every 300 ms, and fades out and collapses when dry. It stays hidden for dead players and in Creative or Spectator mode. Toggle it live through `EnablePlayerWetnessOverlay`.

Cold warnings follow the game’s synchronized freezing effect: the icon appears while that effect is active, and its blue color deepens with the effect’s intensity. Elevated-temperature warnings use the converted reading and appear in orange above 42°C and red above 44°C for the default player. Vanilla’s normal warmth ceiling does not reach these thresholds. The numeric reading uses the character panel’s temperature conversion, rounded to whole degrees. Heat warnings are informational: the game has no native overheating-damage threshold. A quarter-degree recovery margin prevents the heat warning flickering near its visibility boundary.

While a temperature warning is visible, orange chevrons above the reading indicate warming and blue chevrons below it indicate cooling. One chevron means slow change; two vertically stacked chevrons mean fast change. The reading uses normal HUD text size, and the whole indicator keeps the same height as the other status icons. When the reading is hidden, the chevrons remain beside the thermometer.

The warning uses synchronized body temperature and stays hidden in Creative and Spectator modes. Toggle it through `EnableBodyTemperatureOverlay`, or turn off `ShowBodyTemperatureReading` for the icon alone.

### Bow Ammunition

While holding a supported base-game bow in your main hand, a compact `<count>x <arrow icon>` indicator shows the next arrow type and its total positive quantity in inventories eligible for bow selection.

The arrow icon focuses on the arrowhead over a translucent black circular background. The default placement is just above the saturation meter, aligned with its left edge. With no eligible arrows, it shows a muted arrow symbol and localized `No arrows / 0`. Unsupported custom or derived bows hide the indicator.

### New Hotkeys

- Hotkey for quickly swapping a light source into the off-hand (default: `F`) or hotbar (default: `Shift + F`) when available (press again to swap the light source back into its prior slot).

- Hotkey for quickly opening the Quick Tools radial menu (default: `R`).

### Alloy Calculator

When opening a firepit with a crucible, an Alloy Calculator dialog automatically appears alongside the firepit UI. This tool helps you calculate the exact metal ratios needed for creating alloys:

- **Select an alloy** from the dropdown to see its ingredient requirements
- **Adjust the target units** to specify how much metal you want to produce
- **Fine-tune ingredient ratios** using the sliders which automatically stay within valid alloy ranges
- **See required amounts** as item stacks for easy reference
- **Preview fuel needs** with an indicator showing the preferred available fuel and estimated count for the selected batch, even before depositing ingredients or filling the fuel slot
- **See prewarming savings** when queued or burning fuel cannot reach the batch's melting temperature: the indicator estimates how many smelting-fuel items you can save by letting it burn before switching fuel
- **Deposit button** automatically transfers the required ingredients from your backpack, hotbar, and opened storage containers into the crucible. It first reserves space for each selected ingredient form (such as nuggets and metal bits), then spreads the ingredients evenly across spare slots without mixing incompatible stacks.

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

| Setting                      | Default | Range / choices | Description                                                             |
| ---------------------------- | ------- | --------------- | ----------------------------------------------------------------------- |
| `EnableAutoStash` \*         | `true`  | `true`, `false` | Enable auto-stashing items into containers by holding the interact key. |
| `AutoStashDelay`             | `0.5`   | 0.1–2           | Time in seconds to hold the interact key before auto-stashing begins.   |
| `AutoStashGuiRendersTopmost` | `true`  | `true`, `false` | Render the auto-stash progress bar above dialogs and other game UI.     |

#### Auto Loot Settings

| Setting          | Default | Range / choices         | Description                                                                                                        |
| ---------------- | ------- | ----------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `EnableAutoLoot` | `true`  | `true`, `false`         | Enable hold-to-loot for supported harvested corpses. Client-local; disabling cancels current progress immediately. |
| `AutoLootDelay`  | `0.5`   | 0.1-2 seconds, step 0.1 | Hold duration before collection. Client-local; ConfigLib changes apply immediately, including during a hold.       |

There is no AutoLoot layering option: its progress always renders above dialogs, independently of `AutoStashGuiRendersTopmost`. Both AutoLoot controls work live through ConfigLib, including enabling the feature after loading the world. Without ConfigLib, edit the client's `ModConfig/VanillaExpanded.json` and restart to load the changes. Invalid non-positive or non-finite manual delays cancel the attempt and log an error rather than collecting.

#### Ignition Tools

| Setting                  | Default | Range / choices | Description                                                      |
| ------------------------ | ------- | --------------- | ---------------------------------------------------------------- |
| `EnableIgnitionTools` \* | `true`  | `true`, `false` | Enable lighting fires using lanterns, candles, and oil lamps.    |
| `IgnitionDelay`          | `0.5`   | 0.1–2           | Time in seconds to hold the interact key before igniting a fire. |

#### Spawn Decal

| Setting               | Default | Range / choices | Description                                         |
| --------------------- | ------- | --------------- | --------------------------------------------------- |
| `EnableSpawnDecal` \* | `true`  | `true`, `false` | Show a glowing decal at the player's respawn point. |
| `SpawnDecalSize`      | `0.4`   | 0.2–1           | Size of the spawn point decal.                      |

#### Alloy Calculator

| Setting                       | Default | Range / choices | Description                                                                                                                            |
| ----------------------------- | ------- | --------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `EnableAlloyCalculator`       | `true`  | `true`, `false` | Enable the alloy calculator GUI for crucibles.                                                                                         |
| `DisableAlloyCalculatorPatch` | `false` | `true`, `false` | Disable firepit GUI detection for the alloy calculator. Requires a world reload or restart; prevents automatic opening while disabled. |

#### Gameplay

| Setting                             | Default | Range / choices | Description                                                                                                 |
| ----------------------------------- | ------- | --------------- | ----------------------------------------------------------------------------------------------------------- |
| `EnableBucketSourceProtectionPatch` | `true`  | `true`, `false` | Protect existing water blocks from bucket spills when liquid source transport is disabled in world options. |
| `EnableEquipLightHotkey`            | `true`  | `true`, `false` | Enable the hotkey to equip light sources to offhand/hotbar.                                                 |

#### Recipe Toggles

| Setting                    | Default | Range / choices | Description                                                           |
| -------------------------- | ------- | --------------- | --------------------------------------------------------------------- |
| `EnableBackpackDecraft`    | `true`  | `true`, `false` | Enable decrafting backpacks into leather using knife or shears.       |
| `EnableLinenSackDecraft`   | `true`  | `true`, `false` | Enable decrafting linen sacks into flax fibers using knife or shears. |
| `EnableMetalBitsRecycling` | `true`  | `true`, `false` | Enable recycling metal tool heads into metal bits using a chisel.     |
| `EnableStickRecipes`       | `true`  | `true`, `false` | Enable crafting sticks from planks and firewood.                      |
| `EnableWattleDecraft`      | `true`  | `true`, `false` | Enable decrafting wattle blocks into sticks.                          |

#### Item Slot Indicators

| Setting                                     | Default                | Range / choices                                              | Description                                                                                                                                                                                                         |
| ------------------------------------------- | ---------------------- | ------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `EnablePerishableItemFreshnessIndicators`   | `true`                 | `true`, `false`                                              | Show freshness on perishable item slots using the selected indicator style.                                                                                                                                         |
| `EnablePreparationIndicators`               | `true`                 | `true`, `false`                                              | Show preparation progress on item slots.                                                                                                                                                                            |
| `EnableClothingIndicators`                  | `true`                 | `true`, `false`                                              | Show clothing condition on item slots.                                                                                                                                                                              |
| `EnableLiquidContainerIndicators`           | `true`                 | `true`, `false`                                              | Show liquid volume on liquid containers and watering cans.                                                                                                                                                          |
| `EnableCrucibleIndicators`                  | `true`                 | `true`, `false`                                              | Show metal amount in filled crucibles and firepit input crucibles.                                                                                                                                                  |
| `EnableNightVisionFuelIndicators`           | `true`                 | `true`, `false`                                              | Show night-vision fuel on item slots.                                                                                                                                                                               |
| `PerishableItemFreshnessIndicatorStyle`     | `freshness-background` | `freshness-background`, `freshness-outline`, `freshness-bar` | Choose a bottom-up background fill, colored outline, or horizontal progress bar.                                                                                                                                    |
| `PerishableItemFreshnessIndicatorIntensity` | `0.35`                 | 0.05–1                                                       | Opacity of freshness backgrounds and outlines; progress bars remain fully opaque.                                                                                                                                   |
| `FoodLevelIndicatorStyle`                   | `progress-bar`         | `progress-bar`, `slot-background`                            | Show remaining portions for any item with serving data, not just food vessels, as a progress bar or animated particles. Full pie slices show 100%; vessels use their serving capacity. Freshness has its own style. |
| `EnableLiquidSloshEffect`                   | `true`                 | `true`, `false`                                              | Animate liquid volume indicators; disabling retains plain fills and pauses the liquid simulation.                                                                                                                   |
| `EnableCrucibleEffect`                      | `true`                 | `true`, `false`                                              | Animate solid and molten metal; disabling retains a plain amount indicator.                                                                                                                                         |
| `CrucibleIndicatorCapacityUnits`            | `2560`                 | 100–25600                                                    | Metal units represented by a full indicator; a visual reference, not a container capacity limit.                                                                                                                    |

#### Radial Menus

| Setting                       | Default | Range / choices | Description                                                                                              |
| ----------------------------- | ------- | --------------- | -------------------------------------------------------------------------------------------------------- |
| `RadialMenuBackdropOpacity`   | `0.2`   | 0–1             | Opacity of the screen-darkening backdrop behind radial menus; 0 disables it.                             |
| `RadialMenuOpacity`           | `0.5`   | 0–1             | Background and border opacity of radial menus. Icons and labels retain their opacity.                    |
| `EnableToolModeRadialMenu` \* | `true`  | `true`, `false` | Replace base-game tool-mode grids with a radial menu.                                                    |
| `ToolModeMenuSize`            | `1`     | 0.15–2.5        | Scale the tool-mode radial menu; 1 keeps its default size.                                               |
| `ToolModeRingSize`            | `1`     | 0.15–2.5        | Scale tool-mode option-ring thickness while preserving the center circle and gaps.                       |
| `ToolModeCenterSize`          | `1`     | 0.15–2.5        | Scale the tool-mode center circle; 1 keeps its default size.                                             |
| `ToolModeIconSize`            | `0.75`  | 0.15–2.5        | Scale icons within tool-mode wedges; 1 fills the available icon space. Larger values may overlap wedges. |
| `QuickToolSelectOnRelease`    | `true`  | `true`, `false` | Select the hovered quick-tool entry when the activation key is released.                                 |
| `QuickSwapMenuSize`           | `1`     | 0.15–2.5        | Scale the quick-swap radial menu; 1 keeps its default size.                                              |

#### Animal Sex Indicators

| Setting                     | Default    | Range / choices | Description                                                                                              |
| --------------------------- | ---------- | --------------- | -------------------------------------------------------------------------------------------------------- |
| `EnableAnimalSexIndicators` | `true`     | `true`, `false` | Show terrain-occluded sex symbols above living generation 1+ animals, with a heart for pregnant females. |
| `AnimalSexIndicatorSize`    | `0.25`     | 0.05–1          | Base size in blocks, rendered at half this value (default visible size: 0.125 blocks).                   |
| `AnimalSexIndicatorRange`   | `8`        | 1–8             | Viewing distance in blocks; symbols fade over the farther half of this range.                            |
| `AnimalSexIndicatorOpacity` | `0.75`     | 0–1             | Maximum opacity before distance fading; 0.75 means 75%.                                                  |
| `AnimalMaleIconColor`       | `5089023`  | 0–16777215      | Packed RGB decimal color; default blue (#4DA6FF).                                                        |
| `AnimalFemaleIconColor`     | `16741813` | 0–16777215      | Packed RGB decimal color for female and pregnant-female icons; default pink (#FF75B5).                   |

#### HUD Overlays

| Setting                      | Default      | Range / choices                                                                                                                                                                                                              | Description                                                                             |
| ---------------------------- | ------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `EnableHudOverlays`          | `true`       | `true`, `false`                                                                                                                                                                                                              | Enable passive HUD overlays; applies live without restarting.                           |
| `BowAmmunitionIndicatorStyle` | `icon` | `icon`, `text` | Ammunition icon or AMMO name and count. Applies live. |
| `BodyTemperatureIndicatorStyle` | `icon` | `icon`, `text` | Thermometer icon or BODY name, risk, trend, and optional temperature. Applies live. |
| `PlayerWetnessIndicatorStyle` | `icon` | `icon`, `text` | Droplet icon or WET name and percentage. Applies live. |
| `ApproximateTimeGaugeStyle` | `immersive` | `immersive`, `icon`, `text` | Copper clock, time-period icon, or TIME name and estimated period. Applies live. |
| `EnvironmentalTemperatureGaugeStyle` | `immersive` | `immersive`, `icon`, `text` | Copper thermometer, compact icon, or TEMP name and Celsius reading. Applies live. |
| `CalendarDateGaugeStyle` | `immersive` | `immersive`, `icon`, `text` | Mechanical calendar, compact icon, or DATE name and month/day. Applies live. |
| `WeatherConditionsIndicatorStyle` | `icon` | `icon`, `text` | Composite artwork or WTHR name and independent weather bands. Applies live. |
| `RiftActivityIndicatorStyle` | `icon` | `icon`, `text` | Fissure icon or RIFT name and activity level. Applies live. |
| `TemporalStormIndicatorStyle` | `icon` | `icon`, `text` | Fractured iris icon or STORM name and stage; both retain urgency pulses. Applies live. |
| `EnableCalendarDateOverlay` | `true` | `true`, `false` | Show the independent month/day calendar; applies live. |
| `EnableBowAmmunitionOverlay` | `true`       | `true`, `false`                                                                                                                                                                                                              | Show the next-arrow type and type-total quantity while holding a supported bow.         |
| `EnableBodyTemperatureOverlay` | `true` | `true`, `false` | Show the dynamic thermometer when too cold or hot; applies live. |
| `EnablePlayerWetnessOverlay` | `true` | `true`, `false` | Show player wetness and a percentage in the selected style; applies live. |
| `ShowBodyTemperatureReading` | `true` | `true`, `false` | Show whole degrees beside the thermometer; disable for the icon alone. |
| `HeldItemStatusAnchor`       | `saturation` | `saturation`, `hotbar`, `screen-left-top`, `screen-center-top`, `screen-right-top`, `screen-left-middle`, `screen-center-middle`, `screen-right-middle`, `screen-left-bottom`, `screen-center-bottom`, `screen-right-bottom` | Select native docking or a screen position. Unknown values fall back to saturation.     |
| `HeldItemStatusOffsetX`      | `-4`         | -2048–2048 in ConfigLib                                                                                                                                                                                                      | Horizontal offset in GUI units; positive moves right. Nonfinite values fall back to -4. |
| `HeldItemStatusOffsetY`      | `0`          | -2048–2048 in ConfigLib                                                                                                                                                                                                      | Vertical offset in GUI units; positive moves down. Nonfinite values fall back to 0.     |
| `EnableWeatherConditionsOverlay` | `true` | `true`, `false` | Show the composite cloud, wind, precipitation, fog/haze, and thunder indicator; applies live. |
| `EnableRiftActivityOverlay` | `true` | `true`, `false` | Show a fissure icon for temporal rift activity; hidden during calm activity. Applies live. |
| `EnableTemporalStormOverlay` | `true` | `true`, `false` | Show approaching, imminent, and active temporal storms with a fractured iris icon. Applies live. |
| `EnableEnvironmentalTemperatureOverlay` | `true` | `true`, `false` | Show local Celsius temperature in the selected style; applies live. |
| `EnableApproximateTimeOverlay` | `true` | `true`, `false` | Show sunlight-calibrated approximate time; applies live. |

The two offsets are shared across placement choices. The saturation default compensates for content padding and leaves a four-unit gap above the meter. Hotbar docking attaches the group's left-middle to the hotbar's right-middle; screen placements use matching attachment and pivot points. Native viewport clamping keeps the group within the safe rectangle. Placement changes apply live and retain cached presentation; restored content receives a fresh sample before display. Without ConfigLib, edit the JSON file and restart the game to load manual changes.

- Settings marked with an asterisk require a world reload when enabling a feature that was disabled when the world loaded, because its systems or patches were not installed.

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

See [anchored HUD overlays](docs/hud-overlays/HudOverlays.md) for passive registration, configuration, and resource ownership contracts.

See [item-slot indicator effects](docs/ItemSlotIndicatorEffects.md) for internal registration, shader and geometry contracts, resource ownership, and validation requirements.
