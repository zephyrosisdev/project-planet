using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    internal static class RenewablePrototypes {
        internal static ColonyPrototypeAsset Create(string id){
            if(id!="machine-solar-panel"&&id!="machine-wind-turbine"&&id!="machine-battery")return null;
            var geometry=new ModularGeometry();var solids=new List<ColonyPrototypeSolid>();var shell=new Color(.78f,.82f,.85f,0);var dark=new Color(.12f,.17f,.22f,0);var orange=new Color(1,.31f,.035f,0);var cyan=new Color(.03f,.75f,1,1);
            void Box(Vector3 centre,Vector3 size,Color color,bool contact=false,Quaternion? rotation=null){geometry.Box(centre,size,color,rotation);if(contact)solids.Add(new ColonyPrototypeSolid{polygon=new[]{new Vector2(centre.x-size.x*.5f,centre.z-size.z*.5f),new Vector2(centre.x+size.x*.5f,centre.z-size.z*.5f),new Vector2(centre.x+size.x*.5f,centre.z+size.z*.5f),new Vector2(centre.x-size.x*.5f,centre.z+size.z*.5f)},bottom=centre.y-size.y*.5f,top=centre.y+size.y*.5f});}
            Vector3 dimensions;
            if(id=="machine-solar-panel"){
                dimensions=new Vector3(3.2f,1.7f,3.2f);Box(new Vector3(0,.1f,0),new Vector3(3.2f,.2f,3.2f),dark,true);for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Box(new Vector3(x*1.3f,.65f,z*1.2f),new Vector3(.12f,1.1f,.12f),orange,true);
                var q=Quaternion.Euler(10,0,0);Box(new Vector3(0,1.25f,0),new Vector3(3.1f,.12f,2.9f),shell,false,q);Box(new Vector3(0,1.32f,0),new Vector3(2.9f,.035f,2.7f),new Color(.025f,.12f,.32f,0),false,q);
                for(int n=-4;n<=4;n++){Box(new Vector3(n*.32f,1.35f,0),new Vector3(.014f,.012f,2.65f),shell,false,q);Box(new Vector3(0,1.35f,n*.29f),new Vector3(2.9f,.012f,.014f),shell,false,q);}Box(new Vector3(1.3f,.4f,-1.5f),new Vector3(.3f,.2f,.05f),cyan);
                // Conservative panel contact, separate from the thin decorative cell grid.
                solids.Add(new ColonyPrototypeSolid{polygon=new[]{new Vector2(-1.55f,-1.45f),new Vector2(1.55f,-1.45f),new Vector2(1.55f,1.45f),new Vector2(-1.55f,1.45f)},bottom=1,top=1.65f});
            }else if(id=="machine-wind-turbine"){
                dimensions=new Vector3(5,9,3);Box(new Vector3(0,.12f,0),new Vector3(1.8f,.24f,1.8f),dark,true);Box(new Vector3(0,3.3f,0),new Vector3(.3f,6.6f,.3f),shell,true);Box(new Vector3(0,6.6f,.3f),new Vector3(.6f,.6f,1.3f),orange,true);
                for(int blade=0;blade<3;blade++){var q=Quaternion.Euler(0,0,blade*120);Box(new Vector3(0,6.6f,1)+q*(Vector3.up*1.25f),new Vector3(.22f,2.4f,.1f),shell,false,q);}Box(new Vector3(0,.9f,-.25f),new Vector3(.18f,.3f,.04f),cyan);
            }else{
                dimensions=new Vector3(2,2.2f,1.5f);Box(new Vector3(0,1.1f,0),dimensions,shell,true);for(int n=-2;n<=2;n++)Box(new Vector3(0,.35f+(n+2)*.35f,-.77f),new Vector3(1.7f,.22f,.06f),dark);Box(new Vector3(.75f,1.85f,-.81f),new Vector3(.1f,.32f,.04f),cyan);for(int x=-1;x<=1;x+=2)Box(new Vector3(x*.92f,1.1f,-.8f),new Vector3(.1f,2,.08f),orange);
            }
            var mesh=geometry.Build(id);var result=new ColonyPrototypeAsset{id=id,family="machine",dimensions=dimensions,solids=solids.ToArray(),sockets=new[]{new ColonyPrototypeSocket{name="Power",kind="Power",position=new Vector3(0,.5f,-dimensions.z*.5f),yaw=180}},lod=new ColonyPrototypeMesh()};result.SetMesh(mesh);result.lod.ShareGeometry(result);return result;
        }
    }
}
