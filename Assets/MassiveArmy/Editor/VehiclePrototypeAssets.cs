using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MassiveArmy.Planetary;

namespace MassiveArmy.EditorTools
{
    /// <summary>Original metre-scale, editable mesh assets. No imported commercial designs or runtime mesh generation.</summary>
    public static partial class VehiclePrototypeAssets
    {
        const string Root="Assets/MassiveArmy/Resources/MassiveArmy/Vehicles";
        const string Request="TestResults/Vehicles/generate.request";
        static readonly Color Armour=new Color(.22f,.3f,.25f), Dark=new Color(.055f,.065f,.075f), Trim=new Color(.7f,.54f,.19f), Glass=new Color(.12f,.4f,.49f);
        static readonly List<Mesh> meshes=new List<Mesh>();
        static Material material;
        [InitializeOnLoadMethod] static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump(){if(EditorApplication.isCompiling||!File.Exists(Request))return;string action=File.ReadAllText(Request).Trim();File.Delete(Request);try{if(action=="validate-large-planet-motion")VehicleRtsValidation.CheckLargePlanetMotion();else if(action=="probe-rts-movement")VehicleRtsValidation.ProbeLive();else if(action=="start-live-rts")VehicleRtsValidation.StartLive();else if(action=="diagnose-rts-movement")VehicleRtsValidation.DiagnoseLive();else if(action=="validate-rts-vehicles")VehicleRtsValidation.Run();else if(action=="validate-imported-tank")ImportedTankAssets.ValidateAssets();else if(action=="optimize-imported-tank")ImportedTankAssets.OptimizeExisting();else if(action=="generate-imported-tank")ImportedTankAssets.Generate();else if(action=="validate-civic-gpu")GameplayFeatureValidation.RunCivicGpu();else if(action=="validate-pub")GameplayFeatureValidation.RunPubValidation();else if(action=="generate-pub")ModularPubBaker.Generate();else if(action=="validate-vehicle-motion")GameplayFeatureValidation.RunVehicleMotion();else if(action=="validate-crater-precision")GameplayFeatureValidation.RunPrecision();else if(action=="validate-gameplay")GameplayFeatureValidation.Run();else if(action=="generate-quad")GenerateQuad();else if(action=="save-default-cloud-preset")PlanetLiveWeatherEditor.SavePresetAsDefault();else if(action=="apply-cloud-preset")PlanetLiveWeatherEditor.ApplyLargePlanetPreset();else if(action=="capture-weather-wide")PlanetSystemScaleValidation.CaptureCurrentWeather(true);else if(action=="capture-weather")PlanetSystemScaleValidation.CaptureCurrentWeather();else if(action=="validate-scale")PlanetSystemScaleValidation.Run();else if(action=="validate")VehiclePrototypeValidation.Run();else Generate();}catch(Exception e){Debug.LogException(e);}}
        [MenuItem("Synthetic Front/Vehicles/Generate missing prototype models")]
        public static void Generate(){
            Directory.CreateDirectory(Root);
            material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/VehicleArmour.mat");
            if(material==null){material=new Material(Shader.Find("MassiveArmy/Modular structures")){name="Shared vehicle vertex-colour armour"};AssetDatabase.CreateAsset(material,Root+"/VehicleArmour.mat");}
            Build("SF-MW1",VehicleLocomotion.Walker,2,new Vector3(4.8f,7.5f,5.4f),3);
            Build("SF-LUV1",VehicleLocomotion.Wheeled,0,new Vector3(2.35f,2.55f,4.8f),4);
            VehicleInteriorAssets.Upgrade();RefreshStances();AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("Vehicle prototypes updated: one biped mech and one wheeled vehicle, both with first-person interiors.");
        }
        static void Build(string id,VehicleLocomotion kind,int count,Vector3 size,int crew){
            string path=Root+"/"+id+".asset";if(AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path)!=null)return;
            meshes.Clear();var d=ScriptableObject.CreateInstance<VehicleDefinition>();d.id=id;d.displayName=id+" "+(kind==VehicleLocomotion.Wheeled?"Utility":count==4?"Quadruped":crew==1?"Scout":"Combat walker");d.locomotion=kind;d.size=size;
            d.mass=kind==VehicleLocomotion.Wheeled?4200:crew==1?2800:count==4?18000:32000;d.maximumSpeed=kind==VehicleLocomotion.Wheeled?24:crew==1?13:8;d.reverseSpeed=d.maximumSpeed*.4f;
            d.acceleration=kind==VehicleLocomotion.Wheeled?4:crew==1?3.2f:1.8f;d.turnSpeed=crew==1?65:kind==VehicleLocomotion.Wheeled?50:30;d.turnAcceleration=d.turnSpeed*1.8f;
            float hipHeight=kind==VehicleLocomotion.Wheeled?.85f:size.y*.48f;
            d.chassisCentre=new Vector3(0,kind==VehicleLocomotion.Wheeled?1.25f:hipHeight+.35f,0);d.chassisSize=new Vector3(size.x*(count==4?.55f:.72f),size.y*.25f,size.z*.75f);d.centreOfMass=d.chassisCentre;
            d.seats=new VehicleSeatDefinition[crew];for(int i=0;i<crew;i++){
                var role=i==0?VehicleSeatRole.Driver:i==1&&kind==VehicleLocomotion.Walker?VehicleSeatRole.Gunner:i==2&&kind==VehicleLocomotion.Walker?VehicleSeatRole.Commander:VehicleSeatRole.Passenger;
                var p=new Vector3(crew==1?0:(i%2==0?-.45f:.45f),kind==VehicleLocomotion.Wheeled?1.15f:size.y*.76f-.65f,kind==VehicleLocomotion.Wheeled?(i<2?.4f:-.7f):.2f);
                d.seats[i]=new VehicleSeatDefinition{name=role+" "+(i+1),role=role,permissions=i==0?VehicleSeatPermissions.Movement|(crew==1?VehicleSeatPermissions.Aim|VehicleSeatPermissions.Fire:VehicleSeatPermissions.None):role==VehicleSeatRole.Gunner?VehicleSeatPermissions.Aim|VehicleSeatPermissions.Fire:role==VehicleSeatRole.Commander?VehicleSeatPermissions.Command:VehicleSeatPermissions.None,
                    position=p,eye=p+Vector3.up*.65f+Vector3.forward*.3f,entry=new Vector3((i%2==0?-1:1)*(size.x*.5f+.6f),0,.2f),exit=new Vector3((i%2==0?-1:1)*(size.x*.5f+1),0,.2f),aiByDefault=role==VehicleSeatRole.Gunner};
            }
            var root=new GameObject(d.displayName);var rig=root.AddComponent<VehicleRig>();rig.definition=d;
            Part(root.transform,"Chassis",d.chassisCentre,d.chassisSize,Armour,true);
            var box=root.AddComponent<BoxCollider>();box.center=d.chassisCentre;box.size=d.chassisSize;rig.chassis=box;
            var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            // Chamfered, asymmetrical stepped armour with an inset canopy gives each prototype its own silhouette.
            rig.torso=new GameObject("Aim assembly").transform;rig.torso.SetParent(root.transform,false);rig.torso.localPosition=new Vector3(0,kind==VehicleLocomotion.Wheeled?1.6f:size.y*.66f,0);
            if(kind==VehicleLocomotion.Wheeled){
                Part(rig.torso,"Cabin",new Vector3(0,.05f,.25f),new Vector3(1.95f,1.05f,2.15f),Armour,true);
                Part(rig.torso,"Windshield",new Vector3(0,.2f,1.34f),new Vector3(1.65f,.62f,.035f),Glass);
                Part(root.transform,"Rear cargo tray",new Vector3(0,1.3f,-1.75f),new Vector3(2.2f,.25f,1.1f),Dark);
                Part(root.transform,"Front bumper",new Vector3(0,.75f,2.3f),new Vector3(2.3f,.2f,.2f),Trim);
                foreach(int side in new[]{-1,1}){Part(root.transform,"Headlamp",new Vector3(side*.8f,1.02f,2.12f),new Vector3(.3f,.18f,.07f),Color.white);Part(root.transform,"Side glazing",new Vector3(side*1.0f,1.92f,.3f),new Vector3(.04f,.5f,1.4f),Glass);}
                d.wheels=new VehicleWheelDefinition[4];rig.wheels=new Transform[4];
                for(int i=0;i<4;i++){var p=new Vector3((i%2==0?-1:1)*1.02f,.48f,i<2?1.43f:-1.45f);d.wheels[i]=new VehicleWheelDefinition{name=(i<2?"Front":"Rear")+(i%2==0?" left":" right"),position=p,radius=.48f,width=.27f,suspension=.3f,steering=i<2};rig.wheels[i]=Wheel(root.transform,d.wheels[i]);}
            }else{
                Part(rig.torso,"Upper command pod",new Vector3(0,size.y*.06f,0),new Vector3(size.x*.58f,size.y*.3f,size.z*.48f),Armour,true);
                Part(rig.torso,"Inset pilot visor",new Vector3(0,size.y*.12f,size.z*.25f),new Vector3(size.x*.4f,size.y*.08f,.08f),Glass);
                Part(rig.torso,"Offset sensor mast",new Vector3(size.x*.24f,size.y*.24f,-size.z*.15f),new Vector3(.12f,size.y*.15f,.12f),Dark);
                Part(rig.torso,"Weapon barrel",new Vector3(size.x*.32f,0,size.z*.25f),new Vector3(size.x*.12f,size.y*.055f,size.z*.4f),Dark);
                rig.muzzle=new GameObject("Muzzle").transform;rig.muzzle.SetParent(rig.torso,false);rig.muzzle.localPosition=new Vector3(size.x*.32f,0,size.z*.45f);
                d.legs=new WalkerLegDefinition[count];rig.legs=new WalkerLegRig[count];
                for(int i=0;i<count;i++){
                    float side=i%2==0?-1:1;var hip=new Vector3(side*size.x*(count==4?.22f:.23f),hipHeight,count==4?(i<2?1:-1)*size.z*.29f:0);var stance=new Vector3(side*size.x*.38f,.08f,count==4?hip.z:0);
                    float length=hipHeight*.58f;d.legs[i]=new WalkerLegDefinition{name=count==4?(i<2?"Front":"Rear")+(side<0?" left":" right"):side<0?"Left":"Right",hip=hip,stance=stance,pole=new Vector3(side*.15f,0,count==4&&i>=2?-1:1),upperLength=length,lowerLength=length,footWidth=size.x*(count==4?.16f:.22f),footLength=size.z*.3f,stride=size.y*.33f,stepHeight=size.y*.13f,probeHeight=size.y*.4f,probeDepth=size.y,compression=size.y*.15f,maxReach=length*1.95f};
                    var leg=d.legs[i];var upper=Part(root.transform,leg.name+" upper",hip,new Vector3(size.x*.12f,length,.24f*size.z),Armour,true);OffsetMesh(upper,Vector3.down*length*.5f);
                    var lower=Part(root.transform,leg.name+" lower",hip+Vector3.down*length,new Vector3(size.x*.09f,length,.16f*size.z),Dark,true);OffsetMesh(lower,Vector3.down*length*.5f);
                    var foot=Part(root.transform,leg.name+" foot",stance,new Vector3(leg.footWidth,.15f,leg.footLength),Trim,true);rig.legs[i]=new WalkerLegRig{upper=upper,lower=lower,foot=foot};
                }
                d.gaits=new[]{new WalkerGaitDefinition{name="Walk",groups=count==4?new[]{0,2,3,1}:new[]{0,1},minimumGrounded=count==4?3:1,cadence=1.4f,maximumSpeed=d.maximumSpeed*.55f},new WalkerGaitDefinition{name="Travel",groups=count==4?new[]{0,1,1,0}:new[]{0,1},minimumGrounded=count==4?2:1,cadence=2.2f,strideMultiplier=1.15f,maximumSpeed=d.maximumSpeed}};
            }
            for(int i=0;i<d.legs.Length;i++){
                var leg=d.legs[i];WalkerKinematics.Solve(leg.hip,leg.stance,leg.pole,Vector3.up,leg,out var knee,out var ankle);
                WalkerKinematics.Present(rig.legs[i],leg.hip,knee,ankle,Vector3.forward,Vector3.up);
            }
            // Visible crew locations and separate chairs are part of the baked model, not simulated crew objects.
            foreach(var seat in d.seats){Part(root.transform,seat.name+" cushion",seat.position,new Vector3(.5f,.16f,.55f),Dark);Part(root.transform,seat.name+" back",seat.position+new Vector3(0,.3f,-.25f),new Vector3(.5f,.65f,.1f),Dark);}
            rig.articulatedRenderers=root.GetComponentsInChildren<Renderer>();
            var merged=new ModularGeometry();foreach(var filter in root.GetComponentsInChildren<MeshFilter>()){
                var m=filter.sharedMesh;var matrix=root.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;var colours=m.colors;
                // One flat box per part at LOD1, rather than retaining all bevels and wheel treads.
                merged.Box(matrix.MultiplyPoint3x4(m.bounds.center),Vector3.Scale(m.bounds.size,filter.transform.lossyScale),colours.Length>0?colours[0]:Armour,Quaternion.Inverse(root.transform.rotation)*filter.transform.rotation);
            }
            rig.distantProxy=new GameObject("LOD 1 rigid silhouette");rig.distantProxy.transform.SetParent(root.transform,false);rig.distantProxy.AddComponent<MeshFilter>().sharedMesh=SaveMesh(merged.Build(id+" rigid silhouette"));rig.distantProxy.AddComponent<MeshRenderer>().sharedMaterial=material;
            var proxy=Part(root.transform,"LOD 2 bounding shell",Vector3.up*size.y*.5f,size,Armour,true);
            var group=root.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(.12f,rig.articulatedRenderers),new LOD(.025f,new[]{rig.distantProxy.GetComponent<Renderer>()}),new LOD(.002f,new[]{proxy.GetComponent<Renderer>()})});group.RecalculateBounds();
            AssetDatabase.CreateAsset(d,path);d.prefab=PrefabUtility.SaveAsPrefabAsset(root,Root+"/"+id+".prefab");EditorUtility.SetDirty(d);UnityEngine.Object.DestroyImmediate(root);
        }
        static Mesh SaveMesh(Mesh mesh){string name=mesh.name.Replace(' ','_');string path=AssetDatabase.GenerateUniqueAssetPath(Root+"/"+name+".asset");AssetDatabase.CreateAsset(mesh,path);meshes.Add(mesh);return mesh;}
        static void RefreshStances(){
            foreach(var id in new[]{"SF-MW1","SF-LUV1"}){
                string path=Root+"/"+id+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
                try{
                    var rig=root.GetComponent<VehicleRig>();var d=rig.definition;
                    var body=root.GetComponent<Rigidbody>();if(body==null)body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.mass=d.mass;
                    if(d.locomotion==VehicleLocomotion.Walker)foreach(var seat in d.seats){
                        if(Mathf.Abs(seat.position.y-d.size.y*.7f)<.0001f){seat.position.y=d.size.y*.76f-.65f;seat.eye=seat.position+Vector3.up*.65f+Vector3.forward*.3f;EditorUtility.SetDirty(d);}
                        var cushion=root.transform.Find(seat.name+" cushion");var back=root.transform.Find(seat.name+" back");if(cushion!=null)cushion.localPosition=seat.position;if(back!=null)back.localPosition=seat.position+new Vector3(0,.3f,-.25f);
                    }
                    for(int i=0;i<rig.legs.Length;i++){var leg=d.legs[i];WalkerKinematics.Solve(leg.hip,leg.stance,leg.pole,Vector3.up,leg,out var knee,out var ankle);WalkerKinematics.Present(rig.legs[i],root.transform.TransformPoint(leg.hip),root.transform.TransformPoint(knee),root.transform.TransformPoint(ankle),root.transform.forward,root.transform.up);}
                    // Update generated rest geometry in place; keep mesh GUIDs and user-edited definitions.
                    var barrel=root.transform.Find("Aim assembly/Weapon barrel");
                    if(barrel!=null){barrel.localPosition=new Vector3(d.size.x*.32f,0,d.size.z*.25f);if(rig.muzzle==barrel){rig.muzzle=new GameObject("Muzzle").transform;rig.muzzle.SetParent(barrel.parent,false);rig.muzzle.localPosition=new Vector3(d.size.x*.32f,0,d.size.z*.45f);}}
                    var g=new ModularGeometry();foreach(var renderer in rig.articulatedRenderers){var f=renderer.GetComponent<MeshFilter>();if(f==null)continue;var m=f.sharedMesh;var matrix=root.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;var colours=m.colors;g.Box(matrix.MultiplyPoint3x4(m.bounds.center),m.bounds.size,colours.Length>0?colours[0]:Armour,Quaternion.Inverse(root.transform.rotation)*f.transform.rotation);}
                    var existing=rig.distantProxy.GetComponent<MeshFilter>().sharedMesh;var fresh=g.Build(existing.name);EditorUtility.CopySerialized(fresh,existing);UnityEngine.Object.DestroyImmediate(fresh);EditorUtility.SetDirty(existing);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }finally{PrefabUtility.UnloadPrefabContents(root);}
            }
        }
        static Transform Part(Transform parent,string name,Vector3 position,Vector3 size,Color colour,bool bevel=false){
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;var g=new ModularGeometry();
            if(bevel){g.Box(Vector3.zero,new Vector3(size.x*.8f,size.y,size.z),colour);g.Box(Vector3.zero,new Vector3(size.x,size.y*.75f,size.z*.85f),colour*.92f);}else g.Box(Vector3.zero,size,colour);
            go.AddComponent<MeshFilter>().sharedMesh=SaveMesh(g.Build(name));go.AddComponent<MeshRenderer>().sharedMaterial=material;return go.transform;
        }
        static void OffsetMesh(Transform part,Vector3 offset){var mesh=part.GetComponent<MeshFilter>().sharedMesh;var v=mesh.vertices;for(int i=0;i<v.Length;i++)v[i]+=offset;mesh.vertices=v;mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);}
        static Transform Wheel(Transform root,VehicleWheelDefinition d){
            var go=new GameObject(d.name);go.transform.SetParent(root,false);go.transform.localPosition=d.position;var g=new ModularGeometry();const int segments=16;
            for(int i=0;i<segments;i++){float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;var p=new Vector3(0,Mathf.Cos(a)*d.radius,Mathf.Sin(a)*d.radius);var q=new Vector3(0,Mathf.Cos(b)*d.radius,Mathf.Sin(b)*d.radius);var x=Vector3.right*d.width*.5f;g.Quad(p-x,q-x,q+x,p+x,Dark);g.Triangle(-x,q-x,p-x,Dark*.8f);g.Triangle(x,p+x,q+x,Dark*.8f);if(i%2==0)g.Box((p+q)*.49f,new Vector3(d.width*1.08f,.06f,.14f),Dark*1.5f,Quaternion.Euler(a*Mathf.Rad2Deg,0,0));}
            g.Box(Vector3.zero,new Vector3(d.width*1.1f,d.radius*.6f,d.radius*.6f),Trim);go.AddComponent<MeshFilter>().sharedMesh=SaveMesh(g.Build(d.name));go.AddComponent<MeshRenderer>().sharedMaterial=material;return go.transform;
        }
    }
}
