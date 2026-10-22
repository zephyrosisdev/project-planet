# Modular buildings

Open **Massive Army > Art > Modular buildings**. The active catalogue contains the generated buildings. Existing model packs remain available in the model-pack window.

1. **Create / sync profiles** creates one asset per building ID under `Assets/MassiveArmy/Art/ModularBuildings/Profiles`. Existing profiles are preserved. Farms, roads, rails, defence emplacements, generators and launch pads default to their existing specialised art.
2. Choose a generation profile. Set its dimensions in real metres, use, floor count, room division, windows, garden and detail distance. The automatic plan connects four rooms to a central hall on each floor, with stairs and landings. Optional room entries override names, door widths and grid offsets in floor order: negative-X/negative-Z, positive-X/negative-Z, negative-X/positive-Z, positive-X/positive-Z. The hall is not included in this array.
3. Edit the assigned kit to supply wall, window-frame, door-leaf, floor, stair, garden-wall and pillar meshes. Several sizes are supported; the baker selects the nearest matching size and fits its bounds to the required opening/section. Leave a mesh empty for generated boxes. Imported meshes need **Read/Write** enabled. Meshes share the kit material and atlas; glass panes use a separate transparent material.
4. Use **Additions** to kitbash grid pieces. `Cell` is the centre in profile grid units; `Size` multiplies the kit module size; `Quarter Turns` rotates around Y. Room `-1` attaches to the exterior; room `0` is the ground-floor hall, `1..4` its rooms, then `5` the next hall, etc. Additions append geometry; doorway/window cuts and portal connections come from the generated plan, so use room door settings and window settings for functional openings.
5. **Bake selected & use** replaces that building ID in a new selected catalogue. **Bake enabled profiles & use** replaces all enabled IDs. Changes become visible on the next Play session. Bake creates one shared layout asset with mesh subassets per building type; structural pieces are combined, and runtime objects are limited to pooled rooms and interactive doors. Earlier model-pack assets are retained. **Use original art** resets the selector to the original catalogue.

## Runtime detail

- LOD0: detailed modular shell, transparent windows, address plate, room meshes and interactive doors. First-person **F** toggles a nearby door. Floors and smooth stair collision ramps support the existing player capsule. A nearby entrance ramp bridges the foundation to the sampled terrain and refreshes after terrain edits. Collision stays loaded within 15 metres even when you look away.
- LOD1: closed flattened exterior with painted windows and simple garden walls. It also fills in for LOD0 until a nearby interior slot becomes available, avoiding visible empty openings.
- LOD2: one 12-triangle box for the building. Existing frustum, horizon and subpixel rejection still apply.
- At most **four** nearby buildings own detailed interiors/collision at once; at most one is bound each frame. Room rendering uses conservative screen-rectangle portal traversal through open doors, transparent windows and stairs (32 rooms, 128 visits maximum). It uses no GPU readback or per-frame visibility allocations. Closed doors block traversals. This is conservative visibility, not exact polygon clipping or a general GPU occlusion renderer.
- Residential gardens use combined wall segments and posts, with an entrance gap. Nearby road junctions receive STOP signs and selected straight roads receive 30 speed signs. Signs are combined into static 32-tile chunks, capped at 12 signs per chunk and drawn only nearby. Building addresses appear in detailed slots.
- Bake limits: 4,096 exterior triangles, 512 flat triangles, 16,000 total room triangles, 12,000 collision triangles, 32 doors. Buildings must fit their reserved construction footprints. Army records and the million-unit GPU path are unchanged.

## Validation

**Massive Army > Art > Validate modular buildings** checks profile layouts, room connectivity, physical doorway/ramp geometry, portal visibility, shader compilation and LOD budgets. Results go to `TestResults/Planetary/ModularBuildings/editor.json`. The standalone dense-city validation additionally checks streaming, door interaction, real collision and the four-building cap. Performance comparisons are recorded alongside the baseline and current reports in that folder.

Final validation: **63 building checks**, **100 model-pack checks**, and **17 standalone checks** pass. The final 1,024-building 1440p comparison has 10.49 ms near-city p95 and 11.92 ms moving-camera p95 (+1.1% versus the paired control). Full frame-time, simulation-rate and memory measurements: `TestResults/Planetary/ModularBuildings/Results.md`.


## Curved terrain foundations and night visibility

Modular buildings retain rigid, level floors and garden walls. Their plot uses
that same tangent plane instead of a constant-radius spherical surface. Placement
samples the footprint at intervals of at most eight metres and raises the plane
to cover those samples. Batched foundation sides extend into the original terrain;
this is a raised foundation, not a persistent terrain deformation stamp.
Existing spherical non-modular models retain spherical plots.

Road meshes sample the planet at four-metre grid intervals, with twelve-centimetre
clearance instead of the previous thirty-five centimetres. A connected plot has
a driveway from its boundary to the adjacent road centre. The four nearby modular
interior slots reuse the plot and driveway coordinates for walking collision;
there is no collider or GameObject per road tile. First-person entry consults plot
support once, so entering on a raised lot starts above its foundation. Farm fields continue to follow
the terrain. Foundations refresh after terrain edits and driveways after road edits.

At night, existing shader passes provide road marking emission, warm modular
window glass and a restrained blue building rim. These add no realtime lights or
render passes. Cloud opacity keeps at least twelve percent transmission on the
unlit side, so city light remains faintly readable through thick clouds. Opaque
depth, ship cabin exclusion and planet occlusion still limit atmospheric rays.
This is a visibility aid, not physically simulated light transport through fog.
