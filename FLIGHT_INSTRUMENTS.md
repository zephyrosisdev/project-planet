# Cloud visibility and flight instruments

Cloud view bounds use exact perspective sphere tangents in camera coordinates. This fixes off-axis clipping for both active and distant planets, with a full-viewport fallback when the camera intersects the atmosphere. Existing opaque/cabin depth occlusion, cloud shell intervals, and the twelve-sample near-volume budget remain in place.

Flight displays:
- Green: the existing numerical, multi-body unpowered coast forecast. It remains visible while flight assist is enabled; actual thrust/assist can change the future path.
- Blue: a local two-body osculating orbit reference near the closest planet, above its atmosphere. This is a local approximation, not a proof of long-term orbital stability.
- Curves use bounded procedural GPU ribbons with four visual subdivisions per physical segment, constrained Hermite tangents, derivative-based edge antialiasing and paired holographic route bars. No VFX particle simulation or army state is added. Numerical prediction integration and its existing per-frame CPU budget are unchanged.
- Altitude tape: metres/kilometres above the reference planet's nominal sea-level radius, with signed vertical speed. It is not a radar-altitude sensor over hills.
- Bank: roll against the local planetary horizon. Vertical flight and flight outside planetary range show an undefined bank rather than inventing a level plane.
- Travel markers: up to 48 pairs of vertical bars lie along the predicted curve, at distance intervals. A 48-position bounded trail records crossings behind the ship for aft views. Perspective and ship motion provide the flow; there is no screen-space scrolling animation. One extra 768-byte GPU buffer serves the player ship only.
- Prograde uses the camera field of view and clamps to the display edge for off-screen/aft velocity.

Validation action: `flight-weather`, report `TestResults/Planetary/FlightWeather/editor.json`. Standalone weather validation additionally prepares and enters a second planet through the real ship handoff, checks cockpit instruments and renders clouds at five camera yaws. Full-resolution weather timing remains a paired fenced render delta, not total fog GPU timing.


## Planet, moon and stellar orbit guides

While piloting, **O** toggles celestial orbit guides and **P** cycles surveyed planet/moon approach targets in the current star system. The closest known landing body is selected initially. Blue paths show planets, violet paths moons, and amber paths orbiting stars. Selected-body paths are brighter. Binary planets follow their actual barycentre parent rather than an arbitrarily chosen star.

The complete ellipse is displayed in its current parent's frame, anchored to that parent's current position. The selected target also has a direction arrow and **+1 min**, **+5 min**, **+15 min** position markers. Off-screen positions keep labelled edge indicators (including AFT); the nearby direction arrow uses a short adaptive lead so it stays visible when a whole minute of motion leaves the viewport. Those markers evaluate the whole parent hierarchy at the future time, including a moon's moving planet and a planet's moving stellar system.

The approach readout gives range to the body's nominal surface, signed closing speed (positive means approaching), and ship/body relative speed. These values and markers help plan interception; they do not automatically solve or fly a landing manoeuvre.

The visual sampler uses the same prescribed Kepler orbital elements as the celestial simulation. It supports moons when present in the celestial catalogue; this display change does not generate new moons or replace the existing body-motion model with an N-body simulation. Normal-play survey visibility applies. The renderer caps at twelve paths, 193 samples per path, and refreshes at most one path per rendered frame. Samples concentrate around the body’s current position and refresh once per second to keep the nearby curve attached. Encountered bodies within the preparation region are navigable before survey/terrain handoff. Ribbons now use a 3.2-pixel nominal width. Radial visual compression preserves angular directions at stellar distances without creating gameplay objects or changing physics.

Validation action: `celestial-guides`. The Editor test compares forecasts to the actual GalaxyModel ephemeris, exercises an inclined retrograde moon around a moving planet, and renders guide ribbons at local and billion-metre scales. The standalone weather/flight regression additionally checks live path buffers, approach UI and paired guide-on/off render cost after a planet handoff.

## Warp arrival and cabin lighting

Warp analytically sweeps every destination atmosphere/terrain boundary along the whole step. A completed warp maps the saved normal flight velocity into the destination’s moving frame in either flight mode. K must be released before warp can rearm after an interception, so holding it cannot repeatedly skip normal gravity updates. With assist off, summed gravity continues while destination terrain prepares and after automatic handoff. Ordinary non-warp unassisted frame handoffs preserve inertial velocity.

With assist on, interception holds the ship at the destination-relative boundary while terrain prepares, then transfers at rest. Turning assist on during preparation holds the current offset rather than teleporting back to the initial intercept. Assisted hovering near a foreign planet (within 20 radii) tracks that planet’s frame before surface activation, including braking. Once transferred, the local frame follows the new planet. The cockpit displays preparation status.

Foreign worlds preserve the configured country count. Their geology skips countries with no local settlement/home; global procedural deposits remain available. This prevents zero-direction terrain queries from aborting four-country destination preparation. Cabin point lights remain, but the local fixed-exposure volume is removed.

Unassisted arrival/hover regression: run standalone with `-planet-arrival-gravity-check -planet-output <directory>`. It exercises the normal four-country setup; `-planet-country-count 2` provides an equivalent before/after comparison. Results: `TestResults/Planetary/ArrivalGravity/Results.md`.
