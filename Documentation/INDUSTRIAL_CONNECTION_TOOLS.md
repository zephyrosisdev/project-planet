# Industrial connection tools

The Construction tab now offers three separate selectable tools: **Conveyor Planner**, **Wire Tool**, and **Pipe Tool**. Their implementation shares connection picking utilities; the player selects each tool independently. They are currently menu-equipped tools, rather than new crafted inventory items.

The reference workflows are port selection and a construction preview, with support/routing points before the final connection. See [Satisfactory belts](https://satisfactory.wiki.gg/wiki/Belts) and [Facepunch's Rust Wire Tool](https://wiki.facepunch.com/rust/item/wiretool). This prototype adapts those interactions; it does not implement every Satisfactory build mode.

## Controls

1. Open **I > Construction** and select the desired tool. Machine interaction menus also offer tools starting from that machine.
2. Click a visible output handle. Conveyor outputs are green, inputs blue, and the aimed handle yellow.
3. **Click to add intermediate points** wherever no destination handle is selected. No Shift modifier is required. Shift-click can force a point while aiming at a handle.
4. Walk along the planned route. Its generated preview follows the points. Red feedback reports invalid geometry or another placement failure.
5. Click the final input handle to commit the complete run. Materials are consumed only on successful commit.
6. **Right-click** removes the last point. With no points remaining it clears the selected output. **Escape** exits without spending materials.

The Conveyor Planner uses a nominal **1 m** support height. Mouse wheel adjusts the next support in **0.25 m** steps, from 0.5 m to 8 m. Points use the aimed floor/terrain location, raised by the selected amount. A support preview appears at each node. Belts follow a sampled cubic spline and join their output/input faces. The same canonical curve supplies mesh geometry, arc length and visible item movement.

Maximums: **16 conveyor support points / 100 m**, **8 wire or pipe points / 50 m**. These are explicit per-run data/work budgets. Start another run through an appropriate machine/module interface when needed. Conveyor placement checks slope, terrain, world collision and nearby existing belts. Wires/pipes follow curved spans through clicked anchors and check terrain and world geometry. Points are not individual GameObjects. Existing belts and wires do not require a physics object per item.

Survival belts cost **one plate and one gear per started 5 m** of actual curve length, including their support presentation. The final preview shows the bill. Creative belts are free. Wire and pipe connections retain the current resource-free prototype policy. One machine can have one incoming power connection; water flow still uses the established pump/reservoir quantities.

## Endpoints and module logistics

Normal machines expose input/output handles at the prototype logistics deck height. Newly placed **Workshop** and **Warehouse** modules also expose conveyor input/output ports through their access bays. Existing modules gain these ports when construction is persisted, including when equipping the Conveyor Planner.

Module ports use a compact, authoritative **2,000-item-unit buffer** included with the paid room. An incoming belt feeds that inventory; an outgoing belt exports it. These are real inventories rather than decorative points. Use **F** at a module logistics port to inspect/recover items. Empty the buffer and recover connected belts before removing the room. Module buffers cannot be separately packed to manufacture free stores.

Manufacturing components and ore stacks travel visibly along the curve. Bulk pay dirt remains a bulk-material hauling/pile operation; pipe water is not a belt item. This does not introduce arbitrary belt-to-belt splitters, standalone disconnected poles, reverse-input-first construction, belt walking/collision physics, or ship-frame factories. These need their own topology/vehicle integrations.

Removing a belt from either owned endpoint preserves its original paid components and in-flight items. In Survival, make enough inventory room for both. Route anchors, paid receipts, lane contents and module-buffer identities survive snapshots and journal replay. Cached spline samples and generated meshes are rebuilt, rather than saved as authoritative objects. Legacy straight-belt journal commands retain their original construction rules via an explicit new-command version marker.

## Vehicle bottlenecks diagnosed

The inspected autosave on 2026-11-03 showed the three autonomous vehicles at their collection berths, waiting for destination space. Their destination sluice (#9) contained **2,000 / 2,000 L**, was **disabled**, had **no power source** and **no water**. They were not stuck for lack of fuel: Creative propulsion was enabled and fuel remained aboard.

Haulers now identify their full/reserved destination and its state, including the need to switch a disabled machine on. They retain cargo/reservations correctly and resume when real space becomes available. Power the sluice (5 kW), connect a finite pump supply (pump requires 2 kW) directly or through a reservoir, enable processing, and keep ore/tailings outputs clear. This update does not silently enable machines, consume player supplies, or create free water.

Testing also exposed a stale industrial endpoint cache when one machine was removed and another added without changing the total count. Lookup now checks the cached record's slot and allocated identity, rebuilding only when needed. This concerns compact industrial machines; GPU army units remain GPU-authoritative.

## Validation and performance scope

Core: **208 checks pass**, including curved-belt item/activity conservation, canonical arc lengths, persistence, paid placement, long-belt recovery, full-destination resume, module-buffer item transfer, module removal safety and endpoint replacement. Unity: **62 UI/geometry checks and 164 placement/vehicle checks pass**, with zero C# compiler errors. Tests exercise intermediate wire points and undo, separate tool selection, curved mesh generation, solid-geometry rejection and a route moved around an obstacle by adding nodes.

Standalone comparison: equivalent Release fixture with 1,000 stationary machines, 100 active trucks and 16 observers; 600 warm-up / 3,000 sampled ticks, three alternating runs per build, tiered compilation disabled. Every run retains all 100 jobs and delivers 1,919,220 L. The comparison is industrial CPU simulation, not belt rendering or Unity frame performance.

Unpinned verified runs: median tick **0.8317 -> 0.8369 ms**, p95 **0.9594 -> 1.1380 ms** (+18.62%), throughput **1,283 -> 1,230 ticks/s**. The elevated unpinned p95 is retained and is not reported as a performance pass.

Controlled follow-up: same fixture pinned to logical processor 2, AboveNormal priority, three alternating runs per build. Median tick **0.8506 -> 0.8338 ms**, p95 **1.0887 -> 0.9225 ms**, throughput **1,258 -> 1,345 ticks/s**. Process private memory **46,407,680 -> 47,812,608 bytes**, working set **72,282,112 -> 73,498,624 bytes**, managed **17,821,544 -> 18,132,112 bytes**. Planning 100 short routes **127.04 -> 124.65 ms**; warm save **26.70 -> 28.66 ms**, load **34.07 -> 38.13 ms**, snapshots unchanged at **2,932,907 bytes**. Figures are medians across runs. These differing tail results do not establish a portable speed improvement.

Evidence: TestResults/Industrial/SplineTools/comparison-verified.json and comparison-pinned.json; all intermediate/raw runs remain alongside them. Actual Unity frame times, native/GPU memory, fog p95, server bandwidth, GPU readback, 50,000-item rendering and combined fleet acceptance were not measured. No million-unit population, factory activity or army budget was reduced. Existing GPU army code is unchanged. Production multiplayer still needs the established server-side terrain/collision adapter; these offline tools are not a completed network deployment.

After updating, restart Play mode once, then equip the tools from Construction. Existing vehicle jobs can be replanned explicitly; a full processing chain still needs power, water and available storage.


## Pipe socket self-collision correction

New Wire/Pipe tool connections include two automatic clearance anchors. They leave the source socket vertically, cross at least 0.75 m above the higher endpoint socket's radial height, then enter the destination vertically. This prevents a descending pipe from cutting through a reservoir's own housing. Pending source previews also show their exit segment. Direct context-menu connections use the same construction path.

The eight user-selected routing points remain available; the serialized connection budget is ten anchors including the two automatic exits. These are ordinary persisted route points, so old saved routes and journals retain their original geometry/length validation. No collider is globally ignored. When a shared industry collision mesh blocks a route, feedback identifies nearby machinery where possible instead of exposing the internal chunk object name.

Validation after the correction: 210 industrial core checks and 66 Unity UI/geometry checks pass; zero C# compiler errors. The Unity regression uses the actual reservoir and sluice collision meshes merged into one MeshCollider. It reproduces the old descending-pipe collision, accepts the socket-clearance route, rejects another obstruction added to that same collider, and accepts a manual point routing above it. This is a connection-placement correction; factory and GPU army tick logic are unchanged. New full Unity/GPU performance measurements were not taken for this fix.

Restart Play mode after recompilation, then start a fresh Pipe Tool route. Existing connections are preserved; they are not silently rerouted.
