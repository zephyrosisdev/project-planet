"""Export edits to the HQ production shell without regenerating the model."""
import bpy,bmesh,gzip,struct,json,sys
from pathlib import Path
root=Path(sys.argv[sys.argv.index('--')+1]);out=root/'ArtSource/HQ'
bpy.ops.wm.open_mainfile(filepath=str(out/'CommandCentre.blend'))
obj=bpy.data.objects.get('building-command')
if obj is None or obj.type!='MESH':raise ValueError('Missing HQ production shell')
if any(abs(a)>1e-6 for a in obj.rotation_euler) or any(abs(a-1)>1e-6 for a in obj.scale) or obj.location.length>1e-6:raise ValueError('Apply shell transforms and retain the ground-plane origin before exporting')
data=obj.data.copy();bm=bmesh.new();bm.from_mesh(data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free();data.update()
if not data.uv_layers:raise ValueError('Retain the HQ trim UV layer')
data.calc_tangents(uvmap=data.uv_layers[0].name);ao=data.color_attributes.get('ShellAO')
if ao is None or ao.domain!='CORNER':raise ValueError('Retain or rebake the per-corner ShellAO colours')
triangles=len(data.polygons);bounds=[max(v.co[k] for v in data.vertices)-min(v.co[k] for v in data.vertices) for k in range(3)]
if bounds[0]>40.01 or bounds[1]>40.01:raise ValueError('HQ exceeds the reserved 40 x 40 metre footprint')
with gzip.open(out/'building-command.mesh.gz','wb',compresslevel=6) as stream:
    stream.write(b'HQM1');stream.write(struct.pack('<I',triangles*3))
    for face in data.polygons:
        for li in reversed(face.loop_indices):
            loop=data.loops[li];p=data.vertices[loop.vertex_index].co;n=loop.normal;t=loop.tangent;uv=data.uv_layers[0].data[li].uv;c=ao.data[li].color
            if not (0<=uv.x<=1 and 0<=uv.y<=1):raise ValueError('HQ UV outside trim atlas')
            stream.write(struct.pack('<16f',-p.x,p.z,-p.y,-n.x,n.z,-n.y,-t.x,t.z,-t.y,-loop.bitangent_sign,uv.x,uv.y,*c))
report=json.loads((out/'geometry-report.json').read_text());report.update(triangles=triangles,vertices=triangles*3,boundsMetres=[bounds[0],bounds[2],bounds[1]],manuallyEdited=True)
(out/'geometry-report.json').write_text(json.dumps(report,indent=2))
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
bpy.ops.export_scene.fbx(filepath=str(out/'building-command.fbx'),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',bake_space_transform=True,path_mode='RELATIVE')
bpy.data.meshes.remove(data)
print('Exported HQ artist edits:',triangles,'triangles; no decimation')
