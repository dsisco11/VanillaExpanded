# Landscape clock proposal

Design direction superseded by the [separate copper HUD gauges proposal](../hud-overlays/SeparateGaugesProposal.md): the clock is now a 24-segment radial dial, with independent temperature and calendar gauges. The landscape design below is retained for historical reference.

Status: proposal for review. Bold silhouettes are the selected art direction. This document does not authorize production artwork or runtime changes.

## Intended result

Replace the rotating scenery disk with an upright, layered landscape inside the existing circular copper housing. Keep the red rim thermometer and normal-sized temperature reading. The scene communicates estimated solar time and approximately reflects the player's surface environment. It is an illustrative landscape, not a miniature rendering of the surrounding world.

The current clock footprint remains the shared Clock palette size. The existing top-right anchoring, fade/collapse behavior, texture filtering, and temperature configuration remain applicable.

## Verified inputs and their limits

The local API source exposes these fields on ClimateCondition in [IBlockAccessor.cs](../../../vsapi/Common/API/IBlockAccessor.cs):

| Input | Documented meaning | Proposed use |
|---|---|---|
| WorldGenTemperature | World-generation temperature retained when reading current values | Stable climate family |
| WorldgenRainfall | World-generation rainfall, 0–1, retained with current values | Aridity family |
| ForestDensity / ShrubDensity | Normalized 0–1 densities | Vegetation silhouette selection |
| Temperature | Current local temperature | Existing thermometer only |
| Rainfall | Current precipitation when reading NowValues | Future weather contribution, not stable environment classification |
| Biome | Integer, with -1 meaning no biome | Optional future explicit mapping after verifying IDs and client availability |
| GetTerrainMapheightAt | Terrain height query | Surface sample location and coarse relief detection |
| GetSunPosition | Existing clock queries the solar position for an estimated calendar day | Celestial path and lighting |

API declarations establish available contracts, not the installed client's actual field population or a dependable biome taxonomy. Verify that in representative worlds before implementation. Do not infer tree species, wetlands, rivers, or deserts solely from a biome integer or current rainfall.

## Scene composition

Use independent authored texture masks for distant terrain, middle vegetation, and foreground. The shader supplies colors. Sky gradients, sun, moon, and restrained stars sit behind these layers. All landscape layers remain upright.

Keep a large sky opening and recognizable, sparse silhouettes. Avoid small branches, grass blades, or intricate rock shading that disappear at HUD size. Terrain depth comes from distinct layer colors; no shadow maps or calculated cast shadows are proposed.

The renderer consumes a prepared scene description. It does not query climate, terrain, blocks, or the calendar during drawing. Housing, thermometer, and landscape should remain separate responsibilities.

## Initial environment vocabulary

Approved direction: select terrain and vegetation independently, with climate influencing appearance. Avoid a separate preset or texture for every combination.

The initial scene represents the generated surface environment, not a scan of current visible vegetation. Forest and shrub density provide stable, inexpensive classification inputs. Clearing trees or planting a garden therefore does not immediately change the landscape artwork. Local vegetation evidence is a possible later extension, requiring separate sampling-cost and built-environment rules.

| Dimension | Initial choices | Selection basis |
|---|---|---|
| Terrain | Gentle hills; rugged ridges | A small bounded set of terrain-height samples and local relief |
| Vegetation | Open land; scrub; sparse woodland; dense woodland | Forest and shrub density |
| Climate appearance | Temperate; cold; arid | World-generation temperature and rainfall |

Tree-species selection is not approved as a reliable inference from forest density. Conifer versus broadleaf silhouettes require additional evidence or a separately reviewed, explicitly approximate climate-based artistic rule. Arid appearance uses dry ground and sparse vegetation, not automatic dunes. Cold climate does not automatically imply current snow coverage; future snow appearance requires actual coverage evidence. Terrain-height variation identifies ruggedness, not absolute elevation: a high plateau can still select gentle terrain.

Wetlands, coastlines, tropical vegetation, explicit snow coverage, and accurately identified tree types are later candidates requiring additional evidence. A neutral hills-and-sparse-vegetation scene is the safe initial fallback.

Exact thresholds are not fixed here. Verify sample distributions first, then choose and review thresholds and boundary hysteresis. Do not invent precise biome boundaries from undocumented ranges.

## Sampling and transitions

Keep time and thermometer sampling on the existing 300 ms refresh schedule. Evaluate environment on horizontal movement into a new sampling cell or after a slower retry interval; proposed defaults are 16-block cells and a one-second retry. Use a bounded terrain footprint, initially five height samples within 32 blocks, rather than scanning chunks or searching vegetation blocks.

Accept a changed classification only after it remains the candidate for two seconds. Crossfade the old and new layer compositions over one second. If another change arrives during a transition, complete the current fade and retain only the latest candidate; do not allocate an unbounded sequence of scene snapshots.

Those timing and footprint values are proposed tuning defaults. Classification stability must be tested at ordinary biome edges and during fast movement.

Sample climate at the known surface terrain height, rather than allowing player depth to select a different family. Under surface cover or underground, retain the last accepted surface scene. On a fresh underground session, use valid surface data if available; otherwise show the neutral fallback. Missing chunks, invalid values, or temporary failures retain the last scene and retry. World exit clears remembered scenery.

Keep cached masks and crossfade state independent of temperature text caching. Load texture variants at preparation boundaries, not inside the draw callback.

## Time and lighting

Drive the sky and terrain lighting from the existing estimated solar position, using the same observation-age drift as the clock. Temperature remains a real current climate reading.

| Solar state | Visual treatment |
|---|---|
| Sunrise | Sun enters from the left; warm left horizon, cool dark terrain becoming lighter |
| Day | Sun travels across the upper sky; blue sky and separated blue/green terrain layers |
| Sunset | Sun exits on the right; warm right horizon, foreground darkens |
| Night | Deep blue sky, moon and sparse stars, faint cool separation between silhouettes |

Left sunrise and right sunset are fixed instrument conventions, not the player's facing direction. Use solar height and rising/falling direction to blend palettes continuously, rather than abrupt period changes or a fixed six-hour sunrise assumption. Preserve seasonal and polar behavior: sun below the window, persistent twilight, and prolonged day/night must have meaningful imagery.

Determine moon placement and visibility from an explicit artistic path consistent with estimated time; do not claim an accurate lunar ephemeris unless separately implemented and verified. Sun position, terrain tint, and palette transitions should agree. Missing solar input retains the last usable time presentation under the existing clock contract.

## Extensions

Each contributor has a narrow, ordered role:

1. Environment selects terrain and vegetation masks.
2. Season may select foliage variants and apply ground/vegetation snow coverage.
3. Estimated solar time supplies the base sky, celestial positions, and terrain colors.
4. Weather may attenuate lighting and add clouds, fog, or precipitation.
5. Housing and thermometer overlay the completed landscape.

Define a prepared scene description that can carry optional seasonal and weather contributions, but implement only selected initial contributors. Do not add an unrestricted effect registry before actual extension needs exist.

Use actual seasonal or snow evidence for future coverage, not simply current temperature. Weather remains subordinate to time readability, and the separate weather indicator continues communicating precise states. Extreme weather must not fully obscure the sky or eliminate the day/night distinction.

## Configuration and ownership

Preserve the current approximate-time and environmental-temperature toggles. Temperature disabled means the clock-only housing; clock disabled means the standalone temperature indicator. Changing the scene does not change observation tracking or server behavior.

Borrow engine-cached artwork, own shader/quad/text resources, restore enclosing GPU state, and preserve successful shader resources across failed reloads. World exit and final removal release owned resources. Environment data belongs to the sampler/classifier; the presentation receives prepared values.

## Review and validation

Before production artwork or runtime implementation:

- Verify installed client climate and terrain inputs, including unloaded chunks and underground sampling.
- Agree on initial environment families and derived classification thresholds.
- Review a bold-silhouette sheet covering temperate woodland, open land, cold woodland, and arid scrub, each at sunrise, noon, sunset, and night.
- Include previews at the actual GUI footprint, with the current circular housing and thermometer.

Implementation validation should cover solar/polar states, independent climate sampling, classifier hysteresis, interrupted crossfades, missing data, toggles, caching, reloads, and disposal. Focused headless checks establish contracts; the user verifies live legibility, asset alignment, transitions, and rendering cost in game.
