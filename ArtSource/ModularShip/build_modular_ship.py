"""Blender source for socketed expedition ship. Unity coordinates X right/Y up/Z forward.
Creates a separate scene and exports a metre-based mesh/collision/socket catalogue.
"""
import bpy, math, json, os
from mathutils import Vector
ROOT=r'C:/Users/leeda/Documents/UnityProjects/project-planet'
OUT=ROOT+'/ArtSource/ModularShip'; DATA=ROOT+'/Assets/MassiveArmy/Resources/MassiveArmy/ShipArt'
os.makedirs(DATA,exist_ok=True)
scene=bpy.data.scenes.new('Expedition_Modular_Ship');bpy.context.window.scene=scene
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
palette={'shell':(.72,.79,.84),'dark':(.055,.085,.12),'floor':(.15,.21,.26),'orange':(1,.29,.025),'cyan':(.025,.8,1),'steel':(.35,.43,.5)}
mats={}
for key,rgb in palette.items():
 m=bpy.data.materials.new('Ship_'+key);m.diffuse_color=(*rgb,1);m.use_nodes=True
 bsdf=m.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(*rgb,1);bsdf.inputs['Metallic'].default_value=.5;bsdf.inputs['Roughness'].default_value=.36
 if key=='cyan':bsdf.inputs['Emission Color'].default_value=(*rgb,1);bsdf.inputs['Emission Strength'].default_value=2
 mats[key]=m
assets=[]
def vec(p):return Vector((p[0],p[2],p[1]))
def xyz(p):return dict(x=p[0],y=p[1],z=p[2])
def begin(name,dims,at):
 global asset,root
 root=bpy.data.objects.new(name,None);scene.collection.objects.link(root);root.location=vec(at)
 asset=dict(id=name,family='ship',dimensions=xyz(dims),objects=[],solids=[],sockets=[]);assets.append(asset)
def box(name,p,size,mat='shell',solid=True,bevel=.07):
 bpy.ops.mesh.primitive_cube_add(size=1);o=bpy.context.object;o.name=name;o.parent=root;o.location=vec(p);o.scale=vec(size);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mats[mat]);o['ship_material']=mat
 if bevel>0:mod=o.modifiers.new('Armor chamfer','BEVEL');mod.width=bevel;mod.segments=1
 asset['objects'].append(o)
 if solid:asset['solids'].append(dict(polygon=[dict(x=p[0]+dx*size[0]/2,y=p[2]+dz*size[2]/2) for dx,dz in [(-1,-1),(1,-1),(1,1),(-1,1)]],bottom=p[1]-size[1]/2,top=p[1]+size[1]/2))
 return o
def socket(name,kind,p,yaw=0):
 asset['sockets'].append(dict(name=name,kind=kind,position=xyz(p),yaw=yaw,width=12,depth=9))
 marker=bpy.data.objects.new('Socket_'+name+'_'+kind,None);scene.collection.objects.link(marker);marker.parent=root;marker.location=vec(p);marker.rotation_euler[2]=-math.radians(yaw);marker.empty_display_type='ARROWS';marker.empty_display_size=.7;marker['socket_kind']=kind;marker['socket_yaw']=yaw
def room(name,dims,at):
 begin(name,dims,at);w,h,l=dims
 box('Recessed non-slip deck',(0,-h/2+.25,0),(w,.5,l),'floor',bevel=.015)
 box('Segmented pressure roof',(0,h/2-.25,0),(w,.5,l))
 for side in [-1,1]:
  if name=='hangar' and side==-1:
   for lo,hi in [(-l/2,-2),(10,l/2)]:box('Engineering port wall segment',(side*(w/2-.25),0,(lo+hi)/2),(.5,h,hi-lo),'dark')
   box('Engineering port header',(side*(w/2-.25),3.5,4),(.5,4,12),'dark')
  else:box('Inner pressure wall',(side*(w/2-.25),0,0),(.5,h,l),'dark')
  for n in range(max(1,int(l/6))):
   z=-l/2+(n+.5)*l/max(1,int(l/6))
   if not(name=='hangar' and side==-1 and -2<z<10):box('Chamfered armor panel',(side*(w/2+.18),.5,z),(.65,h*.68,l/max(1,int(l/6))-.28),'shell')
   box('Safety stripe',(side*(w/2+.55),-.8,z),(.12,.42,l/max(1,int(l/6))-.8),'orange',False,.02)
   box('Interior service lamp',(side*(w/2-.65),h/2-.65,z),(.15,.16,2),'cyan',False,.02)
  for z in [-l/2+.45,l/2-.45]:box('Socket collar rib',(side*(w/2-.7),0,z),(.9,h-.4,.9),'steel')
 for x in range(-int(w/2)+2,int(w/2),4):box('Deck seam',(x,-h/2+.51,0),(.045,.02,l-.4),'steel',False,0)
 socket('aft','Hull',(0,0,-l/2),180);socket('forward','Hull',(0,0,l/2),0)
room('hangar',(52,11,48),(0,1,-14))
for side in [-1,1]:
 socket('engine-'+str(side),'Engine',(side*30,-1,-18),180)
 for z in [-12,16]:socket('thruster-'+str(side)+'-'+str(z),'Thruster',(side*28,-1,z),90*side)
 box('Landing leg',(side*19,-6.7,12),(2.3,3,3),'dark');box('Landing foot',(side*19,-8,12),(4,.5,6),'steel')
 for z in [-21,-10,1]:box('Roof reinforcement',(side*12,5.65,z),(22,.6,2),'dark')
for side in [-1,1]:
 box('Aft pressure armor',(side*16,0,-24),(20,11,.6),'shell',False,.18)
 box('Aft recessed service panel',(side*16,-.4,-24.35),(17,7,.25),'dark',False,.12)
 for x in [side*10,side*20]:box('Aft armor spine',(x,.4,-24.55),(1,8,.4),'steel',False,.1)
 box('Forward shoulder armor',(side*17.6,0,24),(16.5,11,.5),'shell',False,.1)
for z in range(-20,22,6):
 for side in [-1,1]:
  box('Roof armor tile',(side*13,5.8,z),(24,.55,5.4),'shell',False,.22)
  box('Roof cooling grille',(side*15,6.12,z),(9,.12,1.4),'dark',False,.08)
for side in [-1,1]:
 box('Loading portal orange rail',(side*6.7,0,-24.65),(.75,9,.6),'orange',False,.18)
 box('Loading portal glow',(side*6.15,0,-24.95),(.13,7,.12),'cyan',False,.01)
box('Loading portal lintel',(0,4.8,-24.65),(14,1,.7),'dark',False,.18)
socket('core','Equipment',(18,-3.3,-16));socket('engineering','Hull',(-26,-2,4),-90)
# Forward collar is lower than the hangar's roof, matching a 7m cockpit/corridor.
asset['sockets'][0]['position']=xyz((0,-2,-24))
asset['sockets'][1]['position']=xyz((0,-2,24))
for child in root.children:
 if child.type=='EMPTY' and child.name.startswith(('Socket_aft','Socket_forward')):child.location.z=-2
room('corridor',(19,7,12),(0,-1,16))
room('cockpit',(19,7,16),(0,-1,30));socket('power','PowerInput',(8.5,-1.5,-6))
for side in [-1,1]:
 box('Glazing side pillar',(side*8.6,0,7),(.5,6,.6),'orange')
 box('Glazing header',(side*4.5,2.55,8),(8.8,.65,.7),'dark')
box('Pilot console',(0,-1.9,3),(4,1.1,1),'dark');box('Console display',(0,-1.3,3),(3.5,.08,.8),'cyan',False)
room('engineering',(12,7,12),(-32,-1,-10));socket('core','Equipment',(0,-1.3,0));
for side in [-1,1]:box('Equipment cabinet',(side*4,-1.7,1),(2,2.6,5),'steel')
begin('power-core',(5,3.4,7),(18,-2.3,-30))
box('Reactor base',(0,-1.4,0),(5,.5,7),'dark');box('Contained power core',(0,0,0),(2.5,2.4,4),'cyan');
for side in [-1,1]:
 box('Core shielding rail',(side*2.1,0,0),(.55,3.4,7),'steel')
 for z in [-2.8,0,2.8]:box('Core magnetic ring',(0,.2,z),(4.8,3.1,.4),'dark')
socket('mount','Equipment',(0,0,0));socket('power','PowerOutput',(0,1.6,3.6))
begin('main-engine',(8,7,18),(30,0,-32));box('Engine housing',(0,0,0),(8,7,16),'dark',True,.4)
for z in [-5,0,5]:box('Armor sleeve',(0,.1,z),(8.5,7.4,2),'shell',False,.35)
box('Exhaust throat',(0,0,-8.2),(6,5,.4),'cyan',False);socket('mount','Engine',(0,0,0),180);socket('power','PowerInput',(0,2,7.5))
begin('thruster',(4,4,6),(-28,0,-26));box('Thruster housing',(0,0,0),(4,4,6),'shell',True,.3);box('Vector exhaust',(0,0,-3.1),(3,3,.3),'cyan',False);socket('mount','Thruster',(0,0,0),180);socket('power','PowerInput',(0,1.8,2))
# Export evaluated bevel geometry in Unity local coordinates, with winding corrected for swapped Y/Z.
manifest=[];deps=bpy.context.evaluated_depsgraph_get()
for a in assets:
 verts=[];normals=[];colors=[];tri=[]
 for o in a['objects']:
  mesh=o.evaluated_get(deps).to_mesh();mesh.calc_loop_triangles();mat=o['ship_material'];rgb=palette[mat];emission=.8 if mat=='cyan' else .0
  for face in mesh.loop_triangles:
   start=len(verts)//3
   for ix in face.vertices:
    p=o.matrix_local@mesh.vertices[ix].co;n=face.normal
    verts.extend([round(p.x,5),round(p.z,5),round(p.y,5)]);normals.extend([n.x,n.z,n.y]);colors.extend([*rgb,emission])
   tri.extend([start,start+2,start+1])
  o.evaluated_get(deps).to_mesh_clear()
 manifest.append({k:v for k,v in a.items() if k!='objects'}|dict(vertices=verts,normals=normals,colours=colors,triangles=tri))
with open(DATA+'/ModularShipCatalogue.json','w') as f:json.dump(dict(version=1,assets=manifest),f,separators=(',',':'))
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/ExpeditionModularShip.blend')
result={'blend':OUT+'/ExpeditionModularShip.blend','catalogue':DATA+'/ModularShipCatalogue.json','modules':len(assets),'triangles':sum(len(a['triangles'])//3 for a in manifest)}
