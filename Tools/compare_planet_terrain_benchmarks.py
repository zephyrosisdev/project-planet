import json
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'TestResults' / 'Planetary' / 'Terrain' / 'Benchmarks'
comparisons = []
for scenario in ('surface', 'orbit', 'fps'):
    paths = [root / f'{renderer}-{scenario}' / 'benchmark.json' for renderer in ('uniform', 'cbt')]
    if not all(path.exists() for path in paths):
        continue
    baseline, current = [json.loads(path.read_text()) for path in paths]
    for key in ('gpu', 'api', 'scenario', 'width', 'height', 'units', 'navResolution', 'seed', 'radius', 'errorPixels'):
        assert baseline[key] == current[key], f'Non-equivalent {scenario}: {key}'
    assert baseline['renderer'] == 'uniform' and current['renderer'] == 'implicit-CBT'
    assert baseline['width'] == 2560 and baseline['height'] == 1440
    changes = {key: (current[key] / baseline[key] - 1) * 100 for key in ('medianMs', 'p95Ms')}
    comparisons.append(dict(scenario=scenario, baseline=baseline, current=current,
        frameChangePercent=changes, withinFivePercent=max(changes.values()) <= 5,
        target60Fps=current['p95Ms'] <= 1000 / 60,
        detailSaturated=current['saturatedTriangles'] > 0))
report = dict(comparisons=comparisons,
    scope='Equivalent 32-unit opt-in terrain fixture; full-scale flat army simulation is unchanged. This is not a million-unit planetary or fog acceptance report.',
    memory='Tracked Unity allocation/reservation is measured; terrainBufferBytes is the exact allocated buffer budget, not a dedicated-VRAM measurement.')
root.mkdir(parents=True, exist_ok=True)
(root / 'comparison.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
