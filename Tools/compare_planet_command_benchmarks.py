"""Require equivalent before/after standalone reports for every development viewpoint."""
import json
import argparse
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--original', action='store_true', help='Reproduce the original rejected cross-build comparison')
options = parser.parse_args()
base = Path(__file__).resolve().parents[1] / 'TestResults' / 'Planetary' / 'Commands'
root = base / ('InitialBenchmarks' if options.original else 'QuietControls')
labels = ('before', 'after') if options.original else ('control', 'commands')
comparisons = []
for scenario in ('surface', 'orbit', 'fps'):
    before, after = [json.loads((root / f'{label}-{scenario}' / 'benchmark.json').read_text())
                     for label in labels]
    for key in ('gpu', 'api', 'renderer', 'scenario', 'width', 'height', 'units',
                'navResolution', 'seed', 'radius', 'errorPixels'):
        assert before[key] == after[key], f'Non-equivalent {scenario}: {key}'
    assert after['width'] == 2560 and after['height'] == 1440
    assert after['units'] == 32 and not after.get('rtsContent', False)
    if not options.original:
        for key in ('cameraDistance', 'buildings', 'rtsContent'):
            assert before[key] == after[key], f'Non-equivalent {scenario}: {key}'
        manifests = [json.loads((root / f'{label}-{scenario}' / 'run.json').read_text()) for label in labels]
        assert manifests[0]['assemblySHA256'] == manifests[1]['assemblySHA256'], 'Control build mismatch'
    changes = {key: (after[key] / before[key] - 1) * 100 for key in ('medianMs', 'p95Ms')}
    comparisons.append(dict(scenario=scenario, baseline=before, current=after,
                            frameChangePercent=changes, withinFivePercent=max(changes.values()) <= 5))
report = dict(comparisons=comparisons,
              scope=('Original cross-build comparison. ' if options.original else
                     'Quiet interleaved same-build control: previous all-unit path versus group-command path. ')+
                    'Equivalent 32-unit planetary fixture, unchanged army size and settings. '
                    'Steady movement and rendering only; selection-event latency, eighteen occupied '
                    'fields, million-unit planetary simulation and fog cost are not certified.',
              history='Original cross-build surface p95 and first interleaved FPS p95 failed the 5% gate. '
                      'Those runs remain in InitialBenchmarks and Controls; quiet runs avoid concurrent code imports.',
              memory='Measured Unity tracked allocation/reservation; dedicated VRAM is not measured.')
(root / 'comparison.json').write_text(json.dumps(report, indent=2))
for item in comparisons:
    a, b = item['baseline'], item['current']
    print(f"{item['scenario']}: median {a['medianMs']:.3f}->{b['medianMs']:.3f} ms, "
          f"p95 {a['p95Ms']:.3f}->{b['p95Ms']:.3f} ms, "
          f"Hz {a['simulationHz']:.2f}->{b['simulationHz']:.2f}, "
          f"tracked MiB {a['peakAllocatedBytes']/1048576:.2f}->{b['peakAllocatedBytes']/1048576:.2f}, "
          f"within5={item['withinFivePercent']}")
if not all(item['withinFivePercent'] for item in comparisons):
    raise SystemExit('REJECT: equivalent fixture regression exceeds 5%')
