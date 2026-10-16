# Command Centre concept revision

Scope: HQ (`command`) only. The other 48 buildings, 31 units, placement sizes,
gate rules, GPU army banks and simulation remain unchanged.

The supplied concept is retained in `ArtSource/HQ/reference.png`. Authoring
source is `ArtSource/HQ/CommandCentre.blend`; it contains separately named
editable components with non-destructive three-segment bevel modifiers, plus
the merged production shell. This is a fidelity prototype without a polygon
cap. Its detailed shell is used at both existing LOD distances intentionally;
optimisation has not been disguised as a fidelity change.

The model follows the octagonal glazed command rotunda, stepped split-core
spire, tall antenna array, long inset-panel outriggers, offset raised landing
pads, concave ribbed dish, low perimeter pods and recessed front portal/ramp.
The main body has separate sloping roof plates and real panel gaps. Large
underside caps are omitted. Minor enclosed mechanisms and beams are closed.
The concept-angle camera and offset pad, dish, spire and portal positions are
fitted to image landmarks. The central chassis is recessed behind the entrance
rather than extending a large square platform through the doorway. Final shell
geometry stays inside ±20 m on both horizontal axes; the reserved footprint is
still 5×5 placement tiles, independently of the irregular visible perimeter.

One 2048px reusable eight-strip trim sheet supplies gunmetal, edge alloy, dark
plates, gaskets, blue glass, team lights, amber markings and white stencils.
Maps live in `Assets/MassiveArmy/Art/Textures/building-command-*.png`:

* albedo: sRGB metal and paint colours, subtle brushed grain;
* metallic: linear RGBA = metalness, trim AO, team emission, smoothness;
* normal: linear tangent-space micro-relief, sampled directly by the shader.

The glass strip includes a variable emission profile; metal strips include
rolled-surface variation, scuffs and rubbed edge highlights. These are original
procedural textures, not the reference artwork projected onto a mesh.

Shape, bevels, roof seams, vent slots, glazing mullions, door recess and dish
curvature are geometry rather than painted perspective. Per-corner hemisphere
AO is baked into vertex colours for recessed details in the game material.
BC7 and mipmaps are enabled; CPU texture copies are released after import.
There is one runtime HQ mesh/material per visible LOD, with team-colour
property blocks; there are no objects for individual armour components in play.

Rebuild textures with `python Tools/build_hq_trim.py`. Run Blender 5.2 with
`--background --factory-startup --python Tools/build_hq_art.py -- <project>`;
append `--quick` for a fast EEVEE comparison. The exporter writes FBX and a
versioned compressed float mesh stream. Use `Tools/render_hq_views.py` to
render the saved geometry with Cycles. The streamed importer avoids loading
a large triangle-expanded JSON string through Unity's native JSON parser.
Run **Massive Army → Art → Build detailed HQ only** to update the existing
catalogue while preserving its GUIDs and other entries. Full catalogue builds
also select this HQ source when it exists.

Standalone `-hq-validation -benchmark-output <folder>` checks the material,
texture resolution, AO coverage, catalogue coverage and 40×40 m footprint,
then captures the actual HDRP camera. `TestResults/run_hq_benchmarks.ps1`
compares three equivalent base-close runs per frozen build at 2560×1440.
The normal eight starting units occupy a 50K-capacity battle in this scenario;
it measures base rendering, not a populated 50K army. Timing excludes UI.
The isolated runtime material view uses an independent HDRP camera with global
postprocessing and exposure disabled. This avoids queued battlefield draws
and permits a clear material comparison. The placement capture uses the normal
game camera and its scene settings.

Methods follow the current [Blender 5.2 bevel modifier](https://docs.blender.org/manual/en/5.2/modeling/modifiers/generate/bevel.html)
and [UV workflow documentation](https://docs.blender.org/manual/en/5.2/modeling/meshes/uv/editing.html).
The trim layout deliberately reuses strips instead of reserving unique texture
space for each panel. Editable source components are retained for subsequent
manual refinement and a later optimisation/baking pass.

To preserve manual work, edit the `building-command` production shell in the
saved blend and run `Tools/export_hq_edits.py` instead of the generator. Keep
the trim UV layer, ground-plane origin and `ShellAO` colour layer; apply object
transforms. Major shape edits need a new AO bake. The generator recreates its
authored components, so running it overwrites manual changes to this blend.

Visual comparison and measured results are recorded in `TestResults/HQ`.
Feature coverage and a successful import alone do not certify an exact match
to the concept; the reference and rendered views are provided together for
visual assessment.
