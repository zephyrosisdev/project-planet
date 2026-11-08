# Multiplayer checks

Run from the repository root using the pinned editor's bundled SDK:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/DotNetSdk/dotnet.exe' run --project Tools/MultiplayerChecks/MultiplayerChecks.csproj
& ./Tools/MultiplayerChecks/Compile.ps1 -Target All
```

The domain executable compiles the production industrial/network-domain sources.
It checks input admission, fixed-clock timing, reference-frame/body filtering,
private character persistence, reconnect identity, journal crash recovery, and
durable action receipts. Results go to `TestResults/Multiplayer/domain-checks.json`.
AOI cases also cover deferred delivery/removal commit, restoration on re-entry,
bounded removal batches and aging under a constrained byte budget.
The compile script uses resolved Unity response-file references, without touching
the editor's output assembly. It does not run Unity's import/code-generation or
player build pipeline. `-Target All` compiles Editor, Client, Server and GraphicsProbe
branches separately. `-Target Editor` remains the default. These use editor reference
assemblies with role-specific scripting symbols; platform stripping requires a Unity build.

Also run `Tools/IndustrialChecks/IndustrialChecks.csproj` for the existing
industrial regression suite. Its local TCP clients need loopback socket access.

`RunServerChecks.bat` runs the native simulation, packet, metrics and discovery fixtures (78 checks).
The AOI fixture invokes the production snapshot publisher and decodes the exact
UTF-8 payload passed to its send callback. It verifies filtering, inventory
redaction, coarse proxies, hysteresis and budgeted removal/re-entry without cloud
connections. This is serialized-payload validation, not a Relay packet capture.

These checks do not establish live Relay/Vivox operation, movement collision
parity, standalone frame time, fog cost, or 100-player capacity. See
`Documentation/SURVIVAL_NETWORK_IMPLEMENTATION.md` for remaining gates.

Native checks are available at **Massive Army → Validation → Survival Network
Simulation** in Unity, or through `RunServerChecks.bat` after a dedicated build.
The CLI flag `-survival-simulation-checks` bypasses cloud startup and quits with a
failing exit code if a simulation assertion fails.

Ownership checks cover pilot exclusion for invited passengers, revocation without
in-flight ejection, berth capacity/reuse, and durable invitations. Tick-budget
checks reject handoff-induced double stepping.

The domain suite also checks journalled respawn placement/revival receipts and
atomic death transfers: carried stacks, radioactive bulk, crafting-input refunds,
ship-relative container positions, recovery after restart and reach-checked loot.
No generated crafting output is awarded when a character dies mid-craft.

After building the dedicated server and graphics fixture, run the opt-in authenticated standalone gate:

```powershell
& ./Tools/MultiplayerChecks/RunSessionProbe.ps1 -ServerOnly
& ./Tools/MultiplayerChecks/RunSessionProbe.ps1 -Headless
& ./Tools/MultiplayerChecks/RunSessionProbe.ps1
```

This creates a disposable world/save, Authentication profiles and a temporary
public Lobby/Relay allocation in the linked production UGS project. It requests
normal session cleanup on completion. The full run launches two graphics-enabled Windows
clients at 960×540 and checks remote visibility, bounded authoritative walking,
acknowledged prediction samples and character/position continuity on reconnect.
Reports/logs are written under `TestResults/Multiplayer/Live/<run-id>`. The clients
use batch mode with graphics enabled and synthetic input. This functional fixture
does not establish interactive UI/chat behavior or normal rendered frame times at
the 2560×1440 performance acceptance scenario.
Chat/voice, full survival and the 8/32/100-client gates remain separate.

`-Headless` launches two instances of the current dedicated executable in an
explicit client diagnostic mode, without a menu, renderer or Vivox. They use the
same connection approval, commands, snapshots, fixed clock, prediction and replay
as graphical clients. This is a functional network subset, not the graphical
two-player acceptance gate or full gameplay capacity. The build marker prevents
older server binaries from accidentally starting a normal world for this mode.

The runner rejects builds missing the requested `session-probe.version`. Normal
Player builds compile out the diagnostic probes and intentionally lack this marker.
Native checks and offline fixture seeding compile only in Editor/dedicated builds;
dedicated startup compiles only with `UNITY_SERVER`. BAT builds use two workers
by default. For builds on a machine with limited available memory, set `UNITY_BUILD_LOW_MEMORY=1` before
running the BAT build scripts. This uses one worker; it does not change
runtime quality, simulation settings or the army scale.

Use `-ResumeFrom <completed-Live-run-directory>` with `-Headless` to reopen the
same diagnostic save with the same client profiles. The runner checks character
identity and position continuity across the server restart. Resume paths are
limited to this workspace's diagnostic saves.

`MassiveArmy.EditorTools.SurvivalBuild.GraphicsProbeClient` builds an explicitly
marked fixture at `TestResults/Multiplayer/GraphicsProbeClient`. Its offline
army/RTS compute exclusion list is in `SurvivalServerShaderFilter.ProbeArmyShaders`.
Normal client and server BAT entry points are separate. The fixture requires
probe launch arguments and cannot be used for release or army performance
acceptance. The runner uses this fixture by default; `-ClientExe` can override its path.

Add `-Chat` to require real Vivox text exchange with the peer and acknowledged
delivery, positional audio-channel membership and muted capture, both before and
after reconnect. Use graphics-enabled clients; headless mode does not instantiate
Vivox. Messages go only to the disposable diagnostic world's text channel. This
does not test microphone capture, audible playback or physical keyboard input.

Add `-ListingRecovery` to a new diagnostic session to delete only its dedicated
server's own Lobby after all clients connect. The runner requires marker 11 and
holds clients for at least 40 seconds. The server must create a replacement Lobby
with the same Relay code, world/instance, protocol/content, population and capacity;
the existing client IDs and server clock must survive recovery. This can be combined
with `-Chat` or `-Headless`, but not resume, factory or server-only modes. It does
not test loss/reallocation of the Relay transport. The server report records the
old/new Lobby IDs, recovery duration and continuity assertions.

`-JoinCodeDelaySeconds 15` adds an opt-in server-side wait after transport start
and before asking Relay for the join code. It requires a new marker-11 diagnostic
world and accepts 1–30 seconds. The server report must confirm an active server,
an advancing fixed clock and the full requested delay; normal client connection
checks run afterward. Combine it with `-ListingRecovery -Chat` to also exercise
listing replacement and communications. It does not simulate a provider outage.

`-ClientCount 8` (default 2, maximum 100) changes the disposable server capacity
and number of standalone probe processes. Use `-Headless -ClientCount 8` for the
next transport/movement smoke stage. Each client walks, observes at least one
peer, checks prediction samples and reconnects; this does not validate full
survival load, all-to-all visibility or crowded-hub gameplay capacity. Windows
process working-set high-water marks and sampled private-byte peaks are included
in `result.json`. The short test is not a steady-state performance comparison.

Use `-HoldSeconds 65` to keep every client connected and stationary after its
walking segment, before reconnect. This exercises occupied simulation, snapshot
publication and persistence; it is not full survival load. Timing reports preserve
eight rolling windows next to the save as `*.network-metrics.window-N.json`.
Select windows whose minimum and maximum player counts both match the requested
population, and report stage sample counts with their percentiles. Journal and
checkpoint timings can include work outside ticks, so nested totals must not be
added together.

`NewProfilePair.ps1 -From <original-completed-Live-run>` creates two independently
writable copies of the same compacted checkpoint and disposable player profiles.
It locks the source journal while cloning and verifies matching checkpoint hashes.
Use the returned `before` and `after` directories separately with `-ResumeFrom`;
never reuse the already-mutated before save as the after starting state. The
runner defaults to matching build marker 11. Use `-ProbeVersion 4` only with
matching historical protocol-2 executables, such as the preserved profiling
baseline; it does not make different game protocols compatible.

`RunFactoryProbe.ps1` prepares an isolated copy of a completed two-character
diagnostic world (override its source with `-From`). Preparation happens offline,
with existing authenticated test identities and explicit materials; the running
server has no admission or command-validation bypass. The two clients create
power/water/conveyor links, check cargo and payment conservation, reject foreign
edits, repeat a delivered action, and reconnect. A second server process restores
the checkpoint; the builder walks into reach, removes/reclaims the links, and the
observer verifies removal. The wrapper verifies the final saved state.

Add `-Graphics` to use the graphics fixture; default clients are headless. The
same command path is used in both cases. Graphics runs also require active,
nonempty batched link geometry after creation and empty, inactive geometry after
removal; correct pixels are not asserted. Local acknowledgement files coordinate
the two diagnostic processes, while gameplay assertions use replicated state and
normal action replies. This does not test physical GUI input, survival resource
progression, dense factories or full capacity. Results are in the fixture's
`factory-result.json` and the linked create/recover live reports.

After recording completed before/after live-result paths and assembly hashes in
`TestResults/Multiplayer/profile-pair.json`, run
`python Tools/MultiplayerChecks/CompareProfilePair.py`. It verifies matching
configuration/profiles and selects only sufficiently sampled windows with the
complete population. Results are written to
`TestResults/Multiplayer/profile-comparison.json`. The measured checkpoint and
its limits are documented in `Documentation/SURVIVAL_NETWORK_PERFORMANCE.md`.


`RunFactoryProbe.ps1 -Buildings -LargeSnapshots` also places a paid foundation,
doorway and door, opens it, rejects foreign removal/door actions, checks replay
and reconnect, restarts the dedicated process, and recovers the original material
and activity. Add `-Shipyard` for paid structural ship/wiring recovery and
`-Graphics` for batched geometry creation/removal assertions. Marker-10 building
fixtures passed before the protocol-5 leave update; current runners require
marker 11 and matching protocol-5 binaries. World persistence remains version 1.

`RunSessionProbe.ps1 -Headless -OneWayDelayMs 100 -JitterMs 20 -LossPercent 2`
installs the real UTP simulator layer, preserving Relay/DTLS and reliable
retransmission. Both peers apply those send conditions. Driver settings and
activation are asserted in reports. The pinned NGO `DebugSimulator` API is
obsolete and has no effect; this fixture does not use it. Reconciliation compares
matching server ticks, input sequences, epochs and coordinate frames. Protocol 5
adds a reliable application leave acknowledgement so a delayed transport close
cannot retain the old authenticated character during an ordinary reconnect.

`RunLoadProbe.ps1 -ClientCount 2|8|32|100 -Layout crowded|dispersed -HoldSeconds 90`
uses independent UGS registries and NGO managers in one headless engine process,
with production authentication/session/prediction and two consumable actions per
actor. Clients walk normally to a shared 2 m-spaced grid after admission, then wait
through a ten-second settle period before a common movement timeline. The report asserts the measured starting radius is at most 30 m. Metric windows
must lie entirely inside the measured server ticks; rally time is excluded.
Use at least 120 seconds for dispersed travel.
Start at two clients and increase only after the prior stage works. The dedicated
fixture must also carry `load-probe.version` 2. Reports preserve assembly hashes,
server occupied windows, snapshot cost, simulation rate, application byte rates,
Windows memory peaks, combined headless engine-loop frame times and background
Unity CPU deltas. Preserve the original editor; do not run builds during measured
load. The first protocol-4 two-client harness smoke passed while an isolated build
was running; its timing is not used as an acceptance measurement.

The load report is scripted walking/item use, not full rendered survival,
interior/ship/machinery activity, physical voice, army/fog or full gameplay
capacity acceptance. Never substitute the combined headless-client loop for an
individual rendered client frame time or label transport-only load full capacity.

Initial pre-snapshot connection attempts retry at most three times with bounded
backoff; attempt counts and the last initial error are recorded. Any disconnect
after the first snapshot or after measurement begins fails the run. Pending NGO
shutdown is awaited and manager destruction precedes transport destruction,
including timeout/cancellation cleanup. Early load-marker-1 orbit scenarios are
spawn-area tests, not verified dense hubs; retain their failure/evidence records.
