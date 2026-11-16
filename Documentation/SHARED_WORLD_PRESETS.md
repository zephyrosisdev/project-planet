# Shared survival world settings

New offline and dedicated worlds use `DefaultWorldPreset.asset` under
`Assets/MassiveArmy/Resources/MassiveArmy/Planets`. Its home planet, system and
galaxy references contain the authored generation, atmosphere and cloud settings.
Dedicated generation no longer replaces the home planet's terrain, ocean,
gravity or atmosphere with hardcoded values. Other planets still use the shared
procedural distribution and weather variation; they do not all copy home weather.

Select the preset in Unity, edit its referenced definitions, and increment its
revision when changing the intended world. Rebuild both client and server to ship
new defaults. Main-menu Single Player uses this preset; a custom editor
`PlanetDevelopment` scene can opt in by assigning its **World Preset** field.
Scenes without a preset retain their existing explicit setup.

For a server-only configuration change, use the preset component's context menu
**Export world preset JSON**, then set `worldPresetPath` in `server.json` to that
file. Relative paths resolve beside the server configuration. An empty path uses
the built-in preset. Each build also includes `world-preset.example.json` with
its complete captured settings. Keep this JSON self-contained; Unity instance
references are rejected. No credentials belong in a preset.

`systemSizeMultiplier: 0` inherits the preset's system size multiplier; 1–1000
explicitly overrides it for a **new** world. The current default is 100: home's
authored 120 km radius becomes 12,000 km. Radius and system distances scale;
terrain relief, atmosphere height and cloud height/thickness stay in metres.
Gravity scales to preserve the authored surface acceleration. The same preset,
seed and resolved multiplier produce the same planet definitions in both modes.
The server sends those resolved definitions to clients.

## Safe larger-world testing

In the packaged server, run `RunServer-LargeWorld.bat`. This defaults to 100x and
creates `server.large-100.json` with distinct world/instance IDs and an automatic
separate save directory. It leaves `server.json` and its save alone. For example:

```powershell
.\RunServer-LargeWorld.bat -Multiplier 20
```

The client includes `RunSinglePlayer-LargeWorld.bat`, which opens the game with
a separate `large-test-100` offline slot at 100x. Choose **Single Player** as usual.
Offline and server worlds have separate saves even with identical generation.

Existing worlds load their recorded planet definitions. Changing a preset or
multiplier does **not** resize a saved planet, relocate buildings or reset players.
This includes the existing friends server's 1x world: use the large-world
launcher to test 100x. Restarting that test world also preserves its first settings.
Choose new IDs/slot for another generation rather than editing a populated save.
Do not delete your original world to apply a scale change.

Offline saves now record definitions and versioned preset metadata through the
existing industrial checkpoint/journal. Older offline saves did not store those
definitions; their first load captures the selected settings and logs a warning.
There is no reliable way to reconstruct previously unsaved editor settings, and
existing object coordinates are not migrated. Back up such saves and keep their
original generation settings, or use a separate new slot.

Large-world frame time, terrain/contact agreement and two-person flight remain
playtest gates. No 60 FPS or 100-player capacity result is inferred from preset
generation checks.

