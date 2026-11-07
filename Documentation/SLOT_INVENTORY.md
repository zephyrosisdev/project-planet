# Shared survival inventory

Survival uses one authoritative inventory for personal equipment, resources and crafted placeables. **I** opens 30 backpack slots; the ten numbered hotbar slots hold actual stacks. Drag with LMB to move, merge or swap a stack. Right-drag moves half. Release over the ground area or outside the inventory window to drop recoverable supplies; look at them and press **F** to open them. A rejected transfer keeps its source contents.

Craft machines, vehicles, room modules and sleeping points in **Crafting**. Creative mode waives recipe ingredients. Drag the resulting item onto the hotbar and equip its number key to enter placement. Successful placement consumes one item in either mode. The preview stays active only while that selected stack has items remaining. Another copy elsewhere does not automatically equip. The live Survival UI has no separate Construction tab or Place Bag shortcuts.

Machines, module logistics stores and vehicle cargo use the same drag slots. Machine outputs allow withdrawal only. Pay dirt has a separate bulk tile so volume and composition move together; clean water can be dragged onto a sluice/reservoir's water tile. Machine operations, automation and recipe selection remain beside storage. **F** opens a parked vehicle's cargo and Drive control; **F4** opens cargo while seated. Equip a radiation suit or decontamination kit and use LMB to activate it.

Building modules stack to 20; individual machines, vehicles, tools and sleeping deployables occupy one slot each. Common materials stack to 1,000. Containers retain their existing quantity capacity and show additional stacks through pages of at most 60 slots. Oversized older saves retain visible overflow, which can be moved out; items are never silently truncated. Ground loot has an explicit 256-pile budget and refuses further drops when full.

Slot assignments, stack activity, drops and equipment changes use the industry snapshot/journal. Crafting reserves future output slots as well as quantity capacity. Ownership, reach, output-only rules and stale source item checks run before committing transfers. Ship-mounted drops retain a carrier-relative pose. Dropped radioactive material remains hazardous. These are authoritative local-adapter transactions; this does not claim a completed remote multiplayer inventory protocol.

## Validation and performance

The core suite passes 280 checks, including shared stacks, exact selected-slot depletion, atomic transfers, crafting reservations, overflow migration, radiation and journal replay. The Unity shared-slot fixture passes 18 checks using actual UI Toolkit pointer events and retained construction placement. Existing respawn (30), crafting UI (71) and placement (164) checks also pass. Results are under `TestResults/Survival/Inventory`.

Three alternating standalone Release runs compared the frozen previous core with slot-enabled current code using the same saved route fixture: 1,000 stationary machines, 100 active trucks and 16 observers, 600 warmup ticks and 3,000 measured ticks. Every run delivered 1,919,220 L and retained all 100 jobs. Values below are the median of three runs:

| Metric | Previous | Slot-enabled |
|---|---:|---:|
| Median simulation tick | 0.9006 ms | 0.8956 ms |
| p95 simulation tick | 1.0751 ms | 1.1176 ms |
| Unpaced ticks/second | 1,229 | 1,216 |
| Process private bytes | 46,452,736 | 63,148,032 |
| Working set bytes | 73,183,232 | 89,546,752 |
| Managed bytes | 18,670,272 | 25,601,960 |
| Snapshot bytes | 2,989,928 | 7,674,143 |
| Save | 29.68 ms | 59.56 ms |
| Load | 38.67 ms | 96.58 ms |

The p95 tick change is +3.95%. The slot representation increases memory and serialization cost, particularly for large stock inventories. These are CPU industrial tick measurements, not rendered frame times. Unity native/GPU memory, fog cost, fleet simulation and the 1440p/RTX 3070 target were not measured here. Army authority and unit representation are unchanged. Raw runs and frozen baseline are under `TestResults/Industrial/InventorySlots`.

## Custom planetary daylight

**Synthetic Front → Planets → Use natural daylight atmosphere** sets the active planet (or the default planet when no world is active) to our physical atmosphere renderer: Rayleigh strength 1, aerosol strength 1, haze 0.45, white cloud colour and neutral white primary sunlight/star disc. The scattering model supplies sky and horizon colour. The blue atmosphere tint is only for legacy haze mode. The preset is optional and does not modify settings until selected. Atmosphere rendering performance and visual acceptance remain separate from these inventory checks.
