# Vehicle camera settings

Camera profiles are ScriptableObjects in `Assets/MassiveArmy/Resources/MassiveArmy/VehicleCameras/`.

| Camera | Asset / assignment |
|---|---|
| Spaceship pilot chair | `SpaceshipPilot.asset`. Optional override: PlanetDevelopment component → Spaceship Pilot Camera → Spacecraft Camera Settings. |
| Quad bike | `SF-QB1.asset` camera profile, referenced by the matching VehicleDefinition's Camera Settings field. |
| Jeep | `SF-LUV1.asset` camera profile, referenced by the matching VehicleDefinition's Camera Settings field. |
| Other registered ground vehicles | Matching SF-* camera profile referenced by their VehicleDefinition. |
| Industrial vehicles | `truck.asset`, `loader.asset`, `excavator.asset`, selected when entering that vehicle type. |

Select the **camera profile** under VehicleCameras, not a mesh asset with a similar name. Create additional profiles with Create → Synthetic Front → Vehicle Camera Settings, then assign them to a vehicle definition or the pilot override.

- **Near Clip Plane:** metres from the eye where geometry starts rendering. Defaults: pilot 0.1 m, ground vehicles 0.06 m.
- **Minimum Far Clip Plane:** lower bound for viewing distance, in metres. Pilot defaults to 100,000 m; ground profiles to 30,000 m.
- **Body Distance Multiplier:** actual far plane is `max(minimum, distance from active planet centre × multiplier)`. Pilot defaults to 6, increased from the previous hardcoded 3. Ground profiles retain 3. A pilot 50 km above a 1,200 km radius planet therefore has a 7,500 km far plane.
- **Mask Weather Inside Cabin:** pilot-only diagnostic switch. Normally enabled to prevent exterior haze inside sealed rooms. Temporarily disable it to compare the chair-only cloud/atmosphere flicker; haze may then appear inside the cabin. This does not disable cloud shadows or change weather sampling budgets.

After the scripts and assets have loaded, profile values are read every camera update, so editing a profile in Play mode takes effect without re-entering the vehicle. Profile asset edits persist beyond Play mode; scene-component overrides follow normal Unity scene serialization rules. Newly added scripts require a normal compile/restart of the existing Play session.

## Flicker diagnosis

The earlier quad disappearance was a fixed 30 km far plane while the surface was more than 49 km away. The pilot already used a planet-scale far plane. Clouds and atmosphere use a separate screen-space renderer whose projected bounds do not test the camera far plane, although scene depth can affect compositing. The angle-dependent pilot flicker is therefore **not confirmed** to share the quad cause. The cabin mask is another camera-dependent path to compare. Increasing range is implemented as requested, not presented as proof that the flicker is resolved.

Validation: full runtime C# compile passes; 32 camera range checks pass. Ten profile assets exist (pilot, three industrial types, six registered vehicle definitions). No live post-recompile flicker comparison or standalone performance acceptance measurement has been performed. Terrain residency/detail budgets and GPU army simulation are unchanged.
