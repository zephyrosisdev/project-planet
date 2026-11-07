# Vehicle operation, water storage and pay-dirt buffers

## Driving

Press **F** on a truck, loader or excavator to enter directly. Hold **E** for 0.45 seconds to open its inventory and detailed controls. Survival requires **Fuel** in Machine Input; Creative manual driving requires none. Creative mode also offers optional test fuel. Use **W/S** for throttle/reverse, **A/D** for steering, **Space** to brake, **Alt** for free look, **F** to exit and hold **E** for payload controls. Held weapons are hidden and cannot fire while seated.

The industrial profile previously retained the default walker locomotion while supplying wheels, and repeatedly activated/deactivated the on-foot controller, releasing the mouse. It now uses wheeled support, a seated observer camera and captured-cursor handling. Authority records follow elapsed simulation time rather than wall-clock sampling. The truck, loader and excavator are each validated moving on real planetary contacts and returning to on-foot control. Entry matches the interaction system's range and rejects another owner's vehicle with feedback.

## Simple auto operation

Open the vehicle's **F** menu and leave **Collect from** on **AUTO · closest pay-dirt node**. Leave **Deliver to** on AUTO to use the nearest sluice, falling back to a stockpile/store, or choose a destination. Press **Auto operation / Replan**. The menu closes and the HUD tracks the vehicle. Survival requires fuel; newly assigned Creative jobs use free propulsion.

Automatic mode tries the three closest eligible nodes per planning request. Empty, submerged and construction-undermining sources are excluded; unreachable nearby nodes can be skipped. After a source depletes or becomes unsafe, the vehicle finishes its cargo and selects another node. The automatic-mode flag, source changes, cargo, fuel, route progress and reservations survive snapshots and journal replay. Explicit sources remain supported, including stockpiles that are initially empty.

Routes are outdoor jobs for up to 100 industrial vehicles. The 300 m site restriction and old 1 km / 256-point route caps are removed. Sites must share the vehicle reference frame. Long routes plan across frames in roughly 120 m surface sections, with bounded local searches and an explicit 65,536-point route-data budget. Use Auto operation / Replan to replace an existing saved route. Moving-carrier/interior logistics and animated bucket handoffs remain future work. Retained GPU army simulation/navigation is unchanged.

## Water reservoirs

Choose **Construction → Water Reservoir**. It costs **8 iron bars and 4 plates**, or is free to build in Creative. The tank holds **10,000 L** and requires no power.

Connect **WATER deposit → powered pump → tank → sluice**. Use Locate Nearest Water Intake to find the resource marker. Place the pump within 8 m and select its intake. The pump needs 2 kW. Run a pipe into the tank and a second pipe from the tank to the sluice. The tank passively feeds up to 100 L/s; the sluice needs its own power.

Alternatively fill the tank from carried industrial Water in its F menu. Its Water row and Fill Reservoir button both fill the fluid buffer rather than an unused item input. The progress bar shows litres/capacity. The sluice's existing built-in reservoir remains usable, so pump → sluice also works. Industrial Water is collected with G at the WATER marker or through Creative Test Supplies; hotbar drinking water is separate.

## A pile beside the sluice

Choose **Construction → Place Pay-Dirt Stockpile**, aim at flat clear ground and click the footprint preview. The 16 × 16 m bay requires roughly 11 m or more centre clearance from the sluice; leave room for vehicles.

Set a truck's **Deliver to** to the pile. Set a loader's **Collect from** to that pile and **Deliver to** to the sluice, then start Auto Operation. The loader can be assigned before the first delivery; it waits for stock. Trucks grow the conserved dirt pile, and the loader shrinks it as it feeds the sluice. Vehicle payload appears in the truck bed or loader/excavator bucket. Loading remains an abstract timed operation, not a finished articulated digging animation.

## Checks and measurements

Final correctness validation: **172 core checks, 53 UI/geometry checks and 137 placement/surface/driving checks**. Unity script compilation succeeds. Coverage includes all 19 equipment models, actual manual movement/exit for all three industrial vehicles, nearest-node selection and handoff, empty-pile setup, loader-to-sluice feeding, pump–tank–sluice conservation and crash recovery.

The benchmark projects now use isolated output/intermediate directories so their apphosts cannot overwrite one another. The following are standalone .NET CPU measurements, not Unity frame-time or army acceptance results. Factory-only comparison uses identical 1,000-machine / 16-observer scenarios, 6,000 warm-up / 30,000 sampled ticks, medians of three alternating runs with tiered compilation disabled. The frozen baseline predates the autonomous collection changes.

| Factory-only measurement | Baseline | Current |
| --- | ---: | ---: |
| Median CPU tick | 0.0546 ms | 0.0555 ms |
| p95 CPU tick | 0.0639 ms | 0.0646 ms |
| Simulation ticks/s | 17,764 | 17,527 |
| Process private bytes | 13,320,192 | 13,492,224 |
| Working-set bytes | 32,010,240 | 32,628,736 |
| Managed bytes | 1,266,064 | 1,275,960 |

The measured factory-only p95 change is **1.1%**. A separate 1,000-stationary-machine / **100-active-truck** / 16-observer fixture delivered **1,919,220 L**, retaining every active job and all factory activity. It measured **0.9014 ms median / 2.1352 ms p95** CPU ticks and **979 ticks/s**. Planning all 100 initial routes took 114.5 ms total. Process private / working-set / managed bytes were 50,200,576 / 76,640,256 / 17,761,816.

That active fixture's XML snapshot was 2,925,307 bytes. Warm snapshot/restore took 52.2/97.4 ms; cold initialization plus snapshot/restore took 137.7/73.7 ms. Those synchronous costs are not a 60 FPS save-path pass. Raw results: `TestResults/Industrial/OperationComparison/`.

Full standalone Unity median/p95 frame times, managed/native/GPU memory, vehicle contact rebuild cost, server bandwidth, fog and GPU readback remain unmeasured. These CPU fixtures do not claim the combined industrial/fleet acceptance target or replace the preserved million-unit army path.

## Creative empty-tank driving correction

Creative manual driving now bypasses the fuel gate. Drive Vehicle starts an intact switched-off engine automatically, and the cab HUD reports focus, menu, brake, damage, fuel or physical obstruction blocks. Survival retains real fuel consumption. Newly assigned Creative auto-operation jobs record a persisted free-propulsion policy; turning Creative off restores their fuel requirement. Processing supplies, machine power and water remain real inventories.

The regression fixture now uses a switched-off truck with no vehicle fuel, reads W through a temporary Unity keyboard device, then verifies actual motion and captured controls. Earlier driving fixtures supplied fuel and missed the Creative empty-tank case. Test keyboards are restored after the fixture; no OS key events are generated. Editor/development builds log driving-state transitions with `[INDUSTRIAL DRIVE]` for diagnosing remaining runtime blocks.


Resource sites now use varied seeded placement and at least 80 m separation. Existing tightly clustered test deposits migrate once on planet entry without refilling stock. Restart Play mode after updating, then use Auto operation / Replan. Navigation now uses rotated rectangular building footprints and bounded wider detour searches; clear routes beside long buildings are no longer rejected solely by their enclosing circles.
