# GPU habitat evaluation

The CPU captures and persists original environmental arrays, reads immutable local snapshots and uploads integer textures. It does not sample terrain columns into suitability grids or generate overlay pixels. `GpuHabitatPreparation` owns bounded local reads and reusable upload bundles; `GpuHabitatOverlay` runs the comparison shader on the graphics thread.

## One selected criteria set

`HabitatRanges` contains one active min/max pair for temperature, rainfall, forest, shrubs and elevation, plus MinForestOrShrubs and one sampling convention. `From(HabitatRule)` copies an explicitly chosen definition set. It does not union ranges, combine alternatives or send species/rule arrays to the GPU. Species selection changes comparison uniforms while retaining environmental textures. The catalogue retains definition provenance separately; the controls prefer ongoing-spawn ranges, fall back to worldgen ranges, and select a representative by ordinal entity code within the chosen path.

```mermaid
flowchart LR
    Capture[Original climate forest shrubs heights] --> Storage[Dedicated local database]
    Storage --> Bundles[Bounded original input bundles]
    Bundles --> Upload[Graphics-thread integer textures]
    Selection[One selected range set] --> Uniforms[Comparison uniforms]
    Upload --> Shader[GPU sampling and comparisons]
    Uniforms --> Shader
    Coverage[Current explored mask] --> Shader
    Shader --> Output[Highlight or unavailable crosshatching]
```

## GPU sampling and output

Regional textures preserve original signed 32-bit climate/vegetation values in R32I. Heights use R16UI; R8UI coverage packs exploration in bit 0, known non-ocean in bit 1 and confirmed ocean in bit 2. Packing raw categories does not evaluate habitat. Regional textures can be shared across terrain tiles; tile textures own heights and mutable coverage. Nearest filtering and texelFetch leave interpolation to the shader, which respects each source map's size and padding.

The shader samples generated maps with the active set's worldgen or ongoing-spawn convention. It uses the server temperature clamp, reference-height rainfall conversion, generated terrain height plus one, and normalized elevation. CPU-provided reference-height integers and conversion scale are world metadata, not per-column evaluation. The shader performs all habitat comparisons.

The overlay participates in native GUI depth testing and depth writes at Z=55, between terrain at Z=50 and prospecting/owned-creature markers at Z=60. It retains the caller's depth function and restores depth-test/write state after drawing. Discarded columns do not write depth.

A known mismatch discards the fragment. Missing required inputs produce crosshatching; complete matching inputs produce the highlight. An unexplored column is discarded first. Confirmed ocean is then discarded before habitat sampling or crosshatching, even when height/climate/vegetation are missing. Unknown ocean classification contributes to unavailable-data output; known non-ocean includes freshwater and land below sea level. The backend supplies display colors and retains the map scissor; the presentation owner admits only active full-map explored tiles. Normalized elevation limits are converted once per criteria/world-geometry change into inclusive integer surface-height bounds using the server double-precision convention. The GPU samples terrain height plus one and compares it with those bounds, avoiding float division at elevation thresholds. This converts scalar criteria only; no terrain columns are sampled on the CPU.

## Ownership and validity

`GpuHabitatPreparation` has no species input. Its off-view cache floors are eight regional snapshots, 128 elevation records and 128 lightweight upload bundles. During rendering, retention expands to fit every currently visible explored chunk and its distinct regions, preventing still-visible inputs from being evicted at wider zooms. Admission remains four queued read jobs plus one executing job. Closing the map shrinks input retention back to the configured floors. Representative workload tuning and GPU resource measurements remain pending. No per-column comparison batch or prepared suitability-mask cache exists on the CPU.

Committed storage updates invalidate only the affected source leases/bundles. View membership is separate from source and presentation-lifetime validity: panning and zooming retain current overlapping receipts and useful queued jobs. Consumers must call IsCurrent before uploading or publishing resources; this also checks membership in the latest explored view. Purge resets bundles and source leases. Session shutdown stops acquisition, joins local input work, drains persistent writes and closes storage.

GpuIntegerTexture, GpuRegionalTextures, GpuTileTextures and GpuHabitatOverlay allocate/dispose on the graphics thread. Upload preserves unpack alignment, row length, row/pixel skips, buffer and texture binding; one shared drawing pass restores shader, vertex-array/array-buffer bindings, texture units and sampler-object bindings, blend, culling and depth state and preserves the caller's scissor/framebuffer. After compilation, `GpuHabitatSamplers` queries the linked locations for all five signed/unsigned integer samplers and completes the native uniform dictionary before ordinary engine texture binding. Reload replaces a linked program only after compilation and sampler registration succeed. A successful normal shader reload clears a presentation failure so drawing can resume.

The graphics backend is separate from map controls/resource caching. The native layer routes composition and rendering to the client presentation owner, which supplies current applicability, shared texture ownership and normal shader reload/disposal. The registration owner stops presentation before closing the client session, independent of leave-world subscriber ordering. Focused tests establish input preservation and uniform forwarding. Rendering and resource lifecycle verification belongs to normal game integration; visual acceptance remains user-run.

## Map presentation owners

`EcosystemMapSelection` sorts native localized species names and retains a stable selected code across composer replacement. `EcosystemMapControls` owns the extra panel and requires the exact composed full-map element. `MapComposerOwnership` detaches only that panel entry before disposal, using a checked adapter to the native collection; replacements remain untouched. `ExploredMapCoverage` reads only active vanilla terrain components and their public per-chunk flags, filtering them to the current view. It never opens the map database or treats visible coordinates alone as explored coverage.

`HabitatMapPresentation` polls bounded local preparation jobs without awaiting them inside rendering. It copies the native projection into a reusable owned 16-float buffer once per render before any tile draw: the installed native projection and model-view getters share scratch storage, so retaining their returned array across draws would overwrite the projection when model-view is read. The shared pass receives the stable projection snapshot and copies model-view into its own reusable buffer before binding both matrices. It rejects stale receipts before graphics publication and sends one selected criteria set to `HabitatMapGraphics`. That graphics owner retains original textures across species changes, sizing retention to the complete current explored view with eight regional/128 terrain entries as minimum cache capacities. View shrink releases excess off-view textures first. These capacities are provisional; they do not cap how much of the visible map can receive the overlay. Closing presentation revokes its lifetime, releases textures and cancels disposable jobs while acquisition and durable writes continue. `EcosystemMapClient` coordinates composer replacement, shader events and render-thread purge observation through these owners.
Integer inputs use their own nearest texture filtering during drawing rather than inheriting a potentially incompatible sampler object.

## Shared graphics pass

`HabitatMapGraphics` acquires all missing textures before opening a draw pass. No texture allocation, eviction or upload occurs inside the pass, so cached regional bindings remain valid. The pass captures caller state, suspends the previous shader, establishes common depth/blend/cull/sampler state and binds the criteria, scalar elevation bounds, world metadata, matrices and colors once. Tile draws update heights/coverage, screen bounds and world origin; regional textures, layouts and availability bind only when the regional texture owner changes. Tracking resets for every pass. Empty or fully revoked ready sets open no pass, and disposal restores caller state once even when setup or drawing throws. Leases are checked after acquisition and before each submission.

## Incremental view retention

The current explored chunk set determines applicability; current screen positions and zoom arrive independently on every render. Changing that set updates membership without clearing receipts or replacing their lifetime token. Valid overlapping receipts remain drawable in the first changed-view frame. Entering tiles reuse retained bundles or enter the bounded local-read queue; no speculative prefetch or remote retrieval occurs. Departed tiles never reach upload or drawing. Retained off-view receipts can be reused on re-entry only while their exact source leases remain current.

Every source, bundle, receipt and texture cache installs the new visible working set before shrinking. Eviction selects off-view entries before protected visible entries, irrespective of LRU recency or upload order. Source caches use coordinates; texture caches use coordinates plus the exact regional/tile source lease, excluding invalid leases from protection. Capacities are the maximum of configured floors and the finite visible working set. A late off-view admission can evict itself when all other entries are protected. Thus an unchanged visible input cannot be displaced by entering or obsolete work. Old invalid source textures remain disposable, never eligible for a current draw.

The queue still admits four waiting jobs and one executing job. Jobs check the presentation lifetime and current membership before starting, between regional and tile reads and before publication. Queued departed work completes without further reads; a tile that re-enters before those checks can use the original job. Presentation retains incomplete tasks across view transitions, observes completed off-view results without publishing them, and retries bounded admission on subsequent frames. A regional read already in progress may finish after departure, but cannot revoke protected inputs or publish an inapplicable result. Source updates, ocean updates, purge and world shutdown retain their independent validity checks.

Coverage removal is enforced by the current explored chunk set even when a source remains cached. Coverage re-entry with unchanged source provenance reuses the existing height/coverage/ocean textures; per-frame GPU comparisons and the shared graphics pass continue normally. Close clears disposable receipt/graphics ownership and revokes its lifetime, while local source retention returns to the configured floors. Reopen may reuse those original sources but acquires fresh presentation resources. Shader reload does not invalidate environmental source identities.
