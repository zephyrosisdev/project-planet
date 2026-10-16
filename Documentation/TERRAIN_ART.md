# Terrain texture art

The procedural battlefield uses nine tileable surface sets: grass, sand, dry grass,
forest floor, rock, snow, mud, gravel, and compacted-earth road. The first eight
follow the existing `Biomes.Layers` channel order; the road uses `Splat.b`. All
twenty climate/hydrology categories remain supported in the layer study. Generated
battles use regional habitat masks refined by slope, tree cover and deposition.

Mud is independent of the road layer. It uses filtered, warped biome coverage
through water-bed transitions; the shader preserves that coverage when normalizing
the other bed materials. This avoids restoring coarse water cells as solid,
rounded-square mud patches. Road interiors and cliff coverage stay protected.

Each surface has a 1024 × 1024 albedo, tangent-space normal, and grayscale height
PNG in `Assets/MassiveArmy/Art/Terrain`. Albedo is imported as sRGB; normal and
height are linear data. All textures repeat, use mipmaps, and have anisotropy 8.
White height indicates raised features; black indicates low features. These
maps control texture blending and shading, without displacing terrain geometry.

The built-in image generation tool produced the albedos and corresponding height
edits. The original 1254 × 1254 sources and exact prompts are preserved in
`Tools/TerrainSources/generation-prompts.json` and adjacent PNGs. Height maps
are artistic relief estimates, rather than measured photogrammetric scans.
Unity downsamples these sources to a power-of-two resolution, conditions matching
edge bands, normalizes relief, and generates wrap-aware normals directly from
the heights. Material-specific physical relief ranges from 4.5 cm for sand/road
to 16 cm for rock; tile widths are 4–6 metres.

`Resources/MassiveArmy/TerrainArt/SurfaceLibrary.asset` binds two nine-slice BC7
texture arrays to the runtime battlefield material. The albedo array uses sRGB;
the surface data array uses linear RG normal XY, B height, and A roughness. The
two full mip chains occupy approximately 24 MiB of texture storage. Runtime
does not build arrays, load the source PNGs, or read textures back to the CPU.

The terrain shader uses world-space tiling, normalized height-weighted blending
of active layers, and normal-dependent lighting. Road participates in the same
height blend and fully covers the biome at its interior. Rock projects onto
three axes to reduce stretching on cliffs. Slow world-space colour variation
breaks up long-distance uniformity. The existing HDRP depth pass is retained
for water refraction. The runtime ForwardOnly pass feeds the blended albedo,
world-space normals and roughness into HDRP's Lit lighting loop. Scene lights,
light colour/intensity, exposure, indirect light and shadow maps now affect
the terrain. A ShadowCaster pass lets terrain meshes cast scene shadows.
Surface sampling is shared in `TerrainSurface.hlsl`. This adds no tessellation.
The `TerrainValidation` pass is only drawn explicitly by offscreen material
tests with a fixed reference light; scene cameras never use it.

Select **Massive Army → Art → Rebuild Terrain Textures** after changing sources.
This regenerates the 27 maps and packed arrays, preserving asset GUIDs and the
library's blend settings. The surface library Inspector exposes height blend
width/strength, overall normal strength, macro variation, and each layer's tile
size/normal multiplier through labelled controls. Inspector changes apply live
to the running battle in the Unity Editor, including Undo/Redo. Per-layer relief/roughness defaults live in
`TerrainArtBuilder.cs`; rebuilding recalculates layer parameters.

For tuning without Play mode, open **Massive Army → Art → Terrain Look Dev**.
Enable **Scene view preview**, choose **Layer blend study** or **Biome terrain**,
and use **Frame preview in Scene view**. The blend study lets you choose any two
surfaces and inspect their transition at close range. The biome preview uses a
seeded world with the same biome assignments and terrain shader as the battle.
Adjust height blend strength/width, normal strength, colour variation and named
surface tile sizes directly in the window. Settings edit the shared library
asset and persist after Play mode; asset Undo is supported.

Disable the toggle or close the window to remove the preview. It renders only
in Scene view and creates no saved scene objects. It also shuts down when
entering Play mode or recompiling scripts. Water, scenery and units are omitted
from this terrain-only preview.

Enable the Scene view **Scene Lighting** toggle to tune the actual scene sun.
With Scene Lighting disabled, Unity uses its editor preview light instead.
Directional light position does not change its lighting: adjust its rotation,
colour or intensity. Lighting uses HDRP's physical light units and camera
exposure, so changing exposure also changes the apparent terrain brightness.
The runtime presentation creates **Battle sun** with warm neutral daylight at
50,000 lux, fixed exposure 12.5 EV, a brighter sky for indirect fill, soft
shadows and restrained bloom. Rotate that light while playing to change terrain
lighting. In Edit mode, use a scene directional light for Look Dev.

Open **Massive Army → Art → Battle Atmosphere and Fog Settings** for the shared
`BattleVisualSettings.asset`. Daylight and fog parameters apply live while
playing; edits to this asset persist after leaving Play mode. Increasing
Exposure darkens the image. Sun Lux controls direct illumination; Sky Exposure
controls ambient fill. Shadow Strength reduces the harshness of shadowed areas.
Direct changes to the runtime sun stay in effect until a daylight preset field
changes. Sun rotation is always preserved.

## Volumetric fog of war

Base-building matches render terrain-following cloud over unrevealed areas,
lighter mist over explored areas, and clear regions around current vision.
The existing GPU visibility buffer remains authoritative for enemy detection.
A compute pass filters its edges and smooths density changes over time without
CPU readback. The volume samples drifting 3D noise and integrates extinction
along camera rays, stopping at opaque depth. Half-resolution rendering and
depth-aware upsampling reduce cost and help retain structure silhouettes.

The atmosphere Inspector exposes Fog Of War, Density, Height, Explored Density,
Edge Softness, Noise Scale/Strength, Wind, Fog Colour, Scattering and Ray Steps.
The new motion controls are **Flow Strength** (6 m/s), **Flow Scale** (160 m)
and **Flow Cycle** (18 s). Wind now defaults to X/Z (2.5, 1) m/s and noise
strength to 0.8. A seeded GPU curl-noise flow map includes terrain contour
influence and advects two octaves of 3D gradient noise. Two overlapping phases
hide advection resets; vertical billowing adds evolution beyond horizontal drift.
Motion continues when the battle simulation is paused.

**Unexplored Darkness** defaults to 0.96 and **Explored Darkness** to 0.35.
These independently conceal the ground, so gaps in the animated density do not
reveal hidden terrain. Current vision clears concealment. The cloud itself
retains some shading so its motion stays readable above dark ground. These
controls apply live in Play mode and persist in the shared settings asset.
Disable Fog Of War to return to the original flat reconnaissance overlay.
The benchmark battle does not have visibility gameplay and omits this effect.
This custom HDRP pass runs before post-processing; native global volumetric fog
is unnecessary. It approximates scattering from the sun and ambient fill;
volumetric self-shadowing and multiple-light scattering are not implemented.

**Validate Battle Atmosphere and Fog** checks all three GPU visibility states,
edge filtering and shader compilation. In Play mode after deploying a base,
it additionally checks pass execution and renders fog on/off comparison images
to `TestResults/battle-volumetric-fog.png` and `battle-daylight.png`.
`fog-opacity.png` exposes the half-resolution volume silhouette for inspection.
All ten fog checks and ten terrain lighting checks passed on the DX11 editor.
The full Game-view overview and flat-fog comparison are saved as
`battle-fog-overview.png` and `battle-flat-fog-overview.png`. These are rendering
smoke checks, not a scale performance benchmark. Both fullscreen passes bind
their own targets; automatic camera targeting is disabled to avoid an extra
half-resolution rectangle during HDRP camera render requests.

Research reviewed on 9 October 2026: Unity's
[HDRP 17.6 local fog documentation](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.6/manual/create-a-local-fog-effect.html)
describes density masks, volume boundary blending, and native volumetric
limitations. This implementation uses the project's GPU reconnaissance map
directly in a custom raymarch pass so its spatial visibility rules control fog.

**Massive Army → Art → Validate Terrain Scene Lighting** renders an isolated
fixture through a real HDRP camera and verifies sun rotation, intensity, colour,
sun disabling and shadow receiving from a mesh occluder. Results are written to
`TestResults/terrain-scene-lighting-validation.json`, with `terrain-sun-*.png`
comparison images. The original material tests remain separate from this
lighting integration test.

Select **Massive Army → Art → Validate Terrain Textures** to check imports,
seam matching, GPU array contents, shader compilation, actual height blending,
normal lighting, road dominance, and steep rock projection. Results are saved
to `TestResults/terrain-art-validation.json`; texture and GPU render previews
are saved to `TestResults/terrain-materials.png`,
`TestResults/terrain-rendered-surfaces.png`, `TestResults/terrain-height-blend.png`,
and `TestResults/terrain-world.png`.

Natural transitions use the same generated splat masks in the battle and Look
Dev preview. The two biome masks now generate linear mip chains once, during
level creation (about 171 KiB extra at 256 squared, or 683 KiB at 512 squared).
The shader samples this prefiltered hierarchy at a world-space radius, warps
the sampling position with seeded, two-octave gradient noise, and adds material
competition at coarse and fine scales before the existing height blend.
This broadens region boundaries while retaining texture-scale surface detail.
Rotated noise octaves and domain warping avoid regular square-shaped patches.
Fine patch noise fades under minification to reduce distant shimmering.

In **Terrain Look Dev**, scroll to **Natural biome transitions**:

| Control | Starting value | Effect |
| --- | --- | --- |
| Biome Blend Width | 48 m | Wider transition zone; independent of texture tile size. |
| Boundary Warp | 24 m | Larger values make region edges wander further. |
| Boundary Noise Scale | 96 m | Larger values make bigger bends in the boundaries. |
| Patch Strength | 0.65 | Higher values give locally present materials stronger patch competition. |
| Patch Scale | 12 m | Size of the smaller material patches. |
| Road Edge Noise | 0.35 | Irregular shoulders, with full road-centre coverage retained. |

All six controls update live in Play mode and the Scene preview; regeneration
is unnecessary. The blend study covers 128 metres to show both noise scales.
For a smoother result, widen Biome Blend Width and Height Blend Width. For
stronger breakup, increase Boundary Warp and Patch Strength. Very narrow height
blend widths still produce crisp local edges, even with broad biome masks.
Set Biome Blend Width, Boundary Warp, Patch Strength and Road Edge Noise to
zero to disable these effects. The seed comes from the generated world.

These transition sliders only change rendering weights: biome classifications, terrain height, forests,
water geometry, road paths and navigation costs retain their generation data.
Noise redistributes locally present channels rather than injecting unrelated
materials across the map. Submerged beds keep their local mask assignments;
cliff rock coverage is anchored by the local slope mask before competition.
The rendering adds two filtered biome-mask samples and procedural noise. It
does not allocate or rebuild maps on slider changes. This improves the material
transitions; AAA lighting, vegetation and terrain geometry remain separate art
and rendering work.

Research reviewed on 9 October 2026:

- [Unity HDRP 17.6 Terrain Lit Inspector reference](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.6/manual/terrain-lit-material-inspector-reference.html)
  distinguishes control-map weights from height-based surface competition.
- [Wronski, GPU-Friendly Laplacian Texture Blending, JCGT, February 2025](https://jcgt.org/published/0014/01/02/paper.pdf)
  explains why wide simple texture crossfades lose contrast and proposes
  frequency-dependent blending. The implementation here combines wide mask
  filtering with local height blending; it does not implement that paper's
  Laplacian pyramid reconstruction.

Validation now also checks broadened categorical borders, noisy GPU output,
seed determinism, seed variation, absent-layer exclusion and submerged masks.
Compare `TestResults/terrain-transition-before.png` and
`TestResults/terrain-transition-after.png`; the diagnostic coverage map is
`TestResults/terrain-transition-masks.png` (red grass, green sand, blue rock).

The initial art pass passed 142 checks on an RTX 3070 using DX11. A playable
base-building match also loaded the saved arrays successfully; its smoke-check
status and screenshot are in `TestResults/terrain-runtime-smoke.json` and
`TestResults/terrain-game.png`. This is a rendering smoke check, not a scale
performance benchmark.

## Regional geography — generator version 3

New battles now generate a coherent temperate coastal region instead of
compressing equatorial desert, rainforest, tundra and glacier into one map.
The landform scaffold has a connected mountain spine, branching spurs,
foothills and lowland catchments. Noise provides small surface variation.
Thermal talus relaxation and a simplified stream-power incision/deposition pass
shape the relief before the existing priority-flood hydrology builds waterways.
This is a procedural erosion approximation, not a full hydraulic simulation
or terrain imported from surveyed elevation data.

Moisture responds to catchment flow and an upwind relief rain shadow. Exposed
rock follows steep slopes; mud follows wet depositional hollows; scree follows
slopes and sediment; forest floor follows actual tree-cover density. Generated
worlds reduce boundary warp to 30%, mask-filter radius to 50% and random patch
competition to 20% of the shared library settings to preserve those features.
The manual layer study retains the full controls for material tuning.

Generate a new battle, or rebuild the **Biome terrain** Look Dev preview, to
see the new geography. Existing generated meshes do not change live. Version 3
changes heights, habitats and resulting road/site placement for the same seed;
it keeps deterministic seed generation and validates traversable sites and roads
clear of water. The surface library's saved texture scales and blend settings
are preserved. `BiomeMaps/height-73129.png` shows relief and
`geography-73129.png` encodes catchment flow, sediment and slope in RGB.

Research reviewed on 10 October 2026:

- [SideFX HeightField Erode](https://www.sidefx.com/docs/houdini/nodes/sop/heightfield_erode.html)
  uses sediment, debris, flow and flow-direction layers to guide terrain work.
  The generator here uses its own lightweight catchment and deposition fields.
- [Red Blob Games mapgen4](https://www.redblobgames.com/maps/mapgen4/)
  demonstrates structured mountain and drainage generation. This informed the
  use of connected landforms rather than relying on stacked noise for relief.

Version 3 passes 50 generation checks across three seeds; terrain materials pass
151 checks. All 14 battle fog checks pass, covering GPU concealment and flow-map
values and a real HDRP battle camera in Play mode. `battle-flow-fog-a.png` and
`battle-flow-fog-b.png` capture moving clouds over the same paused battle;
`fog-flow-motion-validation.json` records their pixel comparison, including
ordinary temporal rendering variation. `natural-terrain.png` shows the same
camera with fog disabled. The runtime smoke report reserves 50K GPU slots with
eight starting units; it is not a full 50K active-unit performance benchmark.
