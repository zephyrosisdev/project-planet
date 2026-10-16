from pathlib import Path
import csv,json,statistics
root=Path('.');out=root/'TestResults/BuildingPresentation'
def measure(folder):
 rows=list(csv.DictReader((folder/'base-close.csv').open()));meta=json.loads((folder/'base-close.json').read_text());memory=json.loads((folder/'base-close-memory.json').read_text());assert memory['renderedFrames']>=len(rows)
 values=sorted(float(r['frame_s'])*1000 for r in rows);i=(len(values)-1)*.95;k=int(i)
 return dict(medianMs=statistics.median(values),p95Ms=values[k]+(values[min(k+1,len(values)-1)]-values[k])*(i-k),simulationHz=(int(rows[-1]['tick'])-int(rows[0]['tick']))/(float(rows[-1]['elapsed_s'])-float(rows[0]['elapsed_s'])),allocatedMiB=memory['peakAllocatedBytes']/1048576,reservedMiB=memory['peakReservedBytes']/1048576),meta
result={};metadata={}
for label in ['baseline','current']:
 runs=[];metadata[label]=[]
 for repeat in range(1,4):
  folder=root/f'TestResults/Lighting/Benchmarks/on-{repeat}' if label=='baseline' else out/f'Benchmarks/current-{repeat}'
  r,m=measure(folder);runs.append(r);metadata[label].append(m)
 result[label]=dict(runs=runs,summary={k:statistics.median(r[k] for r in runs) for k in runs[0]})
for a,b in zip(metadata['baseline'],metadata['current']):
 assert a['settings']==b['settings'];assert all(a.get(k)==b.get(k) for k in ['width','height','cameraPosition','cameraAngles','orthographicHalfHeight'])
a=result['baseline']['summary'];b=result['current']['summary']
lines=['# Building shadow and articulation results','','Equivalent standalone RTX 3070 / D3D11 / 2560x1440 base-close views, three runs per build. Shadows enabled, seed/camera/settings identical, 50K reserved slots, eight live starting units, ambient zombies explicitly disabled. Baseline uses the frozen lighting-pass reports in TestResults/Lighting/Benchmarks/on-1 through on-3.','','| Build | Median ms | p95 ms | Sim Hz | Allocated MiB | Reserved MiB |','|---|---:|---:|---:|---:|---:|']
for label,data in result.items():
 s=data['summary'];lines.append(f'| {label} | {s["medianMs"]:.3f} | {s["p95Ms"]:.3f} | {s["simulationHz"]:.3f} | {s["allocatedMiB"]:.2f} | {s["reservedMiB"]:.2f} |')
lines+=['',f'Frame p95 change: {b["p95Ms"]-a["p95Ms"]:+.3f} ms ({(b["p95Ms"]/a["p95Ms"]-1)*100:+.2f}%).',f'60 FPS frame-time target in this base view: {"met" if b["p95Ms"]<=16.67 else "UNMET"}.','GPU-fenced offscreen HDRP captures exclude screen-space UI. Unity allocation/reservation is measured, not dedicated VRAM or estimated budgets. Army GPU and total-fog scopes are unavailable, so their individual targets remain uncertified. The million-unit scenario has not been re-certified by this bounded cosmetic change.','','Validation: catalogue near/far shadows; fixed versus moving geometry for all nine weapon types; rendered head rotation with pixel-identical foundation; actual GPU weapon target and wrapping seam; slot reuse; standalone rotated construction and firing with an unchanged building/grid transform.']
(out/'RESULTS.md').write_text('\n'.join(lines)+'\n',encoding='utf-8');(out/'comparison.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print('\n'.join(lines))
