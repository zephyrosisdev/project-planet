using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    // Untouched deposits have no records: a cell and the planet seed reproduce them on CPU/GPU.
    // Only surveyed/claimed reserves are materialized, so depletion survives saving and revisiting.
    public sealed class PlanetProceduralResources : IDisposable
    {
        readonly PlanetRtsBase world;readonly PlanetDefinition definition;readonly PlanetSurface surface;
        readonly List<ResourceNode> types=new List<ResourceNode>();readonly Dictionary<int,ResourceNode> recorded=new Dictionary<int,ResourceNode>();
        readonly uint seed;readonly float homeCos;int synchronized;GraphicsBuffer overrides;Material material;
        public readonly int Resolution;
        internal int TypeCount=>types.Count;
        internal static uint Hash(uint x){unchecked{x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;return x^(x>>16);}}
        static float Fraction(uint x)=>(x&0xffffffu)/16777216f;
        public PlanetProceduralResources(PlanetRtsBase world,PlanetDefinition definition){
            this.world=world;this.definition=definition;surface=new PlanetSurface(definition);seed=Hash(unchecked((uint)definition.terrainSeed)^Hash((uint)definition.bodyId));
            Resolution=Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(definition.radius/400)),4,4096);homeCos=Mathf.Cos(Mathf.Min(.2f,1600/definition.radius));
            foreach(var d in world.Catalog.buildings){if(d.terrain!=TerrainRule.Node||d.nodes.Length==0)continue;string element=d.extractedResource??"";
                if(types.Exists(n=>n.kind==d.nodes[0]&&(n.elementId??"")==element))continue;
                if(element.Length>0&&(world.Civilisation==null||world.Civilisation.Planet(definition.bodyId).composition.Get(element)<=0))continue;
                if(types.Count==32)throw new InvalidOperationException("Expand the shared procedural resource type table");
                var kind=d.nodes[0];types.Add(new ResourceNode{kind=kind,elementId=element,resource=kind==NodeKind.Oil?ResourceKind.Fuel:kind==NodeKind.RareEarth?ResourceKind.RareElements:kind==NodeKind.Uranium?ResourceKind.Isotopes:ResourceKind.Materials});
            }Sync();
        }
        internal int Key(Vector3 direction)=>CubeSphere.Cell(0,direction,Resolution).Index(Resolution)+1;
        internal Vector3 Direction(int key){int index=key-1,face=index/(Resolution*Resolution),x=index%Resolution,y=index/Resolution%Resolution;uint h=Hash((uint)key^seed);
            return CubeSphere.Direction((CubeFace)face,new Vector2((x+.2f+.6f*Fraction(Hash(h+1)))*2/Resolution-1,(y+.2f+.6f*Fraction(Hash(h+2)))*2/Resolution-1));}
        bool Description(int key,out ResourceNode type,out Vector3 up){type=null;up=default;if(key<=0||key>6*Resolution*Resolution||types.Count==0)return false;uint h=Hash((uint)key^seed);if(h%100>=65)return false;
            up=Direction(key);if(Vector3.Dot(up,world.Homes[0])>homeCos||Vector3.Dot(up,world.Homes[1])>homeCos)return false;type=types[(int)(Hash(h+3)%(uint)types.Count)];return true;}
        internal ResourceNode Get(int key){Sync();if(recorded.TryGetValue(key,out var existing))return existing;if(!Description(key,out var type,out var up)||surface.SampleHeight(up)<definition.oceanHeight+.2f)return null;
            string abundance=!string.IsNullOrEmpty(type.elementId)?type.elementId:type.resource==ResourceKind.Fuel?"hydrogen":type.resource==ResourceKind.RareElements?"rare_earth":type.resource==ResourceKind.Isotopes?"uranium":"iron";
            float quality=(float)(world.Civilisation?.Planet(definition.bodyId).composition.LocalAbundance(abundance,up,definition.terrainSeed)??.5);float capacity=(world.Civilisation?.Settings.depositCapacity??20000)*Mathf.Max(.1f,quality);
            return new ResourceNode{id=-1,proceduralKey=key,kind=type.kind,elementId=type.elementId,resource=type.resource,position=CubeSphere.Encode(up),capacity=capacity,remaining=capacity,quality=quality,rateMultiplier=Mathf.Max(world.Civilisation?.Settings.geologicalMinimumEfficiency??.1f,quality*2)};
        }
        internal ResourceNode Materialize(int key){var node=Get(key);if(node==null)return null;if(node.id>=0)return node;node.id=world.Nodes.Count;world.Nodes.Add(node);world.Civilisation?.Planet(definition.bodyId).frontierDeposits.Add(node);Sync();return node;}
        internal ResourceNode At(Vector3 direction,BuildingDefinition building=null){int key=Key(direction);if(building!=null&&(!Description(key,out var type,out _)||Array.IndexOf(building.nodes,type.kind)<0||(type.elementId??"")!=(building.extractedResource??"")))return null;
            var node=Get(key);return node!=null&&node.id<0&&CubeSphere.ArcDistance(direction,CubeSphere.Decode(node.position),definition.radius)<12?node:null;}
        internal bool Survey(BuildingDefinition building,int team,FrontierSurvey survey){
            var centre=CubeSphere.Cell(0,world.Homes[team],Resolution);
            for(int work=0;work<64;work++){
                int index=survey.cursor++,ring=Mathf.CeilToInt((Mathf.Sqrt(index+1)-1)*.5f),side=ring*2,offset=index-(2*ring-1)*(2*ring-1),x=0,y=0;
                if(ring>0){if(offset<side){x=ring;y=-ring+offset;}else if(offset<side*2){x=ring-(offset-side);y=ring;}else if(offset<side*3){x=-ring;y=ring-(offset-side*2);}else{x=-ring+(offset-side*3);y=-ring;}}
                var direction=CubeSphere.Direction(centre.face,new Vector2((centre.x+x+.5f)*2/Resolution-1,(centre.y+y+.5f)*2/Resolution-1));int key=Key(direction);
                if(!Description(key,out var type,out var up)||Array.IndexOf(building.nodes,type.kind)<0||(type.elementId??"")!=(building.extractedResource??"")||Vector3.Dot(up,world.Homes[team])<Vector3.Dot(up,world.Homes[1-team]))continue;
                var node=Get(key);if(node==null||node.id>=0||node.remaining<=0||node.claimedBy>=0)continue;
                if(world.Validate(building,up,0,false,team).valid){Materialize(key);world.NotifyChanged();return true;}
            }return false;
        }
        internal ResourceNode Pick(Vector2 screen){
            var view=world.World.View;var ray=view.ScreenPointToRay(screen);var origin=world.World.ObserverPosition.RelativeTo(definition.position).Float;float radius=definition.radius+surface.SampleHeight(world.World.Rig.FocusDirection);Vector3 point=default;
            for(int i=0;i<5;i++){float b=Vector3.Dot(origin,ray.direction),c=origin.sqrMagnitude-radius*radius,disc=b*b-c;if(disc<0)return null;float t=-b-Mathf.Sqrt(disc);if(t<0)return null;point=origin+ray.direction*t;radius=definition.radius+surface.SampleHeight(point.normalized);}
            var centre=CubeSphere.Cell(0,point.normalized,Resolution);ResourceNode nearest=null;float closest=float.MaxValue;
            for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){var cell=centre;if(x!=0)cell=CubeSphere.Neighbour(cell,x,0,Resolution);if(y!=0)cell=CubeSphere.Neighbour(cell,0,y,Resolution);var node=Get(cell.Index(Resolution)+1);if(node==null)continue;
                var up=CubeSphere.Decode(node.position);float denominator=Vector3.Dot(ray.direction,up);if(denominator>=-.0001f)continue;var relative=up*(definition.radius+surface.SampleHeight(up))-origin;float distance=Vector3.Dot(relative,up)/denominator;if(distance<0||distance>=closest||distance>view.farClipPlane)continue;
                var delta=ray.direction*distance-relative;var right=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var forward=Vector3.Cross(up,right);var dimensions=BuildingGrid.ResourceDimensions(node.kind,world.Catalog);
                if(Mathf.Abs(Vector3.Dot(delta,right))>dimensions.x*4||Mathf.Abs(Vector3.Dot(delta,forward))>dimensions.y*4)continue;nearest=node;closest=distance;
            }return nearest;
        }
        void Sync(){if(synchronized==world.Nodes.Count)return;for(int i=synchronized;i<world.Nodes.Count;i++)if(world.Nodes[i].proceduralKey>0)recorded[world.Nodes[i].proceduralKey]=world.Nodes[i];synchronized=world.Nodes.Count;if(material!=null)UploadOverrides();}
        internal void Bind(Material target){material=target;target.SetInt("_NaturalResolution",Resolution);target.SetInt("_NaturalSeed",unchecked((int)seed));target.SetInt("_NaturalTypeCount",types.Count);target.SetFloat("_NaturalHomeCos",homeCos);target.SetVector("_NaturalHomeA",world.Homes[0]);target.SetVector("_NaturalHomeB",world.Homes[1]);var data=new Vector4[32];
            float footprint=0;for(int i=0;i<types.Count;i++){var size=BuildingGrid.ResourceDimensions(types[i].kind,world.Catalog);footprint=Mathf.Max(footprint,((Vector2)size).magnitude*4+16);data[i]=new Vector4((int)PlanetResourceDecals.AppearanceKind(types[i]),size.x*8,size.y*8,0);}
            // No natural decal can reach this inner part of the guaranteed starter region.
            target.SetFloat("_NaturalHomeInnerCos",Mathf.Cos(Mathf.Max(0,Mathf.Min(.2f,1600/definition.radius)-footprint/definition.radius)));target.SetVectorArray("_NaturalTypes",data);UploadOverrides();}
        void UploadOverrides(){int capacity=Mathf.NextPowerOfTwo(Math.Max(16,recorded.Count*2));uint[] keys;
            while(true){keys=new uint[capacity];bool fits=true;foreach(int key in recorded.Keys){int at=(int)(Hash((uint)key)&(uint)(capacity-1)),probe=0;while(keys[at]!=0&&probe<16){at=(at+1)&(capacity-1);probe++;}if(probe==16){fits=false;break;}keys[at]=(uint)key;}if(fits)break;capacity*=2;}
            overrides?.Dispose();overrides=new GraphicsBuffer(GraphicsBuffer.Target.Structured,capacity,4);overrides.SetData(keys);material.SetBuffer("_NaturalOverrides",overrides);material.SetInt("_NaturalOverrideMask",capacity-1);}
        internal void Update()=>Sync();
        public void Dispose()=>overrides?.Dispose();
    }
}
