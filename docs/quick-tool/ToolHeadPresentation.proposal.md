# Asset-defined radial-menu tool presentation

Status: complete. Settings resolution, dedicated rendering, and tool-family patches are implemented. Fresh technical validation, user-reported visual acceptance, second review, and independent stage-completion audit passed. The superseded hook is removed.

## Purpose

Show a deliberately framed view of each tool's working head in its quick-swap menu wedge. The head should be large, readable, and oriented appropriately for that wedge. Handles and other parts of the model may extend outside the wedge and be clipped intentionally.

Tool assets define this presentation through a `ve-radial-menu-properties` object. The radial-menu presentation is independent of the base game's inventory GUI transform, which is authored for square item slots.

## Asset settings

Use asset patches to add `ve-radial-menu-properties` under the tool item's `attributes`. Read the resolved collectible's `Attributes`, not stack attributes or its inventory GUI transform. Share patches across variants with matching geometry; use the existing asset variant-resolution conventions for different shapes. Preserve unrelated attributes and create a missing attributes parent without replacing existing data.

The object defines a dedicated presentation transform with:

- **Rotation:** model-space orientation in degrees, chosen to expose the useful face of the tool head.
- **Scale:** uniform model scale relative to the radial renderer's reference size.
- **Translation:** model-space placement used to bring the head to the presentation origin.
- **Wedge-relative rotation:** an optional screen-space angle relative to the wedge's outward radial direction, applied after model orientation and projection.

### Schema and transform convention

```json
{
  "ve-radial-menu-properties": {
    "transform": {
      "rotation": { "x": 0, "y": 0, "z": 0 },
      "translation": { "x": 0, "y": 0, "z": 0 },
      "scale": 1
    },
    "wedgeRotationDegrees": 0
  }
}
```

This is a schema example, not tuned artwork. A present empty properties object is valid identity presentation. Missing transform/vector components default to zero rotation/translation and scale one. An absent properties object selects fallback. Accepted fields and numeric conversions follow engine typed deserialization; unknown fields are ignored. Deserialization failures, a null transform, non-finite values, and non-positive scale components select fallback.

Reuse `ModelTransform` with explicitly initialized identity values, default origin `(0.5, 0.5, 0.5)`, positive scale, and default `Rotate = false`. Its `AsMatrix` provides the existing translation/rotation/scale math. The enclosing properties object adds the optional wedge angle, which that engine type does not provide.

Initialize these values with `ModelTransform.ItemDefaultGui()` and populate the engine's `ModelTransformNoDefaults` input type, seeded with those defaults, through `JsonUtil.PopulateObject` typed deserialization. Transfer its decoded transform fields to the owned `ModelTransform`. This avoids `ModelTransform`'s deserialization callback treating an explicitly authored sentinel vector as omitted. Retain only rendering validation around that operation; do not independently decode vector components or implement transform defaults.

Coordinates are normalized uploaded model units, with one unit corresponding to 16 shape-coordinate units for ordinary shape meshes. Model X points right and Y up before authored rotation; positive Z follows the engine model convention. The default pivot is the model-space point `(0.5, 0.5, 0.5)`; engine transform fields may override it. Translation is in model units, after local rotation/scale, independent of GUI scale; authors use it to bring the working head to the anchor. Rotation follows the engine's degree-based X/Y/Z matrix composition.

For column vectors, define the dedicated model matrix as:

```text
A = ModelTransform.AsMatrix
  = T(t) * T(o) * Rx * Ry * Rz * S(assetScale) * T(-o)
M = T(iconX, iconY, depth) * Rz(screenAngle)
    * S(sizePixels, -sizePixels, sizePixels) * T(-o) * A
```

Here `o` is the resolved engine transform pivot and `t` the authored translation. The rightmost operation applies first. `T(-o) * A` simplifies to `T(t) * Rx * Ry * Rz * S(assetScale) * T(-o)`, so the pivot maps to the icon anchor at identity and stays there under rotation when translation is zero. Do not subtract the pivot on the opposite side of `A`.

At scale one, one model unit spans `sizePixels` screen pixels before projection. This is a reference size, not full-model fitting. The menu already supplies effective size including GUI/menu sizing and hover; do not call `GuiElement.scaled` again. Negative Y scale converts model-up to screen-up in the engine's Y-down GUI coordinates. Retain the existing icon depth argument (`100`) and capture depth policy.

If `wedgeRotationDegrees` is omitted, `screenAngle` is zero and the authored view stays screen-fixed. If supplied, `screenAngle` equals the actual wedge center angle plus that value. Angles are clockwise from screen up, matching `RadialMenuLayout`; supplied zero tracks the outward direction. For a center disc without a radial direction, use zero as the reference angle. Wedge rotation acts around the icon anchor on the completed presentation; it is not an extra model-space Z rotation.

Placement is anchored to the wedge's icon position. Menu size and hover supply the overall presentation scale; asset values must not encode fixed screen coordinates. Wedge-relative rotation lets a long tool follow its wedge's direction as the layout changes.

## Patch authoring example

The authored patches live in [toolheadpresentation](../../VanillaExpanded/assets/vanillaexpanded/patches/toolheadpresentation), with one JSON patch file per targeted item definition, named after its source file. A single-shape family can add its presentation directly; families with different shapes use `ve-radial-menu-propertiesByType` inside `attributes`. The engine resolves the suffix and variant selectors into the same `ve-radial-menu-properties` object before our resolver reads it. Do not add a second variant-selection implementation.

This working example is the [prospecting-pick patch](../../VanillaExpanded/assets/vanillaexpanded/patches/toolheadpresentation/prospectingpick.json):

```json
{
  "op": "add",
  "file": "game:itemtypes/tool/prospectingpick.json",
  "path": "/attributes/ve-radial-menu-properties",
  "value": {
    "transform": {
      "origin": { "x": 1, "y": 0.04375, "z": 0.5 },
      "rotation": { "x": 90, "y": 90, "z": 0 },
      "translation": { "x": 0, "y": 0, "z": 0 },
      "scale": 2.3
    },
    "wedgeRotationDegrees": 0
  }
}
```

Here the engine-supported `origin` field places the pivot at the working head, using normalized model coordinates derived from the shape's `(16, 0.7, 8)` point. Zero translation anchors that point at the wedge icon position. Rotation exposes the head's X/Z plane and sends the shaft inward; supplied wedge rotation zero tracks the wedge's outward direction. Scale enlarges the head without fitting the full handle. Translation can provide further framing adjustments after rotation/scale. The current authored values received user-reported visual acceptance.

All current targets already have an attributes object, so the patches add only the new child. For a future target without that parent, create it before adding the child; never replace existing attributes. Missing/unmatched metadata retains ordinary rendering, and registered custom GUI delegates remain unsupported even when metadata exists.

## Rendering behavior

1. Resolve the selected tool's radial-menu properties.
2. Select ordinary engine fallback before preparation for missing/invalid settings or known unsupported draws. For a supported configured draw, obtain prepared item information once through `api.Render.GetItemStackRenderInfo(slot, EnumItemRenderTarget.Gui, dt)`.
3. Apply the dedicated radial-menu matrix and GUI shader settings, then submit the prepared mesh with `api.Render.RenderMultiTextureMesh(info.ModelRef, "tex2d", 0)`. Do not compound the matrix with the inventory GUI transform or the existing category-specific head-centering offset.
4. Render through the existing icon capture, halo, hover, and wedge-stencil pipeline. Cropping outside the wedge is intentional.

### Dedicated renderer and support

Implement a focused item-rendering method using the public preparation, GUI shader, atlas-position, and mesh-submission APIs. Do not patch `InventoryItemRenderer` or depend on its IL layout. `GetItemStackRenderInfo` supplies ordinary/alternate mesh selection, render settings, transition overlays, and slot/collectible preparation callbacks. Call it once for the selected detached slot; do not manually repeat those callbacks. Prepared mesh and texture references remain borrowed from their existing owners: no additional tessellation, mesh upload, retained geometry, or disposal is required.

Keep all per-draw information and matrices local to the dedicated method. Upload `M` as the model matrix, combine it with the current model-view matrix for the model-view submission, and use the current GUI projection. Use the unoffset wedge anchor for configured draws; `QuickToolItemIcon` retains the category-offset position for ordinary engine fallback. Nested draws must not inherit another draw's presentation or overwrite its owned matrices.

Initially support the ordinary inventory mesh path under the engine's Y-down orthographic GUI projection, including callbacks replacing the mesh or preparing textures/rendering state. Callback changes to `ItemRenderInfo.Transform` are presentation overrides and are deliberately superseded by configured radial settings; preserve other applicable preparation. Registered custom GUI delegates remain unsupported. Query their registration live through the cached registry accessor; removing the Harmony hook does not remove this reflection dependency. Unknown registry access, unsupported projection, or unavailable shader support selects ordinary engine fallback before preparation. Do not infer support from a non-null mesh.

The dedicated method owns GUI shader setup: normal shading and model-matrix application, color, alpha test, temperature/incandescence glow, damage effects, and overlays including sampler binding, texture dimensions, and base atlas UV origin. Reuse `GetEngineShader(EnumShaderProgram.Gui)` and `GetTextureAtlasPosition(stack)` rather than introducing a replacement shader or atlas lookup implementation. Preserve the ordinary GUI path's applicable culling, blending, and depth behavior; do not assume every `ItemRenderInfo` field is consumed by that path. Authored orientation naturally changes normal-based shading.

Preserve the separate `collectible.InGuiIdle(api.World, stack)` notification once for a prepared, drawable item, matching the inspected engine ordering: preparation, non-null mesh check, then notification. This callback is a per-frame GUI lifecycle notification, not mesh preparation. The base implementation is empty; temporal gears use it to animate their inventory `GuiTransform`. Such transform changes do not alter our authored radial matrix. Our renderer must not itself mutate shared collectible GUI transforms; invoking an item's own lifecycle callback can retain that item's intentional side effects. Keep `dt = 0` to match the current no-delta-time GUI call unless a separate animation requirement is approved.

Selected state contract: entry requires the engine GUI shader already active, as supplied by the menu/capture caller, with finite Y-down orthographic projection and model-view matrices. Unsupported shader state selects ordinary engine fallback before preparation. Texture-clamping shaders are unsupported because binding them can mutate borrowed texture parameters. Snapshot touched GUI uniforms and animation controls before callbacks; initialize animation disabled, then retain any callback-enabled animation during submission. Apply ordinary GUI effects after preparation. Use `try/finally` to restore exact enclosing uniform values, active GUI program, texture/sampler bindings on units 0 and 1, GUI UBO block mappings and bound buffer ranges, generic UBO binding, blend/depth/cull settings, stencil/scissor enable flags, and matrix-stack top/depth. Do not rebind an already active GUI shader unnecessarily: its `Use()` reapplies include defaults. Uniform snapshots ignore linker-optimized-out locations. Callback-owned buffer/texture contents remain borrowed and are not copied or rolled back. Callbacks may modify current matrix-stack top/depth but must not mutate lower caller-owned entries or destroy the active borrowed shader/resources during a draw; those operations require their owning lifecycle boundary. The existing capture retains framebuffer/viewport and clipping ownership. Disable inventory rotation and stack-count artwork; ordinary fallback continues to pass `rotate: false` and `showStackSize: false`.

### Selected state and post-preparation policy

Preparation or `InGuiIdle` can change the selected stack, projection, or custom-renderer registration. Recheck eligibility before submission. There is no public engine API to draw an already-prepared `ItemRenderInfo` through the ordinary GUI path. Calling `RenderItemstackToGui` after our preparation would repeat callbacks and is prohibited by the single-preparation requirement.

Selected policy: ordinary engine fallback is available only before preparation. If preparation or the lifecycle notification changes the stack or invalidates renderer registration, projection, shader availability, or matrix validity, consume this invocation without submitting a mesh or retrying the engine draw. Report this invalidation once per collectible during the renderer lifetime. A later invocation reevaluates eligibility and uses ordinary engine fallback if the unsupported condition is already present. This explicitly revises the previous same-draw fallback requirement for post-preparation invalidation; it avoids copying the inventory GUI transform/dispatch implementation and never duplicates callbacks. Missing prepared geometry likewise consumes the invocation without drawing, as the engine does. Exceptions propagate after cleanup without a draw retry.

## Coverage and fallback

Start with the tool families displayed by quick-swap, tuning their patches against representative models and variants. Non-tool entries, tool-mode artwork, and center labels retain their existing presentation unless separately configured and supported.

Items without valid properties use the current rendering behavior, including its existing category offset. Custom renderers that cannot honor the dedicated transform also fall back. Registration and support for those renderers can be extended when a concrete need is established.

Validate configuration values and reject deserialization failures, non-finite transforms, and non-positive scale components. Also reject settings whose composed `ModelTransform.AsMatrix` contains non-finite components, since individually finite inputs can overflow when combined. The later rendering boundary must validate its final matrix after applying menu size, placement, and wedge rotation. Report invalid configuration with bounded diagnostics rather than repeatedly logging during rendering.

## Scope and ownership

The item asset owns its presentation settings. A focused item-rendering component reads and applies them; the radial-menu renderer supplies wedge placement, size, direction, hover, capture, halo, and clipping. Keep composition roots thin and preserve candidate selection, entry ordering, equipment operations, input, and hit testing.

Use focused files in an item-rendering domain for property resolution, presentation matrix construction, dedicated GUI drawing/state cleanup, and renderer eligibility. `QuickToolItemIcon` owns opt-in and fallback placement; the existing optional radial-icon context capability conveys wedge direction without changing unrelated `IRadialMenuIcon` implementations. Keep the client composition root thin and free of transform/draw policy. Remove the superseded `ToolHeadPresentationHook`, `ToolHeadPresentationScope`, their patch installation/disposal, and hook-specific tests when the dedicated path replaces them; retain reusable settings, matrix, context, and eligibility coverage.

Automatic bounds calculation, retained mesh geometry, containment searches, and automatic rotation or shrinking are outside this design. The acceptance criterion is readable tool-head framing, not full-model visibility.

## Validation

- Verify that configured draws use the dedicated transform independently of inventory GUI settings, and that ordinary inventory rendering is unaffected.
- Verify fallback for missing/invalid properties and unsupported renderers, plus exception-safe cleanup and single callback execution.
- Verify one preparation and one applicable `InGuiIdle` notification, including callback-selected meshes, differing inventory transforms, and the selected post-preparation fallback policy.
- Verify GUI shader values for alpha, shading, color, temperature, damage, and overlays, plus shader/state restoration after nested draws and failures. Compare representative effects in user-run visual checks; headless assertions cannot establish GPU appearance.
- Obtain user-run visual checks across representative tool heads, model/material variants, menu sizes, GUI scales, wedge positions, and hover states. Confirm readable heads, intentional handle cropping, suitable orientation, halo appearance, and stable placement.
- Run build and test tools through subagents under repository instructions. Source and headless checks do not establish visual acceptance; no game launch is authorized by this proposal.

## Evidence and remaining validation

The contracts above were established on 2026-10-06 from current repository source, installed assets, and Mono.Cecil inspection of `G:/Vintagestory/VintagestoryLib.dll`, assembly version `1.22.7.0`, SHA256 `E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0`.

| Decision | Evidence |
| --- | --- |
| Attribute location and patch conventions | [CollectibleObject.Attributes](../../../vsapi/Common/Collectible/Collectible.cs); [JsonObject indexer/AsObject](../../../vsapi/Datastructures/JsonObject.cs); installed [pickaxe attributes](G:/Vintagestory/assets/survival/itemtypes/tool/pickaxe.json); repository [healing-item patches](../../VanillaExpanded/assets/vanillaexpanded/patches/healingitems.json) and installed survival attribute patches. |
| Reused transform math | [ModelTransform](../../../vsapi/Common/Collectible/ModelTransform.cs): `AsMatrix`, `ItemDefaultGui`, `EnsureDefaultValues`, and degree rotations. Explicit identity values avoid sentinel/default ambiguity. |
| Public preparation and submission | [IRenderAPI](../../../vsapi/Client/API/IRenderAPI.cs) exposes `GetItemStackRenderInfo`, `GetEngineShader`, `GetTextureAtlasPosition`, and `RenderMultiTextureMesh`. Installed `RenderAPIGame.GetItemStackRenderInfo` forwards to engine preparation; slot/collectible callbacks occur there once. `RenderAPIBase.RenderMultiTextureMesh` binds each section's texture and submits its existing mesh. No public ordinary GUI draw accepting prepared information was found. |
| GUI lifecycle and prepared data | [ItemRenderInfo](../../../vsapi/Client/UI/ItemRenderInfo.cs), [InGuiIdle](../../../vsapi/Common/Collectible/Collectible.cs), and [ItemTemporalGear](../../../vssurvivalmod/Item/ItemTemporalGear.cs). Installed GUI draw checks for a mesh before calling `InGuiIdle`; the no-delta-time overload supplies zero. |
| Draw/shading/eligibility | Model/model-view submissions at GUI `IL_03EC`/`IL_041C`; custom registry lookup at `IL_0439`–`IL_0459`, delegate call `IL_0472`, ordinary mesh draw `IL_04E1`. `ClientEventAPI.itemStackRenderersByTarget` indexes collectible class, GUI target, and ID. Installed `assets/game/shaders/gui.vsh` transforms positions with model-view and normals with model matrix. |
| Menu size, direction, capture and fallback | [RadialMenuLayout](../../VanillaExpanded/src/RadialMenu/RadialMenuLayout.cs), [RadialMenuRenderer](../../VanillaExpanded/src/RadialMenu/RadialMenuRenderer.cs), [RadialMenuIconHalo](../../VanillaExpanded/src/RadialMenu/RadialMenuIconHalo.cs), and [QuickToolItemIcon](../../VanillaExpanded/src/QuickTools/QuickToolItemIcon.cs); angles are clockwise from up, hover scales size/placement once, capture owns depth, fallback has a category offset. |

These establish reusable engine behavior, not a requirement to patch the inspected instruction offsets. Initial coverage and implementation records are in [ToolHeadPresentation.todo](ToolHeadPresentation.todo#initial-asset-coverage). The dedicated renderer passed 354 affected tests and the Release build with 0 errors and 6 existing warnings. A standalone hidden OpenGL context executed production state restoration, including uniforms, UBO ranges, texture/sampler bindings, flags, and matrix-stack cleanup after failure. Integration checks cover single preparation, lifecycle ordering, shader effects, nesting, and pre-preparation fallback/post-preparation cancellation. Second review and independent stage-completion audit passed for rendering.

The authored patches cover 23 item files, 238 allowed variants, 79 base shape paths, and 14 GUI-selectable alternate paths. Historical local verification passed 355 affected tests and the Release build with 0 errors and 6 existing warnings. The former asset-patching test executed the installed engine's patch loader and variant resolver, confirmed valid presentation settings, and compared complete resolved definitions to prove unrelated values remained unchanged. That installation-dependent test and its patch-library reference were removed at user request because they cannot run under CI; these receipts remain historical local evidence. See [authored coverage and traceability](ToolHeadPresentation.todo#authored-asset-coverage-and-traceability). Patch second review and independent stage-completion audit passed. Final technical verification passed 354 retained affected tests, a fresh Release rebuild (0 errors, 6 existing warnings), existing Cake JSON validation, and local checks of all 23 split patch targets. The user accepted the requested visual coverage on 2026-10-06 with "its fine"; no tuning issue was reported. See the plan's [final validation and acceptance record](ToolHeadPresentation.todo#final-validation-and-acceptance-traceability) for the requested scope and distinction between technical checks and user-reported appearance. Final independent stage-completion audit passed; no unresolved findings remain. No quantified performance result or compatibility beyond the inspected engine version is claimed.
