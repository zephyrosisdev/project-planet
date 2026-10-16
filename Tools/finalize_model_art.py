import bpy,sys
from pathlib import Path
root=Path(sys.argv[sys.argv.index('--')+1]);path=root/'ArtSource/SyntheticFront/SyntheticFront.blend'
bpy.ops.wm.open_mainfile(filepath=str(path))
for obj in bpy.data.objects:
 if obj.type=='MESH' and obj.name.endswith('-LOD1'):
  near=bpy.data.objects.get(obj.name[:-5])
  if near:obj.location=near.location.copy()
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(path))
