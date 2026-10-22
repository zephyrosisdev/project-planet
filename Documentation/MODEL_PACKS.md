# Building and unit model packs

Open **Massive Army > Art > Model packs** in Unity, then choose **New pack**.
The profile lists the RTS and civilisation buildings and all 31 unit types.
Select an ID, enable **Override this model**, and set its size in metres.
Unchanged entries continue using the original catalogue. Overrides apply only to
the selected building ID: residential buildings that share the default command
centre mesh do not inherit a command centre override.

Each LOD accepts an imported mesh/model/prefab, a cube, a generated shape,
or original art. Assign a material or an albedo texture and tint to cubes
and procedural shapes. Prefabs combine child meshes and material slots;
an attached LODGroup supplies the corresponding level. Enable Read/Write
in the model importer while baking. Import rotation corrects source axes:
Y is up and +Z is forward. Every level fits the same ground-aligned size box.

Buildings have three mesh levels: 4,096 / 512 / 128 triangles maximum.
Units retain the existing shared GPU banks: 512 / 128 triangles, followed
by eight directional impostors. Leave custom impostors empty to bake them
from LOD1, or assign all eight clockwise from the front (+Z). Flat-world
strategic dots retain their existing renderer. Unit dimensions must fit
the existing GPU culling bound; building sizes must fit their footprint.

Choose **Bake & use pack**, then enter Play mode. Baking creates a new asset
folder beside the profile and preserves earlier bakes. **Use last bake**
switches back to the profile's last result. **Use defaults** restores the
original catalogue, including the planet's all-distance building cubes.
Switching is allowed while Play mode is stopped. The chosen baked catalogue
also ships in player builds.

Materials are converted to the existing art shaders with shared 512px
texture atlases. Standard/HDRP base colour, metallic/smoothness and normal
textures are supported. UVs must lie within 0..1; a model can use up to 16
material tiles across its LODs. Custom shader effects, material tiling and
animation controllers are not transferred. Skinned meshes use their static
bind-pose geometry. Imported units are static; original unit articulation
is retained only by levels using the original geometry. Roads and rails
retain their dedicated network renderer.

The profile changes artwork only: no per-unit GameObjects or CPU simulation
are introduced. Detailed building packs can cost more than the default
12-triangle cubes; the LOD limits are guards, not a guarantee of 60 FPS.
Measure representative standalone scenes when choosing a pack for release.

**Massive Army > Art > Validate model packs** exercises baking, texture-array
copying, generated impostors, prefab combining, GPU bank upload and invalid
assignments without changing the active pack. The report is written to
`TestResults/ArtPass/ModelPacks/validation.json`.
