# Cloud visibility and flight instruments

Cloud view bounds use exact perspective sphere tangents in camera coordinates. This fixes off-axis clipping for both active and distant planets, with a full-viewport fallback when the camera intersects the atmosphere. Existing opaque/cabin depth occlusion, cloud shell intervals, and the twelve-sample near-volume budget remain in place.

Flight displays:
- Green: the existing numerical, multi-body unpowered coast forecast. It remains visible while flight assist is enabled; actual thrust/assist can change the future path.
- Blue: a local two-body osculating orbit reference near the closest planet, above its atmosphere. This is a local approximation, not a proof of long-term orbital stability.
- Curves use bounded procedural GPU ribbons with four visual subdivisions per physical segment, constrained Hermite tangents, derivative-based edge antialiasing and 48-segment route rings. No VFX particle simulation or army state is added. Numerical prediction integration and its existing per-frame CPU budget are unchanged.
- Altitude tape: metres/kilometres above the reference planet's nominal sea-level radius, with signed vertical speed. It is not a radar-altitude sensor over hills.
- Bank: roll against the local planetary horizon. Vertical flight and flight outside planetary range show an undefined bank rather than inventing a level plane.
- Velocity strokes: 24 instrument strokes near the altitude tape indicate forward/reverse/lateral motion relative to the reference planet. Their animation is capped at warp for legibility. They are display cues, not physical space debris.
- Prograde uses the camera field of view and clamps to the display edge for off-screen/aft velocity.

Validation action: `flight-weather`, report `TestResults/Planetary/FlightWeather/editor.json`. Standalone weather validation additionally prepares and enters a second planet through the real ship handoff, checks cockpit instruments and renders clouds at five camera yaws. Full-resolution weather timing remains a paired fenced render delta, not total fog GPU timing.
