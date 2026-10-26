# Wind speed

The wind indicator registers in the bottom-left `player-status` group at order 96. The default group is unlocked and movable; explicit saved layout permissions remain authoritative. It supports the shared HUD's movement, grouping, hidden-item editing, hover label, and context-menu settings.

`EnableWindSpeedOverlay` controls its visibility; global `EnableHudOverlays` also applies. `WindSpeedIndicatorStyle` selects `Icon` or `Text`. Both settings apply live and are exposed through ConfigLib and the indicator menu.

## Sampling and visibility

Every 300 ms, the indicator reads the magnitude of native `GetWindSpeedAt(player.Pos.XYZ)`, including local modifiers. The readout uses the windmill calculation `(int)(100 * speed)`: fractional percentages are truncated and values above 100% remain visible. These are native wind-strength percentages, not physical velocity units. It requires a living local player.

The existing bands control calm-wind visibility and transition history; they do not select the displayed percentage. Percentage changes refresh the reading even within the same band.

| Band | Entry boundary | Exit boundary |
| --- | --- | --- |
| Light | .05 | .035 |
| Moderate | .30 | .25 |
| Strong | .70 | .60 |

Values must exceed a boundary to activate or retain that band. Calm, missing, and nonfinite wind hide the indicator through the standard fade and space collapse. Replacement entities, disabling, death, and world exit clear classification history. Drawing uses prepared content and never samples gameplay state.

## Presentation and resource ownership

Icon mode uses the shared compact icon size and normal HUD text. The reading keeps its prepared font size as GUI scale changes; wider readings expand the footprint. The icon uses `assets/vanillaexpanded/textures/hud/wind-speed/icon.svg`, the approved double-hook line art, above a localized whole-percentage reading such as `65%`. Text mode uses the short name `WIND` above the same reading and loads no icon. Wind strength does not alter the approved icon's shape.

The wind presentation rasterizes the SVG and owns its texture. The shared status-indicator text helper owns the percentage label; the shared text-mode helper owns the short name and reading. Resources are reused across unchanged refreshes and released on style changes, API changes, completed exits, or world exit. Headless checks establish lifecycle and classification behavior; in-game appearance remains user-run validation.
