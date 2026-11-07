# Planetary RTS controls and presentation

- `[` / `]`: cycle owned settlement planets and the physical player planet.
  The HUD identifies remote RTS views. First-person and ship control require
  returning to the physical planet; viewing a settlement does not teleport you.
- Right-click: move selected units. Hold right-click and drag: lay out a front
  line, then release to issue the formation. Columns and following rows are
  assigned on the GPU using movement-class spacing. Normal moves and Stop clear
  the formation. All commands retain the existing shared navigation fields.
- The celestial-orbit toggle displays the current system's ephemerides before
  a close survey. Cached ribbons stay visible while a round-robin update refreshes
  one path each frame.

RTS terrain refinement follows the ground focus. First-person terrain still
refines around the observer. Visibility uses the real camera in both cases;
the terrain patch budget and buffer sizes are unchanged.

Army and police shots feed a 2,048-record GPU tracer ring. New presentation work
is capped per tick and distant shots are omitted. There are no projectile or
unit GameObjects and no CPU army mirror. Gameplay damage remains authoritative
in the existing compute combat kernels.

Persistent nuclear paint retains its existing atlas. A fixed 32-entry recent
detail cache also samples the original crater art at local scale, so small marks
remain visible on Earth-sized planets where a global atlas texel is kilometres
wide. Older marks retain the atlas approximation.

See [COLONIES.md](COLONIES.md) for launch costs, neutral command-centre placement,
default colony automation, AI expansion and dormant economy updates.
