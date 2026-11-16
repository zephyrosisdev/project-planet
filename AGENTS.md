# Survival direction and performance

The product is offline single-player survival and authoritative survival
multiplayer. On 2026-11-13 the user explicitly authorized completely retiring RTS.
Do not restore RTS/army game modes, army shader variants or million-unit stress
benchmarks. Those former acceptance gates no longer apply to this product.
Shared terrain, geometry and item definitions may still use legacy type names;
preserve their survival behavior when removing unused strategy dependencies.

Preserve GPU terrain/rendering and independent CPU headless contact queries.
Performance takes priority over cosmetic fidelity. Target 60 FPS at 2560x1440 on
RTX 3070 in survival; this is an acceptance target, not a measured pass.

Compare equivalent standalone survival scenarios around substantive simulation
changes. Report median/p95 frame time, server simulation rate, measured memory
and networking costs. Reduce cosmetic cost before gameplay correctness and never
claim an unmeasured performance pass.

The current delivery is a two-player playtest. The intended future 100-player
single-instance goal remains unvalidated and its capacity matrix is deferred by
the user. Cross-shard transfers and automatic provisioning remain future work.
