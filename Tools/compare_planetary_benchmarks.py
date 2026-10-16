"""Compare fresh equivalent standalone flat runs before promoting shared shader changes."""
import csv
import json
import statistics
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "TestResults" / "Planetary" / "Benchmarks"

def percentile(values, p=.95):
    values = sorted(values)
    i = (len(values) - 1) * p
    a = int(i)
    return values[a] + (values[min(a + 1, len(values) - 1)] - values[a]) * (i - a)

def read(label, count):
    path = ROOT / f"{label}-{count}" / "overview.csv"
    rows = list(csv.DictReader(path.open()))
    info = json.loads(path.with_suffix(".json").read_text())
    memory = json.loads(path.with_name("overview-memory.json").read_text())
    frames = [float(row["frame_s"]) * 1000 for row in rows]
    scopes = [sum(float(row.get(key, 0)) for key in (
        "Army.SpatialGrid", "Army.Navigation", "Army.CrowdPressure",
        "Army.MovementAvoidance", "Army.ContactGrid", "Army.ContactResolution",
        "Army.TargetAcquisition", "Army.Combat", "Army.Projectiles")) for row in rows]
    fog = [float(row.get("Army.Fog", 0)) for row in rows]
    hz = (int(rows[-1]["tick"]) - int(rows[0]["tick"])) / (float(rows[-1]["elapsed_s"]) - float(rows[0]["elapsed_s"]))
    assert info["settings"]["activeUnits"] == count
    assert info["width"] == 2560 and info["height"] == 1440
    assert memory["renderedFrames"] >= len(rows)
    return info, dict(frames=len(rows), worldSize=info["settings"]["worldSize"],
        seed=info["settings"]["seed"], capacity=info["settings"]["capacity"],medianFrameMs=statistics.median(frames),
        p95FrameMs=percentile(frames), simulationHz=hz,
        p95SimulationGpuMs=percentile(scopes) if max(scopes) > 0 else None,
        p95FogGpuMs=percentile(fog) if max(fog) > 0 else None,
        peakAllocatedMiB=memory["peakAllocatedBytes"] / 1048576,
        peakReservedMiB=memory["peakReservedBytes"] / 1048576,
        aliveFirst=int(rows[0]["alive"]), aliveLast=int(rows[-1]["alive"]))

comparisons = []
for count in (50000, 1000000):
    if not (ROOT / f"current-{count}" / "overview.csv").exists():
        continue
    before_info, before = read("baseline", count)
    after_info, after = read("current", count)
    for key in ("settings", "width", "height", "cameraPosition", "cameraAngles", "cameraFov", "orthographic", "orthographicHalfHeight", "gpu", "api", "unity", "targetFrameRate"):
        assert before_info[key] == after_info[key], f"Not equivalent: {key}"
    regression = {key: (after[key] / before[key] - 1) * 100 for key in ("medianFrameMs", "p95FrameMs")}
    sim_regression = None
    if before["p95SimulationGpuMs"] and after["p95SimulationGpuMs"]:
        sim_regression = (after["p95SimulationGpuMs"] / before["p95SimulationGpuMs"] - 1) * 100
    comparisons.append(dict(units=count, baseline=before, current=after,
        frameChangePercent=regression, simulationGpuChangePercent=sim_regression,
        frameRegressionPass=max(regression.values()) <= 5,
        simulationGpuRegressionPass=None if sim_regression is None else sim_regression <= 5,
        target60FpsPass=after["p95FrameMs"] <= 1000 / 60,
        fogTargetPass=None if after["p95FogGpuMs"] is None else after["p95FogGpuMs"] <= 3))
report = dict(scenario="Standalone flat overview, shadows on, 2560x1440; five-second warmup and twenty-second sample",
    comparisons=comparisons, note="Memory is measured Unity tracked allocation/reservation, not total VRAM. Missing GPU/fog timings are unavailable. These runs assess preservation of flat simulation; they do not certify planetary army performance.")
ROOT.mkdir(parents=True, exist_ok=True)
(ROOT / "comparison.json").write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
