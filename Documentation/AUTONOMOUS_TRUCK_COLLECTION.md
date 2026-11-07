# Autonomous collection and water guidance

The current mining roles and sluice waste behavior are described in [Mining teams and front-discharge tailings](MINING_TEAMS_AND_TAILINGS.md). Excavators now dig at the node and load trucks; trucks transport rather than mine. Assign a separate worker to periodic cleanup of the sluice's front heap. The collection description below documents the earlier direct-hauling workflow; use the new guide for current role selection and fuel costs.

## Water

Open **I > Construction > Locate nearest water intake**, or use the button in a pump's **F** menu. These finite WATER deposit markers are the intake sites, rather than separate constructed intakes. The HUD tracks direction and distance.

Place a pump within **8 m** of a marker, supply **2 kW**, and choose **Use water intake**. Pipe its output directly to a sluice or through a **Water Reservoir**. The reservoir costs **8 iron bars and 4 plates**, holds **10,000 L**, and needs no power. A sluice separately needs **5 kW**, water and room for both ore and tailings. Reservoir and sluice interaction menus can fill from carried water. Water is conserved through pump, tank and sluice; pumps cannot generate water from nothing.

## Vehicle operation

Press **F** on your truck, loader or excavator and choose **Drive Vehicle**. Use W/S for throttle/reverse, A/D for steering, Space for braking, Alt for free look, F to exit and F4 for payload controls. Survival requires Fuel in Machine Input. Creative manual driving requires none. Entering an intact switched-off vehicle starts its engine.

For autonomous collection:

1. Place a vehicle and an accessible sluice, store or destination stockpile outdoors.
2. Open the vehicle's F menu. Leave **Collect from** on **AUTO** for the closest eligible nonempty pay-dirt node, or choose a specific deposit or owned stockpile.
3. Leave **Deliver to** on AUTO for the nearest sluice, falling back to an owned pile/store, or select a destination.
4. Press **Auto operation / Replan**. Planning status appears while a long route is checked; successful assignment closes the menu and tracks the vehicle in the HUD.
5. Supply Fuel in Survival. Newly assigned Creative jobs have free propulsion; changing back to Survival restores fuel requirements.

Automatic mode tries the three closest eligible nodes per request, skipping submerged or construction-undermining deposits. Once a deposit depletes, an empty automatic vehicle switches source. Explicit-source jobs keep their source; an empty stockpile job waits for deliveries. Trucks can deliver to a growing pile while a loader moves material from that pile into a sluice. Leave clear service access around piles (approximately 15 m berth radius).

Collection and unloading are timed bulk-material transfers at berths, without animated excavation or bucket handoffs. These jobs transport pay dirt, including its grade and radioactive content, rather than manufactured item stacks. They cannot discover deposits absent from the world records.

**Stop collection** retains cargo and releases destination reservations. **Drive Vehicle** cancels planning and stops autonomy before manual takeover. Stop jobs before manually transferring bulk cargo or dismantling endpoints. Use Auto operation / Replan to replace a saved route after updating the game.

## Failure and recovery

Full destinations pause upstream collection; competing trucks reserve available space without duplicating cargo. Missing Survival fuel stops movement. Waiting/loading does not consume additional movement fuel. New mining that would undermine construction is refused.

Players, machinery, other vehicles, stockpiles, authored building footprints and the player capital ship block movement. A blocked vehicle waits and resumes when clear. The authoritative adapter attempts recovery after sustained blockage, at most one request per second globally and one per truck every ten simulation seconds. Failure preserves its route and cargo and reports a reason. A loaded vehicle can revisit its source berth when rejoining a newly planned route. Narrow traffic corridors may require changes to the factory layout.

## Route bounds

The **300 m site restriction is removed**, along with the old 1 km route cap and 256-point cap. Source and destination must share the vehicle's body and reference frame. Longer routes follow the planetary surface in approximately **120 m sections**, yielding between sections and failed local searches. Each local search permits **4,096 expansions** on at most a 129 x 129 grid. Intermediate checkpoints can shift around local obstructions. One adapter planning request runs at a time.

There are explicit budgets of **100 active industrial-vehicle jobs** and **65,536 route points** per job. The latter permits roughly 130 km of direct densely sampled route including both legs, less with detours. Exceeding it gives a visible failure suggesting an intermediate logistics site; it does not discard cargo or jobs. Every route point is revalidated before assignment; movement checks obstacles continuously. Final validation, journal size and save costs grow with route size.

This is bounded local corridor routing, not a guarantee of whole-planet reachability. Water, steep slopes, blocked access and occupied berths can prevent a route. Interior navigation, tunnels, moving carrier logistics and animated bucket handoffs remain future work.

## Authority and persistence

Compact authoritative vehicle records retain jobs, fuel, composition, cargo, reservations, route progress and recovery state. Snapshots and the idempotent journal preserve these through load and replay. The simulation continues when owners are elsewhere; stopping/pausing the server freezes it. Unrestricted downtime catch-up is not implemented.

Future network deployment must run terrain queries and planning on the server. The loopback transport rejects client-authored haul-start routes pending that integration; it is not a complete multiplayer implementation. Industrial navigation neither mirrors GPU army positions on the CPU nor creates per-army-unit GameObjects. Retained GPU army navigation and the million-unit path are unchanged.

## Validation

Latest checks: **181 core, 53 UI/geometry and 141 placement/surface checks pass**, and Unity compiles with zero compiler errors. Checks cover finite collection, fuel, competing reservations, manual takeover, material activity/composition, depleted/full destinations, obstacles, ship and pile clearances, route recovery, snapshots and crash replay. New tests cover a **4 km** surface route, incremental planning, an obstructed intermediate checkpoint, full authoritative route validation, mid-trip reload, conserved radioactive delivery and exclusion of other planetary frames.

### Standalone performance measurements

Release .NET control fixture: 1,000 machines, 16 observers, three alternating baseline/current runs, 6,000 warm-up / 30,000 sampled ticks, tiered compilation disabled. Median tick **0.0569 to 0.0570 ms**, p95 **0.0651 to 0.0658 ms** (+1.08%), simulation rate **17,175 to 16,941 ticks/s**. Median private process memory **13,303,808 to 13,549,568 bytes**, working set **32,010,240 to 32,149,504 bytes**, managed memory **1,266,056 to 1,275,960 bytes**. The frozen baseline predates autonomous hauling, rather than isolating this range fix.

Two current Release active-work runs retain **100 jobs**, 1,000 stationary machines and 16 observers, delivering **1,919,220 L** each over the same 600 warm-up / 3,000 sampled ticks. Median tick **0.8533-0.8554 ms**, p95 **1.7001-3.0868 ms**, simulation rate **875-1,149 ticks/s**. Private memory approximately **46.6 MB**, working set **72.4 MB**, managed **17.8 MB**. Planning 100 short jobs totals **114-118 ms**. Warm snapshots save in **69.5-73.5 ms**, load in **100.9-117.5 ms**, and serialize **2,928,907 bytes**. The p95 spread is retained; this is not an isolated active-work regression pass. Debug measurements are kept separately and excluded from the Release comparison.

Raw evidence: TestResults/Industrial/LongRoutes/comparison.json and adjacent files. These are industrial CPU tick measurements, not Unity frame timings. Unity native/GPU memory, fog, readback, bandwidth and combined fleet/million-unit performance were not measured. GPU army code is unchanged; full acceptance gates remain outstanding.

### Saved planet-scale endpoint correction

On entering a planet, the authoritative adapter projects its existing surface deposits onto the current terrain before planning. This repairs old-radius coordinates without replacing deposit IDs, restoring mined stock, changing composition/activity, or erasing removed volume. Position corrections are journalled before mutation and replay before subsequent haul commands. Resource seeding still creates only missing supply types. This also repairs old fuel/water intake locations.

The planner rejects projected service berths outside the allowed pickup/delivery reach before selecting a route. Validation now distinguishes a moved vehicle from an invalid pickup or delivery berth. Restart Play mode after the update, then use the vehicle's Auto operation / Replan control. Existing jobs are not silently redirected.

Regression checks: 186 industrial core checks pass, including old-radius repair, idempotence, accepted truck assignment and conserved radioactive delivery through journal recovery. This changes initialization and planning validation; it does not add per-tick terrain queries or alter GPU army simulation.

### Building clearance and distributed resource update

Construction persistence now records oriented rectangular footprints (width/depth, yaw and height) for industrial route checks. Enclosing circles no longer unnecessarily close the space beside long buildings. Legacy obstacles retain their circle fallback until construction persistence refreshes their definitions, including when Auto operation / Replan is pressed. Real building footprints still block vehicles and unsafe mining.

Local searches retry with 24 m, 60 m and 120 m margins, retaining a 129 x 129 grid cap and 4,096 expansions per attempt. Failed attempts yield before retrying. Grid start and goal positions use the actual service berths, and edges are checked before inclusion. A blocking building is identified by ID where the failure occurs at a service point. A route failure through intermediate terrain can still report a searched-corridor failure; the algorithm is not a global navigation guarantee.

New starter resources use deterministic varied polar placement, normally around 90-400 m from the landing anchor, with a minimum 80 m separation after terrain checks. Dry ground, local slope and existing industrial obstacles are checked. A bounded number of alternate sites is attempted; inability to find suitable ground is reported rather than spawning underwater.

On planet entry, the old tight cluster (legacy layout, all within 40 m of its first point) is moved using the same checks. Existing IDs, remaining stock, removed volume, material grade and radioactive activity are retained. The layout version and positions are journalled. Depleted deposits stay depleted; sites do not move on later reloads. A job referencing a relocated source is stopped through an authoritative command, retaining cargo, so the player can explicitly replan. If suitable new sites cannot be found, existing resources remain in place. Restart Play mode or revisit the planet to run the migration, then replan vehicle collection.

Validation: 192 core checks, 86 Unity arrival/distribution checks, 164 Unity placement/vehicle/migration checks and 53 UI checks pass; scripts compile without errors. New tests demonstrate clear passages between long buildings, wider detours, rotated footprints, preserved resource layouts through crash recovery, minimum spacing and one-time migration. They do not establish reachability of every live factory layout.

Matched standalone Release comparison: 1,000 stationary machines, 100 active trucks, 16 observers, 600 warm-up / 3,000 sampled ticks; three alternating runs per build, tiered compilation disabled. Median tick 0.8342 -> 0.8390 ms; p95 1.0022 -> 0.9959 ms (-0.63%); simulation rate 1,308 -> 1,303 ticks/s. All runs retain 100 jobs and deliver 1,919,220 L. Private memory 46,510,080 -> 46,501,888 bytes; working set 72,409,088 -> 72,384,512 bytes; managed 17,782,608 -> 17,821,408 bytes. Planning 100 short jobs totals 107.23 -> 112.54 ms. Warm snapshot save 27.45 -> 28.01 ms, load 35.57 -> 34.66 ms; serialized size 2,928,907 -> 2,932,907 bytes. Figures are medians across runs; raw outliers remain in the evidence.

Evidence: TestResults/Industrial/PlanningSpread/comparison.json and adjacent run files. This measures industrial CPU ticks and managed/process memory, not Unity render frame times. Native/GPU memory, fog p95, server bandwidth and GPU readback were not measured. GPU army code and the million-unit path are unchanged; no combined fleet/frame acceptance is claimed. Obstacle-heavy planning and large-world initial resource placement need separate performance profiling.
