"""Compare equivalent standalone runs; reject hidden presented runs lacking render proof."""
from pathlib import Path
import csv,json,statistics
root=Path('TestResults/ArtPass');runs={};groups={}
def p95(values):
 values=sorted(values);i=(len(values)-1)*.95;k=int(i);return values[k]+(values[min(k+1,len(values)-1)]-values[k])*(i-k)
for label in ['baseline','current']:
 runs[label]=[]
 for path in sorted((root/'Benchmarks'/label).rglob('*.csv')):
  if path.name=='total-fog.csv':continue
  presented=path.name=='frames.csv';info=json.loads((path.parent/'settings.json' if presented else path.with_suffix('.json')).read_text());memory=json.loads((path.parent/'memory.json' if presented else path.with_name(path.stem+'-memory.json')).read_text())
  rows=list(csv.DictReader(path.open()));frames=[float(r['frame_s'])*1000 for r in rows]
  sim=[sum(float(r.get(key,0)) for key in ['Army.SpatialGrid','Army.Navigation','Army.CrowdPressure','Army.MovementAvoidance','Army.ContactGrid','Army.ContactResolution','Army.TargetAcquisition','Army.Combat','Army.Projectiles']) for r in rows];fog=[float(r.get('Army.Fog',0)) for r in rows]
  rendered=(path.parent/'presented.png').exists() if presented else memory.get('renderedFrames',0)>=len(rows)
  case=path.parent.name.rsplit('-',1)[0]
  result=dict(case=case,path=str(path),medianFrameMs=statistics.median(frames),p95FrameMs=p95(frames),simulationHz=(int(rows[-1]['tick'])-int(rows[0]['tick']))/(float(rows[-1]['elapsed_s'])-float(rows[0]['elapsed_s'])),medianAlive=statistics.median(int(r['alive']) for r in rows),peakAllocatedMiB=memory['peakAllocatedBytes']/1048576,peakReservedMiB=memory['peakReservedBytes']/1048576,p95SimulationGpuMs=p95(sim) if max(sim)>0 else None,p95FogDrawGpuMs=p95(fog) if max(fog)>0 else None,renderVerified=rendered,presented=presented,frames=len(rows))
  runs[label].append(result);groups.setdefault(case,{}).setdefault(label,[]).append((result,info))
comparisons=[]
for case,labels in sorted(groups.items()):
 if set(labels)!=set(['baseline','current']) or any(len(v)!=3 for v in labels.values()):raise ValueError('Expected three runs per build: '+case)
 equivalent=[]
 for a,b in zip(labels['baseline'],labels['current']):equivalent.append(all(a[1].get(key)==b[1].get(key) for key in ['settings','width','height','orthographicHalfHeight','cameraPosition','cameraAngles']))
 summaries={}
 for label,rows in labels.items():
  summaries[label]={key:statistics.median(r[0][key] for r in rows) if all(r[0][key] is not None for r in rows) else None for key in ['medianFrameMs','p95FrameMs','simulationHz','medianAlive','peakAllocatedMiB','peakReservedMiB','p95SimulationGpuMs','p95FogDrawGpuMs']}
  summaries[label]['renderVerified']=all(r[0]['renderVerified'] for r in rows)
 a,b=summaries['baseline'],summaries['current'];valid=all(equivalent) and a['renderVerified'] and b['renderVerified']
 comparisons.append(dict(case=case,baseline=a,current=b,equivalent=all(equivalent),usable=valid,medianFrameChangePercent=(b['medianFrameMs']/a['medianFrameMs']-1)*100 if valid else None,p95FrameChangePercent=(b['p95FrameMs']/a['p95FrameMs']-1)*100 if valid else None,frameTargetPass=b['p95FrameMs']<=16.67 if valid else None,armySimulationAcceptance='unverified: GPU scope timings unavailable' if b['p95SimulationGpuMs'] is None else 'see measured scopes',fogAcceptance='unverified: complete fog timing unavailable'))
report=dict(cases=comparisons,runs=runs,note='Three-run medians of each run statistic. GPU-fenced off-screen standalone frames exclude screen-space UI. Hidden presented windows without a screenshot/render proof are rejected. Measured Unity allocated/reserved memory excludes total dedicated VRAM. Missing timings are unavailable, never zero.')
(root/'comparison.json').write_text(json.dumps(report,indent=2));print(json.dumps(comparisons,indent=2))
lines=['# 3D art pass validation results','','Windows, Unity 6000.6.4f1, RTX 3070, D3D11, 2560×1440. Three runs per build with the same seeds, settings and camera poses. The earlier exploratory runs overlapped artwork rendering and are excluded.','','| Scenario | Baseline median / p95 ms | Art median / p95 ms | Art simulation Hz | Baseline → art allocated MiB | Baseline → art reserved MiB |','|---|---:|---:|---:|---:|---:|']
for row in comparisons:
 if not row['usable']:continue
 a,b=row['baseline'],row['current'];lines.append(f'| {row["case"]} | {a["medianFrameMs"]:.2f} / {a["p95FrameMs"]:.2f} | {b["medianFrameMs"]:.2f} / {b["p95FrameMs"]:.2f} | {b["simulationHz"]:.2f} | {a["peakAllocatedMiB"]:.1f} → {b["peakAllocatedMiB"]:.1f} | {a["peakReservedMiB"]:.1f} → {b["peakReservedMiB"]:.1f} |')
lines+=['','Memory is measured Unity tracked allocation/reservation, not total VRAM. Stress cases retain the requested 50K and 1M capacities. Base-close uses 50K capacity with the normal eight starting units; it is a base-rendering case rather than a populated 50K army.','','The frame target is 16.67 ms p95. Any cases above it remain unmet. Simulation GPU scope timings and complete fog timings are unavailable in these standalone runs, so the ≤5% army simulation regression and ≤3 ms total fog targets cannot be certified. All 45 compiled non-building army kernels remain byte-identical to the committed baseline; that supports architectural preservation but does not replace measurements.','','Presented queue runs were collected separately. A hidden window did not provide a valid screenshot/render proof, so those frame results are excluded from acceptance. GPU-fenced frame numbers above exclude screen-space UI.','','Asset acceptance: 312 model/runtime checks, all 31 types accepted by normal GPU spawning, and 844 grid/gate checks on each of the 4096 m and 16384 m worlds. All 120 shader stages compile. Every model is inspected in the complete three-sheet gallery. Exact raw run metadata and comparisons are in `TestResults/ArtPass/comparison.json`.']
Path('Documentation/3D_ART/RESULTS.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
