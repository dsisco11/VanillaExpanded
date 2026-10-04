# Item-slot indicator effects

Effects attach to internal provider registrations. Providers still return `ItemSlotIndicator(Fill, Color)`; selection, priorities, and cached sampling remain independent of animation. Registrations without an effect use the ordinary rectangle. This is an internal extension mechanism, not a public third-party API.

## Registering an effect

Create one immutable definition and pass it to `ItemSlotIndicatorSystem.Register`:

```csharp
var effect = new ItemSlotIndicatorEffectDefinition(
    "vanillaexpanded:my-effect",
    Constants.ModId,
    "vanillaexpanded_itemslot_my_effect",
    segmentCount: 16,
    parameters: new Vector4(0.5f, 0, 0, 0),
    needsCameraMotion: true);
system.Register(provider, priority: -10, effect: effect);
```

Define and validate the units/ranges of the four parameter lanes in an effect-specific factory. Unused lanes should be zero. The generic definition rejects nonfinite parameters, unsupported ABI/topology, invalid names, and subdivision counts outside the geometry contract. Registration and GPU resource management belong on the client graphics thread.

IDs and asset domains use canonical lowercase names. Shader basenames must start with `vanillaexpanded_itemslot_`. Place the matching `.vsh` and `.fsh` files in `assets/<domain>/shaders/`. Reusing an identical effect ID/definition is allowed; conflicting definitions under one ID are rejected before changing provider selection. Appearance variants need distinct IDs, but may share the same shader domain/basename/ABI. The same engine shader name cannot identify conflicting program keys.

## Bounded draw levels

Providers may return `new ItemSlotIndicator(fill, color, new ItemSlotIndicatorDrawRange(0.2f, 0.8f))`. Omit the range for existing empty-to-full behavior. Levels are fractions of slot height/area measured upward from the bottom: mapped height is `minimum + resourceFill * (maximum - minimum)`. Thus resource fill 0 draws at 20%, 0.5 at 50%, and 1 at 80% in this example. The colored region remains bottom-anchored; the range limits its top edge, not its bottom. Use a positive minimum to retain visible fill at empty, and a maximum below one to retain headroom at full. Ranges require finite `0 <= minimum < maximum <= 1`.

Resource fill and color remain provider-owned samples. Only rendering maps fill. The shader receives mapped height as `fill`; resource fill and draw levels stay on the CPU for mapping and separate boundary-bar draws. The shader ABI remains unchanged. Effect deformation follows the common slot-containment and mapped-area contract below. Opacity zero remains intentionally invisible. Liquid containers and nonempty watering cans use the bounded range described below; other built-in providers retain their existing levels.

Within the nearest 15% of resource fill to an endpoint, one fixed horizontal bar marks that boundary. It fades in with smoothstep, reaches 60% of provider opacity at the endpoint, and uses RGB lightened 65% toward white. Its thickness is one pixel at a 48-pixel slot, scaling with slot size. The bar stays inside the slot and disappears in the middle range. The GUI rectangle path draws it after an effect returns to the GUI shader, or in the same scope as plain/fallback fill; no new shader assets or mesh uploads are needed. Cached range changes participate in adaptive activity detection.

## Geometry and shader inputs

ABI 1 uses indexed triangles and one `vec3` position attribute at location 0. Each vertex contains `(u, edge, 0)`: `u` is the normalized horizontal sample and `edge` is zero for the bottom or one for the surface. Quad topology requires one segment; fill strips accept 2–64 segments, with 16 as the default. Meshes have `2(N+1)` vertices and `6N` indices and are shared by ABI/topology/subdivision count.

| Uniform | Type | Meaning |
| --- | --- | --- |
| `projectionMatrix` | `mat4` | Current engine GUI projection |
| `modelViewMatrix` | `mat4` | Current engine GUI model-view, including dialog depth |
| `slotBounds` | `vec4` | Scaled pixel left/top/width/height |
| `fill` | `float` | Mapped geometry height in [0,1] |
| `color` | `vec4` | Sanitized straight RGBA in [0,1] |
| `timeSeconds` | `float` | Shared periodic animation clock in [0,64) |
| `motion` | `vec2` | Shared damped camera rates in [-1,1] |
| `cameraBob` | `float` | Shared damped local eye-height rate in [-1,1], positive upward |
| `effectParameters` | `vec4` | Immutable effect-specific values |
| `segmentCount` | `int` | Fixed mesh subdivision count |

Projection, model-view, bounds, and fill must remain active linked uniforms. Other common inputs may be optimized out; submission checks their presence. Preparation validates common uniform types and the single position attribute. Programs cannot use geometry stages, samplers, images, or UBOs. Trusted shaders must also obey the output restrictions below; linked-input validation is not a shader sandbox.

For a surface height `h(u) = fill + d(u)`, compute:

```glsl
vec2 position = vec2(slotBounds.x + u * slotBounds.z,
    slotBounds.y + slotBounds.w * (1.0 - edge * h));
gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 80.0, 1.0);
```

Keep horizontal coordinates, bottom anchoring, slot extent, and local depth unchanged. Require `abs(d) <= min(0.1, 0.25*fill, 0.25*(1-fill))` and zero discrete trapezoidal mean over the mesh samples. This preserves the represented area and forces zero deformation at empty/full fill. Prove the bound and mean analytically and sample every supported subdivision and boundary fill in tests. Clamping individual surface heights can bias area and is unsuitable as a substitute.

The fragment shader writes only attachment zero as `vec4(color.rgb * color.a, color.a)`. Premultiply once; retain provider opacity and color. Do not write fragment depth or introduce side effects. Default-mode zero fill or zero alpha skips drawing; bounded mode maps empty fill onto its minimum level and can still show a boundary cue; a provider's full-fill warning overlay remains full coverage even when the physical resource is empty.

## Animation, rendering, and ownership

One Ortho callback at order 0.99 publishes the frame snapshot before engine GUI rendering. Slot draws only read that snapshot. Time uses a monotonic clock and accepted positive intervals up to 0.25 seconds; long/invalid gaps contribute no catch-up. Shader time functions must be periodic over 64 seconds to avoid a discontinuity at wrap.

Camera capture occurs only when a registration requests it. Motion X represents looking right and Y looking up, normalized at four radians/second and damped with a 0.12-second time constant. Invalid/missing orientation, camera/player/world/mode replacement, inactivity, long gaps, or turns greater than pi/2 reset angular motion. Motion never invalidates provider samples.

Footstep bob changes the engine player's `LocalEyePos.Y`, so it is captured separately from rotation. First-person cameras with `viewBobbing` enabled publish local eye-height changes normalized at 0.5 blocks/second and damped with a 0.06-second time constant. World travel and jumps of the player's body are excluded by using the local offset. Other local eye-height changes, such as crouching and immersive camera animation, can also contribute. Missing/nonfinite height, height jumps over 0.25 blocks, camera context changes, inactivity, and long gaps reset bob; enabling bob establishes a neutral baseline. The optional `cameraBob` uniform leaves effects that do not consume it unchanged.

The resource owner prepares through the game's `IShaderProgram` factory, file registration, and compiler. Programs share domain/basename/ABI keys. Preparation happens at initialization, queued late-registration boundaries, and shader reload, never in a draw. Late registrations render plain until prepared. Reload invalidates old handles first, recompiles stable engine names, and retains successful fixed meshes. Failed mesh uploads may retry at reload. Shutdown unregisters the frame callback and shader event and disposes owned meshes/programs, tolerating engine disposal during reload; queued callbacks become harmless.

Rendering remains immediately before the original item call, inside its inherited clipping. Both paths disable depth writes while drawing, preserve inherited depth-test/cull state, and use engine premultiplied blending. They restore Standard blending, which the preceding slot-background helper establishes. Only three depth/cull state queries are needed. Rectangles use the GUI shader's `noTexture` branch and owned matrix scratch storage; they restore texture mode and model-view without changing engine matrix stacks or texture bindings. Effects switch through engine `Stop`/`Use` and return to the GUI program, whose engine-owned UBOs are rebound by `Use`. Subsequent item/overlay draws establish their own presentation and mesh/texture bindings. This contract is specific to the inspected slot-grid hook, not arbitrary renderer callers.

Missing or invalid resources select the rectangle using the winning provider's original fill/color. A draw failure disables only that effect until reload and restores state before fallback. Diagnostics are bounded per preparation attempt/effect, and host GUI failures are logged once per renderer lifetime. The original item call is still forwarded with all arguments; its exceptions are not swallowed. An unusable engine GUI shader cannot be repaired by indicator fallback.

## Adding and validating an effect

1. Add paired shader assets and a declaration factory with parameter validation.
2. Prove surface containment, bottom anchoring, mean area, empty/full behavior, and clock periodicity; add focused boundary tests.
3. Attach the definition to the intended registration without changing provider sampling or priority.
4. Inspect the built ZIP for both assets, then validate engine compilation/linking and appearance in a user-run client.
5. Test load failure, repeated shader reload, and shutdown in that graphics context; compare matched CPU/GPU workloads using the matched workload described below.

## Liquid sloshing

Liquid containers and watering cans share `LiquidSloshIndicatorEffect.Definition`, a sixteen-segment vertex effect enabled with their existing indicator setting. Normal liquid samples use average surface levels 0.15–0.85, preserving raw resource fill, color, opacity, sampling, and priority. Empty containers retain a small blue liquid surface and the minimum boundary cue. Empty watering cans instead retain their existing unbounded full red warning: mapped fill is one, deformation is zero, and no boundary cue is drawn. Other providers remain ordinary rectangles.

With `u` measured left to right and `activity = max(abs(motion.x), abs(motion.y), abs(cameraBob))`, the standing wave is `-(0.625 + 0.125 * activity) * cos(2*pi*u) * cos(2*pi*timeSeconds/2 + 0.75*cameraBob)`. Its center rises as both sides fall, then the motion reverses, repeating every two seconds. Resting wave amplitude is 25% stronger than the previous standing wave. Actual eye bob both strengthens and shifts the wave phase, producing a signed response even when the camera is not turning. Camera tilt is `-0.0625 * motion.x * (2*u - 1)`: looking right raises the left edge. Shared damped camera signals control settling; there is no separate fluid simulation or inertial spring.

A permanent meniscus adds `0.1875 * ((2*u - 1)^4 - m) / (1 - m)`, raising the outer edges with a shallow depression in the middle. For `N` strip segments, `m = 1/5 + 4/(3*N^2) - 8/(15*N^4)` is the exact discrete trapezoidal mean of the quartic shape. The existing `segmentCount` uniform supplies `N`. Parameter lanes encode resting wave, added motion wave, tilt, and meniscus weights. The combined wave, tilt, and meniscus are multiplied by `a = min(0.1, 0.25*fill, 0.25*(1-fill))`, retaining slot containment and flat empty/full warning overlays.

The nonnegative weights total one, bounding absolute displacement by `a`. The centered linear tilt and complete spatial cosine period have zero discrete trapezoidal mean. Subtracting the quartic's sampled mean also preserves area; for subdivisions 2–64, `m <= 0.5`, so dividing by `1 - m` bounds its absolute excursion by one. Their sum preserves fill area without height clipping. Thirty-two clock cycles make the wave seamless across the 64-second wrap. Limits mark average surface height, not wave extrema; the existing separate boundary bars stay fixed. Bottom anchoring, depth, provider color, and simple premultiplied fragment output are unchanged. Numerical reference tests cover containment and sampled mean across all supported subdivisions, endpoint fills, motion extremes, center/side oscillation, meniscus symmetry, and clock periodicity; they do not establish runtime shader compilation or appearance.

The temporary demonstration declaration, environment override, assets, and shader-specific tests were removed after user-run animation validation. That result does not validate the liquid shader; its live appearance, reload behavior, and GPU cost still require a user-run client.

User-run acceptance covers inventory and hotbar placement, GUI scales 1.0/1.5, scrolling/scissor edges, overlays and notification jitter, returned fills 0/0.01/0.25/0.5/0.99/1, static/moving cameras, long gaps, and camera/world changes. Include warnings whose returned fill differs from physical resource level. Run valid and deliberately broken shader assets, reload repeatedly, restore valid assets, and shut down while checking logs and resource lifetime. Never interpret headless test results or ZIP inspection as runtime shader validation.

For matched measurements, compare 10/100/250 visible slots at GUI scales 1.0/1.5, N=16/64, plain/effect/mixed cases, and static/moving cameras. Warm up for 10 seconds, then collect three paired 30-second runs; report CPU median/p95 and asynchronous GPU measurements separately, with shader-switch and mesh-upload counts. The initial budgets were 0.05 ms maximum disabled-effects regression and 0.25 ms median / 0.5 ms p95 enabled-effects overhead for CPU and GPU independently; those measurements were waived at user acceptance and are not claimed as passing. Record machine/driver/build, workload conditions, shader switches, mesh uploads, and measurement uncertainty. The user confirmed visible demonstration animation on 2026-10-04. The detailed graphics acceptance matrix, reload/failure/shutdown cases, and matched performance budgets remain unverified until their results exist. Geometry shaders and production themes remain deferred.

Implementation anchors: [system](../VanillaExpanded/src/ItemSlotIndicators/ItemSlotIndicatorSystem.cs), [definition](../VanillaExpanded/src/ItemSlotIndicators/Effects/ItemSlotIndicatorEffectDefinition.cs), [resources](../VanillaExpanded/src/ItemSlotIndicators/Rendering/ItemSlotIndicatorResources.cs), [draw backend](../VanillaExpanded/src/ItemSlotIndicators/Rendering/ItemSlotIndicatorDrawBackend.cs). Engine integration was inspected against Vintage Story 1.22.7.0; recheck it when updating engine versions.
