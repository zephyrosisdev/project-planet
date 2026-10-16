"""Render every authored model and eight directional unit impostors in Blender."""
import bpy, math, sys, json
from pathlib import Path
from mathutils import Vector

ROOT=Path(sys.argv[sys.argv.index('--')+1]);SOURCE=ROOT/'ArtSource/SyntheticFront'
GALLERY=ROOT/'TestResults/ArtPass/Gallery';SPRITES=ROOT/'Assets/MassiveArmy/Art/Impostors'
GALLERY.mkdir(parents=True,exist_ok=True);SPRITES.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'SyntheticFront.blend'))
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.film_transparent=True
scene.render.resolution_percentage=100;scene.render.resolution_x=512;scene.render.resolution_y=512
scene.world=bpy.data.worlds.new('Cool studio environment');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.19,.24,.31,1);scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.6
scene.view_settings.view_transform='AgX'
camdata=bpy.data.cameras.new('Catalogue camera');camera=bpy.data.objects.new('Catalogue camera',camdata);scene.collection.objects.link(camera);scene.camera=camera;camdata.type='ORTHO';camdata.clip_end=2000
def light(name,kind,energy,location,size=100):
    data=bpy.data.lights.new(name,kind);data.energy=energy;data.color=(.80,.90,1);obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=location;obj.rotation_euler=(-obj.location).to_track_quat('-Z','Y').to_euler()
    if kind=='AREA':data.shape='DISK';data.size=size
light('Large key','AREA',18000,(35,-45,70),50);light('Edge light','AREA',14000,(-45,20,45),40);light('Sun','SUN',2,(30,-40,80))
models=[o for o in bpy.data.objects if o.type=='MESH' and not o.name.endswith('LOD1')]
for o in bpy.data.objects:
    if o.type=='MESH':o.hide_render=True
for obj in models:
    key=obj.name
    if key=='building-command' and (ROOT/'ArtSource/HQ/building-command.mesh.gz').exists():continue
    original=obj.location.copy();obj.location=(0,0,0);obj.hide_render=False
    bpy.context.view_layer.update()
    mat=bpy.data.materials.new(key+' textured armour');mat.use_nodes=True;nodes=mat.node_tree.nodes;links=mat.node_tree.links;bsdf=nodes.get('Principled BSDF')
    uv=nodes.new('ShaderNodeUVMap');uv.uv_map=obj.data.uv_layers[0].name
    tex={}
    for suffix in ['albedo','metallic','normal']:
        t=nodes.new('ShaderNodeTexImage');t.image=bpy.data.images.load(str(ROOT/'Assets/MassiveArmy/Art/Textures'/(key+'-'+suffix+'.png')));t.image.colorspace_settings.name='sRGB' if suffix=='albedo' else 'Non-Color';links.new(uv.outputs['UV'],t.inputs['Vector']);tex[suffix]=t
    links.new(tex['albedo'].outputs['Color'],bsdf.inputs['Base Color'])
    channels=nodes.new('ShaderNodeSeparateColor');links.new(tex['metallic'].outputs['Color'],channels.inputs['Color']);links.new(channels.outputs['Red'],bsdf.inputs['Metallic'])
    invert=nodes.new('ShaderNodeMath');invert.operation='SUBTRACT';invert.inputs[0].default_value=1;links.new(tex['metallic'].outputs['Alpha'],invert.inputs[1]);links.new(invert.outputs[0],bsdf.inputs['Roughness'])
    normal=nodes.new('ShaderNodeNormalMap');normal.uv_map=uv.uv_map;links.new(tex['normal'].outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bsdf.inputs['Normal'])
    emission=nodes.new('ShaderNodeMath');emission.operation='MULTIPLY';emission.inputs[1].default_value=2;links.new(channels.outputs['Blue'],emission.inputs[0]);links.new(emission.outputs[0],bsdf.inputs['Emission Strength']);bsdf.inputs['Emission Color'].default_value=(.015,.65,1,1)
    for i in range(len(obj.data.materials)):obj.data.materials[i]=mat
    low=bpy.data.objects.get(key+'-LOD1')
    if low:
        for i in range(len(low.data.materials)):low.data.materials[i]=mat
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.fbx(filepath=str(ROOT/'Assets/MassiveArmy/Art/Models'/(key+'.fbx')),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',bake_space_transform=True,path_mode='RELATIVE')
    coords=[Vector(v) for v in obj.bound_box];height=max(v.z for v in coords);bottom=min(v.z for v in coords);centre=Vector((0,0,(height+bottom)*.5));span=max(obj.dimensions)*1.5;camdata.ortho_scale=span
    def pose(angle):
        camera.location=centre+Vector((math.sin(angle)*100,-math.cos(angle)*100,100));camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
    pose(math.pi*.25);scene.render.resolution_x=scene.render.resolution_y=512;scene.render.filepath=str(GALLERY/(key+'.png'));bpy.ops.render.render(write_still=True)
    if key.startswith('unit-'):
        scene.render.resolution_x=scene.render.resolution_y=128
        for direction in range(8):pose(direction*math.tau/8);scene.render.filepath=str(SPRITES/(key+'-'+str(direction)+'.png'));bpy.ops.render.render(write_still=True)
    obj.hide_render=True;obj.location=original;print('ART_RENDER',key,flush=True)
for obj in models:obj.hide_render=False
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'SyntheticFront.blend'))
print('Rendered',len(models),'models and 248 impostors',flush=True)
