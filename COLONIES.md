# Colonies and background settlements

Planetary resource deposits are inexhaustible. Extraction retains its normal
rate, workforce, food, power and delivery requirements; inventory storage remains
finite. Old depleted deposits are restored when their settlement initializes,
and deposit tooltips display INFINITE.

Use **[** and **]** to cycle between planets with your command centres and the
planet containing your character/ship. Other planets allow RTS control only;
returning restores your physical view without transferring the ship.

Open Build and place a Command Center on neutral, dry, clear ground. This also
works elsewhere on the same planet. Claimed land rejects Command Centers;
Forward Outposts can establish military construction access in enemy territory.
An operational, road-connected homeworld Launch Pad is required. The order pays
for the command center, solar array, warehouse, four houses and starter supplies from
the homeworld settlement. The delivery normally takes 15 simulation seconds;
distance does not extend that timer. Arrival creates the colony even if you have
left the destination. The Orbital Assets panel lists colony deliveries.

The kit includes 600 construction materials, 100 propellant, 100 food, residents
and a cargo cart. Start normal production and expand from its command center;
new colonies enable Auto Pilot by default. The Command Center toggle controls
that planet's planners independently. Autonomous countries can also fund new
off-world kits from their homeworld Launch Pads; only one expansion destination
is prepared/planned at a time.
There must also be room for the kit's power, storage and houses beside the chosen
landing site. If that space becomes blocked in transit the landing waits.

Use **Massive Army > Planetary > Settlement and Colony Settings** in Unity to
select the project settings asset:

- **Colony Delivery Seconds** controls the short transfer timer.
- **Background Tick Seconds** controls the interval between visits to distant
  settlement data (default 5 seconds).
- **Starting Prebuild Seconds** limits the initial free expansion phase (default
  3 seconds). Set it to zero to keep only the original starter structures.
- **Starting Prebuild Maximum Additions** bounds the number of placement attempts
  across all starting countries (default 512, as a safety limit).

Prebuild uses the existing terrain, district and road placement rules. It runs
across frames with a progress bar and does not charge construction resources.
Larger time/attempt limits permit larger starting settlements; placement failures
and locked technologies can still limit expansion. Prebuild does not advance
the game clock or train free military units.

Distant visited settlements are serviced in round-robin order, at most one world
per frame, with the existing catch-up time budget and a bounded number of economy
substeps. Elapsed time remains queued if the budget is exhausted. Production,
food, cargo, orbital jobs, research and paid expansion continue. New background
structures complete immediately. Detailed civilian society and movement are
asleep. Completed unit production orders retain their queue records and spawn
through the existing GPU path when tactical simulation resumes; no CPU army
mirror or per-unit objects are introduced. Planet preparation happens when a
player visits or an actual funded AI colonisation plan needs a destination;
routine background economy updates do not prepare unrelated planets.

Standalone validation fixtures skip initial prebuild so existing benchmark
scenarios retain their starting conditions. `-planet-no-prebuild` also disables
it explicitly. The colony checks use `-planet-colony-check -planet-output <path>`.
