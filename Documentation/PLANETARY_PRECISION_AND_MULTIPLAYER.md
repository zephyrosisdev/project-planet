# Camera origin and shared simulation coordinates

The local Unity gameplay camera is placed at world position (0, 0, 0) whenever RTS, FPS or pilot presentation is resolved. Camera orientation remains in the planet/universe axes; the world is translated, not rotated. The render origin is the canonical eye position, not the avatar feet or ship centre.

`UniversePosition` retains signed 64-bit sector IDs plus normalized double local offsets. `SpacePose` retains double positions/velocities relative to a stable frame ID. Ship-local occupants retain small local coordinates. Ground-player positions now remain double planet-local positions, including contact displacement; converting a planet-sized float back to double cannot recover lost bits.

`FloatingOrigin.Anchor` updates one presentation context exactly to an observer; stationary observers do not emit redundant events. Its original threshold-based `Update` remains available for other callers, but gameplay camera presentation uses Anchor. `RenderPosition` subtracts the canonical origin before converting to float. `RenderToUniverse` reverses a local render position. `PlanetDevelopment.RenderBodyPosition` and `RenderToBody` convert bounded body-local proxies through that same context.

Buildings, resource markers, ship and nuke emitters are presented from their canonical positions each frame. They do not accumulate repeated float translations. Nearby player queries run against the already synchronized native colliders; there is one observer capsule and bounded ship/building shells. GPU army buffers, cell IDs, orders and simulation ticks are never shifted when a camera moves. Ship frames publish parent-axis angular velocity in radians/second, including the resulting occupant point velocity.

## Multiplayer contract

Each client/process owns its own render origin, camera and bounded native presentation scene. The authoritative host does not take another client's Unity origin as a world coordinate. Two clients can observe the same canonical entity from different local origins without changing its simulation state.

Future replication must carry authoritative entity IDs, body/frame IDs, canonical sector/local or frame-local poses, velocities, orientation, simulation tick and ownership. An interior occupant should be replicated as ship frame ID plus local pose; the authoritative ship pose resolves that occupant for each observer. Frame registration/ID assignment belongs to session authority. Frame ID 1 currently identifies the single prototype ship; it is not a multiplayer ID allocator.

Serialize canonical state, never Unity Transform positions or Rebased deltas. Interpolation/reconciliation must happen in the shared frame before projecting into the receiving client's current origin. Camera-only anchoring must not generate network movement, add velocity, rewind simulation, or transform a GPU army buffer. Client interest regions must remain bounded; adding a CPU mirror of the army or a per-unit GameObject is outside this architecture. A future network design needs an explicit GPU host/server strategy and bounded GPU snapshots; headless server support is not provided here.

The two-observer validation exercises coordinate independence and shared ship-frame resolution. It is not a network session test. Transport, authoritative ownership, replication, prediction/reconciliation, security and bandwidth budgeting are not implemented. Simultaneous split-screen views with different origins would also need per-view rendering and separate native proxy/physics contexts; this change does not shift one shared native scene differently for two cameras.

## Remaining precision limits

Camera-at-zero is a presentation guarantee, not arbitrary precision throughout the engine. Nearby transforms and native PhysX queries still use floats. Camera-relative ship/local physics avoids planet-sized values there. Planet terrain/navigation and GPU army surface data still use float directions/coordinates, with centimetre-scale quantization possible at 120 km radius. Deep terrain/local GPU precision and regional navigation remain separate work; distant bodies continue to use the existing visual scaled-space projection. No army simulation or shader source was changed for this camera/observer stage.

Canonical positions and view matrices are stable across client rebases; they are not a promise of bitwise deterministic cross-platform PhysX. Multiplayer collision/authority must be designed explicitly around canonical state and local proxy regions.

See `TestResults/Planetary/Precision` for exact build, coordinate, native ship, menu and production regression reports. The prior million-unit performance gate remains unresolved; this observer/presentation change makes no new million-unit performance acceptance claim.
