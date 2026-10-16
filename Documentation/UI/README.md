# Synthetic Front UI

Runtime presentation uses one screen-space Unity 6000.6 PanelRenderer with shared Front, CatalogRow and ResourceCell UXML templates and USS, Rajdhani headings and Inter text. UI uses existing BattleApp/RtsWorld actions and aggregate GPU reductions; army state, dispatch order, shaders and the original off-screen benchmark remain unchanged. Opt-in profiling scopes were added around existing fog work.

## Artwork

The Resources/MassiveArmy/UI asset catalogue contains all 49 building portraits, 31 unit portraits and 25 technology vectors. Runtime portraits are transparent 512×512 cutouts. Original generated images and exact imagegen prompts are preserved in PortraitSources. Every image was visually reviewed in seven contact sheets. Art provenance is recorded in art-catalogue.json. The emblem and 60 vector icons are original geometry. ControlHighlight is a small baked procedural highlight; no blur passes or animated effects are used.

Rajdhani SemiBold/Bold and Inter Regular/Medium sources and SIL OFL licenses are bundled under Assets/MassiveArmy/Resources/MassiveArmy/UI/Fonts, with four TextCore SDF assets. Inter static sources were instantiated from the bundled official variable font. Source references: https://github.com/google/fonts/tree/main/ofl/rajdhani and https://github.com/rsms/inter.

## Layout and input

Logical reference 1920×1080 with uniform Expand scaling, scrollable side panels and modals, and saved UI scale 85–125%. Construction and building details close independently. Numbers update at 10 Hz, visible progress at 30 Hz and minimap fog at 4 Hz. The construction catalogue recycles ListView rows, production/research rows reuse a bounded pool, and selected controls rebuild when selection/queue/availability changes. All five resources, power, battery, tier, contacts, performance and existing action explanations are retained.

The pointer router retains ordered Input System transitions, including multiple changes per frame. It temporarily disables redundant event merging and restores the previous setting on disposal. Actual UI geometry blocks world gestures and wheel zoom. Mouse reset/removal notifications clear cached button transitions and cancel the owning gameplay gesture. Modals and text entry suppress gameplay shortcuts; focus loss, modal opening and teardown cancel active pointer gestures. Settings restore the prior paused/running state.

## Reproduction

Use Massive Army → UI → Build UI Assets, then Validate UI and Input in Edit mode. Validate All Editor Screens runs the shared 24-screen walkthrough and input checks; standalone adds the twelve resolution/scale captures. Validation failure gates the standalone UI build. Use RtsUiValidation.BuildPlayer for an isolated Windows build.

Opt-in standalone paths: -ui-gallery -benchmark-output <folder> captures the screens and layout matrix and executes interaction checks. -ui-benchmark <capacity> runs a presented 1440p companion to the retained -army-benchmark off-screen path. run_benchmarks.ps1 records three runs for each of six scenarios; summarize.py calculates median/p95, simulation rate, tracked memory and GPU scopes. Stress worlds use 4,096 m for 50K and 16,384 m for 1M, with seed 73129, full render scale, DX11, VSync off. No army scale is reduced. Base scenarios retain the existing initial units and stress slot capacity; populated queue fixtures create facilities through normal structure APIs.

Measured results and the final screen gallery are documented in acceptance.md. Invalid hidden-window, unsupported small-map, one mismatched-camera repeat and incomplete fog diagnostics are excluded.

The container is explicitly stretched on reload; actual geometry checks prevent zero-height Editor containers or compressed panels from being accepted.

PanelRenderer lifecycle follows the installed versioned reload callback API; see [Unity 6000.6 Panel Renderer documentation](https://docs.unity.com/en-us/engine/6000.6/manual/uitoolkits/uielements/uie-support-for-runtime-ui/uie-render-runtime-ui/panel-renderer-component). Fog telemetry uses opt-in scopes only; dispatch order, shaders and army simulation are preserved.

Total fog measurement: -benchmark-fog-total with -ui-benchmark enables GPU intel/contact scopes, CPU fog-pass preparation, CPU fog readback and minimap uploads. total-fog.csv reports these alongside the existing GPU mask/wake/raymarch/composite scope. The report also charges the entire shared GPU visibility and forest-culling scopes conservatively because they contain fog-dependent culling. Adding GPU and CPU durations is conservative because some work overlaps. The ordinary comparison runs do not enable these additional scopes.

The project-owned FrontTheme.tss explicitly imports Unity’s default runtime theme. Scroll content and fixed-height rows retain natural size. Screen assertions verify slider thumbs, checkboxes, queue rows, graph height and an actual power deficit in Editor and player. The low-power fixture disables generation, drains stored battery and advances through the economy update interval.
