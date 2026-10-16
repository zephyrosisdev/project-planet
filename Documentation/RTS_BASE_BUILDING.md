# Base-building RTS

Start `Assets/Scenes/MassiveBattle.unity` in Play mode and choose **START BASE BUILDING RTS**. The chosen scale reserves GPU capacity; it does not give either faction a free mass army. The original **DEPLOY ARMIES** mode remains available for stress testing.

## Starting a base

1. Select the Command Centre, then construct wind or solar generation from Power.
2. Hover a visible resource deposit to inspect its type, quality and remaining reserves. Choose a matching extractor from Economy; the placement ghost snaps to nearby compatible deposits.
3. Build Barracks and a Light Vehicle Depot. Select a completed producer to queue units. All costs are deducted when an order is accepted.
4. Construct a Research Lab and upgrade the Command Centre. Open the technology graph, select a technology and an appropriate laboratory, then start research. The graph shows prerequisites, costs, time and unlocks.
5. Use scouts to reveal new territory and capture sectors. Controlled sectors or a powered construction radius permit expansion. Develop factories, defenses, airfields, coast-dependent naval yards and higher research eras.

Generation and demand are separate MW quantities. Fuel-burning generators consume fuel; batteries store surplus and bridge deficits. Low power slows basic facilities and disables demanding systems. Extractors stop depositing when storage is full and leave the unaccepted quantity in the deposit. Research laboratories generate Research, a separate currency. Selling a completed building returns 45% of its construction cost; unfinished queues and upgrades refund their remaining fraction.

## Controls

| Action | Control |
|---|---|
| Select units | Click / drag; Shift adds, Ctrl removes |
| Move selected army | Right-click terrain |
| Attack an enemy building | Right-click the visible hostile building; units approach and fire when in range |
| Set formation width and facing | Hold the right mouse button and drag across terrain, then release; mixed unit selections share the layout with separate terrain routes |
| Attack-move | A, then left-click |
| Stop | S |
| Construction / technology | B / T |
| Select Command / Barracks / Factory / Airbase / Naval Yard | F1 / F2 / F3 / F4 / F5 |
| Assign / recall a control group | Ctrl+number / number; Shift adds |
| Set producer rally | Select producer, right-click terrain or a friendly unit |
| Repeat placement | Hold Shift when placing |
| Place walls | Choose Wall and drag across terrain |
| Cancel placement, patrol editing or technology panel | Escape; right-click cancels a building ghost |
| Pause | Space |
| Camera | Arrows, middle drag, wheel, Q / E; existing camera bindings also apply |

Base hotkeys are Inspector-configurable. Production panels support pause, paid repeat, reordering, cancellation, clearing, upgrades, repair, power down and decommissioning. Factory upgrades add parallel bays. Defense panels support hold fire and domain priorities. Gates open for nearby friendly ground traffic when no hostile ground contact is present.

Scouts and reconnaissance aircraft support Spiral, Grid, Sector, Perimeter, Route, Auto Explore, Observe and Follow Army. Route editing accepts 2–16 terrain waypoints. Up to 64 active shared route commands are retained; routes without users are recycled. Auto Explore scores unexplored cells and abandons stalled destinations. Select an APC, transport helicopter or transport ship and use Load Transport near eligible passengers; Unload finds traversable positions near the carrier. Aircraft take off, fly missions, return when fuel, ammunition or health runs low, rearm and repair at powered air facilities, then resume. Researched carriers can launch paid fighters from the selected carrier.

Supply consumption for ground forces is optional through **SUPPLY ON/OFF**. Aircraft servicing remains active independently. Supply vehicles replenish nearby ammunition/fuel, repair vehicles heal nearby allies, depots repair units, and command vehicles improve nearby movement and firing rate. Engineer and repair-vehicle presence also repairs damaged buildings at a material cost.

## Architecture

`RtsCatalog.json` is the authored source of building, unit and technology definitions. `Tools/generate_rts_catalog.py` regenerates it. Catalogue validation checks IDs, references, nonnegative costs, supported GPU type count and dependency cycles. The runtime has 49 buildings, 31 units and 25 technologies.

`RtsWorld` owns only buildings, finite resource nodes, faction wallets, queues, tech state and high-level AI. `RtsEconomy` advances construction, extraction, power, storage, research, paid repeat orders, upgrades, repairs, capture and building damage. `RtsStrategy` chooses paid enemy build/research/production orders and retains only discovered enemy infrastructure.

`GpuBattle` retains authoritative 64-byte unit records. Production submits batches of at most 64 32-byte spawn commands. The GPU claims a vacant slot, writes both snapshots, initializes operational state and returns a compact acknowledgement. Paid orders remain queued if no slot is available; death returns slots to the GPU free stack. There is no CPU unit list, unit prefab, per-unit Transform, Rigidbody, NavMeshAgent or ECS entity.

The GPU also owns 64-byte unit traits, 48-byte aircraft/transport records, 32-byte scouting records, control-group masks, fog/radar maps, support influence and a bounded pool of 32 strategic missiles. Patrol points are shared high-level commands, rather than CPU unit paths. Mixed-domain orders allocate separate movement routes. Air and water traffic do not contribute ground crowd pressure; carried passengers leave collision, sensor and rendering grids.

Buildings upload at most 512 64-byte records. GPU weapons accumulate per-building damage and send compact reductions to the CPU. Fog and radar are 128×128 for each faction; readbacks contain maps, building contacts, sector reductions and acknowledgements, rather than complete unit snapshots. Sensor/structure/support work runs at approximately four updates per second, with readbacks gated to prevent an unbounded queue.

Placement validates terrain, coastline, compatible unclaimed nodes, footprint overlaps, scouting, territory, build radius, affordability and technology. Buildings update only footprint rows in the mutable navigation costs and invalidate a local neighborhood of active flow fields. The procedural map and quadtree terrain remain intact. Removing a wall or opening a gate releases its obstacle references. Foundation meshes bridge local height variation.

## Validation

Unity menu **Massive Army → Validate RTS Foundation** exercises paid construction, extraction/depletion, power, queues/refunds/reorder, GPU production, command/factory upgrades, research unlocks, air/naval spawns, fog, obstacle removal and enemy economy. It also constructs a modest base on a map sized for the one-million-unit setting and checks separate resource footprints; this is a startup check, not a million-unit RTS performance benchmark. **Validate RTS Combined Arms GPU** exercises transport manifests, aircraft servicing, radar/SAM engagement, scouting/waypoints, strategic interception/impact, dead-slot reuse, building damage/shields, control groups and mixed-domain routes. Their JSON reports are saved in `TestResults`.

The original **Run GPU Validation** checks stress-mode spatial grids, combat, selection, formations and navigation. Validation-only bounded unit readbacks are Editor code and are not a gameplay unit mirror.

Mixed selections share one formation layout while retaining routes for each movement class. Formation ranks are copied between GPU buffers explicitly created with Unity's `CopySource` and `CopyDestination` targets. Drag previews and committed destinations are checked together by the combined-arms validation. Editor gesture checks exercise right-button press, drag and release through the gameplay input handler; their results are in `TestResults/rts-formation-input.txt`.

Editor battle loads record terrain, GPU allocation, rendering and base setup timings in `TestResults/startup-timing.txt`. These timings exclude Unity's script import/reload and first-use shader compilation outside the loading coroutine.

## Prototype limits

The systems support a playable escalation loop, but competitive balance, lengthy campaign/AI soak tests and mature-base performance at hundreds of thousands of produced units still need measurement. Reserving capacity currently means unit kernels scan that capacity, including vacant slots. The old stress-mode benchmark numbers do not measure these extra systems.

Art uses modular procedural meshes, shared faction materials and building LODs. Unit silhouettes distinguish infantry, vehicles, support/artillery, aircraft and ships; far impostors remain the shared tank atlas. Helicopter/ship subclasses and individual roster entries need more distinctive authored art, animation, audio and richer destruction effects. Blender MCP was unavailable during implementation. Building foundations are meshes, without terrain excavation.

Fog is a coarse radial sensor model without terrain line-of-sight occlusion. Radar variants currently differ by power, range and technology rather than a detailed electromagnetic model. Strategic missiles have bounded interception and area damage; specialized missile types and counter-battery classification need further visual/gameplay refinement. Follow Army uses nearby friendly presence. Rally-to-unit resolves the target's current position when production completes. Wall placement creates straight oriented segments; junction/corner art is currently represented by adjoining wall ends.

AI profiles influence build/research priorities, but the opponent is a prototype strategic AI, not a tuned competitive player. Matches are local, and save/load, multiplayer and a campaign are outside this expansion.
