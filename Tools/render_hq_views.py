"""Render the saved HQ without rebuilding or changing its geometry."""
import bpy,sys
from pathlib import Path
from mathutils import Vector
root=Path(sys.argv[sys.argv.index('--')+1]);out=root/'ArtSource/HQ'
bpy.ops.wm.open_mainfile(filepath=str(out/'CommandCentre.blend'));scene=bpy.context.scene
for image in bpy.data.images:
    if image.name.startswith('building-command-'):
        image.filepath=str(root/'Assets/MassiveArmy/Art/Textures'/image.name)
        image.unpack(method='REMOVE');image.reload();image.pack()
scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True
scene.render.resolution_x=scene.render.resolution_y=1024
bpy.ops.wm.save_as_mainfile(filepath=str(out/'CommandCentre.blend'))
for name,xyz in [('concept-view',(-68.23,-103.32,64)),('front',(-6.23,-113.32,60)),('rear',(-65,85,75))]:
    target=Vector((-6.23,-13.32,14)) if name=='concept-view' else Vector((0,0,12))
    scene.camera.location=xyz;scene.camera.rotation_euler=(target-scene.camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(out/(name+'.png'));bpy.ops.render.render(write_still=True)
print('HQ final studio views complete',flush=True)
