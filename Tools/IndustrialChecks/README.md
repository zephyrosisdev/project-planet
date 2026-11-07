# Industrial domain checks

From the repository root, run:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/DotNetSdk/dotnet.exe' run --project Tools/IndustrialChecks/IndustrialChecks.csproj
```

The checks compile the production domain sources directly against .NET 8. They
cover conservation, complete production chains, backpressure, ownership/replay
rejection, durable recovery, construction payment, vehicle payloads, shielding,
lethal exposure, and two real loopback TCP clients. The loopback portion requires
permission to open local sockets. It does not contact an external service.

Artifacts are written under `TestResults/Industrial`. This suite is not a Unity
Play Mode, rendering, physics, cloud multiplayer, or performance acceptance test.

For a graphics-enabled Unity player built from this project:

```text
SyntheticFront.exe -batchmode -force-d3d11 -industry-gpu-probe -industry-output <absolute-output-directory>
```

Do not pass `-nographics`. The opt-in probe checkpoints 4,096 GPU records, restores
them to a new buffer, compares every byte, writes `result.json`, and exits. That
small device/checkpoint proof does not replace the million-unit stress path or
the combined industrial/fleet acceptance scenario.
