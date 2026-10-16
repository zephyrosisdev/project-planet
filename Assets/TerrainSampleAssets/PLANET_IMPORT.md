# Planet terrain stamp sources

Twelve original 16-bit heightmaps and their Unity metadata were selectively imported from the user's cached **Terrain Sample Asset Pack** by Unity Technologies. Demo scenes, terrain objects, shaders, vegetation and materials were omitted. The original source assets remain under their Unity Asset Store licence.

Official overview: https://unity.com/blog/games/the-latest-unity-terrain-sample-pack-is-here

Package: `Unity Technologies/3D ModelsEnvironmentsLandscapes/Terrain Sample Asset Pack.unitypackage` in the local Unity Asset Store cache. Original package paths and GUIDs: `TestResults/Planetary/Stamps/import-manifest.json`.

## Definitions and baking

`Assets/MassiveArmy/Resources/MassiveArmy/PlanetStamps/Library.asset` exposes source heightmaps, height gain, footprint and height offset. Preserve definition order for saved worlds. Stop Play mode before editing; use **Massive Army → Planetary → Bake Stamp Heightmaps** or the Inspector bake button after changing source maps/order. Height gain, offset and footprint are baked into per-planet placement descriptors at runtime.

`Heights.bytes` contains twelve little-endian unsigned 16-bit 512×512 layers in library order, bottom row first (6 MiB). CPU queries and the GPU R16 array use the same samples. Original TIFFs are outside Resources and referenced by editor path, excluding full-resolution source dependencies from players. The initial source bake used 16-bit Lanczos downsampling; Unity editor rebakes use GPU bilinear resampling. Rebakes change terrain content and should be versioned with saved worlds.

## Spherical sampling

Four corner stamps blend per queried direction. Shared integer cube-corner coordinates give identical seed choices at face seams. Tangent projection, smooth blend weights, seed-selected reflection and quarter-turns are independent of query order and UnityEngine.Random. The broad continental and mountain/plain masks remain; stamps replace analytic ridge/hill relief. Biomes, terrain layer textures/normals/height blending, base pads and crater edits retain their existing stages.

The optimized sampler precomputes 3,750 immutable 48-byte corner descriptors per enabled large planet (180,000 bytes each for CPU and GPU). No whole-planet height atlas, CPU army state or per-unit objects are created. Disabled/compact planets bind one fallback texel/record and do not load full stamp maps. Original visible mesh patches remain streamed on the GPU. Performance and functional reports: `TestResults/Planetary/Stamps/RESULTS.md`.

## Saved project preset

At the user's request, `Assets/MassiveArmy/Resources/MassiveArmy/Planets/DefaultPlanet.asset` now explicitly enables stamps for this project. The development scene and original start-menu modes use private copies of it, preserving menu match seeds without modifying the saved asset. Use **Massive Army → Planetary → Select Default Planet** to locate it in Unity.

New/unrelated PlanetDefinition instances still default to stamps off. `-planet-stamp-terrain` enables stamps and `-planet-legacy-terrain` forces legacy relief for a session. The saved preset currently opts in without either flag.
