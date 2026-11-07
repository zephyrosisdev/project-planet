# Space survival multiplayer plan

## Current scope

Survival is the main menu's normal start action and the PlanetDevelopment scene's
saved mode. It starts in first person. It retains planetary terrain, weather,
spaceflight, radial gravity, floating origins, survival vitals and inventory.
It does not instantiate PlanetRtsBase, PlanetEncounter, CivilisationRuntime,
settlement renderers, cargo navigation, civilian/crime systems, or RTS HUDs.
No army or pathfinding GPU buffers are allocated in survival. The pathfinding
implementation, legacy RTS launch APIs and 10K–1M stress fixture are retained.
Starting tanks and prototype deployment require the legacy base and cannot spawn
in survival. Other planets inherit the session mode.

This is an offline foundation. No multiplayer packages or cloud credentials have
been added. Survival construction now uses independent storage, and an industrial
prototype is integrated. See [implementation status and remaining gates](INDUSTRIAL_IMPLEMENTATION.md)
for current controls, verified domain behavior, the standalone GPU checkpoint
probe, and the substantial work still required for the full design.

## Networking choice

Use Unity Multiplayer Services SDK sessions for Lobby and Relay, with Unity
Transport and Netcode for GameObjects for the initial small hosted prototype.
Unity's sessions API integrates session membership and connection setup; it
supports both player-hosted Relay connections and dedicated server connections.
See [Unity's session overview](https://docs.unity.com/en-us/mps-sdk/sessions-overview)
and [session integration](https://docs.unity.com/en-us/mps-sdk/sessions).

Start with two clients, then validate an eight-player scenario. Eight is an initial
test scenario, not a promised final player cap. Choose the production player count
from measured network, server and client budgets. Relay routes packets: it does
not simulate the world, validate hits, save bases or keep a player's host running
after they quit. For persistent public survival worlds, plan a dedicated
authoritative server and durable storage. A hosted session is useful for early
playtests but trusts the host. Lobby ownership transfer alone must never be
treated as migration of gameplay state or the Relay connection.

Unity documents Relay's region routing and player limits separately. Those service
limits are not evidence that this game can support that many players:
[Relay limitations](https://docs.unity.com/en-us/mps-sdk/relay-limitations).

## Authority and GPU boundary

GPU-heavy rendering is compatible with networking. Keep terrain meshes, weather
presentation, LOD, distant planets and visual particles client-local. Send the
world seed, generator/content version, authoritative celestial time, terrain edits
and persistent world changes. Clients reconstruct presentation from that state.
Do not assume physics or GPU compute is deterministic across machines.

The host/server owns player movement validation, health, oxygen, inventory,
resource nodes, crafting, construction, doors, ship physics and damage. Clients
send sequenced input/actions; the host validates ownership, reach, movement,
costs and cooldowns. Use local movement prediction and reconciliation, with
interpolated remote players/ships. A NetworkObject per player or independently
interactive ship/building is a bounded gameplay choice; never create one per
legacy army unit.

Current local controllers, personal consumables and ship integration mutate
state directly. Put these behind authority interfaces before connecting remote
players. Use stable entity IDs, network ticks and body/frame IDs. Serialize
canonical sector/double positions or bounded frame-relative positions; never
replicate a client's floating-origin Unity Transform as a global position.
Players may visit different planets simultaneously: the current single
ActiveWorld and local observer sleeping rules must become presentation concerns;
the server schedules occupied world regions independently.

The GPU army architecture remains GPU-authoritative, without a CPU unit mirror.
If NPC armies return, use one authoritative GPU simulation, asynchronous bounded
event/snapshot extraction and interest filtering. Do not sync complete buffers
every frame or run independent GPU lockstep on clients. That path needs a server
with a working compute device and measured readback/network costs. A graphics-free
headless server cannot be assumed to run existing compute terrain/navigation.
For the survival server, implement bounded CPU terrain queries/collision using
the same seed and edits, then test CPU/GPU surface agreement. This does not create
a CPU mirror of army state. Retaining source pathfinding does not yet make it
server-ready.

## Implementation order and acceptance

1. Separate survival world persistence, build sites, construction coordinates and
   resource ownership from civilisation. Save/load stable IDs and versioned world
   changes. Restore first-person building on that storage.
2. Introduce an offline authority boundary and input commands. Separate local
   camera/input from player state, celestial simulation and ship movement.
3. Add Authentication, Multiplayer Services, Transport and NGO packages after
   checking versions against this editor. Configure a UGS project/environment;
   implement host, join code, leave and failure handling in a small test scene.
4. Connect two standalone clients: authoritative spawn, predicted walking,
   remote interpolation, reconnect, disconnect and late-join snapshots. Test
   planetary origin shifts and ship/frame handoffs.
5. Replicate gathering, inventories, crafting, construction and doors as validated
   transactions. Reject duplicate/replayed commands, invalid reach and item
   creation; verify late join and reconnect cannot duplicate items or structures.
6. Add authoritative ship controls, seat ownership, combat, death and respawn.
   Test simultaneous players on different bodies, latency and packet loss.
7. Add durable server saves, restart recovery and a dedicated server build. Make
   host loss behavior explicit before public testing; test a server without a GPU
   against bounded terrain collision queries.

Preserve 60 FPS at 2560×1440 on RTX 3070 as the acceptance target. Keep the
million-unit benchmark separate and unchanged. For substantive simulation work,
capture equivalent standalone baseline/current scenarios: median and p95 frame
time, simulation ticks/second, measured managed/native/GPU/process memory, plus
network bytes/second, snapshot/readback cost and server tick time. Reject legacy
army regressions above 5%; retain the ≤3 ms p95 total fog target. Never compare
zero-army survival to a million-unit battle as a performance improvement. Include
an eight-player host scenario to measure the combined simulation/render burden.

## Verification for this change

The runtime assembly compiles with Unity's bundled C# compiler and cached editor
references. This is a compile check, not a Unity Play Mode or standalone pass.
The project is already open in Unity; a second batch editor cannot open it.
No equivalent standalone before/after run has been completed, so median/p95 frame
time, simulation rate and memory are unmeasured. No performance pass is claimed.
