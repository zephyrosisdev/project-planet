using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    static class ShipPrototypeLibrary {
        static Dictionary<string,ColonyPrototypeAsset> assets;
        internal static ColonyPrototypeAsset Find(string id){if(assets==null){var text=Resources.Load<TextAsset>("MassiveArmy/ShipArt/ModularShipCatalogue");if(text==null)return null;var data=JsonUtility.FromJson<ColonyPrototypeCatalogue>(text.text);assets=new Dictionary<string,ColonyPrototypeAsset>();foreach(var asset in data.assets)assets.Add(asset.id,asset);}return assets.TryGetValue(id,out var result)?result:null;}
    }
    public sealed partial class PlanetSpacecraft {
        ShipAssembly assembly;long assemblyRevision=long.MinValue;Transform modularRoot;Material modularMaterial;Mesh moduleVisualMesh,moduleContactMesh,moduleGlassMesh;Mesh shipWireMesh;GameObject shipWireVisual;
        public ShipAssembly Assembly=>assembly;
        internal void SetAssembly(ShipAssembly value){if(value==null||ReferenceEquals(assembly,value)&&assemblyRevision==value.revision)return;assembly=value;value.UpdatePower();if(!CockpitPowered)StopOrbitalVelocityHold();if(EnginePower<=0){AutoFlightActive=false;SetFlightAssistance(false);StopOrbitalVelocityHold();}RebuildModularSections();}
        static Vector3 Local(FramePose p)=>new Vector3((float)p.x,(float)p.y,(float)p.z);
        internal Vector3 ShipModulePoint(ShipModuleRecord m,FramePose p)=>Local(ShipModules.Transform(m.pose,p));
        bool TryBuildModularInterior(){
            if(ShipPrototypeLibrary.Find("hangar")==null)return false;
            assembly=ShipModules.Starter(1);modularMaterial=ColonyPrototypeLibrary.Material("Socketed ship armor");RebuildModularSections();
            // These moving contacts keep the established carrier/door frame contract.
            HatchAt("REAR HATCH",-38.25f,25.5f);HatchAt("INNER AIRLOCK",-31,8);
            Part("Bridge upper pressure bulkhead",new Vector3(0,4.25f,10),new Vector3(18.5f,3.5f,.5f),steel);
            foreach(int side in new[]{-1,1})Part("Hangar shoulder pressure bulkhead",new Vector3(side*17.5f,1,10),new Vector3(16.5f,10,.5f),steel);
            ramp=Part("Boarding ramp",new Vector3(0,-6,-44),new Vector3(12,.4f,13),steel,Quaternion.identity,true);
            CabinLight(new Vector3(0,0,-43));CabinLight(new Vector3(0,1,25));CabinLight(new Vector3(0,1,-31));CabinLight(new Vector3(0,1,-5));CabinLight(new Vector3(-18,1,-8));CabinLight(new Vector3(18,1,-15));
            FlushInteriorBatches();return true;
        }
        void FlushInteriorBatches(){foreach(var batch in batches){var mesh=new Mesh{name="Combined ship fittings"};mesh.CombineMeshes(batch.Value.ToArray(),true,true);interiorMeshes.Add(mesh);var visual=new GameObject("Shared ship fittings");visual.transform.SetParent(hull.transform,false);visual.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=visual.AddComponent<MeshRenderer>();renderer.sharedMaterial=batch.Key;renderer.shadowCastingMode=batch.Key==window?ShadowCastingMode.Off:ShadowCastingMode.On;}batches.Clear();}
        void RebuildModularSections(){
            if(assembly==null||modularMaterial==null)return;if(modularRoot!=null){modularRoot.gameObject.SetActive(false);ArmyLifetime.Destroy(modularRoot.gameObject);}
            var root=new GameObject("Socketed ship modules · one aggregate flight body");root.transform.SetParent(hull.transform,false);modularRoot=root.transform;
            ArmyLifetime.Destroy(moduleVisualMesh);ArmyLifetime.Destroy(moduleContactMesh);ArmyLifetime.Destroy(moduleGlassMesh);
            var glazing=new ModularGeometry();var visualGeometry=new ModularGeometry();var contacts=new ModularGeometry();var bays=new List<VehicleCarrierBay>();
            foreach(var m in assembly.modules){var asset=ShipPrototypeLibrary.Find(m.definition);if(asset==null)continue;var q=Quaternion.Euler(0,m.pose.yaw,0);var at=Local(m.pose);visualGeometry.AppendMesh(asset.Mesh("Ship "+m.definition),at,q);contacts.AppendMesh(asset.Collision(),at,q);
                var spec=ShipModules.Definitions[m.definition];
                if(m.definition=="cockpit"){
                    var chair=ShipModulePoint(m,ShipModules.P(0,-2.6,0));var back=ShipModulePoint(m,ShipModules.P(0,-1.8,-.5));visualGeometry.Box(chair,new Vector3(1.2f,.8f,1.2f),new Color(.24f,.3f,.36f),q);visualGeometry.Box(back,new Vector3(1.2f,1.3f,.25f),new Color(.24f,.3f,.36f),q);contacts.Box(chair,new Vector3(1.2f,.8f,1.2f),Color.white,q);contacts.Box(back,new Vector3(1.2f,1.3f,.25f),Color.white,q);
                    if(!assembly.Occupied(m.id,"forward")){var glassAt=ShipModulePoint(m,ShipModules.P(0,0,spec.depth/2+.25));glazing.Box(glassAt,new Vector3((float)spec.width-1,6,.5f),Color.white,q);contacts.Box(glassAt,new Vector3((float)spec.width-1,6,.5f),Color.white,q);}
                }
                if(m.definition=="hangar")bays.Add(new VehicleCarrierBay{name="Hangar #"+m.id,interior=new Bounds(at,Abs(q*Vector3.right)*50+Abs(q*Vector3.forward)*46+Vector3.up*10),entrance=new Vector2(12,9)});
                foreach(var socket in spec.sockets)if(socket.kind=="Hull"&&!assembly.Occupied(m.id,socket.name)&&!(m.parent!=0&&m.inputSocket==socket.name)){
                    if(m.parent==0&&socket.name=="aft"||m.definition=="cockpit"&&socket.name=="forward")continue;
                    var capPoint=ShipModulePoint(m,socket.pose);var capRotation=Quaternion.Euler(0,m.pose.yaw+socket.pose.yaw,0);float w=m.definition=="hangar"?(socket.name=="engineering"?12:19):(float)spec.width;
                    var capSize=new Vector3(w,7,.35f);visualGeometry.Box(capPoint,capSize,new Color(.25f,.33f,.4f),capRotation);contacts.Box(capPoint,capSize,Color.white,capRotation);
                }
            }
            vehicleBays=bays.ToArray();moduleVisualMesh=visualGeometry.Build("Batched socketed ship sections");moduleContactMesh=contacts.Build("Connected ship walkable contacts");root.AddComponent<MeshFilter>().sharedMesh=moduleVisualMesh;root.AddComponent<MeshRenderer>().sharedMaterial=modularMaterial;root.AddComponent<MeshCollider>().sharedMesh=moduleContactMesh;
            moduleGlassMesh=glazing.Build("Modular cockpit glazing");var glassObject=new GameObject("Cockpit windows");glassObject.transform.SetParent(modularRoot,false);glassObject.AddComponent<MeshFilter>().sharedMesh=moduleGlassMesh;var glassRenderer=glassObject.AddComponent<MeshRenderer>();glassRenderer.sharedMaterial=window;glassRenderer.shadowCastingMode=ShadowCastingMode.Off;
            assemblyRevision=assembly.revision;BuildShipPowerMesh();
        }
        internal static Vector3 PowerPoint(ShipModuleRecord module){var socket=ShipModules.Socket(module,"power");return Local(socket==null?ShipModules.Transform(module.pose,ShipModules.P(0,1.5,0)):ShipModules.Transform(module.pose,socket.pose));}
        void BuildShipPowerMesh(){
            ArmyLifetime.Destroy(shipWireMesh);if(shipWireVisual==null){shipWireVisual=new GameObject("Ship power cable routes");shipWireVisual.transform.SetParent(hull.transform,false);shipWireVisual.AddComponent<MeshFilter>();shipWireVisual.AddComponent<MeshRenderer>().sharedMaterial=modularMaterial;}
            var g=new ModularGeometry();foreach(var wire in assembly.wires){var from=assembly.Module(wire.source);var to=assembly.Module(wire.target);if(from==null||to==null)continue;Vector3 previous=PowerPoint(from);foreach(var point in wire.route){ShipCableGeometry(g,previous,Local(point));previous=Local(point);}ShipCableGeometry(g,previous,PowerPoint(to));}
            shipWireMesh=g.Build("Ship power circuits");shipWireVisual.GetComponent<MeshFilter>().sharedMesh=shipWireMesh;
        }
        internal static void ShipCableGeometry(ModularGeometry geometry,Vector3 a,Vector3 b){
            // Sagged polyline ribbon, no physics objects or fluid simulation per span.
            Vector3 previous=a;for(int i=1;i<=12;i++){float t=i/12f;var point=Vector3.Lerp(a,b,t)-Vector3.up*(Mathf.Sin(t*Mathf.PI)*Mathf.Min(.4f,Vector3.Distance(a,b)*.025f));var direction=(point-previous).normalized;var right=Vector3.Cross(direction,Vector3.up);if(right.sqrMagnitude<.01f)right=Vector3.right;right=right.normalized*.035f;var up=Vector3.Cross(direction,right).normalized*.035f;var color=new Color(1,.3f,.025f,.2f);geometry.Quad(previous-right,point-right,point+right,previous+right,color);geometry.Quad(previous-up,previous+up,point+up,point-up,color);previous=point;}
        }
        internal bool ModuleContains(ShipModuleRecord m,Vector3 local,float padding=0){var d=ShipModules.Definitions[m.definition];if(!d.room)return false;var p=Quaternion.Euler(0,-m.pose.yaw,0)*(local-Local(m.pose));return Mathf.Abs(p.x)<d.width/2+padding&&Mathf.Abs(p.y)<d.height/2+padding&&Mathf.Abs(p.z)<d.depth/2+padding;}
        internal bool ModularInterior(Vector3 p){if(assembly==null)return false;foreach(var m in assembly.modules)if(ModuleContains(m,p,-.3f))return true;return false;}
        internal bool ModularDeck(Double3 point,out VehicleGroundContact contact){contact=default;if(assembly==null)return false;var local=BodyToLocal(point);foreach(var m in assembly.modules){var d=ShipModules.Definitions[m.definition];if(!d.room)continue;var p=Quaternion.Euler(0,-m.pose.yaw,0)*(local-Local(m.pose));float floor=-(float)d.height/2+.5f;if(Mathf.Abs(p.x)>d.width/2-.6||Mathf.Abs(p.z)>d.depth/2-.05||p.y<floor-1.2f)continue;var at=Local(m.pose)+Quaternion.Euler(0,m.pose.yaw,0)*new Vector3(p.x,floor,p.z);contact.position=LocalToBodyPrecise(at);contact.normal=Rotation*Vector3.up;return true;}return false;}
        internal float EnginePower=>assembly==null?1:(float)assembly.PoweredFraction("main-engine");
        internal void CabinBounds(Vector3 eye,out Bounds a,out Bounds b){
            a=WeatherAft;b=WeatherBridge;if(assembly==null||a.Contains(eye)||b.Contains(eye))return;
            foreach(var m in assembly.modules)if(ModuleContains(m,eye)){
                var spec=ShipModules.Definitions[m.definition];var q=Quaternion.Euler(0,m.pose.yaw,0);Vector3 size=Abs(q*Vector3.right)*(float)spec.width+Abs(q*Vector3.forward)*(float)spec.depth+Vector3.up*(float)spec.height;a=new Bounds(Local(m.pose),size);b=a;var parent=assembly.Module(m.parent);if(parent!=null&&ShipModules.Definitions[parent.definition].room){var d=ShipModules.Definitions[parent.definition];var pq=Quaternion.Euler(0,parent.pose.yaw,0);b=new Bounds(Local(parent.pose),Abs(pq*Vector3.right)*(float)d.width+Abs(pq*Vector3.forward)*(float)d.depth+Vector3.up*(float)d.height);}return;
            }
        }
        static Vector3 Abs(Vector3 p)=>new Vector3(Mathf.Abs(p.x),Mathf.Abs(p.y),Mathf.Abs(p.z));
        internal float ShipThrustScale=>assembly==null?1:assembly.PoweredCount("main-engine")*.5f*113000f/(float)Math.Max(1000,assembly.Mass);
        internal float ThrusterPower=>assembly==null?1:assembly.PoweredCount("thruster")*.25f;
        ShipModuleRecord PilotModule=>assembly?.modules.Find(m=>m.definition=="cockpit");
        internal Vector3 PilotLocalEye=>PilotModule==null?PilotEye:ShipModulePoint(PilotModule,ShipModules.P(0,-.5,1));
        internal Quaternion PilotLocalRotation=>PilotModule==null?Quaternion.identity:Quaternion.Euler(0,PilotModule.pose.yaw,0);
        internal Vector3 PilotSeatPoint=>PilotModule==null?new Vector3(0,-2.7f,30):ShipModulePoint(PilotModule,ShipModules.P(0,-1.7,0));
        internal Vector3 PilotExitPoint=>PilotModule==null?PilotExit:ShipModulePoint(PilotModule,ShipModules.P(1.8,-3,-3));
        internal bool CockpitPowered=>assembly==null||PilotModule?.powered==true;
        internal string ShipPowerReadout=>assembly==null?"":$"\nSHIP {assembly.Mass/1000:0.0} t · DRIVE {EnginePower:P0} · RCS {ThrusterPower:P0}";
        void DisposeModularShip(){ArmyLifetime.Destroy(moduleVisualMesh);ArmyLifetime.Destroy(moduleContactMesh);ArmyLifetime.Destroy(moduleGlassMesh);ArmyLifetime.Destroy(shipWireMesh);ArmyLifetime.Destroy(modularMaterial);}
    }
}
