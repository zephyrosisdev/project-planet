# Planetary sunlight scattering

Planet weather now supports single-scattered Rayleigh light and a forward-scattering aerosol (Mie approximation). Rayleigh coefficients vary across RGB, producing a blue daytime sky and redder light after long paths near sunset. Aerosols create the broader bright region around the star. Both view transmission and sunlight transmission use Beer–Lambert attenuation; the solid planet blocks direct sunlight on the night side. Clear atmospheres work without clouds, and airless planets still skip weather rendering.

The settings are in each `PlanetDefinition.weather`:

* `physicalAtmosphere`: enables the new scattering; disabling it retains the original tinted haze for comparison.
* `rayleighStrength`: molecular scattering multiplier, default 1.
* `aerosolStrength`: aerosol scattering/extinction multiplier, default 1.
* `haze`: adjusts aerosol density in physical mode; it no longer disables molecular scattering when zero.
* `surfacePressure`: scales both molecular and aerosol density.

During Play mode, **Synthetic Front → Planets → Edit active planet weather** selects the active planet for editing. The former `atmosphereColour` remains the legacy haze tint; physical mode uses Earth-like RGB molecular coefficients. Alien atmospheric chemistry, ozone absorption, spectral solar irradiance and full multiple scattering are not implemented. Cloud lighting retains its existing bounded multiple-scattering approximation, with atmospheric attenuation added to its direct sunlight.

## Daylight preview

During Play mode, open **Synthetic Front → Planets → Atmosphere time preview**. Use the solar-hour slider, Dawn/Noon/Dusk/Night buttons, or **Play day cycle**. Choose a full-day duration of 15, 30, 60, 120 or 300 real seconds. Pause the sweep to inspect an angle. The preview captures a fixed local horizon at activation; it is a lighting diagnostic, not planetary rotation or a persistent world clock. Gameplay and production speed remain unchanged. It drives the real directional light, atmosphere/cloud lighting and primary star disc together. **Restore actual sun**, closing the window, or changing planets ends the override. A paused game also pauses automatic sweeps; manual scrubbing still works.

An initialized planet must be active. Otherwise the controls show a waiting message. This diagnostic adds no simulation objects or army state. Fast sweeps change shadow maps more often than the normal celestial sun; this is not a performance acceptance scenario.

## Rendering and bounds

Exponential molecular/aerosol scale heights are 0.125 and 0.01875 of the authored atmosphere height. This maps a 64 km atmosphere to approximately 8 km and 1.2 km density scale heights. Earth-like vertical optical depths are RGB (0.0464, 0.108, 0.264) for molecular scattering and 0.024 for aerosol extinction; aerosol single-scattering albedo is 0.9. These are a compact game RGB model, rather than a spectral atmosphere solver.

Each visible body's sunlight density columns are cached in a 128×64 RGFloat texture-array slice. Coordinates concentrate precision near the surface and horizon. A 64-step compute integration builds a slice only when its atmosphere-height/radius ratio changes. Pressure, scattering strength, sun rotation and observer movement reuse those density columns. A view owns at most 17 slices and builds at most one per frame, nearest first. A body waiting for initialization uses the original haze; it never reads an uninitialized slice. The cache is released with the view.

Atmospheric integration retains six view samples, including the existing split around cloud entry. Each step uses the cached sunlight columns, normalized Rayleigh and Henyey–Greenstein phase functions, RGB extinction, and an analytic constant-density segment integral. No nested per-pixel sunlight raymarch is added. The solid-planet horizon and a small solar-disk transition bound direct sunlight.

A second half-resolution RGBAHalf target stores RGB transmission. Depth-aware compositing multiplies the opaque scene by transmission, then adds in-scattering. This avoids reducing colour extinction to one alpha value or copying the full-resolution scene colour. Existing opaque depth, compressed distant-body depth and sealed-cabin exclusion continue to bound integration. The atmosphere cache costs 1,114,112 bytes; the extra transmission target costs 7,372,800 bytes at 2560×1440 with 50% weather scale. These are texture-format sizes, not measured total driver allocations.

Atmospheres of different bodies retain bounded far-to-near radiance blending with scalar alpha between bodies; transmission within each body's atmosphere and final opaque-scene extinction are RGB. Exactly ordered overlapping atmospheric volumes, terrain-shadowed air and secondary-star atmospheric illumination are not provided by this approximation. The primary star supplies the scattering illumination.

This rendering work does not change army authority, navigation, unit counts or the million-unit path. No per-unit CPU data or objects are introduced.

## Reference and reproduction

The separation of density columns, transmittance and phase functions follows the standard radiative-transfer formulation described in [Bruneton's atmospheric-scattering reference](https://ebruneton.github.io/precomputed_atmospheric_scattering/atmosphere/functions.glsl.html). This implementation uses a much smaller single-scattering approximation; it does not implement that reference's full multiple-scattering model or four-dimensional scattering tables.

`MassiveArmy.EditorTools.PlanetAirBuild.Build` builds an isolated standalone graphics fixture with the frozen pre-change shader and density include under `Tools/Fixtures/PlanetWeatherPreRayleigh*`. An open editor also accepts `build` in `TestResults/Planetary/Atmosphere/request.txt`. The temporary baseline shader, density include and empty fixture scene are removed after building.

Run `Atmosphere.exe -batchmode -force-d3d11 -atmosphere-validation -planet-output <directory>` for physical scattering and add `-atmosphere-legacy` for the exact old shader. The fixture checks cached GPU columns against independent 8,192-step CPU integrals, wavelength response, sunset, night-side occlusion, density, vacuum, aerosol glare, cabin exclusion and finite orbital output. It captures images and compares the same 2560×1440 near-cloud view with 40 warm-up and 120 measured frames.

Reports distinguish fenced CPU-plus-GPU draw time from GPU timestamps, and report frame time, render rate, process memory, managed memory, Unity allocations/reservations and graphics-driver memory. The isolated scene has no army and no simulation rate; its performance is not a combined-world 60 FPS or million-unit acceptance result.
