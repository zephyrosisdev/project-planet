"""Compare one matched, fully occupied headless window per diagnostic build."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def select_window(folder, population, rate):
    candidates = []
    for path in Path(folder).glob("industry.xml.network-metrics.window-*.json"):
        window = read(path)
        if (window["minimumPlayers"] == population == window["maximumPlayers"]
                and window["samples"] >= rate * 25
                and window["elapsedSeconds"] >= 25):
            candidates.append((window["window"], path, window))
    if not candidates:
        raise ValueError(f"No sufficiently sampled, fully occupied window in {folder}")
    _, path, window = max(candidates, key=lambda item: item[0])
    return path, window


def summarize(folder, result_path, population):
    result = read(ROOT / result_path)
    if result["status"] != "PASS" or result["clientCount"] != population:
        raise ValueError("Both live runs must pass with the requested population")
    if Path(result["resumedFrom"]).resolve() != Path(folder).resolve():
        raise ValueError("Live result does not refer to this comparison save")
    config = read(Path(folder) / "server.json")
    path, window = select_window(folder, population, config["tickRate"])
    phases = {phase["name"]: phase for phase in window["phases"]}
    snapshot = phases["Snapshot"]
    clients = [read(path) for path in result["reports"]]
    return {
        "windowReport": str(path), "liveResult": str(result_path),
        "window": window["window"], "ticks": window["samples"],
        "minimumPlayers": window["minimumPlayers"], "maximumPlayers": window["maximumPlayers"],
        "tickMedianMs": window["tickMedianMs"], "tickP95Ms": window["tickP95Ms"],
        "simulationTicksPerSecond": window["simulationTicksPerSecond"],
        "snapshotSamples": snapshot["samples"],
        "snapshotMeanMs": snapshot["totalMs"] / snapshot["samples"],
        "snapshotMedianMs": snapshot["medianMs"], "snapshotP95Ms": snapshot["p95Ms"],
        "industryP95Ms": phases["Industry"]["p95Ms"],
        "journalFlushP95Ms": phases["JournalFlush"]["p95Ms"],
        "sentApplicationBytesPerSecond": window["sentBytesPerSecond"],
        "receivedApplicationBytesPerSecond": window["receivedBytesPerSecond"],
        "managedBytes": window["managedBytes"], "unityAllocatedBytes": window["unityAllocatedBytes"],
        "windowsServerSessionPeakMemory": next(m for m in result["processMemory"] if m["role"] == "server"),
        "hardCorrections": sum(client["hardCorrections"] for client in clients),
        "profiles": sorted(client["profile"] for client in clients),
        "holdSeconds": result["holdSeconds"],
    }, config


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pair", default=ROOT / "TestResults/Multiplayer/profile-pair.json", type=Path)
    args = parser.parse_args()
    pair = read(args.pair)
    if pair["beforeCheckpointSha256"] != pair["afterCheckpointSha256"]:
        raise ValueError("Starting checkpoint hashes differ")
    before, before_config = summarize(pair["before"], pair["baselineResult"], pair["players"])
    after, after_config = summarize(pair["after"], pair["afterResult"], pair["players"])
    for key in ("worldId", "instanceId", "seed", "tickRate", "snapshotRate", "maxPlayers",
                "packetBytesPerSecond", "surfaceRadius", "shipRadius", "proxyRadius", "hysteresis", "walkSpeed"):
        if before_config[key] != after_config[key]:
            raise ValueError(f"Comparison configuration differs: {key}")
    if before["profiles"] != after["profiles"] or before["holdSeconds"] != after["holdSeconds"]:
        raise ValueError("Player profiles or occupied workload differ")
    report = {
        "scope": "One matched headless stationary fixture window per build; not full survival, army/fog, client frame-time or capacity acceptance. Timings are not repeated confidence intervals.",
        "startingCheckpointSha256": pair["beforeCheckpointSha256"],
        "baselineAssemblySha256": pair["baselineAssemblySha256"], "afterAssemblySha256": pair["afterAssemblySha256"],
        "before": before, "after": after,
        "tickP95ChangePercent": 100 * (after["tickP95Ms"] / before["tickP95Ms"] - 1),
        "snapshotMeanChangePercent": 100 * (after["snapshotMeanMs"] / before["snapshotMeanMs"] - 1),
        "fixtureTickP95BudgetMs": 1000 / after_config["tickRate"],
        "fixtureTickP95WithinBudget": after["tickP95Ms"] <= 1000 / after_config["tickRate"],
        "memoryScope": "Windows peaks span the entire diagnostic process, not only the selected occupied window.",
        "medianScope": "Staggering work can raise tick median while reducing p95; snapshot timings measure one publication across all clients.",
    }
    output = ROOT / "TestResults/Multiplayer/profile-comparison.json"
    output.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
