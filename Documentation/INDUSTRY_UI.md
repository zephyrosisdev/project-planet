# Crafting and placed-object interaction

The industry IMGUI console has been replaced with UI Toolkit, using the same retained panel host as the personal inventory. The project already includes Unity's UI Elements module; no additional third-party UI package is required.

## Controls

- **I → Crafting** or **F4** opens the crafting browser. Category sidebar, searchable recipe grid and selected-recipe details remain visible together. Ingredient counts show what you have versus what is required. The optional ingredients filter narrows the grid without hiding progression by default.
- **Quantity / + / − / Max** controls field-craft batches (up to 20 per submission). The bottom queue displays up to 32 reserved jobs, current progress and remaining time. Click a queued job's **×** to cancel that exact job and recover its ingredients if there is space. Jobs and reservations survive save/reload.
- Advanced recipes show the station they require. **Aim at a placed machine and press F** to open that machine's interaction panel. Physical obstructions and range limit interaction. Access its inventories, recover input items, choose compatible production recipes, toggle operation/shielding, manage water or payloads, or pack empty machinery.
- For visible cables/pipes, choose **Construction → Run power cable / Run water pipe**, or start at a source machine through its **F** menu. **LMB** selects sockets, **Shift + LMB** adds routing points, **RMB** undoes and **Esc** exits. Routes are limited to 50 m/eight points, survive save/reload and show obstruction feedback. Source/destination context buttons remain available, including conveyors with editable filters. See [connection controls and limits](PLACEMENT_AND_POWER.md#visible-connections).
- **I → Construction** lists machines and colony modules with direct resource costs. Having the resources enables placement. Room placement equips/selects its blueprint; machine placement uses the existing ghost and socket validation. **R** rotates; **LMB** places; **Escape** cancels. Foundations use stone; panels, doors and stairs use metal. No construction kit is required.
- **I → Inventory** includes industrial materials and radiation protection/treatment alongside personal items. **Escape** or **Close** dismisses a menu and returns mouse control to gameplay.
- Piles and vehicles have their own context actions. While driving, **F4** opens that vehicle’s payload/bucket controls. **G** resource collection remains available while menus are closed. Industry HUD messages, radiation, deposit markers and placement prompts now use UI Toolkit as well.

Machines keep their existing paid production chain: ingredients go into machine buffers and outputs arrive in their inventories or on belts. Showing an advanced recipe in the crafting browser does not grant free crafting or bypass its machine.

## Creative testing and resource construction

Use **CREATIVE MODE** in the menu top bar to bypass building and hand-crafting costs. The host controls this persisted world setting. Test-supply controls in Construction and Inventory add real materials or pay dirt without gathering. Processing machines still use their actual inventories, power and water; creative mode does not change production conservation.

Placement stores the exact resource payment. Dismantling refunds what was paid, with capacity and contents checks. Free builds and cancelled free crafts cannot produce ingredient refunds when creative is disabled. Legacy construction kit definitions remain readable for older saves/journals, are hidden from new crafting, and can be recycled from inventory. Legacy conveyor parts require a complete original batch for integer-material recovery.

## Reference research

Rust's category/recipe/detail workflow informed the layout, without importing its artwork or copying its progression economy. Facepunch documents workbench/blueprint progression at [Blueprint](https://wiki.facepunch.com/rust/item/blueprintbase), queue interaction work in [crafting improvements](https://commits.facepunch.com/AdamWoolridge/rust_reboot/main/crafting_improvements), and consistent item/crafting modals in [Harder Core](https://rust.facepunch.com/news/harder-core). Our categories fit the space-industrial recipes and room kits already in this project.

## Validation

`Tools/IndustrialChecks` covers authoritative transfers, queue capacity, batch reservations, stable-ID cancellation and save recovery. The Unity diagnostic **Synthetic Front / Diagnostics / Validate industrial UI Toolkit menus** (request action `industry-ui`) exercises real retained controls with an isolated inventory, including recipe search, quantity submission, cancellation, input focus preservation, machine deposit/recovery, station compatibility, line-of-sight interaction and menu closure. It writes a report and preview to `TestResults/Industrial/UiToolkit`.

This UI work does not change GPU army authority, fleet storage, fog, or the million-unit path. No whole-game frame-rate or fleet-scale performance acceptance is claimed by UI/control tests.

See [large-section placement and starter power](PLACEMENT_AND_POWER.md) for fuel collection, solar/wind/battery wiring and the latest placement changes.

Truck context menus now provide source/destination dropdowns and Start / Replan Collection, Stop Collection controls. Construction and pump contexts provide Locate Nearest Water Intake, with HUD distance/turn guidance. See [autonomous collection controls and limits](AUTONOMOUS_TRUCK_COLLECTION.md).


Connection placement now has separate Conveyor Planner, Wire Tool and Pipe Tool buttons in Construction, with click-to-add intermediate points and right-click undo. See [Industrial connection tools](INDUSTRIAL_CONNECTION_TOOLS.md) for controls, component costs, module buffers, validation and performance scope.
