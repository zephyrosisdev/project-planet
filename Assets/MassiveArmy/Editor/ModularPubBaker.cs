using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MassiveArmy.Planetary;
namespace MassiveArmy.EditorTools {
    // Shared baked meshes/colliders and portal rooms; no furniture GameObjects.
    public static class ModularPubBaker {
        public static ModularBuildingBaker.Result Bake(ModularBuildingProfile profile){
            var result=new ModularBuildingBaker.Result();var layout=result.Own(ScriptableObject.CreateInstance<ModularBuildingLayout>());
            result.layout=layout;layout.buildingId=profile.buildingId;layout.material=profile.kit.material;layout.glassMaterial=profile.kit.glassMaterial;layout.detailDistance=profile.detailDistance;
            var shell=new ModularGeometry();var collision=new ModularGeometry();var glass=new ModularGeometry();
            var rooms=new System.Collections.Generic.List<ModularBuildingLayout.Room>();var interiors=new System.Collections.Generic.List<ModularGeometry>();var portals=new System.Collections.Generic.List<ModularBuildingLayout.Portal>();var doors=new System.Collections.Generic.List<ModularBuildingLayout.Door>();
            Color wall=profile.exterior,wood=new Color(.29f,.17f,.08f),cream=new Color(.76f,.7f,.58f);const float h=3.6f;
            void Box(ModularGeometry mesh,Vector3 centre,Vector3 size,Color colour,bool solid=true,Quaternion? rotation=null){mesh.Box(centre,size,colour,rotation);if(solid)collision.Box(centre,size,Color.white,rotation);}
            int Room(string name,Vector3 centre,Vector3 size){int i=rooms.Count;rooms.Add(new ModularBuildingLayout.Room{name=name,bounds=new Bounds(centre,size)});interiors.Add(new ModularGeometry());return i;}
            int Door(int a,int b,Vector3 centre,float width,float height,float yaw){int index=doors.Count;var mesh=new ModularGeometry();mesh.Box(Vector3.zero,new Vector3(width,height,.09f),wood);doors.Add(new ModularBuildingLayout.Door{centre=centre,width=width,height=height,yaw=yaw,panel=result.Own(mesh.Build("Pub door "+index))});portals.Add(new ModularBuildingLayout.Portal{a=a,b=b,door=index,bounds=new Bounds(centre,new Vector3(width,height,.15f)),normal=Quaternion.Euler(0,yaw,0)*Vector3.forward});return index;}
            void Link(int a,int b,Vector3 centre,Vector3 size,Vector3 normal){portals.Add(new ModularBuildingLayout.Portal{a=a,b=b,door=-1,bounds=new Bounds(centre,size),normal=normal});}
            int bar=Room("Public bar and pool room",new Vector3(0,1.8f,3.5f),new Vector3(24,h,17));
            int toilets=Room("Toilets and wash basins",new Vector3(-7,1.8f,-8.5f),new Vector3(10,h,7));
            int store=Room("Kitchen and cellar store",new Vector3(7,1.8f,-8.5f),new Vector3(10,h,7));
            int stair=Room("Stair hall",new Vector3(0,1.8f,-8.5f),new Vector3(4,h,7));
            int balcony=Room("Gallery overlooking the bar",new Vector3(0,5.4f,8.5f),new Vector3(24,h,7));
            int left=Room("West gallery",new Vector3(-10,5.4f,0),new Vector3(4,h,10));
            int right=Room("East gallery",new Vector3(10,5.4f,0),new Vector3(4,h,10));
            int landing=Room("Upstairs landing",new Vector3(0,5.4f,-4),new Vector3(24,h,2));
            int apartmentA=Room("Apartment 1",new Vector3(-7,5.4f,-8.5f),new Vector3(10,h,7));
            int apartmentB=Room("Apartment 2",new Vector3(7,5.4f,-8.5f),new Vector3(10,h,7));
            Box(shell,new Vector3(0,-.1f,0),new Vector3(24,.2f,24),profile.floor);
            Box(shell,new Vector3(0,7.3f,0),new Vector3(24.4f,.2f,24.4f),profile.roof);
            // Exterior panels include actual door/window apertures.
            for(int side=0;side<4;side++){var rotation=Quaternion.Euler(0,side*90,0);for(int floor=0;floor<2;floor++)for(int panel=0;panel<6;panel++){
                float x=-10+panel*4,y=floor*h;bool entrance=side==0&&floor==0&&(panel==2||panel==3);var centre=rotation*new Vector3(x,y+1.8f,12);
                if(entrance){float edge=panel==2?-2.7f:2.7f;Box(shell,rotation*new Vector3(edge,y+1.2f,12),new Vector3(2.6f,2.4f,.22f),wall);Box(shell,centre+Vector3.up*1.2f,new Vector3(4,1.2f,.22f),wall,true,rotation);}
                else {Box(shell,centre-Vector3.up*1.3f,new Vector3(4,1,.22f),wall,true,rotation);Box(shell,centre+Vector3.up*1.2f,new Vector3(4,1.2f,.22f),wall,true,rotation);
                    for(int sign=-1;sign<=1;sign+=2){shell.Box(centre+rotation*new Vector3(sign*1.5f,0,0),new Vector3(1,1.6f,.22f),wall,rotation);collision.Box(centre+rotation*new Vector3(sign*1.5f,0,0),new Vector3(1,1.6f,.22f),Color.white,rotation);}glass.Box(centre,new Vector3(2,1.6f,.03f),Color.white,rotation);collision.Box(centre,new Vector3(2,1.6f,.04f),Color.white,rotation);
                    int room=floor==0?side==2?toilets:bar:side==2?apartmentA:balcony;portals.Add(new ModularBuildingLayout.Portal{a=room,b=-1,door=-1,window=true,bounds=new Bounds(centre,new Vector3(4,2,4)),normal=rotation*Vector3.forward});}
            }}
            Door(bar,-1,new Vector3(0,1.2f,12),2.8f,2.4f,0);
            // Ground-floor service rooms and the stair corridor.
            for(int sign=-1;sign<=1;sign+=2){Box(shell,new Vector3(sign*2,1.8f,-8.5f),new Vector3(.2f,h,7),cream);int room=sign<0?toilets:store;Box(shell,new Vector3(sign*9,1.8f,-5),new Vector3(6,h,.2f),cream);Box(shell,new Vector3(sign*3,1.8f,-5),new Vector3(2,h,.2f),cream);Box(shell,new Vector3(sign*5,3,-5),new Vector3(2,1.2f,.2f),cream);Door(bar,room,new Vector3(sign*5,1.2f,-5),2,2.4f,0);}
            Link(bar,stair,new Vector3(0,1.8f,-5),new Vector3(3.8f,3.4f,.2f),Vector3.back);
            // Upper ring leaves a real void over the public room.
            Box(interiors[balcony],new Vector3(0,h-.08f,8.5f),new Vector3(24,.16f,7),profile.floor);
            Box(interiors[left],new Vector3(-10,h-.08f,0),new Vector3(4,.16f,10),profile.floor);Box(interiors[right],new Vector3(10,h-.08f,0),new Vector3(4,.16f,10),profile.floor);
            Box(interiors[landing],new Vector3(0,h-.08f,-4),new Vector3(24,.16f,2),profile.floor);
            for(int sign=-1;sign<=1;sign+=2){int room=sign<0?apartmentA:apartmentB;Box(interiors[room],new Vector3(sign*7,h-.08f,-8.5f),new Vector3(10,.16f,7),profile.floor);Box(shell,new Vector3(sign*2,5.4f,-8.5f),new Vector3(.2f,h,7),cream);Box(shell,new Vector3(sign*9,5.4f,-5),new Vector3(6,h,.2f),cream);Box(shell,new Vector3(sign*3,5.4f,-5),new Vector3(2,h,.2f),cream);Box(shell,new Vector3(sign*5,6.6f,-5),new Vector3(2,1.2f,.2f),cream);Door(landing,room,new Vector3(sign*5,4.8f,-5),2,2.4f,0);Box(interiors[room],new Vector3(sign*8,h+.45f,-9),new Vector3(2,.9f,2.8f),cream);Box(interiors[room],new Vector3(sign*4,h+.7f,-10),new Vector3(1,1.4f,1.2f),wood);}
            Link(bar,balcony,new Vector3(0,h,3),new Vector3(16,.2f,12),Vector3.up);Link(balcony,left,new Vector3(-10,5.4f,5),new Vector3(4,h,.2f),Vector3.back);Link(balcony,right,new Vector3(10,5.4f,5),new Vector3(4,h,.2f),Vector3.back);Link(left,landing,new Vector3(-10,5.4f,-5),new Vector3(4,h,.2f),Vector3.back);Link(right,landing,new Vector3(10,5.4f,-5),new Vector3(4,h,.2f),Vector3.back);Link(stair,landing,new Vector3(0,h,-4),new Vector3(3.8f,.2f,2),Vector3.up);
            // Rails, stair treads and a smooth collision ramp.
            for(int side=-1;side<=1;side+=2){Box(interiors[side<0?left:right],new Vector3(side*8,h+1,-.5f),new Vector3(.1f,.12f,11),wood);for(int z=-5;z<=5;z++)Box(interiors[side<0?left:right],new Vector3(side*8,h+.5f,z),new Vector3(.08f,1,.08f),wood);}
            Box(interiors[landing],new Vector3(0,h+1,-3),new Vector3(16,.12f,.12f),wood);for(int x=-8;x<=8;x++)Box(interiors[landing],new Vector3(x,h+.5f,-3),new Vector3(.08f,1,.08f),wood);
            Box(interiors[balcony],new Vector3(0,h+1,5),new Vector3(16,.12f,.12f),wood);for(int x=-8;x<=8;x++)Box(interiors[balcony],new Vector3(x,h+.5f,5),new Vector3(.08f,1,.08f),wood);
            for(int i=0;i<20;i++)interiors[stair].Box(new Vector3(0,(i+1)*h/40,-10.5f+(i+.5f)*5.5f/20),new Vector3(3.4f,(i+1)*h/20,5.5f/20),wood);
            collision.Quad(new Vector3(-1.7f,0,-10.5f),new Vector3(-1.7f,h,-5),new Vector3(1.7f,h,-5),new Vector3(1.7f,0,-10.5f),Color.white);
            // Bar, stools, pool table and toilets are baked into room meshes.
            Box(interiors[bar],new Vector3(-6,.6f,-1),new Vector3(7,1.2f,1),wood);Box(interiors[bar],new Vector3(-6,1.25f,-1),new Vector3(7.3f,.15f,1.2f),wood);
            for(int i=0;i<4;i++)Box(interiors[bar],new Vector3(-9+i*2,.42f,1),new Vector3(.6f,.84f,.6f),wood);
            Box(interiors[bar],new Vector3(6,.95f,1),new Vector3(2.4f,.25f,4.2f),new Color(.04f,.32f,.16f));for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Box(interiors[bar],new Vector3(6+x*.9f,.4f,1+z*1.7f),new Vector3(.18f,.8f,.18f),wood);
            for(int i=0;i<3;i++){Box(interiors[toilets],new Vector3(-10+i*2,.4f,-10),new Vector3(.7f,.8f,1.1f),Color.white);Box(interiors[toilets],new Vector3(-10+i*2,1,-11),new Vector3(.7f,.6f,.35f),Color.white);}Box(interiors[toilets],new Vector3(-4,.85f,-9),new Vector3(1.2f,.3f,2.2f),Color.white);
            var flat=new ModularGeometry();flat.Box(new Vector3(0,3.6f,0),new Vector3(24,7.2f,24),wall);var proxy=new ModularGeometry();proxy.Box(new Vector3(0,3.6f,0),new Vector3(24,7.2f,24),wall);
            layout.exterior=result.Own(shell.Build("Pub outer shell"));layout.flat=result.Own(flat.Build("Pub flat LOD"));layout.proxy=result.Own(proxy.Build("Pub bounds LOD"));layout.collision=result.Own(collision.Build("Pub shared collision"));layout.glass=result.Own(glass.Build("Pub glass"));
            for(int i=0;i<rooms.Count;i++)rooms[i].mesh=result.Own(interiors[i].Build(rooms[i].name));layout.rooms=rooms.ToArray();layout.portals=portals.ToArray();layout.doors=doors.ToArray();layout.bounds=layout.exterior.bounds;layout.numberPosition=new Vector3(2,2.3f,12.2f);return result;
        }
        [MenuItem("Synthetic Front/Buildings/Generate pub and funeral directors")]
        public static void Generate(){string root=ModularBuildingWindow.Root;var source=AssetDatabase.LoadAssetAtPath<ModularBuildingProfile>(root+"/Profiles/civil_housing.asset");if(source==null)throw new InvalidOperationException("Create the modular building kit first.");var profiles=new System.Collections.Generic.List<ModularBuildingProfile>();foreach(string id in new[]{"civil_pub",CivilisationRuntime.FuneralDirectors}){string path=root+"/Profiles/"+id+".asset";var p=AssetDatabase.LoadAssetAtPath<ModularBuildingProfile>(path);if(p==null){p=ScriptableObject.CreateInstance<ModularBuildingProfile>();AssetDatabase.CreateAsset(p,path);}p.buildingId=id;p.kit=source.kit??AssetDatabase.LoadAssetAtPath<ModularBuildingKit>(root+"/DefaultKit.asset");p.enabledForGeneration=true;p.gardenWalls=false;p.use=id=="civil_pub"?ModularUse.Pub:ModularUse.Office;p.width=id=="civil_pub"?24:14;p.depth=id=="civil_pub"?24:16;p.floors=id=="civil_pub"?2:1;p.floorHeight=3.6f;p.pitchedRoof=false;EditorUtility.SetDirty(p);profiles.Add(p);}AssetDatabase.SaveAssets();ModularBuildingWindow.BakeAll(false,null,profiles.ToArray());System.IO.File.WriteAllText("TestResults/Gameplay/pub-assets.txt","Generated modular public house with two apartments, gallery, bar, pool table, toilets and shared collision; funeral directors profile.");}
    }
}
