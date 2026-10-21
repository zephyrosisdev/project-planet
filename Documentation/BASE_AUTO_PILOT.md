# Base Auto Pilot

In RTS and Survival, select your completed Command Centre and click **AUTO PILOT: OFF** to turn it on. Click again to return to manual base control. New games start with it off; the existing civilisation faction save data preserves the setting.

Auto Pilot uses the same planners as the spectator factions. It constructs and supplies the economy, connects roads, repairs buildings, develops defences, researches technology and queues production. Construction uses normal placement rules, resources, supply and build times. Manual orders remain available. Switching off stops new planner decisions; construction and queued work already ordered continue.

Base strategy decisions retain their four-second interval and existing bounded placement budgets. Army records, movement and combat remain GPU-authoritative. Auto Pilot does not change army selection or issue offensive orders for the player.

The standalone menu validation exercises the actual Command Centre toggle in RTS and Survival, checks paid construction, serializes the faction setting, and checks that decisions stop after switching it off. Spectator controls remain read-only.

## Standalone validation, 21 October 2026

The final standalone player passed 1,782 integration checks across RTS, Survival and spectator mode, including pointer access to the toggle and resolving a procedural deposit into a single persistent construction record.

Equivalent fenced measurements used the RTX 3070 at 2560×1440, 4,096 zombies and 4,160 total GPU units in both builds. These compare the pending planet-wide resource changes as well as the Auto Pilot wiring; they are not a million-unit throughput measurement. Army kernels and the million-unit stress path were not changed.

| Measurement | Before | Optimized |
| --- | ---: | ---: |
| Frame median / p95 | 6.090 / 7.425 ms | 7.167 / 8.500 ms |
| Army simulation median / p95 | 0.519 / 0.942 ms | 0.539 / 0.617 ms |
| Simulation rate | 27.825 Hz | 27.426 Hz |
| Unity tracked allocation | 572.45 MiB | 545.84 MiB |
| Unity tracked reservation | 799.66 MiB | 799.66 MiB |

Army simulation median increased 3.88%; p95 decreased 34.50%. Planet-wide deposit rendering adds approximately 1.08 ms to median frame time; the measured p95 remains below the 16.67 ms frame budget at this tested scale. Tracked memory is not total VRAM or process memory.

The separate full AI regression reports **FAIL in both builds** on the existing late assertion `AI resumes patrol after all enemy targets disappear`. The comparison above uses the completed, equivalent 5–35 second measurement phase before that assertion; it does not present the entire AI regression as passing.

Reports: `TestResults/Planetary/AutoPilot/Confirmed/validation.json`, `TestResults/Planetary/ProceduralDeposits/Before/validation.json`, and `TestResults/Planetary/ProceduralDeposits/Optimized/validation.json`.
