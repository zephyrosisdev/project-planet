from pathlib import Path
import csv,json,statistics
root=Path(__file__).resolve().parents[1]/'TestResults/Lighting'
def percentile(xs):
 xs=sorted(xs);v=(len(xs)-1)*.95;i=int(v);return xs[i]+(xs[min(i+1,len(xs)-1)]-xs[i])*(v-i)
result={};metadata={}
for mode in ['off','on']:
 runs=[];metadata[mode]=[]
 for repeat in range(1,4):
  p=root/f'Benchmarks/{mode}-{repeat}';rows=list(csv.DictReader((p/'base-close.csv').open()));m=json.loads((p/'base-close-memory.json').read_text());metadata[mode].append(json.loads((p/'base-close.json').read_text()))
  values=[float(x['frame_s'])*1000 for x in rows];assert m['renderedFrames']>=len(rows)
  runs.append(dict(medianMs=statistics.median(values),p95Ms=percentile(values),simulationHz=(int(rows[-1]['tick'])-int(rows[0]['tick']))/(float(rows[-1]['elapsed_s'])-float(rows[0]['elapsed_s'])),alive=statistics.median(int(x['alive']) for x in rows),allocatedMiB=m['peakAllocatedBytes']/1048576,reservedMiB=m['peakReservedBytes']/1048576))
 result[mode]=dict(runs=runs,summary={k:statistics.median(r[k] for r in runs) for k in runs[0]})
for a,b in zip(metadata['off'],metadata['on']):
 sa=a['settings'].copy();sb=b['settings'].copy();assert not sa.pop('shadows') and sb.pop('shadows');assert sa==sb
 assert all(a.get(k)==b.get(k) for k in ['width','height','cameraPosition','cameraAngles','orthographicHalfHeight'])
lines=['# Optional realtime-shadow cost','','RTX 3070 / Windows D3D11 / 2560×1440, three runs each. Same base-close scene, seed and camera; 50K reserved slots, eight live starting units, ambient zombies explicitly disabled. Normal Burst player. GPU-fenced offscreen HDRP frames exclude screen-space UI.','','| Shadows | Median ms | p95 ms | Sim Hz | Allocated MiB | Reserved MiB |','|---|---:|---:|---:|---:|---:|']
for mode,data in result.items():
 s=data['summary'];lines.append(f'| {mode} | {s["medianMs"]:.3f} | {s["p95Ms"]:.3f} | {s["simulationHz"]:.3f} | {s["allocatedMiB"]:.2f} | {s["reservedMiB"]:.2f} |')
a=result['off']['summary'];b=result['on']['summary'];lines+=['',f'On/off p95 difference: {b["p95Ms"]-a["p95Ms"]:+.3f} ms ({(b["p95Ms"]/a["p95Ms"]-1)*100:+.2f}%).',f'60 FPS frame-time target in this base view: {"met" if b["p95Ms"]<=16.67 else "UNMET"} with shadows enabled.','GPU army/total fog scopes are unavailable, so this does not certify either GPU limit. Unity allocated/reserved memory is measured, not dedicated VRAM. This is an optional cosmetic comparison, not a change in army simulation or a million-unit performance certification.']
(root/'RESULTS.md').write_text('\n'.join(lines)+'\n',encoding='utf-8');(root/'comparison.json').write_text(json.dumps(result,indent=2));print('\n'.join(lines))
