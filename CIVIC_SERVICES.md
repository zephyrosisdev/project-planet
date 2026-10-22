# Police, deaths and funeral services

Build **Police HQ** and **Graveyard** from Support and connect their entrances to town roads. Both require power, completed construction and assigned staff. These services work in RTS and survival. Spectators can inspect records and routes.

Police HQ controls requested patrol strength and automatic escalation after civilian zombie deaths. Available employed officers fill that request; additional/upgraded HQs supply more jobs. The initial circuit visits connected residential entrances. Add or remove building entrances in the HQ patrol circuit (up to eight stops per faction). Officers share GPU reverse road flows and walk sidewalks; closed gates and disconnected roads stop them. They shoot encountered zombies within 35 metres, stop to engage and resume the circuit afterward. Zombies acquire outdoor residents within 60 metres and attack at normal melee range. Residents inside buildings are sheltered.

A staffed graveyard includes a GPU road hearse, up to eight hearses per colony to bound route storage. It visits the closest friendly road node to the recorded death position and returns to its graveyard. Actual GPU arrival events trigger collection and delivery. Blocked missions wait for road repairs or opened gates. CPU state contains endpoints and biographies, never live vehicle/unit poses.

Click a hearse for the deceased's name, age, cause, game-calendar death date and transport/funeral status. Click a graveyard for paginated recent deaths; Residents also lists these records. Records and service missions save with the colony.

Natural lifespan is deterministically varied from 72 to 100 years. Old-age deaths and zombie deaths use the same funeral service. Police service is remembered after retirement. Honours are scheduled for the next day (08:00 local game time); a missing graveyard or undelivered body delays the ceremony. Funerals last one game hour. Family members attend; police colleagues also attend police honours, then resume normal activities.

## Performance and ownership

One GPU dispatch per tick uses the existing army spatial hash for bounded local combat queries (maximum 192 examined neighbours per outdoor resident). Officers stay in GPU civilian buffers; no officer, civilian or hearse GameObjects are created. Health, damage, patrol progress and positions are GPU authoritative. CPU resident entries contain names, employment, family and life/death metadata.

The existing army aggregate readback signals pending deaths; there is no extra polling readback while nobody has died. At most 256 named deaths are read back per request, four requests per second, one request in flight. Deaths remain pending until acknowledged; overflow or failed readback retries. Slot generations reject stale acknowledgements when a slot is reused. Hearse selection reads one packed GPU ray-hit word.

Police and cemetery art uses the shared modular catalogue, combined garden/gravestone meshes and existing detail pool. The editor menu Massive Army > Art > Create police and graveyard models regenerates their profiles and entries, preserving other building/unit art.

Standalone validation flag: -planet-civic-validation -planet-output <directory>. Validation alone reads/writes GPU poses for reproducible fixtures; runtime does not mirror them.

The benchmark-only flag -planet-civic-combat-control disables civilian combat for equivalent million-zombie comparisons; normal games enable it.

## Verified release

Unity build: Succeeded, 0 errors. New civic tests: 36 PASS; household regressions: 322 PASS; modular art: 63 PASS; model packs: 102 PASS; city validation: 17 PASS. Release runtime suites report zero errors.

At 1440p on RTX 3070, the million-zombie run measured 7.00/11.95 ms frame median/p95, 27.09 simulation ticks/s, and 557.5 MiB tracked allocated memory (797.7 MiB reserved). Simulation p95 was 5.12 ms against 5.45 ms in the matched control. Full evidence: TestResults/Planetary/CivilSecurity/Results.md.


## Inspecting crime and security

Use **CRIME** in the right-hand settlement toolbar, or **Crime & Security** inside the settlement information window. Police HQs, county jails, courts and federal prison buildings also provide a **Crime Reports & Custody** shortcut.

- **Incidents** lists named suspects, victims, reporting officers, dates and court outcomes. **Show incident location** closes the window and focuses the RTS camera on the recorded event location.
- **Custody** shows detainees, their stage of proceedings, assigned facility and sentence end where applicable.
- **Gangs** lists associations known from police reports. **Businesses** shows suspected premises associated with reported suspects, including closure status.
- **Incident map** toggles orange minimap crosses for up to 32 visible open cases among the most recent 256 incidents. These are recorded incident locations, not tracked criminal positions.
- **Residents** includes reported offences and custody information. AI Test spectators additionally see underlying activity, dependency, hardship, firearm ownership and named social ties. Ammunition is explicitly shown as cumulative issued/purchased, not remaining rounds.

Normal play hides unreported incidents, unknown gangs/premises and undiscovered bodies; spectators may inspect them. Reported allegations do not imply conviction. Independent adult work and exploitation victims are described separately from offences.

Fresh settlements can have no crime. The current simulation begins increasing criminal activity risk above 60 residents and evaluates development over simulated days. This UI does not seed offences or alter the simulation.

UI validation: watcher action `crime-ui`, report `TestResults/Planetary/Crime/UI/editor.json`. This validates presentation, visibility, paging and action callbacks; it is not an end-to-end acceptance of the larger crime simulation.
