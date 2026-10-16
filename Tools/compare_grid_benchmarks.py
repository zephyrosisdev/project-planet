"""Aggregate three-run grid benchmarks without treating unavailable GPU timings as zero."""
import json
import statistics
from pathlib import Path

root = Path('TestResults/GridPlacement')
reports = {}
for label in ('baseline', 'current'):
    reports[label] = json.loads((root / label / 'summary.json').read_text())

def aggregate(rows):
    keys = ('medianFrameMs', 'p95FrameMs', 'simulationHz',
            'p95SimulationGpuMs', 'p95FogGpuMs', 'peakAllocatedBytes',
            'peakReservedBytes', 'medianAlive')
    result = {key: statistics.median([r[key] for r in rows])
              if all(r.get(key) is not None for r in rows) else None
              for key in keys}
    result['runs'] = len(rows)
    result['renderVerified'] = all(r['renderVerified'] for r in rows)
    return result

cases = sorted({Path(r['path']).parent.name.rsplit('-', 1)[0]
                for r in reports['baseline']})
comparison = []
for case in cases:
    groups = {label: [r for r in reports[label]
                      if Path(r['path']).parent.name.rsplit('-', 1)[0] == case]
              for label in reports}
    if any(len(rows) != 3 for rows in groups.values()):
        raise ValueError(f'{case}: expected three runs per build')
    equivalent = []
    for before, after in zip(sorted(groups['baseline'], key=lambda r: r['path']),
                             sorted(groups['current'], key=lambda r: r['path'])):
        a = json.loads(Path(before['path']).with_suffix('.json').read_text())
        b = json.loads(Path(after['path']).with_suffix('.json').read_text())
        equivalent.append({key: a.get(key) == b.get(key) for key in
                           ('settings', 'width', 'height', 'orthographic',
                            'orthographicHalfHeight', 'cameraPosition', 'cameraAngles')})
    a, b = aggregate(groups['baseline']), aggregate(groups['current'])
    delta = {key: (b[key] / a[key] - 1) * 100
             if a[key] is not None and b[key] is not None and a[key] != 0 else None
             for key in ('medianFrameMs', 'p95FrameMs', 'p95SimulationGpuMs')}
    comparison.append(dict(case=case, baseline=a, current=b, changePercent=delta,
                           equivalence=equivalent,
                           armyRegressionPass=delta['p95SimulationGpuMs'] <= 5
                           if delta['p95SimulationGpuMs'] is not None and
                           all(all(row.values()) for row in equivalent) else None))
result = dict(cases=comparison, note='Off-screen GPU-fenced frames exclude screen-space UI. '
              'Memory is measured Unity tracked allocated/reserved memory, not total VRAM. '
              'Unavailable simulation/fog scopes do not establish acceptance.')
(root / 'comparison.json').write_text(json.dumps(result, indent=2))
print(json.dumps(result, indent=2))
