"""Run in Blender. Creates a separate scene; leaves existing scene objects intact.
Unity coordinates in this authoring file are X/right, Y/up, Z/depth. Exported
meshes, collision prisms and named sockets share one metre-based manifest.
"""
import bpy, math, json, os, re
from mathutils import Vector

ROOT = os.environ.get('COLONY_PROJECT_ROOT', r'C:/Users/leeda/Documents/UnityProjects/project-planet')
SOURCE = ROOT+'/ArtSource/MartianColony'
EXPORT = ROOT+'/Assets/MassiveArmy/Art/ColonyPrototypes'
DATA = ROOT+'/Assets/MassiveArmy/Resources/MassiveArmy/ColonyPrototypes'
for path in (SOURCE, EXPORT, DATA): os.makedirs(path, exist_ok=True)
scene = bpy.data.scenes.new('Martian_Colony_Prototypes')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
palette = {
 'shell':(.78,.82,.85), 'edge':(.12,.17,.22), 'orange':(1,.31,.035),
 'cyan':(.03,.75,1), 'floor':(.19,.24,.28), 'dark':(.035,.055,.075),
 'glass':(.08,.3,.4), 'warm':(1,.62,.12), 'green':(.15,.48,.17),
 'red':(.75,.09,.06), 'steel':(.42,.48,.53), 'bed':(.17,.38,.52)
}
materials={}
for name, rgb in palette.items():
 m=bpy.data.materials.new('Colony_'+name); m.diffuse_color=(*rgb,1); m.use_nodes=True
 bsdf=m.node_tree.nodes.get('Principled BSDF'); bsdf.inputs['Base Color'].default_value=(*rgb,1)
 bsdf.inputs['Metallic'].default_value=.45 if name in ('edge','steel') else .12
 bsdf.inputs['Roughness'].default_value=.4
 if name in ('cyan','warm'):
  bsdf.inputs.get('Emission Color',bsdf.inputs.get('Emission')).default_value=(*rgb,1); bsdf.inputs['Emission Strength'].default_value=1.8
 materials[name]=m

assets=[]; current=None
def vec(p): return Vector((p[0],p[2],p[1]))
def jvec(p): return dict(x=round(p[0],6),y=round(p[1],6),z=round(p[2],6))
def begin(name, family, dims, at):
 global current
 collection=bpy.data.collections.new(name); scene.collection.children.link(collection)
 root=bpy.data.objects.new(name,None); collection.objects.link(root); root.location=vec(at)
 current=dict(id=name,family=family,dimensions=jvec(dims),objects=[],solids=[],sockets=[],root=root,collection=collection)
 assets.append(current)
 return current
def link(obj, colour, detail=False):
 for collection in list(obj.users_collection): collection.objects.unlink(obj)
 current['collection'].objects.link(obj); obj.parent=current['root']; obj.data.materials.append(materials[colour]); obj['colony_colour']=colour; obj['detail']=detail
 current['objects'].append(obj)
 return obj
def solid(p,size,yaw=0):
 x,y,z=p; w,h,d=size; a=math.radians(yaw)
 poly=[]
 for px,pz in ((-w/2,-d/2),(w/2,-d/2),(w/2,d/2),(-w/2,d/2)):
  poly.append(dict(x=x+math.cos(a)*px+math.sin(a)*pz,y=z-math.sin(a)*px+math.cos(a)*pz))
 current['solids'].append(dict(polygon=poly,bottom=y-h/2,top=y+h/2))
def box(name,p,size,colour='shell',collision=False,yaw=0,detail=False,bevel=.025):
 bpy.ops.mesh.primitive_cube_add(size=1)
 obj=bpy.context.object; obj.name=current['id']+'_'+name; obj.location=vec(p);obj.scale=vec(size); obj.rotation_euler[2]=-math.radians(yaw)
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel>0:
  mod=obj.modifiers.new('Prototype edge chamfer','BEVEL');mod.width=bevel;mod.segments=1
  bpy.context.view_layer.objects.active=obj;bpy.ops.object.modifier_apply(modifier=mod.name)
 link(obj,colour,detail)
 if collision:solid(p,size,yaw)
 return obj
def cylinder(name,p,radius,height,colour='steel',detail=False):
 bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=radius,depth=height)
 obj=bpy.context.object;obj.name=current['id']+'_'+name;obj.location=vec(p);link(obj,colour,detail)
 current['solids'].append(dict(polygon=[dict(x=p[0]+math.cos(k*math.pi/4)*radius,y=p[2]+math.sin(k*math.pi/4)*radius) for k in range(8)],bottom=p[1]-height/2,top=p[1]+height/2))
 return obj
def socket(name,kind,p,yaw=0,width=0,depth=0):
 record=dict(name=name,kind=kind,position=jvec(p),yaw=yaw,width=width,depth=depth)
 current['sockets'].append(record)
 obj=bpy.data.objects.new('SOCKET_'+name,None);current['collection'].objects.link(obj);obj.parent=current['root'];obj.location=vec(p);obj.rotation_euler[2]=-math.radians(yaw);obj.empty_display_type='ARROWS';obj.empty_display_size=.35;obj['socket_kind']=kind;obj['socket_name']='SOCKET_'+name
def portal(z,half,height=3):
 # Standard 1.5 x 2.6 m clear aperture. Orange chamfered gateway, cyan status strips.
 for x in (-.9,.9):box('Port post',(x,1.35,z),(.24,2.7,.25),'orange')
 box('Port lintel',(0,2.72,z),(1.95,.28,.25),'orange')
 for x in (-.77,.77):box('Seal status',(x,1.38,z-.14),(.045,2.3,.035),'cyan',detail=True,bevel=0)
 box('Threshold',(0,.11,z),(1.75,.12,.6),'edge')
def room(name,style,width,depth,at,ports=2):
 begin('room-'+name,'room',(width,3,depth),at)
 w=width/2;d=depth/2
 box('Deck',(0,.1,0),(width,.2,depth),'floor',True)
 box('Roof',(0,2.92,0),(width,.16,depth),'shell',True)
 for z in (-d+.1,d-.1):
  # Doorway remains open; wall and collision share the same actual aperture.
  for sign in (-1,1):box('End panel',(sign*(w+.75)/2,1.55,z),(w-.75,2.7,.2),'shell',True)
  box('Over door',(0,2.8,z),(1.5,.4,.2),'edge',True);portal(z,w)
  socket('Port_'+('front' if z<0 else 'rear'),'HabitatPort',(0,.2,z+(-.1 if z<0 else .1)),180 if z<0 else 0)
 for x in (-w+.1,w-.1):
  if ports==4:
   for sign in (-1,1):box('Junction side',(x,1.55,sign*(d+.75)/2),(.2,2.7,d-.75),'shell',True)
   box('Junction lintel',(x,2.8,0),(.2,.4,1.5),'edge',True)
   socket('Port_'+('left' if x<0 else 'right'),'HabitatPort',(x+(-.1 if x<0 else .1),.2,0),270 if x<0 else 90)
  else:
   box('Window sill',(x,.5,0),(.2,.6,depth),'shell',True)
   box('Window head',(x,2.6,0),(.2,.6,depth),'shell',True)
   for z in (-d+.2,0,d-.2):box('Window mullion',(x,1.6,z),(.22,1.6,.16),'edge',True)
   # Recessed glazing frames with open prototype apertures keep interiors readable.
   for z in (-d/2,d/2):box('Window lower trim',(x,1,z),(.26,.07,d-.25),'orange',detail=True)
 for x in (-w+.22,w-.22):
  for z in (-d+.22,d-.22):box('Corner spine',(x,1.5,z),(.32,3,.32),'orange')
 for x in (-w+.35,w-.35):box('Floor guide',(x,.208,0),(.045,.016,depth-.5),'cyan',detail=True,bevel=0)
 # Repeated exterior ribs and panel breaks keep the kit recognisable at gameplay range.
 for z in (-d+.55,d-.55):
  for sign in (-1,1):
   box('Armoured corner post',(sign*(w-.16),1.48,z),(.38,2.96,.38),'orange',bevel=.12)
   obj=box('Chamfered shoulder',(sign*(w-.46),2.78,z),(.72,.36,.4),'orange',bevel=.06)
   obj.rotation_euler[1]=sign*math.radians(38)
  box('Roof cross brace',(0,3.05,z),(width-.75,.26,.38),'edge',bevel=.07)
 for side in (-1,1):
  for z in (-d+.2,0,d-.2):box('Panel seam',(side*(w+.008),.45,z),(.025,.48,.045),'edge',detail=True,bevel=0)
  box('Lower armour band',(side*(w+.015),.22,0),(.07,.25,depth-.25),'edge',bevel=.025)
 box('Roof service pod',(0,3.18,.4),(min(1.8,width-.8),.4,1.5),'edge',bevel=.1)
 for z in (-.1,.15,.4,.65,.9):box('Cooling fin',(0,3.4,z),(min(1.5,width-1),.06,.08),'steel',detail=True,bevel=0)
 if style in ('command','life-support','lab'):
  for x in (-.4,.4):cylinder('Communications service',(x,3.72,.4),.16,1,'orange')
 if style=='medical':
  box('Medical cross horizontal',(-w+.65,1.8,-d-.012),(1,.2,.035),'red',detail=True)
  box('Medical cross vertical',(-w+.65,1.8,-d-.035),(.2,.8,.035),'red',detail=True)

 if style=='habitat':
  for side in (-1,1):
   box('Bunk base',(side*(w-.8),.5,.5),(1.1,.6,2.1),'edge',True,detail=True)
   box('Mattress',(side*(w-.8),.88,.5),(1.05,.16,2),'bed',detail=True)
   box('Pillow',(side*(w-.8),1,-.15),(.75,.14,.4),'shell',detail=True)
 elif style in ('command','lab','medical'):
  for side in (-1,1):
   box('Station',(side*(w-.8),.65,0),(1.05,1.1,depth-1.5),'shell',True,detail=True)
   box('Console',(side*(w-.78),1.3,.4),(.9,.08,1.4),'edge',detail=True)
   box('Display',(side*(w-.78),1.5,.4),(.7,.3,.05),'cyan',detail=True)
  if style=='medical':box('Medical insignia',(w+.02,1.6,.5),(.03,.8,.2),'red',detail=True)
 elif style=='greenhouse':
  for side in (-1,1):
   box('Growing bed',(side*(w-.9),.5,0),(1.25,.6,depth-1.2),'shell',True,detail=True)
   for z in (-1.5,-.5,.5,1.5):
    for k in (-.3,.3):cylinder('Hydroponic crop',(side*(w-.9)+k,1.05,z),.18,.6,'green',True)
 elif style=='life-support':
  for side in (-1,1):
   for z in (-1.3,0,1.3):cylinder('Oxygen reservoir',(side*(w-.7),1.4,z),.4,2.4,'steel',True)
   box('Environmental console',(side*(w-.65),2.6,0),(.9,.2,3.8),'cyan',detail=True)
 elif style in ('workshop','warehouse'):
  if style=='workshop':
   for x in (-2.25,2.25):socket('Machine_'+str(x),'MachineMount',(x,.2,0),0,3.4,4.6)
  else:
   for x in (-2.25,2.25):
    for z in (-2.25,2.25):socket('Storage_'+str(x)+'_'+str(z),'MachineMount',(x,.2,z),0,2.6,2.6)
  for x in (-w+.65,w-.65):
   for z in (-d+.8,d-.8):box('Wall locker',(x,1.1,z),(.8,1.8,.8),'edge',True,detail=True)
 elif style=='airlock':
  for side in (-1,1):box('Decon cabinet',(side*(w-.35),1.2,.3),(.4,2,.8),'edge',True,detail=True)
  box('Airlock status',(0,2.5,-d-.05),(1,.12,.08),'cyan',detail=True)
 socket('Floor','FloorCenter',(0,0,0))
 return current

room_specs=[('HabitatRoom','habitat',6,6),('MedicalRoom','medical',6,6),('ResearchRoom','lab',6,6),('CommandRoom','command',6,6),('LifeSupportRoom','life-support',6,6),('AirlockRoom','airlock',3,3),('CorridorRoom','corridor',3,6),('JunctionRoom','junction',6,6),('GreenhouseRoom','greenhouse',6,6),('WorkshopRoom','workshop',9,9),('WarehouseRoom','warehouse',9,9)]
for i,(name,style,w,d) in enumerate(room_specs):room(name,style,w,d,((i%4)*13,0,(i//4)*13),4 if name=='JunctionRoom' else 2)

def machine(name,at):
 sizes={'workbench':(1.5,1.2,1.0),'generator':(2.4,2.8,2.4),'sluice':(2.4,1.5,4),'foundry':(2.8,2.6,3.6),'press':(2,2.4,2),'wire-mill':(2,2,2),'assembler':(2.4,2.6,2.6),'munitions':(2.4,2.6,2.6),'refinery':(2.6,3,3.2),'pump':(1.5,1.5,1.5),'store':(2,2,2),'shielded-store':(2.2,2.2,2.2)}
 w,h,d=sizes[name];begin('machine-'+name,'machine',(w,h,d),at)
 box('Skid',(0,.12,0),(w,.24,d),'edge',True)
 if name=='workbench':
  box('Work surface',(0,1,0),(w,.15,d),'shell',True)
  for x in (-w/2+.12,w/2-.12):box('Leg',(x,.5,0),(.16,.85,d-.1),'orange',True)
  box('Tool panel',(0,1.6,d/2-.1),(w,.9,.12),'edge',True)
  for x in (-.5,0,.5):box('Tools',(x,1.6,d/2-.18),(.12,.45,.06),'cyan',detail=True)
 elif name in ('store','shielded-store'):
  for x in (-w/2+.1,w/2-.1):box('Storage side',(x,h/2,0),(.2,h,d),'shell',True)
  box('Back',(0,h/2,d/2-.1),(w,h,.2),'shell',True)
  for y in (.3,1,h-.12):box('Shelf',(0,y,0),(w,.12,d),'edge',True)
  for y in (.55,1.3):
   for x in (-.45,.45):box('Stored crate',(x,y,0),(.68,.48,1.2),'orange',detail=True)
  if name=='shielded-store':box('Shielded access',(0,h/2,-d/2),(w-.2,h-.25,.22),'steel',True)
 elif name=='sluice':
  for x in (-w/2+.2,w/2-.2):box('Wash trough wall',(x,.85,0),(.2,1.4,d),'shell',True)
  box('Wash channel',(0,.4,0),(w-.3,.12,d-.1),'steel',True)
  for z in (-1.4,-.7,0,.7,1.4):box('Riffle',(0,.5,z),(w-.5,.15,.12),'orange',detail=True)
  box('Feed hopper',(0,1.25,d/2-.55),(w,.5,1),'edge',True)
  box('Water feed',(-w/2-.12,1.3,0),(.15,.15,d),'cyan',detail=True)
 else:
  # Open operator face and side access around a visible working core.
  for x in (-w/2+.15,w/2-.15):box('Machine column',(x,h/2,0),(.3,h,d),'shell',True)
  box('Backplane',(0,h/2,d/2-.1),(w,h,.2),'edge',True)
  box('Crown',(0,h-.15,0),(w,.3,d),'shell',True)
  for x in (-w/2+.3,w/2-.3):box('Orange spine',(x,h/2,-d/2-.02),(.18,h,.12),'orange')
  if name in ('generator','refinery','pump'):
   for x in (-w*.22,w*.22):cylinder('Process vessel',(x,h*.48,0),w*.2,h*.65,'steel')
   for x in (-w*.25,w*.25):
    cylinder('Service stack',(x,h+.35,d*.2),.15,.8,'orange')
    cylinder('Stack cap',(x,h+.8,d*.2),.2,.15,'edge',True)
  elif name=='foundry':
   cylinder('Crucible',(0,.9,.2),.65,1.3,'edge')
   cylinder('Molten charge',(0,1.57,.2),.53,.025,'warm',True)
   box('Casting lip',(0,.45,-d*.36),(1.2,.12,.8),'warm',detail=True)
  else:
   box('Working bed',(0,.65,0),(w-.45,.2,d-.4),'steel',True)
   box('Actuator',(0,h*.65,.2),(.65,h*.4,.7),'orange',True)
   box('Workpiece',(0,.85,-.3),(.8,.14,.8),'warm',detail=True)
  box('Operator display',(w/2+.1,1.3,-d*.25),(.12,.45,.5),'cyan',detail=True)
 socket('Input','ConveyorInput',(0,.65,d/2),0)
 socket('Output','ConveyorOutput',(0,.65,-d/2),180)
 socket('Power','Power',(-w/2,.5,0),270)
 if name in ('sluice','pump','refinery'):socket('Fluid','Fluid',(w/2,.8,0),90)
 return current

for i,name in enumerate(('workbench','generator','sluice','foundry','press','wire-mill','assembler','munitions','refinery','pump','store','shielded-store')):machine(name,(65+(i%4)*9,0,(i//4)*9))

# The fifteen original sockets keep their dimensions and stable serialized enum IDs.
legacy=['FoundationSquare','FoundationTriangle','Wall','DoorwaySingle','DoorwayDouble','DoorFrame','GarageFrame','DoorSingle','DoorDouble','GarageDoor','CeilingSquare','CeilingTriangle','StairsStraight','Stairs90','Stairs180']
def prism(name,poly,bottom,top,colour='shell'):
 verts=[tuple(vec((x,y,z))) for y in (bottom,top) for x,z in poly];n=len(poly)
 # Unity-to-Blender reflection reverses winding here as well.
 faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update();obj=bpy.data.objects.new(name,mesh);current['collection'].objects.link(obj);obj.parent=current['root'];obj.data.materials.append(materials[colour]);obj['colony_colour']=colour;current['objects'].append(obj)
for i,name in enumerate(legacy):
 begin('base-'+name,'base',(3,3,3),(115+(i%5)*5,0,(i//5)*5))
 tri='Triangle' in name;poly=[(-1.5,-.8660254),(1.5,-.8660254),(0,1.7320508)] if tri else [(-1.5,-1.5),(1.5,-1.5),(1.5,1.5),(-1.5,1.5)]
 if name.startswith('Foundation') or name.startswith('Ceiling'):
  h=3 if name.startswith('Foundation') else .1;prism(name,poly,0,h,'shell' if h==3 else 'floor')
  box('Deck marking',(0,h+.005,0),(1,.014,.09),'orange',detail=True,bevel=0)
 elif name=='Wall':
  box('Panel',(0,1.5,0),(3,3,.1),'shell');box('Trim',(0,.12,-.07),(2.9,.2,.08),'orange')
 elif name.startswith('Stairs'):
  if name=='StairsStraight':
   for k in range(12):box('Step',(0,(k+1)*.125,-1.375+k*.25),(2.9,(k+1)*.25,.25),'floor',bevel=0)
  else:
   for k in range(6):box('Lower',(-.9,(k+1)*.125,-1.35+k*.3),(1.2,(k+1)*.25,.3),'floor',bevel=0)
   box('Landing',(-.9 if name=='Stairs90' else 0,.75,.9),(1.2 if name=='Stairs90' else 3,1.5,1.2),'floor',bevel=0)
   for k in range(6):
    p=(-.15+k*.3,(1.5+(k+1)*.25)/2,.9) if name=='Stairs90' else (.9,(1.5+(k+1)*.25)/2,.15-k*.3)
    size=(.3,1.5+(k+1)*.25,1.2) if name=='Stairs90' else (1.2,1.5+(k+1)*.25,.3)
    box('Upper',p,size,'floor',bevel=0)
 elif name in ('DoorSingle','DoorDouble','GarageDoor'):
  w=.96 if name=='DoorSingle' else 2 if name=='DoorDouble' else 2.64;h=2.16 if name!='GarageDoor' else 2.7
  box('Leaf',(0,h/2+.02,0),(w,h,.08),'edge');box('Inset',(0,h*.55,-.05),(w-.12,h-.3,.035),'shell')
  box('Door status',(w*.32,h*.5,-.08),(.045,h*.6,.025),'cyan',detail=True,bevel=0)
 else:
  half=1.04 if name=='DoorwayDouble' else 1.36 if name=='GarageFrame' else .52;top=2.75 if name=='GarageFrame' else 2.2
  for sign in (-1,1):box('Jamb',(sign*(1.5+half)/2,1.5,0),(1.5-half,3,.1),'shell')
  box('Header',(0,(3+top)/2,0),(2*half,3-top,.1),'shell')
  for sign in (-1,1):box('Seal',(sign*(half+.06),top/2,-.08),(.12,top,.08),'orange')

# Export a single source of truth; runtime never needs Blender or one object per piece.
bpy.context.view_layer.update()
manifest={'version':1,'assets':[]}
depsgraph=bpy.context.evaluated_depsgraph_get()
def mesh_data(asset,lod=False):
 vertices=[];normals=[];colours=[];indices=[]
 for obj in asset['objects']:
  if lod and obj.get('detail',False):continue
  evaluated=obj.evaluated_get(depsgraph);mesh=evaluated.to_mesh();mesh.calc_loop_triangles()
  mat=obj.get('colony_colour','shell');rgb=palette[mat];emission=1 if mat in ('cyan','warm') else 0
  transform=asset['root'].matrix_world.inverted()@obj.matrix_world
  normal_matrix=transform.to_3x3().inverted().transposed()
  for triangle in mesh.loop_triangles:
   for vi in reversed(triangle.vertices):
    v=transform@mesh.vertices[vi].co;n=(normal_matrix@triangle.normal).normalized()
    vertices.extend(round(a,6) for a in (v.x,v.z,v.y));normals.extend(round(a,6) for a in (n.x,n.z,n.y));colours.extend((*rgb,emission));indices.append(len(indices))
  evaluated.to_mesh_clear()
 return dict(vertices=vertices,normals=normals,colours=colours,triangles=indices)
for asset in assets:
 record={k:asset[k] for k in ('id','family','dimensions','solids','sockets')};record.update(mesh_data(asset));record['lod']=mesh_data(asset,True);manifest['assets'].append(record)
 bpy.ops.object.select_all(action='DESELECT')
 for obj in list(asset['collection'].objects):obj.select_set(True)
 asset['root'].select_set(True);bpy.context.view_layer.objects.active=asset['root']
 # Native FBX is supplied for artist iteration. Runtime consumes the same exported manifest.
 original=asset['root'].location.copy();asset['root'].location=(0,0,0);bpy.context.view_layer.update()
 renamed=[];displaced=[]
 for obj in list(asset['collection'].objects):
  if obj.type!='EMPTY' or not obj.name.startswith('SOCKET_'):continue
  desired=obj.get('socket_name',re.sub(r'\.\d{3}$','',obj.name));other=bpy.data.objects.get(desired)
  if other is not None and other!=obj:displaced.append((other,other.name));other.name='TEMP_SOCKET_'+str(other.as_pointer())
  renamed.append((obj,obj.name));obj.name=desired
 bpy.ops.export_scene.fbx(filepath=EXPORT+'/'+asset['id']+'.fbx' ,use_selection=True,object_types={'MESH','EMPTY'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False)
 for obj,old_name in renamed:obj.name=old_name
 for obj,old_name in displaced:obj.name=old_name
 asset['root'].location=original
with open(DATA+'/Catalogue.json','w',encoding='utf-8') as f:json.dump(manifest,f,separators=(',',':'))

world=bpy.data.worlds.new('Colony studio');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.11,.14,.19,1);world.node_tree.nodes['Background'].inputs[1].default_value=.7;scene.world=world
bpy.ops.object.light_add(type='SUN',location=(0,0,25));sun=bpy.context.object;sun.name='Colony studio sun';sun.rotation_euler=(.4,-.3,-.5);sun.data.energy=2.2
bpy.ops.object.light_add(type='AREA',location=(18,-12,28));light=bpy.context.object;light.name='Colony studio softbox';light.data.energy=3500;light.data.shape='DISK';light.data.size=30
bpy.ops.mesh.primitive_plane_add(size=300,location=(50,15,-.08));bpy.context.object.name='Colony studio ground';bpy.context.object.data.materials.append(materials['dark'])
bpy.ops.object.camera_add();camera=bpy.context.object;camera.name='Colony presentation';camera.data.type='ORTHO';scene.camera=camera
scene.render.engine='BLENDER_EEVEE_NEXT' if bpy.app.version < (5,0,0) and bpy.app.version >= (4,2,0) else 'BLENDER_EEVEE'
if hasattr(scene,'eevee') and hasattr(scene.eevee,'use_gtao'):scene.eevee.use_gtao=True;scene.eevee.gtao_distance=3
if hasattr(scene,'eevee') and hasattr(scene.eevee,'use_bloom'):scene.eevee.use_bloom=True
scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard';scene.view_settings.look='Medium High Contrast' if bpy.app.version<(4,0,0) else 'Medium High Contrast';scene.render.image_settings.file_format='PNG'
def render(name,target,eye,scale):
 camera.location=Vector(eye);camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale;scene.render.filepath=SOURCE+'/'+name+'.png';bpy.ops.render.render(write_still=True)
render('ColonyRooms',(18,13,1),(50,-43,43),60)
render('IndustrialMachines',(78,9,1),(103,-23,26),41)
# Human-height view through a furnished room and a roof-cut gallery of interiors.
camera.data.type='PERSP';camera.data.lens=22
render('HabitatInterior',(0,1,1.4),(0,-4.8,1.8),8)
camera.data.type='ORTHO'
hidden=[]
for asset in assets:
 if asset['family']!='room':continue
 for obj in asset['objects']:
  if any(token in obj.name for token in ('_Roof','_Cooling','_Communications')):
   hidden.append(obj);obj.hide_render=True
render('ColonyInteriors',(18,13,1),(50,-43,43),60)
for obj in hidden:obj.hide_render=False
bpy.ops.wm.save_as_mainfile(filepath=SOURCE+'/ColonyPrototypes.blend',copy=True)
result={'assets':len(assets),'triangles':sum(len(x['triangles'])//3 for x in manifest['assets']),'manifest':DATA+'/Catalogue.json','blend':SOURCE+'/ColonyPrototypes.blend','renders':[SOURCE+'/ColonyRooms.png',SOURCE+'/IndustrialMachines.png']}
