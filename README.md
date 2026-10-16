# Synthetic Front

Playable base-building RTS and GPU army prototype for Unity 6000.6.4f1 and HDRP 17.6.0.

An opt-in [planetary development scene and migration audit](Documentation/PLANETARY_MIGRATION.md)
adds spherical GPU navigation, adaptive CBT terrain/ocean, a globe camera,
first-person radial gravity and distant scaled rendering. Open
`Assets/Scenes/PlanetDevelopment.unity`; the normal flat RTS scene remains the
production entry point. The bounded CBT prototype is documented
[here](Documentation/PLANETARY_CBT.md); Earth-scale allocation/precision and
production RTS/vehicle/ship integration are still pending. Planetary tanks now
support [GPU click/drag selection and independent move/stop orders](Documentation/PLANETARY_COMMANDS.md).
The planet scene now defaults to the [current RTS unit models, buildings and shared in-game HUD](Documentation/PLANETARY_RTS.md),
with construction, training queues and research. Combat, fog and AI migration are still pending.

Generator 4 adds configurable wrapping worlds, orthographic scrolling and
interactive 3D smog. Use **WORLD SETUP** in the main menu; see the
[world setup, fog Look Dev and validation guide](Documentation/WRAPPING_WORLDS.md).

Open `Assets/Scenes/MassiveBattle.unity`, enter Play, select **50K**, then **START BASE BUILDING RTS**. This reserves 50,000 GPU slots and starts each faction with a Command Centre, Engineer, Scout and two Infantry. **DEPLOY ARMIES** still starts the original stress battle. Units live in GPU buffers; there is no GameObject, Transform or ECS entity per unit.

Select your Command Centre, build wind or solar generation, place a Mine on a metal deposit, and construct Barracks. Select Barracks to queue Infantry and Engineers; right-click terrain or a friendly unit to set its rally target. A Research Lab and Command Centre upgrade open the technology graph. Production, construction, research and upgrades spend real resources and take time.

Base mode adds five resources, finite seeded deposits, storage, a power grid and batteries, 49 building definitions, 31 ground/air/naval/support/strategic unit definitions, and 25 technologies across five eras. It includes walls and automatic gates, CPU building damage and repair, GPU fog and radar, scout searches and waypoint patrols, paid enemy base AI, GPU transports, aircraft servicing, strategic missiles and interception.

Use **B** for construction and **T** for research. **F1–F5** select the Command Centre, Barracks, Factory, Airbase and Naval Yard. Ctrl+number assigns a GPU control group; number recalls it, and Shift adds. Select reconnaissance units, choose a search pattern, and press **SCOUT ORDER**. For Route, click up to 16 waypoints and right-click to begin. Select transports and press **LOAD TRANSPORT** / **UNLOAD** near passengers. These bindings are configurable on `BattleApp.baseHotkeys` in the Inspector.

- [Base-building systems, controls and prototype limits](Documentation/RTS_BASE_BUILDING.md)
- [Swarm survival: ten-minute preparation, countdowns and doubling waves](Documentation/SWARM_MODE.md)
- [Swarm GPU and wave-clock validation](TestResults/swarm-validation.json)
- [RTS economy/progression validation](TestResults/rts-validation.json)
- [Combined-arms GPU validation](TestResults/rts-gpu-validation.json)

Click/drag selects cyan tanks. Formation centres are spaced 5.2 world units apart with extra hull clearance. Right-click moves in a grid; right-drag previews formation width and facing. Blocked positions are skipped and the formation extends into clear rear rows. Repeated orders immediately reuse a fully selected group. Attack-move and Stop remain available as command buttons. Shift adds to selection and Ctrl removes. Use WASD or arrows, middle drag and wheel to control the camera. M opens the biome atlas (legend, terrain/roads, hover details and export). In stress mode, F1 toggles the GPU panel; Escape opens settings. The main menu includes 10K–1M stress configurations.

- [Architecture, controls, validation and limitations](Documentation/GPU_ARCHITECTURE.md)

- [GPU memory budgets](Documentation/GPU_MEMORY.md)

- [Measured scale benchmarks](Documentation/BENCHMARKS.md)

- [GPU validation results](TestResults/gpu-validation.json)
- [Biome generation validation](TestResults/world-validation.json)
- [Generated biome atlas](TestResults/BiomeMaps/biomes-73129.png)

Procedural landscapes use a coherent temperate region with connected ridges, foothills, eroded catchments and sediment-driven material masks, clustered vegetation and rocks, lakes/ponds/rivers, an ocean, and dry-land road networks. Choose a new seed in Settings for the next battle. Base mode fog of war uses animated gradient noise advected by a terrain-aware curl flow map, with separate darkness for unexplored and explored ground. Tune it live through **Massive Army → Art → Battle Atmosphere and Fog Settings**.

Terrain surfaces now use generated tileable albedo, normal, and height maps for all eight biome layers plus roads, with height blending and cliff rock projection. See [terrain art assets and rebuild controls](Documentation/TERRAIN_ART.md).

The battlefield grows with the deployed army: roughly 2,368 units across at 50K and 10,432 at 1M, with further growth if a seed lacks usable land. Deployment uses unique 5.2-unit slots with hull clearance on one connected landmass; isolated summits and islands are excluded. Navigation prefers open ground around banks and rocks, keeps obstacle detours stable, and detects stalled route progress to recover from circling. Fine-field lanes and predictive right/left passing reduce traffic jams on passable routes, with hull-sized contacts and a spacing margin for both teams. GPU density pressure resists compression and spreads traffic toward available space; density-aware passing, queue braking and damped contacts reduce vibration. This is a bounded maximum-density approximation, retaining individual tank steering and formation slots.

- [Deployment checks at all scales](TestResults/deployment-validation.json)
- [One-million-unit GPU spawn check](TestResults/million-spawn-validation.json)
- [Crowd compression and stability check](TestResults/crowd-validation.json)

The visual reference guides the cyan/red palette, modular neon buildings and procedural unit silhouettes. Art and balance are prototype quality; the original stress benchmark figures below predate the RTS systems and should not be treated as measurements of a mature base-building match.

