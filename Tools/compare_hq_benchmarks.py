"""Compare three frozen standalone runs per HQ variant; unavailable scopes stay null."""
from pathlib import Path
import csv,json,statistics,hashlib,re
root=Path(__file__).resolve().parents[1];out=root/'TestResults/HQ';runs={}
def p95(a):
    a=sorted(a);x=(len(a)-1)*.95;k=int(x);return a[k]+(a[min(k+1,len(a)-1)]-a[k])*(x-k)
metadata={}
for label in ['baseline','current']:
    runs[label]=[];metadata[label]=[]
    for repeat in range(1,4):
        folder=out/f'Benchmarks/{label}/base-close-{repeat}';path=folder/'base-close.csv'
        info=json.loads(path.with_suffix('.json').read_text());mem=json.loads((folder/'base-close-memory.json').read_text());rows=list(csv.DictReader(path.open()))
        frame=[float(r['frame_s'])*1000 for r in rows];sim=[sum(float(r.get(k,0)) for k in ['Army.SpatialGrid','Army.Navigation','Army.CrowdPressure','Army.MovementAvoidance','Army.ContactGrid','Army.ContactResolution','Army.TargetAcquisition','Army.Combat','Army.Projectiles']) for r in rows];fog=[float(r.get('Army.Fog',0)) for r in rows]
        runs[label].append(dict(run=repeat,medianFrameMs=statistics.median(frame),p95FrameMs=p95(frame),simulationHz=(int(rows[-1]['tick'])-int(rows[0]['tick']))/(float(rows[-1]['elapsed_s'])-float(rows[0]['elapsed_s'])),medianAlive=statistics.median(int(r['alive']) for r in rows),peakAllocatedMiB=mem['peakAllocatedBytes']/1048576,peakReservedMiB=mem['peakReservedBytes']/1048576,renderVerified=mem.get('renderedFrames',0)>=len(rows),p95ArmyGpuMs=p95(sim) if max(sim)>0 else None,p95FogGpuMs=p95(fog) if max(fog)>0 else None))
        metadata[label].append(info)
keys=['settings','width','height','orthographicHalfHeight','cameraPosition','cameraAngles']
equivalent=all(all(a.get(k)==b.get(k) for k in keys) for a,b in zip(metadata['baseline'],metadata['current']))
summary={label:{key:statistics.median(row[key] for row in data) if all(row[key] is not None for row in data) else None for key in data[0] if key not in ['run','renderVerified']} for label,data in runs.items()}
verified=all(row['renderVerified'] for data in runs.values() for row in data)
if not equivalent or not verified:raise ValueError('Comparison rejected: non-equivalent or unverified rendered runs')
a=summary['baseline'];b=summary['current'];delta={k:(b[k]/a[k]-1)*100 for k in ['medianFrameMs','p95FrameMs']}
report=dict(equivalent=equivalent,renderVerified=verified,summary=summary,frameChangePercent=delta,runs=runs,note='GPU-fenced off-screen 2560x1440 frames exclude UI. Base-close has eight starting units in 50K capacity. Unity measured memory is not total VRAM. Missing GPU scopes do not mean zero cost.')
(out/'comparison.json').write_text(json.dumps(report,indent=2))
geometry=json.loads((root/'ArtSource/HQ/geometry-report.json').read_text())
lines=['# HQ revision: validation and rendering cost','','Unity 6000.6.4f1, Windows D3D11, RTX 3070, 2560×1440. Three sequential runs per frozen build, identical seeds, camera and preparation. Artwork rendering and Unity compilation were stopped before measurements.','','| Measured metric | Previous HQ | Detailed HQ |','|---|---:|---:|']
for label,key,unit in [('Median frame','medianFrameMs','ms'),('p95 frame','p95FrameMs','ms'),('Simulation rate','simulationHz','Hz'),('Unity allocated memory','peakAllocatedMiB','MiB'),('Unity reserved memory','peakReservedMiB','MiB')]:lines.append(f'| {label} | {a[key]:.3f} {unit} | {b[key]:.3f} {unit} |')
lines+=['',f'Frame change: median {delta["medianFrameMs"]:+.2f}%; p95 {delta["p95FrameMs"]:+.2f}%. This measures the cosmetic HQ rendering change, not an army-simulation change.',f'Final HQ: {geometry["triangles"]:,} triangles; {geometry["components"]} editable authoring components; {geometry["boundsMetres"][0]:.2f} × {geometry["boundsMetres"][2]:.2f} m footprint and {geometry["boundsMetres"][1]:.2f} m height. One combined runtime shell, three 2048px BC7 maps, unchanged army banks. Both LOD distances use the detailed shell during this prototype.','',f'60 FPS frame target in this specific base-close test: {"met" if b["p95FrameMs"]<=16.67 else "UNMET"}. The scenario has eight starting units and 50K capacity, so it does not establish 60 FPS with a populated 50K or million-unit army. The million-unit simulation path is retained and receives no changes in this revision.','', 'Allocated/reserved values are measured Unity tracked memory, not total dedicated VRAM or estimated budgets. Army GPU scopes and complete fog timing are unavailable here; the ≤5% simulation regression and ≤3 ms p95 total-fog targets cannot be certified from these measurements. No unit scale or simulation setting was reduced.','', 'The opt-in standalone fixture checks the final HQ shell, material, 2K maps, vertex AO, existing catalogue coverage, reserved footprint, construction shell reuse, rotation and snapped placement. Runtime errors fail the fixture. Screenshots use explicit HDRP render requests, including an isolated concept-angle runtime capture and placement-grid capture.','', 'Visual review: `gallery.html` and `comparison.png` show the supplied concept beside the model, plus front, rear and actual Unity material views. The major concept features are reproduced. This is an authored reconstruction with differences in panel shapes, small equipment and surface finish; an exact visual match is not asserted or marked accepted.']
(out/'RESULTS.md').write_text('\n'.join(lines)+'\n',encoding='utf-8');print(json.dumps(dict(summary=summary,frameChangePercent=delta),indent=2))
manifest={}
for path in [root/'ArtSource/HQ/building-command.mesh.gz',root/'Assets/MassiveArmy/Art/Models/building-command.fbx',root/'Assets/MassiveArmy/Resources/MassiveArmy/ModelArt/Meshes/building-command.asset',*(root/'Assets/MassiveArmy/Art/Textures').glob('building-command-*.png')]:manifest[str(path.relative_to(root))]=hashlib.sha256(path.read_bytes()).hexdigest()
(out/'asset-hashes.json').write_text(json.dumps(manifest,indent=2))
