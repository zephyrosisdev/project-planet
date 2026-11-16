# Survival networking implementation checkpoint

## Current delivery — protocol 10 ramp reboarding correction

Package: `Builds/Playtest-20261114-152650`. This supersedes protocol 9; both clients
and the server must update. Existing worlds and character identity mappings remain
compatible. The user deferred performance investigation after reporting Unity
Editor was also open. No measured cause or FPS improvement is claimed here.

The fall-through defect was in the network movement adapter: exterior ground
walking ignored the ship's CPU ramp contacts until attachment, while attachment
used a fixed -8 m entry/proximity gate and a narrow foot strip. After landing on
uneven terrain that gate could miss the actual rendered ramp. Rotation-only
changes also failed to invalidate its cached collision model.

Ground prediction and authority now resolve the same nearby ramp contacts before
attachment. Boarding uses actual support geometry across the supported width,
preserves heading/pitch, and the prompt uses the terrain-adjusted endpoint. The
cache includes landing attitude. Ownership/invitation and occupied-space validation
remain server controlled. No correction-distance or movement-speed limit was raised.

Validation: all four conditional runtime compilation modes PASS, 260 domain checks
PASS and 238 standalone native checks PASS (`Logs/survival-ramp-final-native.log`).
The native regression walks an off-centre ground character up an elevated, tilted
ramp, compares prediction with authority, walks off and reboards. It also exercises
rotation-only cache invalidation. Both normal client and dedicated server builds
passed in `Logs/survival-ramp-final-build.log`.

The rendered two-client Relay run passed in
`Live/69f90b78b8a4/interior-result.json` (actual clients `9fbf42034b1a`), with 100 ms
one-way delay, 20 ms jitter and 2% packet loss. It covers initial boarding, hatch
collision, ground disembark/reboard, pilot authority, actual takeoff and landing,
then a second disembark/reboard and stopped-character reconnect. Owner reports
`interiorLandedReboarded` and `interiorReconnect` true. The earlier headless delayed
round trip passed in `Live/e82cbee9d7bb/interior-result.json`. The earlier rendered
`Live/348dd3aad3f7` failed its reconnect comparison after the landing/reboarding
steps passed: its harness sampled position while walking inputs were still in
flight. The passing repeat waits for server-confirmed stopped movement and retains
the same 0.25 m reconnect bound. Failure evidence is retained.

The earlier sections below describe historical packages and scopes. Physical user
playtesting remains necessary; performance and large-capacity acceptance remain
unmeasured/deferred as previously recorded.


## Historical delivery — November 14: multiplayer presentation parity

The protocol-9 package is `Builds/Playtest-20261114-144453`. It supersedes the
November 13 package. Both clients and the dedicated server must update together.
The user tested the preceding online build and reported missing single-player
presentation; this delivery addresses those gaps.

- Clients reconstruct the same saved solar/galactic system (192 planets in current
  fixtures), stars, distant terrain and sky through the shared `PlanetSystemRuntime`.
  Only the viewed planet has full client terrain. The network adapter borrows the
  canonical manifest, does not run offline authority, and does not advance an
  independent clock or initiate offline region transfers.
- New worlds use the authored single-player 10x system scale. Existing saves retain
  their saved scale, positions and identities; no destructive resize is performed.
  `systemSizeMultiplier` is validated server configuration for new worlds, and the
  effective saved value is published to joining clients.
- Detailed unpiloted ship simulation sleeps beyond connected players' 100 km proxy
  interest range. Occupied planets run independently; persistent industry and cheap
  analytic celestial/station orbits continue outside visibility.
- Detailed ships use the existing single-player `PlanetSpacecraft` presentation:
  modular hull, cockpit glazing, fittings, lights, animated doors and a
  terrain-adjusted access ramp. Replicated/predicted snapshots drive this view;
  it never reads movement input or integrates authoritative flight.
- Authorized players walk onto the ramp and through validated rear/inner hatches.
  Boarding preserves canonical feet instead of teleporting into the airlock.
  Ramp support uses the shared CPU contact model, disembarking is physical, takeoff
  rejects an occupied ramp, and reconnect restores safe feet on inclined support.
- Multiplayer status, inventory, crafting/construction, machinery, shipyard,
  respawn, settings and chat use retained UI Toolkit elements. The offline system
  debug IMGUI overlay is suppressed online. Element reuse handles changing control
  types, and panels use logical UI dimensions so resolution scaling remains coherent.
- Pilots receive the shared orbit map, gravity forecast, prograde/landing instruments
  and celestial navigation. O/P control navigation presentation; L is a server-validated
  attitude-hold input. Chat/focus/menu capture still suppresses gameplay and camera input.

Validation: 233 native standalone checks and 260 production domain checks PASS;
four runtime compilation modes PASS. Native tests include slope support/reconnect,
continuous boarding, ramp takeoff safety, distant-region sleeping and attitude hold.
Both normal client and dedicated-server Unity builds succeeded. The final client
cleanup build is in `Logs/survival-parity-client-cleanup-build.log`; authority/native
build evidence is in `Logs/survival-parity-complete-build.log` and
`Logs/survival-parity-complete-native.log`.

Real Relay/Vivox text and reconnect passed in `Live/9e3b2e2c83c7/result.json`.
An earlier `Live/73073371d977` reconnect failure on the inclined ramp is retained;
the standing-clearance fix and regression above supersede it. Rendered physical
boarding, hatch collision, owner/passenger authority, pilot HUD, flight and walking-occupant
character persistence passed in `Live/ce2c63043db2/interior-result.json` (actual clients
`e48c1224ea46`). This interior test reconnects a walking occupant in a moving ship;
station mooring itself is covered by the separate historical docking fixture.
The delayed headless interior test passed at 100 ms one-way delay, 20 ms jitter and
2% loss in `Live/e2f4dbd6457f/interior-result.json`. Two rendered clients walking and
reconnecting on different planets passed in `Live/d2db4bf97efd/planet-result.json`
(actual clients `456a8d22945b`), including no remote-player leakage across bodies
and persistence of independent region identities. Captured world/pilot PNGs are
beside client reports; these are automated render captures, not physical UI tests.

The short Relay/chat run recorded 29.96 server ticks/s, tick median/p95 1.241/5.866 ms,
and aggregate outbound/inbound 46.3/5.9 kB/s over 900 ticks (mean 0.927 connected players,
maximum 2). It includes startup, reconnect and diagnostic rendering. It is not an
equivalent standalone baseline comparison or capacity result. Client median/p95
rendered frame times and 60 FPS at 1440p on RTX 3070 remain unmeasured. Manual controls,
audible voice and broader gameplay remain user/friend playtest gates; 100-player
capacity remains deferred. No army/fog pass is claimed; RTS remains retired.

The sections below are historical implementation logs and earlier delivery states.


## Historical delivery — November 13, 2026

The matching protocol-8 Windows client and dedicated server are packaged in
`Builds/Playtest-20261113-231404`. This is ready for the agreed two-player playtest;
it is not full production, capacity or performance acceptance. See
`MULTIPLAYER_PLAYTEST.md` for hosting, joining, controls and remaining manual checks.
RTS retirement and deferral of the 100-player matrix were explicitly authorized
by the user. Older entries below are historical and do not describe current builds.

- Normal client and headless dedicated server builds: PASS. Client assembly
  `8EBA8DF5C9C2E2F7A48F286386052BB6ABA67BEFABC9035AE3710C987E31303D`;
  server assembly `0DC196905C0C42324EFA6B180096CE7ED9ACE505B7BA64FF61FFE44B80F1FD1F`.
  The normal client excludes diagnostic session hooks. Dedicated build uses Unity's
  Server subtarget and `UNITY_SERVER` conditional compilation; client presentation
  remains excluded from headless paths.
- Packaged dedicated executable: 226 native simulation checks PASS
  (`Logs/packaged-server-simulation-checks.log`). Domain checks: 260 PASS;
  industrial checks: 280 PASS. Four conditional runtime compilation modes PASS.
- Real two-client Relay/Vivox graphics fixture: PASS in
  `TestResults/Multiplayer/Live/a384f69fa4fd/result.json`, including exchanged text,
  positional-channel membership/muting and reconnect. This is automated SDK and
  gameplay behavior; microphone capture/playback and physical input remain manual.
- Real orbital docking repeat: PASS in `Live/6cc3487a764b/dock-result.json`, using
  100 ms one-way delay, 20 ms jitter and 2% loss. Owner undock, predicted approach,
  berth reacquisition, passenger authority denial, canonical save and moored
  reconnect passed. The owner recorded 61 matching prediction samples, zero hard
  corrections and maximum error about 0.00000443 m. This narrow fixture does not
  establish high-speed or full gameplay acceptance.
- Fixed loss of orbital velocity during delayed pilot-epoch acknowledgement and
  blocked chat/menu input: ships release thrust without synthesizing a brake;
  ground vehicles retain their safe parking behavior. Added domain/native checks.
- Current save content 2 upgrades content 1 atomically and rejects unsupported
  future versions. Stations remain in the existing authority journal/checkpoint.

The short graphics functional run measured server ticks at 29.89/s, tick median
0.421 ms / p95 4.402 ms, snapshot median 0.115 ms / p95 1.186 ms, and aggregate
outbound/inbound 36.3/5.7 kB/s. It includes startup/reconnect, averages 0.93 players
(maximum 2) across 900 ticks, and is not a steady-state or baseline comparison.
Observed peak private bytes were approximately 437 MB server and 1.39 GB per client;
working-set peaks were approximately 305 MB server and 789–791 MB per client.
Industry stage p95 was 22.211 ms, so these short results must not be extrapolated
to crowded-world capacity. Rendered client median/p95 frame times, survival 60 FPS
at 1440p on RTX 3070, audible voice and full manual gameplay remain unverified.
100-player validation is deferred; sharding/provisioning remain separate work.

## Historical implementation log

This is an **incomplete multiplayer foundation**, not completion of the full
survival multiplayer design or a capacity/performance acceptance result.

## Product scope update — November 13

The user explicitly authorized completely retiring RTS. The current product is
single-player Survival and authoritative survival multiplayer. The main menu's
army stress controls and the standalone million-unit harness are removed; legacy
RTS entry points reject activation, and old planetary scene modes normalize to
Survival. Player builds omit the retired army/strategy compute kernels. GPU terrain,
surface sampling, industrial construction and survival rendering remain.

The earlier army/million-unit/fog acceptance gates are retired, not reported as
passes. `AGENTS.md` now reflects the user's decision. The survival 60 FPS target
still requires standalone measurement. The delivery focus is a two-player build;
the 100-player capacity matrix is deferred and user/friend playtesting covers
physical controls, voice and broader gameplay. The initial army-inclusive build
was explicitly stopped at the user's scope change; its completed graphics cache
is retained for the survival build. No separate reduced-scale benchmark is used.

## November 13 resumed verification

The freshly resolved isolated dedicated build passed 210 native checks, including
acceleration-limited walking at 30 Hz and 10 Hz. The industrial suite passes 280
checks. The current domain suite passes 258 checks, including station deployment
payment, identity, replay rejection and journal recovery. All four runtime
conditional-compilation modes pass with the station source overlay.

The two-worker eight-client Relay smoke passed in `Live/976290549b1f/load-result.json`
with all eight independent identities and conserved consumables. This 30-second run
verifies worker coordination, not gameplay capacity; it used the preceding protocol-7
server assembly `B88C92A7B960E52CED6ABABDAF2C94CE80F8DCE6D18A832AFF872D00138A57BC`.

The user selected **player-built stations only**. Current source adds paid docking
station kits, canonical analytic orbits, stable owner identities and invitations,
one reserved berth per station, CPU gantry sweeps, bounded approach validation,
authoritative mooring/undocking and explicit prediction resets. Private docking
invitations are omitted from public interest state. Stations use the existing world
checkpoint and journal. They are external docking gantries; walking remains inside
the ship, and no neutral starter stations are generated. Source protocol is now 8;
221 native checks and the protocol-8 two-client session passed. The real docking
scenario passed in `Live/0dea4b2e3469/dock-result.json` (clients `5e979a6addff`),
including moored reconnect and the canonical ship-local checkpoint. An earlier
runner incorrectly compared orbital displacement with ground walking; its failure
remains recorded in `Live/d0914c9adfcd`. The latency repeat was explicitly stopped
in `Live/72e2c4922752` after exposing a stale station contact-time issue. Matching
replay-time and moving-frame regression checks pass in the fresh 223-check native
player. Its delayed transport repeat (`Live/08418c1a1d71`) was explicitly stopped
after the probe sent an action before the new pilot epoch arrived. The harness now
waits for the authoritative pilot state; the normal UI already gates that command
on the pilot state. This harness change awaits the next dedicated fixture
build, along with a server-only content-2 save marker. Existing content-1 saves
are migrated atomically, and older content-1-only servers reject the new saves
instead of silently dropping station records. The full normal client is currently building from the timing-fixed source.

The server configuration example exposes implemented movement profiles. Initial
compilation against the open Editor failed because its restored package references
lacked the declared networking assemblies. Fresh isolated resolution and compilation
succeeded. The open user Editor has not been stopped or its Library modified.
The pre-multiplayer army baseline source (`fde1a71`) is extracted separately with an
identical editor-only build entry point and unchanged runtime/benchmark code.

The user now prioritizes a matching client/server pair for two friends and defers
the 100-player capacity matrix; the intended future target is unchanged. Voice,
physical controls and general playtesting will be performed by the user and friend.
Live travel, rendered client, current capacity and equivalent army/fog/60 FPS gates
remain open. No current-source capacity or performance acceptance is claimed.

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


## 2026-11-14 protocol 11: inventory and render-rate prediction presentation

Current delivery restores shared UI Toolkit slot inventory, ten-slot action bar,
vitals, walking compass and nearby/owned storage. All slot moves, drops,
selection and consumable use remain validated server commands. Ship-local
drops retain canonical frames and use the existing inventory journal.

Prediction, server acknowledgements and pending-input replay already existed.
Local visual sampling now interpolates adjacent predicted fixed ticks each
render frame, applies unconsumed walking mouse look, and keeps occupied ship
parents on the same sampled state. Small position corrections decay visually;
frame/epoch changes and hard corrections reset presentation. The authoritative
clock and input advancement do not change. UI data refreshes are bounded and
unchanged ship physics JSON is cached.

Validation: 246 native checks passed, including render interpolation without
advancing simulation, immediate pending look, frame reset, inventory split/stale
item validation, ship-local drop and recovery conservation. Two graphical Relay
clients passed slot callback split/equip/drop/recover, conservation and reconnect
under 100 ms one-way delay, 20 ms jitter and 2% loss (Live/75bb1b9bfb21). This
uses application-level UI events; physical keyboard/mouse operation needs the
friend playtest. The earlier recovery-test failure remains in Live/67dd9ad271eb.

Matched diagnostic profile: Live/0038b001021a baseline and Live/36722553ef4c
current, identical checkpoint hashes, one graphics client, one headless peer and
co-hosted dedicated server. Baseline selects the prior presentation/UI/cache
paths in the current executable; it is not an archived protocol-10 build. Each
run warms up 10 seconds and samples 30 seconds with an explicit 2560x1440 HDRP
render and GPU completion fence. Median/p95 frame times: 10.552/11.738 ms before
and 10.726/11.958 ms after. Client tick median/p95: 0.096/0.246 ms before and
0.061/0.200 ms after. Reconciliation median/p95: 0.970/1.038 ms before and
0.916/0.993 ms after. Relay RTT was 48/64 ms respectively. This measures an idle
scene and does not establish release FPS, flight performance or capacity.

Server rate: 29.936/29.960 ticks/s; tick median/p95 0.833/4.552 ms before,
0.869/3.011 ms after. Observed client peak working sets: 966.3/969.5 MiB;
observed private-byte peaks: 2634.2/2660.7 MiB. Server peak working sets:
301.6/300.2 MiB. These are short-run process observations, not steady-state
allocation budgets. Full raw measurements and scope are recorded in
TestResults/Multiplayer/client-feel-comparison.json. The first delayed profile
failed when its headless peer timed out (Live/df1e3d99f932); matched performance
runs used ordinary Relay conditions. Functional delayed inventory testing passed
separately. No 60 FPS or 100-player acceptance claim is made.

Follow-up protocol-11 graphical ship regression passed in Live/58f3932e748e
(transport Live/ed97a454ff09): owner/passenger authority, hatch collision, ship-local
walking, moving parent, walking off/reboarding, takeoff/landing/ramp reboarding,
pilot HUD and reconnect at 100 ms one-way delay, 20 ms jitter and 2% loss.
All four conditional compilation modes pass. Client/server archives in
Builds/Playtest-20261114-164123 pass SHA-256, ZIP CRC and packaged assembly checks;
the normal client excludes the opt-in session probe.


## 2026-11-14 protocol 12: N-key orbit hold and duplicate navigation

Multiplayer now captures N as a pending edge between render and simulation
ticks, consumes it once, clears it on blocked input/focus loss/epoch reset, and
uses the shared ship orbit-hold toggle. The server input queue never repeats
the edge when packets are missing. Hold radius, reference node/plane and HUD
status are part of the existing ship physics snapshot, restoring the same
controller during prediction replay and persistence. Legacy saves default to
hold disabled. Both client and server must update to protocol 12.

Walking compass visibility now excludes piloting in the shared compass itself.
The online inventory no longer overrides that component's visibility immediately
before Refresh could re-enable it. Pilot navigation retains the shared flight
compass. No renderer or terrain allocation strategy changes.

Checks cover one serialized N edge enabling hold, key release, snapshot replay,
HUD status restoration, second-press cancellation, braking, landed rejection and
missing-packet edge suppression. The Editor batch suite reaches these checks
but later hits its existing edit-mode DontDestroyOnLoad restriction in transport
leave tests; the dedicated standalone suite is the required validation here.

Final protocol-12 validation: 254 standalone simulation checks, 260 domain
checks and all four conditional compilation modes pass. The two-client Relay
ship regression passed in Live/d2743c3f859d (transport Live/1fc28e341d46) under
100 ms one-way delay, 20 ms jitter and 2% loss, including landed ramp reboarding
and reconnect. The captured pilot view was inspected: one top-centre navigation
compass and N-toggle help. Physical keyboard interaction still needs playtesting.
Docking explicitly clears hold so undocking cannot resume stale thruster control.

Matching client/server ZIPs in Builds/Playtest-20261114-171802 pass SHA-256, ZIP
CRC, protocol marker and assembly identity checks. The normal client excludes
session diagnostics. Raw delivery evidence is protocol-12-validation.json in
TestResults/Multiplayer. No new release FPS or capacity claim is made.


## 2026-11-14 orbital weather stability (protocol 12 compatible)

Air quadrature now stays fixed as faint clouds cross zero opacity. Pixel-derived
rays and footprint-filtered cloud noise with continuous mip phase address other
rendering discontinuities without increasing ray steps or rendering buffers.
See WEATHER_STABILITY.md for GPU negative controls, both Direct3D backends,
133 bounds/flight checks, copied saved-view scope, standalone comparison and
failed diagnostic attempts. The new client is packaged in
Builds/Playtest-20261114-191031 and is compatible with the existing server/saves.

### 2026-11-15: opted-in multiplayer test clock (protocol 13)

Dedicated test servers can enable shared numpad +/- controls at 1x/2x/4x/8x.
Normal startup remains 1x with controls disabled. The clock uses additional fixed
physical steps with matching client prediction and unchanged packet cadence.
64x was measured and rejected for severe stalls. See
[MULTIPLAYER_TEST_CLOCK.md](MULTIPLAYER_TEST_CLOCK.md) for launcher instructions,
native/live checks, frame/tick/memory/bandwidth measurements and limitations.

## Ship recall (2026-11-15)

An authenticated owner on dry ground can request `ship-recall` for their saved
ship ID from Escape or F4, including after a Home-region respawn and while the
ship is outside AOI. The server rejects non-owner, dead/airborne callers,
occupied ships (including disconnected occupants), docked ships, different-body
ships, duplicate jobs and unpowered propulsion. A bounded CPU landing search
chooses a supported dry site about 140–800 m from the call location. Failed
searches are throttled too. The caller does not supply coordinates.

A headless flight job climbs radially, travels around the planet above its relief
bound and descends to the existing checked landing approach. It uses the fixed
physical clock, bounded speed/acceleration and swept obstacles, celestial bodies,
terrain footprint and player clearance. It stops on obstruction or loss of power;
`ship-recall-cancel` is owner-only. It does not teleport, follow a moving caller,
cross planets, undock automatically or take occupied ships away. Recall is an
explicit active-simulation reason outside normal observer interest; it does not
subscribe distant clients to detailed ship state. Default cruise speed is
`shipRecallSpeed=1000` m/s, validated between 10 and 10000 m/s. Landing assistance
uses its existing approach speed and contact rules.

Route stages and destination are part of the existing ship physics checkpoint
and journal, including cancellation and arrival. Content is `survival-3` on
protocol 13. Server save version 3 accepts earlier versions 1/2, and older servers
reject version 3 to avoid interpreting a recall as an ordinary landing assist.
Back up a world before starting the new server; do not reset or rewrite player
ownership to solve a Home-region respawn. Improved refusal text identifies the
other ship/owner and says when the survivor's own ship is still airborne.

Validation: 282 production domain checks, 280 standalone native simulation checks
and five conditional compilation configurations pass. Native fixtures cover
recall authorization/rejection, persistence and simulation reconstruction,
outside-AOI progress, bounded per-tick travel, supported landing, cancellation,
blocked routes, player clearance and no render-resource allocation. A fixture's
atmosphere assertion was corrected to use zero ephemeris time so its geometry
comparison excludes the unrelated moving-body inflation margin. Final native
result: `TestResults/Multiplayer/ship-recall-native-final.log`.

Interactive Relay recall, changing terrain/obstacles during the full descent,
long-haul/crowded recall performance and the 60 FPS standalone acceptance remain
unmeasured. The functional before/after performance comparison uses ordinary
survival play; it does not establish a cost bound for simultaneous recall jobs.

The ordinary-survival standalone comparison is recorded in
`TestResults/Multiplayer/ship-recall-validation.json` (before `Live/0f6cbe8fba18`,
recall build `Live/b0b020fa4f52`). Both fresh worlds use matching seed/config,
5 s warmup and 15 s samples with an explicit 2560x1440 fenced HDRP render, a
headless peer and co-hosted server. It is not a release FPS test or an active
recall workload. Frame median/p95: 10.829/15.374 ms before, 10.656/15.654 ms after.
Server simulation: 29.961/29.943 Hz; tick median/p95: 1.174/4.376 versus
1.230/4.951 ms; snapshot median/p95: 0.268/1.950 versus 0.428/2.922 ms.
Server send/receive rates: 49350/6736 versus 55553/7425 bytes/s. The server metric
windows include differing admission populations (mean 0.964 versus 1.029), so
these bandwidth/timing figures are not a controlled steady-population comparison.
Primary client peak working set/private bytes observed: 1009.4/2698.3 MiB before,
1010.1/2726.9 MiB after; server 315.4/441.4 versus 316.5/438.2 MiB.
Both functional session checks passed. No 60 FPS or simultaneous-recall capacity
pass is claimed.

## Recalled ship suspended above its landed collision (2026-11-15)

The reported long/steep ramp was a client presentation fault. Read-only replay of
ship #1's saved recall landing against its public planet definition measured
8.323 m centre clearance, 0.310–0.339 m landing-foot clearance and a 16.965°
boarding ramp. The server's landed destination was supported. Capture and replay:
`TestResults/Multiplayer/RecallLandingCapture/analysis.json`. The original world
was read only and has not been repositioned or repaired by this investigation.

`PrepareShipPoses` smoothed detailed ships from `entityPositions`, but only the
coarse primitive render branch wrote that dictionary. The detailed craft branch
returned early, so a promoted ship could blend from the same old airborne proxy
position every frame indefinitely. Its visible ramp stretched down to terrain
while authoritative boarding contacts remained at the landed position.

Presentation now commits the current drawn ship point every frame. Unoccupied
landed ships use the authoritative point and attitude immediately, aligning the
visible hull/ramp with walking collision. Ship removal from interest clears both
cached position and attitude, preventing interpolation from an unrelated old
location on re-entry. No landing-height or authoritative flight change is made.

Six native regression cases cover airborne interpolation, proxy-to-detailed
convergence, immediate grounded alignment at a low render blend, matching visual
and authoritative ramp endpoints, interest-exit cleanup and clean re-entry.
The earlier recall suite verified landing simulation but missed this client
presentation transition. This correction changes client presentation only; it
does not establish a new simulation/frame-time/capacity performance pass.

## Flight prediction, 30 km orbit and navigation (2026-11-15)

Content `survival-4` (protocol 13, existing save version 3) requires matching
client/server updates without changing world identity or ownership. Orbit hold
engagement uses terrain relief + 20 m and a visual-atmosphere contribution capped
at 30 km. Once engaged, hold may continue below the engagement altitude while
acquiring tangential speed, until the actual terrain safety margin; it does not
switch off from a centimetre of gravitational descent. The local orbit guide
uses the same engagement rule and remains visible for an engaged hold.

Post-respawn/reboard flight had a separate prediction-layout lifetime bug:
`peer.privateShip` retained its old receipt while the player left the ship, but
the client's `latest.pilotedShip` no longer existed on foot. On returning to that
same ship, the receipt suppressed the resend and prediction ignored the cached
public layout, falling back to linear extrapolation. Publication now invalidates
the receipt when no ship is attached; owner prediction also resolves the cached
layout while a refreshed private layout arrives. Full physics remains private
to its owner. Recoverable death containers have no movement collider and do not
create a second seated character.

Render interpolation follows adjacent input sequences instead of assuming server
and client ticks never realign. Reconciliation rebases previous render samples,
then smooths camera-local, ship-parent position and ship rotation errors together.
Explicit authority epochs/control changes clear every correction offset. This
keeps pilot cameras and walking interiors aligned with their displayed hull at
accelerated test speeds; it does not weaken server movement validation.

The first matching 8x standalone comparison exposed 16–18 ms reconciliation
replay spikes. The final client can retain the pending future of a verified
acknowledgement only when entity/actor/frame/sequence/tick and physical mode
state match. Kinematic tolerances are 1 mm / 1 mm/s with tightly bounded rotation
and walking-state checks; non-kinematic physics fields are compared after
normalizing roundoff, inactive path history and HUD text. Any mismatch, automatic
flight, foreign/private-physics-redacted ship or authority reset uses normal
restore/replay. Retention restores the celestial model to pending-input time
once, rather than leaving it at the acknowledged past time. Diagnostics count
retained and replayed snapshots. This is a client prediction optimization, never
a server admission shortcut or extra simulation-time budget.

The online UI Toolkit HUD has a centered, non-interactive crosshair hidden for
menus/chat/death. Compass marker geometry now refreshes every rendered frame;
inventory/readout text may remain throttled. An owner-only canonical body-frame
ship beacon in every core/lean snapshot removes AOI and planet range limits for
YOUR SHIP without subscribing to its distant physics/layout/inventory. Nearby
other ships have separate bounded markers. Nearby beacons use the same presented
ship point as the hull; distant inactive ships use last authoritative position
and do not extrapolate a frozen simulation. Navigation adds no per-marker world
GameObjects.

Validation: 308 native checks and 282 production domain checks pass. Matching
checkpoint hash for accelerated standalone comparisons:
`F5610F24AF725C26F672A142CC6AB7DBBEA5A1C114F4DAFFA98E4CA4F7B44922`.
The first baseline attempt `Live/505400c04cf0` failed to acquire the pilot seat
because the synthetic walker stood just outside the real 3.5 m reach. Only the
disposable fixture was moved to z=29; passing archived baseline is
`Live/61ea20551078`. Pre-optimization updated run `Live/918a884169fe` passed,
with zero hard corrections and a smaller matching prediction error, but retained
replay stalls; it is not the final performance claim. Neither synthetic flow
replaces the interactive death -> recall -> reboard playtest. Native tests cover
receipt invalidation, cached-layout 8x owner/interior prediction, retained-state
rejection, celestial-clock restoration and 30 km acquisition. The 100-player,
full gameplay load and release FPS acceptance remain unvalidated/deferred.

Final measured comparison: archived `Live/61ea20551078` versus optimized
`Live/3b6272035ffd`, from identical checkpoint hash above, at 8x time. Both
functional sessions passed with zero hard snaps. Frame median/p95 changed from
7.762/22.963 ms to 7.549/11.146 ms; reconciliation median/p95 from
14.409/16.192 ms to 0.145/0.149 ms. During the measured final window, 150 verified
ship snapshots retained prediction and zero required full replay. Maximum
matching prediction error was 0.000145 m (before 0.194052 m). This is a short
GPU-fenced diagnostic comparison, not a release FPS or capacity pass.

Server tick median/p95 was 12.970/16.399 ms before and 8.411/18.663 ms after;
base simulation rate 29.942/29.945 Hz and physical substeps 239.532/239.557 Hz.
Snapshot p95 1.212/1.046 ms. Send/receive 82064/9952 versus 47521/5284 bytes/s.
These server windows include different admission populations (mean 1.358 versus
0.751), so bandwidth/tick differences do not establish a steady-population
improvement. Primary client peak working set/private bytes: 1124.5/2772.1 MiB
before and 1130.8/2770.8 MiB after; server 302.6/402.6 versus 302.7/400.1 MiB.
Full raw results and limitations: `TestResults/Multiplayer/flight-navigation-validation.json`.
Native final result: `flight-navigation-verified-native.log` (308 passing checks).
Five conditional compilation configurations and 282 production domain checks pass.
