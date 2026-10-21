using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy.Planetary {
    /// <summary>Only the current planet has views. Distant assets have analytic data and no colliders.</summary>
    public sealed class PlanetOrbitalPresentation:IDisposable {
        readonly PlanetDevelopment world;readonly Dictionary<long,OrbitalStationView> views=new Dictionary<long,OrbitalStationView>();
        readonly Material hull,panel,beacon,exhaust;readonly Mesh cube,plume;double nextDiscovery;int revision=-1,lastCreationFrame=-1;
        public PlanetOrbitalPresentation(PlanetDevelopment world){this.world=world;hull=Material("Orbital hull",new Color(.12f,.17f,.21f));panel=Material("Orbital arrays",new Color(.035f,.13f,.3f));beacon=Material("Orbital guidance",new Color(.12f,.9f,.62f));var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.SetActive(false);cube=UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);ArmyLifetime.Destroy(go);exhaust=new Material(Shader.Find("HDRP/Unlit")){name="Launch exhaust"};exhaust.SetColor("_UnlitColor",Color.black);exhaust.SetColor("_EmissiveColor",new Color(8,2,.15f));exhaust.SetFloat("_EmissiveExposureWeight",0);plume=BuildPlume();}
        static Material Material(string name,Color color){var m=new Material(Shader.Find("HDRP/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.3f);return m;}
        public int ViewCount=>views.Count;
        static Mesh BuildPlume(){
            const int sides=8;var vertices=new Vector3[sides*3];var indices=new int[vertices.Length];
            for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;int k=i*3;vertices[k]=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));vertices[k+1]=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));vertices[k+2]=Vector3.down;indices[k]=k;indices[k+1]=k+1;indices[k+2]=k+2;}
            var mesh=new Mesh{name="Shared launch plume"};mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public OrbitalStationView Find(long id)=>views.TryGetValue(id,out var v)?v:null;
        public OrbitalStationView Support(Collider support){if(support==null)return null;foreach(var v in views.Values)if(v.Owns(support))return v;return null;}
        public OrbitalStationView Nearest(Double3 at,out double distance){distance=double.MaxValue;OrbitalStationView best=null;foreach(var v in views.Values){if(!v.Habitable||!v.State.Ready(world.Base.Civilisation.RenderTime))continue;double d=(v.Pose.position-at).Magnitude;if(d<distance){distance=d;best=v;}}return best;}
        public bool NearSegment(Double3 from,Double3 to,double radius){var line=to-from;double length2=line.x*line.x+line.y*line.y+line.z*line.z;foreach(var v in views.Values){if(!v.Habitable)continue;var delta=v.Pose.position-from;double t=length2>0?Math.Clamp((delta.x*line.x+delta.y*line.y+delta.z*line.z)/length2,0,1):0;if((delta-line*t).Magnitude<radius)return true;}return false;}
        public bool Near(Double3 at,double radius){foreach(var v in views.Values)if(v.Habitable&&(v.Pose.position-at).Magnitude<radius)return true;return false;}
        public void Tick(){var sim=world.Base?.Civilisation;if(sim==null)return;
            if(revision!=sim.Orbitals.Revision||Time.unscaledTimeAsDouble>=nextDiscovery){revision=sim.Orbitals.Revision;nextDiscovery=Time.unscaledTimeAsDouble+1;foreach(var a in sim.Orbitals.Assets(sim.BodyId(world.definition.bodyId)))if(!views.ContainsKey(a.id)){nextDiscovery=0;if(lastCreationFrame!=Time.frameCount){lastCreationFrame=Time.frameCount;views.Add(a.id,new OrbitalStationView(world,a,cube,hull,panel,beacon,plume,exhaust));}break;}}
            foreach(var view in views.Values)view.Rebase(sim.RenderTime);
        }
        public int DoorInteraction(Vector3 eye,Vector3 forward,out OrbitalStationView station){foreach(var v in views.Values)if(v.Habitable&&v.DoorVisible(eye,forward)){station=v;return 0;}station=null;return -1;}
        public void DrawHud(){
            var observer=world.ObserverPosition.RelativeTo(world.definition.position);foreach(var v in views.Values){var target=v.Pose.position;var line=target-observer;double length2=line.x*line.x+line.y*line.y+line.z*line.z;if(length2<1)continue;double t=-(observer.x*line.x+observer.y*line.y+observer.z*line.z)/length2;
                if(t>0&&t<1&&(observer+line*t).Magnitude<world.definition.radius*.995)continue;var screen=world.View.WorldToScreenPoint(world.RenderBodyPosition(target));if(screen.z<=0||screen.x<12||screen.x>Screen.width-180||screen.y<90||screen.y>Screen.height-80)continue;
                GUI.color=v.State.faction==0?new Color(.2f,1,.7f):new Color(1,.4f,.3f);GUI.Label(new Rect(screen.x-5,Screen.height-screen.y-8,20,20),"+");GUI.Label(new Rect(screen.x+12,Screen.height-screen.y-12,240,45),v.State.Name+" #"+v.State.id+"\n"+(Math.Sqrt(length2)/1000).ToString("0.0")+" km");
            }GUI.color=Color.white;
        }
        public void Dispose(){foreach(var v in views.Values)v.Dispose();views.Clear();ArmyLifetime.Destroy(cube);ArmyLifetime.Destroy(hull);ArmyLifetime.Destroy(panel);ArmyLifetime.Destroy(beacon);ArmyLifetime.Destroy(exhaust);ArmyLifetime.Destroy(plume);}
    }
    public sealed class OrbitalStationView:IDisposable {
        readonly PlanetDevelopment world;readonly GameObject root;readonly Mesh mesh;readonly List<BoxCollider> colliders=new List<BoxCollider>();readonly GameObject door;readonly Mesh guidanceMesh;readonly Renderer guidance;double poseTime=double.NaN;readonly Renderer renderer;readonly Renderer doorRenderer;readonly BoxCollider leaf;readonly List<Light> lights=new List<Light>();VolumeProfile exposure;
        readonly Mesh plumeMesh;readonly Material exhaustMaterial;Renderer launchExhaust;
        public readonly OrbitalAssetState State;public SpacePose Pose {get;private set;}public bool Habitable=>State.kind!=OrbitalPayloadKind.SurveySatellite;
        public bool LaunchExhaustVisible=>launchExhaust!=null&&launchExhaust.enabled;
        public Quaternion Rotation=>Pose.rotation;public Vector3 DockPosition=>new Vector3(0,0,-5);
        public GameObject Root=>root;
        public OrbitalStationView(PlanetDevelopment world,OrbitalAssetState state,Mesh cube,Material hull,Material panel,Material beacon,Mesh plume,Material exhaust){
            this.world=world;State=state;plumeMesh=plume;exhaustMaterial=exhaust;root=new GameObject(state.Name+" #"+state.id);root.transform.SetParent(world.transform,false);var shapes=new List<CombineInstance>();var lines=new List<CombineInstance>();
            void Box(Vector3 centre,Vector3 size,bool collision=true){shapes.Add(new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(centre,Quaternion.identity,size)});if(collision){var c=root.AddComponent<BoxCollider>();c.center=centre;c.size=size;colliders.Add(c);}}
            if(Habitable){
                void LightAt(Vector3 p){var go=new GameObject("Local station light");go.transform.SetParent(root.transform,false);go.transform.localPosition=p;var light=go.AddComponent<Light>();light.type=LightType.Point;go.AddComponent<HDAdditionalLightData>();light.lightUnit=LightUnit.Lumen;light.intensity=350000;light.range=65;light.color=new Color(.65f,.85f,1);light.shadows=LightShadows.None;light.enabled=false;lights.Add(light);}
                LightAt(new Vector3(0,18,-45));LightAt(new Vector3(41,-7,116));
                var zone=new GameObject("Station eye adaptation");zone.transform.SetParent(root.transform,false);zone.transform.localPosition=new Vector3(0,5,0);var region=zone.AddComponent<BoxCollider>();region.isTrigger=true;colliders.Add(region);region.size=new Vector3(120,50,220);var volume=zone.AddComponent<Volume>();volume.isGlobal=false;volume.priority=190;volume.blendDistance=2;exposure=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=exposure;var eye=exposure.Add<Exposure>();eye.mode.Override(ExposureMode.Fixed);eye.fixedExposure.Override(Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings").exposure-4);
                Box(new Vector3(0,-11,0),new Vector3(124,2,224));Box(new Vector3(-61,10,0),new Vector3(2,42,224));Box(new Vector3(61,10,0),new Vector3(2,42,224));Box(new Vector3(0,31,0),new Vector3(124,2,224));
                // Landing centreline, edge strips and structural ribs are batched into one cheap guidance mesh.
                for(int z=-100;z<85;z+=15)lines.Add(new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(new Vector3(0,-9.96f,z),Quaternion.identity,new Vector3(1,.04f,7))});
                foreach(int side in new[]{-1,1}){lines.Add(new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(new Vector3(side*44,-9.96f,0),Quaternion.identity,new Vector3(.4f,.04f,200))});for(int z=-90;z<90;z+=35)Box(new Vector3(side*58,10,z),new Vector3(3,39,2),false);}
                // Back bulkhead leaves a human-sized airlock on the starboard side.
                Box(new Vector3(-11,10,98),new Vector3(100,42,2));Box(new Vector3(52,10,98),new Vector3(18,42,2));Box(new Vector3(41,12,98),new Vector3(4,38,2));
                Box(new Vector3(41,-11,116),new Vector3(38,2,36));Box(new Vector3(41,-5,116),new Vector3(38,2,36));Box(new Vector3(23,-8,116),new Vector3(2,6,36));Box(new Vector3(59,-8,116),new Vector3(2,6,36));Box(new Vector3(41,-8,134),new Vector3(38,6,2));
                for(int i=0;i<4;i++)Box(new Vector3(-46+i*9,-8,120),new Vector3(6,4,8),false);
                Box(new Vector3(0,40,10),new Vector3(20,16,35),false);Box(new Vector3(-100,28,10),new Vector3(78,1,110),false);Box(new Vector3(100,28,10),new Vector3(78,1,110),false);
                door=new GameObject("F - Crew airlock");door.transform.SetParent(root.transform,false);door.transform.localPosition=new Vector3(41,-8.15f,98);door.transform.localScale=new Vector3(4,3.7f,1.2f);door.AddComponent<MeshFilter>().sharedMesh=cube;doorRenderer=door.AddComponent<MeshRenderer>();doorRenderer.sharedMaterial=beacon;leaf=door.AddComponent<BoxCollider>();
            }else {Box(Vector3.zero,new Vector3(5,4,6),false);Box(new Vector3(-11,0,0),new Vector3(17,.2f,9),false);Box(new Vector3(11,0,0),new Vector3(17,.2f,9),false);}
            if(lines.Count>0){guidanceMesh=new Mesh{name="Hangar guidance"};guidanceMesh.CombineMeshes(lines.ToArray(),true,true);var go=new GameObject("Landing guidance strips");go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=guidanceMesh;guidance=go.AddComponent<MeshRenderer>();guidance.sharedMaterial=beacon;guidance.shadowCastingMode=ShadowCastingMode.Off;guidance.receiveShadows=false;}
            mesh=new Mesh{name=state.Name+" shared silhouette"};mesh.CombineMeshes(shapes.ToArray(),true,true);root.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=State.kind==OrbitalPayloadKind.Warehouse?panel:hull;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            if(doorRenderer!=null){doorRenderer.shadowCastingMode=ShadowCastingMode.Off;doorRenderer.receiveShadows=false;}Rebase(world.Base.Civilisation.RenderTime);
        }
        public Double3 LocalToBody(Vector3 local)=>Pose.position+Double3.Rotate(Rotation,new Double3(local));
        public Vector3 BodyToLocal(Double3 body)=>Double3.Rotate(Quaternion.Inverse(Rotation),body-Pose.position).Float;
        public Vector3 LocalToRender(Vector3 local)=>world.RenderBodyPosition(LocalToBody(local));
        public Vector3 VelocityAt(Vector3 local)=>Pose.linearVelocity.Float+Vector3.Cross(Pose.angularVelocity,Rotation*local);
        public bool Owns(Collider collider)=>collider!=null&&(collider.transform==root.transform||collider.transform.IsChildOf(root.transform));
        public bool NearDeck(Vector3 p)=>Mathf.Abs(p.x)<62&&p.z>-114&&p.z<136&&p.y>-16&&p.y<34;
        public bool Pressurized(Vector3 p)=>!State.airlockOpen&&p.x>24&&p.x<58&&p.z>99&&p.z<133&&p.y>-10.3f&&p.y<-6;
        public bool DoorVisible(Vector3 eye,Vector3 forward){if(!Habitable||!root.activeInHierarchy)return false;var delta=LocalToRender(new Vector3(41,-8,98))-eye;return delta.sqrMagnitude<16&&Vector3.Dot(delta.normalized,forward)>.6f;}
        public bool ToggleDoor(){if(leaf==null)return false;if(State.airlockOpen){var centre=LocalToRender(new Vector3(41,-8.15f,98));if(Physics.CheckBox(centre,new Vector3(1.9f,1.8f,.5f),Rotation,1<<2,QueryTriggerInteraction.Ignore))return false;}State.airlockOpen=!State.airlockOpen;Rebase(world.Base.Civilisation.RenderTime);return true;}
        public void Rebase(double time){if(time!=poseTime){poseTime=time;Pose=State.Pose(time);}var position=world.RenderBodyPosition(Pose.position);if(!root.transform.position.Equals(position)||!root.transform.rotation.Equals(Rotation))root.transform.SetPositionAndRotation(position,Rotation);
            double distance=(position-world.View.transform.position).magnitude;float extent=Habitable?280:40;float pixels=extent*world.View.pixelHeight/(2*Mathf.Tan(world.View.fieldOfView*Mathf.Deg2Rad*.5f)*(float)Math.Max(1,distance));
            bool visible=world.ResidentActive&&pixels>=2;if(renderer.enabled!=visible)renderer.enabled=visible;if(guidance!=null&&guidance.enabled!=visible)guidance.enabled=visible;bool solid=visible&&distance<1000&&State.Ready(time);foreach(var c in colliders)if(c.enabled!=solid)c.enabled=solid;foreach(var light in lights){bool lit=solid&&distance<200;if(light.enabled!=lit)light.enabled=lit;}
            bool launching=visible&&time>=State.epoch&&!State.Ready(time);
            if(launching&&launchExhaust==null){var go=new GameObject("Powered launch exhaust");go.transform.SetParent(root.transform,false);go.transform.localPosition=new Vector3(0,Habitable?-13:-3,0);go.AddComponent<MeshFilter>().sharedMesh=plumeMesh;launchExhaust=go.AddComponent<MeshRenderer>();launchExhaust.sharedMaterial=exhaustMaterial;launchExhaust.shadowCastingMode=ShadowCastingMode.Off;launchExhaust.receiveShadows=false;}
            if(launchExhaust!=null){launchExhaust.enabled=launching;if(launching){float pulse=1+.06f*Mathf.Sin((float)((time-State.epoch)*37));launchExhaust.transform.localScale=Habitable?new Vector3(12,110*pulse,12):new Vector3(2,24*pulse,2);}}
            if(door!=null){door.transform.localPosition=new Vector3(State.airlockOpen?45.5f:41,-8.15f,98);doorRenderer.enabled=visible;leaf.enabled=solid&&!State.airlockOpen;}
        }
        public void Dispose(){ArmyLifetime.Destroy(root);ArmyLifetime.Destroy(mesh);ArmyLifetime.Destroy(guidanceMesh);ArmyLifetime.Destroy(exposure);}
    }
}
