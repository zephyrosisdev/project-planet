# Survival crafting UI restoration

Multiplayer previously exposed the full industrial recipe catalogue as a flat
list of identifiers with a single craft button. It now uses the existing offline
Industry stylesheet and shared recipe names/categories for a retained UI Toolkit
browser. Offline crafting keeps its existing interaction model, with the shared
categories also exposing vehicle and logistics entries consistently.

Press **F4** or choose crafting from inventory. Search by display name or ID,
filter by category or available ingredients, inspect outputs and required inputs,
and choose a batch of 1–20 with quantity, plus/minus and MAX controls. MAX accounts
for the 32-job queue, ingredient stock, physical inventory capacity and reserved
output slots. These previews never change a client inventory. The server still
validates and journals every craft/cancellation command.

The bottom queue shows active authoritative progress, waiting jobs and individual
cancellation/refund actions. Work continues while menus are closed and follows
server simulation time. Empty inline jobs reconstructed by Unity JSON are removed
at snapshot normalization for both regular packets and chunked private state;
real and migrated legacy jobs are preserved.

**CRAFT AT** lists nearby owned production machines within the existing 12 m
interaction range. Choosing one filters the catalogue to supported recipes and
shows its input stock, current batch, status, water and available power. The
server rejects recipe changes while a batch or reservation is active. Manage
input/output through the slot inventory and toggle production from the details.
Machine power/water costs describe machine production; hand crafting retains its
existing resource/queue rules. Moving out of range or losing access removes the
machine selection. Existing construction, logistics, ship and respawn controls
remain accessible through **CONSTRUCTION / MACHINES / SHIPS** and **BACK TO
CRAFTING**. No client-side save or creative-mode authority is introduced.

Search/quantity fields capture gameplay shortcuts and prevent Enter from opening
chat while editing. Crafting menus suppress gameplay camera/input through the
existing multiplayer menu gate and do not pause simulation.

Validation: all five conditional compilation roles, 338 native headless checks
and 282 production domain checks passed. The native cases cover filtering,
quantity previews, queue limits, category parity, machine-only recipe restrictions,
stable cancellation identities and JSON null-job normalization. Detached native
UI elements have no panel event dispatcher; graphical standalone probes validate
attached-panel callbacks separately. Native checks do not establish real keyboard
input, visual acceptance, gameplay capacity or frame-time acceptance.

No server simulation cadence or industrial production algorithm changed. Broad
60 FPS and 100-player acceptance remain unmeasured; RTS remains retired.

Standalone Relay run `TestResults/Multiplayer/Live/409c07293d5e` passed with two
graphical clients and a dedicated server on a newly generated 100x world (192
planet definitions). Both clients filtered to the solar-panel recipe, dispatched
pointer clicks on visible controls, checked typed quantity clamping at 20, queued
two jobs through the server, cancelled both and verified all stone was refunded.
Both then passed inventory splitting, selection, drop/recovery and conservation.
The diagnostic driver waits for finite layout bounds and confirms the panel hit
target before clicking; queue entries are cancelled after authoritative promotion
to the visible active row. Initial driver failures from detached controls,
offscreen navigation and queue reflow are superseded by this passing run.

Delivery is `Builds/Playtest-20261115-200425`, protocol 13/content `survival-5`.
The ordinary server launcher retains `friends-survival-100x / friends-100x` and
100x scale. Source defaults, exported presets and both standalone build roles use
100x; populated worlds still restore their stored definitions. All 235 client
and 236 server archive entries and checksum files were verified. See
`TestResults/Multiplayer/crafting-package-checks.json` and `crafting-validation.json`.
