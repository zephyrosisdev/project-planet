# Planet approach and camera-focused terrain

Full terrain presentation now has a separate approach distance from surface simulation interest. Select **Synthetic Front → Planets → Select approach detail settings** to open `Resources/MassiveArmy/Planets/DefaultSystem.asset`.

| Setting | Default / purpose |
|---|---|
| Detail Activation Altitude | Minimum full-detail altitude: 500,000 m above the surface. |
| Detail Activation Radius | Full-detail altitude is at least one planet radius. |
| Preparation Radius | Start background preparation another planet radius earlier, or Preparation Distance if larger. |
| Activation Altitude | Existing army/base simulation interest remains 80,000 m. |
| Sleep Hysteresis | Existing boundary hysteresis remains 20,000 m. |

For a 1,200 km-radius planet, preparation becomes eligible at 2,400 km altitude and full detail at 1,200 km altitude. Previously those distances were 280 km and 80 km regardless of planet size. The nearest eligible unprepared planet is automatically prepared, with only one automatic preparation in progress at a time. Full detail switches once the world is ready and it is the nearer planet by the existing hysteresis margin. An exceptionally fast warp can cross this range before preparation finishes; the existing swept warp-arrival handling remains in place.

The camera's centre ray now selects the terrain latitude/longitude to prioritise while piloting, driving, or walking inside the ship. A bounded visual intersection uses four radial height refinements, not physics GameObjects or a CPU terrain traversal. Its virtual LOD observer retains the ship's real altitude, so an orbital view does not request metre-size meshes at the look point. The real observer is retained as a secondary focus for nearby geometry. Looking into empty space falls back to the ordinary observer focus; RTS retains its existing selected-ground focus.

Both streamed terrain and the adaptive CBT fallback receive that focus. Frustum/horizon culling continues to use the real camera. Streamed terrain still has 384 patches by default, 34,848 bytes of residency/history buffers, the same maximum depth and the same pixel-error setting. Detail moves within that budget; it is not unlimited maximum tessellation. Contact queries, navigation, armies and material quantities are unchanged. Full planet resources can become resident earlier, so memory is committed earlier during an approach.

## Gravity

In the normal galaxy setup, the ship's unassisted integrator sums gravity from the galaxy's celestial body records independently of loaded planet worlds. Loading terrain does not enable a gravity source. Flight assist deliberately controls velocity/hover, and orbit hold continually corrects orbital motion; disable those when comparing passive gravitational pull. The legacy non-galaxy setup has only its current-body gravity fallback.

An earlier warp-arrival defect did prevent inward acceleration while K stayed held; the existing arrival-gravity report documents its previous fix and standalone checks. This change does not change gravity equations or claim a new gravity measurement.

## Verification and limits

- Runtime and Editor compilation pass.
- Approach policy checks cover 1× through 1000× radii, earlier visual detail while surface ticks sleep, preserved hysteresis, and configurable legacy thresholds.
- GPU gaze tests cover off-centre look targets, preserved altitude, fixed residency storage, looking into space, and adaptive CBT focus. Streaming and watertight CBT regressions are recorded in `TestResults/Planetary/Streaming/validation.json` and `TestResults/Planetary/Terrain/validation.json`.
- `TestResults/Planetary/GazeDetail/validation.json` records isolated Editor generation latency with CPU+GPU fencing and centre-ray calculation. This is not a standalone frame-time, VRAM, fog or million-unit acceptance measurement. Early sequential timings are retained separately because warm-up/Editor noise made them unsuitable as performance evidence.
- The broad system-size fixture currently fails its unrelated assumption that the authored cloud-octave setting matches the supplied preset; the user's cloud settings were preserved. The focused approach checks run independently of that preset.

A live multi-planet flight replay and matched standalone performance/physics validation remain outstanding. No 60 FPS acceptance claim is made for these rendering changes.
