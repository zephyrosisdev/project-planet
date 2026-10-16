# Building shadows and weapon articulation

The HQ and standard building shadow passes previously applied
`GetCameraRelativePositionWS` to `TransformObjectToWorld`'s already camera-relative
result. The second subtraction displaced building shadows. Both passes now
transform that result directly to the shadow clip space. The lit terrain receiver,
lighting profile and shadow switch retain their existing behaviour.

Building weapon targeting remains in the existing GPU `StructureWeapons` kernel.
It publishes the selected firing target and wrapped-world yaw into a fixed
512-entry, 16-byte-per-entry pose buffer (8 KiB). There is no extra targeting
dispatch, CPU target scan, unit readback or per-unit object. Reused building
slots clear the old pose.

All nine gun/launcher types use one cached joint-mask mesh per catalogue LOD:
autocannon, AT, AA, SAM, advanced SAM, point defence, artillery, missile defence
and laser. Complete connected shells above the authored y=6.5 bearing rotate;
the pedestal and foundation remain fixed. This avoids shearing a head mesh by
tagging its top vertices alone. Placement yaw is subtracted from the world aim
before the vertex shader turns positions, normals and tangents. The shadow pass
uses the identical pose. Missile silos remain stationary. Conservative bounds
cover a full weapon rotation. Original catalogue meshes/UVs are preserved.

The mask is generated once per shared mesh when that type first appears and
disposed with the match. No additional draw per turret or child GameObject is
created. Weapon heads face the last acquired firing target, including during
reload; this does not add a continuous tracking scan or change firing cadence.
Future replacement turret artwork must preserve or explicitly revise the
fixed-bearing/independent-head geometry convention.

Validation uses actual catalogue materials and near/far meshes at a nonzero
world position with the 1400m RTS camera. Rendered HQ, factory and turret shadow
differences are checked. Every turret LOD is checked for both fixed and moving
geometry. Rendered north/east weapon poses differ while the rendered foundation
and its shadow remain identical, including a 90-degree placed rotation.

The GPU fixture checks live target selection, wrapping seams, slot reuse and
D3D11's eight-UAV zombie variant without skipped dispatches. The standalone
`-building-presentation-validation` path constructs a rotated turret through
the actual placement API, submits an enemy through GPU spawning, checks the
published firing pose and verifies the building/grid rotation stays unchanged.
Reports and render evidence are in `TestResults/BuildingPresentation`.
