from pathlib import Path
import csv,json,statistics
root=Path(__file__).resolve().parents[1];out=root/'TestResults/Zombies'
army=['Army.SpatialGrid','Army.Navigation','Army.CrowdPressure','Army.MovementAvoidance','Army.ContactGrid','Army.ContactResolution','Army.TargetAcquisition','Army.Combat','Army.Projectiles']
def p95(data):
    data=sorted(data);at=(len(data)-1)*.95;i=int(at);return data[i]+(data[min(i+1,len(data)-1)]-data[i])*(at-i)
def measure(folder,scenario):
    rows=list(csv.DictReader((folder/(scenario+'.csv')).open()));meta=json.loads((folder/(scenario+'.json')).read_text());memory=json.loads((folder/(scenario+'-memory.json')).read_text())
    frame=[float(r['frame_s'])*1000 for r in rows];gpu=[sum(float(r.get(k,0)) for k in army) for r in rows];fog=[float(r.get('Army.Fog',0)) for r in rows]
    assert memory.get('renderedFrames',0)>=len(rows),'Frames were not GPU-fenced'
    result=dict(medianMs=statistics.median(frame),p95Ms=p95(frame),simulationHz=(int(rows[-1]['tick'])-int(rows[0]['tick']))/(float(rows[-1]['elapsed_s'])-float(rows[0]['elapsed_s'])),alive=statistics.median(int(r['alive']) for r in rows),allocatedMiB=memory['peakAllocatedBytes']/1048576,reservedMiB=memory['peakReservedBytes']/1048576,armyGpuP95Ms=p95(gpu) if max(gpu)>0 else None,fogGpuP95Ms=p95(fog) if max(fog)>0 else None)
    return result,meta
report={};lines=['# Zombie implementation: measured results','','Three runs per frozen player, Windows D3D11 / RTX 3070, 2560×1440. Identical seed, settings, preparation and camera. Explicit HDRP frames are GPU-fenced and exclude screen-space UI. Normal Burst player settings are retained.','','## Equivalent existing-army scenarios','','NPCs are disabled explicitly for these comparisons. This preserves the exact 50K/million population and isolates regressions in the existing simulation path.','','| Case / build | Median ms | p95 ms | Sim Hz | Alive | Allocated MiB | Reserved MiB |','|---|---:|---:|---:|---:|---:|---:|']
for scenario,count in [('overview',50000),('overview',1000000),('base-close',50000)]:
    key=f'{scenario}-{count}';report[key]={};meta={}
    for label in ['baseline','current']:
        data=[];meta[label]=[]
        for repeat in range(1,4):
            run,info=measure(out/f'Benchmarks/{label}/{key}-{repeat}',scenario);data.append(run);meta[label].append(info)
        summary={k:statistics.median(r[k] for r in data) if all(r[k] is not None for r in data) else None for k in data[0]}
        report[key][label]=dict(summary=summary,runs=data)
        lines.append(f'| {key} / {label} | {summary["medianMs"]:.3f} | {summary["p95Ms"]:.3f} | {summary["simulationHz"]:.3f} | {summary["alive"]:,.0f} | {summary["allocatedMiB"]:.2f} | {summary["reservedMiB"]:.2f} |')
    for a,b in zip(meta['baseline'],meta['current']):
        before=a['settings'].copy();before.setdefault('ambientZombies',0);after=b['settings'].copy()
        assert before==after,(key,'Settings differ')
        assert all(a.get(k)==b.get(k) for k in ['width','height','cameraPosition','cameraAngles','orthographicHalfHeight']), (key,'Camera differs')
    a=report[key]['baseline']['summary'];b=report[key]['current']['summary'];delta=(b['p95Ms']/a['p95Ms']-1)*100
    gpuDelta=(b['armyGpuP95Ms']/a['armyGpuP95Ms']-1)*100 if a['armyGpuP95Ms'] and b['armyGpuP95Ms'] else None
    report[key]['p95FrameChangePercent']=delta;report[key]['armyGpuChangePercent']=gpuDelta
    report[key]['frameProxyWithinFivePercent']=delta<=5
lines+=['','Frame p95 changes: '+', '.join(f'{case} {value["p95FrameChangePercent"]:+.2f}%' for case,value in report.items())+'.',
        'The 60 FPS target is '+('met' if report['overview-1000000']['current']['summary']['p95Ms']<=16.67 else 'UNMET')+' in the million-unit scenario. Army scale has not been reduced.',
        'GPU army scopes and total-fog timing are '+('available in the raw reports.' if all(v['current']['summary']['armyGpuP95Ms'] is not None for v in report.values()) else 'unavailable in these recordings. Frame-time comparisons are a conservative proxy; the 5% army-GPU and 3 ms total-fog targets cannot be certified from missing scopes.'),
        'Unity tracked allocated/reserved memory is measured; it is not total dedicated VRAM or an estimated buffer budget.']
feature=[]
for scenario,count in [('base-close',50000),('swarm-heavy',250000)]:
    found=[]
    for repeat in range(1,4):
        folder=out/f'Benchmarks/feature/{scenario}-{count}-{repeat}'
        if (folder/(scenario+'.csv')).exists():found.append(measure(folder,scenario)[0])
    if len(found)==3:
        summary={k:statistics.median(r[k] for r in found) if all(r[k] is not None for r in found) else None for k in found[0]};feature.append(dict(scenario=scenario,summary=summary,runs=found))
if feature:
    lines+=['','## New populated zombie scenarios','','These are feature-cost measurements, not equivalent old/new population comparisons. Base-close has 100K ambient zombies; swarm-heavy adds a 128K invading wave in a 350K-slot match.','','| Scenario | Median ms | p95 ms | Sim Hz | Alive | Allocated MiB | Reserved MiB |','|---|---:|---:|---:|---:|---:|---:|']
    for row in feature:
        s=row['summary'];lines.append(f'| {row["scenario"]} | {s["medianMs"]:.3f} | {s["p95Ms"]:.3f} | {s["simulationHz"]:.3f} | {s["alive"]:,.0f} | {s["allocatedMiB"]:.2f} | {s["reservedMiB"]:.2f} |')
    lines+=['','The populated base meets the 16.67 ms frame target in this capture path. The 128K-wave case exceeds it at p95; the full 100K ambient and 128K wave populations are retained. These captures exclude screen-space UI and are not certification of complete presented-player performance.']
(out/'comparison.json').write_text(json.dumps(dict(equivalent=report,feature=feature),indent=2));(out/'RESULTS.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(json.dumps({key:dict(p95ChangePercent=value['p95FrameChangePercent'],withinFivePercent=value['frameProxyWithinFivePercent']) for key,value in report.items()},indent=2))
