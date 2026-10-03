# Mesh-aware quick-swap icon fitting

Status: proposed; implementation and runtime validation are outstanding.

Date: 2026-10-03.

## Purpose

Fit complete quick-swap item models inside their radial menu wedges. Measure mesh vertices in the engine's GUI projection, preserve the normal orientation when it fits, and otherwise calculate a screen-space rotation, uniform scale, and translation.

The user requires geometry-based measurement. Pixel scanning, alpha-mask readback, and framebuffer analysis must not determine bounds. The existing framebuffer remains responsible for rendering the icon and its halo only.

This proposal changes icon presentation. Candidate selection, equipment operations, entry ordering, input handling, and menu hit testing retain their current owners and behavior. Tool-mode artwork and center labels do not opt into item fitting automatically.

## Current behavior and evidence

The investigation inspected repository source and the installed `G:/Vintagestory/VintagestoryLib.dll` and API assembly, version 1.22.7. Engine findings below come from assembly metadata and IL inspection, not an executed rendering experiment. Recheck hook locations against the installed assembly during implementation.

| Component | Observed behavior | Design implication |
| --- | --- | --- |
| [QuickToolItemIcon](../../VanillaExpanded/src/QuickTools/QuickToolItemIcon.cs) | Calls the engine GUI item renderer and applies a category-specific tool-head offset. | Successful whole-model fitting replaces that offset. |
| [RadialMenuLayout](../../VanillaExpanded/src/RadialMenu/RadialMenuLayout.cs) | Calculates a nominal square icon size from radial and angular capacity. | Retain this as the desired reference size, not proof of model containment. |
| [RadialMenuRenderer](../../VanillaExpanded/src/RadialMenu/RadialMenuRenderer.cs) | Positions icons, applies hover scaling, and clips captured content to the wedge stencil. | Supply the actual wedge geometry to fitting; retain stencil protection. |
| [RadialMenuIconHalo](../../VanillaExpanded/src/RadialMenu/RadialMenuIconHalo.cs) | Captures the engine-rendered icon before applying its pixel-distance halo. | Render fitted geometry into this capture; no second image transformation is necessary. |
| [MeshData](../../../vsapi/Client/Model/Mesh/MeshData.cs) | Exposes positions, valid vertex counts, indices, and update offsets. No reusable mesh-bounds calculator was found. | Retain owned geometry and calculate projected bounds locally. |
| [MeshRef and MultiTextureMeshRef](../../../vsapi/Client/API/MeshRef.cs) | Do not expose CPU positions or bounds. The installed `VAO` implementation also has no cached bounding box. | Associate retained geometry with mesh object identity. |
| `RenderAPIBase.UploadMultiTextureMesh` | Splits geometry by texture and calls the platform upload directly. | Patching only `RenderAPIBase.UploadMesh` misses this path. |
| `ShapeTesselatorManager` | Uploads default inventory block meshes, item meshes, and alternate item variants through `UploadMultiTextureMesh`. | Capture must be installed before these meshes are generated. |
| `InventoryItemRenderer.GetItemStackRenderInfo` | Resolves variants, transition state, slot callbacks, and collectible callbacks that can replace the mesh or transform. | Consume the result of the actual draw; do not make a second measurement call. |
| `InventoryItemRenderer.RenderItemstackToGui` | Builds item/block-specific GUI matrices before submitting shader uniforms and invoking the selected draw path. | Integrate at the completed matrix boundary. |
| `ClientPlatformWindows.UpdateMesh` | Supports partial position-buffer updates and updates with no positions. | Merge position updates or invalidate retained geometry; do not treat every update as replacement. |
| `TCTCache.UpdateChunkMinMax` and `TesselatedChunk.SetBounds` | Accumulate/store terrain chunk bounds. | These are not a general item-mesh bounds API. |

The generic [radial menu shader](../../VanillaExpanded/assets/vanillaexpanded/shaders/radial_menu.fsh) defines the visible rounded contour. The fitter must derive its safe region from that contour and [RadialMenuWedgeStyle](../../VanillaExpanded/src/RadialMenu/RadialMenuWedgeStyle.cs), rather than treating hit testing or square icon sizing as a containment oracle.

## Behavioral contract

For a supported mesh and valid wedge:

1. Measure the complete projected geometry at the current nominal icon size.
2. Try the normal orientation with geometric recentering and placement adjustment.
3. If necessary, search rotations and placements at that size.
4. If no tested orientation fits, reduce uniform scale and select the best feasible result from the bounded search.
5. Render with the resulting transform before halo generation.

Do not enlarge beyond the nominal size initially. Prefer the smallest absolute rotation among equally sized feasible results, then the placement nearest the wedge midpoint. Use deterministic tie breaking and tolerances so nearly equal solutions do not alternate between frames.

The fit result includes angle, positive uniform scale, translation, and a status distinguishing fitted, unsupported, invalid geometry, and insufficient space. An approximate search must not claim a globally optimal fit. Every accepted result must pass the conservative containment test.

When geometry or integration is unsupported, preserve the existing icon-rendering and stencil path, including its existing offset behavior. This fallback can clip and must never be reported as fully fitted. When the safe region has no usable area, report insufficient space and use that fallback. A mathematically contained icon is not necessarily readable at extremely small menu sizes.

## Geometry capture and lifetime

### Upload coverage

Use a narrow engine adapter around `UploadMultiTextureMesh` to establish a capture scope. Observe the platform uploads within that scope and associate each successful child `MeshRef` with an owned copy of its valid positions. Do not copy complete `MeshData` objects, texture data, or unused array capacity.

This arrangement follows the engine's texture splitting and supports updates to individual children. It avoids duplicating an aggregate position snapshot alongside the same child snapshots. It also avoids retaining every terrain and GUI mesh passing through the platform uploader.

The adapter must be installed early enough to observe inventory mesh creation even if quick-swap is initially disabled. Runtime enabling may reuse these observations. If tracking is absent or its cache was cleared while engine meshes remain alive, missing geometry remains unsupported until explicitly registered or uploaded again; default retessellation is not proof that a replacement mesh matches.

Provide an explicit registration boundary for manually assembled mesh references or cooperating custom renderers. Global capture of all uploads and GPU-buffer readback are outside this proposal.

### Owned data and invalidation

- Store position snapshots by `MeshRef` object identity, not OpenGL handle or item code. Handles can be reused.
- Respect the valid vertex count. Retaining all valid uploaded vertices is conservative even if some are not currently indexed; do not use unused buffer capacity.
- Give each snapshot a geometry revision. Aggregate identity includes every child reference and its revision.
- Observe platform updates only for tracked meshes. Merge known position ranges using the engine's actual destination-offset units and count semantics, which must be verified before implementation.
- Color, UV, and normal-only changes do not invalidate position geometry. Unsupported position updates, allocations without known complete contents, invalid ranges, or non-finite coordinates invalidate fitting for that mesh.
- A complete snapshot can remain conservative across index-only changes while all referenced positions remain known. Validate this assumption against the actual draw range; otherwise invalidate.
- Copy incoming positions while they are valid. Never retain a caller-owned array that can be pooled, mutated, or discarded after upload.
- Use weak mesh ownership and release entries on disposal when observable. Clear projected-fit caches on resource replacement and client shutdown without disposing engine-owned meshes.
- World transitions and reloads must not silently erase the only geometry snapshots for still-live engine meshes. Separate menu/fit-cache resets from mesh-snapshot lifetime.

Establish thread ownership from actual upload/update call sites. Publish complete snapshots atomically; do not let fitting read an array while an update mutates it.

Raw retained position storage is approximately `12 * vertexCount` bytes per tracked mesh, plus object/cache overhead. Actual retained counts, memory use, and update cost require measurement.

## Rendering integration

`QuickToolItemIcon` opts into a scoped fit request containing the target wedge and desired size. The radial menu supplies geometry without knowing item categories or inventory policy. Preserve the existing `IRadialMenuIcon` path for artwork that does not support fitting; use an optional capability/context boundary rather than requiring every icon to become mesh-aware.

During the ordinary engine GUI draw, the adapter obtains the resolved `ModelRef` and completed matrix from that same invocation. It must not invoke `GetItemStackRenderInfo`, `OnBeforeRender`, or the draw itself a second time for measurement.

Project retained positions with the engine's actual model-view and projection matrices and viewport mapping. Account for the screen Y convention, homogeneous division, and item/block-specific transforms already present in those matrices. Reject invalid or unsupported projection cases, including geometry crossing the homogeneous clipping boundary, rather than fabricating bounds.

Apply an additional screen-space affine transform to the position transform before the item is drawn into the existing capture. For the normal orthographic GUI path, derive the equivalent matrix composition from the active projection. Do not assume that changing a model-space Z rotation produces a screen-space rotation after the other GUI rotations.

Preserve the engine's lighting matrix when applying this presentation transform so fitting does not change the item's shading. The installed renderer submits separate model and model-view uniforms; the adapter must avoid mutating a matrix array shared with the lighting submission. Leave depth ordering and engine render-state restoration intact.

The final projected point has the form:

```text
fittedPoint = placement + scale * rotation(angle) * (projectedPoint - pivot)
```

The pivot is derived from projected geometry and recorded with the fit. Translation must include both geometric recentering and placement; the manual category-specific head offset does not participate in a successful fit.

The scope is limited to the intended quick-swap item invocation, is safe under nested calls, and is restored in a `finally`/equivalent exception-safe boundary. Ordinary inventory draws must remain unaffected. A missing or incompatible engine hook disables fitting with a bounded diagnostic and leaves normal rendering available.

## Fitting geometry and search

### Projected hull

Project valid positions from all children of the selected mesh and construct a 2D convex hull. Deduplicate coincident points and handle point/line degeneracies explicitly. A hull conservatively encloses model triangles and transparent portions of those triangles. Transparency may make the result less tight, but no texture inspection is required.

Do not first reduce the geometry to a 3D axis-aligned box: its projected corners can include substantial empty space around diagonal tools. Retain the source positions for changes to the engine's GUI transform, and cache the projected hull when the geometry and orientation remain equivalent.

### Conservative convex fit region

The visible wedge is an annular sector with separators and rounded corners. Vertex-only testing against that non-convex shape is insufficient: an edge can cross the inner circle while its endpoints appear valid.

Use a convex subset of the visible wedge for the initial implementation. For an ordinary wedge with angular width at most 180 degrees, construct it from:

- An outer disk reduced by a safety inset.
- Inset half-planes for the two separator-adjusted angular boundaries.
- An outward-facing tangent half-plane excluding the inner disk, oriented along the wedge bisector.

Choose the safety inset conservatively from halo radius, border width, padding, corner radius, and numerical/rendering tolerance. In particular, reserve the full corner radius in addition to the required visible clearance so the accepted region avoids the shader's rounded-corner cuts. Derive the constants from shared style values and verify containment against the shader contour over the supported size range.

The resulting intersection is convex. Testing every transformed hull vertex inside it guarantees containment of the hull's edges and interior. It deliberately sacrifices some space near the inner arc and corners for a simple, provable containment rule.

Handle a single annular wedge, sectors wider than 180 degrees, and center discs explicitly. They must not accidentally enter a two-half-plane construction intended for narrower sectors. Center text/artwork remains unchanged; unsupported annular cases use the existing fallback until a tested safe-region construction is supplied.

### Bounded search

Begin with zero rotation and the preferred wedge-midpoint placement. Search feasible translations within the safe region before concluding that rotation is required. Include radial and tangential long-axis alignments, then a bounded angular sweep with local refinement around promising results.

At each angle, solve or conservatively search translation feasibility for the scaled hull. A fixed placement chosen before fitting can unnecessarily shrink the result. Use bounded scale refinement only with a feasibility predicate whose assumptions are established; failed heuristic placement must not be described as proof that no placement exists.

Cap scale at one relative to nominal size. Prefer full-size solutions, then larger scale, then smaller absolute rotation and smaller midpoint displacement. Validate the selected result with numerical slack before exposing it to rendering. Search budgets and tie tolerances are implementation constants to tune from representative meshes, not measured performance claims.

### Hover and resizing

Use the actual ring radii, start angle, direction, separator angle, screen radius, and hover geometry. Hover expands the wedge and icon around the menu center, while border, corner, and halo widths remain pixel-based.

Prefer a stable resting fit that is transformed with the existing hover animation, provided containment is established for the complete hover range. If that proof does not hold for the chosen safe region, update placement/scale as necessary while retaining orientation where feasible. Test intermediate hover states, not just endpoints.

Recompute or invalidate on changes to geometry, effective GUI orientation, projection/GUI scale, menu size, wedge count/placement, or style clearance. Menu-center translation alone should not require rebuilding a local hull. Avoid retessellation, vertex scanning, hull construction, and angle searches on unchanged frames.

## Ownership and source layout

Keep responsibilities directional and each file focused:

| Owner | Responsibility |
| --- | --- |
| Mesh geometry cache | Owned positions, child identity, revisions, updates, and lifetime. |
| Engine mesh adapter | Upload/update/disposal observation and registration. |
| GUI item fitting adapter | Scoped request, actual draw matrix access, and final position transform application. |
| Projected geometry helper | Projection validation and convex hull construction. |
| Radial icon fitter | Safe region, containment, bounded search, and fit result. No engine or inventory policy. |
| Quick-tool item icon | Opt-in and fallback selection using the existing engine renderer. |
| Radial menu renderer | Target wedge context, hover integration, capture, halo, and stencil. |

Use a dedicated item-rendering domain for engine adapters/cache and keep generic fitting math under the radial-menu domain. Composition roots install and dispose owners; they do not implement search or geometry policy. Final type names are implementation choices.

## Compatibility boundaries

| Rendering case | Proposed support |
| --- | --- |
| Default inventory item/block mesh and alternate variant | Supported when upload was observed. |
| `OnBeforeRender` replaces `ModelRef` or its transform | Supported when the selected replacement geometry is tracked; measure the resulting transform. |
| Manually assembled mesh with untracked children | Unsupported until explicitly registered. Do not fit only the known children. |
| Registered renderer using the supplied mesh and prepared matrices | Supported after its behavior is verified. The base game's [liquid GUI renderer](../../../vssurvivalmod/Systems/Liquid/LiquidItemStackRenderer.cs) follows this pattern with stack-size display disabled. |
| Renderer drawing extra meshes, changing matrices, or adding overlays | Requires an adapter describing the full geometry/transform contract; otherwise fallback. |
| Shader-driven animation/deformation | Static uploaded positions are insufficient. Requires evaluated geometry or a conservative animation envelope; otherwise fallback. |
| Unknown mesh mutation outside observed APIs | No universal guarantee. Explicit registration/invalidation is required. |

Do not claim arbitrary-mod compatibility. The engine GUI shader supports joint animation, so support cannot be inferred solely from a non-null `ModelRef`.

## Validation and acceptance

Implementation is acceptable only when the supported path demonstrates complete geometry containment and preserves the engine's ordinary rendering behavior.

### Focused automated validation

- Projection: compare measured coordinates and applied transforms using item/block origins, GUI translations, nonuniform base scale, and rotated models; reject invalid homogeneous coordinates.
- Geometry: include asymmetric tools, diagonal thin meshes, multi-part models, transparent-plane geometry, unused buffer capacity, and point/line degeneracies.
- Containment: exercise angular boundaries, separators, inner-circle edge crossings, outer arcs, rounded corners, and halo/border clearance. Check independent contour samples along edges/interiors as well as the solver's own predicate.
- Search: cover unrotated fits, placement-only fits, rotation preserving full size, necessary shrinking, deterministic ties, bounded search, and empty safe regions.
- Lifecycle: verify copied-array ownership, partial position updates, color-only updates, invalid ranges, mesh replacement, child updates, disposal, handle reuse, reloads, and enabling after initial loading.
- Integration: prove the fit uses the draw's final replacement mesh and invokes rendering callbacks only once; verify scope cleanup on failure/nesting and no changes to ordinary inventory rendering.
- Layout: cover supported wedge counts, menu-size limits, GUI scales, viewport changes, and intermediate hover states. Explicitly exercise unsupported geometry fallbacks.

Builds and test tools must run through a subagent under the repository's working instructions. Completion requires focused validation and an independent implementation review; this proposal does not mark implementation work complete.

### In-game acceptance and measurement

User-run visual acceptance should include long tools, broad shields, bows, asymmetric models, block-like light sources, and stack-dependent replacement meshes. Check full-model visibility, constant halo thickness, depth correctness, stable orientation, screen edges, resizing, and hover transitions.

Measure first-open work separately from unchanged-frame work, retained geometry bytes, cache misses, and update/reload behavior. Report CPU/GPU costs only from measurements. Headless tests do not establish visual quality or GPU cost, and no game launch is authorized by this proposal.

## Outstanding implementation decisions

1. Verify the earliest client installation point and ensure all relevant inventory uploads are observed.
2. Choose and validate a version-sensitive hook at the completed GUI matrix boundary without repeating item callbacks or changing lighting.
3. Confirm update-offset units, partial-update range semantics, and disposal coverage in the installed engine.
4. Choose the bounded translation/angle search and tolerances, with guaranteed containment separated from best-fit quality.
5. Establish how registered custom renderers and animated draws are detected before granting fitted status.
6. Decide whether single annular wedges receive a dedicated safe region initially or remain an explicit fallback.

These decisions affect integration coverage and packing quality. They do not change the core requirements: measure mesh vertices, use the actual engine draw state, preserve normal orientation when feasible, and never label an unverified fallback as fully contained.
