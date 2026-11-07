# Modular expedition ship prototype

The quad (SF-QB1) and jeep (SF-LUV1) return as explicit, bounded personal test vehicles in Survival. They reuse the existing motors, seats, detailed rigs, collision sweeps, carrier attachment and cargo transfer. Survival does not allocate an RTS army or navigation field to create these two vehicles.

## Try it

- Start a fresh Play session to load the new Blender catalogue and ship shell. The two vehicles deploy behind the starting ship. If a session is already running, land and press **F8** to deploy missing test vehicles. Repeated deployment does not duplicate a living quad/jeep. Deployment is a testing shortcut, with no fuel/resource cost.
- Aim at a vehicle and press **F** to enter directly. Hold **E** for 0.45 seconds to inspect its inventory and controls. WASD drives; F exits. Open **both rear ship doors** using their illuminated side buttons before driving up the ramp. Existing automatic cargo support and moving-carrier inheritance remain in use.
- On foot, press **F6** for the UI Toolkit ship assembly panel. Select a section, aim at an open cyan socket and click to place. Red preview feedback reports missing components, incompatible/occupied sockets, overlaps or reach problems. Esc cancels. Switch to another equipped construction item to cancel ship placement.
- Construction consumes components from the real player inventory: hull panels, frames, electronics, engines, motors, reactor parts and wire. These use the existing industrial recipes. No construction kits are introduced. Enable Creative in the existing F4 crafting UI for free ship construction.
- Choose **WIRE POWER** in F6, or equip the existing Wire Tool while inside the ship. Click a core output, click intermediate routing points, then a system input. Shift-click forces a routing point; RMB undoes; Esc cancels. A run supports eight intermediate points and 150 m. Wire costs one wire per 5 m, rounded up, and is recovered when disconnected. Creative wires have no paid-resource refund.
- The assembly panel reports powered/unpowered systems and supports cable disconnection and dismantling. Move close to the source core to edit a power circuit. Dismantling requires removing cables and structural children first; occupied rooms, vehicles and sleeping points prevent removal.

## What is implemented

Seven authored module types: hangar, corridor, cockpit, engineering room, power core, main engine and maneuvering thruster. Blender mesh data, collision prisms and named socket transforms export together. Both asset and runtime sockets are checked for agreement.

A starter assembly contains ten module instances. Section attachments and cable routes persist in the industrial snapshot and idempotent transaction journal. The starter has preconnected circuits; additional powered modules start unwired. Each consumer requires a direct core connection; a core provides 2,000 abstract power units with deterministic allocation when overloaded.

Cockpit camera, seat interaction and exit positions follow the cockpit section. Main-engine count, power and aggregate mass affect translational acceleration; thruster power affects turning. Cockpit power gates piloting. Gravity continues without power. This remains one aggregate movement body, with batched section visuals and contacts rebuilt on assembly changes; armor panels have no individual objects or rigidbodies.

The starting hangar and rear ramp remain the root of this prototype. You can extend/reconfigure that vessel through its sockets; this does **not** yet provide multiple independently launched player-built ships or a full shipyard/blueprint production queue. The core supplies abstract prototype power, without reactor fuel/cooling simulation. Detailed module damage, dynamic centre of mass/inertia, full compartment pressure graphs and fleet networking remain future work. The existing physical flight model and first-person ship interfaces are retained.

Two cabin bounds are retained for atmospheric exclusion, with occupied extension rooms supplying their own bounds; no shader loops or extra atmosphere samples are added.

## Art and reproduction

- `ArtSource/ModularShip/expedition-utility-concept.png`: generated design reference, using the built-in image-generation tool. Prompt is in `concept-prompt.txt`.
- `ArtSource/ModularShip/ExpeditionModularShip.blend`: editable Blender scene with modular mesh objects and named socket empties.
- `ArtSource/ModularShip/build_modular_ship.py`: non-destructive source generator, creating a new scene and exporting the catalogue.
- `ArtSource/ModularShip/render_prototype.py`: assembled preview setup, run after the generator.
- `Assets/MassiveArmy/Resources/MassiveArmy/ShipArt/ModularShipCatalogue.json`: runtime mesh/collision/socket catalogue.

The Blender model is a geometric gameplay prototype based on the concept, not a finished production art match.

## Validation and limits

- Full runtime C# compile passes with existing project warnings.
- `dotnet run --project Tools/IndustrialChecks/ShipbuildingChecks.csproj`: 25 checks covering atomic costs, replay rejection, structural constraints, power, refunds, Creative behavior and crash recovery.
- `python Tools/verify_modular_ship_assets.py`: seven mesh/socket checks and conservative quad/jeep boarding envelope checks, sampling 73 positions each.
- Existing industrial regression suite: **280 checks pass**, including its localhost command test (run with local socket access after the restricted run was blocked).
- Reports: `TestResults/Planetary/ModularShip/`.

Live driving, moving-carrier loading, UI behavior and complete standalone performance have not been verified in this change. The asset envelope checks do not establish that Unity vehicle contacts pass. No 60 FPS, 5% army-regression or 3 ms fog acceptance claim is made. No target populations are reduced and no CPU army mirror is introduced.
