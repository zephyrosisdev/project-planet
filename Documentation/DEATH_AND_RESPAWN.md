# Death, sleeping bags and sleeping pods

In Survival, zero health opens a UI Toolkit death overlay. Movement, weapons, construction and interaction stop; the cursor is released. Industrial production and the server's simulation clock continue. The overlay offers surface respawn and every owned sleeping point in a horizontal list. The mouse wheel scrolls that list, and its scrollbar also supports dragging. Cooling points display their remaining time and cannot be selected.

Open **Crafting → Survival**, or search for **sleeping**:

| Deployable | Current prototype ingredients | Craft time |
|---|---|---:|
| Sleeping bag | 12 stone, 2 iron bars | 5 seconds |
| Sleeping pod | 8 plates, 4 wire, 2 electronics | 12 seconds |

Both recipes use the current resource economy and can be crafted by hand. The bag's starter ingredients are placeholders until a fabric/textile chain exists. Creative crafting requires no resources; placement still consumes the crafted item.

Drag a crafted bag or pod into the hotbar and equip its numbered slot. Leave vehicle seats before placing. Aim at a fairly flat floor within 10 m, use **R** to rotate, **LMB** to place, and **Esc** to cancel. Placement consumes one crafted deployable in either mode. The selected slot clears when exhausted. Both have distinct procedural prototype meshes and visible placement previews. Ground placement supports natural terrain and construction floors; ship placement uses the actual hull floor and stores the point in the player's carrier frame. Ship points follow translation and rotation, including the existing interplanetary carrier handoff.

Press **F** while looking at your placed point to name it or pick it up. Names are limited to 48 characters. The current explicit limit is 128 placed points per player. Placement reports the limit rather than replacing earlier points.

Each point starts a **120-second cooldown on placement**, and using it restarts **only that point's** cooldown. Picking up and replacing a point starts a fresh cooldown. The cooldown uses authoritative simulation ticks; pausing the server or stopping it freezes elapsed time. Surface respawn has no cooldown. Point records, names, ownership, canonical poses and cooldowns are stored in the existing industry snapshot and transaction journal.

Respawn tests four exits around a bag or pod. It prepares nearby construction collision first, checks floor support and capsule clearance, and refuses obstructed exits without consuming the point's cooldown. A failed attempt leaves the player dead with a visible explanation. Respawning restores health, hunger, thirst and suit oxygen and clears the actor's accumulated radiation dose. Existing carried inventories are retained.

Ground points on another system body prepare that destination before switching the viewed world. A ship respawn resolves the current carrier host instead of an old planetary coordinate. These paths reuse the present single-player-carrier and resident-world architecture; arbitrary modular-fleet carrier ownership/network handoff is still outside that architecture. The current offline player adapter owns actor 1; this does not claim completed multiplayer respawn transport. New respawn transactions are deliberately absent from the raw loopback command gateway and require authoritative placement/exit validation.

Rendering uses two shared meshes, instanced draws and a pool of the nearest 16 contact colliders within 24 m. It does not create an object for every respawn record or add CPU army state. Cooldowns are derived from the existing tick counter without a per-point simulation tick. The overlay refreshes at 10 Hz while dead and rebuilds cards only when the point list or names change.

Validation uses `Tools/IndustrialChecks` for crafting, payments, cooldown independence, ownership, removal and save/journal recovery. **Synthetic Front → Diagnostics → Validate death screen and respawn points** checks the real Unity controller, obstruction rejection, moving ship-relative poses, placement on a rotated ship's actual floor, crafting UI, a long horizontal list, card selection and mouse-wheel events. Results and the rendered screenshot are written under `TestResults/Survival/Respawn`. The open editor also accepts a request file at `TestResults/Survival/Respawn/request.txt`.

This UI and event-driven player change has not been measured as an equivalent standalone army benchmark. No 60 FPS, fog-cost, combined-world, or million-unit performance pass is claimed. Army movement, authority, unit representation and stress populations are unchanged.

See `SLOT_INVENTORY.md` for the shared inventory workflow and current checks. The respawn fixture includes placement and pickup on a rotated ship floor, collision following carrier movement, the rendered death heading, and actual UI Toolkit wheel events.
