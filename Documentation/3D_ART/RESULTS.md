# 3D art pass validation results

Windows, Unity 6000.6.4f1, RTX 3070, D3D11, 2560×1440. Three runs per build with the same seeds, settings and camera poses. The earlier exploratory runs overlapped artwork rendering and are excluded.

| Scenario | Baseline median / p95 ms | Art median / p95 ms | Art simulation Hz | Baseline → art allocated MiB | Baseline → art reserved MiB |
|---|---:|---:|---:|---:|---:|
| base-close-50000 | 7.93 / 9.13 | 7.98 / 9.01 | 30.00 | 335.8 → 346.8 | 800.2 → 802.2 |
| overview-1000000 | 26.27 / 27.84 | 26.35 / 28.04 | 30.00 | 334.9 → 335.0 | 808.2 → 808.2 |
| overview-50000 | 8.57 / 10.26 | 8.56 / 10.40 | 30.01 | 332.7 → 332.8 | 800.2 → 800.2 |

Memory is measured Unity tracked allocation/reservation, not total VRAM. Stress cases retain the requested 50K and 1M capacities. Base-close uses 50K capacity with the normal eight starting units; it is a base-rendering case rather than a populated 50K army.

The frame target is 16.67 ms p95. Any cases above it remain unmet. Simulation GPU scope timings and complete fog timings are unavailable in these standalone runs, so the ≤5% army simulation regression and ≤3 ms total fog targets cannot be certified. All 45 compiled non-building army kernels remain byte-identical to the committed baseline; that supports architectural preservation but does not replace measurements.

Presented queue runs were collected separately. A hidden window did not provide a valid screenshot/render proof, so those frame results are excluded from acceptance. GPU-fenced frame numbers above exclude screen-space UI.

Asset acceptance: 312 model/runtime checks, all 31 types accepted by normal GPU spawning, and 844 grid/gate checks on each of the 4096 m and 16384 m worlds. All 120 shader stages compile. Every model is inspected in the complete three-sheet gallery. Exact raw run metadata and comparisons are in `TestResults/ArtPass/comparison.json`.
