# Ground vehicle prototypes

The prototype framework uses GPU army records for distant movement, pathfinding,
health, targeting and damage. A bounded observer pool supplies nearby articulated
models. Only a player in a movement seat takes local movement authority; leaving
that seat restores GPU movement without replacing the army entity. There is no
per-army-unit GameObject or CPU physics fleet.

## Trying the prototypes

Start a fresh Play session after compilation. Press **F8** in the planetary
development game to deploy a biped mech and a wheeled utility vehicle behind the capital ship. These are
development prototypes, not extra free units granted on every normal game start.
Their combat stats inherit existing vehicle archetypes; their separate GPU motion
profiles use the prototype's dimensions, maximum speed and turning rate.

| Prototype | Locomotion | Seats |
| --- | --- | --- |
| SF-MW1 | Heavy biped | Driver, AI gunner, commander |
| SF-LUV1 | Four wheels with suspension and steering | Driver and 3 passengers |

Approach an entry point in first person and press **F**. **W/S** drive, **A/D**
steer, **Space** brakes, the camera stays in the first-person interior; hold **Left Alt** to free look, then release it for a fast smooth return, **1–4** changes seats,
**F** exits at a clear supported location, and **Tab** returns to RTS. Gunner seats
use mouse aim and left-click fire at an acquired enemy under the reticle. Built-in
AI gunners yield their seat and resume after the player leaves. Crew role
permissions are independent: a driver does not acquire a separate gunner's weapon.
RTS selection and orders continue using the existing GPU navigation and combat.

## Detail budget

Select the `PlanetDevelopment` component and find **Ground vehicle detail →
Detailed Vehicle Rig Limit**. The live-editable range is **1–32**, default **4**.
The controlled vehicle has priority. Remaining slots go to nearby vehicles, with
distance hysteresis to avoid exchanging equally close rigs every frame. Vehicles
outside the budget retain their GPU records and draw a baked GPU-positioned
fallback. Up to 32 explicit prototype registrations are supported per resident
planet; this is separate from the million-unit army capacity.

Each profile has separate full, reduced and simplified articulation distances.
Terrain probes reduce with distance, then stop when only the GPU proxy is used.
The vehicle shader variant is enabled only when the prototype pool is created;
the normal army path compiles out the additional vehicle branches. Prototype GPU
archetype storage adds a fixed 4 KiB per resident world. Higher rig limits increase
local IK/contact and draw costs; no 60 FPS or standalone regression pass is claimed
without equivalent measurements.

## Parking and transport

Open both capital-ship boarding doors and drive up the 12 m wide rear ramp.
The rear cargo bay is now 15 m wide, 28 m long and 10 m high, with a 12 m wide,
9 m high entrance. Both the utility vehicle and mech fit.

Vehicles attach to supported floors throughout the ship wherever they fit, including rooms outside the rear bay. No locking key is needed. The carrier frame stays attached while driving inside; leaving its supporting surface releases it. Door state and a named bay are not parking requirements.
An analytic deck/ramp support supplements collider contacts to prevent losing
support during origin shifts. Cargo, including planted mech feet, follows ship
translation and rotation and transfers with it between planets.

`IVehicleCarrier`, `VehicleCarrierBay` and `VehicleParking` support future carrier
types, bay dimensions, orientation/fit checks and nesting-cycle rejection. This
does not add multiplayer or remote player occupancy.

## Models and profiles

Assets are under `Assets/MassiveArmy/Resources/MassiveArmy/Vehicles`:
the active mech and utility rig prefabs, their `VehicleDefinition` assets, shared
material, articulated parts (including separate wheel meshes), rigid flat-part
LODs and bounding-shell LODs. Definitions expose arbitrary leg arrays, gait phase
groups, joint limits, stride, probes, steering/suspension, vehicle dimensions and
seat positions/permissions. Replace meshes on the rig references to experiment
with other original models. Bones carry no gameplay scripts.

**Synthetic Front → Vehicles → Generate missing prototype models** generates
missing assets and refreshes generated rest poses without replacing definitions.
**Validate prototype assets and contact math** runs editor asset, IK, contact and
GPU ownership checks without entering Play mode or taking over the camera.
Results go to `TestResults/Vehicles/validation.json`.

The terrain contact sampler uses the same analytic planet height field as the
first-person observer and supplements it with local building/ramp/ship collider
queries. Vehicle movement probes the wheel/foot footprint, sweeps the chassis,
limits slope/step traversal, and applies an independent analytic terrain floor.
It does not wait for streamed terrain collider meshes.

Runtime acceptance still needs the player's terrain/ramp/ship gameplay checks and
equivalent standalone median/p95 frame time, simulation rate and memory captures.
This prototype does not add production-building menus, networked seats, limb-hit
combat, or a general CPU vehicle army.

Other authored carriers can use `VehicleCarrierVolume`: supply interior room bounds and colliders. Its floor queries and room-floor fallback provide support through collider streaming and origin shifts; split room volumes around real floor openings.

Detached registered vehicles retain their current carrier-relative exit pose and inherit the carrier's linear and angular velocity. They use continuous ballistic integration with celestial gravity while airborne, including when unoccupied; ground braking resumes on landing. This is bounded to the existing 32 explicit registrations and does not add CPU army simulation. Distant proxies retain their actual pose and altitude. Standalone performance and live cargo-exit gameplay still require verification.

Jump boosters: hold Left Shift in the driver's seat to fire an upward burst (maximum 0.65 seconds). Release before firing another burst. Fuel lasts 1.5 seconds total, recharges over 5 seconds after a 1.25-second release delay, and is shown on the vehicle HUD. These values and acceleration are editable on each vehicle definition. Space remains the brake. R searches nearby collision-free chassis positions for recovery; automatic penetration correction and planted-foot reset also prevent common contact deadlocks. Ground support no longer snaps down across a hill; gravity and retained climbing velocity determine airtime.

RTS view shows cyan car/biped icons for owned registered vehicles once their projected model size drops below 32 UI pixels. Markers include identity, distance, cargo status and offscreen/horizon bearings; overlapping markers separate with leader lines. The bounded retained overlay updates at 10 Hz and uses existing registration state without additional army readbacks.
