# Colony construction prototypes

The supplied reference guides this first Blender kit: light armour panels, orange frames, dark machinery and cyan connection/status lighting. These are deliberately simple playable prototypes, with metre-scale interiors and collision rather than final production artwork.

## Playing

1. Open **I → Construction** and select a machine or colony module. Each card lists its resource cost. Primitive stations use stone to bootstrap production; larger machinery and rooms use metals, plates, wiring, gears and manufactured components. Construction kits are no longer required.
2. Aim at terrain, a floor, or a compatible workshop/storage mount. **R** rotates; **LMB** places; **Escape** cancels. The ghost shows validity. Resources are spent only after successful placement.
3. Aim at a placed machine and press **F** for inventories, production recipes and connection controls. Generate power, supply water, wash pay dirt, smelt ore and fabricate components. **I → Crafting** (or **F4**) keeps the recipe browser and component crafting queue.
4. Colony modules use the building blueprint, selected automatically from Construction. Alternatively equip it, hold **RMB**, scroll categories, then release to select. Doorway ports connect rooms; workshop and warehouse sockets mount machines. **Z** undoes the last module and **F** operates built doors.
5. Enable **CREATIVE MODE** in the crafting/construction menu's top bar for free placement and hand crafting. Creative test supplies let you add resources or pay dirt without gathering. Machines continue processing actual buffers, so production and blockage tests remain meaningful. The setting persists with the world and only the host can change it.
6. Dismantling empty disconnected machinery and undoing rooms return their recorded paid materials. Free creative builds return nothing, including after creative is disabled. Existing kit-built rooms/machines recover compatible resource materials. Legacy items in inventory can be recycled through **I → Inventory**; no stored items are silently deleted.

The personal food/tool inventory and industrial materials remain separate data adapters, presented together in Inventory. See [crafting and interaction UI](INDUSTRY_UI.md) for controls.

## Assets and integration

- Editable source: `ArtSource/MartianColony/ColonyPrototypes.blend`.
- Reproducible authoring/export script: `ArtSource/MartianColony/build_prototypes.py`; run in Blender's Python environment. It creates a separate scene and preserves existing scenes.
- 38 FBXs: 11 rooms, 12 machines, 15 original structural pieces. FBX socket empties retain canonical names. Model importer hierarchy is preserved.
- A versioned Resources catalogue exports the same mesh vertices, normals, vertex colours, simplified collision prisms, sockets and reduced-detail geometry. Unity consumes this catalogue directly; Blender is not a runtime dependency.
- Rooms have furnished interiors and open 1.5 m entrance apertures. Furniture participates in collision. Workshops/warehouses deliberately leave their bays empty for player machinery. Medical, research, command, greenhouse and life-support furnishings are visual prototypes; they do not add new treatment, research, food or atmosphere simulation. Window apertures and airlocks are open prototypes, without sealing/pressure simulation.
- Buildings remain batched into spatial render/collision chunks. Repeated machines use GPU-instanced batches with cached meshes and simpler geometry at distance; nearby machine collision is combined into a single contact mesh. Belt items remain GPU-instanced presentation without per-item GameObjects, rigidbodies or NetworkObjects. Army movement/storage and the million-unit path are unchanged.
- Component crafting, inventory reservations, machine attachments, resource payment/refunds and mount registries are persisted. Older journals replay their original pricing rules. Creative mode and exact resource receipts are saved. Runtime state remains independent of visual objects.

## Validation and limits

Run `dotnet run --project Tools/IndustrialChecks/IndustrialChecks.csproj` for industrial transaction/recovery/concurrency checks. The localhost test needs local socket permission. In Unity use **Synthetic Front / Diagnostics / Validate colony prototypes**, or write `colony-prototypes` to `TestResults/Planetary/request.txt`. It checks imported FBXs/socket names, mesh winding, walkable floors, clear entrance aisles, room joins and matching machine footprints, then draws three shared-mesh instances into an isolated GPU render target. The image and report are written to `TestResults/Industrial/ColonyPrototypes`.

Prototype validation is not the required standalone performance comparison. The RTX 3070 1440p 60 FPS target, <=3 ms p95 fog budget, million-unit path and <=5% army regression gate remain unchanged and have not been established by these art/transaction checks. There is no claim that production multiplayer, room atmosphere or the complete fleet plan is implemented.

See [crafting and interaction UI](INDUSTRY_UI.md) for the current menu layout and controls.

## Runtime equipment prototypes and cache recovery

Solar panels, wind turbines, batteries, dump trucks, loaders and excavators have shared procedural prototype models in addition to the 38 Blender-exported assets. All 18 machine/vehicle entries now resolve to render geometry, distant geometry and collision prisms, rather than vehicle fallback boxes. Equipment keeps its existing simulation footprint and reservation radius; vehicle booms/buckets use the larger reserved vehicle clearance.

Generated meshes retain serializable vertex, normal, colour and triangle data. Destroying Unity's temporary mesh cache (including between Play sessions with retained static state) rebuilds from that data instead of throwing in the placement preview. Renewable near/distant definitions share their backing arrays. Vehicle models are batched with the same prototype renderer as stationary equipment; decorative vehicle parts do not create individual GameObjects or colliders.

Ground equipment previews fit the sampled footprint above terrain, with support clearance. `IndustrialPlacementValidation` covers all 18 equipment definitions: render/collision presence, footprint metadata, destroyed-cache recovery, distant geometry, actual first-person ground preview, authoritative placement and recovery. The combined placement/surface-hauling fixture has 121 checks. This correctness fixture is not a standalone GPU/frame-time performance measurement; the army/fog acceptance targets remain unmeasured for this asset correction.
