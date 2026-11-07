# Industrial implementation status

This change implements the first offline industrial foundation and a GPU-server
device/checkpoint probe. **The complete industrial, shipbuilding and fleet plan
is not implemented.** Domain checks, integrated prototype code, and unverified
acceptance requirements are distinguished below.

## Trying the prototype

Start Survival with freshly imported scripts/assets. The industrial presentation
now uses the Blender colony prototype kit for buildings and stationary machines.
See [colony construction controls and progression](COLONY_PROTOTYPES.md). A complete
standalone gameplay/performance acceptance run is still outstanding.

- **I → Crafting** or **F4** opens the UI Toolkit crafting browser with categories,
  search, recipe details, quantities and a persistent cancellable queue. Escape closes it.
  Use **I → Construction** to place directly with resource costs or enable
  **CREATIVE MODE** for free testing. Test supplies can be added in creative mode.
- Finite starting resource markers identify iron/copper pay dirt, radioactive pay
  dirt, stone, fuel, and a water supply. Move within 5 m and hold **G** to collect.
  This depletes saved material quantities; **it does not yet deform the terrain**.
- Use a workbench, generator, sluice, pump and foundry to bootstrap production.
  Generators consume fuel. Pumps move finite supplied water into connected machine
  reservoirs; water is not generated freely. The sluice recycles half its supply.
- Aim at a placed machine and press **F** to deposit/withdraw materials or change recipes. Select a
  source, then a destination, to connect power, water, or a filtered conveyor. A
  conveyor needs a manufactured belt kit; links are limited to 30 m.
- Mark a stockpile, then dump/scoop material through its context menu. Its height grid and
  visible mesh grow/shrink with volume. Composition and activity are conserved.
- Pay the excavator/truck/loader resource list to place its vehicle, or place freely
  in creative mode.
  Enter through its context menu: **WASD** drives, **Space** brakes, **F** exits. Supply
  fuel in the payload. Excavator collection and payload transfer/discharge actions
  are available in context menus. Articulated digging/tipping animations and route
  automation are not implemented.
- The personal inventory grants the building blueprint again. Equip it, hold
  **RMB** and scroll categories to choose a module, **LMB** to place, **R** to rotate, **Z** to undo, and
  **F** to operate doors. Foundations use stone; structural panels use metal, and rooms use their listed
  resources/components. Undo returns the recorded paid materials when there is capacity.
  Workshops and warehouses provide sockets for compatible machine kits.
- Radiation is visible in the industry HUD. Closed shielded stores reduce source
  emissions; opening/damaging shielding increases them. Manufactured suits reduce
  exposure, treatment reduces dose, and sustained exposure can reduce health to
  zero. Radioactive pay dirt, products, tailings, payloads and belt contents retain
  their activity.
- Survival prototype ship rockets now consume manufactured `missile` items from
  the industrial inventory. Crafting military items does not yet implement modular
  weapons, guided fleet missiles, nuclear detonation, or bombardment.

The personal food/tool inventory and the industrial material inventory remain
separate adapters. The inventory and crafting UI expose the latter; unifying player item storage
and persistence is still required before production multiplayer.

## Architecture delivered

`Runtime/Industrial` contains engine-independent definitions, world records,
inventories, lots, actors, machines, recipes, piles, conveyors and commands.
Quantities use integer units. Dirt uses litres plus constituent quantities and
activity. Item stacks carry their actual total activity through transfers and
recipe reservations; processing cannot silently remove it.

Commands validate ownership, reach, capacity, compatibility and sequence before
mutation. Machines reserve outputs before consuming inputs. Belts preserve order,
filter items, propagate backpressure and recover their contents on removal.
Power is currently explicit generator-to-machine allocation in stable creation
order, not a general multi-generator electrical network.

One authority serializes mutations. A flushed transaction journal precedes each
accepted change; versioned snapshots are atomically replaced before journal
compaction. Recovery replays logical ticks and transactions once, including
construction payment/data/ID allocation and vehicle position checkpoints. An
unterminated crash tail is truncated; other corruption fails explicitly. Backup
snapshots are retained for manual diagnosis, not silently substituted after
journal compaction.

Survival construction uses `PlayerBuildStorage`, independent of civilisation.
The legacy adapter references the original settlement-backed lists without
reactivating that simulation. Construction commits are an internal validated
Unity adapter; arbitrary construction JSON is not accepted by the wire gateway.

Survival uses one local industrial actor. Worlds save under
`Application.persistentDataPath/Survival/<seed>/industry.xml`. Visited planets share
one industrial authority; all recorded machine networks advance while the active
survival session runs. This is not yet an always-running dedicated world host.

Presentation uses instanced belt items grouped by type, changing pile meshes,
combined nearby machine collision geometry and a bounded nearby pile contact
pool. It creates no GameObject/Rigidbody/NetworkObject per belt item or dirt grain.
Only the currently driven industrial vehicle uses the existing detailed motor.
No legacy army simulation buffers, kernels or million-unit configurations changed.

## Verification and its limits

- **38 domain checks passed**, including two real TCP loopback clients, the full
  dirt → sluice → foundry → press → storage chain, conservation, blocked outputs,
  save/replay recovery, construction payment, vehicle payloads and lethal radiation.
- Runtime sources compiled against Unity's cached editor and player references.
- A graphics-enabled standalone probe passed on **RTX 3070 / Direct3D 11**:
  **4,096 × 64-byte records**, a **262,144-byte checkpoint**, and a byte-for-byte
  GPU-buffer restore. Its memory sample was **257,032,294 bytes Unity allocated**
  and **3,600,384 bytes managed**. These are probe samples, not full-game or GPU
  memory budgets.
- The probe used an isolated copy of an existing same-engine player with rebuilt
  scripts and its probe startup registration added. It reused that player's baked
  assets. It does not certify a fresh build, industrial scene presentation, shader
  changes, multiplayer GPU replication, or gameplay physics.
- The TCP gateway binds **loopback only** and uses server-issued test tickets. It
  is a transport/concurrency proof, not Unity Lobby/Relay, UGS authentication,
  movement prediction, interest management or production network security.
- No equivalent standalone before/after factory/fleet benchmark has completed.
  Median/p95 frame time, full-game simulation rate, process/native/GPU memory,
  bandwidth and fog cost remain unmeasured. **No performance acceptance pass.**

## Remaining acceptance gates

| Milestone | Status / next work |
| --- | --- |
| GPU-server architecture | Device/checkpoint and separate two-client authority probes pass; combine authoritative GPU state with replication, authenticated players, persistence and a real server lifecycle. |
| Industrial construction | Offline paid placement/storage adapter implemented; verify the fresh scene, terrain/occupancy validation, player inventory unification and remote construction commands. |
| Manual mining | Finite material and payload/pile handling implemented; replace the 32-stamp terrain limit with persistent surface-height chunks, shared CPU/GPU queries, excavation collision and support validation. |
| Processing | Domain chain passes; validate presentation/physics, add construction previews, render power/fluid links, and implement general power/fluid networks and logistics junctions. |
| Automation | Manual prototype vehicles implemented; add articulated buckets/tipping, machine interfaces, saved routes, work zones, reservations, navigation invalidation and recovery. |
| Radiation | Domain exposure/shielding/activity flow implemented; verify scene HUD/equipment and complete survival death/respawn and contamination zones. |
| Modular shipbuilding | Pending: authored modules/socket graph, shipyard payments, aggregate flight body, walkable compartments, service graphs and launch validation. |
| Carriers | Pending: arbitrary docking ports, moving-frame capture, berth reservations, service/launch/recovery and persistent docking graphs. |
| Military industry | Recipe/item foundation and prototype ammunition consumption implemented; modular damage/breaches, fleet weapons, nuclear effects, interception, fallout and salvage remain pending. |
| Scale acceptance | Pending: 16 players, 100 capitals, 2,000 fighters plus the full factory fixture; preserve actual populations and report equivalent measurements. |

Current belts have one lane with 0.6 m spacing. The planned 50,000 in-flight items
on 10 km of physical belts needs at least three such lanes, or another explicitly
validated belt tier. Do not seed overlapping items or silently lower that target
to label the stress fixture a pass.

Unobserved production currently uses fixed logical ticks, not the planned
event-scheduled catch-up. Always-on hosting, Lobby/Relay integration, bounded
interest replication, detailed/strategic fleet transitions and production save
migrations remain open. The 60 FPS / 1440p / RTX 3070, ≤3 ms p95 fog and ≤5% army
regression requirements remain unchanged.

Bounded autonomous truck collection is now available: selected finite pay-dirt deposits/stockpiles → sluice/store/stockpile, with reservations, fuel, route recovery and persisted jobs. See [controls, scope and measured limits](AUTONOMOUS_TRUCK_COLLECTION.md). This is an outdoor hauling prototype, not general excavator/loader automation or a completed persistent multiplayer deployment.
