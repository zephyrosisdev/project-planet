using UnityEngine;
namespace MassiveArmy {
    // Conservative screen-rectangle portal traversal. Fixed work and no GPU readback.
    public sealed class ModularPortalVisibility {
        const int MaximumRooms=32,MaximumVisits=128;
        readonly Rect[] coverage=new Rect[MaximumRooms],rectangles=new Rect[MaximumVisits];readonly int[] queue=new int[MaximumVisits];
        public readonly bool[] visible=new bool[MaximumRooms];int head,tail;Matrix4x4 projection;
        public int Visits {get;private set;}
        public void Evaluate(ModularBuildingLayout layout,Camera camera,Matrix4x4 localToWorld,uint openDoors){
            for(int i=0;i<MaximumRooms;i++){visible[i]=false;coverage[i]=default;}head=tail=0;Visits=0;projection=camera.projectionMatrix*camera.worldToCameraMatrix*localToWorld;
            var eye=localToWorld.inverse.MultiplyPoint3x4(camera.transform.position);int inside=-1;for(int i=0;i<layout.rooms.Length;i++)if(layout.rooms[i].bounds.Contains(eye)){inside=i;break;}
            var viewport=new Rect(0,0,1,1);
            if(inside>=0)Push(inside,viewport);
            else foreach(var portal in layout.portals)if(portal.b<0&&Open(portal,openDoors)&&Vector3.Dot(eye-portal.bounds.center,portal.normal)>0&&Project(portal.bounds,out var rect)&&Intersect(viewport,rect,out var clip))Push(portal.a,clip);
            while(head<tail&&Visits++<MaximumVisits){int room=queue[head];var aperture=rectangles[head++];foreach(var portal in layout.portals){int neighbour=portal.a==room?portal.b:portal.b==room?portal.a:-1;if(neighbour<0||!Open(portal,openDoors)||!Project(portal.bounds,out var rect)||!Intersect(aperture,rect,out var clip))continue;Push(neighbour,clip);}}
        }
        static bool Open(ModularBuildingLayout.Portal p,uint mask)=>p.window||p.door<0||(mask&(1u<<p.door))!=0;
        void Push(int room,Rect rect){if(room<0||room>=MaximumRooms||tail>=MaximumVisits)return;if(visible[room]){var old=coverage[room];if(old.xMin<=rect.xMin&&old.yMin<=rect.yMin&&old.xMax>=rect.xMax&&old.yMax>=rect.yMax)return;rect=Rect.MinMaxRect(Mathf.Min(old.xMin,rect.xMin),Mathf.Min(old.yMin,rect.yMin),Mathf.Max(old.xMax,rect.xMax),Mathf.Max(old.yMax,rect.yMax));}visible[room]=true;coverage[room]=rect;queue[tail]=room;rectangles[tail++]=rect;}
        bool Project(Bounds bounds,out Rect rect){float x0=float.MaxValue,y0=float.MaxValue,x1=float.MinValue,y1=float.MinValue;int front=0;
            for(int i=0;i<8;i++){var p=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));var clip=projection*new Vector4(p.x,p.y,p.z,1);if(clip.w<=.01f)continue;front++;float x=clip.x/clip.w*.5f+.5f,y=clip.y/clip.w*.5f+.5f;x0=Mathf.Min(x0,x);y0=Mathf.Min(y0,y);x1=Mathf.Max(x1,x);y1=Mathf.Max(y1,y);}
            rect=front<8?new Rect(0,0,1,1):Rect.MinMaxRect(x0,y0,x1,y1);return front>0;
        }
        static bool Intersect(Rect a,Rect b,out Rect result){float x=Mathf.Max(a.xMin,b.xMin),y=Mathf.Max(a.yMin,b.yMin),right=Mathf.Min(a.xMax,b.xMax),top=Mathf.Min(a.yMax,b.yMax);result=Rect.MinMaxRect(x,y,right,top);return right>x&&top>y;}
    }
}
