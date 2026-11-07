# Planet/system size experiments

Select `Assets/MassiveArmy/Resources/MassiveArmy/Planets/DefaultSystem.asset` in Unity and set **Size Multiplier** before starting a new session. Use any multiplier from 1 to 1000. A running session is not resized in place.

The default homeworld has a 120 km radius (240 km diameter):

| Multiplier | Radius | Diameter |
| --- | --- | --- |
| 1 | 120 km | 240 km |
| 10 | 1,200 km | 2,400 km |
| 100 | 12,000 km | 24,000 km |
| 1000 | 120,000 km | 240,000 km |

The PlanetDevelopment component also has a System Size Multiplier override and 1x/10x/100x context-menu presets. Its default of 1 uses the system asset's setting. Standalone launches accept `-planet-size-multiplier 10` or `-planet-size-multiplier 100`; the command-line setting overrides both Inspector settings. `-planet-radius` supplies the unmultiplied home radius in metres.

Planet/star radii, binary separation, planetary orbital radii and galaxy distances scale together. Gravitational parameters scale with the square of the multiplier to preserve surface gravity. Orbital periods consequently increase with the square root of the multiplier. Local buildings, vehicles, movement speeds, terrain relief, atmosphere heights, streaming ranges and army capacity are not enlarged. Generated cloud properties still use their existing weather rules. Runtime settings are copies; assets are not rewritten.

This changes configuration, not terrain or army budgets. The GPU-authoritative army and million-unit path remain intact. Larger systems do not automatically allocate an army for every planet. The terrain tree's existing maximum level and float directional coordinates remain limits: 100x is an experimental precision/LOD stress case, not a claim of identical near-ground detail or performance. The existing floating origin and double celestial positions remain in use.

Validation: Synthetic Front > Planets > Validate system size multipliers checks 1/10/100x generation, radius/spacing/gravity ratios, finite orbital states, fixed terrain relief, legacy separation and unchanged source settings. Results: `TestResults/Planetary/size-scale.json`.

Performance acceptance has not been measured for these scales. Compare equivalent standalone runs at 2560x1440 with the same seed, army count, camera path, simulation speed and quality; report median/p95 frame time, simulation rate and measured memory. Do not lower army count or terrain budgets to claim a pass. Larger separation also makes travel take longer at unchanged ship speed.

Large-world terrain uses per-face nearby reference points computed in CPU double precision, plus rationalised local direction differences on the GPU. This avoids subtracting planet-radius float positions for streamed terrain vertices without enabling native GPU doubles. The existing directional UV resolution and maximum tree level still apply. The render camera stays at zero; the player's feet/vehicle body are offset from that eye position.

Cloud detail-noise size automatically follows the applied world multiplier: detail-feature size follows multiplier^1.5. A 10x world has about 31.6x detail size, and a 100x world has 1000x detail size (ten times larger than the previous 100x-world noise). Broad coverage already uses angular coordinates and therefore grows with planet radius. Cloud rendering and light-cache/shadow integration share this scale; no separate manual noise setting is needed. Sample counts and texture sizes stay unchanged.

The scale validator now also dispatches the shared GPU precision function at nine nearby vertices on each cube face, at 1x/10x/100x, and checks relative-position error below 1 mm against CPU double reference coordinates. This tests coordinate reconstruction, not the resolution of terrain source maps, live driving jitter, or standalone GPU frame cost.

Cloud evolution: each planet weather profile now exposes Detail Counter Drift (default 0.2 of angular wind speed), Interior Softness (default 0.25) and Interior Extinction (default 0.4). Detail noise counter-rotates relative to the broad banks using a separately wrapped phase, avoiding jumps at wind wrap. Dense cores use broader density lobes and extra extinction; angular scattering becomes more diffuse at depth. The shared density function drives visual marching and the lighting cache, including the world-size detail scale. No additional noise fetches, march samples, particles or textures were added.

Cloud profile refinement: the shared layer now reuses broad/medium/small noise to vary column thickness (with more small-scale contribution), lowers mean thickness, tapers the column toward low-coverage edges, and uses linear sloping vertical falloffs. The analytic fallback's integrated density factor matches the new trapezoidal profile. No additional noise fetches or ray steps.

The maximum is now 1000x; cloud noise follows this automatically. This raises the available range without changing the selected multiplier. 1000x is an experimental stress setting; existing terrain LOD depth and directional-coordinate precision limits still apply.

Cloud aliasing refinement: quintic interpolation smooths enlarged noise-cell boundaries without extra texture fetches; camera-relative layer altitude avoids a planet-radius subtraction. Nearby resident clouds add one fine detail octave, fading out as detail LOD increases; non-resident bodies keep the two-octave erosion path. The linear vertical profile is analytically averaged across each view-ray step to reduce sheet-like bands without increasing march count. Additional near-cloud noise cost is unmeasured in standalone; planet scale settings are unchanged.

Live tuning: start a session, then choose Synthetic Front > Planets > Edit active planet weather. In the selected runtime PlanetDefinition, expand Weather and adjust Live Cloud Noise Tuning. Detail Noise Frequency controls feature count/size; Detail Erosion controls holes and wisps; Fine Noise Strength/Frequency controls the near third octave; Noise Contrast adjusts separation. Rendering and shadows consume these values each frame. Use Synthetic Front > Planets > Copy active weather settings to copy JSON for sharing before stopping Play; runtime edits are not persisted to authored assets automatically.

Live lighting controls in Weather: Multiple Scattering approximates internal light bounces; Forward Scattering controls the sun-facing response; Interior Ambient sets the darkest diffuse cloud shade; Cloud Brightness scales cloud light; existing Light Scattering and Internal Shadow Contrast remain available. Cloud Shadow Strength independently adjusts terrain-shadow darkness. The light-bounce control is an approximation, not path tracing.

Live Quality presets: Low = half resolution / up to 6 ray steps, Medium (default) = half / 12, High = 75% / 16, Ultra = full / 24. Switching quality recreates only view render targets and is immediate. The optical-depth cache now follows quality: Low 64x32x8, Medium 128x64x8, High 256x128x12, Ultra 512x256x16. Ultra can cost substantially more than Medium (four times as many integration pixels and twice the maximum cloud samples); no standalone GPU acceptance measurements have been made for these opt-in tiers. World scale and army capacity are unchanged.

Large-planet cloud refinement: screen-size detail retention now overrides absolute-distance falloff. Full Detail Screen Fraction defaults to 0.2 (full detail while projected diameter is at least 20% of screen height); the analytic fallback still begins below 2% and completes below 1.2%. Large non-resident planets can use eight view samples rather than the previous unconditional four.

Advanced live resolution overrides (zero uses the quality preset): Render Scale Override (0.25–1), March Steps Override (4–64), Shadow Resolution Override (power of two, 64–4096 wide, height half the width), Shadow Layers Override (4–16). These change rendering cost and memory, not army simulation or the planet scale multiplier. Ultra shadow storage is 8 MiB versus Medium's 256 KiB; it also integrates many more lighting samples. Performance is not measured for these optional settings.

The view-ray shell intersections now use CPU-derived eye altitude and a rationalised quadratic root, avoiding cancellation both in planet-radius differences and near-surface roots. A GPU check verifies a 2 km shell boundary at the 1000x home radius within 1 cm. This is a coordinate check, not a visual or standalone performance acceptance result.

Automatic per-body precision: PlanetDefinition selects CameraRelative, Compensated or ExtendedLocal from the float spacing at its own radius, independently of the system multiplier. CameraRelative is used below ~65.5 km radius; Compensated through radii below ~2,097 km; larger planets use ExtendedLocal CPU surface projection and stable normal differences. CPU authoritative positions remain double precision in every mode. The Inspector displays the selected mode and estimated planet-centred float spacing.

ExtendedLocal avoids a Double3 -> float direction -> Double3 round trip during vehicle floor correction, preserving tangent movement rather than snapping it onto planet-scale float increments. Ship ramp endpoints, initial ship placement, orbital launch origins and RTS surface camera placement also avoid planet-sized float position intermediates. Validation covers 1000 centimetre movements on the 1000x home radius.

This policy does not convert the million-unit GPU army to native FP64, expand its coordinate record, increase terrain tree depth or recover detail missing from float directional addresses. Those are separate remaining constraints at extreme scales. It is not a claim that every 1000x artefact is resolved. No standalone performance acceptance measurements have been made for these precision changes.

Shadow resolution overrides now support 4096; all quality preset defaults remain unchanged. At 4096x2048, the RFloat cache alone costs 256 MiB with 8 layers or 512 MiB with 16 layers. Every cache rebuild also performs substantially more lighting work; this optional maximum is not a performance recommendation or a 60 FPS claim. The active planet Inspector shows its selected cache dimensions and memory estimate. Limit checks do not allocate the maximum-sized GPU texture.

Noise size and octaves: Noise Feature Size is a live multiplier on automatic world-size scaling; values above 1 broaden features and values below 1 produce finer breakup without extra samples. Detail Noise Frequency is the inverse-style frequency control, not an octave count. Detail Noise Octaves now exposes a maximum of 2–16 (default 3), with Octave Persistence and Lacunarity. Additional fine octaves fade with distance, remain disabled for non-resident distant bodies, and stop before float coordinates lose useful fractional precision. Their GPU cost is optional and unmeasured; start by adjusting feature size before increasing count.

Large-planet profile and distance adaptation: the supplied user cloud values are preserved in Resources/MassiveArmy/Planets/LargePlanetClouds.json. Synthetic Front > Planets > Apply saved large-planet cloud preset applies them to the active runtime planet without changing planet scale or generated-world defaults.

Adaptive Local Detail preserves the broad profile and blends in a separate metre-sized density layer near the resident planet's cloud shell. Local Detail Distance (default 50 km), Local Detail Feature Size (350 m) and Local Detail Strength (0.25) are live controls. Distance is measured to the actual shell, including below it. Wind phase and tile anchor are computed in CPU double precision, then only bounded local offsets go to the GPU. The added layer costs two noise fetches where enabled and does not replace the global shadow cache's large-scale lighting approximation.

Cloud Altitude and Cloud Thickness are now honoured without silently clamping them to a fraction of atmospheric height. Draw bounds and ray-shell extent include clouds above the nominal atmosphere. Octave weights below 0.001 stop contributing extra samples. Blanket GPU FP64 has not been enabled; source noise resolution, ray sampling and shadow-cache resolution are separate from arithmetic precision. Live appearance and standalone GPU cost remain unverified.

The supplied large-planet cloud values are now serialized directly into DefaultPlanet.asset's weather block, so new starting worlds load them automatically rather than requiring the optional preset menu. Generated other worlds retain their procedural weather. A field-by-field validation compares the authored weather block against every value in the saved user preset. The planet scale asset was not changed.
