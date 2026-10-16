"""Bespoke concept-led HQ. Blender 5.2, metres, Unity front +Z.

Editable bevelled components, trim UVs, merged render shell and vertex AO.
This intentionally imposes no triangle budget on the HQ prototype.
"""
import bpy,bmesh,math,json,sys,gzip,struct
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(sys.argv[sys.argv.index('--')+1]);OUT=ROOT/'ArtSource/HQ';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC'
parts=[];bands=[];assembly_moves=[]
def p(v):return Vector((v[0],-v[2],v[1]))
mat=bpy.data.materials.new('HQ eight-strip armour trim');mat.use_nodes=True
nodes=mat.node_tree.nodes;links=mat.node_tree.links;bs=nodes.get('Principled BSDF')
tex={}
for s in ['albedo','metallic','normal']:
    t=nodes.new('ShaderNodeTexImage');t.image=bpy.data.images.load(str(ROOT/f'Assets/MassiveArmy/Art/Textures/building-command-{s}.png'));t.image.colorspace_settings.name='sRGB' if s=='albedo' else 'Non-Color';tex[s]=t
links.new(tex['albedo'].outputs['Color'],bs.inputs['Base Color'])
channels=nodes.new('ShaderNodeSeparateColor');links.new(tex['metallic'].outputs['Color'],channels.inputs['Color']);links.new(channels.outputs['Red'],bs.inputs['Metallic'])
inv=nodes.new('ShaderNodeMath');inv.operation='SUBTRACT';inv.inputs[0].default_value=1;links.new(tex['metallic'].outputs['Alpha'],inv.inputs[1]);links.new(inv.outputs[0],bs.inputs['Roughness'])
norm=nodes.new('ShaderNodeNormalMap');links.new(tex['normal'].outputs['Color'],norm.inputs['Color']);links.new(norm.outputs['Normal'],bs.inputs['Normal'])
em=nodes.new('ShaderNodeMath');em.operation='MULTIPLY';em.inputs[1].default_value=1.25;links.new(channels.outputs['Blue'],em.inputs[0]);links.new(em.outputs[0],bs.inputs['Emission Strength']);bs.inputs['Emission Color'].default_value=(.0,.40,.85,1)
author=bpy.data.collections.new('EDITABLE COMPONENTS');scene.collection.children.link(author)
def finish(obj,name,band,bevel=.09):
    obj.name=name;obj['trim_strip']=band;obj.data.materials.append(mat)
    for col in list(obj.users_collection):col.objects.unlink(obj)
    author.objects.link(obj)
    if bevel:
        mod=obj.modifiers.new('Manufactured edge bevel','BEVEL');mod.width=bevel;mod.segments=3
    parts.append(obj);bands.append(band);return obj
def mesh(name,verts,faces,band=0,bevel=.09):
    d=bpy.data.meshes.new(name);d.from_pydata([p(v) for v in verts],[],faces);d.update()
    bm=bmesh.new();bm.from_mesh(d);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(d);bm.free()
    o=bpy.data.objects.new(name,d);author.objects.link(o);return finish(o,name,band,bevel)
def ring(w,d,cut):
    x=w/2;z=d/2;c=min(cut,x*.75,z*.75)
    return [(-x+c,-z),(x-c,-z),(x,-z+c),(x,z-c),(x-c,z),(-x+c,z),(-x,z-c),(-x,-z+c)]
def block(name,x,y,z,w,h,d,band=0,cut=.3,bevel=.08,top=1):
    r=ring(w,d,cut);verts=[(x+a,y-h/2,z+b) for a,b in r]+[(x+a*top,y+h/2,z+b*top) for a,b in r]
    return mesh(name,verts,[tuple(range(8,16))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)],band,bevel)
def beam(name,start,end,width,depth,band=1,bevel=.08):
    bpy.ops.mesh.primitive_cube_add(size=1,location=(p(start)+p(end))/2);o=bpy.context.object
    o.rotation_euler=(p(end)-p(start)).to_track_quat('Z','Y').to_euler();o.scale=(width,depth,(p(end)-p(start)).length)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);return finish(o,name,band,bevel)
def cyl(name,x,y,z,r,h,band=0,sides=32,top=1):
    vs=[]
    for yy,ratio in [(y-h/2,1),(y+h/2,top)]:
        for k in range(sides):a=k*math.tau/sides;vs.append((x+math.cos(a)*r*ratio,yy,z+math.sin(a)*r*ratio))
    return mesh(name,vs,[tuple(range(sides,sides*2))]+[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)],band,.025)
def annulus(name,x,y,z,r,width,band=5,sides=96):
    vs=[(x+math.cos(i*math.tau/sides)*rr,y,z+math.sin(i*math.tau/sides)*rr) for rr in [r,r-width] for i in range(sides)]
    return mesh(name,vs,[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)],band,0)
def antenna(x,y,z,h):
    cyl('Antenna socket',x,y+.15,z,.30,.3,2,12);cyl('Antenna mast',x,y+h/2,z,.065,h,1,12)
    cyl('Cyan antenna tip',x,y+h-.3,z,.11,.65,5,12)
    for yy in [y+.6,y+h-.7]:cyl('Mast collar',x,yy,z,.13,.10,2,12)
def bolts(x,y,z,w,d):
    for xx in [-w/2+.25,w/2-.25]:
        for zz in [-d/2+.25,d/2-.25]:cyl('Flush titanium fastener',x+xx,y,z+zz,.07,.03,1,8)
def vent(x,y,z,w,d):
    block('Recessed vent well',x,y,z,w,.08,d,3,.12,.015)
    for i in range(7):block('Vent fin',x-w*.4+i*w*.8/6,y+.065,z,w*.035,.08,d*.85,2,.015,.009)
def wall_window(name,a,b,y,h):
    beam(name+' black gasket',(a[0],y,a[1]),(b[0],y,b[1]),h+.32,.22,3,.025)
    # Beam local Z is length; local X is approximately vertical for horizontal edges.
    o=beam(name+' cyan glass',(a[0],y,a[1]),(b[0],y,b[1]),h,.24,5,.02)
    return o

# Armoured foundation follows the building mass: no generic square plinth.
block('Central recessed chassis',0,1.6,8,18,3.2,16,2,3,.16)
block('Main machinery deck',0,3.6,8,18,4.6,16,0,3,.16,top=.94)
block('Deck lower alloy lip',0,1.0,8,18.5,.38,16.5,1,3,.06)
# Layered command rotunda, eight bevelled sides, uninterrupted segmented glazing.
rotunda_begin=len(parts)
block('Rotunda undercut',0,7.0,1,17.7,3.0,16.5,3,4,.12)
block('Window sill armour',0,6.1,1,19.0,.65,17.6,1,4.2,.12)
rr=ring(18.0,16.6,4.1)
for i in range(8):
    a=rr[i];b=rr[(i+1)%8];length=math.dist(a,b);sections=max(1,round(length/2.1))
    for j in range(sections):
        t0=(j+.035)/sections;t1=(j+.965)/sections
        aa=(a[0]+(b[0]-a[0])*t0,a[1]+(b[1]-a[1])*t0+1);bb=(a[0]+(b[0]-a[0])*t1,a[1]+(b[1]-a[1])*t1+1)
        # Explicit vertical panes and mullions independent of beam orientation.
        dx=bb[0]-aa[0];dz=bb[1]-aa[1];nx=dz/length; nz=-dx/length
        vs=[(aa[0],6.8,aa[1]),(bb[0],6.8,bb[1]),(bb[0],8.25,bb[1]),(aa[0],8.25,aa[1])]
        mesh('Cyan command glazing',vs,[(0,1,2,3)],5,0)
        beam('Window mullion',(aa[0],6.65,aa[1]),(aa[0],8.42,aa[1]),.13,.22,2,.025)
block('Rotunda roof eave',0,8.65,1,19.0,.7,17.6,1,4.2,.10)
block('Sloping command roof',0,9.6,1,18.6,1.5,17.3,0,4.1,.13,top=.71)
# Separate shaped plates on each sloping roof facet, with genuine gasket gaps.
roof_ring=ring(18.6,17.3,4.1)
for i in range(8):
    a=Vector((roof_ring[i][0],8.85,roof_ring[i][1]+1));b=Vector((roof_ring[(i+1)%8][0],8.85,roof_ring[(i+1)%8][1]+1))
    c=Vector((roof_ring[(i+1)%8][0]*.71,10.35,roof_ring[(i+1)%8][1]*.71+1));e=Vector((roof_ring[i][0]*.71,10.35,roof_ring[i][1]*.71+1))
    center=(a+b+c+e)/4;n=(b-a).cross(e-a).normalized()
    if n.y<0:n=-n
    bed=[center+(v-center)*.92+n*.07 for v in [a,b,c,e]]
    mesh('Roof facet recessed gasket',bed,[(0,1,2,3)],3,0)
    plate=[center+(v-center)*.83+n*.10 for v in [a,b,c,e]]
    mesh('Individual sloping roof armour plate',plate,[(0,1,2,3)],0,0)
    for k in [0,1]:
        a0=plate[k];b0=plate[3-k];beam('Roof plate bright edge',a0+n*.035,b0+n*.035,.055,.055,1,.014)
    # Two dark engineering slots on broad faces, placed parallel to their pitch.
    if (b-a).length>4:
        for t in [.31,.68]:
            q=a.lerp(b,t).lerp(e.lerp(c,t),.65)+n*.14
            beam('Roof plate recessed service slot',q-(b-a).normalized()*.55,q+(b-a).normalized()*.55,.11,.06,2,.016)
block('Raised roof crown',0,10.6,.4,13.6,.4,11.6,1,2.7,.08)
block('Roof centre inset',0,10.82,.4,11.8,.12,10.1,2,2,.03)
for x in [-5.0,5.0]:
    beam('Roof longitudinal armour rail',(x,9.35,7.1),(x*.72,10.9,-3.0),.52,.28,1,.06)
for x in [-3.2,3.2]:
    block('Forward roof armour plate',x,10.5,4.0,4.0,.28,3.2,0,.7,.06)
    bolts(x,10.66,4,3.8,3)
block('Forward roof tactical display',0,10.42,6.0,3.2,.36,1.0,3,.14,.05)
block('Tactical display cyan slit',0,10.64,6.15,2.5,.05,.22,5,.04,.01)
rotunda_end=len(parts)

# High tapered split-core spire, tall armour shoulders and stepped collar stack.
spire_begin=len(parts)
block('Tower foundation',0,11.4,-3,8.2,2,8.6,2,1.5,.16)
block('Tower lower shoulder',0,14.0,-3,6.2,4.2,6.8,0,1.15,.16,top=.85)
block('Tower lower front inset bed',0,13.45,.28,2.75,2.7,.18,3,.35,.06)
block('Tower lower front alloy surround',0,13.45,.42,2.35,2.4,.22,1,.25,.07)
block('Tower lower front service panel',0,13.45,.56,1.85,1.95,.12,2,.19,.03)
block('Tower lower front cyan display',0,13.95,.66,1.40,.46,.07,5,.06,.02)
for x in [-.68,.68]:block('Tower lower warning label',x,12.92,.65,.25,.10,.04,6,.015,.01)
block('Tower black core',0,18.4,-3,3.15,8.1,3.3,3,.55,.10)
for x in [-.60,.60]:block('Split luminous core',x,18.5,-1.28,1.0,7.25,.22,5,.12,.05)
for y,w,d in [(14.6,4.8,4.9),(21.6,4.05,4.0),(25.5,2.75,2.65)]:
    block('Spire stepped alloy collar',0,y,-3,w,.65,d,1,.65,.10)
    block('Spire inset collar gasket',0,y-.4,-3,w*.91,.16,d*.91,2,.55,.02)
block('Upper tower tapered housing',0,23.6,-3,3.5,4.0,3.6,0,.55,.12,top=.72)
block('Upper tower cyan strip',0,23.5,-1.42,.48,3.2,.18,5,.05,.025)
block('Top tower radio housing',0,27,-3,2.15,2.8,2.2,1,.3,.09,top=.8)
block('Radio housing recessed face',0,27,-1.91,1.1,1.7,.12,2,.14,.025)
block('Radio housing narrow cyan face',0,27,-1.82,.20,1.4,.05,5,.02,.01)
antenna(0,28.4,-3,8.8);antenna(-1.2,25.7,-3.4,7.0);antenna(1.1,25.7,-3.3,4.7)
for x in [-3.1,3.1]:
    beam('Tower taper armour shoulder',(x,11.4,-.7),(x*.60,22.4,-2.0),1.15,2.35,1,.12)
    beam('Shoulder inset dark panel',(x,13,-1.92),(x*.65,20.4,-2.99),.61,.15,2,.055)
    beam('Shoulder cyan seam',(x*.92,15.8,-.62),(x*.77,18.5,-.95),.25,.13,5,.025)
spire_end=len(parts)
# Four heavy diagonal outriggers connect the tower to ground, separate armour faces.
for ex,endz,tx,tz,rear in [(-13.9,12.3,-.9,8.6,False),(8.3,15.1,3.3,8.6,False),(-8,-6,-.9,4,True),(9,-4,4,4,True)]:
    st=(ex,1.1,endz);en=(tx,11.4 if rear else 15.6,tz)
    beam('Outrigger dark structural core',st,en,1.2 if rear else 2.35,1.1 if rear else 2.05,2,.15)
    aa=(ex,1.7,endz+.26);bb=(tx,10.9 if rear else 15.1,tz+.3)
    armour=beam('Long diagonal silver armour',aa,bb,.95 if rear else 1.83,1.3 if rear else 2.23,0,.14)
    offset=armour.rotation_euler.to_matrix()@Vector((0,.69 if rear else 1.16,0));offset=Vector((offset.x,offset.z,-offset.y))
    cross=armour.rotation_euler.to_matrix()@Vector((.38 if rear else .77,0,0));cross=Vector((cross.x,cross.z,-cross.y))
    for sign in [-1,1]:beam('Outrigger narrow alloy border',Vector(aa)+offset+cross*sign,Vector(bb)+offset+cross*sign,.12,.07,1,.035)
    for t in [.17,.43,.69]:
        a=Vector(aa).lerp(Vector(bb),t)+offset;b=Vector(aa).lerp(Vector(bb),t+.12)+offset
        beam('Outrigger inset service plate',a,b,.55 if rear else 1.07,.055,2,.045)
        for shift in [-.40,.40]:
            beam('Outrigger recessed plate keyline',a+Vector((shift,0,0)),b+Vector((shift,0,0)),.025,.065,0,.008)
    a=Vector(aa).lerp(Vector(bb),.82)+offset;b=Vector(aa).lerp(Vector(bb),.92)+offset
    beam('Outrigger cyan status bar',a,b,.45,.06,5,.025)

# Raised satellite / landing-pad side annexes, offset like the concept.
for x,z in [(-14.1,2.1),(14.1,-6.7)]:
    begin=len(parts)
    block('Landing annex armoured pedestal',x,3,z,9.0,6,9.1,2,1.25,.15,top=.85)
    for side in [-1,1]:
        block('Annex vertical armour cheek',x+side*3.35,3.5,z,1.25,5.7,7.8,0,.45,.1,top=.80)
        block('Annex front cyan window',x+side*2.15,4.7,z+4.05,1.65,.7,.18,5,.12,.025)
    block('Landing annex continuous glass belt',x,5.15,z+4.05,5.8,.70,.19,5,.12,.025)
    for xx in [-2.15,-.72,.72,2.15]:block('Landing annex window mullion',x+xx,5.15,z+4.18,.13,.82,.12,2,.02,.02)
    side=-1 if x<0 else 1
    block('Landing annex outward glass belt',x+side*4.12,5.15,z,.15,.70,5.8,5,.05,.02)
    for zz in [-2.15,-.72,.72,2.15]:block('Landing annex side mullion',x+side*4.22,5.15,z+zz,.13,.82,.13,2,.02,.02)
    block('Landing pad lower chassis',x,6.05,z,9.4,.65,9.4,3,2.72,.1)
    block('Landing pad alloy surround',x,6.54,z,9.3,.36,9.3,1,2.72,.06)
    block('Landing pad inset dark deck',x,6.77,z,8.4,.12,8.4,2,1.5,.03)
    annulus('Cyan landing circle',x,6.85,z,3.8,.10)
    annulus('Landing circle alloy keyline',x,6.851,z,4,.035,1)
    # White arrow/aircraft mark, real geometry decal on the pad.
    mesh('Landing aircraft stencil',[(x-.9,6.86,z-.4),(x-.22,6.86,z-.1),(x-.18,6.86,z+1.15),(x+.18,6.86,z+1.15),(x+.23,6.86,z-.1),(x+.95,6.86,z-.42),(x+.1,6.86,z-.7)],[(0,1,2,3,4,5,6)],7,0)
    for s in [-1,1]:
        block('Landing edge amber marker',x+s*3.25,6.87,z+2.8,.30,.045,.55,6,.03,.01)
        antenna(x+s*3.1,6.9,z-3.0,2.4 if s<0 else 3.1)
    bolts(x,6.77,z,8.7,8.7)
    for xx in [-2.35,2.35]:
        beam('Landing annex swept face armour',(x+xx,1,z+4.55),(x+xx,5.65,z+3.15),2.45,.48,0,.12)
        beam('Landing annex bright plate edge',(x+xx-1.05,1,z+4.85),(x+xx-1.05,5.65,z+3.45),.14,.12,1,.04)
        block('Landing annex panel dark inset',x+xx,2.2,z+4.25,1.3,.65,.16,2,.14,.025)
    sign=-1 if x<0 else 1
    for zz in [-2.35,2.35]:
        beam('Landing annex exterior swept armour',(x+sign*4.5,1,z+zz),(x+sign*3.15,5.7,z+zz),.5,2.5,0,.12)
        block('Landing annex side recess',x+sign*4.3,2.2,z+zz,.16,.6,1.4,2,.06,.025)

    assembly_moves.append((begin,len(parts),x,z,-11.55 if x<0 else 16.1,2.42 if x<0 else 5.1,.75 if x<0 else .65,.75 if x<0 else .65,1,0))
# Low independent service pods produce the irregular characteristic perimeter.
for x,z,w,d in [(-8.3,13.2,7,7),(8.3,13.2,7,7),(-13,-10.3,7,6),(13,-12.8,7,6)]:
    begin=len(parts)
    block('Utility pod recessed chassis',x,2.1,z,w*.88,4.2,d*.88,3,.9,.1)
    block('Utility pod tapered armour',x,2.3,z,w*.97,4.2,d*.95,0,.9,.13,top=.83)
    block('Pod rooftop rim',x,4.5,z,w*.84,.42,d*.84,1,.7,.07)
    block('Pod rooftop inset',x,4.74,z,w*.7,.08,d*.7,2,.5,.02)
    block('Pod forward window',x,3.1,z+d*.48,w*.55,.55,.20,5,.09,.025)
    for xx in [-w*.34,w*.34]:
        block('Pod corner reinforcing plate',x+xx,2.5,z+d*.47,w*.14,3.9,.28,1,.12,.06)
        block('Recessed corner vent',x+xx,1.5,z+d*.64,w*.06,1.4,.1,3,.02,.01)
    for zz in [-d*.29,d*.29]:block('Pod roof separate armour slab',x,4.82,z+zz,w*.58,.25,d*.17,0,.13,.04)
    vent(x,4.81,z,w*.35,d*.28);antenna(x-.9,4.9,z-.6,3.2);antenna(x+.8,4.9,z-.6,1.8)
    bolts(x,4.98,z,w*.67,d*.67)
    # Both outward-facing sides have layered inset plates and luminous glazing.
    side=-1 if x<0 else 1
    sx=x+side*w*.46
    block('Pod side black panel bed',sx,2.4,z,.24,3.3,d*.63,3,.05,.03)
    block('Pod side upper alloy plate',sx+side*.13,3.65,z,.16,.72,d*.60,1,.05,.04)
    block('Pod side lower armoured panel',sx+side*.14,1.45,z,.18,1.40,d*.60,0,.06,.06)
    block('Pod side glowing glass belt',sx+side*.24,2.85,z,.10,.5,d*.55,5,.025,.02)
    for zz in [-d*.23,0,d*.23]:block('Pod side window mullion',sx+side*.32,2.85,z+zz,.11,.65,.10,2,.02,.02)
    for zz in [-d*.32,d*.32]:
        beam('Pod side inclined armour jamb',(sx+side*.21,.65,z+zz),(sx-side*.36,4.35,z+zz),.55,.6,1,.06)
    for xx in [-w*.19,w*.19]:
        block('Pod front recessed panel',x+xx,2.0,z+d*.475,w*.27,1.7,.17,2,.12,.04)
        for j in range(3):block('Pod front recessed vent slot',x+xx+(j-1)*w*.055,2.0,z+d*.57,w*.025,.85,.04,3,.015,.01)

    nx,nz,sx,sz,sy=(-9.1,16,1,1,1) if z>0 and x<0 else ((9.1,16,1,1,1) if z>0 else ((-17.5,2.94,.57,.85,.74) if x<0 else (12.4,19.1,.85,.85,1)))
    assembly_moves.append((begin,len(parts),x,z,nx,nz,sx,sz,sy,0))
# Wide front gatehouse: recessed cyan portal, strong segmented overhead frame.
portal_begin=len(parts)
entrance=block('Front entrance black chassis',0,2.7,12.0,10.2,5.4,5.5,3,1.1,.12)
# Open the portal face: an actual recess, with the door visible inside it.
bm=bmesh.new();bm.from_mesh(entrance.data);bm.faces.ensure_lookup_table();bmesh.ops.delete(bm,geom=[bm.faces[5]],context='FACES');bm.to_mesh(entrance.data);bm.free()
for x in [-4.0,4.0]:
    block('Entrance shoulder',x,3.6,12.0,2.8,6.8,6.6,0,.65,.15,top=.78)
    beam('Entrance swept alloy jamb',(x*1.17,1.1,15.4),(x*.95,6.8,11.7),1.5,1.7,1,.15)
    block('Portal cyan vertical trim',x*.86,2.7,15.45,.43,3.0,.18,5,.06,.025)
block('Entrance overhead armour',0,6.75,12.8,8.4,1.6,4.6,1,.65,.13,top=.86)
block('Entrance lintel recess',0,6.5,15.11,5.6,.85,.12,3,.14,.025)
block('Entrance overhead cyan window',0,6.5,15.2,4.8,.40,.1,5,.1,.02)
block('Door inset back',0,2.9,13.25,5.7,4.5,.16,2,.25,.02)
for x in [-1.8,1.8]:
    block('Door side inset cyan',x,3.0,13.38,.32,2.75,.12,5,.04,.02)
    block('Door folded panel',x*.55,2.8,13.45,1.35,3.4,.18,0,.12,.04)
block('Door centre lock seam',0,2.8,13.58,.08,3.1,.1,3,.01,.01)
block('Recessed portal cyan crossbar',0,4.65,13.57,4.6,.16,.08,5,.03,.015)
for x in [-2.5,2.5]:block('Recessed portal inner cyan jamb',x,2.9,13.57,.16,3.3,.08,5,.03,.015)
block('Portal interior blue threshold',0,1.1,13.9,5.4,.06,1.4,4,.08,.01)
mesh('Entrance sloped ramp',[(-3.3,.12,19),(3.3,.12,19),(3.3,1.28,14.9),(-3.3,1.28,14.9)],[(0,1,2,3)],2,.02)
for x in [-3.3,3.3]:beam('Ramp alloy edge',(x,.15,19),(x,1.33,14.9),.22,.20,1,.03)
for i in range(7):
    z=15.35+i*.49;y=1.28-(z-14.9)*1.16/4.1
    beam('Ramp tread',( -2.75,y+.025,z),(2.75,y+.025,z),.045,.065,0,.01)
for x in [-2.9,2.9]:block('Ramp amber threshold',x,1.33,14.97,.23,.035,.36,6,.02,.005)
assembly_moves.append((portal_begin,len(parts),0,0,0,3,1,1,1,0))
# Exposed connecting deck pipes and cyan strips.
flank_begin=len(parts)
for x in [-9.0,9.0]:
    block('Flank cyan machinery band',x,3.9,6.9,3.7,.5,.15,5,.06,.025)
    block('Flank segmented roof armour',x,5.7,5.1,4.5,.35,3.3,1,.65,.07)
    vent(x,5.91,5.1,2.4,1.2)
    for j in range(3):block('Deck amber inspection marking',x-1+j*.3,6.01,6.15,.14,.02,.27,6,.01,0)
    sign=-1 if x<0 else 1
    for zz in [-2.7,1.4,5.5]:
        block('Flank machinery dark recess',sign*11.65,3.4,zz,.32,2.6,3.5,3,.10,.04)
        block('Flank cyan window strip',sign*11.85,3.85,zz,.12,.50,2.85,5,.025,.02)
        block('Flank lower armour panel',sign*11.88,2.75,zz,.16,1.10,3.0,0,.05,.04)
        for j in [-1,0,1]:block('Flank window mullion',sign*11.98,3.85,zz+j*.90,.14,.70,.1,2,.02,.02)
        beam('Flank structural pillar',(sign*12.1,1.1,zz+1.8),(sign*10.9,5.7,zz+1.8),.7,.75,1,.08)
assembly_moves.append((flank_begin,len(parts),0,0,0,8,1,1,1,0))
spire_detail_begin=len(parts)
for x in [-3.6,3.6]:
    for y in [12.1,13.9]:
        block('Tower shoulder armour layering',x,y,-.65,1.5,.75,1.3,1,.18,.07)
        block('Tower shoulder cyan service slit',x,y-.23,.045,.65,.20,.10,5,.02,.02)
for x in [-1.15,1.15]:
    block('Spire core vertical silver frame',x,18.2,-1.1,.32,6.5,.33,1,.06,.04)
    for y in [15.4,20.6]:block('Spire core transverse retainer',x,y,-1.0,.55,.45,.55,1,.08,.05)
block('Spire central split metal spine',0,18.5,-1.09,.17,7.2,.25,1,.035,.025)
for x in [-2.6,2.6]:vent(x,11.04,-3.1,1.2,2.7)
spire_detail_end=len(parts)
roof_begin=len(parts)
for x in [-7.6,7.6]:
    block('Armoured flank raised roof',x,6.08,-.1,4.1,.35,12.8,0,.6,.07)
    for zz in [-4.1,0,4.1]:
        block('Flank roof separate panel gasket',x,6.29,zz,3.6,.07,3.55,3,.4,.025)
        block('Flank roof layered silver slab',x,6.37,zz,3.3,.14,3.25,0,.38,.04)
        vent(x,6.49,zz,1.65,1.2);bolts(x,6.47,zz,3.0,2.9)
    beam('Flank long alloy rail',(x-1.7,6.4,-6),(x-1.7,6.4,6),.12,.13,1,.025)
assembly_moves.append((roof_begin,len(parts),0,0,0,7,1,1,1,0))
for x in [-3.1,3.1]:
    block('Entrance to rotunda roof bridge',x,5.95,8.1,3.8,.7,5.5,0,.6,.08)
    block('Bridge recessed roof panel',x,6.35,8.0,2.9,.12,3.5,2,.4,.035)
    beam('Bridge longitudinal alloy keyline',(x,6.5,6.4),(x,6.5,9.6),.11,.11,1,.025)
for x in [-7.6,7.6]:
    begin=len(parts)
    block('Rear stepped equipment shoulder',x,6.9,-8.5,5.8,2.0,5.0,0,.7,.1,top=.8)
    block('Rear equipment roof inset',x,7.98,-8.5,4.2,.14,3.5,2,.4,.035)
    vent(x,8.10,-8.5,2.8,1.9)
    block('Rear equipment cyan side window',x,6.9,-5.93,2.8,.40,.10,5,.06,.02)
    bolts(x,8.13,-8.5,4,3)

    assembly_moves.append((begin,len(parts),x,-8.5,-6.4 if x<0 else 10,2 if x<0 else 12,1,1,1,.5))
# Actual concave, ribbed satellite dish tilted above starboard shoulder.
dish_begin=len(parts)
cx,cy,cz=4.9,18.7,-4.7;axis=Vector((-.30,.68,.67)).normalized();u=axis.cross(Vector((0,1,0))).normalized();v=axis.cross(u);r=1.75;vs=[];rings=12;sides=64
for j in range(rings+1):
    rr=r*j/rings;depth=.55*(rr/r)**2
    for k in range(sides):
        a=k*math.tau/sides;q=Vector((cx,cy,cz))+axis*depth+(u*math.cos(a)+v*math.sin(a))*rr;vs.append(q)
faces=[]
for j in range(rings):
    for k in range(sides):a=j*sides+k;b=j*sides+(k+1)%sides;faces.append((a,b,b+sides,a+sides))
mesh('Concave satellite reflector',vs,faces,1,0)
for k in range(12):
    a=k*math.tau/12;last=Vector((cx,cy,cz))
    for j in range(1,7):
        rr=r*j/6;q=Vector((cx,cy,cz))+axis*(.55*(rr/r)**2+.035)+(u*math.cos(a)+v*math.sin(a))*rr
        beam('Dish radial reinforcement',last,q,.035,.035,2,.006);last=q
beam('Dish receiver', (cx,cy,cz),Vector((cx,cy,cz))+axis*1.1,.13,.13,2,.025)
beam('Dish articulated support',(3,13,-5),(cx,cy,cz),.36,.36,2,.06)
assembly_moves.append((dish_begin,len(parts),0,0,1.7,12,1,1,1,0))

# Evaluate real bevel geometry, generate strip UVs per face, retaining editable sources.
for index,obj in enumerate(parts):
    inverse=obj.matrix_world.inverted()
    for vert in obj.data.vertices:
        q=obj.matrix_world@vert.co
        if q.z>11:q.z=11+(q.z-11)*.55
        if rotunda_begin<=index<rotunda_end:q.x=q.x*.62+1.2;q.y=-1+(q.y+1)*.64-10;q.z+=.8
        if spire_begin<=index<spire_end or spire_detail_begin<=index<spire_detail_end:q.x=q.x*.85+1.7;q.y=-(-3+(-q.y+3)*.85+10)
        for begin,end,ox,oz,nx,nz,sx,sz,sy,dy in assembly_moves:
            if begin<=index<end:q.x=nx+(q.x-ox)*sx;q.y=-(nz+(-q.y-oz)*sz);q.z=q.z*sy+dy
        q.y+=3
        vert.co=inverse@q
    obj.data.update()
merged_v=[];merged_f=[];merged_uv=[]
deps=bpy.context.evaluated_depsgraph_get()
for o,band in zip(parts,bands):
    ev=o.evaluated_get(deps);d=ev.to_mesh();base=len(merged_v)
    merged_v.extend(o.matrix_world@vert.co for vert in d.vertices)
    for face in d.polygons:
        ids=list(face.vertices);merged_f.append([base+i for i in ids]);coords=[o.matrix_world@d.vertices[i].co for i in ids]
        n=o.matrix_world.to_3x3()@face.normal;drop=max(range(3),key=lambda k:abs(n[k]));axes=[k for k in range(3) if k!=drop]
        lo=[min(c[k] for c in coords) for k in axes];span=[max(c[k] for c in coords)-lo[j] for j,k in enumerate(axes)]
        # Horizontal dimension follows strip, minor dimension traverses its width.
        if span[1]>span[0]:axes.reverse();lo.reverse();span.reverse()
        merged_uv.append([(.02+.96*(c[axes[0]]-lo[0])/max(span[0],1e-5),(band+.09+.82*(c[axes[1]]-lo[1])/max(span[1],1e-5))/8) for c in coords])
    ev.to_mesh_clear();o.hide_render=True;o.hide_set(True)
d=bpy.data.meshes.new('HQ production shell');d.from_pydata(merged_v,[],merged_f);d.update()
uv=d.uv_layers.new(name='HQ_Trim');
for face,coords in zip(d.polygons,merged_uv):
    for li,coord in zip(face.loop_indices,coords):uv.data[li].uv=coord
o=bpy.data.objects.new('building-command',d);scene.collection.objects.link(o);d.materials.append(mat)
# Triangle normals keep the bevel smooth without softening broad planar armour.
bm=bmesh.new();bm.from_mesh(d);bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(d);bm.free();d.update();d.calc_tangents(uvmap='HQ_Trim')
print('HQ authored components',len(parts),'triangles',len(d.polygons),flush=True)
# Vertex ambient occlusion: local creases and overlaps survive cheap game lighting.
bvh=BVHTree.FromPolygons([v.co for v in d.vertices],[list(f.vertices) for f in d.polygons],all_triangles=True)
ao={};values=[]
for loop in d.loops:
    vert=d.vertices[loop.vertex_index];n=loop.normal;key=(loop.vertex_index,round(n.x,3),round(n.y,3),round(n.z,3))
    if key not in ao:
        t=n.cross(Vector((0,0,1)) if abs(n.z)<.9 else Vector((1,0,0))).normalized();b=n.cross(t);blocked=0
        for k in range(12):
            a=k*2.399963;c=math.sqrt((k+.5)/12);direction=(n*math.sqrt(1-c*c)+(t*math.cos(a)+b*math.sin(a))*c).normalized()
            hit=bvh.ray_cast(vert.co+n*.04,direction,4.5)
            if hit[0] is not None:blocked+=1-min(hit[3]/4.5,1)*.5
        ao[key]=max(.30,1-blocked/12*.75)
    values.append(ao[key])
attr=d.color_attributes.new(name='ShellAO',type='FLOAT_COLOR',domain='CORNER')
for li,val in enumerate(values):attr.data[li].color=(val,val,val,1)
vertices=[]
for face in d.polygons:
    # Blender is right-handed; reflect X and winding for Unity's front +Z frame.
    for li in reversed(face.loop_indices):
        loop=d.loops[li];q=d.vertices[loop.vertex_index].co;n=loop.normal;t=loop.tangent
        vertices.append(dict(p=[-q.x,q.z,-q.y],n=[-n.x,n.z,-n.y],t=[-t.x,t.z,-t.y,-loop.bitangent_sign],uv=list(d.uv_layers[0].data[li].uv),c=[values[li]]*3+[1],part=0))
with gzip.open(OUT/'building-command.mesh.gz','wb',compresslevel=6) as stream:
    stream.write(b'HQM1');stream.write(struct.pack('<I',len(vertices)))
    for vertex in vertices:stream.write(struct.pack('<16f',*(vertex['p']+vertex['n']+vertex['t']+vertex['uv']+vertex['c'])))
minimum=[min(v['p'][k] for v in vertices) for k in range(3)];maximum=[max(v['p'][k] for v in vertices) for k in range(3)]
extents=[maximum[k]-minimum[k] for k in range(3)]
assert all(abs(v)<=20.01 for k in [0,2] for v in [minimum[k],maximum[k]]), 'HQ geometry leaves its reserved 40m footprint'
(OUT/'geometry-report.json').write_text(json.dumps(dict(triangles=len(vertices)//3,vertices=len(vertices),components=len(parts),boundsMetres=extents,minimumMetres=minimum,maximumMetres=maximum,textureSize=2048,trimStrips=8,aoSamples=12),indent=2))
bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
bpy.ops.export_scene.fbx(filepath=str(OUT/'building-command.fbx'),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',bake_space_transform=True,path_mode='RELATIVE')
quick='--quick' in sys.argv
scene.render.engine='BLENDER_EEVEE' if quick else 'CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True
scene.render.resolution_x=scene.render.resolution_y=768 if quick else 1024;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.film_transparent=True
scene.world=bpy.data.worlds.new('HQ studio');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.24,.30,.38,1);scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.35
scene.view_settings.view_transform='Standard';scene.view_settings.exposure=-.45
for name,loc,energy,size in [('Key',(-15,-35,60),32000,40),('Fill',(35,-10,25),13000,35),('Rim',(10,35,40),35000,25)]:
    ld=bpy.data.lights.new(name,'AREA');ld.energy=energy;ld.shape='DISK';ld.size=size;ob=bpy.data.objects.new(name,ld);scene.collection.objects.link(ob);ob.location=loc;ob.rotation_euler=(Vector((0,0,12))-ob.location).to_track_quat('-Z','Y').to_euler()
cam=bpy.data.cameras.new('Concept comparison');co=bpy.data.objects.new('Concept comparison',cam);scene.collection.objects.link(co);scene.camera=co;cam.type='ORTHO';cam.ortho_scale=40
def pose(x,y,z):
    co.location=(x,y,z);co.rotation_euler=(Vector((-6.23,-13.32,14))-co.location).to_track_quat('-Z','Y').to_euler()
pose(-68.23,-103.32,64)
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CommandCentre.blend'))
scene.render.filepath=str(OUT/'concept-view.png');bpy.ops.render.render(write_still=True)
for name,xyz in ([] if quick else [('front',(0,-100,60)),('rear',(-65,85,75))]):
    pose(*xyz);scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
print('HQ export and reference-angle renders complete',flush=True)
