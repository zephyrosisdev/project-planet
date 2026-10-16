from pathlib import Path
import subprocess,re,hashlib
root=Path.cwd();source=root/'Assets/MassiveArmy/Resources/MassiveArmy';out=root/'TestResults/CompileCheck/shaders';out.mkdir(parents=True,exist_ok=True)
fxc=Path('C:/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/fxc.exe');dxc=fxc.with_name('dxc.exe');errors=[];passed=0
for p in source.glob('*.compute'):
 for k in re.findall(r'^#pragma kernel (\w+)',p.read_text(),re.M):
  proc=subprocess.run([str(fxc),'/nologo','/T','cs_5_0','/E',k,'/Fo',str(out/(p.stem+'-'+k+'.cso')),str(p)],capture_output=True,text=True)
  if proc.returncode:errors.append(p.name+' '+k+'\n'+proc.stdout+proc.stderr)
  else:passed+=1
cache=root/'TestResults/CompileCheck/shader-includes';cache.mkdir(exist_ok=True);mapped={}
def dependency(p):
 p=p.resolve()
 if p in mapped:return mapped[p]
 q=cache/(hashlib.sha256(str(p).encode()).hexdigest()[:20]+p.suffix);mapped[p]=q
 def resolve(m):
  name=m[1]
  if name.startswith('Packages/'):
   parts=name.split('/');matches=list((root/'Library/PackageCache').glob(parts[1]+'@*'));d=matches[0].joinpath(*parts[2:]) if matches else Path(name)
  else:
   d=p.parent/name
   if not d.exists():d=root/'Assets/MassiveArmy/Resources/MassiveArmy'/name
   if not d.exists():d=Path('C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Resources/CGIncludes')/name
  return '#include "'+dependency(d).as_posix()+'"' if d.exists() else m[0]
 q.write_text(re.sub(r'#include\s+"([^"]+)"',resolve,p.read_text()));return q
for name in ['Units','Scenery','FormationPreview','StrategicMissiles','VolumetricFogOfWar','FogVolumeSlice','ArtUnits','ArtStructure']:
 p=source/(name+'.shader');s=p.read_text();common=re.search(r'HLSLINCLUDE(.*?)ENDHLSL',s,re.S);common=common[1] if common else ''
 for i,body in enumerate(re.findall(r'HLSLPROGRAM(.*?)ENDHLSL',s,re.S)):
  h=source/('Offline-'+name+str(i)+'.hlsl');h.write_text(common+'\n'+body)
  try:q=dependency(h)
  finally:h.unlink()
  for stage,entry in re.findall(r'#pragma (vertex|fragment) (\w+)',body):
   hdrp='ShaderVariables.hlsl' in common+body or name in ['VolumetricFogOfWar'];compiler=dxc if hdrp else fxc;profile=('vs' if stage=='vertex' else 'ps')+('_6_0' if hdrp else '_5_0')
   args=[str(compiler),'-T' if hdrp else '/T',profile,'-E' if hdrp else '/E',entry,'-Fo' if hdrp else '/Fo',str(out/(name+str(i)+'-'+stage+'.cso'))]
   if not hdrp:args+=['/nologo','/Gec']
   for define in ['SHADER_API_D3D11=1','SHADER_TARGET=50','UNITY_VERSION=6000','UNITY_REVERSED_Z=1']:args+=['-D' if hdrp else '/D',define]
   proc=subprocess.run(args+[str(q)],capture_output=True,text=True)
   if proc.returncode:errors.append(name+str(i)+' '+stage+'\n'+proc.stdout+proc.stderr)
   else:passed+=1
(root/'TestResults/CompileCheck/shader-errors.txt').write_text('\n'.join(errors))
print(str(passed)+' shader stages passed; '+str(len(errors))+' failures')
for error in errors:print(error[:3000])
raise SystemExit(bool(errors))
