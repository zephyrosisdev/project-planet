# Circular navigation compass

A curved, top-center **360-degree compass bar** now appears on foot, in ground vehicles and in the cockpit. It uses UI Toolkit retained elements and vector icons, with no world-space marker GameObjects or GPU position readbacks.

## Behavior

- Horizontal bearing places a target along the wrapping bar. A target on your current azimuth is centered, even when you still need to pitch toward it.
- Vertical misalignment shrinks its icon smoothly: maximum size when pitch matches, 36% size at 90 degrees or more of mismatch. An up/down chevron and the focused readout indicate the required pitch correction.
- Facing the target within about 3 degrees horizontally and 4 degrees vertically highlights it with a gold ring and an ALIGNED readout.
- Targets behind you remain near the bar's ends, with sideways rear cues. They never appear centered merely because they share your elevation.
- The focused target shows its name and distance. Planet distances are measured to the reference sphere's surface; ship and waypoint distances are point-to-point.
- N/E/S/W tick labels follow local planetary north near a planet and a fixed north frame in deep space. Roll does not rotate the compass. Nearly vertical views retain their last usable heading, and polar views retain a stable tangent north when geographical north is undefined.

The icon pitch scaling is the requested custom behavior. It is based on pitch error, so turning horizontally does not incorrectly make an otherwise level target appear smaller.

## Targets

- Your ship: mint-green ship outline, on foot/in vehicles, including a ship on another planet if its carrier record is available. It is omitted while piloting it; the compass center indicates your view bearing instead.
- Planets: blue ringed circles, with the selected flight approach destination in gold. The existing **P** flight control changes the selected destination.
- Tracked tribe order: amber diamond, using the order's canonical planetary position.

The nearby planet's center is omitted below 1 km altitude, avoiding a permanent directly-down icon when standing on its surface. Selection includes up to twelve planets total, prioritizing the selected flight destination and the current star system; a selected destination in another system is retained. Ship and order markers have reserved slots, for a maximum of fourteen markers. Selection is refreshed twice per real second; positions and bearings are evaluated each visible frame, including accelerated orbits.

Crowded bearings use three icon rows. The footer reports overlapping markers and any additional bodies outside the display budget. These are presentation limits: no ships, inventories or simulation entities are removed. All compass elements ignore pointer input and hide during modal menus or death. **Planet Development → Navigation HUD → Show Navigation Compass** disables the element.

## Research

Hello Games' [Atlas Rises notes](https://www.nomanssky.com/atlas-rises-update/) document cardinal directions and distance markers on No Man's Sky's compass, plus ship tracking arrows. Its [Worlds Part II notes](https://www.nomanssky.com/worlds-part-ii-update/) document east/west compass markers and distinct vehicle compass icons. These support a compass built around bearings, recognizable target types and distance information.

Bethesda's [Skyrim manual, Compass section](https://assets.ctfassets.net/rporu91m20dc/46SNPirCFGI0eC6w84UI8w/0f5b441e255a26b9371608ee71b73af3/manual_skyrim-le_x360_en-int.pdf) describes compass markers for nearby locations, quest objectives and a custom map target. It supplies the simpler location/waypoint comparison.

The exact No Man's Sky marker pitch-size algorithm was not documented in these sources. This implementation adds the requested smooth pitch scaling to those reference ideas.

## Verification

- Full runtime C# compile passes with existing warnings.
- `dotnet run --project Tools/IndustrialChecks/CompassChecks.csproj`: 112 direction/projection checks pass, covering front/side/rear bearings, pitch alignment, straight-up/down views, seam wrapping, invalid/coincident inputs, very large coordinate scales and bounded monotonic sizing.
- Direction arithmetic subtracts sector-based universe positions before calculating double-precision bearings. It does not convert whole galactic coordinates into floats.
- Target arrays and UI elements are allocated once. Marker drawing is bounded, target references refresh at 2 Hz, and text refreshes at 10 Hz.

Reports: `TestResults/Planetary/NavigationCompass/`. Live inspection reproduced nadir marker jitter. Near-vertical bearings now ignore float tangent residuals; rear-seam hysteresis prevents noise from alternating between bar edges. Standalone frame-time/GPU-memory performance has not been measured. No change is made to army simulation, fleet populations or fog algorithms, and no new performance acceptance claim is made.
