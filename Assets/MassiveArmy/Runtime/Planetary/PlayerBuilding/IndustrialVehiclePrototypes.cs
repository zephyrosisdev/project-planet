using System.Collections.Generic;
using UnityEngine;

namespace MassiveArmy.Planetary {
    // Shared prototype equipment, with no renderer or physics object per decorative part.
    internal static class IndustrialVehiclePrototypes {
        internal static ColonyPrototypeAsset Create(string id){
            if(id!="machine-truck"&&id!="machine-loader"&&id!="machine-excavator")return null;
            var geometry=new ModularGeometry();var distant=new ModularGeometry();var solids=new List<ColonyPrototypeSolid>();
            var orange=new Color(.95f,.35f,.035f,0);var shell=new Color(.65f,.72f,.76f,0);var dark=new Color(.09f,.12f,.15f,0);var glass=new Color(.04f,.4f,.57f,.3f);
            void Box(Vector3 centre,Vector3 size,Color color,bool collision=false){geometry.Box(centre,size,color);distant.Box(centre,size,color);if(collision)solids.Add(new ColonyPrototypeSolid{polygon=new[]{new Vector2(centre.x-size.x*.5f,centre.z-size.z*.5f),new Vector2(centre.x+size.x*.5f,centre.z-size.z*.5f),new Vector2(centre.x+size.x*.5f,centre.z+size.z*.5f),new Vector2(centre.x-size.x*.5f,centre.z+size.z*.5f)},bottom=centre.y-size.y*.5f,top=centre.y+size.y*.5f});}
            void Strut(Vector3 start,Vector3 end,float width,Color color){var delta=end-start;var q=Quaternion.FromToRotation(Vector3.up,delta.normalized);geometry.Box((start+end)*.5f,new Vector3(width,delta.magnitude,width),color,q);distant.Box((start+end)*.5f,new Vector3(width,delta.magnitude,width),color,q);}
            Box(new Vector3(0,.9f,0),new Vector3(2.2f,.55f,4.6f),dark,true);Box(new Vector3(0,1.3f,0),new Vector3(2.3f,.3f,4.4f),orange,true);
            for(int side=-1;side<=1;side+=2)for(int axle=-1;axle<=1;axle+=2){Box(new Vector3(side*1.04f,.58f,axle*1.55f),new Vector3(.42f,1.1f,1.1f),dark,true);geometry.Box(new Vector3(side*1.26f,.58f,axle*1.55f),new Vector3(.04f,.48f,.48f),shell);}
            float cabX=id=="machine-excavator"?-.55f:0,cabZ=id=="machine-truck"?1.35f:.35f;
            Box(new Vector3(cabX,2.05f,cabZ),new Vector3(id=="machine-excavator"?1.15f:1.85f,1.35f,1.5f),shell,true);Box(new Vector3(cabX,2.8f,cabZ),new Vector3(id=="machine-excavator"?1.3f:2, .15f,1.65f),orange);
            Box(new Vector3(cabX,2.25f,cabZ+.76f),new Vector3(id=="machine-excavator"?.9f:1.55f,.65f,.035f),glass);for(int side=-1;side<=1;side+=2)geometry.Box(new Vector3(cabX+side*(id=="machine-excavator"?.58f:.93f),2.25f,cabZ),new Vector3(.035f,.65f,1.1f),glass);
            if(id=="machine-truck"){
                Box(new Vector3(0,1.6f,-1),new Vector3(2.2f,.22f,2.7f),shell,true);for(int side=-1;side<=1;side+=2)Box(new Vector3(side*1.02f,2,-1),new Vector3(.16f,.8f,2.7f),orange,true);Box(new Vector3(0,2,-2.28f),new Vector3(2.2f,.8f,.16f),orange,true);Box(new Vector3(0,2,.27f),new Vector3(2.2f,.8f,.16f),orange,true);
            }else{
                bool excavator=id=="machine-excavator";float armX=excavator?.6f:0;var shoulder=new Vector3(armX,1.7f,1);var elbow=new Vector3(armX,excavator?3.8f:1.6f,3);var wrist=new Vector3(armX,.8f,4.5f);Strut(shoulder,elbow,.35f,orange);Strut(elbow,wrist,.28f,orange);Strut(shoulder+Vector3.up*.3f,elbow-Vector3.up*.3f,.12f,shell);
                Box(new Vector3(armX,.5f,4.65f),new Vector3(1.8f,.18f,1.3f),dark,true);Box(new Vector3(armX,.9f,4.1f),new Vector3(1.8f,.7f,.16f),orange,true);for(int side=-1;side<=1;side+=2)Box(new Vector3(armX+side*.84f,.75f,4.65f),new Vector3(.12f,.5f,1.3f),orange,true);
                for(int tooth=-2;tooth<=2;tooth++)geometry.Box(new Vector3(armX+tooth*.32f,.48f,5.35f),new Vector3(.13f,.12f,.32f),shell);
            }
            var asset=new ColonyPrototypeAsset{id=id,family="machine",dimensions=new Vector3(2.5f,4,5),solids=solids.ToArray(),sockets=new ColonyPrototypeSocket[0],lod=new ColonyPrototypeMesh()};asset.SetMesh(geometry.Build(id));asset.lod.SetMesh(distant.Build(id+" distant"));return asset;
        }
    }
}
