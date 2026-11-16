using System.Collections.Generic;
using MassiveArmy.Networking;
using UnityEngine;
namespace MassiveArmy.Planetary {
    internal static class SurvivalCampGeometry {
        internal readonly struct Part {internal readonly Vector3 center,size;internal readonly int material;internal Part(Vector3 center,Vector3 size,int material=0){this.center=center;this.size=size;this.material=material;}}
        internal static List<Part> Parts(SurvivalCampPublic camp){var parts=new List<Part>();var origin=SurvivalCampGeneration.Point(camp.pose);var inverse=Quaternion.Inverse(SurvivalCampGeneration.Rotation(camp.pose));
            foreach(var shop in camp.shops){var at=Double3.Rotate(inverse,SurvivalCampGeneration.Point(shop.pose)-origin).Float;
                void Box(Vector3 center,Vector3 size,int material=0)=>parts.Add(new Part(at+center,size,material));
                Box(new Vector3(0,.05f,4),new Vector3(12,.1f,10));Box(new Vector3(-5.8f,2,4),new Vector3(.3f,4,10));Box(new Vector3(5.8f,2,4),new Vector3(.3f,4,10));Box(new Vector3(0,2,8.8f),new Vector3(12,4,.3f));Box(new Vector3(0,4.2f,4),new Vector3(13,.4f,11));
                Box(new Vector3(0,.65f,.6f),new Vector3(8,1.3f,1.2f),1);Box(new Vector3(0,1.4f,.6f),new Vector3(8.4f,.2f,1.6f),2);Box(new Vector3(0,3.35f,-.6f),new Vector3(6,1,.2f),1);
                Box(new Vector3(-3,1,5.5f),new Vector3(1.5f,2,1.5f),1);Box(new Vector3(3,.5f,5.5f),new Vector3(2,1,2),1);
            }return parts;
        }
        internal static SurvivalContacts.Shape Contact(SurvivalCampPublic camp){var bounds=new Vector4[camp.shops.Count];var inverse=Quaternion.Inverse(SurvivalCampGeneration.Rotation(camp.pose));for(int i=0;i<bounds.Length;i++){var point=Double3.Rotate(inverse,SurvivalCampGeneration.Point(camp.shops[i].pose)-SurvivalCampGeneration.Point(camp.pose)).Float+new Vector3(0,2,4);bounds[i]=new Vector4(point.x,point.y,point.z,12);}var parts=Parts(camp);var solids=new BuildSolid[parts.Count];double radius=0;for(int i=0;i<parts.Count;i++){var part=parts[i];var half=part.size*.5f;solids[i]=BuildSolid.Box(part.center.x-half.x,part.center.x+half.x,part.center.z-half.z,part.center.z+half.z,part.center.y-half.y,part.center.y+half.y);radius=System.Math.Max(radius,part.center.magnitude+half.magnitude);}return new SurvivalContacts.Shape{id=camp.id*8+7,body=camp.pose.body,origin=SurvivalCampGeneration.Point(camp.pose),rotation=SurvivalCampGeneration.Rotation(camp.pose),solids=solids,radius=radius,indexingSpheres=bounds};}
    }
}

