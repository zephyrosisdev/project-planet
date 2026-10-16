from pathlib import Path
import csv,json,statistics
root=Path(__file__).resolve().parents[1]/'TestResults/AiTest'
army=['Army.SpatialGrid','Army.Navigation','Army.CrowdPressure','Army.MovementAvoidance','Army.ContactGrid','Army.ContactResolution','Army.TargetAcquisition','Army.Combat','Army.Projectiles']
def percentile(v):
 v=sorted(v);at=(len(v)-1)*.95;i=int(at);return v[i]+(v[min(i+1,len(v)-1)]-v[i])*(at-i)
def measure(folder,scenario):
 rows=list(csv.DictReader((folder/(scenario+'.csv')).open()));meta=json.loads((folder/(scenario+'.json')).read_text());memory=json.loads((folder/(scenario+'-memory.json')).read_text());assert memory['renderedFrames']>=len(rows)
 frames=[float(r['frame_s'])*1000 for r in rows];stepped=[];drawOnly=[];previous=int(rows[0]['tick'])
 for row in rows[1:]:
  tick=int(row['tick']);(stepped if tick>previous else drawOnly).append(float(row['frame_s'])*1000);previous=tick
 gpu=[sum(float(r.get(k,0)) for k in army) for r in rows];fog=[float(r.get('Army.Fog',0)) for r in rows]
 return dict(meanFrameMs=statistics.mean(frames),simulationFrameMedianMs=statistics.median(stepped) if stepped else None,drawOnlyFrameMedianMs=statistics.median(drawOnly) if drawOnly else None,medianMs=statistics.median(frames),p95Ms=percentile(frames),simulationHz=(int(rows[-1]['tick'])-int(rows[0]['tick']))/(float(rows[-1]['elapsed_s'])-float(rows[0]['elapsed_s'])),alive=statistics.median(int(r['alive']) for r in rows),allocatedMiB=memory['peakAllocatedBytes']/1048576,reservedMiB=memory['peakReservedBytes']/1048576,armyGpuP95Ms=percentile(gpu) if max(gpu)>0 else None,fogGpuP95Ms=percentile(fog) if max(fog)>0 else None),meta
result={};metadata={}
for label,scenario in [('baseline','overview'),('current','overview'),('current','ai-test')]:
 key=label+'-'+scenario;runs=[];metadata[key]=[]
 for repeat in range(1,4):
  r,m=measure(root/f'Benchmarks/{key}-{repeat}',scenario);runs.append(r);metadata[key].append(m)
 result[key]=dict(runs=runs,summary={k:statistics.median(r[k] for r in runs) if all(r[k] is not None for r in runs) else None for k in runs[0]})
for a,b in zip(metadata['baseline-overview'],metadata['current-overview']):
 before=a['settings'].copy();before.setdefault('spectatorMode',False);before.setdefault('aiCyanProfile',7);before.setdefault('aiRedProfile',7);assert before==b['settings'], 'Scenario settings differ'
 assert all(a.get(k)==b.get(k) for k in ['width','height','cameraPosition','cameraAngles','orthographicHalfHeight']), 'Camera differs'
a=result['baseline-overview']['summary'];b=result['current-overview']['summary'];delta=(b['p95Ms']/a['p95Ms']-1)*100
lines=['# AI spectator test: measured results','','RTX 3070 / Windows D3D11 / 2560x1440, three runs each. Normal Burst settings. Explicit HDRP frames are GPU-fenced and exclude screen-space UI. Baseline/current overview cases preserve 1,000,000 live units, identical seed, preparation, camera and settings. The new-mode case uses 100K ambient zombies plus the starting armies in a 150K-slot match and a full-map spectator view; it measures early base development, not a mature battle.','','| Case | Median ms | p95 ms | Sim Hz | Alive | Allocated MiB | Reserved MiB |','|---|---:|---:|---:|---:|---:|---:|']
for key,data in result.items():
 s=data['summary'];lines.append(f'| {key} | {s["medianMs"]:.3f} | {s["p95Ms"]:.3f} | {s["simulationHz"]:.3f} | {s["alive"]:,.0f} | {s["allocatedMiB"]:.2f} | {s["reservedMiB"]:.2f} |')
lines+=['',f'Equivalent million-unit frame p95 change: {delta:+.2f}% ({"within" if delta<=5 else "EXCEEDS"} the 5% frame proxy).',f'60 FPS frame-time target: million-unit stress {"met" if b["p95Ms"]<=16.67 else "UNMET"}; AI test view {"met" if result["current-ai-test"]["summary"]["p95Ms"]<=16.67 else "UNMET"}. No population reduction is used.', 'Measured Unity allocation/reservation is not total dedicated VRAM or an estimated budget. Army GPU/total-fog scopes are unavailable; their 5%/3ms acceptance limits cannot be certified from these records.']
lines+=['',f'Million-unit frame samples are bimodal at 30 simulation Hz. Baseline runs have 599-600 simulation steps and about 601 draw-only samples; current runs retain 600 steps with 586-596 draw-only samples. Crossing the 50% boundary shifts the raw median between the two modes without a comparable change in step cost.',f'Simulation-step frame median: {a["simulationFrameMedianMs"]:.3f} -> {b["simulationFrameMedianMs"]:.3f} ms ({(b["simulationFrameMedianMs"]/a["simulationFrameMedianMs"]-1)*100:+.2f}%). Draw-only frame median: {a["drawOnlyFrameMedianMs"]:.3f} -> {b["drawOnlyFrameMedianMs"]:.3f} ms. Mean frame: {a["meanFrameMs"]:.3f} -> {b["meanFrameMs"]:.3f} ms ({(b["meanFrameMs"]/a["meanFrameMs"]-1)*100:+.2f}%). Raw medians are retained above; these stratified values are frame proxies, not GPU army scopes.']
(root/'RESULTS.md').write_text('\n'.join(lines)+'\n',encoding='utf-8');(root/'comparison.json').write_text(json.dumps(dict(cases=result,millionFrameP95ChangePercent=delta),indent=2),encoding='utf-8');print('\n'.join(lines))
