import csv, json, math, statistics
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'TestResults' / 'AiAccess'
army = ['Army.SpatialGrid', 'Army.Navigation', 'Army.CrowdPressure',
        'Army.MovementAvoidance', 'Army.ContactGrid', 'Army.ContactResolution',
        'Army.TargetAcquisition', 'Army.Combat', 'Army.Projectiles']

def p95(values):
    return sorted(values)[max(0, math.ceil(len(values) * .95) - 1)]

def measure(folder):
    meta = json.loads((folder / 'overview.json').read_text())
    memory = json.loads((folder / 'overview-memory.json').read_text())
    with (folder / 'overview.csv').open(newline='') as stream:
        rows = list(csv.DictReader(stream))
    assert meta['width'] == 2560 and meta['height'] == 1440
    assert meta['gpu'] == 'NVIDIA GeForce RTX 3070'
    frames = [float(r['frame_s']) * 1000 for r in rows]
    steps, draw = [], []
    previous = int(rows[0]['tick'])
    for row in rows[1:]:
        tick = int(row['tick'])
        (steps if tick > previous else draw).append(float(row['frame_s']) * 1000)
        previous = tick
    gpu = [sum(float(r.get(k, 0)) for k in army) for r in rows]
    fog = [float(r.get('Army.Fog', 0)) for r in rows]
    return dict(medianMs=statistics.median(frames), p95Ms=p95(frames),
                meanMs=statistics.mean(frames),
                simulationFrameMedianMs=statistics.median(steps),
                simulationHz=(int(rows[-1]['tick']) - int(rows[0]['tick'])) /
                (float(rows[-1]['elapsed_s']) - float(rows[0]['elapsed_s'])),
                alive=statistics.median(int(r['alive']) for r in rows),
                allocatedMiB=memory['peakAllocatedBytes'] / 1048576,
                reservedMiB=memory['peakReservedBytes'] / 1048576,
                armyGpuP95Ms=p95(gpu) if max(gpu) > 0 else None,
                fogGpuP95Ms=p95(fog) if max(fog) > 0 else None), meta

cases, metadata, changes = {}, {}, {}
for prefix, count in (('', 50000), ('', 1000000), ('confirm-', 50000)):
    if prefix and not (root / 'Benchmarks' / 'confirm-current-50000-3' / 'overview.csv').exists():
        continue
    for label in ('baseline', 'current'):
        key = f'{prefix}{label}-{count}'
        runs, metadata[key] = [], []
        for repeat in range(1, 4):
            run, meta = measure(root / 'Benchmarks' / f'{key}-{repeat}')
            assert run['alive'] == count
            runs.append(run)
            metadata[key].append(meta)
        cases[key] = dict(runs=runs, summary={
            k: statistics.median(r[k] for r in runs)
            if all(r[k] is not None for r in runs) else None for k in runs[0]})
    for before, after in zip(metadata[f'{prefix}baseline-{count}'], metadata[f'{prefix}current-{count}']):
        assert before['settings'] == after['settings'], 'Scenario settings differ'
        for k in ('width', 'height', 'cameraPosition', 'cameraAngles', 'orthographicHalfHeight'):
            assert before[k] == after[k], f'{k} differs'
    a, b = cases[f'{prefix}baseline-{count}']['summary'], cases[f'{prefix}current-{count}']['summary']
    changes[f'{prefix}{count}'] = dict(frameP95Percent=(b['p95Ms'] / a['p95Ms'] - 1) * 100,
                          simulationFrameMedianPercent=(b['simulationFrameMedianMs'] /
                                                        a['simulationFrameMedianMs'] - 1) * 100)

lines = ['# AI objectives and compound access: validation and performance', '',
         'Unity 6000.6.4f1, RTX 3070, Windows D3D11, 2560×1440. Three runs per build and army size, paired baseline/current. Frozen pre-fix AI-test build compared with the corrected build. Settings, seeds, scenario preparation and camera are asserted identical; populations remain 50,000 and 1,000,000. Normal Burst configuration. GPU-fenced off-screen HDRP capture excludes screen-space UI.', '',
         '| Case | Median ms | p95 ms | Sim Hz | Allocated MiB | Reserved MiB |',
         '|---|---:|---:|---:|---:|---:|']
for key, case in cases.items():
    s = case['summary']
    lines.append(f'| {key} | {s["medianMs"]:.3f} | {s["p95Ms"]:.3f} | {s["simulationHz"]:.3f} | {s["allocatedMiB"]:.2f} | {s["reservedMiB"]:.2f} |')
lines += ['', 'Measured memory is Unity allocation/reservation, not total VRAM or an estimated budget. Army GPU and total-fog scopes are unavailable in this harness; the army 5% and fog 3ms acceptance limits cannot be certified from zero-valued scope columns. These comparisons use frame proxies.']
for count, delta in changes.items():
    s = cases[count.replace('confirm-', 'confirm-current-') if count.startswith('confirm-') else f'current-{count}']['summary']
    lines.append(f'{count} units: frame p95 {delta["frameP95Percent"]:+.2f}%; simulation-step frame median {delta["simulationFrameMedianPercent"]:+.2f}%. 60 FPS target: {"met" if s["p95Ms"] <= 16.67 else "UNMET"}.')
if 'confirm-50000' in changes:
    lines += ['', 'The 50K case was repeated for both builds because the initial frame-p95 proxy exceeded 5% and had substantial run-to-run variation, particularly in draw-only frames. Both complete datasets are retained above. A repeat does not erase the initial observation or supply missing GPU army/fog timings.']
lines += ['', 'Raw frame medians are retained; 30Hz simulation produces a bimodal mix of simulation-step and draw-only frames. No army scale or tick rate was lowered.', '',
          'Editor access fixture: persistent objectives, complete perimeter around existing buildings, production exit rejection, four traversable owner gates, repeated order-sequence stability, 16/16 vehicles escaping the compound, and arrived-slot preservation. Regular Red individual rally orders also pass GPU travelling/arrival-counter checks, objective persistence while travelling, and exploration advancement after arrival and the hold period. The accelerated two-AI economy fixture reaches tier 5 on both factions, builds walls/gates/outposts and replaces exhausted mines using normal costs; it is not a balance-duration test. Windows fixture checks default 100K zombies, spectator visibility/input isolation, snapshots and repeated mode transitions. Exact reports accompany this file.', '',
          'Non-base stress movement remains unchanged. Base-building spawn fallback now checks the exact foundation grid; no CPU army mirror or per-unit objects were added. Base-layout flood fills run only for construction planning, with bounded local extents, and use immutable terrain plus existing building occupancy. These stress comparisons do not certify the cost of a mature, heavily populated AI base.']
(root / 'comparison.json').write_text(json.dumps(dict(cases=cases, changes=changes), indent=2))
(root / 'RESULTS.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
print('\n'.join(lines))
