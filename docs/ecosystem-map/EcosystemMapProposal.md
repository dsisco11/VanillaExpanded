# Ecosystem map overlay proposal

Status: client-side habitat evaluation with server-supplied original shrub arrays and omission of backfill approved; remaining design decisions are for review. This document describes the intended behavior and proposed integration boundaries. It is not an implementation checklist.

## Intended result

Add an **Ecosystem** toggle tab to the world map. While active, it displays a dropdown of unique wild animal species. Selecting a species highlights the mapped areas whose regional environment satisfies that species' habitat requirements.

The highlight uses world-generation temperature, rainfall, forest density, shrub density, and elevation. It communicates suitable habitat rather than sightings or the presence of living animals.

The agreed scope is:

- Show the Ecosystem controls and habitat highlight only on the full world map; exclude the minimap.

- Include all naturally spawning wild mammals and birds defined by the base game, with one dropdown entry per species combining age, sex, and cosmetic variants.
- Exclude fish, insects, supernatural creatures, and domestic-only variants. Mod-added species are deferred to a follow-up.
- Omit backfill. Capture environmental coverage from data available during normal play, including already loaded areas, and persist it for later use.
- Evaluate regional environmental requirements using worldgen temperature, rainfall, forest density, and shrub density, with terrain elevation checked separately. Current weather and seasonal climate do not drive habitat eligibility.
- Ignore water, ground, clearance, light, population caps, player distance, and spawn chance.
- Use the existing map layer system with client-side habitat evaluation and persistence. Receive the server's original regional shrub arrays through the existing map-layer transport; this requires a server component for shrub delivery.
- Use client-local animal definitions as the baseline catalogue. A supplemental server-supplied catalogue is deferred to a follow-up.

```mermaid
flowchart LR
    Map[World map] --> Tab[Ecosystem tab active]
    Tab --> Species[Unique-species dropdown]
    Species --> Selection[Selected species]
    Selection --> Evaluation[Evaluate environmental requirements]
    Environment[Cached regional environment and elevation] --> Evaluation
    Evaluation --> Highlight[Habitat highlight over mapped terrain]
```

## Player interaction

The Ecosystem tab follows the existing map's toggle-layer behavior. Its dropdown is visible while the tab is active. The selected species controls one translucent highlight, with terrain, landmarks, and other map layers remaining readable.

Changing the selection uses cached environmental data locally. Panning or zooming prepares highlights from cached data and any normally received world data available for the displayed mapped area. It does not request remote environmental backfill. Rendering consumes prepared highlight textures rather than performing world queries each frame.

Proposed presentation behavior:

- Sort species by their localized display names and retain the current selection while the map is recomposed or reopened within the session.
- Render highlights only over explored map coverage, even when environmental records cover a larger region.
- Distinguish unavailable environmental data from a confirmed habitat mismatch. A short loading or unavailable indication in the layer panel can provide this distinction without colouring unexplored terrain.
- Keep the dropdown attached to the full world map, following the existing extra-panel mechanism. Render the habitat highlight only in the full map, even when the Ecosystem layer is active while the minimap is visible.

## Verified integration points

Inspection of the installed game assemblies established the following behavior. These findings describe the inspected version; they should be checked against the target game version during implementation.

| Integration point | Observed behavior | Consequence |
|---|---|---|
| `WorldMapManager.RegisterMapLayer` and `LayerGroupCode` | Registered layer groups supply map toggle tabs. | Ecosystem can be a normal map layer. |
| `MapLayer.ComposeDialogExtras` | Layers can supply an extra GUI composer; the world map enables it with the corresponding tab. | The species dropdown can follow the prospecting layer's existing pattern. |
| `MapLayerData` | Contains a registered layer code and an opaque byte-array payload. | Route original shrub-array payloads to the Ecosystem layer. |
| `MapLayerUpdate` | Carries an array of `MapLayerData` messages. | Reuse this envelope for shrub delivery without a new outer packet type. |
| `MapPieceDB` | Stores rendered terrain pixels. | Saved map images do not retain the original elevation or climate arrays. |
| `MapDB.SetMapPieces` | Replaces the serialized row when a terrain tile is saved. | Extra fields appended to the vanilla record would need preservation patches. |
| Client region packets | Supply climate and forest maps, but omit shrub maps. | Send the server's existing shrub map separately, preserving its array and layout metadata. |
| Client map chunks | Supply rain and world-generation terrain heightmaps. | Elevation can be captured while map chunks are available. |
| Client entity packets | Supply entity identity and variants, but omit server spawn conditions. | Use local animal assets for the baseline catalogue; packet omission does not establish that those assets are unavailable. |

The [prospecting layer](../../../../vssurvivalmod/Systems/Prospecting/OreMapLayer/OreMapLayer.cs) demonstrates dropdown composition and map-layer data delivery. The shared contracts are available in [SpawnConditions.cs](../../../../vsapi/Common/Entity/SpawnConditions.cs), [IMapRegion.cs](../../../../vsapi/Common/API/IMapRegion.cs), and [IMapChunk.cs](../../../../vsapi/Common/API/IMapChunk.cs).

## Species catalogue and habitat rules

Build the initial catalogue of base-game wild mammals and birds from client-local animal definitions, including locally available asset patches for those supported species. Verify access to patched assets and reuse of the game's variant resolution; fully resolved spawn conditions on client runtime entities are not assumed. Define stable species identities independently of localized names and individual entity codes.

Local knowledge is the baseline and does not depend on a server catalogue. Server-only changes may differ from that baseline. A supplemental server-supplied catalogue is deferred to a follow-up; its protocol, precedence, and reconciliation rules are outside the initial implementation.

For animals such as deer, retain the species `type` and combine `age` and `gender` variants. Names such as Elk, Caribou, and Fallow Deer remain separate species entries. Some included families need explicit normalization: chickens encode sex in their age variants. Combine cosmetic variants under their species identity as well. A generic entity-code suffix removal is insufficient.

Use a narrowly owned species mapping for supported animals. Do not classify every land entity as wildlife or assume every `type` variant represents a separate species. The approved catalogue includes all naturally spawning base-game wild mammal and bird species. Exclude fish, insects, supernatural creatures, and domestic-only variants. Support for mod-added species is deferred until their species identities can be supplied reliably; local patches to supported base-game species remain applicable.

Each species entry contains the relevant environmental limits and the entity identities from which they were derived. Where one species genuinely has multiple habitat rule sets, preserve those alternatives. A sample matches if it satisfies a complete applicable rule set; do not merge separate ranges into a larger interval that invents suitable habitat between them.

The environmental predicate covers:

| Input | Comparison |
|---|---|
| Regional temperature | Minimum and maximum temperature |
| Regional rainfall | Minimum and maximum rainfall |
| Forest density | Minimum and maximum forest density |
| Shrub density | Minimum and maximum shrub density |
| Forest or shrubs | `max(forest, shrubs) >= MinForestOrShrubs` |
| Elevation | Minimum and maximum normalized elevation |

Use the game's elevation convention: below sea level, `y / seaLevel`; above sea level, `1 + (y - seaLevel) / (worldHeight - seaLevel)`. The exact terrain sample and surface offset must be consistent with the selected habitat semantics.

### Regional climate semantics

The World API exposes efficient regional climate lookup, but calling `GetClimateAt` at the surface is not automatically equivalent to animal habitat evaluation.

The inspected runtime animal spawner evaluates climate at a reference height of approximately `seaLevel * 1.09`, then checks actual elevation separately. Worldgen spawning uses a similar reference for temperature, but reads rainfall differently. Client and server temperature conversion also have different lower clamps. Reuse the appropriate game conversion and sampling conventions rather than the player's displayed temperature.

Use worldgen environmental values for habitat eligibility. The inspected base-game animal definitions retain the default `ClimateValueMode.WorldGenValues`; temperature matching reads `WorldGenTemperature`, and ongoing spawning samples generated forest and shrub maps. Spawning during world generation and spawning during ongoing play both use generated environmental values. They are not alternative sources of seasonal or current-weather data.

Preserve any applicable environmental rule alternatives and reconcile differences in vanilla sampling against the agreed behavior: highlight where the species can spawn under those environmental requirements. This is an implementation reconciliation, not an outstanding player-facing policy choice. Do not introduce a worldgen/runtime mode selector.

The client climate lookup can return placeholder values when regional data is missing. Validate source availability explicitly; placeholders and absent shrub data must never become valid habitat samples.

## Client-side environmental acquisition

Use normally received climate, forest, and elevation data alongside client-local animal definitions. Receive the original shrub-density array from the server's existing `IMapRegion.ShrubMap`, then cache it on the client. Habitat evaluation and rendering remain client-side.

Do not reconstruct shrub maps locally or duplicate the vanilla generator's initialization and orchestration calls. The inspected vanilla code splits that orchestration between `GenMaps.initWorldGen()` and the private `OnMapRegionGen(...)`; it does not expose a standalone shrub-map operation with a client-compatible context. Moreover, forced climate changes can occur after shrub generation, so the transmitted climate map is not always the original shrub-generator input.

The server component reads the existing regional shrub map rather than generating a replacement. The payload preserves the region coordinates, original integer array, map size, top-left padding, and bottom-right padding, together with a payload version. Preserve the original values and sampling layout rather than sending a resampled approximation.

Missing shrub data remains unavailable until received or recovered from this world's cache. Do not substitute zero density or a locally regenerated array. The initial feature therefore requires server shrub-delivery support to populate uncached shrub coverage. This dependency is separate from the deferred server species catalogue and does not change installation requirements for unrelated mod features.

## Data ownership and transport

Keep registration thin. Species classification, environmental acquisition, server shrub delivery, storage, habitat evaluation, and presentation each have a separate owner. The renderer consumes prepared results and does not own persistence or species policy.

```mermaid
flowchart TB
    Normal[Normal game region and chunk delivery] --> LocalMaps[Client climate, forest, and height data]
    subgraph Server
        Original[Existing regional ShrubMap] --> Snapshot[Original array and layout snapshot]
        Snapshot --> Delivery[Ecosystem map-layer delivery]
    end
    Delivery --> Envelope[MapLayerData inside MapLayerUpdate]
    Envelope --> Receive[Client Ecosystem OnDataFromServer]
    subgraph Client
        Definitions[Local animal definitions] --> Rules[Baseline species habitat catalogue]
        LocalMaps --> Cache[Environmental cache]
        Receive --> Cache
        Cache --> Evaluate[Habitat evaluator]
        Rules --> Evaluate
        Selected[Selected species] --> Evaluate
        Evaluate --> Prepared[Prepared highlight tiles]
        Coverage[Explored map coverage] --> Render[Map overlay renderer]
        Prepared --> Render
    end
```

Use `SendMapDataToClient` and the layer's `OnDataFromServer` callback for the versioned shrub payload. `MapLayerData` provides routing; `MapLayerUpdate` provides the existing envelope. The feature owns decoding, completeness checks, and persistence. Capture the server array safely before asynchronous serialization, and avoid resending unchanged regions unnecessarily.

Coordinate shrub delivery with regions supplied during ordinary exploration, including relevant regions already loaded when the feature initializes. Delivery must not depend solely on opening the Ecosystem tab. Map-open and view-change callbacks can supplement this flow for available regions; they must not load or generate remote regions to fill missing ecosystem coverage. The precise delivery hook remains an implementation detail to verify. If requests are needed, account for the fact that the inspected `OnDataFromClient(byte[])` callback omits sender identity, whereas server map-open and view-change callbacks receive the player.

Persist normally received inputs and the original shrub maps for use after regions unload. Missing climate or elevation remains unavailable; shrub delivery alone cannot recover those inputs from old saved map images.

The species catalogue continues to use client-local animal definitions. A supplemental server catalogue remains deferred to a follow-up; original shrub delivery is part of the initial implementation and does not introduce catalogue synchronization.

## Persistence in a dedicated ecosystem database

Maintain a separate client-local SQLite database file owned by the Ecosystem feature and scoped to the current server/world identity. The feature owns its connection, schema, transactions, and lifecycle. Do not open, modify, or extend vanilla's map database file, connection, tables, or `MapPieceDB` serialization.

Store environmental data at its natural granularity: climate and vegetation arrays once per region, with elevation and tile-specific completeness per map piece. Preserve array size and padding metadata alongside the original values. Concrete type, table, and file names remain implementation choices.

```mermaid
erDiagram
    ECOSYSTEM_MAP_PIECE {
        long position PK
        long region_position FK
        int schema_version
        blob elevation_data
        int completeness
    }
    ECOSYSTEM_REGION {
        long position PK
        int schema_version
        blob climate_data
        blob forest_data
        blob shrub_data
        int completeness
    }
    ECOSYSTEM_REGION ||--o{ ECOSYSTEM_MAP_PIECE : "regional inputs"
```

Both tables belong exclusively to the ecosystem database. Tile coordinates associate environmental records with displayed terrain in memory; there is no cross-database join or foreign-key dependency on vanilla's map tables. Partial records may exist while other inputs are arriving.

Vanilla terrain redraws and pixel-record replacement do not affect ecosystem records. The stored environmental inputs remain reusable when the player selects another species. Persist normally received climate, forest, and elevation as well as separately delivered shrubs; vanilla's in-memory environmental arrays are not retained by its pixel cache.

Serialize database access through the feature's storage owner, perform disk work off the render thread, and use bounded transactions. Incoming data is queued as safe snapshots. World exit must finish or safely cancel outstanding work, flush pending writes as appropriate, and close the feature-owned connection before releasing world-specific state. Keep records isolated between server/world identities.

Coordinate ecosystem cache clearing with map purge without accessing vanilla's database: clear the feature's own records, pending writes, and derived highlights. Continue clipping highlights to currently explored map coverage. Version environmental records explicitly, and invalidate derived highlight tiles when inputs or the habitat catalogue change. Avoid storing an independent permanent mask for every species when it can be rebuilt from shared environmental inputs.

## Coverage, backfill, and historical compatibility

Capture and persist environmental data as areas become available through ordinary play, so the overlay continues working after their world chunks unload. Acquisition should not depend solely on the Ecosystem tab being open. Also capture relevant regions and map chunks already loaded when the feature initializes.

**Backfill** means obtaining missing environmental inputs for areas already drawn in the saved map before this feature captured them, without requiring the player to revisit those areas. For example, a distant valley may remain visible as a saved map image while its climate and height arrays are no longer available locally. The pixel-only image cannot reconstruct those inputs. Receiving a shrub array alone does not recover missing climate or elevation.

Without backfill, existing worlds remain usable: areas available during normal play gain ecosystem coverage, and revisiting an old mapped area supplies its inputs through normal game loading. Once captured, that coverage persists across travel and reconnects. Old uncached areas remain visible on the ordinary map but have unavailable ecosystem coverage until their required inputs become available. Panning over an old map image alone does not load those inputs.

**Historical shrub-generation compatibility** requires no client reproduction of old algorithms: the server supplies the shrub array already held by the region, including historical or server-specific results. For an unloaded old region, capture its shrub record when the region becomes available through normal play.

Approved scope: omit backfill. Do not add saved-record retrieval, remote region loading, or terrain generation to populate old map-only coverage. Background processing may evaluate and cache available environmental inputs for mapped areas; it does not trigger world loading to obtain missing inputs.

Clip the highlight to explored coverage even when a cached regional array extends beyond it.

## Performance and lifecycle expectations

Evaluate bounded batches of visible map samples, reuse regional arrays, and cache prepared highlight tiles. Selecting another species invalidates the derived highlight rather than reloading the environmental source data. Zooming may require a different presentation resolution, but does not change the underlying habitat predicate.

Choose sampling density against habitat-boundary accuracy, particularly where elevation changes rapidly. Avoid marking a whole chunk suitable from a single centre sample. Exact sampling resolution, cache limits, and batch sizes require measurement rather than fixed speculative budgets.

Snapshot data before background evaluation where game-owned arrays or API access cannot safely be shared. Upload and release rendering resources on the appropriate client thread. Closing the map stops unnecessary presentation work; world exit closes storage and clears world-specific state.

## Validation expectations

Validation should demonstrate local catalogue construction and use without any server catalogue message, locally patched definitions and variant resolution, species deduplication across age, sex, and cosmetic variants, coverage of naturally spawning base-game wild mammals and birds, exclusion of fish, insects, supernatural creatures, domestic-only variants, and mod-added species, complete environmental-rule matching, boundary handling, independence from current weather and seasonal climate, and unknown-data behavior. Compare representative results with the inspected game habitat calculations, including reference-height climate and normalized elevation.

Exercise persistence across tile redraw, map close/reopen, region unload, reconnect, purge, and version changes. Verify server/world isolation, pending-write handling at shutdown and purge, and that ecosystem storage never opens or modifies the vanilla map database. Verify that panning over old uncached map images does not trigger saved-record retrieval or world loading, and that ordinary revisiting populates and retains ecosystem coverage. Confirm that server shrub arrays survive transport and persistence with unchanged values, size, padding, and region identity. Verify missing-server-support and missing-record behavior, correct routing to the Ecosystem layer, and that old map images without environmental records remain distinguishable from unsuitable habitat. Include a region with forced climate changes to ensure the existing shrub array is delivered rather than recomputed from the final climate map.

Measure map opening, panning, species switching, cache size, and client environmental capture and server shrub delivery with representative explored maps. Focused automated checks cannot establish visual quality or live performance. In-game acceptance should confirm readable terrain, correct dropdown visibility, stable highlights, and no reveal beyond mapped coverage. Verify that neither Ecosystem controls nor habitat highlights appear on the minimap, including while the layer is active and after closing the full map.

## Decisions remaining before an implementation plan

Database ownership is settled: use a dedicated client-local ecosystem database. No further product-scope decisions are currently identified; concrete storage and lifecycle hooks remain implementation details to verify.

## Evidence references

The analysis used local API and survival-mod source plus targeted decompilation of the installed `VSEssentials.dll` and `VintagestoryLib.dll`. Relevant installed symbols are:

- `WorldMapManager`: layer registration, tab grouping, network envelopes, and receive dispatch.
- `GuiDialogWorldMap`: tab activation and extra-composer visibility.
- `MapLayerData`, `MapLayerUpdate`, and `MapLayer`: transport envelope and layer callbacks.
- `ChunkMapLayer`, `MapDB`, and `MapPieceDB`: client-local cache, tile replacement, and pixel-only persistence.
- `ClientWorldMap.GetClimate` / `GetClimateAt`: cached sampling and placeholder fallback.
- `ClientMapRegion.UpdateFromPacket`, `ClientMapChunk.UpdateFromPacket`, and `EntityTypeNet`: actual client data availability.
- `ServerSystemEntitySpawner.GetSuitableClimateTemperatureRainfall` and `GenCreatures`: habitat sampling conventions.
- `GenMaps.initWorldGen`, `GenMaps.OnMapRegionGen`, and `GenMaps.ForceClimate`: server-dependent orchestration and climate modification after shrub generation.
- `ServerMapRegion` and `ClientMapRegion`: original shrub-map ownership and omission from normal region packets.

The temporary decompiled analysis files are not repository dependencies. Durable API references include [climate conversion](../../../../vsapi/Common/Climate.cs), [world lookup contracts](../../../../vsapi/Common/API/IBlockAccessor.cs), [server world-manager access](../../../../vsapi/Server/API/IWorldManagerAPI.cs), and [SQLite connection behavior](../../../../vsapi/Common/IO/SQLiteDBConnection.cs).
