# Testing with accelerated time

Numpad **+ / −** selects **0.25×, 0.5×, 1×, 2×, 4×, 8×, 16×, 32×, 64×**. The existing flight HUD shows the selected rate. The rate starts at 1× and clamps at both ends.

Ship flight, planetary orbital time and weather time use the selected rate. This does not change Unity's global time scale; independent ground/player/industrial systems can still run on their own clocks.

Ship integration already subdivides accelerated frame time into steps of at most **50 ms**. The galaxy gravity integrator also applies its local dynamical step bound. Planet trajectories are prescribed Kepler orbits evaluated at the requested time. The new rates do not enlarge either integration bound or change orbital equations.

The result is suitable for fast orbit and rendering inspection, but is not identical to a 1× run: step sizes, control feedback and the number of rendered observations differ. At 60 rendered FPS, 64× advances approximately 1.07 simulated seconds per rendered frame and needs about 22 ship substeps. Use 1× for docking, landing, close collision tests and final physics comparisons. Moving colliders and visual effects do not all update at every flight substep.

Higher requested rates increase work per real second; full 64× throughput and 60 FPS are not guaranteed. Existing per-frame clock clamps and bounded catch-up can limit achieved acceleration during slow frames. No army populations, terrain budgets, simulation architecture or accuracy bounds were reduced to enable these testing rates.

Validation: runtime compilation and speed-ladder value/label/clamp checks. The existing settlement fixture's maximum-rate assertion was updated to 64×; its separate 8× throughput scenario remains unchanged. No standalone 64× accuracy or performance acceptance claim is made.
