import json,math,re
from pathlib import Path
root=Path('.')
catalog=json.loads((root/'Assets/MassiveArmy/Resources/MassiveArmy/ShipArt/ModularShipCatalogue.json').read_text())
defs=json.loads((root/'TestResults/Planetary/ModularShip/definitions.json').read_text())
checks=[]
for a in catalog['assets']:
 name=a['id'];spec=defs[name];n=len(a['vertices'])//3
 assert n>0 and len(a['normals'])==n*3 and len(a['colours'])==n*4
 assert len(a['triangles'])%3==0 and all(0<=i<n for i in a['triangles'])
 assert all(math.isfinite(v) for key in ['vertices','normals','colours'] for v in a[key])
 exported={s['name']:s for s in a['sockets']}
 for s in spec['sockets']:
  actual=exported[s['name']];assert actual['kind']==s['kind'],(name,s['name'])
  for axis in ['x','y','z']:assert abs(actual['position'][axis]-s['pose'][axis])<1e-5,(name,s['name'],axis)
  assert abs(actual['yaw']-s['pose']['yaw'])<1e-5,(name,s['name'],'yaw')
 checks.append(name+': finite mesh, valid indices and matching authored/runtime sockets')
hangar=next(a for a in catalog['assets'] if a['id']=='hangar')
for vehicle in ['SF-QB1','SF-LUV1']:
 text=(root/f'Assets/MassiveArmy/Resources/MassiveArmy/Vehicles/{vehicle}.asset').read_text()
 match=re.search(r'  size: \{x: ([\d.]+), y: ([\d.]+), z: ([\d.]+)\}',text);w,h,l=map(float,match.groups())
 assert w+.24<12 and h+.12<9
 # Conservative full vehicle bounding box through the straight boarding lane.
 for step in range(73):
  z=-37+step*.5;lo=(-w/2,-5,z+14-l/2);hi=(w/2,-5+h,z+14+l/2)
  for solid in hangar['solids']:
   p=solid['polygon'];bounds=([min(v['x'] for v in p),solid['bottom'],min(v['y'] for v in p)],[max(v['x'] for v in p),solid['top'],max(v['y'] for v in p)])
   assert not all(hi[i]>bounds[0][i]+.01 and lo[i]<bounds[1][i]-.01 for i in range(3)),(vehicle,z,solid)
 checks.append(vehicle+': fits 12m hatch and authored hangar boarding lane in 73 sampled poses')
report={'status':'PASS','scope':'Offline catalogue/socket and conservative vehicle envelope checks; not Unity physics or live driving validation.','checks':checks,'moduleTypes':len(catalog['assets']),'catalogueTriangles':sum(len(a['triangles'])//3 for a in catalog['assets'])}
(root/'TestResults/Planetary/ModularShip/asset-checks.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
