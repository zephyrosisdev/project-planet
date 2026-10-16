"""Bake UV-authored material islands, panel relief and metallic masks for the model library.

Maps: sRGB albedo; linear metallic/AO/team-emission/smoothness RGBA; tangent normal.
Normal relief derives from the same UV-space height field used by the panel finish.
"""
from pathlib import Path
import json, math, struct
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import distance_transform_edt, maximum_filter, label, find_objects

ROOT=Path.cwd();SOURCE=ROOT/'ArtSource/SyntheticFront';OUT=ROOT/'Assets/MassiveArmy/Art/Textures'
DATA=ROOT/'Assets/MassiveArmy/Resources/MassiveArmy/ModelArt/Data'
OUT.mkdir(parents=True,exist_ok=True);DATA.mkdir(parents=True,exist_ok=True)
catalog=json.loads((ROOT/'Assets/MassiveArmy/Resources/MassiveArmy/RtsCatalog.json').read_text())
colors=np.array([[53,67,87],[112,133,155],[17,23,30],[12,107,150],[10,27,42],[14,28,51],[20,28,37]],dtype=float)
metal=np.array([220,235,8,110,30,70,190]);smooth=np.array([135,170,35,180,225,200,50])
metal_source=Image.open(SOURCE/'metal-source.png').convert('RGB').resize((512,512))
metal_noise=np.asarray(metal_source).mean(axis=2);metal_noise=(metal_noise-metal_noise.mean())*.3
reports=[]
for kind,defs in [('building',catalog['buildings']),('unit',catalog['units'])]:
    for d in defs:
        key=kind+'-'+d['id']
        if key=='building-command' and (ROOT/'ArtSource/HQ/building-command.mesh.gz').exists():
            hq=json.loads((ROOT/'ArtSource/HQ/geometry-report.json').read_text());reports.append(dict(id='command',kind='building',resolution=2048,triangles=hq['triangles'],source='ArtSource/HQ'));continue
        payload=json.loads((SOURCE/(key+'.json')).read_text());size=512
        labels=Image.new('L',(size,size),0);draw=ImageDraw.Draw(labels);edgecounts={}
        for p in payload['polygons']:
            xy=[(float(u)*size,(1-float(v))*size) for u,v in p['uv']];draw.polygon(xy,fill=p['material']+1)
            for i,a in enumerate(xy):
                b=xy[(i+1)%len(xy)];e=tuple(sorted((tuple(round(x,3) for x in a),tuple(round(x,3) for x in b))));edgecounts[e]=edgecounts.get(e,0)+1
        edges=Image.new('L',(size,size),0);ed=ImageDraw.Draw(edges)
        for e,count in edgecounts.items():
            if count==1:ed.line(e,fill=255,width=1)
        idx=np.maximum(0,np.asarray(labels).astype(int)-1);coverage=np.asarray(labels)>0
        distance=distance_transform_edt(np.asarray(edges)==0)
        yy,xx=np.mgrid[:size,:size]
        grain=metal_noise+np.sin(xx*1.9+yy*.13)*.9
        panel=np.where(distance<2,.63,np.where(distance<3,1.12,1))
        rgb=colors[idx]*panel[...,None]+grain[...,None]
        relief=np.zeros((size,size));islands,count=label(coverage & (distance>1))
        # Island-local inset armour seams, fasteners and inset access hatches.
        # UV-space relief follows each packed panel instead of a world-space projection.
        for number,region in enumerate(find_objects(islands),1):
            if region is None:continue
            ry,rx=region;h=ry.stop-ry.start;w=rx.stop-rx.start
            if min(h,w)<14 or h*w<350:continue
            own=islands[region]==number;u=(xx[region]-rx.start)/w;v=(yy[region]-ry.start)/h
            inset=own & (((abs(u-.16)<.012)|(abs(u-.84)<.012)) & (v>.16)&(v<.84) | ((abs(v-.16)<.012)|(abs(v-.84)<.012)) & (u>.16)&(u<.84))
            rgb[region][inset]*=.55;relief[region][inset]=-.12
            for pu,pv in [(.20,.20),(.80,.20),(.20,.80),(.80,.80)]:
                bolt=own & (((u-pu)*w)**2+((v-pv)*h)**2<2.2)
                rgb[region][bolt]=[135,152,169];relief[region][bolt]=.13
        # Vent louvres and solar cell divisions are baked into their own material islands.
        vents=(idx==6)&((yy%5)<2);solar=(idx==5)&(((xx%16)<1)|((yy%12)<1))
        rgb[vents]*=.45;rgb[solar]*=.4
        albedo=np.clip(rgb,0,255).astype(np.uint8)
        height=np.minimum(distance/4,1)*.32
        height+=relief
        height[vents]-=.18;height[solar]-=.09
        height+=grain*.003
        dy,dx=np.gradient(height)
        normals=np.stack((-dx*3,dy*3,np.ones_like(dx)),axis=2);normals/=np.linalg.norm(normals,axis=2)[...,None]
        normal=np.clip((normals*.5+.5)*255,0,255).astype(np.uint8)
        mask=np.stack((metal[idx],np.clip(185+distance*12,0,255),np.where(idx==3,255,0),smooth[idx]),axis=2).astype(np.uint8)
        # Dilate island borders by four pixels to avoid dark mip seams.
        for values in (albedo,normal,mask):
            for channel in range(values.shape[2]):values[...,channel]=np.where(coverage,values[...,channel],maximum_filter(np.where(coverage,values[...,channel],0),size=9))
        Image.fromarray(albedo).save(OUT/(key+'-albedo.png'))
        Image.fromarray(mask).save(OUT/(key+'-metallic.png'))
        Image.fromarray(normal).save(OUT/(key+'-normal.png'))
        reports.append(dict(id=d['id'],kind=kind,resolution=size,uvCoverage=float(coverage.mean()),triangles=len(payload['vertices'])//3))
        if kind=='unit':
            for suffix,budget in [('',512),('-lod1',128)]:
                verts=json.loads((SOURCE/(key+suffix+'.json')).read_text())['vertices']
                if len(verts)>budget*3:raise ValueError(key+' exceeds triangle budget')
                data=bytearray()
                for v in verts:data+=struct.pack('<16f',*v['p'],round(v['part']),*v['n'],0,*v['t'],*v['uv'],0,0)
                data+=bytes((budget*3-len(verts))*64)
                (DATA/(key+suffix+'.bytes')).write_bytes(data)
(SOURCE/'texture-report.json').write_text(json.dumps(reports,indent=2))
print('Baked',len(reports),'UV texture sets')
