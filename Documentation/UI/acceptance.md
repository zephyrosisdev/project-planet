# Synthetic Front UI acceptance results

Implemented runtime UI replacement and complete artwork coverage. The 60 FPS target remains unchanged; the million-unit stress scenario does not meet it. No army scale was reduced.

## Implementation and visual review

One Unity 6000.6.4f1 screen-space PanelRenderer explicitly stretches its container to fill the panel, including Editor reloads, and hosts shared Front, CatalogRow and ResourceCell UXML templates and USS tokens. The project-owned FrontTheme.tss explicitly imports Unity’s default runtime theme; scroll content and fixed cards retain their natural size in both Editor and player. All runtime IMGUI and UnityEngine.Event gesture dependencies have been removed. BattleApp and existing RtsWorld/GPU action APIs retain simulation ownership. Army state, dispatch algorithms, shaders, ArmyRenderer and the original off-screen harness remain unchanged; source hashes are in simulation-source-proof.json. Optional profiling scopes surround existing fog work.

The catalogue covers 49 buildings, 31 units and 25 technologies. All 80 portraits were generated with the built-in imagegen workflow, visually inspected, packaged as transparent 512×512 assets, and mapped by existing definition type/ID. Original images and exact prompts are in PortraitSources; provenance and coverage are in art-catalogue.json. There are 60 original SVG icons including the angular emblem. Rajdhani SemiBold/Bold and Inter Regular/Medium sources, SIL OFL licenses and four TextCore SDF assets are bundled.

The final [screen gallery](../../TestResults/UiRedesign/gallery.html) contains 36 standalone captures and a 24-screen Editor walkthrough. It includes menus, setup/preview, loading/cancellation/failure, settings, battle, construction, hostile/removed buildings, production/research queues, completed research, technology graph, atlas, resource tooltip, scout/transport controls, swarm warning, victory/defeat and developer benchmark controls. Twelve layout captures cover 1920×1080, 1280×720, 3440×1440 and 2560×1440 at 85%, 100% and 125%. Actual client sizes were verified in gallery-final/resolutions.txt. All screens and all seven portrait contact sheets were inspected.

Side panels close independently; logical uniform scaling preserves artwork aspect ratios. Construction rows are reused by ListView; production/research rows use a bounded pool under the existing 64-order limit, preserving the pressed order across rebinding. Cached labels update at 10 Hz, visible progress at 30 Hz and minimap fog at 4 Hz. Small baked highlights provide active glow without blur passes.

## Functional verification

- Asset/ordered input validation: PASS; 180 checks.
- GPU gameplay controls: PASS; 102 checks.
- editor/interactions.json: PASS; 28 checks.
- editor/gestures.json: PASS; 37 checks.
- editor/text-focus.json: PASS; numeric text focus.
- editor/formation.json: PASS; GPU preview and distinct selected-unit destinations.
- editor/queue-pointer.json: PASS; safe pointer release across queue-row rebinding.
- editor-gallery/interactions.json: PASS; 28 checks.
- editor-gallery/gestures.json: PASS; 37 checks.
- editor-gallery/text-focus.json: PASS; numeric text focus.
- editor-gallery/formation.json: PASS; GPU preview and distinct selected-unit destinations.
- editor-gallery/queue-pointer.json: PASS; safe pointer release across queue-row rebinding.
- gallery-final/interactions.json: PASS; 28 checks.
- gallery-final/gestures.json: PASS; 37 checks.
- gallery-final/text-focus.json: PASS; numeric text focus.
- gallery-final/formation.json: PASS; GPU preview and distinct selected-unit destinations.
- gallery-final/queue-pointer.json: PASS; safe pointer release across queue-row rebinding.
- editor-gallery actual layout geometry: 1 views × 7 checks; PASS.
- gallery-final actual layout geometry: 13 views × 7 checks; PASS.
- editor-gallery standard controls, scroll rows, graph and low-power state: 75 checks across 10 screens; PASS.
- gallery-final standard controls, scroll rows, graph and low-power state: 75 checks across 10 screens; PASS.
- Complete Editor screen walkthrough: PASS; 24 screens.

Checks exercise same-frame press/move/release ordering and modifiers, mouse reset/removal lifecycle and first post-reset clicks, independent side panels, queue cancellation/reordering/repeat, hostile/removed selection, action explanations, repair/power/upgrade cancellation, world gestures crossing HUD edges, middle-drag origins, UI wheel blocking, text-entry shortcut suppression, Escape priority, pre-modal pause restoration, focus/modal route cancellation, rally picking, Shift placement repetition, wall chains and wrapping seams, and minimap/atlas coordinates. Actual transport/scout GPU behavior is additionally covered by the 102-check combined-arms validation. Validation readbacks are bounded diagnostic samples; no CPU unit mirror or per-unit UI/GameObjects were added. Repeated base/swarm transitions and cancellation/failure are exercised by the standalone gallery fixture.

## Presented performance

RTX 3070, Windows 11, Direct3D11, Unity 6000.6.4f1 development players with GPU profiling; 2560×1440, render scale 100%, VSync off, uncapped presentation, seed 73129. Each run warms for five seconds and records twenty seconds. Three runs per build/scenario, with identical settings, camera poses and fixture preparation; each table entry is the median of the three per-run statistics. Compilation and other owned validation processes were stopped during recording. The presented matrix was rerun from the fullscreen-container build, and both affected populated HUD cases were then repeated three times after queue-row pooling and canonical runtime-theme validation. The other cases contain no queue rows and retain the same timed rendering and simulation paths; gallery geometry assertions guard actual panel dimensions and surfaces. One baseline 50K repeat with a different camera pose was replaced by an equivalent repeat. Earlier fog diagnostics missing the CPU preparation scope are excluded; final supplemental runs use that scope in both builds. Raw CSV, settings and measured memory are retained in [TestResults/UiRedesign](../../TestResults/UiRedesign/).

Stress scenarios contain 50,000 or 1,000,000 active units. They use 4,096 m and 16,384 m worlds respectively to preserve supported deployment. Base scenarios use 50K slot capacity and the existing eight starting units; populated HUD fixtures create barracks/laboratory through normal structure APIs and enqueue 24 infantry orders plus available research. Those two facilities are paused to keep a stable populated queue throughout recording; the battle simulation continues at its normal tick rate. Active queue progression and research completion are covered by the functional walkthrough, not claimed as this fixture’s workload. Stress-mode battles do not enable the base-game fog renderer. This distinction is retained in the settings files.

| Scenario | Baseline median / p95 ms | Redesign median / p95 ms | Baseline / redesign ticks/s | Settings equivalent |
|---|---:|---:|---:|:---:|
| overview-50000 | 7.02 / 9.32 | 7.03 / 8.64 | 29.74 / 29.85 | True |
| overview-1000000 | 25.25 / 27.99 | 25.23 / 27.84 | 29.73 / 29.79 | True |
| base-50000 | 6.76 / 7.75 | 6.52 / 7.79 | 29.90 / 29.91 | True |
| base-close-50000 | 6.60 / 7.59 | 6.33 / 7.35 | 29.80 / 29.90 | True |
| base-queue-50000 | 6.66 / 7.31 | 6.28 / 7.38 | 29.79 / 29.90 | True |
| base-research-50000 | 6.66 / 7.58 | 6.28 / 7.04 | 29.80 / 29.89 | True |

| Scenario | Simulation GPU p95 baseline / redesign ms | Change | Reject >5% regression |
|---|---:|---:|:---:|
| overview-50000 | 1.72 / 1.56 | -9.04% | PASS |
| overview-1000000 | 20.02 / 19.84 | -0.87% | PASS |
| base-50000 | 0.85 / 0.85 | 0.17% | PASS |
| base-close-50000 | 1.06 / 0.85 | -19.46% | PASS |
| base-queue-50000 | 0.85 / 0.85 | 0.46% | PASS |
| base-research-50000 | 0.98 / 0.84 | -13.72% | PASS |

Simulation GPU cost sums the existing army simulation scopes, excluding rendering and fog. It is reported separately from end-to-end frame time and tick rate. The render frame distribution can change while 30 Hz simulation remains intact, so the million-unit median alone must not be interpreted as stable 60 FPS. The p95 frame acceptance limit is 16.67 ms. The million-unit p95 remains above that limit in both builds; it is an unmet acceptance target, not an accepted reduction in scale.

| Scenario | Measured allocated MiB baseline / redesign | Measured reserved MiB baseline / redesign |
|---|---:|---:|
| overview-50000 | 335.8 / 342.4 | 800.2 / 800.2 |
| overview-1000000 | 338.7 / 344.9 | 808.2 / 808.2 |
| base-50000 | 336.9 / 345.8 | 800.2 / 800.2 |
| base-close-50000 | 337.8 / 347.7 | 800.2 / 800.2 |
| base-queue-50000 | 337.3 / 349.7 | 800.2 / 800.2 |
| base-research-50000 | 338.0 / 349.2 | 800.2 / 800.2 |

These are measured Unity tracked peak allocated/reserved bytes, including startup and recording, and are not measured dedicated VRAM usage. Settings JSON also contains the existing estimated army/map buffer budget; that estimate is not substituted for measured memory. Physical GPU memory capacity is hardware metadata.

## Total fog measurement

Supplemental presented base-overview runs enable the same opt-in profiling scopes in both players, with three runs each. The sum includes GPU mask/wake/raymarch/composite, GPU intel/contact collection, CPU fog-pass preparation, CPU visibility readback and CPU minimap uploads. The full shared Army.Visibility and Army.ForestVisibility GPU scopes are also charged conservatively because they include fog-dependent culling; their complete camera-culling cost is included rather than estimating the incremental fog test. CPU and GPU work can overlap, so this sum is conservative. The total is sampled per presented frame; 4 Hz work appears in fewer frames at uncapped presentation, so p99 is included to expose its spikes. Fog timing here is measured in the base overview with eight starting units, not a claim about a fully populated million-unit fog battle.

| Build | Total fog p95 ms | Total fog p99 ms | All components measured | p95 ≤3 ms |
|---|---:|---:|:---:|:---:|
| baseline | 1.74 | 1.97 | True | PASS |
| current | 1.66 | 1.92 | True | PASS |

Component CSV and raw per-run statistics are in fog-total and performance.json. total-fog-inclusive.csv combines the pipeline samples with the nearest frame’s measured shared culling scopes; the original pipeline-only total-fog.csv is preserved. Army.Fog in the ordinary comparison CSV is the GPU draw path alone and must not be presented as total fog cost.

## Retained off-screen benchmark

The existing -army-benchmark path uses a 2560×1440 explicit HDRP target and GPU completion fence. It remains available alongside -ui-benchmark, which captures real presented screen-space UI. One baseline and redesigned off-screen check at each stress size verifies the retained path; these companion checks do not replace the three-run presented comparison.

| Run | Median / p95 ms | Ticks/s | Measured allocated / reserved MiB | Rendered frames |
|---|---:|---:|---:|---:|
| baseline-1000000-1 | 27.62 / 29.55 | 30.00 | 307.4 / 808.2 | 1171 |
| baseline-50000-1 | 8.63 / 10.81 | 30.00 | 306.2 / 544.1 | 2779 |
| current-1000000-1 | 27.19 / 28.84 | 30.00 | 333.1 / 808.2 | 1199 |
| current-50000-1 | 8.58 / 10.29 | 29.99 | 330.7 / 800.2 | 2810 |

## Reproduction and limitations

Build with RtsUiValidation.BuildPlayer, then use run_benchmarks.ps1, run_supplemental.ps1 and summarize.py under TestResults/UiRedesign. The gallery uses -ui-gallery and executes the screen/input fixtures before capturing the layout matrix. Editor verification is available through Massive Army → UI → Validate Presented Editor UI. Build validation gates missing artwork, fonts, templates and runtime IMGUI.

Battlefield models and terrain art remain outside this pass. Existing user terrain changes are preserved. Hidden-window presented runs and unsupported small-map million-unit trials were excluded; the off-screen path instead uses an explicit render fence. The benchmark does not claim release-build timings, total dedicated VRAM usage or a million-unit fog pass. The 60 FPS and million-unit targets remain unchanged.
