# Large-section placement and starter power

## Placement fix

The previous room path required a free socket, so a room could never be placed on bare ground. Its fallback ghost was always four metres from the camera. Large room footprints could cover the player's feet, and ground colliders were also treated as obstructions during initial support selection.

Rooms can now begin a standalone site on sufficiently level terrain or connect to existing compatible sockets. The preview moves farther away with its footprint/height, while retaining the actual model scale and matching committed position. Ground placement fits the room above sampled surface heights; steep ground still requires a better site or foundations. Snapped modules stay aligned to their sockets. Foundations retain their original ground-aim behaviour.

The UI Toolkit placement prompt distinguishes missing materials, player intersection, blocked geometry, missing sockets and unsuitable terrain. Collision and reach checks remain active in creative mode. Other buildings, the player's occupied space and obstacles are still rejected.

## Obtaining fuel

New players start with eight fuel units. Look for **FUEL** near the initial landing area; move within five metres and hold **G**. This collects finite fuel into the industrial inventory. Press **F** at a generator, deposit fuel, then connect its power output to machines. Vehicle fuel goes in its input/payload inventory. Creative test supplies can also give fuel immediately.

Fuel refining/manufacturing is not implemented. Older worlds missing the original stone/fuel/water supply nodes gain those missing starter nodes on load; exhausted existing nodes do not refill. Collecting fuel or water does not remove supporting ground, so nearby construction no longer prevents that collection. Pay-dirt/stone excavation keeps its construction-protection check.

## Fuel-free power

All devices are placed from **I → Construction**, using direct resources or free creative placement.

| Device | Prototype construction cost | Behaviour |
| --- | --- | --- |
| Starter solar panel | 8 stone | Up to 20 kW from local sun altitude; zero at night |
| Wind turbine | 4 iron bars, 2 gears | Up to 24 kW from configured planetary wind; works at night; zero without usable wind/atmosphere |
| Battery | 4 iron bars, 4 wire | Stores 6,000 kJ; up to 30 kW discharge and 30 kW charge input, with 90% charging efficiency |

These are prototype gameplay costs and tunable power values. Solar has a stone bootstrap price so the first metal-processing chain does not require fuel or pre-existing metal production.

To connect: open **Construction → Run power cable**, or press **F** on solar, wind or a generator and choose **Run power cable from this machine**. Left-click an output socket, then a destination socket. **Shift + LMB** adds routing points; **RMB** undoes a point/source selection; **Esc** exits. Water uses the separate **Run water pipe** tool, starting at a pump. Connections have a 50 m curved-route limit and up to eight routing points. The original source/destination context buttons remain available. For storage, connect **source → battery → machines**. Direct source-to-machine connections also work.

Each consumer has one source; each battery has one charging source. Multiple consumers can share it within the source's budget. Batteries charge only from unused source power, store actual energy and stop supplying loads when insufficient charge remains. Battery-to-battery charging and invalid/self connections are rejected. Source status, available power, charge and charging rate appear in the context menu. Charged batteries must be discharged or explicitly have their charge discarded before dismantling.

Power/environment state is saved. Journal recovery records the environmental inputs used for each tick, preserving solar/wind/battery outcomes. Server downtime freezes production as before. This prototype samples sun altitude and a planetary wind scalar; it does not simulate roof shadows or turbine aerodynamics.

Renewable devices use small shared prototype meshes and batched collision. They do not add per-item physics objects or change GPU army movement/storage.

## Checks and timing scope

- Industrial domain checks include solar day/night output, no-fuel operation, wind availability, charge/discharge conservation, capacity, invalid connections, save/load and crash recovery.
- Unity action `industry-placement` checks a complete large-room placement/removal on flat ground, preview clearance, player/obstacle/resource failure reasons, ground-collider support and renewable model footprints.
- Unity action `industry-ui` checks retained crafting, construction and interaction controls.

An equivalent standalone **industrial CPU-core** fixture was frozen before the power change: 1,000 identical legacy-type machines and 16 observers, 600 warm-up ticks and 3,000 sampled ticks, three alternating runs per version. Optimized JIT was established before warm-up (`DOTNET_TieredCompilation=0`). Results below are medians of the three runs.

| Measurement | Baseline | Current |
| --- | ---: | ---: |
| Simulation tick median | 0.0816 ms | 0.0641 ms |
| Simulation tick p95 | 0.1042 ms | 0.0849 ms |
| Unthrottled simulation rate | 11,738 ticks/s | 14,801 ticks/s |
| Measured process private memory | 15,728,640 B | 16,338,944 B |
| Measured working set | 34,811,904 B | 34,615,296 B |
| Measured managed memory | 5,054,376 B | 5,104,280 B |

Avoidable per-machine environment closures were removed and immutable machine definitions are cached with invalidation on type changes. The final CPU-core comparison has lower median/p95 tick cost, with a small persistent-data memory increase. Source reports are in `TestResults/Industrial/PowerComparison/comparison-final.json`. The current fixture is reproducible with `Tools/IndustrialChecks/IndustrialPowerBenchmark.csproj` and an output JSON path.

These figures are **not whole-game Unity frame times**. Native/GPU memory, fog cost, rendering, bandwidth and combined fleet-scale acceptance were not measured by this fixture. The 60 FPS / 1440p / RTX 3070 target, million-unit path, <=3 ms p95 fog target and army regression policy remain unchanged and require their existing standalone acceptance scenarios.

## Visible connections

Power cables sag toward local gravity; water pipes use a shallower curve. Coloured service socket markers appear while the tool is active. Existing saved logical connections also render, without reconnecting them. Routes are saved as planetary/frame-relative anchors and recover through the transaction journal. Power inputs and pump outputs can be removed separately through their machine context menus; Disconnect Power and Water clears every link touching the selected machine.

The preview reports invalid endpoints, range and obstruction failures. Source ownership, destination ownership/reach, route length, finite coordinates and frame compatibility are validated by the authoritative data model. The offline world adapter additionally checks nearby collider/terrain obstruction before submitting a connection. Multiplayer world-geometry validation must move to the authoritative world-query adapter when that deployment is integrated.

Connections currently cost no resources, preserving starter power access. Connecting a new source replaces a consumer's existing input; a pump has one outgoing water connection. These are direct links, not a general power-pole or branching pipe network. Later construction does not yet reroute an existing cable. Conveyors retain their current straight connections and visible item transport.

Cable geometry is cached and batched by local interest, with reusable preview meshes. There is no physics simulation or GameObject per cable segment. Army/fleet simulation is unchanged. Unity editor compile, UI/geometry checks and engine-independent industry regressions validate correctness; the full standalone RTX 3070 frame-time/fog/memory acceptance scenario has not been measured for this presentation addition.

## Water and manual mining

Pumps require a 2 kW power connection and an outgoing water pipe. Press F at the pump and either deposit carried **Water** into **Machine Input**, or choose **Use water intake** for a clean **WATER** deposit within 8 metres. The intake draws at most 10 litres per simulation tick (100 L/s), consumes the finite deposit and stops when it depletes or the destination reservoir fills. Intake selection and withdrawals persist through snapshots/journal replay. Pumped water appears in the destination's **Reservoir**, not the pump's item output. The pump menu shows carried water, intake ID and destination ID, with distinct failure states.

The sluice needs its own 5 kW power connection, reservoir water and pay dirt. Its context menu can **Fill reservoir** from up to 100 L of clean carried water as a manual/testing alternative to a pump. This spends carried water; creative mode does not generate processing supplies automatically. Hold G within 5 metres of a **WATER** marker to collect water, or spawn it from Creative Test Supplies.

Hold **G** within five metres of an **IRON PAY DIRT** or **COPPER PAY DIRT** marker to gather bulk dirt. **I → Inventory** shows carried pay dirt. At a sluice, choose **Load up to 100 L pay dirt**. For hauling, load dirt into a truck, deposit fuel, drive it near a sluice/stockpile, leave the vehicle and use its F menu to unload. Trucks can also run repeating autonomous collection jobs from their F menu; see [autonomous truck collection](AUTONOMOUS_TRUCK_COLLECTION.md). Excavators have **Excavate deposit** actions when parked within eight metres of a deposit. Excavators remain manually driven; autonomous truck jobs operate on selected finite deposits or stockpiles.

## Ship door controls

The rear hatch and inner airlock each have green illuminated buttons beside the left jamb, on both sides, at standing interaction height. Look at a button within 3.5 m and press **F** to open/close. Buttons remain stationary when the panel slides open. Line-of-sight and existing occupied-door closing safeguards still apply. The controls join the existing batched ship interior; they add no lights or per-frame army simulation.

### Water change validation

The final changes pass 126 industry-core checks and 45 Unity UI/geometry checks, including finite intake depletion, power/full-reservoir stoppages, direct fills, journal recovery, buttons on both sides of both hatches, open-door access and occlusion. Unity script compilation completes with no errors.

Equivalent standalone .NET water-core comparison: 1,000 machines (100 generators, 100 supplied pumps, 100 sluices, 700 stores), 16 observers, 6,000 warm-up ticks and 30,000 measured ticks per run; three alternating runs, tiered compilation disabled. The baseline retains the previous pump step with otherwise matching code/data. These are industry CPU tick measurements, not Unity frame timings or army-scale acceptance.

| Measurement | Previous pump | Current pump |
| --- | ---: | ---: |
| Median tick | 0.0559 ms | 0.0576 ms |
| p95 tick | 0.0693 ms | 0.0744 ms |
| Simulation ticks/s | 17,086 | 16,400 |
| Process private bytes | 13,553,664 | 13,369,344 |
| Working-set bytes | 33,685,504 | 33,513,472 |
| Managed bytes | 1,266,064 | 1,265,968 |

The industry-only p95 is 7.4% higher in this fixture; the median increase is 0.0017 ms. Initial per-tick closure/status allocations were removed. This is not a measured army regression or a full performance-target pass. GPU-authoritative armies remain unchanged. Full standalone Unity frame time, native/GPU memory, fog, server bandwidth and fleet-scale timing remain unmeasured. Raw runs and aggregates are under `TestResults/Industrial/WaterComparison/`.

## Placeable water reservoir

Construction now includes **Water Reservoir**, a shared prototype tank costing 8 iron bars and 4 plates (free construction in Creative). It holds **10,000 L** and needs no power. Use the F menu's Fill Reservoir action or carried Water row to transfer industrial water into it. A powered pump can instead feed the tank through a water pipe. Run another water pipe from the tank to the sluice: **WATER node → powered pump → reservoir tank → sluice**. The tank passively feeds up to 100 L/s with explicit volume conservation. The sluice still needs its own power connection.

The existing sluice reservoir remains available, so **pump → sluice** and manual filling still work. Water in the industrial inventory comes from G at a WATER marker or Creative Test Supplies; hotbar drinking-water items are a separate survival inventory. New tank meshes retain rebuildable geometry, collision and service sockets.

See [vehicle operation and buffer setup](VEHICLE_OPERATION_AND_BUFFERS.md) for current driving controls, automatic nearest-node collection, reservoir piping and the stockpile–loader–sluice workflow.
