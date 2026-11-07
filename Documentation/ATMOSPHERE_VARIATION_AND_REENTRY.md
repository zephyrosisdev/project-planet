# Atmospheric variation and ship re-entry

Physical atmosphere rendering now includes a soft upper density taper, broad density noise and small solar/gravitational tide terms. The optical-depth lookup uses smooth interpolation rather than a visible linear cell transition. No extra ray-march steps or noise volumes are added: the existing weather volume is sampled once per integrated air segment, with the variation kept coherent along that short column. Cloud sunlight uses the smooth tidal term without another noise lookup.

Select **Synthetic Front → Planets → Edit active planet weather** during Play mode. The Planet Definition Inspector exposes **Atmosphere density noise** directly near the top; the same values also remain under **Weather → Upper atmosphere variation**. Atmosphere density currently uses one noise layer. **Weather → Live cloud noise tuning → Detail Noise Octaves** controls clouds separately:

| Setting | Default | Meaning |
|---|---:|---|
| Air density noise | 0.06 | Small broad optical-density variation |
| Air density noise scale | 8 | Angular feature scale; lower values make larger features |
| Air solar tide | 0.025 | Broad two-lobed variation aligned to sunlight |
| Air gravity tide | 0.012 | Variation aligned to the strongest external tidal source |

Gravitational direction/strength is selected using external `mu / distance³`, excluding the body itself and barycentres, and cached for one second. Solar forcing follows the actual sun direction, including the daylight-preview tool. The density multiplier is bounded to 0.8–1.2. This is a game approximation of atmospheric tides, not a global atmosphere, pressure, wind or climate solver. Atmospheric tides involve both heating and gravity; [NASA discusses thermal atmospheric tides](https://astrobiology.nasa.gov/news/how-life-could-help-atmospheric-tides-slow-a-planets-rotation/), while its [space-geodesy overview covers gravitational atmospheric tides](https://ilrs.gsfc.nasa.gov/about/reports/tides.html).

## Entry heat and plasma

The player ship samples altitude, pressure and velocity relative to its nearby planet. The mean density uses the same exponential scale height, upper taper and tide approximation. Small rendered noise is cosmetic and does not become a CPU density field. The heating signal scales with `sqrt(density) × speed³`, normalized for gameplay. It accumulates stored skin heat and cools gradually. Ionization responds more quickly to current density/speed, so weak upper-atmosphere glow can precede substantial skin heat.

Ordinary assisted flight does not produce a fireball. For a clear test, enter air at several km/s; faster and deeper entry raises the effect and stored heat much more quickly. Airless bodies and flight above the atmosphere do not generate new atmospheric heat. The temporary warp override is excluded. Heat survives the existing planetary ship handoff and continues updating when the pilot leaves the seat. Pausing freezes it. The flight HUD shows heat and ionization percentages, with an overheat warning above 75% heat.

The effect represents the heated/ionized shock-layer gas around a craft, distinct from a whole-planet ionosphere simulation. [NASA's entry-plasma overview](https://ntrs.nasa.gov/citations/20190026772) describes the underlying shock-layer process. Colours, activation thresholds, heat capacity and cooling are tunable game values, not physical spectroscopy or certified thermal-protection predictions. Heat is a signal and warning at this stage; this change does not add hull destruction, ablation, aerodynamic drag or new damage rules.

## GPU presentation

The editable asset is `Assets/MassiveArmy/Resources/MassiveArmy/ShipArt/ReentryPlasma.vfx`. Its custom HLSL blocks sample a windward ellipsoid matching the current prototype ship, project flow around the shell, advect a short wake and emit violet/blue or orange additive light according to ionization/heat. A generated soft turbulent texture breaks up the emission. The half-extents parameter supports later adaptation to other hulls.

One lazily created local-space Visual Effect component is attached to the player ship, with a fixed capacity of **2,048 GPU particles** and an emission ceiling of **1,600/second**. No CPU particles, particle GameObjects, particle rigidbodies, per-particle lights or fleet mirrors are created. Local simulation follows ship rotation and floating-origin rebasing. View resources are released when leaving the resident world; thermal data remains separate. State-to-VFX property updates run at most once per rendered frame. The native particle count is read only by the diagnostic, not by gameplay.

**Synthetic Front → Art → Build ship re-entry VFX Graph** rebuilds the graph/texture from the checked-in HLSL authoring sources. HDRP already supplies the VFX Graph package; no additional package was needed.

## Validation and remaining performance gates

**Synthetic Front → Diagnostics → Validate ship re-entry heat and GPU plasma** passes 11 checks, including vacuum/slow-flight behavior, heating/cooling, graph properties and actual visible GPU renders. It briefly enters Play mode when invoked from edit mode, creates an isolated additive scene and returns to edit mode afterward. If already playing, it keeps Play mode active. Render captures and JSON reports are under `TestResults/Planetary/Reentry`.

**Validate atmosphere limb variation** passes three graphics checks at 2560×1440. It captures uniform and varied controls using the same shader/resolution and records fenced CPU+GPU readback latency. Those timings include editor/driver scheduling and are not isolated GPU timestamps, full-frame measurements or a fog-budget pass. The capture verifies visible variation and supported shader compilation.

Army authority, representations and populations are unchanged. No full standalone baseline/current performance comparison, GPU/native memory accounting, combined-world stress pass, ≤3 ms p95 total-fog pass, or 60 FPS RTX 3070 claim is made. Those acceptance gates still require the equivalent standalone scenarios from the project policy; the capped effects and isolated editor checks do not replace them.
