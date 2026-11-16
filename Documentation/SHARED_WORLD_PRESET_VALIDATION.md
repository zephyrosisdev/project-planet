# Shared world preset validation — 2026-11-15

Implementation uses one captured, versioned preset for new offline/dedicated
worlds. The default asset references the existing authored home/system/galaxy.
Saved canonical definitions remain authoritative, including old multiplayer saves
without preset metadata. New offline saves journal their definitions through the
existing industrial store. No populated-world resizing or coordinate migration
is performed.

Delivery: `Builds/Playtest-20261115-184913`, protocol 13, content `survival-5`,
server save version 3. Update both clients and server. This package retains the
opt-in test clock and weather recorder. Larger-world launchers create separate
saves. Client/server ZIP entry hashes and checksum files were verified (234/236
files respectively). See `TestResults/Multiplayer/world-preset-package-checks.json`.

Checks passed:

- All five conditional compilation roles: editor, client, server, graphics
  diagnostic and weather recorder.
- Windows dedicated, graphics diagnostic and weather-recorder client builds.
- 324 native headless checks, including 16 shared-preset cases, and 282 domain
  checks. Matching seed/preset/scale produces identical canonical definitions.
  Cases cover export/import, gravity preservation, source-asset immutability,
  saved-world precedence, journal recovery and save-slot isolation.
- Larger-world launcher preparation, repeated invocation, original-config
  preservation and rejection of a conflicting output without overwriting it.
- Two authenticated Relay clients restored matching saved ship/character state
  with the previous and updated standalone binaries.
- Fresh graphical offline startup generated 192 bodies at the requested 2x scale,
  240 km home radius, 2.5 m/s² gravity, 5,678 m atmosphere and 1,234 m cloud
  altitude. It saved and returned to the menu cleanly. Restarting the same slot
  with a missing preset file and requested 99x scale restored the original 2x
  definitions and identical preset fingerprint. Reports:
  `TestResults/Multiplayer/world-preset-offline-create.json` and
  `world-preset-offline-reload.json`. The probe waits until gameplay releases the
  journal before reloading it; the synthetic 37 m-relief unit-test planet is not
  used as a playable starting-region fixture.

## Equivalent saved-world comparison

Archived `survival-4` versus updated `survival-5`, matching checkpoint SHA256
`F5610F24AF725C26F672A142CC6AB7DBBEA5A1C114F4DAFFA98E4CA4F7B44922`.
Co-hosted dedicated server, one graphical RTX 3070 client, one headless peer,
2560×1440 explicit HDRP rendering with GPU completion fence, 8x test simulation,
5-second warmup and 15-second frame sample. No Unity build or ZIP compression
ran during either performance window. The user's separate editor remained open.
This is a short regression fixture, not a release FPS or gameplay-capacity pass.

| Measurement | Previous | Updated |
| --- | ---: | ---: |
| Client frame median / p95 | 8.218 / 11.285 ms | 7.582 / 10.452 ms |
| Server base simulation | 29.929 Hz | 29.912 Hz |
| Physics steps at 8x | 239.431 Hz | 239.294 Hz |
| Server tick median / p95 | 7.714 / 17.129 ms | 13.293 / 20.140 ms |
| Snapshot p95 | 1.128 ms | 1.995 ms |
| Server send / receive | 46,868 / 4,989 B/s | 81,782 / 9,716 B/s |
| Mean connected population in server window | 0.736 | 1.340 |
| Client peak working set / observed private | 1,136.3 / 2,792.7 MiB | 1,130.5 / 2,772.4 MiB |
| Server peak working set / observed private | 302.5 / 406.8 MiB | 304.0 / 404.8 MiB |

Client frame samples show no regression in this scenario. Server metric windows
include admission and different population duty cycles, so their CPU/snapshot and
bandwidth totals are **not comparable steady two-player measurements**. The
updated run kept its intended base simulation rate. Do not interpret the larger
server totals as a measured preset cost or claim an overall server performance
improvement. Full data: `TestResults/Multiplayer/world-preset-validation.json`.
Runs: `Live/f4e69edc5fe2` and `Live/182984c54502` under that results directory.

## Remaining playtest gates

Interactive two-person exploration of newly generated 10x worlds, terrain/contact
agreement at their larger radii, broad standalone performance and atmosphere
appearance remain unmeasured. This does not establish the 60 FPS acceptance
target or future 100-player capacity. Existing friends saves are preserved and
still have their recorded 1x scale; use the separate launcher to see larger planets.
RTS and army benchmarks remain retired.
