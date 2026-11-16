# Two-player playtest

Run one dedicated server and one client per person. The server can run on either
player's PC alongside their client. Both PCs need the same protocol-13 build.

The shared-world/crafting update uses content `survival-5`; update both clients and the server.
They load existing server saves (versions 1 and 2) and save version 3 to preserve
in-flight recall jobs. Back up your server save before first starting this build;
older servers refuse the upgraded save.

**N** can engage orbit hold at **30 km** on the home planet. Terrain clearance
still takes priority on bodies with exceptionally high relief; visual atmosphere
height alone no longer forces a 66–72 km orbit. Orbit hold maintains your current
altitude when engaged; it does not automatically descend to 30 km.

The crosshair is restored in the online HUD and hides while menus/chat capture
input. The compass follows camera rotation every rendered frame. **YOUR SHIP**
uses an owner-only location beacon with no distance or planet cutoff; nearby
other ships also have markers. Distant beacons do not subscribe you to detailed
ship physics, layouts, passengers or inventories.

Reboarding after respawn/disembarking now restores full owner prediction using
the cached ship layout while any refreshed layout arrives. Camera and ship-frame
corrections are smoothed together, including interior walking at accelerated
test speeds. A death container has no player/ship movement collider and is not
used as a second seated character.

After a Home-region respawn, use **Escape → CALL SHIP TO LAND NEAR ME**
(also available in the **F4** ship panel). The server finds dry, level ground
roughly 140–800 m from where you called, then flies your ship there and lands.
Stay near your call location: the destination does not follow your character.
The ship must be empty, powered, undocked and at the same planet as you.
Passengers, including disconnected passengers, prevent recall. Landed ships
already within 250 m do not need recall. Recall continues outside visibility
range and survives checkpoints/restarts. **CANCEL RECALL** stops the flight;
obstacles or loss of power also stop it. Move to open ground and retry if the
route or landing area is blocked. After arrival, board using the ramp as usual.
Default recall cruise speed is 1000 m/s, configurable with `shipRecallSpeed`
(10–10000 m/s); this uses simulation time, including enabled test acceleration.
Site searches are throttled to one per ship per ten wall-clock seconds.

For orbital/weather testing, launch the packaged `RunServer-TestClock.bat`.
It generates `server.test-clock.json` from your original `server.json`, retaining
its world identity and save path and enabling test controls at 1x. It leaves the
original configuration intact. Alternatively, set `allowTestTimeControls` to
`true` in the server's JSON configuration. Numpad **+ / -** changes the shared server speed
through **1x, 2x, 4x, 8x**. The pilot HUD and online status show
the current test speed. Chat/menu input suppresses these shortcuts. Any connected
player can request a change on an opted-in test server; leave the flag `false`
for normal play. `testTimeScale` sets the starting speed and defaults to `1`.

Acceleration advances actual authoritative simulation, including orbits,
weather, survival needs and industry. Movement/collision and prediction use
extra ordinary fixed steps; mouse/jump/toggle edges are consumed once per input.
Input/snapshot packet frequency, bandwidth budgets, command rate limits and
save intervals retain their wall-time cadence. Speed changes reset prediction
epochs for everyone, so a brief correction can occur when changing speed.
The selected speed resets to the configuration on server restart; world state
and elapsed simulation time remain saved. High speeds cost more simulation CPU
and are diagnostic settings, not a performance acceptance mode.

The weather-recording client also includes `RunWeatherTrace.bat`. After a flicker,
press Ctrl+Shift+F10 to save the preceding render history and a screenshot in
`Diagnostics`. Recording adds overhead. Starting the executable directly leaves
recording disabled. Both testers and the server must update to protocol 13;
existing content-2 saves remain supported.

## Host

1. Keep the complete server folder together, including its data folder and DLLs.
2. In `server.json`, set a recognizable `displayName` and `maxPlayers` to `2`.
   Keep the same `worldId`, `instanceId` and `seed` when resuming that world.
3. Run `RunServer.bat`. Wait for `Server ready` and the Relay join code.
4. Leave the server running while either player is playing. Relay handles the
   connection; neither player needs to host gameplay inside their client.

With an empty `savePath`, server saves are separate from offline saves under
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\project-rts\Servers\<worldId>\<instanceId>`.
The checkpoint and journal belong together. Each client retains its character
through its local Unity Authentication profile; keep that application data.

## World settings

New worlds share the authored planet/system/galaxy preset with Single Player.
Use `RunServer-LargeWorld.bat` for a separate 100x test world; your existing
friends save retains its recorded scale. `RunSinglePlayer-LargeWorld.bat` uses a
separate offline slot. See SHARED_WORLD_PRESETS.md for authoring/export details.

## Join

**F4** opens the full crafting browser: search, categories, ingredient filter,
recipe outputs, time/power/water requirements, quantity and MAX controls. The
bottom queue shows server progress and cancellation with ingredient refunds.
Crafts reserve ingredients and output slots and continue with the menu closed.
Choose a nearby owned machine in **CRAFT AT** to browse its supported recipes,
set production and view status. **MANAGE INPUT / OUTPUT INVENTORY** opens the
shared slot inventory. Busy batches must finish before their recipe can change.
Use **CONSTRUCTION / MACHINES / SHIPS** for placement, logistics, ship and other
existing controls, then **BACK TO CRAFTING** to return. Search and quantity input
capture gameplay shortcuts and Enter while focused. Menus do not pause the server.
Empty crafting jobs reconstructed by Unity JSON are normalized on receipt, so
empty/completed queues do not show a phantom cancel entry or consume queue space.

Run `SyntheticFront.exe`, choose **MULTIPLAYER**, then refresh and join the host's
listing. Alternatively enter their Relay join code and choose **JOIN CODE**.
**SINGLE PLAYER** starts offline Survival. RTS and army stress modes are retired.

Default multiplayer controls:

| Action | Key |
| --- | --- |
| Text input / send | Enter, then Enter |
| Clear and cancel text | Escape |
| Proximity voice | Hold V |
| Pilot assist | U |
| Inventory / crafting and construction | I / F4 |
| Door, pilot seat, leave seat or vehicle | F, near the interaction |
| Board / disembark a landed ship | Walk up / down its ramp |
| Dock / undock as the owner pilot | J |

Voice and pilot-assist bindings can be changed in the multiplayer Escape menu.
Typing should block movement, gameplay actions and mouse look; Escape should
cancel chat before opening any menu. Multiplayer menus do not pause the server.

The ship owner pilots. Invite the other player through F4 to let them board as a
passenger. Walk onto the foot of the ramp to board; open the rear and inner
hatches with F and walk through. Boarding preserves your position instead of
teleporting into the airlock. Death drops carried items into a recoverable container. Check that
reconnecting restores the same character and inventory, and that restarting the
server restores construction, containers and ships.

Stations are player-built orbital docking gantries. Craft a docking station kit,
pilot your ship above the atmosphere and at least 20 km altitude, then deploy it
from F4. The berth permits the station owner and invited pilots. Docking requires
a free berth, alignment within 10 degrees, distance within 12 m and relative speed
below 5 m/s. Walking remains inside the moored ship.

For playtest reports, include both players' `Player.log`, the server log, the build
version, what each player did and whether reconnecting changes the problem.
Please exercise voice, physical chat controls, construction, passenger travel,
death/loot, reconnect and server restart. Automated checks supplement these tests;
they do not establish 100-player capacity or the 60 FPS survival performance target.

## World and flight presentation update

Protocol 12 includes the single-player solar-system renderer, stars and distant
planets from the server's saved universe manifest. Only your current planet gets
full terrain detail; the server runs detailed ship movement near connected players
(100 km by default), while persistent industrial work and analytic celestial/station
orbits continue independently. Players can occupy different planets in one instance.

New worlds default to `systemSizeMultiplier: 0`, inheriting the shared preset's
current 100x scale. Existing server saves retain their original scale, positions and identities.
To try the new default scale, use new world/instance IDs and an unused save path;
keep your existing checkpoint and journal together. This does not import an offline
save into the server.

Multiplayer now uses the same modular ship shell, glazing, animated hatches and
terrain-adjusted boarding ramp as single player. Owner pilots receive the shared
UI Toolkit orbit map, gravity forecast, prograde marker, landing instruments and
celestial navigation. O toggles celestial orbit guides, P cycles navigation targets,
L toggles authoritative attitude hold, and N toggles orbital velocity hold.
All multiplayer panels and text chat use UI Toolkit. Both players and the server
must update together; older packages cannot join protocol-12 sessions.

## Ramp reboarding correction

This update gives ground walking the same CPU ramp support on the client and
server before the ship attachment acknowledgement arrives. Boarding follows the
actual landed position, rotation and terrain-adjusted endpoint, including the
full supported ramp width. Both clients and server need protocol 12.

To verify, take off, land on a planet, leave the pilot seat, open the rear hatch,
walk down to the ground, then turn around and walk up again. You should stay on
the ramp and walk through the open hatch without falling through or teleporting.
Invited passengers can board; pilot control remains owner-only.

Performance investigation was deferred at the user's request after confirming
Unity Editor was also open. Retest the standalone client with the Editor closed;
no measured cause or FPS improvement is claimed by this collision fix.

## Inventory and movement presentation update

Protocol 12 restores the shared slot-based UI Toolkit inventory, ten-slot action
bar, vitals and walking compass. Press I for inventory; drag to move stacks,
right-drag to split, use number keys to select a belt slot, and drag to the drop
zone to create a recoverable ground container. Nearby containers and owned
machine storage appear in the inventory panel. F4 opens crafting/construction.
Consumables use the equipped slot; deployable selection opens placement.

Movement remains locally predicted with authoritative acknowledgements, input
replay and correction. Presentation now interpolates predicted fixed-clock
states at render frequency and applies pending walking mouse look immediately.
The server still advances at its configured fixed tick rate. Frame changes and
authorized teleports reset interpolation. Network UI data refreshes are bounded
and repeated ship physics JSON is cached to reduce client work.

## Orbit hold and compass correction

Press N once while piloting in planetary orbital space to enable orbit hold;
press N again to turn it off. Shift boosts the bounded thruster correction.
Manual translation, braking, warp, landing or docking cancels hold. Orbit hold
requires being above the planet's atmosphere/terrain and within orbital range;
it cannot launch a landed ship. Hold state, captured radius/plane and status now
survive authoritative snapshots and prediction replay. The walking compass hides
while piloting, leaving one navigation display. Both clients and server must use
protocol 12; existing world checkpoints remain compatible.

## Orbital clouds and atmosphere correction

The client update keeps air sampling stable when faint clouds appear/disappear,
filters cloud detail to its pixel/ray footprint, and stabilizes mip transitions
and bounded weather rays. It applies to offline and multiplayer rendering.
Protocol remains 12 and the existing server can stay in place. Please retry the
previously affected L-locked orbit on the same saved world; atmosphere/cloud
settings and world state are unchanged.


