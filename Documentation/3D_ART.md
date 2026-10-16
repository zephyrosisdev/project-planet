# Synthetic Front model library

The Command Centre now uses a separate detailed concept reconstruction with
2K trim maps and an unrestricted prototype mesh. See [HQ authoring notes](HQ_ART.md)
and [its visual review](../TestResults/HQ/gallery.html). The old generic HQ in
the packed library is superseded by `ArtSource/HQ/CommandCentre.blend`.
Library texture/render/export tools preserve this bespoke asset.

The game catalogue has 49 building shells and 31 unit shells, using the established dark metallic armour and cyan equipment markings. Every definition has an editable mesh, a packed UV layout, and separate 512×512 albedo, metallic and tangent-space normal maps. The building foundations use the existing 8 metre tiles. A gate is 24×8 metres; the compact defence turrets occupy 8×8 metres.

Browse [the complete portrait/model comparison](3D_ART/gallery.html). The three labelled contact sheets and 80 transparent model renders are in `TestResults/ArtPass`. Exact dimensions, triangle counts and footprint reservations are in `ArtSource/SyntheticFront/manifest.json`.

## Authoring and material convention

`ArtSource/SyntheticFront/SyntheticFront.blend` contains the complete editable library, including near and medium distance meshes and packed texture sources. The models are arranged in a library grid; the FBX files in `Assets/MassiveArmy/Art/Models` use a ground-level origin without that display offset. Mesh and UV payloads are also saved as JSON alongside the blend file so Unity meshes can be rebuilt consistently.

The silhouettes are authored from the UI portraits using reusable low-poly components: chamfered armour, octagonal mechanisms, exposed weapons, intake assemblies, radar reflectors and faceted canopies. Bottom faces of foundations and vertical closed components are omitted. Horizontal gun caps, wheels and visible equipment surfaces remain. These are shell assets rather than watertight manufacturing meshes.

Texture conventions:

- Albedo is sRGB and contains panel colour, inset seams, fasteners and fine brushed-metal grain.
- Metallic is a linear RGBA mask: R = metallic, G = ambient occlusion, B = team emission/recolour mask, A = smoothness.
- Normal is linear RGB tangent-space relief derived from the same panel height field. The custom shaders read RGB directly; the importer deliberately uses Default texture mode rather than Unity's packed Normal Map mode.
- All runtime maps use mipmaps and BC7 compression. CPU-readable import copies are disabled after catalogue generation.

The original metal micro-detail image was made through the built-in imagegen workflow. Its source and authoring brief are saved in `ArtSource/SyntheticFront/metal-source.png` and `metal-source-prompt.txt`. Model geometry, palette bakes and directional renders are produced by the project scripts; no external model library is used.

## Runtime and budgets

Building rendering uses the shared asset catalogue, team material property blocks and two existing LOD thresholds. Walls retain all 16 connection configurations. Gates have a separate leaf shell, and the existing friendly-only passage logic still controls its opening. Construction previews use the same building meshes and foundation sampling as completed structures.

Units continue to live in the GPU simulation buffers. There are no per-unit GameObjects, CPU unit states or skinned renderers. Two shared, catalogue-indexed vertex banks feed the existing indirect draws: at most 512 triangles near the camera and 128 at medium distance. Each unit also has eight transparent 128×128 directional impostors for the distant draw. Turret rotation and helicopter rotor motion are resolved in the vertex shader.

The separate million-unit stress renderer and simulation kernels retain their existing implementation. The art change does not alter navigation, flow generation, unit scale limits or gate team rules. Lighting uses the existing inexpensive sun/sky approach with UV metallic response, normal detail and emission, rather than introducing an additional full lighting pass per army draw.

## Rebuilding

Run these scripts from the project root with a fresh background Blender process for the Blender steps:

1. `blender -b --factory-startup --python Tools/build_model_art.py -- <project root>`
2. `python Tools/bake_model_textures.py`
3. `blender -b --factory-startup --python Tools/render_model_art.py -- <project root>`
4. `python Tools/review_model_art.py`
5. In Unity: **Massive Army → Art → Build model art catalogue**.

For manual artist edits, keep the library offsets, edit vertices and UVs locally, and apply object rotation/scale. Run `Tools/export_model_art.py` in background Blender instead of step 1; it preserves edited geometry and rejects budget overruns. Then run steps 2–5. The seven face material indices used for the palette bake are armour, edge alloy, rubber, team light, glass, solar and vent, in that order.

The catalogue builder rejects missing maps or vertex banks, invalid UVs, non-finite vertices and buildings that exceed their reserved X/Z dimensions. The opt-in Windows argument `-art-validation` exercises complete asset coverage and submits all 31 catalogue unit types through normal GPU spawning. `-grid-validation` retains the placement, rotation and gate traversal acceptance checks.

Visual inspection covers every item in the Blender catalogue renders. Unity/player validation and equivalent performance measurements are recorded in `TestResults/ArtPass`; see the [validation results](3D_ART/RESULTS.md) for measured targets and limitations. Estimates of texture budgets are not measured VRAM usage.
