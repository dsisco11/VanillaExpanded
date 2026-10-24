# Weather conditions

The weather indicator occupies 24 × 24 base GUI units (36 × 36 at the standard HUD scale) in the top-right `world-status` group below the calendar while visible and releases its space when hidden. The composite artwork consists of transparent 64 × 64 textures drawn at the same origin, using one shared backdrop. Fog and haze share one channel; thunder has its own channel. Colour must not be the only distinction between weather types or intensity levels.

## Current behavior

The shared HUD content multiplier is 1.5×, so this indicator's 24 × 24 base footprint displays at 36 × 36 GUI units before the game's GUI scale. The 64 × 64 source textures are unchanged.

The complete 21-texture set is included. Independent sky, precipitation, fog/haze, and thunder layers combine inside one fixed footprint. The renderer retains an existing-artwork fallback for missing or incomplete asset packs: precipitation and fog share the slot, while cloud coverage, thunder, and intensity are unavailable in that fallback.

The complete composite set enables a visible clear-weather state. An unavailable cloud estimate selects an unknown-sky layer, rather than claiming clear skies. Disabling the feature, losing the player or climate data, and leaving the world clear presentation and transition history. Changing API ownership reloads borrowed texture bindings; GUI scale and weather changes reuse them. The engine owns the textures.

See [wind speed](../wind-speed/WindSpeed.md) for local wind readings.

## Weather inputs

Sampling runs every 300 ms, outside rendering.

| Channel | Source and meaning |
| --- | --- |
| Precipitation | Local current climate rainfall, with native `BlendedPrecType`. Automatic selection uses temperature below `snowThresholdTemp` for snow and otherwise rain. Explicit rain, snow, and hail remain explicit. Invalid automatic inputs do not invent rain. |
| Cloud coverage | A bounded 5 × 5 neighborhood from the existing player weather reader's cloud tiles. Average `clamp(opacity * clamp(10 * thickness, 0, 1), 0, 1)`, following the native cloud shader's alpha relationship. This is an effective local coverage estimate, not an exact visible-sky percentage; it excludes view-distance, perspective, edge fading, and shader undulation. Missing caches produce unknown. The sampler never initializes or refreshes native cloud caches. |
| Fog/haze | Weather snapshot fog density multiplied by its weight, combined with weather flat fog evaluated at camera height. Flat fog uses `1 - exp(-(eyeY - layerY) * density)`, clamped to 0–1, with `layerY = seaLevel + weighted FlatFogYPos`. Negative density represents mist below the layer. The stronger classified fog/mist band wins. Underwater and temporal scene fog are not sampled. |
| Thunder | Native near and distant lightning rates, summed and multiplied by local `RainCloudOverlay`, clamped to 0–1. The native minimum-temperature gate must pass. This describes current thunder activity, not a forecast or an individual lightning flash. Rain and wind alone never imply thunder. |

Cloud coverage uses existing regional blends and cached density noise. Its estimate, the provisional intensity thresholds, and the artwork's actual readability require in-game evaluation; headless checks cannot establish those visual results.

## Intensity bands

Channels vary independently. Values must exceed an entry boundary to advance; an active band persists until its lower exit boundary is crossed. Non-finite intensity inputs clear that channel; a non-finite cloud estimate remains unknown.

| Channel | Entry boundaries | Exit boundaries |
| --- | --- | --- |
| Cloud coverage: clear, mostly clear, partly cloudy, mostly cloudy, overcast | .05, .25, .60, .90 | .03, .20, .55, .85 |
| Precipitation: absent, light, moderate, heavy | .01, .25, .65 | .005, .20, .55 |
| Distance fog/haze: absent, light, moderate, dense | .0025, .008, .020 | .002, .006, .016 |
| Local mist opacity: absent, light, moderate, dense | .15, .40, .75 | .12, .32, .65 |
| Thunder: absent, light, moderate, heavy | .001, .25, .65 | .0005, .20, .55 |

The low precipitation entry/exit values preserve the previous indicator's transition behavior. The native particle renderer stops particles at rainfall ≤ .02, so the indicator can retain a trace-precipitation state after particles stop.

## Artwork contract

All layers live under `assets/vanillaexpanded/textures/hud/weather/composite/`. Each icon has an editable `.svg` source and a `.png` texture used by the current renderer. Loading requires the complete PNG set; an absent or partial set uses the existing-artwork fallback without requesting missing textures.

| Draw order | Texture names |
| --- | --- |
| Sky | `sky-clear`, `sky-mostly-clear`, `sky-partly-cloudy`, `sky-mostly-cloudy`, `sky-overcast`, `sky-unknown` |
| Precipitation | `rain-light`, `rain-moderate`, `rain-heavy`; equivalent `snow-*` and `hail-*` sets |
| Fog/haze | `fog-light`, `fog-moderate`, `fog-dense` |
| Thunder | `thunder-light`, `thunder-moderate`, `thunder-heavy` |

Each layer uses the entire transparent 64 × 64 canvas. Spatial allocation belongs in the artwork; no condition moves another layer. Thunder draws last. Keep shape boundaries distinct in monochrome and avoid overlap that erases precipitation type. The set is cached for an API binding; adding the approved assets takes effect after presentation reset or a new world session.

## Selected artwork

The selected design uses rounded ivory clouds and precipitation option A. There is no outer ring. Straight fog bands sit below precipitation. Rain uses fine cyan diagonal streaks, snow uses simple six-spoke white flakes, and hail uses pale-blue solid round pellets with short motion ticks. A yellow lightning bolt occupies a reserved central gap. The clear-sky marker is a small sparkle, and unavailable cloud data uses a question mark.

Rain and hail increase from two to four to six marks. Snow increases from one to two to four larger flakes, preserving more of its distinct shape at the 24-pixel HUD size. Fog increases from one to three strokes. Thunder increases bolt breadth. Cloud coverage changes canopy width while keeping its baseline fixed. Intensity distinctions and precipitation types therefore do not depend solely on colour.

The 21 active SVG sources contain the paths, colours, and contrasting outlines directly on a shared `0 0 64 64` view box. Each declares `preserveAspectRatio="xMidYMid meet"` and uses explicit fills and rounded strokes, without stylesheet rules, clones, masks, or filters. Edit these source assets directly. The existing 64 × 64 PNG textures remain the runtime assets; changing an SVG alone does not update its PNG counterpart.

