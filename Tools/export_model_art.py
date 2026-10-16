"""Export artist edits from the packed blend without regenerating its geometry.

Edit mesh vertices/UVs in local coordinates. Library display offsets are excluded.
All budgets are checked; this exporter never silently decimates edited models.
"""
import bpy,bmesh,json,sys
from pathlib import Path
root=Path(sys.argv[sys.argv.index('--')+1]);source=root/'ArtSource/SyntheticFront'
bpy.ops.wm.open_mainfile(filepath=str(source/'SyntheticFront.blend'))
reports=[]
def export_payload(obj,key,near):
 data=obj.data.copy();bm=bmesh.new();bm.from_mesh(data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free()
 data.calc_loop_triangles();data.calc_tangents(uvmap=data.uv_layers[0].name)
 kind,id=key.split('-',1);budget=(512 if near else 128) if kind=='unit' else (2048 if near else 256)
 if len(data.loop_triangles)>budget:raise ValueError(key+' exceeds budget: '+str(len(data.loop_triangles)))
 part=data.attributes.get('SFPart');uv=data.uv_layers[0];vertices=[]
 for tri in data.loop_triangles:
  for li in tri.loops:
   loop=data.loops[li];p=data.vertices[loop.vertex_index].co;n=loop.normal;t=loop.tangent
   vertices.append(dict(p=[p.x,p.z,-p.y],n=[n.x,n.z,-n.y],t=[t.x,t.z,-t.y,loop.bitangent_sign],uv=list(uv.data[li].uv),part=part.data[loop.vertex_index].value if part else 0))
 payload=dict(kind=kind,id=id,vertices=vertices)
 if near:payload['polygons']=[dict(uv=[list(uv.data[i].uv) for i in p.loop_indices],material=p.material_index) for p in data.polygons]
 (source/(key+('' if near else '-lod1')+'.json')).write_text(json.dumps(payload))
 if near:
  extents=[max(v['p'][i] for v in vertices)-min(v['p'][i] for v in vertices) for i in range(3)]
  reports.append(dict(kind=kind,id=id,triangles=len(vertices)//3,vertices=len(vertices),width=extents[0],height=extents[1],depth=extents[2]))
 bpy.data.meshes.remove(data)
for obj in list(bpy.data.objects):
 if obj.type!='MESH' or obj.name.endswith('-LOD1'):continue
 key=obj.name
 if not key.startswith(('building-','unit-')):continue
 if key=='building-command' and (root/'ArtSource/HQ/building-command.mesh.gz').exists():
  hq=json.loads((root/'ArtSource/HQ/geometry-report.json').read_text());reports.append(dict(kind='building',id='command',triangles=hq['triangles'],vertices=hq['vertices'],width=hq['boundsMetres'][0],height=hq['boundsMetres'][1],depth=hq['boundsMetres'][2],source='ArtSource/HQ'));continue
 if any(abs(s-1)>1e-5 for s in obj.scale) or any(abs(a)>1e-5 for a in obj.rotation_euler):raise ValueError('Apply object rotation/scale before export: '+key)
 export_payload(obj,key,True);low=bpy.data.objects.get(key+'-LOD1')
 if low is None:raise ValueError('Missing medium mesh '+key)
 export_payload(low,key,False)
 original=obj.location.copy();obj.location=(0,0,0)
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
 bpy.ops.export_scene.fbx(filepath=str(root/'Assets/MassiveArmy/Art/Models'/(key+'.fbx')),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',bake_space_transform=True,path_mode='RELATIVE')
 obj.location=original
(source/'geometry-report.json').write_text(json.dumps(reports,indent=2))
print('Exported artist edits for',len(reports),'models')
