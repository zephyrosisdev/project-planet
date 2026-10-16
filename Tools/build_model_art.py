"""Blender source generator for the Synthetic Front catalogue.

Run in a fresh background Blender process with --python and -- <project root>.
Coordinates in authoring helpers are Unity metres: X width, Y up, Z front.
Faces on the underside are omitted. Geometry and UVs are editable in the blend.
"""
import bpy, bmesh, math, json, sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(sys.argv[sys.argv.index('--') + 1])
OUT = ROOT / 'Assets/MassiveArmy/Art/Models'
SOURCE = ROOT / 'ArtSource/SyntheticFront'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
catalog = json.loads((ROOT / 'Assets/MassiveArmy/Resources/MassiveArmy/RtsCatalog.json').read_text())
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
palette = [(0.29,0.34,0.41,1),(0.58,0.65,0.73,1),(0.035,0.05,0.065,1),
           (0.01,0.55,0.72,1),(0.025,0.10,0.15,1),(0.045,0.075,0.15,1),(0.065,0.08,0.10,1)]
materials = []
for i,color in enumerate(palette):
    m=bpy.data.materials.new(['Armour','Edge alloy','Rubber','Team light','Glass','Solar','Vent'][i]);m.diffuse_color=color
    materials.append(m)
objects=[]
collection=None
simple_panels=False

def point(v): return (v[0],-v[2],v[1])
def mesh(name, vertices, faces, material=0, part=0):
    data=bpy.data.meshes.new(name);data.from_pydata([point(v) for v in vertices],[],faces);data.update()
    obj=bpy.data.objects.new(name,data);collection.objects.link(obj);objects.append(obj)
    for m in materials:data.materials.append(m)
    for p in data.polygons:p.material_index=material
    a=data.attributes.new('SFPart','FLOAT','POINT')
    for value in a.data:value.value=part
    return obj

def box(x,y,z,sx,sy,sz,mat=0,part=0,bevel=.12):
    # Eight corners in horizontal X/Z; cut corners make real chamfered armour.
    b=min(sx,sz)*bevel;hx=sx*.5;hz=sz*.5
    ring=[(-hx,-hz),(hx,-hz),(hx,hz),(-hx,hz)] if simple_panels else [(-hx+b,-hz),(hx-b,-hz),(hx,-hz+b),(hx,hz-b),(hx-b,hz),(-hx+b,hz),(-hx,hz-b),(-hx,-hz+b)]
    count=len(ring);v=[(x+a,y-sy*.5,z+c) for a,c in ring]+[(x+a*.93,y+sy*.5,z+c*.93) for a,c in ring]
    faces=[tuple(range(count,count*2))]+[(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)]
    return mesh('Chamfered panel',v,faces,mat,part)

def wedge(x,y,z,sx,sy,sz,mat=0,part=0):
    v=[(x-sx/2,y,z-sz/2),(x+sx/2,y,z-sz/2),(x+sx/2,y,z+sz/2),(x-sx/2,y,z+sz/2),
       (x-sx*.43,y+sy,z-sz*.42),(x+sx*.43,y+sy,z-sz*.42),(x+sx*.43,y+sy*.4,z+sz*.48),(x-sx*.43,y+sy*.4,z+sz*.48)]
    return mesh('Sloping armour',v,[(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat,part)

def tube(x,y,z,r,h,mat=0,sides=8,part=0,axis='y',top=.9):
    v=[]
    for offset,ratio in [(-h/2,1),(h/2,top)]:
        for k in range(sides):
            a=k*math.tau/sides;c=math.cos(a)*r*ratio;s=math.sin(a)*r*ratio
            v.append((x+c,y+offset,z+s) if axis=='y' else (x+c,y+s,z+offset) if axis=='z' else (x+offset,y+c,z+s))
    faces=[tuple(range(sides,sides*2))]+[(k,(k+1)%sides,(k+1)%sides+sides,k+sides) for k in range(sides)]
    # Horizontal cylinders expose their rear face; vertical bottom caps are unnecessary.
    if axis!='y':faces.append(tuple(reversed(range(sides))))
    return mesh('Octagonal mechanism',v,faces,mat,part)

def antenna(x,y,z,h):
    tube(x,y+h*.5,z,.08,h,1,4);box(x,y+h*.78,z,.16,h*.18,.16,3,bevel=0)

def strip(x,y,z,sx,sy,sz,part=0):return box(x,y,z,sx,sy,sz,3,part,0)

def armour_detail(w,d,h):
    # Separate roof plates, recessed vent banks and reinforced front foundation.
    for x in [-w*.24,0,w*.24]:
        box(x,1.67,-d*.34,w*.19,.16,d*.18,0)
        box(x,1.68,d*.35,w*.19,.18,d*.15,1)
        strip(x,1.82,d*.35,w*.12,.06,d*.08)
    for x in [-w*.43,w*.43]:
        box(x,2.0,0,w*.09,1.2,d*.20,6)
        box(x,2.68,0,w*.10,.14,d*.22,1)

def rotate_component(obj,pivot,angle):
    # Pose armour parts around Unity X without introducing skeletal objects.
    pivot=Vector(point(pivot));c=math.cos(angle);s=math.sin(angle)
    for v in obj.data.vertices:
        p=v.co-pivot;v.co=pivot+Vector((p.x,c*p.y-s*p.z,s*p.y+c*p.z))

def dish(x,y,z,r,tilt=.65):
    # Concave dish with front aperture, radial ribs and central receiver.
    v=[(x,y,z-.35*r)]+[(x+math.cos(k*math.tau/12)*r,y+math.sin(k*math.tau/12)*r*tilt,z+.3*r) for k in range(12)]
    mesh('Radar reflector',v,[(0,k+1,(k+1)%12+1) for k in range(12)],1)
    tube(x,y,z+.4*r,.11,r,3,6,axis='z')

def buttresses(w,d,h=3):
    for x in [-w*.37,w*.37]:
        for z in [-d*.32,d*.32]:wedge(x,1,z,w*.17,h,d*.20,1);strip(x,2,z+d*.09,w*.09,.35,.15)

def dome(x,y,z,r,mat=4):
    vertices=[(x,y+r,z)];rings=3;sides=12
    for j in range(1,rings+1):
        a=j*math.pi/2/rings
        vertices.extend((x+math.sin(a)*r*math.cos(k*math.tau/sides),y+math.cos(a)*r,z+math.sin(a)*r*math.sin(k*math.tau/sides)) for k in range(sides))
    faces=[(0,1+k,1+(k+1)%sides) for k in range(sides)]
    for j in range(rings-1):
        for k in range(sides):a=1+j*sides+k;b=1+j*sides+(k+1)%sides;faces.append((a,b,b+sides,a+sides))
    mesh('Faceted dome',vertices,faces,mat)

def guns(x,y,z,count=1,length=3,part=0):
    for k in range(count):
        q=x+(k-(count-1)/2)*.65
        tube(q,y,z+length*.5,.20,length,1,6,part,'z');tube(q,y,z+length,.27,.3,2,6,part,'z')

def missiles(x,y,z,count=4,scale=1,part=0):
    for k in range(count):
        q=x+(k-(count-1)/2)*.75*scale
        tube(q,y,z,.28*scale,3*scale,1,6,part,'z');tube(q,y,z+1.7*scale,.28*scale,.6*scale,1,6,part,'z',.05)
        strip(q,y+.26*scale,z,.12*scale,.05*scale,2*scale,part)

def building(d):
    id=d['id'];w=d['footprintWidth']*8*.91;depth=d['footprintDepth']*8*.91
    if id=='wall':
        box(0,3,0,2.2,6,2.2,0);strip(0,5.7,0,2.4,.18,2.4)
        for k,(x,z,sx,sz) in enumerate([(2.4,0,3.2,1.5),(0,2.4,1.5,3.2),(-2.4,0,3.2,1.5),(0,-2.4,1.5,3.2)]):
            box(x,3,z,sx,5.6,sz,0,part=k+1);strip(x,4.7,z,sx,.22,sz,part=k+1)
        return
    if id=='gate':
        for x in [-9.5,9.5]:box(x,4,0,4,8,4,0);wedge(x,0,0,5,4,depth,1);strip(x,5.5,2,2,.6,.2);antenna(x,8,0,4)
        box(0,7.7,0,16,.8,1.5,1);box(0,3.8,0,15.5,7.2,1,0,part=7);strip(0,6.2,.55,15.5,.25,.15,7)
        return
    box(0,.8,0,w,1.6,depth,0);buttresses(w,depth,min(5,w*.2));armour_detail(w,depth,5)
    if id in ['command','outpost','field_command']:
        box(0,4,0,w*.7,6,depth*.62,0);tube(0,8,0,w*.24,5,1,8);strip(0,7,depth*.15,w*.4,1,.3)
        box(0,12,-depth*.08,w*.16,8,depth*.19,1);strip(0,13,depth*.015,w*.09,5,.2);antenna(0,16,0,8)
        for x in [-w*.33,w*.33]:box(x,4,depth*.1,w*.18,5,depth*.35,0);strip(x,5,depth*.27,w*.13,.4,.2)
        if id=='command':
            for x in [-w*.22,w*.22]:
                box(x,8.8,depth*.15,w*.10,7,depth*.27,0);strip(x,9.6,depth*.28,w*.045,5,.2)
                box(x,12.8,depth*.14,w*.12,1,depth*.29,1)
            for z in [-depth*.19,depth*.12]:box(0,9.1,z,w*.27,.4,depth*.13,6)
            for x in [-w*.30,w*.30]:tube(x,6,-depth*.31,w*.14,.6,1,8);tube(x,6.35,-depth*.31,w*.11,.12,3,12)
        else:dish(w*.25,10,-depth*.15,w*.10)
    elif d['category']==3 or id in ['light_depot','logistics','repair_depot','material_storage']:
        # Production bays, accessible front mouth, two side towers and ribbed roof.
        height=5 if id=='helipad' else 8
        if id=='helipad':
            tube(0,2.1,0,min(w,depth)*.45,.6,1,12);tube(0,2.45,0,min(w,depth)*.33,.1,3,12);return
        box(0,height*.55,-depth*.18,w*.75,height,depth*.58,0)
        box(0,height+.4,-depth*.18,w*.64,.7,depth*.52,1)
        for x in [-w*.37,w*.37]:box(x,height*.55,depth*.20,w*.16,height,depth*.35,1);strip(x,height*.7,depth*.37,w*.10,.5,.2)
        box(0,height,depth*.21,w*.65,1.3,depth*.18,1);strip(0,height-.5,depth*.29,w*.49,.6,.15)
        wedge(0,1,depth*.37,w*.55,1,depth*.22,1)
        for x in [-w*.21,0,w*.21]:box(x,height+1,-depth*.18,w*.08,1,depth*.44,6)
        if id in ['airbase','naval_yard']:box(0,1.7,depth*.13,w*.55,.15,depth*.64,2);strip(0,1.82,depth*.10,.4,.1,depth*.60)
        for z in [-depth*.35,-depth*.18,0]:box(0,height+1.12,z,w*.54,.15,depth*.035,1)
        if id=='barracks':
            for x in [-w*.18,w*.18]:box(x,4,-depth*.02,w*.14,3,depth*.025,4);strip(x,5.5,-depth*.01,w*.15,.16,.12)
        if id=='logistics':
            for x in [-w*.20,0,w*.20]:box(x,3,depth*.33,w*.15,2,depth*.18,0)
        if id in ['factory','ordnance']:tube(-w*.24,12,-depth*.3,1,7,1);tube(w*.24,12,-depth*.3,1,7,1)
        if id=='naval_yard':box(-w*.25,15,0,1,26,1,1);box(0,27,0,w*.6,1,1,1)
        if id=='repair_depot':box(w*.27,height+3,0,1,6,1,1);box(0,height+6,0,w*.55,1,1,1)
        antenna(-w*.3,height,depth*.2,4)
    elif 'radar' in id:
        box(0,5,0,w*.47,6,depth*.5,0);tube(0,10,0,1,6,1)
        r=min(w,depth)*.35
        if id=='counter_radar':box(0,12,0,w*.68,depth*.4,.5,5)
        else:dish(0,13,0,r,.9 if id in ['phased_radar','horizon_radar'] else .6)
        antenna(-w*.23,6,0,7)
    elif d['category']==4 or id=='shield':
        h=10 if id=='lab' else 14 if id=='advanced_lab' else 18
        tube(0,h*.5+1,0,min(w,depth)*.27,h,1,12);dome(0,h+1,0,min(w,depth)*.26)
        for k in range(4):a=k*math.tau/4;x=math.cos(a)*w*.32;z=math.sin(a)*depth*.32;tube(x,5,z,w*.09,7,0);strip(x,5,z+w*.085,.4,4,.2)
        if id=='shield':
            for x in [-w*.31,w*.31]:wedge(x,1,0,w*.12,14,depth*.45,1)
        antenna(0,h+1,0,4)
    elif id in ['autocannon','at_turret','aa_turret','sam','advanced_sam','point_defence','artillery_battery','missile_defence','laser']:
        tube(0,4,0,min(w,depth)*.30,4,1,8);tube(0,6.4,0,min(w,depth)*.23,.7,2,8)
        box(0,8,0,w*.40,3,depth*.32,0)
        if id in ['aa_turret','sam','advanced_sam','missile_defence']:missiles(0,9,0,6 if id=='aa_turret' else 4,min(w,depth)/8)
        else:guns(0,9,0,2 if id in ['point_defence','at_turret'] else 1,min(depth*.42,6));strip(0,9,-depth*.12,w*.25,.4,.2)
        if id=='laser':tube(0,9,depth*.25,w*.09,.8,3,8,axis='z')
        for x in [-w*.18,w*.18]:box(x,8.3,0,w*.08,2,depth*.30,1)
    elif id in ['solar','wind']:
        tube(0,9,0,.8,16,1)
        if id=='wind':
            tube(0,18,0,.9,2,0,8,axis='z')
            for k in range(3):
                a=k*math.tau/3;verts=[(0,18,0),(math.cos(a)*w*.40,18+math.sin(a)*w*.40,0),(math.cos(a+.10)*w*.38,18+math.sin(a+.10)*w*.38,.15)];mesh('Turbine blade',verts,[(0,1,2)],1)
        else:
            for x in [-w*.25,w*.25]:
                for z in [-depth*.25,depth*.25]:box(x,6,z,w*.40,.4,depth*.40,5);strip(x,6.3,z,.22,.1,depth*.36)
    elif id=='oil':
        box(0,3,0,w*.42,3,depth*.5,0);box(0,9,0,.8,14,.8,1);box(0,15,0,w*.65,.7,1,1);tube(w*.25,6,0,1.8,8,0)
    elif id=='gas_turbine':
        tube(0,5,0,w*.27,depth*.66,1,12,axis='z');tube(0,5,depth*.36,w*.20,.5,2,12,axis='z');tube(-w*.29,10,-depth*.2,1,15,1)
        for k in range(5):tube(0,5,(k-2)*depth*.1,w*.28,.3,0,12,axis='z')
    elif id in ['mine','silo']:
        tube(0,7,0,w*.25,10,0,8);tube(0,13,0,w*.18,4,1,8);strip(0,10,depth*.21,1,8,.25)
        if id=='mine':wedge(0,1,depth*.32,w*.35,2,depth*.3,1);box(0,5,depth*.35,w*.2,1,depth*.45,6)
        else:tube(0,17,0,w*.13,7,1,8,top=.05)
    else:
        # Storage tanks, extractor columns and reactor arrays retain their individual roles.
        storage='storage' in id
        h=7 if storage else 13 if id in ['fusion','exotic'] else 10
        tube(0,h*.5+1,0,w*.22,h,1,12);tube(0,h*.5+1,0,w*.16,h*.78,3,12)
        count=4 if id in ['battery','fusion','rare','exotic'] else 2
        for k in range(count):
            a=(k+.5)*math.tau/count;x=math.cos(a)*w*.31;z=math.sin(a)*depth*.31
            tube(x,5,z,w*.09,7,0);tube(x,8.7,z,w*.1,.5,1);strip(x,5,z+w*.085,.6,4,.2)
            box(x*.5,2,z*.5,w*.09,1.5,depth*.25,1)
        if id in ['rare_storage','isotope_storage']:box(0,h+2,0,w*.35,3,depth*.35,0)
        if id in ['synthetic','gas']:tube(-w*.3,11,-depth*.3,.8,17,1)
        if id=='exotic':antenna(0,h+1,0,9)

def unit(d):
    id=d['id'];move=d['movement'];r=d['radius'];w=r*1.5;depth=r*2.6
    if move==0:
        heavy=id=='heavy_infantry';s=1.14 if heavy else 1
        # Armoured human silhouette with articulated limb groups, helmet and held equipment.
        for x in [-.22,.22]:
            box(x,.20,.10,.30,.30,.54,2);box(x,.55,0,.27,.48,.31,1);box(x,1,0,.31,.46,.33,0);strip(x,.62,.17,.13,.20,.06)
        box(0,1.28,0,.61,.28,.39,2);wedge(0,1.40,0,.72*s,.58,.48,0);strip(0,1.75,.28,.20,.12,.05)
        tube(0,2.15,0,.28,.40,1,8);box(0,2.17,.24,.44,.19,.09,3)
        for x in [-.45,.45]:
            tube(x,1.82,0,.19,.28,1,6,axis='x')
            rotate_component(box(x,1.51,.08,.22,.39,.24,0),(x,1.8,0),-.20 if x<0 else .35)
            rotate_component(box(x,1.26,.2,.23,.32,.26,1),(x,1.42,.08),-.70)
        box(0,1.7,-.30,.48,.58,.28,0)
        if id=='engineer':box(-.48,.85,.23,.40,.34,.40,1);strip(-.48,1.04,.23,.23,.05,.42);tube(.46,1.25,.52,.11,.75,1,6,axis='z')
        else:box(.25,1.34,.47,.16,.17,.74,0);tube(.25,1.35,.92,.07,.60,1,6,axis='z')
        return
    if move in [1,2]:
        wedge(0,.45,0,w,.8,depth,0);box(0,.42,0,w*.78,.7,depth*.92,2)
        if move==1:
            for x in [-w*.48,w*.48]:
                for z in [-depth*.32,depth*.32]:tube(x,.50,z,w*.18,.35,2,6,axis='x');tube(x*1.05,.5,z,w*.09,.08,3,6,axis='x')
        else:
            for x in [-w*.44,w*.44]:
                box(x,.45,0,w*.21,.75,depth,2)
                for z in [-depth*.30,0,depth*.30]:tube(x*1.32,.43,z,w*.105,w*.045,1,6,axis='x')
                for z in [-depth*.29,depth*.04,depth*.32]:box(x,.84,z,w*.23,.18,depth*.25,0)
        strip(-w*.30,1,depth*.35,w*.15,.12,.12);strip(w*.30,1,depth*.35,w*.15,.12,.12)
        if id in ['tank','heavy_tank','at','artillery_unit']:
            box(0,1.43,-depth*.07,w*.62,.85,depth*.37,1,part=1);guns(0,1.47,0,1,depth*.74 if id=='artillery_unit' else depth*.50,1);strip(0,1.7,-depth*.10,w*.4,.09,.1,1)
            tube(-w*.16,1.96,-depth*.10,w*.10,.12,0,6,1)
            for x in [-w*.24,w*.24]:box(x,1.85,-depth*.09,w*.11,.25,depth*.28,0,1)
            if id=='heavy_tank':guns(w*.20,1.97,-depth*.02,1,depth*.22,1)
        elif id in ['mlrs','mobile_missile','strategic_platform','aa']:
            box(0,1.3,-depth*.15,w*.6,.6,depth*.40,0,1);missiles(0,1.9,-depth*.06,4 if id!='aa' else 6,.55 if id=='aa' else .8,1)
        elif id=='mobile_radar':box(0,1.5,-depth*.12,w*.6,1.1,depth*.4,0);dish(0,2.8,0,w*.4)
        elif id=='repair':box(0,1.4,-depth*.15,w*.55,.9,depth*.5,1);box(0,2,-depth*.15,.2,.3,depth*.70,0)
        elif id=='supply':box(0,1.4,-depth*.12,w*.65,1,depth*.55,1);box(0,1.3,depth*.30,w*.60,.70,depth*.25,0);box(0,1.5,depth*.43,w*.4,.3,.08,4)
        else:box(0,1.25,-depth*.05,w*.64,.85,depth*.58,1);box(0,1.37,depth*.24,w*.4,.3,.10,4)
        if id=='command_vehicle':antenna(w*.12,1.8,-depth*.10,1.4);dish(0,2.1,-depth*.18,w*.2)
        box(0,1.06,-depth*.31,w*.50,.08,depth*.15,6)
        return
    if move==3:
        heli='heli' in id or id=='gunship';w*=1.5;depth*=1.5
        wedge(0,.15,0,w*.34,.70,depth,0);box(0,.80,depth*.22,w*.25,.48,depth*.22,4)
        if id=='gunship':
            box(0,.55,-depth*.10,w*.46,.55,depth*.45,0)
            for x in [-w*.43,w*.43]:
                tube(x,.55,-depth*.05,w*.22,.24,1,10);tube(x,.70,-depth*.05,w*.16,.05,2,8)
                box(x,.76,-depth*.05,w*.27,.025,.06,1,4)
        elif heli:
            box(0,.4,-depth*.39,w*.13,.40,depth*.47,1);tube(0,1.45,0,.13,.8,1,6)
            box(0,1.90,0,w*1.45,.05,.14,1,4);box(0,1.90,0,.14,.05,w*1.45,1,4)
            for x in [-w*.28,w*.28]:tube(x,.0,0,.065,depth*.6,1,4,axis='z')
            if id=='transport_heli':box(0,.40,-depth*.06,w*.40,.65,depth*.5,1)
        else:
            verts=[(-w*.14,.36,depth*.20),(-w*.70,.18,-depth*.21),(-w*.28,.26,-depth*.36),(-w*.10,.40,-depth*.20)]
            mesh('Port swept wing',verts,[(0,1,2,3)],1);mesh('Starboard swept wing',[(-x,y,z) for x,y,z in verts],[(3,2,1,0)],1)
            for x in [-w*.15,w*.15]:tube(x,.38,-depth*.35,w*.09,depth*.35,0,8,axis='z');strip(x,.45,-depth*.47,w*.12,.1,.15)
            for x in [-w*.15,w*.15]:wedge(x,.5,-depth*.37,.10,.7,depth*.24,1)
            if id=='bomber':
                for x in [-w*.40,w*.40]:tube(x,.26,-depth*.25,w*.09,depth*.35,0,6,axis='z')
            if id=='recon_air':dish(0,.8,-depth*.10,w*.13)
        if id in ['attack_heli','gunship','bomber']:missiles(0,.0,depth*.20,2,.40)
        return
    # Ship hull has a pointed bow, open bottom and progressively specialised decks.
    w*=1.4;depth*=2.2
    if id=='submarine':tube(0,.15,0,w*.3,depth*.8,0,10,axis='z');tube(0,.15,depth*.44,w*.3,depth*.13,1,10,axis='z',top=.05);box(0,.8,0,w*.25,1.1,depth*.15,1);antenna(0,1.4,0,1);return
    verts=[(-w*.44,0,-depth*.48),(w*.44,0,-depth*.48),(w*.48,0,depth*.18),(0,0,depth*.52),(-w*.48,0,depth*.18)]
    verts += [(x*.85,y+.7,z) for x,y,z in verts]
    mesh('Armoured displacement hull',verts,[tuple(range(5,10))]+[(k,(k+1)%5,(k+1)%5+5,k+5) for k in range(5)],0)
    if id=='carrier_unit':box(-w*.29,1.5,-depth*.1,w*.25,1.4,depth*.20,1);strip(0,.76,0,.1,.03,depth*.75)
    elif id=='transport_ship':box(0,1.1,0,w*.6,.6,depth*.5,6);box(0,2,-depth*.28,w*.48,2,depth*.16,1)
    else:
        box(0,1.5,-depth*.15,w*.5,1.5,depth*.30,1);box(0,2.4,-depth*.2,w*.25,.5,depth*.15,4);antenna(0,2.7,-depth*.2,2)
        count=3 if id=='battleship' else 2 if id in ['cruiser','destroyer'] else 1
        for k in range(count):z=depth*(.26-k*.22);box(0,1.25,z,w*.38,.6,depth*.09,1);guns(0,1.3,z,2,depth*.12)
    for x in [-w*.39,w*.39]:strip(x,.8,0,.10,.05,depth*.65)
    for x in [-w*.30,w*.30]:
        for z in [-depth*.27,depth*.05]:box(x,.85,z,w*.13,.20,depth*.12,6)

def finish(kind,d,index):
    global objects
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join();obj=bpy.context.object;obj.name=kind+'-'+d['id']
    obj.data.calc_loop_triangles()
    if kind=='unit' and len(obj.data.loop_triangles)>512:
        modifier=obj.modifiers.new('512 triangle game budget','DECIMATE');modifier.ratio=510/len(obj.data.loop_triangles)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    # Remove occluded downward surfaces while retaining visible wing undersides and gun caps.
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bm=bmesh.from_edit_mesh(obj.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bmesh.update_edit_mesh(obj.data)
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.012,area_weight=.5,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    modifier=obj.modifiers.new('Export triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=modifier.name)
    data=obj.data;data.calc_loop_triangles();data.calc_tangents()
    part=data.attributes.get('SFPart');uv=data.uv_layers.active
    uv2=data.uv_layers.new(name='Motion')
    for loop in data.loops:uv2.data[loop.index].uv=(part.data[loop.vertex_index].value if part else 0,0)
    data.uv_layers.active=uv
    polygons=[dict(uv=[list(uv.data[i].uv) for i in p.loop_indices],material=p.material_index) for p in data.polygons]
    vertices=[]
    for tri in data.loop_triangles:
        for li in tri.loops:
            loop=data.loops[li];p=data.vertices[loop.vertex_index].co;n=loop.normal;t=loop.tangent
            vertices.append(dict(p=[p.x,p.z,-p.y],n=[n.x,n.z,-n.y],t=[t.x,t.z,-t.y,loop.bitangent_sign],uv=list(uv.data[li].uv),part=part.data[loop.vertex_index].value if part else 0))
    (SOURCE/(obj.name+'.json')).write_text(json.dumps(dict(kind=kind,id=d['id'],vertices=vertices,polygons=polygons)))
    if not (obj.name=='building-command' and (ROOT/'ArtSource/HQ/building-command.mesh.gz').exists()):
        bpy.ops.export_scene.fbx(filepath=str(OUT/(obj.name+'.fbx')),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_custom_props=True)
    # Simplify a separate copy; reuse the exact UV texture set and retain the editable near mesh.
    if kind in ['unit','building']:
        low=obj.copy();low.data=obj.data.copy();collection.objects.link(low)
        obj.select_set(False);low.select_set(True);bpy.context.view_layer.objects.active=low
        budget=128 if kind=='unit' else 256
        modifier=low.modifiers.new('Medium distance budget','DECIMATE');modifier.ratio=min(1,(budget*.85)/len(vertices)*3)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        modifier=low.modifiers.new('Medium triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=modifier.name)
        bm=bmesh.new();bm.from_mesh(low.data)
        remaining=set(bm.verts);components=[]
        while remaining:
            seed=remaining.pop();group={seed};stack=[seed]
            while stack:
                for e in stack.pop().link_edges:
                    for v in e.verts:
                        if v in remaining:remaining.remove(v);group.add(v);stack.append(v)
            faces={f for v in group for f in v.link_faces}
            coords=[v.co for v in group];extent=[max(v[i] for v in coords)-min(v[i] for v in coords) for i in range(3)]
            score=extent[0]*extent[1]+extent[1]*extent[2]+extent[0]*extent[2]
            components.append((score,faces))
        total=len(bm.faces)
        for _,faces in sorted(components,key=lambda item:item[0]):
            if total<=budget:break
            total-=len(faces);bmesh.ops.delete(bm,geom=list(faces),context='FACES')
        bm.to_mesh(low.data);bm.free();low.data.update()
        low.data.calc_loop_triangles();low.data.calc_tangents(uvmap=low.data.uv_layers[0].name)
        partlow=low.data.attributes.get('SFPart');uvlow=low.data.uv_layers[0];lowvertices=[]
        for tri in low.data.loop_triangles:
            for li in tri.loops:
                loop=low.data.loops[li];p=low.data.vertices[loop.vertex_index].co;n=loop.normal;t=loop.tangent
                lowvertices.append(dict(p=[p.x,p.z,-p.y],n=[n.x,n.z,-n.y],t=[t.x,t.z,-t.y,loop.bitangent_sign],uv=list(uvlow.data[li].uv),part=partlow.data[loop.vertex_index].value if partlow else 0))
        (SOURCE/(obj.name+'-lod1.json')).write_text(json.dumps(dict(kind=kind,id=d['id'],vertices=lowvertices)))
        low.name=obj.name+'-LOD1';low.hide_render=True;low.hide_viewport=True;low.select_set(False);obj.select_set(True);bpy.context.view_layer.objects.active=obj
    # Editable source is arranged as a library, never exported at this display offset.
    obj.location=((index%10)*100,(index//10)*100,0)
    if kind in ['unit','building']:low.location=obj.location.copy()
    return dict(kind=kind,id=d['id'],triangles=len(vertices)//3,vertices=len(vertices),width=float(obj.dimensions.x),depth=float(obj.dimensions.y),height=float(obj.dimensions.z))

reports=[]
for kind,definitions in [('building',catalog['buildings']),('unit',catalog['units'])]:
    for d in definitions:
        collection=bpy.data.collections.new(kind+'-'+d['id']);scene.collection.children.link(collection);objects=[]
        simple_panels=kind=='unit' and d['movement'] in [1,2]
        (building if kind=='building' else unit)(d)
        reports.append(finish(kind,d,len(reports)));print('ART_MODEL',reports[-1],flush=True)
(SOURCE/'geometry-report.json').write_text(json.dumps(reports,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'SyntheticFront.blend'))
