# Battle lighting and shadows

Open `Assets/Scenes/MassiveBattle.unity`. The **Battle lighting · edit sun and
global volume** hierarchy contains **Battle sun** and **Battle global volume**.
These are saved scene objects; gameplay uses them rather than creating another
sun or rewriting the profile. The old `BattleVisualSettings` daylight fields
are initialization defaults only; its fog-of-war controls remain live.

Select the global volume and edit its saved `BattleAtmosphereProfile`:
Exposure, Tonemapping, Bloom, Gradient Sky, Visual Environment, Shadows and
Water Rendering are available. Adjust the sun's rotation, colour, lux and
shadow controls directly in its Inspector. Changes made outside Play mode
persist. Play-mode object changes follow Unity's usual discard-on-exit rules.

The directional light is explicitly assigned to `RenderSettings.sun`. In HDRP,
ambient sky lighting comes from
the Volume's Visual Environment, set to **Dynamic** here. The gradient sky's
colours/exposure supply ambient light independently of sun angle. For a sky
that responds physically to the sun, change Sky Type to **Physically Based Sky**,
add that sky override and enable **Affect Physically Based Sky** on the sun.
The Lighting window's built-in skybox controls do not replace HDRP's Volume sky.

The RTS camera is at least 1,400m from the ground. HDRP's default 500m shadow
range cannot reach it. The saved Shadows override uses 2,600m, four cascades
with splits 0.45/0.62/0.80, and a 2048px sun shadow map with 0.1 normal bias.
These defaults were verified by rendering at the actual 1,400m camera distance;
both the default short range and the runtime off switch remove the shadow.
At extreme zoom-out, adjust the range/cascades if needed; far impostors do not
cast shadows, and scenery casts only at the existing close-view LOD.

**Settings > Realtime shadows** toggles camera shadow-map rendering and the sun.
Unit/scenery caster submission also follows this setting. **Save & Close** saves
the choice. The authored volume and its cascade settings are preserved.
Terrain uses HDRP Lit and receives shadows. Armour shaders keep their existing
stylized lighting: they cast onto the terrain but do not yet sample scene
shadows or ambient probes on their own surfaces.

Standalone comparisons should pass `-benchmark-shadows on` or
`-benchmark-shadows off`. Off is the harness default, independent of saved
preferences; the setting is included in the benchmark JSON. Keep the switch,
seed, camera, population and other settings equal when comparing simulation
changes. An on/off comparison measures the optional cosmetic cost. The capture
path excludes screen-space UI and unavailable fog/GPU scopes remain unavailable.

Use **Massive Army > Lighting > Create or Select Scene Lighting** for a missing
rig. It preserves an existing authored profile. Use **Massive Army > Art >
Validate Terrain Scene Lighting** to check sun direction/intensity/colour,
shadow range and on/off restoration.

Unity references: [HDRP environment lighting](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Environment-Lighting.html),
[shadow distance](https://docs.unity.com/en-us/engine/6000.5/manual/lighting-overview/shadows/shadow-realtime/shadow-distance).
