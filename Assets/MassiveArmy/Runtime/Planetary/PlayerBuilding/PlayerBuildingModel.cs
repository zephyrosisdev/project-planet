using System;

using System.Collections.Generic;

using UnityEngine;

namespace MassiveArmy.Planetary {

    public enum PlayerModuleKind {FoundationSquare,FoundationTriangle,Wall,DoorwaySingle,DoorwayDouble,DoorFrame,GarageFrame,DoorSingle,DoorDouble,GarageDoor,CeilingSquare,CeilingTriangle,StairsStraight,Stairs90,Stairs180,HabitatRoom,MedicalRoom,ResearchRoom,CommandRoom,LifeSupportRoom,AirlockRoom,CorridorRoom,JunctionRoom,GreenhouseRoom,WorkshopRoom,WarehouseRoom}

    internal enum BuildSocketKind {FoundationEdge,FloorEdge,WallBase,RoofEdge,FloorCenter,StairCenter,SingleDoor,DoubleDoor,GarageDoor,HabitatPort,MachineMount}

    [Serializable]public sealed class PlayerModuleRecord {public long id,parent;public int parentSocket=-1;public PlayerModuleKind kind;public Vector3 position;public float yaw;public bool open;public string constructionItem;public int constructionCount;public double constructionActivity;public bool resourceConstruction;public List<MassiveArmy.Industrial.ItemStack> constructionPaid=new List<MassiveArmy.Industrial.ItemStack>();}

    [Serializable]public sealed class PlayerBuildSite {public long id,owner;public Double3 anchor;public Quaternion rotation=Quaternion.identity;public List<PlayerModuleRecord> modules=new List<PlayerModuleRecord>();}

    internal readonly struct BuildSocket {internal readonly Vector3 point;internal readonly float yaw;internal readonly BuildSocketKind kind;internal readonly float width,depth;internal BuildSocket(BuildSocketKind kind,Vector3 point,float yaw=0,float width=3,float depth=3){this.kind=kind;this.point=point;this.yaw=yaw;this.width=width;this.depth=depth;}}

    internal sealed class BuildSolid {

        internal readonly Vector2[] polygon;internal readonly float bottom,top;

        internal BuildSolid(Vector2[] polygon,float bottom,float top){this.polygon=polygon;this.bottom=bottom;this.top=top;}

        internal static BuildSolid Box(float x0,float x1,float z0,float z1,float y0,float y1)=>new BuildSolid(new[]{new Vector2(x0,z0),new Vector2(x1,z0),new Vector2(x1,z1),new Vector2(x0,z1)},y0,y1);

        internal BuildSolid Turn(float yaw,Vector2 hinge){var result=new Vector2[polygon.Length];var q=Quaternion.Euler(0,yaw,0);for(int i=0;i<result.Length;i++){var p=polygon[i]-hinge;var v=q*new Vector3(p.x,0,p.y);result[i]=hinge+new Vector2(v.x,v.z);}return new BuildSolid(result,bottom,top);}

    }

    internal sealed class PlayerModuleProfile {

        internal string name;internal PlayerModuleKind kind;internal BuildSolid[] solids,openSolids,clearance;internal BuildSocket[] inputs,outputs;internal Bounds bounds,interactionBounds;internal Mesh mesh,openMesh;

        internal bool Room=>kind>=PlayerModuleKind.HabitatRoom;
        internal bool Foundation=>kind==PlayerModuleKind.FoundationSquare||kind==PlayerModuleKind.FoundationTriangle;

        internal bool Door=>kind==PlayerModuleKind.DoorSingle||kind==PlayerModuleKind.DoorDouble||kind==PlayerModuleKind.GarageDoor;

        internal bool Wall=>kind>=PlayerModuleKind.Wall&&kind<=PlayerModuleKind.GarageFrame;

        internal BuildSolid[] Shape(bool open)=>open&&openSolids!=null?openSolids:solids;

        internal Mesh Preview(bool open=false)=>open&&openMesh!=null?openMesh:mesh;

    }

    internal static class PlayerBuildingProfiles {

        internal const float Grid=3,SiteRadius=96,Reach=12;

        internal static readonly Vector2[] Square={new Vector2(-1.5f,-1.5f),new Vector2(1.5f,-1.5f),new Vector2(1.5f,1.5f),new Vector2(-1.5f,1.5f)};

        internal static readonly Vector2[] Triangle={new Vector2(-1.5f,-.8660254f),new Vector2(1.5f,-.8660254f),new Vector2(0,1.7320508f)};

        internal static PlayerModuleProfile[] Create(bool collisionOnly=false){var result=new PlayerModuleProfile[Names.Length];for(int n=0;n<result.Length;n++){

            var kind=(PlayerModuleKind)n;var solids=new List<BuildSolid>();var inputs=new List<BuildSocket>();var outputs=new List<BuildSocket>();var clear=new List<BuildSolid>();var p=new PlayerModuleProfile{kind=kind,name=Names[n]};result[n]=p;

            bool floor=kind==PlayerModuleKind.CeilingSquare||kind==PlayerModuleKind.CeilingTriangle;bool tri=kind==PlayerModuleKind.FoundationTriangle||kind==PlayerModuleKind.CeilingTriangle;

            if(p.Room){var asset=collisionOnly?ColonyPrototypeLibrary.StoredProfile("room-"+kind):ColonyPrototypeLibrary.Find("room-"+kind);if(asset==null)throw new InvalidOperationException("Import the Blender colony prototype catalogue before using colony rooms");foreach(var shape in asset.solids)solids.Add(shape.Shape());foreach(var socket in asset.sockets){if(!Enum.TryParse(socket.kind,out BuildSocketKind socketKind))continue;var value=new BuildSocket(socketKind,socket.position,socket.yaw,socket.width,socket.depth);if(socketKind==BuildSocketKind.FloorCenter)inputs.Add(value);else if(socketKind==BuildSocketKind.HabitatPort){inputs.Add(value);outputs.Add(value);}else if(socketKind==BuildSocketKind.MachineMount)outputs.Add(value);}}
            else if(p.Foundation||floor){float height=p.Foundation?3:.1f;var poly=tri?Triangle:Square;solids.Add(new BuildSolid(poly,0,height));

                for(int i=0;i<poly.Length;i++){var a=poly[i];var b=poly[(i+1)%poly.Length];var mid=(a+b)*.5f;var outward=new Vector2(b.y-a.y,a.x-b.x);float yaw=Mathf.Atan2(outward.x,outward.y)*Mathf.Rad2Deg;var at=new Vector3(mid.x,0,mid.y);

                    var edgeAt=at+Vector3.up*(p.Foundation?height:0);inputs.Add(new BuildSocket(p.Foundation?BuildSocketKind.FoundationEdge:BuildSocketKind.FloorEdge,edgeAt,yaw));if(floor)inputs.Add(new BuildSocket(BuildSocketKind.RoofEdge,at,yaw));outputs.Add(new BuildSocket(p.Foundation?BuildSocketKind.FoundationEdge:BuildSocketKind.FloorEdge,edgeAt,yaw));outputs.Add(new BuildSocket(BuildSocketKind.WallBase,at+Vector3.up*height,yaw));}

                outputs.Add(new BuildSocket(BuildSocketKind.FloorCenter,Vector3.up*height));if(!tri)outputs.Add(new BuildSocket(BuildSocketKind.StairCenter,Vector3.up*height));if(floor)inputs.Add(new BuildSocket(BuildSocketKind.FloorCenter,Vector3.zero));

            }else if(p.Wall){inputs.Add(new BuildSocket(BuildSocketKind.WallBase,Vector3.zero));outputs.Add(new BuildSocket(BuildSocketKind.RoofEdge,Vector3.up*3));outputs.Add(new BuildSocket(BuildSocketKind.WallBase,Vector3.up*3));

                if(kind==PlayerModuleKind.Wall)solids.Add(BuildSolid.Box(-1.5f,1.5f,-.05f,.05f,0,3));

                else {float half=kind==PlayerModuleKind.DoorwayDouble?1.04f:kind==PlayerModuleKind.GarageFrame?1.36f:.52f;float top=kind==PlayerModuleKind.GarageFrame?2.75f:2.2f;

                    if(kind==PlayerModuleKind.DoorFrame){solids.Add(BuildSolid.Box(-1.5f,-1.38f,-.05f,.05f,0,3));solids.Add(BuildSolid.Box(1.38f,1.5f,-.05f,.05f,0,3));solids.Add(BuildSolid.Box(-1.38f,1.38f,-.05f,.05f,2.88f,3));solids.Add(BuildSolid.Box(-.64f,-half,-.05f,.05f,0,2.32f));solids.Add(BuildSolid.Box(half,.64f,-.05f,.05f,0,2.32f));solids.Add(BuildSolid.Box(-half,half,-.05f,.05f,2.2f,2.32f));}

                    else {solids.Add(BuildSolid.Box(-1.5f,-half,-.05f,.05f,0,3));solids.Add(BuildSolid.Box(half,1.5f,-.05f,.05f,0,3));solids.Add(BuildSolid.Box(-half,half,-.05f,.05f,top,3));}

                    outputs.Add(new BuildSocket(kind==PlayerModuleKind.DoorwayDouble?BuildSocketKind.DoubleDoor:kind==PlayerModuleKind.GarageFrame?BuildSocketKind.GarageDoor:BuildSocketKind.SingleDoor,Vector3.zero));}

            }else if(p.Door){inputs.Add(new BuildSocket(kind==PlayerModuleKind.DoorSingle?BuildSocketKind.SingleDoor:kind==PlayerModuleKind.DoorDouble?BuildSocketKind.DoubleDoor:BuildSocketKind.GarageDoor,Vector3.zero));

                if(kind==PlayerModuleKind.DoorSingle){var leaf=BuildSolid.Box(-.48f,.48f,-.04f,.04f,.02f,2.16f);solids.Add(leaf);p.openSolids=new[]{leaf.Turn(90,new Vector2(-.48f,0))};}

                else if(kind==PlayerModuleKind.DoorDouble){var left=BuildSolid.Box(-1,-.02f,-.04f,.04f,.02f,2.16f);var right=BuildSolid.Box(.02f,1,-.04f,.04f,.02f,2.16f);solids.Add(left);solids.Add(right);p.openSolids=new[]{left.Turn(90,new Vector2(-1,0)),right.Turn(-90,new Vector2(1,0))};}

                else {solids.Add(BuildSolid.Box(-1.32f,1.32f,-.04f,.04f,.02f,2.7f));p.openSolids=new[]{BuildSolid.Box(-1.32f,1.32f,-.08f,.08f,2.56f,2.72f)};}

            }else {inputs.Add(new BuildSocket(BuildSocketKind.StairCenter,Vector3.zero));

                if(kind==PlayerModuleKind.StairsStraight){for(int i=0;i<12;i++){float z=-1.5f+i*.25f;solids.Add(BuildSolid.Box(-1.45f,1.45f,z,z+.25f,0,(i+1)*.25f));clear.Add(BuildSolid.Box(-1.4f,1.4f,z,z+.25f,(i+1)*.25f,(i+1)*.25f+1.85f));}outputs.Add(new BuildSocket(BuildSocketKind.FloorCenter,new Vector3(0,3,3)));}

                else {for(int i=0;i<6;i++){float z=-1.5f+i*.3f;solids.Add(BuildSolid.Box(-1.5f,-.3f,z,z+.3f,0,(i+1)*.25f));clear.Add(BuildSolid.Box(-1.45f,-.35f,z,z+.3f,(i+1)*.25f,(i+1)*.25f+1.85f));}

                    solids.Add(BuildSolid.Box(-1.5f,kind==PlayerModuleKind.Stairs180?1.5f:-.3f,.3f,1.5f,0,1.5f));clear.Add(BuildSolid.Box(-1.45f,kind==PlayerModuleKind.Stairs180?1.45f:-.35f,.35f,1.45f,1.5f,3.35f));

                    for(int i=0;i<6;i++){BuildSolid step=kind==PlayerModuleKind.Stairs90?BuildSolid.Box(-.3f+i*.3f,i*.3f,.3f,1.5f,0,1.5f+(i+1)*.25f):BuildSolid.Box(.3f,1.5f,-i*.3f,.3f-i*.3f,0,1.5f+(i+1)*.25f);solids.Add(step);clear.Add(new BuildSolid(step.polygon,step.top,step.top+1.85f));}

                    outputs.Add(new BuildSocket(BuildSocketKind.FloorCenter,kind==PlayerModuleKind.Stairs90?new Vector3(3,3,0):new Vector3(0,3,-3)));}

            }

            p.solids=solids.ToArray();p.inputs=inputs.ToArray();p.outputs=outputs.ToArray();p.clearance=clear.ToArray();var bounds=new Bounds();bool started=false;foreach(var shape in p.solids)foreach(var v in shape.polygon){var a=new Vector3(v.x,shape.bottom,v.y);var b=new Vector3(v.x,shape.top,v.y);if(!started){bounds=new Bounds(a,Vector3.zero);started=true;}bounds.Encapsulate(a);bounds.Encapsulate(b);}p.bounds=bounds;foreach(var shape in p.clearance)foreach(var v in shape.polygon){bounds.Encapsulate(new Vector3(v.x,shape.bottom,v.y));bounds.Encapsulate(new Vector3(v.x,shape.top,v.y));}if(p.openSolids!=null)foreach(var shape in p.openSolids)foreach(var v in shape.polygon){bounds.Encapsulate(new Vector3(v.x,shape.bottom,v.y));bounds.Encapsulate(new Vector3(v.x,shape.top,v.y));}p.interactionBounds=bounds;

        }return result;}

        internal static readonly string[] Names={"Square\nfoundation","Triangle\nfoundation","Wall","Single\ndoorway","Double\ndoorway","Door\nframe","Garage\nframe","Single\ndoor","Double\ndoor","Garage\ndoor","Square\nceiling","Triangle\nceiling","Straight\nstairs","90°\nstairs","180°\nstairs","Habitat","Medical","Research\nlab","Command","Life\nsupport","Airlock","Corridor","Junction","Greenhouse","Workshop","Warehouse"};

        internal static int Radial(Vector2 direction,int count){if(count<=1)return 0;float angle=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;return Mathf.FloorToInt((angle+360+180f/count)/(360f/count))%count;}

        internal static bool Overlap(BuildSolid a,Vector3 ap,Quaternion ar,BuildSolid b,Vector3 bp,Quaternion br,float tolerance=.006f){

            Span<Vector3> av=stackalloc Vector3[16];Span<Vector3> bv=stackalloc Vector3[16];int ac=a.polygon.Length,bc=b.polygon.Length;

            for(int i=0;i<ac;i++){var p=a.polygon[i];av[i]=ap+ar*new Vector3(p.x,a.bottom,p.y);av[i+ac]=ap+ar*new Vector3(p.x,a.top,p.y);}for(int i=0;i<bc;i++){var p=b.polygon[i];bv[i]=bp+br*new Vector3(p.x,b.bottom,p.y);bv[i+bc]=bp+br*new Vector3(p.x,b.top,p.y);}

            if(Separated(av.Slice(0,ac*2),bv.Slice(0,bc*2),ar*Vector3.up,tolerance)||Separated(av.Slice(0,ac*2),bv.Slice(0,bc*2),br*Vector3.up,tolerance))return false;

            for(int i=0;i<ac;i++){var edge=av[(i+1)%ac]-av[i];if(Separated(av.Slice(0,ac*2),bv.Slice(0,bc*2),Vector3.Cross(edge,ar*Vector3.up),tolerance))return false;for(int j=0;j<bc;j++)if(Separated(av.Slice(0,ac*2),bv.Slice(0,bc*2),Vector3.Cross(edge,bv[(j+1)%bc]-bv[j]),tolerance))return false;}

            for(int j=0;j<bc;j++)if(Separated(av.Slice(0,ac*2),bv.Slice(0,bc*2),Vector3.Cross(bv[(j+1)%bc]-bv[j],br*Vector3.up),tolerance))return false;

            for(int i=0;i<ac;i++)if(Separated(av.Slice(0,ac*2),bv.Slice(0,bc*2),Vector3.Cross(av[(i+1)%ac]-av[i],br*Vector3.up),tolerance))return false;

            for(int j=0;j<bc;j++)if(Separated(av.Slice(0,ac*2),bv.Slice(0,bc*2),Vector3.Cross(ar*Vector3.up,bv[(j+1)%bc]-bv[j]),tolerance))return false;

            return !Separated(av.Slice(0,ac*2),bv.Slice(0,bc*2),Vector3.Cross(ar*Vector3.up,br*Vector3.up),tolerance);

        }

        static bool Separated(ReadOnlySpan<Vector3> a,ReadOnlySpan<Vector3> b,Vector3 axis,float tolerance){float length=axis.magnitude;if(length<.000001f)return false;axis/=length;float amin=float.MaxValue,amax=float.MinValue,bmin=float.MaxValue,bmax=float.MinValue;foreach(var v in a){float d=Vector3.Dot(v,axis);amin=Mathf.Min(amin,d);amax=Mathf.Max(amax,d);}foreach(var v in b){float d=Vector3.Dot(v,axis);bmin=Mathf.Min(bmin,d);bmax=Mathf.Max(bmax,d);}return Mathf.Min(amax,bmax)-Mathf.Max(amin,bmin)<=tolerance;}

    }

}

