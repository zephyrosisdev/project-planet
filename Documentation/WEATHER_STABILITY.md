# Orbital weather stability correction

The user still reports a whole-layer blink in both standalone and Editor play.
The corrections below improved the result but do **not** close that report.
See the remaining-flicker investigation at the end of this document.

Cloud and atmosphere rendering is shared by offline Survival and multiplayer.
This is a client rendering update on protocol 12; no server or save migration is
required. Weather density, coverage, altitude, authored quality, 24-step Ultra
cloud budget, six air samples and bounded GPU cache sizes are preserved.

The main discontinuity was in atmosphere integration. A cloud with zero opacity
used one six-sample air march. An arbitrarily faint cloud used separate three-
sample marches before/behind its shell entry. This changed sample positions and
large-scale density probes abruptly. The new path samples the air at six fixed
locations and analytically partitions optical depth inside the segment crossed
by the cloud. Correct front/back composition is retained as cloud opacity tends
to zero. The physical and legacy haze paths use the same continuous scheme.

Fine cloud erosion previously sampled detail smaller than a pixel or ray step.
Existing noise mips now use the larger footprint, including octave frequency and
sun-cache texel/step size. Close detail remains available when resolved. Noise
coordinate warping fades continuously into mip filtering, preventing a phase
jump when the selected mip crosses an integer. These changes add no ray steps,
noise texture fetches or persistent rendering buffers.

Weather rays now derive from pixel centres instead of interpolation across a
moving bounds quad. The existing off-axis tangent bounds are retained. Depth-
aware upsampling compares the represented depth footprint rather than only its
nearest corner, avoiding complete tap rejection on steep faces while retaining
thin-foreground occlusion. Each weather draw establishes viewport/clipping state.

## Regressions and scope

RTX 3070 Direct3D 12 GPU regressions used the saved L-locked orbital attitude,
120 km planet, matched frozen noise/wind and a 2560x1440 sampling footprint.
Only public universe/view data was copied into disposable diagnostic fixtures;
the original server save was read without modification.

- A cloud opacity change from 0 to 0.000001 caused a maximum normalized colour/
  opacity jump of 0.218426 in the previous scheme; the corrected maximum is
  0.000000894. Both physical and legacy air modes are exercised.
- Across 96 locations with approximately 6 m perturbations, p95 opacity changes
  fell from 0.026272 to 0.001936. Mean opacity was 0.458744 before and 0.457026
  after; the test rejects smoothing that materially removes the layer.
- Noise mip-boundary jumps fell from 0.087251 to zero at measured precision.
- Depth-ramp compositing and thin foreground rejection pass. The initial
  nearest-only regression had 1,488 missing pixels at its steepest slope.
- All 133 existing off-axis bounds/flight checks pass, including bounded versus
  full-screen GPU weather at both projection Y orientations. Earlier interpolated
  rays failed a strict alpha-difference gate; pixel-derived rays pass it.

Raw GPU reports are in TestResults/Planetary/WeatherStability. These are rendered
regression fixtures, not a release FPS, full gameplay or capacity acceptance pass.

The orbital sweep is an opt-in GraphicsProbeClient diagnostic using a copied
universe and saved view. It changes presentation only; no runtime command bypass
or server player teleport was added. Normal clients exclude the session probe.
Early attempts with resized render-request targets clipped to the window's
render area; these were test-harness failures, not proof of the reported gameplay
cause. A persistent target and explicit pixel rect correct that setup. Early
public-universe fixture swaps also failed an unrelated character-position restart
gate; the final paired fixtures project diagnostic characters onto their copied
terrain before testing. The failed reports remain available. One isolated Editor
readback run exhausted staging memory; the final fixture reads a one-pixel copy
and completes with bounded staging allocations.

## Paired standalone render cost

The same GraphicsProbeClient binary rendered the copied 120 km universe and
saved L-locked view into a persistent 2560x1440 HDRP target, with GPU completion
fences and diagnostic trace readbacks in both runs. After 10 s warmup, each run
sampled 30 s. Baseline selects the prior point-noise/opacity-dependent air layout
variants; current selects the corrected variants. Starting checkpoints match.
The co-hosted dedicated server and headless peer use the same inputs.

Baseline median/p95 frame intervals were 15.046/17.138 ms; corrected intervals
were 14.442/15.931 ms. Client peak working sets were 1206.1/1186.9 MiB and observed
private-byte peaks were 2824.3/2842.0 MiB. These include diagnostic rendering and
readback overhead, exclude a real occupied cockpit/pilot HUD, and are not a full
gameplay 60 FPS acceptance pass. Both Relay runs and restart continuity checks
pass. Raw memory, server tick/rate/bandwidth and frame measurements are in
TestResults/Multiplayer/weather-render-comparison.json (Live/6eee0e5a54de and
Live/f8e60b6c3be9). The source server simulation is unchanged by this update.

The final motion/air-limit/mip regressions also pass on Direct3D 11. Delivery is
Builds/Playtest-20261114-191031. Client/server ZIP CRC and SHA-256 checks pass;
the normal client excludes session diagnostics and its packaged shader resources
match the validated build. The server managed assembly is byte-identical to the
previous protocol-12 delivery; only clients need updating. Full delivery evidence
is TestResults/Planetary/WeatherStability/delivery.json.

## Remaining full-layer flicker investigation

The corrected real-pilot fixture (Live/8ce698166ca9) includes the production
pilot command, authoritative orbital motion, cockpit windows, cabin masking and
pilot HUD. The complete accelerated visual orbit (Live/0d76152844a6) includes the
same occupied cockpit. Both Relay/reconnect/canonical attachment checks pass.
The reported whole-layer dropout was not reproduced; neither result establishes
that the user's issue is resolved. Capture overhead makes these diagnostics
unsuitable for performance acceptance. Source user saves were not modified.

An early cockpit run (Live/dcb20213d956) had mismatched native-window and target
dimensions and clipped its output. Its generic stationary-position restart
assertion also did not apply to an occupied moving ship. Those failures remain
recorded; the launcher now matches dimensions and validates the canonical
ship-local character transform for the pilot fixture.

`SurvivalBuild.WeatherTraceClient` produces a separate client with
`SURVIVAL_WEATHER_TRACE`, without session automation or gameplay overrides.
Launching it with `-planet-weather-trace` enables a bounded 4096-render history.
Ctrl+Shift+F10 saves JSON plus a screenshot; disconnect/exit saves JSON as well.
`-planet-weather-trace-output <directory>` selects the destination. Normal
clients and dedicated servers exclude this diagnostic code. The separate build
uses the same protocol and rendering as the current normal client.

The trace includes render dimensions, projection, observer/attitude, cabin state,
bounds, centre opaque depth, centre weather and reconstructed integration range,
cache readiness and sparse 64x36 opacity/light samples over opaque pixels.
The historical `terrainWeather*` names include nearby opaque cockpit surfaces;
they must not be interpreted as terrain-only measurements. GPU probes copy
centre pixels to 1x1 targets before readback, avoiding full-resolution staging
allocations for those samples. Diagnostics add overhead and are opt-in.

Raw investigation scope is recorded in
TestResults/Planetary/WeatherStability/remaining-flicker-investigation.json.
The next required evidence is a trace from an actually affected gameplay orbit.
