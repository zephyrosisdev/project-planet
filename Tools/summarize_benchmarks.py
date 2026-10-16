import csv,json,sys,statistics
from pathlib import Path
root=Path(sys.argv[1] if len(sys.argv)>1 else 'TestResults/Benchmarks/current')
def pct(values,p):
 v=sorted(values);i=(len(v)-1)*p;a=int(i);return v[a]+(v[min(a+1,len(v)-1)]-v[a])*(i-a)
reports=[]
for p in root.rglob('*.csv'):
 rows=list(csv.DictReader(p.open()));
 if not rows:continue
 info=json.loads(p.with_suffix('.json').read_text());memoryPath=p.with_name(p.stem+'-memory.json');memory=json.loads(memoryPath.read_text()) if memoryPath.exists() else {}
 frames=[float(r['frame_s'])*1000 for r in rows];fog=[float(r.get('Army.Fog',0)) for r in rows];sim=[sum(float(r.get(k,0)) for k in ['Army.SpatialGrid','Army.Navigation','Army.CrowdPressure','Army.MovementAvoidance','Army.ContactGrid','Army.ContactResolution','Army.TargetAcquisition','Army.Combat','Army.Projectiles']) for r in rows]
 engine=[float(r['engine_gpu_ms']) for r in rows];ticks=(int(rows[-1]['tick'])-int(rows[0]['tick']))/max(.0001,float(rows[-1]['elapsed_s'])-float(rows[0]['elapsed_s']))
 valid=memory.get('renderedFrames',0)>=len(rows)
 alive=[int(r['alive']) for r in rows]
 reports.append(dict(path=str(p),units=info['settings']['activeUnits'],capacity=info['settings']['capacity'],medianAlive=statistics.median(alive),peakAlive=max(alive),worldSize=info['settings']['worldSize'],renderVerified=valid,frames=len(rows),medianFrameMs=statistics.median(frames),p95FrameMs=pct(frames,.95),simulationHz=ticks,p95SimulationGpuMs=pct(sim,.95) if max(sim)>0 else None,p95FogGpuMs=pct(fog,.95) if max(fog)>0 else None,peakAllocatedBytes=memory.get('peakAllocatedBytes'),peakReservedBytes=memory.get('peakReservedBytes'),frameTargetPass=(pct(frames,.95)<=16.67) if valid else None,fogTargetPass=(pct(fog,.95)<=3) if valid and max(fog)>0 else None,note='Unity tracked memory excludes total dedicated VRAM; missing GPU timings are unavailable, not zero cost. Base scenarios report capacity separately from actual alive units.'))
print(json.dumps(reports,indent=2));root.mkdir(parents=True,exist_ok=True);(root/'summary.json').write_text(json.dumps(reports,indent=2))
