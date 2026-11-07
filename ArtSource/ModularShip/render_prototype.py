"""Run after build_modular_ship.py in Blender to save the assembled design preview."""
import bpy,math
from mathutils import Vector
scene=bpy.context.scene
roots={o.name.split('.')[0]:o for o in scene.objects if o.type=='EMPTY' and o.parent is None}
def duplicate_tree(source,name,at,angle=0):
 root=source.copy();root.name=name;scene.collection.objects.link(root);root.location=(at[0],at[2],at[1]);root.rotation_euler[2]=-math.radians(angle)
 for child in source.children:
  item=child.copy();item.data=child.data;scene.collection.objects.link(item);item.parent=root
 return root
duplicate_tree(roots['main-engine'],'Port main engine',(-30,0,-32))
roots['thruster'].rotation_euler[2]=-math.pi/2
for name,at,angle in [('Port forward thruster',(-28,0,2),90),('Starboard aft thruster',(28,0,-26),-90),('Starboard forward thruster',(28,0,2),-90)]:duplicate_tree(roots['thruster'],name,at,angle)
bpy.ops.mesh.primitive_cube_add(size=1,location=(0,-45,-6));ramp=bpy.context.object;ramp.name='Articulated loading ramp reference';ramp.dimensions=(12,14,.3);ramp.rotation_euler[0]=math.radians(-16);ramp.data.materials.append(next(m for m in bpy.data.materials if m.name.startswith('Ship_dark')))
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.render.resolution_x=1280;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Modular ship studio');scene.world.color=(.13,.13,.13)
for at,power,size in [((40,-30,65),60000,45),((-45,20,30),35000,35),((0,55,45),45000,30)]:
 data=bpy.data.lights.new('Studio area','AREA');data.energy=power;data.shape='DISK';data.size=size;o=bpy.data.objects.new('Studio area',data);scene.collection.objects.link(o);o.location=at;o.rotation_euler=(Vector((0,0,0))-o.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('Modular ship camera');o=bpy.data.objects.new('Modular ship camera',data);scene.collection.objects.link(o);o.location=(100,-130,75);o.rotation_euler=(Vector((0,-2,0))-o.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=130;scene.camera=o
scene.render.filepath=r'C:/Users/leeda/Documents/UnityProjects/project-planet/ArtSource/ModularShip/blender-prototype.png'
bpy.ops.wm.save_as_mainfile(filepath=r'C:/Users/leeda/Documents/UnityProjects/project-planet/ArtSource/ModularShip/ExpeditionModularShip.blend')
bpy.ops.render.render(write_still=True)
result={'render':scene.render.filepath,'objects':len(scene.objects)}
