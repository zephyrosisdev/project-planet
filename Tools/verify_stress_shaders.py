"""Compare compiled non-building army kernels with the committed baseline.

This supplements measurements; it does not certify frame-time or fog targets.
"""
from pathlib import Path
import hashlib
import json
import re
import subprocess

root = Path.cwd()
source = root / 'Assets/MassiveArmy/Resources/MassiveArmy'
out = root / 'TestResults/GridPlacement/stress-shaders'
baseline = out / 'baseline'
baseline.mkdir(parents=True, exist_ok=True)
fxc = 'C:/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/fxc.exe'
for path in source.glob('*'):
    if path.suffix not in ('.compute', '.hlsl'):
        continue
    old = subprocess.run(['git', 'show', 'HEAD:' + path.relative_to(root).as_posix()],
                         capture_output=True)
    if old.returncode == 0:
        (baseline / path.name).write_bytes(old.stdout)

checks = []
for name in ('Battle.compute', 'Navigation.compute'):
    for kernel in re.findall(r'^#pragma kernel (\w+)', (source / name).read_text(), re.M):
        hashes = []
        for label, directory in (('baseline', baseline), ('current', source)):
            binary = out / (label + '-' + kernel + '.cso')
            subprocess.run([fxc, '/nologo', '/T', 'cs_5_0', '/E', kernel,
                            '/Qstrip_reflect', '/Qstrip_debug', '/Fo', str(binary),
                            str(directory / name)], check=True, capture_output=True)
            hashes.append(hashlib.sha256(binary.read_bytes()).hexdigest())
        checks.append(dict(shader=name, kernel=kernel, identical=hashes[0] == hashes[1]))
report = dict(status='PASS' if all(c['identical'] for c in checks) else 'DIFFERENT',
              baseline='git HEAD', checks=checks,
              note='Non-building cs_5_0 kernels, stripped reflection/debug. '
                   'Bytecode equality supplements rather than replaces performance measurements.')
(out / 'comparison.json').write_text(json.dumps(report, indent=2))
print(report['status'], len(checks), 'non-building army kernels')
