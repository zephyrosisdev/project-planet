# Ground factory multiplayer checkpoint

Multiplayer now exposes ground power, water and routed conveyors through the
existing industrial authority and journal. Offline commands and simulation remain
the shared implementation. This is an incomplete survival multiplayer checkpoint.

## Controls

Open F4 beside an owned stationary machine and choose **POWER OUTPUT**, **WATER
OUTPUT** or **BELT OUTPUT**. Close F4 to walk to the destination, reopen it within
12 m, and select **Connect …** on the destination. Power begins at a provider;
water begins at a pump/reservoir. Both endpoints must be owned by the actor.

The pending-route section can add a routing point at the player's current feet
plus the chosen support height. Walk between points; use Undo or Cancel as needed.
The server adds cable socket exits. Power/water routes allow eight user points
and 50 m total length; conveyors allow 16 points and 100 m total length. These
controls use the existing multiplayer menu/input capture, so simulation continues.

Remove power at its consumer, remove water at its source, or recover a conveyor
from either nearby owned endpoint. Conveyor construction spends one plate and
one gear per 5 m, rounded up, using the existing resource/activity receipts.
Recovery requires capacity for paid construction and any cargo and cannot be
replayed to duplicate refunds. Power/water retain their existing industrial costs.
Machines still require their normal paid crafted deployable items for placement.

## Authority, contacts and visibility

- The authenticated actor is taken from the server peer. The gateway checks
  ownership/reach, construction policy, finite same-body anchors, route budgets,
  endpoint type, slope and physical obstruction before issuing an ordinary
  journalled command. Mobile endpoints and ship-local/integrated module sockets
  remain unsupported by this ground adapter.
- CPU contact queries read stored collision prisms through a collision-only DTO,
  ignoring render vertex/normal/colour/triangle arrays. They never request render
  meshes, cameras or materials. Models generated only by presentation code use
  conservative catalogue boxes. Full collision/render parity remains a gate.
- Terrain, machine prisms, ship collision envelopes, reserved front discharge
  areas and existing conveyors can reject a route. Existing belt contacts use a
  revision-cached spatial index rather than testing every sample against every
  other sample on each command.
- Power/water routes and conveyor construction, cargo movement, receipts and
  recovery use the current world snapshot/journal. Industrial work continues
  independently of visibility. No competing factory save store was added.
- Public links contain kind, endpoint IDs and bounded canonical routing anchors.
  They exclude conveyor items, inventory and construction receipts. Link IDs use
  a stable negative presentation namespace derived from persistent entity IDs.
  AOI extents account for a link crossing the interest boundary; required endpoint
  presentation is subscribed alongside it. Endpoint buffers/receipts remain
  redacted outside authorized interaction reach.
- The client draws visible links in one combined mesh, rebuilds when geometry or
  endpoint availability changes, and rebases its anchor every frame. There are no
  per-item conveyor GameObjects. Dense-factory rendering performance is unmeasured.
- Network protocol is **4**, content label **survival-2**, diagnostic build marker **10**.
  Matching builds are required. Existing world persistence content remains version
  1 because the industrial connection schema was already present.

Windows atomic checkpoint replacement now retries only known transient sharing/
replacement errors, with at most 70 ms total backoff. It keeps atomic replacement
and does not truncate the journal before success. Other errors still fail rather
than switching to delete/overwrite. Controlled backup-reader tests cover .NET and
Unity runtimes; this does not make permanent filesystem failures recoverable.

## Validation and remaining gates

**167 domain checks** pass, including ownership/reach rejection, finite/body/route
limits, canonical socket exits, paid construction, replay rejection, transport
conservation, journal recovery and exact refunds. **280 industrial checks** pass.
**129 native Unity checks** pass, including CPU contacts, terrain/ship obstruction,
authority execution, serialized link privacy, AOI endpoints, delayed endpoint
arrival, resource-allocation checks, native-runtime checkpoint locking and 18
injected discovery retry/recovery/shutdown checks and seven saved-binding checks.

The earlier eight-client Relay session smoke passed in
`TestResults/Multiplayer/Live/0bedecbfe35e/result.json`. Current graphics fixtures
passed movement, visibility, real text exchange, muted positional membership and
reconnect in `TestResults/Multiplayer/Live/5b6c82dd99a0/result.json`. No send-queue
overflow or Vivox API exception appeared in those final logs. Current artifact
hashes and source matching are in `TestResults/Multiplayer/build-validation.json`.
Real authenticated two-client factory tests now pass with headless clients in
`TestResults/Multiplayer/Live/b194d09202b6/factory-result.json` and graphics clients
in `TestResults/Multiplayer/Live/1303a8c7eedb/factory-result.json`. Both use an
offline-prepared diagnostic world with existing authenticated characters and
explicit materials. All live commands use normal transport and authority checks;
local acknowledgement files only coordinate the diagnostic processes.

The owner creates power, water and conveyor links; the observer verifies private
buffer redaction and two rejected foreign edits. Repeating the delivered belt
action does not charge again. Both characters reconnect, then a fresh dedicated
process restores the checkpoint. The owner walks into interaction reach and
recovers each connection. Both clients observe removal; saved inventory, cargo
and resource activity are conserved exactly. Graphics assertions require active,
nonempty batched geometry after creation and empty, inactive geometry after
removal. These assertions do not establish correct pixels or physical UI input.

Reproduce with `Tools/MultiplayerChecks/RunFactoryProbe.ps1`, adding `-Graphics`
for the graphics fixture. Matching diagnostic build marker 10 is required. Interactive
construction controls, moving conveyor presentation, full factory load,
latency/loss, large hubs and 32/100-player gameplay capacity remain open.
Cables/conveyors inside ships remain unfinished. Later source implements surface
modular construction/doors and structural shipyards; see the update below.


Later source adds bounded large snapshots, paid structural shipyard commands,
owner-checked surface buildings/doors, shared CPU walking contacts and building
obstruction checks on routes. See SURVIVAL_NETWORK_IMPLEMENTATION.md for exact
source/artifact limits and newer evidence. The 280 industrial checks remain the
previous unchanged suite. The new `-Buildings` fixture extends the normal
creation/restart/recovery flow; its full live recovery is still being validated.
