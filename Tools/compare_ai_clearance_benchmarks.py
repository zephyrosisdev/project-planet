import csv, json, math, statistics
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'TestResults' / 'AiClearance'
army = ['Army.SpatialGrid', 'Army.Navigation', 'Army.CrowdPressure',
        'Army.MovementAvoidance', 'Army.ContactGrid', 'Army.ContactResolution',
        'Army.TargetAcquisition', 'Army.Combat', 'Army.Projectiles']

def p95(values):
    return sorted(values)[max(0, math.ceil(len(values) * .95) - 1)]

def measure(folder, scenario):
    meta = json.loads((folder / f'{scenario}.json').read_text())
    memory = json.loads((folder / f'{scenario}-memory.json').read_text())
    with (folder / f'{scenario}.csv').open(newline='') as stream:
        rows = list(csv.DictReader(stream))
    assert meta['width'] == 2560 and meta['height'] == 1440
    assert meta['gpu'] == 'NVIDIA GeForce RTX 3070'
    frames = [float(r['frame_s']) * 1000 for r in rows]
    steps, previous = [], int(rows[0]['tick'])
    for row in rows[1:]:
        tick = int(row['tick'])
        if tick > previous:
            steps.append(float(row['frame_s']) * 1000)
        previous = tick
    gpu = [sum(float(r.get(k, 0)) for k in army) for r in rows]
    fog = [float(r.get('Army.Fog', 0)) for r in rows]
    return dict(medianMs=statistics.median(frames), p95Ms=p95(frames),
                simulationFrameMedianMs=statistics.median(steps),
                simulationHz=(int(rows[-1]['tick']) - int(rows[0]['tick'])) /
                (float(rows[-1]['elapsed_s']) - float(rows[0]['elapsed_s'])),
                alive=statistics.median(int(r['alive']) for r in rows),
                allocatedMiB=memory['peakAllocatedBytes'] / 1048576,
                reservedMiB=memory['peakReservedBytes'] / 1048576,
                armyGpuP95Ms=p95(gpu) if max(gpu) > 0 else None,
                fogGpuP95Ms=p95(fog) if max(fog) > 0 else None), meta

cases, changes = {}, {}
for prefix, case, scenario, count in [('', '50k', 'overview', 50000), ('', 'million', 'overview', 1000000), ('', 'probe', 'ai-test', None), ('final-', 'probe', 'ai-test', None)]:
    if prefix and not (root / 'Benchmarks' / 'final-current-probe-3' / 'ai-test.csv').exists():
        continue
    metadata = {}
    for label in ('baseline', 'current'):
        key = f'{prefix}{label}-{case}'
        runs, metadata[label] = [], []
        for repeat in range(1, 4):
            folder = root / 'Benchmarks' / f'{key}-{repeat}'
            run, meta = measure(folder, scenario)
            if count is not None:
                assert run['alive'] == count
            if case == 'probe' and label == 'current':
                for team in range(2):
                    query = json.loads((folder / f'ai-area-{team}.json').read_text())
                    assert query['valid'] and query['cells'] > 0, 'Area probe did not execute'
            runs.append(run)
            metadata[label].append(meta)
        cases[key] = dict(runs=runs, summary={k: statistics.median(r[k] for r in runs)
                                            if all(r[k] is not None for r in runs) else None
                                            for k in runs[0]})
    for before, after in zip(metadata['baseline'], metadata['current']):
        assert before['settings'] == after['settings'], 'Scenario settings differ'
        for k in ('width', 'height', 'cameraPosition', 'cameraAngles', 'orthographicHalfHeight'):
            assert before[k] == after[k], f'{k} differs'
    a, b = cases[f'{prefix}baseline-{case}']['summary'], cases[f'{prefix}current-{case}']['summary']
    changes[f'{prefix}{case}'] = dict(frameP95Percent=(b['p95Ms'] / a['p95Ms'] - 1) * 100,
                         simulationFrameMedianPercent=(b['simulationFrameMedianMs'] /
                                                       a['simulationFrameMedianMs'] - 1) * 100)

lines = ['# AI outpost loss and clearance: validation and performance', '',
         'Unity 6000.6.4f1, RTX 3070, Windows D3D11, 2560x1440. Three paired baseline/current runs per case. Baseline is the frozen pre-clearance AiAccess player. Identical settings, seed, camera and scenario preparation are asserted. Normal Burst settings; off-screen GPU-fenced HDRP frames exclude screen-space UI.', '',
         '| Case | Median ms | p95 ms | Sim Hz | Alive | Allocated MiB | Reserved MiB |',
         '|---|---:|---:|---:|---:|---:|---:|']
for key, case in cases.items():
    s = case['summary']
    lines.append(f'| {key} | {s["medianMs"]:.3f} | {s["p95Ms"]:.3f} | {s["simulationHz"]:.3f} | {s["alive"]:,.0f} | {s["allocatedMiB"]:.2f} | {s["reservedMiB"]:.2f} |')
lines += ['', 'The probe case is an early AI-test scene with default 100K ambient zombies and both 200m area queries enabled at deterministic land positions beyond the starting refuges. Its CLI flag has no effect in the old baseline, enabling a comparison of the added local query work. It does not alter troop state or grant vision. Query reports prove both probes executed. It is not a mature failed-expansion balance test.', '',
          'Memory is measured Unity allocation/reservation, not total dedicated VRAM or estimated budgets. GPU army and total-fog scope recordings are unavailable, not zero-cost; their 5% and 3ms acceptance limits cannot be certified. Frame comparisons below are proxies.']
for case, delta in changes.items():
    s = cases[case.replace('final-', 'final-current-') if case.startswith('final-') else f'current-{case}']['summary']
    lines.append(f'{case}: frame p95 {delta["frameP95Percent"]:+.2f}%; simulation-step frame median {delta["simulationFrameMedianPercent"]:+.2f}%; 60 FPS target {"met" if s["p95Ms"] <= 16.67 else "UNMET"}.')
lines += ['', 'The full million-unit deployment and 30Hz simulation target are retained. Raw frame medians include the bimodal mixture of simulation-step and draw-only frames.', '',
          'Editor GPU fixture passes: paid outpost loss, no material debits from repeated expansion retries, offset exclusion, exact visible zombie/enemy/friendly counts, troop dispatch in both spectator and regular RTS, research progression while held, no objective oscillation, unknown-area rejection, sustained safe release, voluntary-sale exclusion, repeat-loss memory and queued second sites. Existing gate-egress, ordinary rally and two-faction tech/base/expansion fixtures also pass. Windows player fixture passes default zombies, spectator controls, visibility and mode transitions; build completed with zero errors.', '',
          'No CPU unit mirror or per-unit GameObjects were introduced. Threat counts come from the existing GPU spatial grid, and only four counters per active team are read back once per simulation second. No query dispatch runs while inactive. Regular clearance commands use reserved enemy group 1; the existing GPU movement solver is unchanged. The spending quota grants no currency and retains normal production/research prerequisites.']
if 'final-probe' in changes:
    lines += ['', 'The final base-AI capital/backlog safeguard was added after the first measurements. It changes spending decisions only during clearance; army/render/query shaders and non-base stress code are unchanged. The final Windows build and final clearance fixture pass, including protected capital and the three-site backlog. Three new paired probe runs validate the final base build; both initial and final measurements are retained. These probes enable the queries directly rather than forcing a combat-loss economy, so they do not certify mature clearance-battle balance or timing.']
(root / 'comparison.json').write_text(json.dumps(dict(cases=cases, changes=changes), indent=2))
(root / 'RESULTS.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
print('\n'.join(lines))
