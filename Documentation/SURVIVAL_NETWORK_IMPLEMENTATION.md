# Survival networking implementation checkpoint

This is an **incomplete multiplayer foundation**, not completion of the full
survival multiplayer design or a capacity/performance acceptance result.

## November 8 continuation checkpoint

Current source and the latest validated headless build pass 208 native checks;
the domain suite passes 181 checks. All four runtime compilation modes pass.
These counts are correctness checks, not gameplay capacity or frame-time acceptance.

Completed real authenticated Relay checks in this continuation:

- Combined factory/shipyard/building/large-state creation and fresh-process recovery:
  `Live/673883210f1e/factory-result.json` (creation `b6503ddfdd24`, recovery `6050aeed38ab`).
- Ship-local walking, hatch collision, owner-only piloting, moving passengers and reconnect:
  `Live/a60311ae6540/interior-result.json`.
- The same interior scenario with 100 ms one-way delay, 20 ms jitter and 2% loss:
  `Live/beb07ab86ea9/interior-result.json` (clients `0713ef07dbd2`).
- Two independently occupied planets with separate canonical definitions, walking,
  reconnect, saved identities and interest isolation:
  `Live/1556ad44076b/planet-result.json` (clients `a058ff2cc57e`).
- Rendered Relay/Vivox text, muted positional membership, reconnect and Lobby
  recovery: `Live/994cbbce887b/result.json`; listing recovery took 4.63 seconds.

Ship-local machinery now supports validated deck placement, CPU contacts,
canonical reach, inventory/radiation handling, packing refunds and local factory
routes. Native checks also cover deck vehicle driving, driver release and moving
parent coordinates. The planet vehicle recovery path is explicitly excluded from
ship-local simulation. Disconnected characters are absent from environmental damage
and placement occupancy; independent machinery and crafting continue.

The server sends a bounded universe manifest and owns celestial time, ship gravity,
warp sweeps, orbital velocity matching and canonical body handoffs. Handoffs retain
ship-local occupants/objects, reset input epochs and reload only the client's viewed
world without disconnecting. Native region tests and the separate-planet transport
check passed; an end-to-end flown planetary transfer remains unverified.

Server/client builds use Unity's `UNITY_SERVER` conditional compilation and the
Standalone Server build subtarget. The graphics diagnostic uses a separate
`SURVIVAL_GRAPHICS_PROBE` define and cannot replace the full release client.
The normal release client and equivalent standalone army/fog performance comparison
are still required. The delivered graphics/server protocol is currently 7; source
continues to change, so artifact hashes must be refreshed at final delivery.

The real combat fixture reached death, owned revival and item recovery, then was
explicitly stopped to correct a test assertion that expected a reply to a silently
discarded replay. A corrected repeat is pending. The multi-process load harness
now coordinates one common rally and server-tick measurement window across workers;
it still needs standalone validation and the 2/8/32/100 runs.

The 100-player, full gameplay, audible microphone/physical-input, rendered terrain
parity, full client, army/fog and 60 FPS gates remain open. Docking/station policy
has been asked of the user; cross-shard authority transfers remain outside this milestone.

Historical notes below describe earlier checkpoints and failed attempts; this
checkpoint supersedes their current-status statements.

## Implemented source

- Windows client and dedicated-server editor build entry points and root BAT
  scripts. Server builds use an empty scene and `UNITY_SERVER` bootstrap, keeping
  offline cameras, terrain renderers, audio and army allocation out of startup.
  A scoped shader-build callback strips graphics/compute variants only during
  this dedicated entry point; client and army builds retain their variants.
  A separate, explicitly marked survival graphics fixture omits unreachable
  offline army/RTS compute shaders. It cannot be launched as a normal game and
  does not replace the production client or the million-unit path.
- NGO/Unity Transport with Relay, a Lobby server browser and join codes, bounded
  connection approval, heartbeat/population updates, and best-effort cleanup.
- UAS JWT signature verification against Unity JWKS, project/issuer/environment
  and expiry validation, duplicate-character admission prevention, separate
  Authentication profiles for server instances, and no embedded service secrets.
  Display names are validated before world mutation so malformed XML/Unicode
  cannot poison persistent character state.
  New character identity and starter items are one flushed journal transaction,
  avoiding per-registration checkpoint replacements during simultaneous joins.
- A fixed server clock, sequenced/rate-limited input, predicted spherical walking,
  acknowledged input replay and configurable walking snap threshold. The movement
  implementation now shares CPU prism/ramp contacts between server walking and
  prediction, including floors, walls, stairs and doors. Terrain support uses the
  existing CPU sampler; full physical/rendered-contact parity remains a gate. Replay refreshes future prediction samples;
  absent matching samples do not produce false distance snaps. Walking snapshots
  normalize zero-valued inline craft objects materialized by Unity JSON.
- Authoritative truck/loader/excavator driving using the existing wheeled motor,
  including full motor capture/replay, fuel, validated driver leases, safe exits
  and restart recovery. Ground machine placement uses paid equipped items and
  server terrain/footprint checks. I opens inventory; F4 exposes nearby buffers
  and recipes.
- Authoritative home-body capital ships reuse the existing assisted/unassisted
  flight and landing/takeoff integrators through a simulation-only constructor.
  Owners retain pilot authority and manage invitations in F4. Invited players use
  eight fixed passenger berths. Revocation prevents boarding again without ejecting
  existing passengers in flight. Disconnected berths are reusable; reconnect
  chooses an available berth. Free interior walking remains unfinished.
- Per-entity tick budgets prevent seat handoffs from advancing a craft twice in
  one server tick. Control epochs reject pre-handoff walking/driving packets.
- Detailed ship layouts inside 5 km, coarse proxies out to 100 km, speed-based
  look-ahead and occupied-ship subscriptions. Walking/vehicle/ship snap profiles
  use 3/10/50 m defaults. Identical layouts share meshes, with at most one new
  combined ship mesh generated per client frame.
- Server-owned vitals, gather cooldowns, consumable use, and a restricted set of
  existing industry/crafting/inventory commands. Online clients do not instantiate
  the offline authority or write offline saves. Creative/admin and unsupported
  construction/navigation commands are rejected by the network adapter.
- Server spatial buckets, per-client entry/change/removal state, body/frame
  separation, hysteresis and a packet budget. Other players' inventories are not
  replicated; machine contents require ownership and interaction reach.
  Remote player proxies contain presentation and parent IDs instead of nested
  driving/piloting simulation state.
  Per-client delivery state is committed after the reliable snapshot send call;
  budget-deferred removals remain pending and re-entry restores a complete entity.
  Dirty updates age into higher priority so nearby movers cannot permanently
  starve lower-priority proxies. Removals fit both the packet and current token
  budget, with a 256-ID ceiling. Detailed and coarse ship subscriptions have
  separate exit hysteresis. Entity deltas carry schema version and server-tick
  revision; canonical pose changes participate in delta signatures.
  The checked final UTF-8 payload is reused by NGO sending; budgeting does not
  require a second serialization of the completed snapshot.
- Existing industrial snapshots/journal extended with stable character identities,
  world/instance/seed compatibility checks, vitals, server ticks and durable action
  receipts. A mutation's journal entry carries its receipt even if a crash occurs
  before the subsequent checkpoint. Offline storage remains separate.
  Revival resets survival vitals and seat/control state inside its journalled
  transaction, including recovery without a following network checkpoint.
- Ground sleeping bags/pods use the existing paid placement, ownership, rename,
  pack and cooldown transactions. Owners choose a point after death; the server
  resolves terrain and checks occupancy, then resets prediction explicitly.
  Respawn metadata is private; visible ground markers follow surface interest.
  Ship-local respawn points remain unfinished.
- Death transfers carried items and bulk into one recoverable container. Paid
  unfinished crafting jobs return their inputs to that container without creating
  outputs. The same journal record clears the backpack and jobs; replay cannot
  duplicate them. Ship deaths retain a stable ship parent and local position.
  Nearby living players can recover loot through the validated inventory gateway.
- Vivox positional voice by body plus server-wide text. Enter opens/sends, Escape
  cancels/clears, chat suppresses movement/look, V is push-to-talk and U replaces V
  for offline pilot assist. Focus loss mutes capture. Chat errors leave gameplay
  connected. Main-menu Settings exposes two survival key selectors; select both
  keys and use **APPLY KEYS**. The multiplayer Escape menu exposes the same choices
  and saves selections immediately. Voice defaults to V and pilot assist to U;
  available alternatives are T/Y/X/Z and F5–F12. Duplicate/reserved choices are
  rejected. Local preferences survive restarts; invalid saved pairs restore the
  defaults. Flight/voice help follows the chosen keys. Voice stays muted in the
  Escape controls menu. `SurvivalNetworkSession.Bindings.TrySet` and `Save` are
  available for programmatic changes. Physical UI/key-input checks remain open.
  SDK login/logout and channel changes are serialized across reconnects. Text and
  voice readiness are independent; positional joins retry after failure. Capture
  stays muted until an explicit held push-to-talk request succeeds. Cleanup tries
  channel leave and logout even if disabling transmission fails.
- Periodic server reports alongside the save contain measured tick median/p95,
  simulation rate, byte rates, managed/Unity/graphics-driver allocations and
  process memory. These are diagnostics, not standalone acceptance claims.

## Build and service setup

`BuildServer.bat` selects Unity's `StandaloneBuildSubtarget.Server`, which defines
`UNITY_SERVER`. Dedicated startup is compiled only with that symbol. `BuildClient.bat`
selects the Player subtarget. Both builds retain shared authority, simulation and
protocol code because offline single player and client prediction need it; this
is not an assembly-wide removal of every server-side type from clients.

Factory fixture seeding and native checks compile only for `UNITY_SERVER` or
`UNITY_EDITOR`. Automated session/factory probes compile for those targets or
the separate `SURVIVAL_GRAPHICS_PROBE` fixture. Normal client builds contain none
of those diagnostic entry points and do not receive `session-probe.version`.
The diagnostic runner defaults to the graphics fixture rather than a release client.
`Compile.ps1 -Target All` checks all four conditional branches using editor
reference assemblies; actual Unity builds remain a separate gate.

Close the project editor before running `BuildClient.bat` or `BuildServer.bat`.
`UNITY_EDITOR` can override the pinned Hub editor location; `Unity.com` is the
default console launcher so the batch process waits and propagates errors.
BAT builds default to two job workers. Set `UNITY_BUILD_LOW_MEMORY=1` for one worker;
this changes build parallelism only, not runtime quality or army scale.
The server build requires **Windows Dedicated Server Build Support** for Unity
6000.6.4f1. This module has now been installed through the official Hub CLI. The
build guard recognizes Unity's development/nondevelopment variation folders.

The project is already linked to a UGS project. Authentication, Lobby, Relay and
Vivox must be enabled/configured in that project's production environment. A
disposable real Authentication/Relay/Lobby startup-and-shutdown probe now passes.
No dashboard settings or credentials were changed. Real Vivox text exchange and
muted positional-channel membership now pass before and after reconnect. Audible
voice and physical keyboard/cursor behavior remain unverified.

The server build creates `Builds/WindowsServer/server.json` from
`Server/server.example.json` only when no operator config already exists.
Launch with `RunServer.bat [absolute-config-path]`. Empty `savePath` resolves under
Unity's persistent data directory in `Servers/<worldId>/<instanceId>/industry.xml`.
Each instance needs a distinct ID and save file; concurrent writers to one save
are rejected by the journal file lock. This does not implement shared-world shards.
The default requested capacity is 100 clients; the transport config conservatively
allows at most 149 clients plus the allocating server.

### Discovery recovery

Relay is allocated/configured and the server transport starts before join-code
retrieval or Lobby advertisement. Allocation, join-code retrieval and initial advertisement retry
recognized transient errors at most three times, with 2/4-second backoff.
Permanent authorization/argument failures fail startup. An operation already
accepted by the provider may outlive a cancelled request; known late Lobby
responses are removed during shutdown, while unidentified allocations/listings
remain subject to provider expiry. SDK request timeouts still apply.

Heartbeat and population updates are serialized. A missing Lobby is recreated
with the existing Relay code and current population; transient discovery errors
back off from 2 to 30 seconds. Permanent discovery errors suspend retries and log
status without shutting down gameplay. Shutdown waits for an in-flight request,
removes a known listing once, and retries transient deletion failures within the
same bounded startup retry budget. This does not restore a failed Relay transport
or implement authority/session migration.

`RunSessionProbe.ps1 -ListingRecovery` exercises real deletion/re-registration of
a disposable test Lobby while connected clients keep running. Injected native
fixtures separately exercise failure classification, bounded retries, overlapping
requests, latest population preservation, permanent failures and shutdown races.

The current live run passed in
`TestResults/Multiplayer/Live/5b6c82dd99a0/result.json`: a real deleted Lobby was
replaced in **4.7749 seconds**, preserving both connected client IDs, the Relay
code, server tick progression and all checked metadata. Real Vivox text exchange,
muted positional membership and later reconnect also passed. This verifies the
missing-listing path; transient provider outages and permanent errors use the
injected native cases rather than induced production outages. No Relay loss,
transport migration, audible voice, GUI or capacity acceptance is claimed.

This run also injected **15.0806 seconds** before requesting the Relay join code.
The transport had already started and the authoritative clock advanced **452
ticks** during that interval; both graphics clients subsequently connected and
completed normal prediction/reconnect and Vivox checks. The diagnostic delay is
not a simulated HTTP failure, measured startup SLA or Relay-outage failover test.

## Unfinished implementation

- Freely moving players inside ships, vehicle carrier/loading integration,
  docking, orbital velocity/warp controls and travel between bodies. Passengers
  currently use fixed berths; pilot entry is validated at the boarding ramp.
- Integrated ship-local factory links, combat/hit validation and ship-local respawn
  points. Surface construction/doors and structural shipyards are implemented;
  their remaining interactive/load gates are described below.
- Occupied-region celestial scheduling on multiple planets; the current headless
  simulation loads the home body only.
- Complete parity between online canonical contact queries and offline capsule/
  collider contacts. Current craft collision envelopes are conservative.
- Priority tuning under full gameplay load, complete bounded-snapshot stress
  validation, and recovery from loss of an active
  Relay allocation. Discovery recovery does not migrate or reallocate a live session.
- End-to-end cloud and standalone validation, production server deployment and
  2/8/32/100-client dispersed/crowded load scenarios.

The multiplayer menu is a development entry point. Existing offline survival,
RTS and million-unit fixtures remain the complete gameplay paths. Do not publish
this checkpoint as the complete multiplayer release.

## Verification checkpoint

The existing industrial suite passed **280 checks** with loopback networking
enabled. The multiplayer domain suite currently passes **167 checks**, including
invitation/berth authorization, respawn crash/replay recovery and one-step-per-craft
tick budgets, death inventory/crafting conservation and container recovery,
Authentication profile length/stability, malformed-name rejection without world
mutation, revival without its next checkpoint, and nine text-input/push-to-talk
policy checks. These input policy checks do not establish physical keyboard,
cursor, microphone capture or playback behavior.
Twelve additional registration checks cover eight joins while a checkpoint is
held open, replay from a pre-multiplayer checkpoint, exact starter inventory/slot
restoration, reconnect deduplication and journal compaction. The initial live
eight-client smoke run (`5405fe0debdd`) failed: one admission encountered a
checkpoint replacement error, and the transport logged send-queue overflow.
The runner now fails on queue overflow rather than accepting client reports alone.
The repeat eight-headless-client smoke passed in
`TestResults/Multiplayer/Live/688ede7b5b26/result.json`: all eight connected
simultaneously, walked, observed a peer and reconnected, with at least 68 matching
prediction samples each, no hard corrections and maximum matching error about
0.467 m. No send-queue overflow was logged after using bounded 2,048-packet
transport queues. The diagnostic server window reported 0.0275 ms median /
26.8422 ms p95 CPU tick cost, 29.311 ticks/s, approximately 145,837 sent and 9,844
received application bytes/s. Windows measured server peak working set
255,819,776 bytes and observed private-byte peak 367,575,040 bytes; headless client
peak working sets ranged from 242,085,888 to 243,924,992 bytes. The window includes
joins, departures and empty ticks, not steady-state eight-player gameplay. It does
not establish snapshot cost, graphical frame times, full survival, a baseline
comparison or 8/32/100-player gameplay capacity acceptance.
Runtime compilation passes. An isolated Unity Editor run passed **8 simulation
checks** covering takeoff, snapshot replay, gravity, vehicle replay, speed and
braking, and absence of camera/mesh/material/GameObject creation by simulation
constructors. Reports live under `TestResults/Multiplayer`.

The current dedicated executable is available at `Builds/WindowsServer` and
`RunServerChecks.bat` passes **78 standalone checks**: the original simulation,
prediction-history, proxy privacy and walking JSON cases, plus 20 AOI delivery
and serialization cases, three stage-metrics checks, two stable-layout checks,
16 factory/persistence checks, 18 discovery checks and seven saved-binding checks.
The binding checks use injected preference storage rather than real user settings.
The server-only real service gate passed in
`TestResults/Multiplayer/Live/8cbb57b4ec0e`, with a disposable world and normal
shutdown/cleanup. That gate found and fixed an invalid 31-character server Auth
profile and a nested NetworkManager. Native transport now lives on a root object.
Two real authenticated **headless** clients also passed in
`TestResults/Multiplayer/Live/6d259b9248dc`: each moved about 28.01 m, saw its peer,
and reconnected as the same character at the saved position. They recorded 105
and 69 matching prediction samples, zero hard corrections and maximum matching
errors below 4e-11 m in this simple walking scenario. Both used the production
session/prediction path and a disposable world; cleanup was requested normally.
This does not establish rendering, UI/chat, vehicle/ship gameplay, latency/loss
behavior or capacity. A second real run in
`TestResults/Multiplayer/Live/e769d014b130` reopened that saved world using the same
Authentication profiles and verified character identities and positions across
a dedicated-server restart.
The final copied dedicated artifact was verified again sequentially after its
copy completed; its two-client report is
`TestResults/Multiplayer/Live/899e91c8474d/result.json`.

Two graphics-enabled **survival fixtures** passed in
`TestResults/Multiplayer/Live/620005331b4b`, using Direct3D 12 on the RTX 3070.
Both preserved the server's canonical planet definition, moved about 28.01 m,
saw their peer and reconnected correctly. Each recorded 69 matching prediction
samples, maximum error about 0.23343 m and zero hard corrections. These clients
used batch mode, synthetic input, a 960×540 window and the diagnostic build with
offline army kernels omitted. This does not establish interactive UI, Vivox,
normal rendered frame times, full survival or capacity acceptance.

The updated two-client real Vivox gate passed in
`TestResults/Multiplayer/Live/48fa266eb168/result.json`. Both graphics fixtures
exchanged peer messages and acknowledgements, joined positional voice while
muted, and repeated those checks after reconnect. Neither client log contains the
earlier in-flight-send/logout error; sends now share the SDK lifecycle gate.
Both also passed walking, canonical definitions and remote visibility with no
hard corrections. Windows-side sampling measured server peak working set
232,001,536 bytes and observed private-byte peak 357,978,112 bytes; client peak
working sets were 773,763,072 and 772,825,088 bytes. These are short diagnostic
runs, not steady-state memory/capacity or equivalent-baseline comparisons. The
Unity-reported process fields were zero, so use the runner's Windows measurements.
Microphone capture, audible playback, interactive UI and physical key handling
are still separate gates.
After the eight-client registration/queue fixes, the final copied graphics and
server artifacts passed the same real Vivox gate again in
`TestResults/Multiplayer/Live/e6136fc4abac/result.json`. Both clients passed all
text, membership, mute and reconnect checks, with 133/131 matching prediction
samples and zero hard corrections. Neither log contains a Vivox API exception
or transport queue overflow. Staged C# sources match the workspace; delivered
assembly hashes and scoped results are recorded in
`TestResults/Multiplayer/build-validation.json`.

The full client release build remains unfinished: its cold build reached 29,184
variants in the offline `PlanetEncounter` compute shader. Normal `BuildClient`
continues to include all army kernels and variants. No target or army scale has
been reduced to turn these functional fixtures into a performance pass.

No equivalent standalone before/after army or full survival gameplay scenario was measured. A matched stationary eight-client headless fixture is now measured in `SURVIVAL_NETWORK_PERFORMANCE.md`.
Median/p95 frame times, client simulation rate, native/GPU/process memory under
gameplay load and total fog cost therefore remain unmeasured. The 60 FPS at
2560×1440 on RTX 3070 target, million-unit path, 5% army regression ceiling and
3 ms p95 fog target are unchanged. No army shader or per-unit representation was
modified.

## AOI delivery validation

The earlier AOI delivery checkpoint passed 117 domain checks, 280 industrial checks and 32
native checks. Its packet fixture calls the production publisher and decodes the
exact UTF-8 buffer supplied to its transport callback; it is not an on-wire Relay
capture. Cases cover surface/body filtering, private machine inventories, public
player/ship data, coarse proxies, pose-only deltas, surface/detail/proxy hysteresis,
budget-deferred removal and complete re-entry, including large-ID removals with
4 KB available tokens. Eleven domain cases cover delivery commit and age-based
scheduling fairness. The first native fixture run failed because a fixed 2 KB
test budget did not reserve Unity's inline-class envelope; it now measures that
envelope before allocating delta bytes.

The final AOI server passed the eight-client Relay smoke in
`TestResults/Multiplayer/Live/1b67fb5e48ac/result.json`, with at least 65 matching
prediction samples each, no hard corrections and maximum error below 2e-10 m.
No send-queue overflow was logged. Its short, mixed startup/gameplay/departure
window measured 0.0271 ms median / 38.6906 ms p95 CPU tick cost, 29.132 ticks/s,
approximately 141,535 sent and 9,784 received application bytes/s. Server peak
working set was 255,655,936 bytes and observed private-byte peak 378,765,312 bytes.
The p95 exceeds a 30 Hz tick budget of 33.3 ms; steady-state performance and
capacity acceptance remain unmet. Removing duplicate final-snapshot serialization
reduces repeated work, but these short runs do not prove a timing improvement or
an equivalent-baseline regression result.

The matching final graphics fixture and server passed the two-client real Vivox
and reconnect gate in `TestResults/Multiplayer/Live/759b7afe9fb0/result.json`.
Both clients passed text delivery, muted positional-channel membership, walking,
remote visibility and reconnect, with zero hard corrections. Neither client log
contains a Vivox API exception, and neither final live run logged queue overflow.
Delivered assembly hashes and matching staged-source checks are recorded in
`TestResults/Multiplayer/build-validation.json`. Audible voice, interactive UI,
the full release client and the broader gameplay/performance gates remain open.

## Matched occupied profiling update

The current checkpoint passes **125 domain, 280 industrial and 37 native checks**.
A matching saved-world comparison with eight clients present for an entire window
reduced tick p95 from **41.1646 to 18.6678 ms**, and mean snapshot publication cost
from **21.1135 to 9.9261 ms**. Tick median changed from 0.0547 to 6.8584 ms because
publication and industrial work now occupy different ticks. Both measured windows
ran about 30.06 ticks/s; no simulation/snapshot rate or journal durability was
reduced. This resolves the fixture's p95 budget, not full gameplay capacity.

The publisher skips encoding unchanged structured fingerprints, keeps layout
geometry in stable ship-local frames, and offsets snapshots from industrial
steps. Stage timing reports retain eight occupied/history windows and mark
unsupported process-memory measurements explicitly. Authentication keys are
fetched with bounded transient retry before advertisement, preserving signature
and claim validation. Runs with failed admissions were excluded.

See [the profiling report](SURVIVAL_NETWORK_PERFORMANCE.md) for matching checkpoint
and assembly hashes, median/p95, stage cost, bandwidth, memory and exact limits.
The final graphics fixture passed real Relay/Vivox text, muted positional-channel
membership and reconnect in `TestResults/Multiplayer/Live/cb6ad82d4ef7/result.json`.

## Ground factory connection update

Ground power, water and conveyor commands now use authenticated ownership/reach,
CPU terrain/contact validation and the existing industrial journal. F4 exposes
output selection, optional routing points, completion and recovery. Public link
geometry follows AOI extents and includes necessary endpoint presentation; private
buffers and construction receipts remain reach/ownership filtered. The client
batches link geometry without per-item conveyor objects.

The current checks pass **167 domain, 280 industrial and 129 native cases**. The
new native cases cover the actual headless gateway/publisher, stored CPU prisms,
terrain/ship obstruction, endpoint inclusion, privacy and delayed endpoint arrival.
A transient Windows backup-reader test passes in both runtimes. Atomic replacement
has bounded sharing-error retry without deleting a checkpoint or weakening journal
flush/compaction ordering.
The latest native run also includes 18 injected discovery retry/recovery and
shutdown-race checks and seven saved-binding checks. The domain total includes 16 bounded snapshot-chunk cases; the industrial suite
is unchanged.

Protocol **4** and content **survival-2** govern network compatibility. Diagnostic
build marker **10** selects matching test executables; it is not a wire protocol.
Saved-world content remains version 1. The earlier eight-client protocol/session
smoke passed in `TestResults/Multiplayer/Live/0bedecbfe35e/result.json`.
The matching graphics/Vivox regression passed in
`TestResults/Multiplayer/Live/5b6c82dd99a0/result.json`; current factory and chat
fixtures use copied artifacts matching workspace C# sources. The final live runs logged no send-queue
overflow or Vivox API exception.
Real two-client factory transport tests now pass for headless clients
(`Live/b194d09202b6/factory-result.json`) and graphics clients
(`Live/1303a8c7eedb/factory-result.json`), under `TestResults/Multiplayer`.
They cover three connection types, foreign-edit rejection, private buffers,
duplicate action delivery, reconnect, a fresh server restart, walking into reach,
recovery/refunds and saved cargo/activity conservation. The graphics test also
checks batched mesh activation and clearing. Materials and initial machinery are
prepared offline; local files coordinate the clients without bypassing admission
or command validation. Physical GUI input and correct rendered pixels remain
unverified.

See [the factory checkpoint](SURVIVAL_FACTORY_NETWORKING.md) for controls, limits
and remaining GUI/load gates. The prior matched timing comparison
is a historical stationary fixture and does not establish dense-factory performance.

## Multiplayer controls in this checkpoint

- F boards your owned landed ship at its ramp and takes the pilot seat. F while
  piloting releases control but keeps you aboard; E disembarks when landed.
- Invited players use F at the ramp to board, and F/E to disembark when landed.
  They cannot pilot or manage invitations.
- Ship: WASD thrust, R/C vertical thrust, Q/E roll, Shift boost, Space landing/
  takeoff, U flight assist, G gear, B brake.
- F enters/exits an owned nearby industrial vehicle; WASD drives, Space brakes.
- I inventory, F4 industry/invitations, G gathering on foot, Enter text chat,
  hold V proximity voice. Menus and chat do not pause the authoritative world.


## Bounded snapshots, shipyard and modular buildings

Protocol 4 adds bounded, ordered, SHA-256 checked snapshot chunks, with at most
one 1 MiB logical transfer buffered per client. Core movement acknowledgements
continue while private inventory or an oversized public entry streams. Sender
cursors and AOI delivery revisions advance only after send commit. Transfer
cancellation handles interest loss, control epochs and private reach changes;
stale completed/replaced transfers cannot overwrite newer state. Native checks
include a real 4 KiB/s refill schedule to prevent core-only packet starvation.

Paid structural ship placement, recovery and power wiring use owner/landed/reach
checks and the existing industrial resource journal. Canonical routing points
name the actual ship parent and are normalized inside the validated adapter.
Recovery rejects occupied sections and connected cables. F4 exposes the controls.
Real combined large-state/factory/shipyard tests passed headlessly in
`TestResults/Multiplayer/Live/e3e56462cc54/factory-result.json` and graphically in
`TestResults/Multiplayer/Live/811f82b02f7c/factory-result.json`. These runs include
replay rejection, reconnect, a fresh dedicated process and exact material/activity
recovery. They used the preceding marker-9 binaries, not the newer building/load
source. This is not interactive shipyard or rendered-pixel acceptance.

Surface modular buildings now have owner identities, validated structural-socket
commands, paid placement/recovery and obstruction-checked doors. The server and
offline adapter share one construction projection into the existing body save,
including every owner's machine mounts, resource sources, haul obstacles and
integrated logistics ports. CPU room profiles omit all render arrays. Nearby
public parts contain structural data only; AOI exit releases client contacts,
and re-entry restores the full current part state. Client rendering batches parts
in 12 m chunks with at most one mesh rebuild per frame. F4 exposes module/socket
selection and machine mounting. Shape/terrain/ownership checks also protect
factory routes, respawns and shipyard placement against buildings.

The 129 native checks include CPU/offline-profile agreement, owner/replay/reach
rejection, door obstruction, exact paid recovery after restart, multi-owner
projection, canonical public AOI entry/exit/re-entry, swept walls and continuous
stair ramps. Real marker-10 headless building creation, foreign-edit rejection and
reconnect passed in `Live/2ee81bf6fc1b`; its combined restart run was stopped after
a probe-flow error left the owner outside private factory inventory reach. The
probe now walks back before inspecting it. Complete live building recovery,
physical construction UI and load/performance acceptance remain pending.

A new opt-in headless load harness uses independent Unity Services registries and
NGO managers in one process. It exercises the production authenticated session,
walking prediction, spatial snapshots and two consumable transactions per actor.
It avoids 100 whole engine processes. `RunLoadProbe.ps1` records measured occupied
server windows and Windows process memory. It must pass first with two clients
before 8/32/100; compilation alone is not a load result. Scripted walking and item
use do not establish full survival, graphical client, voice, crowded factory,
ship, army or fog capacity. Ordinary client and graphics builds exclude this
load entry point; dedicated fixtures carry a separate `load-probe.version` marker.


## Protocol-5 leave and contact update

The current dedicated source uses protocol **5**, content **survival-2** and
fixture marker **11**. The separately recorded marker-9/10 graphics results above
remain historical and cannot be joined to this protocol-5 server. The ordinary
client release build is still unfinished. World persistence remains version 1.

**137 native checks** pass, now including shared vehicle floor probes/chassis
sweeps, repeatable load startup, matching-tick corrections, reliable-leave
control release and the supported 10 Hz walking setting. Default simulation is
still 30 Hz. Server walking and client prediction use the same prism/ramp contacts;
vehicle probes/sweeps use those canonical contacts too. Ship/building sweeps use
conservative envelopes. Full rendered collision parity remains unmeasured.

Protocol 5 adds a reliable application leave/acknowledgement and bounded wait for
the server's transport close. The old connection immediately loses command and
seat authority; persistent identity, attachment and inventory remain. Waiting for
the close releases the Relay slot before reconnect, including a completely full
two-client allocation. A failed graceful attempt still proceeds with teardown.
The new real UTP latency/jitter/loss regression passed in
`TestResults/Multiplayer/Live/bd608976f4b9/result.json`. Earlier failures are retained
for the identified close/slot-retirement bug; no identity bypass or extra provider
slots were used. Distance corrections require matching ticks, input sequences,
epochs and canonical body/frame identities; held inputs cannot create false
same-sequence snaps at a different simulation time.

The corrected marker-10 surface-building/factory tests passed both phases with
headless clients (`Live/39ac6e1b2158/factory-result.json`) and graphics clients
(`Live/9915df80ca32/factory-result.json`). These cover real paid construction,
foreign-edit rejection, open-door persistence, duplicate delivery, reconnect,
restart, structural-order recovery, exact material/activity conservation and
batched graphical creation/removal. Physical controls and rendered pixels were
not asserted. The protocol-5 graphics regression is still pending.

New 2/8/32/100-client crowded scripted-load measurements are running sequentially
with no concurrent builds. The first two-client 90-second run passed in
`Live/ead28ca36dcb/load-result.json`; see the performance report for measurements
and limits. Actual rendered gameplay, full survival and army/fog gates remain
open, independently of scripted load status.


## Universe and personal combat authority stage

Source and standalone checks now include saved CPU universe definitions, stable
body keys and explicit per-body instance ownership, a persisted celestial clock,
and solar/wind environments for industrial work on occupied machine regions.
They use the existing world checkpoint and journal. They do not yet implement
client travel transitions, free ship walking, docking or cross-shard handoffs.
Personal weapon commands derive their equipped definition, aim and target on
the server; ammunition, damage, cooldown and receipt share one journal entry.
CPU ray queries traverse indexed cells and test authored solids, ramps, terrain
and player capsules. Native checks cover misses, walls, cooldown/replay and crash
recovery. Real transport combat and complete gameplay load remain unverified.

The latest native player passes 166 checks, including a causality check that a
new core acknowledgement does not imply fresh streamed private inventory.
Action replies now carry server ticks. Combined large-state factory/shipyard/
building creation, replay and reconnect passed in Live/169d82565409. The combined
fresh-process recovery was explicitly stopped when its scripted straight return
route hit the constructed foundation; the saved ledger was correct. A normal
walking waypoint around the structure is compiled but awaits a fresh player
build and repeat. Retain the failed Live/e58935911d76 report as failure evidence.

A subsequent graphics rebuild stalled under memory pressure and was explicitly
stopped with only its verified isolated build/helper processes terminated. The
original project editor was preserved. Delivered graphics and dedicated binaries
are not yet a matching final set; the full client release build remains unfinished.
