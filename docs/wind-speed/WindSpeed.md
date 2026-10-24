# Wind speed

The wind indicator registers in the top-right `world-status` group, after weather at order 102. It supports the shared HUD's movement, grouping, hidden-item editing, hover label, and context-menu settings.

`EnableWindSpeedOverlay` controls its visibility; global `EnableHudOverlays` also applies. `WindSpeedIndicatorStyle` selects `Icon` or `Text`. Both settings apply live and are exposed through ConfigLib and the indicator menu.

## Sampling and visibility

Every 300 ms, the indicator reads the magnitude of native `GetWindSpeedAt(player.Pos.XYZ)`, including local modifiers. These are engine units, not a physical speed measurement. It requires a living local player.

| Band | Entry boundary | Exit boundary |
| --- | --- | --- |
| Light | .05 | .035 |
| Moderate | .30 | .25 |
| Strong | .70 | .60 |

Values must exceed a boundary to activate or retain that band. Calm, missing, and nonfinite wind hide the indicator through the standard fade and space collapse. Replacement entities, disabling, death, and world exit clear classification history. Drawing uses prepared content and never samples gameplay state.

## Presentation and resource ownership

Icon mode uses `assets/vanillaexpanded/textures/hud/wind-speed/icon.svg`, the approved double-hook line art, above a localized Light, Moderate, or Strong reading. Text mode uses the short name `WIND` above the same reading and loads no icon. Wind strength does not alter the approved icon's shape.

The shared compact-icon helper rasterizes the SVG and owns its texture; the shared text helpers own their lettering. Resources are reused across unchanged refreshes and released on style changes, API changes, completed exits, or world exit. Headless checks establish lifecycle and classification behavior; in-game appearance remains user-run validation.
