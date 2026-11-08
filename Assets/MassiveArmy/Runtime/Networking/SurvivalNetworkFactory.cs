using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        sealed class FactoryPublication {public SurvivalFactoryLink link;public FramePose centre;public double extent;public string data;}
        sealed class FactorySolid {public ColonyPrototypeSolid[] solids;public double bound;}
        readonly SurvivalFactoryConnections.ContactIndex factoryContacts=new SurvivalFactoryConnections.ContactIndex();
        readonly Dictionary<string,FactorySolid> factorySolids=new Dictionary<string,FactorySolid>();
        readonly Dictionary<long,FactoryPublication> factoryPublished=new Dictionary<long,FactoryPublication>();long factoryRevision=long.MinValue;
        readonly Dictionary<long,SurvivalFactoryLink> clientFactoryLinks=new Dictionary<long,SurvivalFactoryLink>();
        readonly Dictionary<long,SurvivalInterestDelivery.Fingerprint> factoryEndpointPoses=new Dictionary<long,SurvivalInterestDelivery.Fingerprint>();
        sealed class FactoryBatch {internal GameObject view;internal Mesh mesh;internal Double3 anchor;}
        readonly Dictionary<(int body,long frame),FactoryBatch> factoryBatches=new Dictionary<(int,long),FactoryBatch>();
        static void PrepareMachinePublication(SurvivalEntity entity,IndustrialMachine machine){
            if(entity.privateData!=null)return;
            entity.privateData=JsonUtility.ToJson(machine);var copy=JsonUtility.FromJson<IndustrialMachine>(entity.privateData);
            copy.input=new Inventory();copy.output=new Inventory();copy.reserved.Clear();copy.frontTailings=null;copy.constructionPaid.Clear();entity.data=JsonUtility.ToJson(copy);
        }
        bool factoryMeshDirty;GameObject factoryView;Mesh factoryMesh;Material factoryMaterial;Double3 factoryAnchor;
        long factorySource;string factoryKind="belt";float factoryLift=1.5f;readonly List<FramePose> factoryAnchors=new List<FramePose>();
        internal bool FactoryVisualsMatch(int links){if(clientFactoryLinks.Count!=links)return false;if(links==0)return factoryBatches.Count==0;foreach(var batch in factoryBatches.Values)if(batch.view.activeSelf&&batch.mesh.vertexCount>0)return true;return false;}
        string ValidateFactoryConnection(IndustrialActor actor,IndustrialCommand command){
            if(!SurvivalFactoryConnections.Creates(command.kind))return null;
            if(!surfaces.ContainsKey(actor.pose.body))return "Region unavailable";
            return SurvivalFactoryConnections.Prepare(authority.World,actor.id,command,(point,radius)=>FactoryObstruction(command,point,radius),factoryContacts);
        }
        FactorySolid FactoryCollisionData(IndustrialMachine machine){
            if(factorySolids.TryGetValue(machine.definition,out var cached))return cached;
            // Read stored collision prisms only. Generated fallback render meshes are never requested.
            var solids=ColonyPrototypeLibrary.StoredCollision("machine-"+machine.definition);
            if(solids==null||solids.Length==0){float x=(float)machine.Definition.Width*.5f,z=(float)machine.Definition.Depth*.5f;solids=new[]{new ColonyPrototypeSolid{bottom=0,top=(float)IndustrialCablePath.Height(machine.definition),polygon=new[]{new Vector2(-x,-z),new Vector2(x,-z),new Vector2(x,z),new Vector2(-x,z)}}};}
            double bound=0;foreach(var solid in solids)foreach(var p in solid.polygon)bound=Math.Max(bound,Math.Sqrt(p.x*p.x+p.y*p.y+Math.Max(solid.bottom*solid.bottom,solid.top*solid.top)));
            cached=new FactorySolid{solids=solids,bound=bound};factorySolids.Add(machine.definition,cached);return cached;
        }
        static bool InFactoryPrism(Vector3 point,ColonyPrototypeSolid solid,double clearance){
            if(point.y<solid.bottom-clearance||point.y>solid.top+clearance)return false;bool inside=false;var polygon=solid.polygon;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++){
                var a=polygon[j];var b=polygon[i];double dx=b.x-a.x,dz=b.y-a.y,length=dx*dx+dz*dz,t=length<1e-12?0:Math.Max(0,Math.Min(1,((point.x-a.x)*dx+(point.z-a.y)*dz)/length));double x=point.x-a.x-dx*t,z=point.z-a.y-dz*t;
                if(x*x+z*z<=clearance*clearance)return true;
                if((a.y>point.z)!=(b.y>point.z)&&point.x<(b.x-a.x)*(point.z-a.y)/(b.y-a.y)+a.x)inside=!inside;
            }return inside;
        }
        string FactoryObstruction(IndustrialCommand command,FramePose point,double clearance){
            if(point.frame!=0)return ShipFactoryObstruction(command,point,clearance);
            if(!surfaces.TryGetValue(point.body,out var surface)||point.frame!=0)return "Unsupported connection frame";
            var position=SurvivalVehicleSimulation.Position(point);
            if(position.Magnitude<surface.Definition.radius+surface.SampleHeight(position)+clearance+.05)return "Route intersects terrain; raise its routing points";
            if(BuildingAuthority.Overlaps(point.body,position,Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(position)),new[]{BuildSolid.Box((float)-clearance,(float)clearance,(float)-clearance,(float)clearance,(float)-clearance,(float)clearance)},clearance))return "Route intersects a building; add routing points";
            foreach(var ship in authority.World.multiplayer.ships)if(ship.pose.body==point.body&&(position-SurvivalVehicleSimulation.Position(ship.pose)).Magnitude<ship.collisionRadius+clearance)return "Keep the route clear of ship hulls";
            foreach(var machine in authority.World.machines){
                if(machine.pose.body!=point.body||machine.pose.frame!=0)continue;
                if(command.kind=="belt"&&(machine.id==command.source&&point.Distance(IndustrialConveyorPath.Pin(machine,true))<1.1||machine.id==command.target&&point.Distance(IndustrialConveyorPath.Pin(machine,false))<1.1))continue;
                var model=FactoryCollisionData(machine);var delta=position-SurvivalVehicleSimulation.Position(machine.pose);if(delta.Magnitude>model.bound+clearance)continue;
                var up=PlanetPrecisionPolicy.Direction(SurvivalVehicleSimulation.Position(machine.pose));var q=Quaternion.FromToRotation(Vector3.up,up)*Quaternion.Euler(0,machine.pose.yaw,0);var local=Quaternion.Inverse(q)*delta.Float;
                foreach(var solid in model.solids)if(InFactoryPrism(local,solid,clearance))return "Route intersects machine #"+machine.id+"; add routing points";
            }return null;
        }
        void PublishFactoryLinks(){
            if(factoryRevision!=authority.World.connectionRevision){
                factoryPublished.Clear();factoryRevision=authority.World.connectionRevision;
                foreach(var link in SurvivalFactoryConnections.Links(authority.World)){
                    var from=authority.World.Machine(link.source);var to=authority.World.Machine(link.target);if(from==null||to==null||from.pose.frame!=to.pose.frame||from.pose.body!=to.pose.body)continue;
                    var points=SurvivalFactoryConnections.Points(authority.World,link);if(points.Count<2)continue;var centre=points[points.Count/2];double extent=0;foreach(var p in points)extent=Math.Max(extent,centre.Distance(p));
                    factoryPublished.Add(SurvivalFactoryConnections.WireId(link.entity,link.kind),new FactoryPublication{link=link,centre=centre,extent=extent,data=JsonUtility.ToJson(link)});
                }
            }
            foreach(var pair in factoryPublished){var local=pair.Value.centre;var canonical=ResolveNetworkInventoryPose(local);if(!canonical.HasValue)continue;AddEntity(pair.Key,"factory-link",canonical.Value,pair.Value.data,pair.Value.extent);if(local.frame!=0){entities[pair.Key].fingerprintPose=local;entities[pair.Key].requiredParent=local.frame*8+5;}}
        }
        void AddFactoryEndpoints(HashSet<long> nearby){foreach(long id in new List<long>(nearby))if(factoryPublished.TryGetValue(id,out var publication)){long from=publication.link.source*8+2,to=publication.link.target*8+2;if(entities.ContainsKey(from))nearby.Add(from);if(entities.ContainsKey(to))nearby.Add(to);}}
        void ReceiveFactoryLinks(SurvivalSnapshot snapshot){
            foreach(long id in snapshot.removed){if(clientFactoryLinks.Remove(id))factoryMeshDirty=true;if(id%8==2&&factoryEndpointPoses.Remove(id/8))factoryMeshDirty=true;}
            foreach(var entity in snapshot.changed)if(entity.kind=="machine"&&clientMachines.TryGetValue(entity.id/8,out var machine)){
                var stamp=new SurvivalInterestDelivery.Fingerprint{data=machine.definition,pose=machine.pose};if(!factoryEndpointPoses.TryGetValue(machine.id,out var prior)||!prior.Same(stamp)){factoryEndpointPoses[machine.id]=stamp;if(clientFactoryLinks.Count>0)factoryMeshDirty=true;}
            }
            foreach(var entity in snapshot.changed)if(entity.kind=="factory-link"){clientFactoryLinks[entity.id]=JsonUtility.FromJson<SurvivalFactoryLink>(entity.data);factoryMeshDirty=true;}
        }
        void FactoryRouteGUI(){
            if(factorySource==0)return;GUILayout.Label("ROUTE "+factoryKind+" from #"+factorySource+" · "+factoryAnchors.Count+" points");
            GUILayout.Label("Walk to the destination; finish within 12 m. Optional supports use your current position.");
            factoryLift=GUILayout.HorizontalSlider(factoryLift,.5f,8);GUILayout.BeginHorizontal();
            if(GUILayout.Button("Add point at feet +"+factoryLift.ToString("0.0")+" m")){
                int maximum=factoryKind=="belt"?16:8;if(factoryAnchors.Count>=maximum)Status="Routing point limit reached";else{var pose=predicted?.interior==true?predicted.pose:latest.self.pose;var p=SurvivalVehicleSimulation.Position(pose);p+=new Double3(pose.frame!=0?Vector3.up:PlanetPrecisionPolicy.Direction(p))*factoryLift;pose.x=p.x;pose.y=p.y;pose.z=p.z;factoryAnchors.Add(pose);}
            }
            if(GUILayout.Button("UNDO")&&factoryAnchors.Count>0)factoryAnchors.RemoveAt(factoryAnchors.Count-1);
            if(GUILayout.Button("CANCEL")){factorySource=0;factoryAnchors.Clear();}GUILayout.EndHorizontal();
        }
        void FactoryMachineControlsGUI(IndustrialMachine machine){
            if(machine.Definition.Vehicle||machine.integratedLogistics)return;
            GUILayout.BeginHorizontal();
            void Source(string kind){factorySource=machine.id;factoryKind=kind;factoryAnchors.Clear();}
            if(machine.Definition.PowerProvider&&GUILayout.Button("POWER OUTPUT"))Source("power");
            if((machine.definition=="pump"||machine.definition=="water-reservoir")&&GUILayout.Button("WATER OUTPUT"))Source("water");
            if(GUILayout.Button("BELT OUTPUT"))Source("belt");GUILayout.EndHorizontal();
            if(factorySource!=0&&factorySource!=machine.id&&GUILayout.Button("Connect "+factoryKind+" from #"+factorySource+" to #"+machine.id)){
                Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind=factoryKind,source=factorySource,target=machine.id,route=new List<FramePose>(factoryAnchors)}});factorySource=0;factoryAnchors.Clear();
            }
            GUILayout.BeginHorizontal();
            if(machine.powerSource!=0&&GUILayout.Button("Remove power"))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="remove-power",target=machine.id}});
            if(machine.waterTarget!=0&&GUILayout.Button("Remove water"))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="remove-water",target=machine.id}});GUILayout.EndHorizontal();
            foreach(var link in clientFactoryLinks.Values)if(link.kind=="belt"&&(link.source==machine.id||link.target==machine.id)&&GUILayout.Button("Recover conveyor #"+link.entity))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="remove-belt",target=link.entity}});
        }
        void RenderFactoryLinks(){
            if(factoryMeshDirty){
                factoryMeshDirty=false;var grouped=new Dictionary<(int body,long frame),List<SurvivalFactoryLink>>();
                foreach(var link in clientFactoryLinks.Values)if(clientMachines.TryGetValue(link.source,out var machine)&&machine.pose.body==clientWorld.definition.bodyId){var key=(machine.pose.body,machine.pose.frame);if(!grouped.TryGetValue(key,out var links)){links=new List<SurvivalFactoryLink>();grouped.Add(key,links);}links.Add(link);}
                foreach(var batch in factoryBatches.Values)batch.view.SetActive(false);
                factoryView=null;factoryMesh=null;
                foreach(var group in grouped){
                    var geometry=new ModularGeometry();bool anchored=false;Double3 anchor=default;
                    foreach(var link in group.Value){
                        if(!clientMachines.TryGetValue(link.source,out var from)||!clientMachines.TryGetValue(link.target,out var to))continue;
                        var points=SurvivalFactoryConnections.Points(from,to,link);if(points.Count<2)continue;if(!anchored){anchor=SurvivalVehicleSimulation.Position(points[0]);anchored=true;}
                        var color=link.kind=="power"?new Color(1,.65f,.1f):link.kind=="water"?Color.cyan:Color.gray;float width=link.kind=="belt"?.8f:link.kind=="water"?.14f:.05f;var previous=SurvivalVehicleSimulation.Position(points[0]);
                        for(int i=1;i<points.Count;i++){var next=SurvivalVehicleSimulation.Position(points[i]);if((next-previous).Magnitude<1&&i<points.Count-1)continue;var direction=(next-previous).Float.normalized;var up=group.Key.frame!=0?Vector3.up:PlanetPrecisionPolicy.Direction((next+previous)*.5);var side=Vector3.Cross(up,direction).normalized*width*.5f;var a=(previous-anchor).Float;var b=(next-anchor).Float;geometry.Quad(a-side,b-side,b+side,a+side,color);if(link.kind!="belt")geometry.Quad(a-up*width*.5f,b-up*width*.5f,b+up*width*.5f,a+up*width*.5f,color);previous=next;}
                    }
                    if(!factoryBatches.TryGetValue(group.Key,out var batch)){batch=new FactoryBatch{view=new GameObject("Network factory links"),mesh=new Mesh{name="Batched network factory links"}};batch.view.transform.SetParent(clientWorld.transform);batch.view.AddComponent<MeshFilter>().sharedMesh=batch.mesh;factoryMaterial??=ColonyPrototypeLibrary.Material("Network factory links");batch.view.AddComponent<MeshRenderer>().sharedMaterial=factoryMaterial;batch.view.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;factoryBatches.Add(group.Key,batch);}
                    geometry.BuildInto(batch.mesh);batch.anchor=anchor;batch.view.SetActive(anchored);factoryView??=batch.view;factoryMesh??=batch.mesh;factoryAnchor=anchor;
                }
                foreach(var key in new List<(int body,long frame)>(factoryBatches.Keys))if(!grouped.ContainsKey(key)){var batch=factoryBatches[key];Destroy(batch.view);Destroy(batch.mesh);factoryBatches.Remove(key);}
            }
            foreach(var pair in factoryBatches){var batch=pair.Value;if(pair.Key.body!=clientWorld.definition.bodyId){batch.view.SetActive(false);continue;}if(pair.Key.frame==0)batch.view.transform.SetPositionAndRotation(clientWorld.RenderBodyPosition(batch.anchor),Quaternion.identity);else if(presentedShips.TryGetValue(pair.Key.frame,out var parent))batch.view.transform.SetPositionAndRotation(clientWorld.RenderBodyPosition(parent.point+Double3.Rotate(parent.rotation,batch.anchor)),parent.rotation);else batch.view.SetActive(false);}
        }
        void ClearFactoryViews(){
            foreach(var batch in factoryBatches.Values){Destroy(batch.view);Destroy(batch.mesh);}factoryBatches.Clear();if(factoryMaterial!=null)Destroy(factoryMaterial);factoryView=null;factoryMesh=null;factoryMaterial=null;
            clientFactoryLinks.Clear();factoryEndpointPoses.Clear();factoryPublished.Clear();factorySolids.Clear();factoryRevision=long.MinValue;factoryMeshDirty=false;factorySource=0;factoryAnchors.Clear();
        }
    }
}
