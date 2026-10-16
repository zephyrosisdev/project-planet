# Project performance policy

Preserve the GPU-authoritative army architecture: no CPU unit mirror or per-unit
GameObjects. Performance takes priority over cosmetic fidelity and engine fashion.
Target 60 FPS at 2560x1440 on RTX 3070, with a preserved million-unit stress path.
This is an acceptance target, not a claim that all existing builds attain it.

Compare equivalent standalone scenarios before and after substantive simulation
changes. Report median/p95 frame time, simulation rate and measured memory.
Reject army-simulation regressions above 5% against an equivalent baseline and
target at most 3 ms p95 total fog cost. Reduce cosmetic cost before gameplay
correctness. Never silently reduce the target or army scale to report a pass.

If a future request conflicts with these priorities, explain the measured or
expected cost and explicitly ask the human whether to accept that tradeoff.
Sales or popularity alone are never grounds to replace an existing technique.
