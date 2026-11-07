# Mining teams and front-discharge tailings

## Using the system

Restart Play mode after compilation, then leave the cab before assigning jobs.

1. Open an excavator's **F** menu and choose **Auto operation / Replan**. AUTO finds a pay-dirt node, parks the excavator there and digs into its bucket. It stays at that worksite to load trucks. A loader can perform the same role through **WORKSITE LOADER · LOAD DUMP TRUCKS**.
2. Open a dump truck's **F** menu and choose **Auto operation / Replan**. AUTO prioritizes owned active digging sites. Select a sluice, stockpile or store as the destination, or use AUTO destination selection. Trucks receive real material from a worker within 12 m; they no longer mine nodes themselves. A truck arriving before its excavator waits visibly.
3. Assign a separate excavator or loader to **PERIODIC TAILINGS CLEANUP**. It selects the nearest owned sluice and establishes a remote tailings dump on suitable ground. It waits for a 500 L batch, or collects a smaller batch after 20 simulation seconds, then fills its bucket, delivers and returns. The primary mining digger remains at its node.
4. Supply Fuel in Survival. Newly assigned Creative jobs require no fuel. Status text explains missing workers, fuel, full destinations and obstructions. Manual takeover stops the job without deleting cargo.

The sluice's front chute discharges into a visible growing heap; only ore enters the ordinary output inventory/conveyor. The front heap holds 5,000 L. Filling it stops washing before consuming more dirt or water. Its **F** menu shows volume, configures a dump, and allows manual recovery of up to 100 L at a time. Packing a machine with remaining front waste is refused.

Automatic dump placement tests clear, flat ground 32/48/64/80 m away, preferring the front direction. Valid sites must be 25–100 m away and clear of machinery, construction, players and other piles. Dumps hold 100,000 L; full dumps pause cleanup with cargo retained. Provide access and keep the front discharge area clear. New belt/wire/pipe routes reserve its ground-level clearance; overhead routes can pass above it.

One cleanup vehicle does not guarantee continuous full-rate processing: travel distance, payload, fuel and ore grade affect capacity. In the stress fixture below, one loader per sluice on a 40 m route sustains 86.6% of theoretical production, with safe full-heap pauses. Shorten routes or provide additional cleanup capacity. Multiple vehicles still need separate accessible berths and space to avoid blocking one another.

## Simulation and migration

Material volume, ore composition and radioactive activity follow bucket → truck → processing and front heap → cleanup bucket → dump. Tailings remain separate from pay dirt and cannot be washed repeatedly. Radioactive waste remains hazardous. Dump height-grid cells conserve volume; front heaps use a shared instanced mound mesh scaled by volume. Chutes and payload fills are prototype presentation; bucket transfers are timed authoritative operations, rather than articulated physical grain simulation.

New automated travel uses energy costs of 4 for excavators, 2 for loaders and 1 for trucks per movement tick. One fuel item supplies 100 energy; excavation and bucket transfers also cost energy. Idle waiting costs none. An equal-material test moving 1,000 L over 300 m used 14 fuel with a parked excavator/truck versus 95 for a travelling excavator; this is fixture-specific, not a universal saving.

The workflow is enabled through a versioned journal entry. Older events replay under their original rules. Active legacy excavators migrate to digging, trucks to transport and loaders to hauling. Existing output tailings move to the front buffer as space permits; overflow stays accounted in output until it can discharge. Replan old routes after updating. Saves retain roles, reservations, buckets, heap contents, dump shapes and radioactive activity.

Route planning probes obstacles every 0.5 m with 0.15 m extra planning clearance, while storing approximately 2 m waypoints. Trucks avoid the excavator's future parking berth and choose a pickup within loading reach. Runtime movement continues checking obstacles. The adapter retains bounded recovery/replanning rather than silently teleporting blocked vehicles.

## Validation and measurements

237 standalone core checks, 71 Unity UI Toolkit checks and 164 Unity placement checks cover the transfer chain, roles, ownership, future loading berth, fuel, blockage, full front buffers, radioactive waste, pile conservation, manual recovery and save/journal recovery.

Equivalent Release standalone CPU control fixtures use identical serialized starting inventories/routes, 1,000 stationary machines, 100 active industrial vehicles and 16 remote observers. Three alternating runs use 600 warm-up and 3,000 sampled ticks, pinned logical CPU 2, AboveNormal priority and disabled tiered compilation. These are industrial CPU tick times, **not Unity frame times**.

| Control measurement | Frozen baseline | Current |
|---|---:|---:|
| Median tick | 0.8008 ms | 0.8366 ms |
| p95 tick | 0.8473 ms | 0.8848 ms |
| Simulation rate | 1,432.5 ticks/s | 1,367.8 ticks/s |
| Process private bytes | 46,071,808 | 46,362,624 |
| Working set bytes | 72,769,536 | 73,109,504 |
| Managed bytes | 18,557,984 | 18,657,000 |
| Planning, 100 jobs | 106.7 ms | 196.2 ms |
| Snapshot size | 2,981,307 B | 2,989,907 B |
| Timed save / load | 26.18 / 33.94 ms | 25.74 / 33.72 ms |
| Delivered material | 1,919,220 L | 1,919,220 L |

Equivalent steady-state p95 increased 4.43%. Denser safety probes increase planning cost. Initial independently generated routes produced a 10.47% p95 difference and slightly different delivery totals; those raw measurements remain preserved, and the table uses identical routes for the steady-state comparison. Serializer measurements follow a warm-up serialization pass.

Separate feature scenarios are not equivalent throughput comparisons:

* 50 excavators + 50 trucks, 1,000 stationary machines: median 0.7349 ms, p95 0.7791 ms, 1,457.9 ticks/s; private/working/managed bytes 46,485,504 / 72,744,960 / 17,647,992. Delivered 548,700 L; source, bucket, truck and destination quantities conserve all 50 million L. Save/load 23.56 / 31.21 ms.
* 100 cleanup loaders + 100 powered sluices + 900 other stationary machines, 20% ore, 40 m dumps: three normal-grade runs have median tick 1.0490 ms, median p95 1.1079 ms and median simulation rate 1,193.3 ticks/s. Median private/working/managed bytes are 48,164,864 / 74,911,744 / 12,440,440. All 100 jobs remain active, with no fuel starvation. Delivered waste is 1,953,080 L. Processing utilization is 86.575%, explicitly below full rate because cleanup throughput limits the chain. All input, ore, waste and dump-cell quantities conserve material. Median save/load 27.68 / 38.91 ms; snapshot 3,431,904 B. Earlier richer 60% ore runs in the comparison JSON are distinct fixtures, not evidence of normal-grade full-rate operation.

Raw measurements: `TestResults/Industrial/MiningTeams/comparison-canonical.json`, `waste-normal-1.json` through `waste-normal-3.json`, plus frozen baseline sources and canonical snapshot in that directory. Full Unity render frame time, native/GPU memory, network bandwidth, readback cost, fog cost and combined million-unit/fleet acceptance remain unmeasured. GPU army simulation and its million-unit path were not changed; these compact industrial records do not add an army CPU mirror or per-army-unit objects.
