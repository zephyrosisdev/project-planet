using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MassiveArmy.Planetary {
    internal sealed partial class PlanetIndustry {
        float conveyorLift=1;bool cableActive;string cableKind="power",cableReason,cablePrompt;long cableSource;
        readonly List<FramePose> cableAnchors=new List<FramePose>();
        IndustrialMachine cableHover;IndustrialCommand cablePreview;double nextCablePreview,connectionToolReady;
        readonly ModularGeometry cableGeometry=new ModularGeometry(),cablePreviewGeometry=new ModularGeometry(),cablePortGeometry=new ModularGeometry();
        Mesh cableMesh,cablePreviewMesh,cablePortMesh;Double3 cableAnchor;long cableRevision=-1;int cableBody=int.MinValue;
        static readonly Color PowerCableColor=new Color(.95f,.38f,.04f,0),WaterPipeColor=new Color(.05f,.65f,.9f,0);
        void StartCableTool(string kind,long from=0){
            if(Driving){message="Leave the vehicle to run connections";return;}
            CloseMenu();cableAnchor=Body(actor.pose);placementArmed=false;cableActive=true;cableKind=kind;cableSource=from;cableAnchors.Clear();cablePreview=null;cableReason=null;nextCablePreview=0;connectionToolReady=Time.unscaledTimeAsDouble+.15;
            if(kind=="belt"&&surface!=null)world.PersistPlayerBuild();
            message=kind=="belt"?"Conveyor Planner: select an output, click support nodes, finish at an input":kind=="power"?"Select a power output, then a machine input":"Select a pump or reservoir output, then a destination";
        }
        void StopCableTool(){cableActive=false;cablePreview=null;cableHover=null;cableAnchors.Clear();cableSource=0;}
        bool CablePortEligible(IndustrialMachine machine,bool output){
            if(machine.externalVehicle!=0||machine.owner!=actor.id||machine.Definition.Vehicle||machine.pose.body!=actor.pose.body||machine.pose.frame!=actor.pose.frame)return false;
            if(cableKind=="belt")return output||machine.id!=cableSource;
            if(output)return cableKind=="power"?machine.Definition.PowerProvider:(machine.definition=="pump"||machine.definition=="water-reservoir");
            if(machine.id==cableSource)return false;if(cableKind=="water")return machine.definition=="sluice"||machine.definition=="water-reservoir"||machine.recipe!=null&&IndustrialCatalog.Recipes.TryGetValue(machine.recipe,out var recipe)&&recipe.water>0;return !machine.Definition.PowerProvider||machine.definition=="battery";
        }
        FramePose ToolPin(IndustrialMachine machine,bool output)=>cableKind=="belt"?IndustrialConveyorPath.Pin(machine,output):IndustrialCablePath.Pin(machine,cableKind);
        FramePose CableAimPoint(){var ray=new Ray(world.View.transform.position,world.View.transform.forward);Vector3 at;bool hitSurface=Physics.Raycast(ray,out var hit,12,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);if(hitSurface)at=hit.point+hit.normal*(cableKind=="belt"?0:.25f);else at=ray.GetPoint(6);var p=Pose(world.RenderToBody(at));if(cableKind=="belt"){if(!hitSurface&&surface!=null)p=Pose(surface.ProjectToSurface(Body(p)));var up=PlanetPrecisionPolicy.Direction(Body(p));p.x+=up.x*conveyorLift;p.y+=up.y*conveyorLift;p.z+=up.z*conveyorLift;}return p;}
        void FindCablePreview(){
            cableHover=null;cablePreview=null;cableReason=null;var ray=new Ray(world.View.transform.position,world.View.transform.forward);float best=.45f*.45f;
            foreach(var m in Authority.World.machines){if(!CablePortEligible(m,cableSource==0)||actor.pose.Distance(m.pose)>12)continue;var p=world.RenderBodyPosition(Body(ToolPin(m,cableSource==0)));var delta=p-ray.origin;float along=Vector3.Dot(delta,ray.direction);if(along<0||along>12)continue;float distance=(delta-ray.direction*along).sqrMagnitude;if(distance>=best)continue;if(Physics.Linecast(ray.origin,p,out var hit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&Vector3.Distance(hit.point,p)>.2f)continue;best=distance;cableHover=m;}
            var from=Authority.World.Machine(cableSource);
            if(cableSource!=0&&from==null){cableSource=0;cableAnchors.Clear();}
            if(from!=null&&cableHover!=null){cablePreview=Command(cableKind,from.id,cableHover.id);cablePreview.route=cableKind=="belt"?new List<FramePose>(cableAnchors):IndustrialCablePath.WithSocketClearance(from,cableHover,cableKind,cableAnchors);if(cableKind=="belt"){cablePreview.item=filter;cablePreview.definition="spline-conveyor-v1";}cableReason=cableKind=="belt"?Authority.World.ConveyorError(actor.id,cablePreview,out _)??ConveyorObstruction(cablePreview):Authority.World.ConnectionError(actor.id,cablePreview)??CableObstruction(cablePreview);}
            cablePrompt=(cableKind=="belt"?"CONVEYOR PLANNER":cableKind=="power"?"WIRE TOOL":"PIPE TOOL")+" · "+(cableSource==0?"LMB an output socket":cableHover==null?"LMB a destination socket":Nice(cableHover.definition)+" #"+cableHover.id)+"\n";
            if(cableKind=="belt"&&cablePreview!=null){double length=new IndustrialConveyorPath(from,cableHover,cableAnchors).Length;long units=Math.Max(1,(long)Math.Ceiling(length/5));cablePrompt+=$"{length:0.0} / 100 m · {cableReason??"Ready"}\n";cablePrompt+=Authority.World.creativeMode?"Creative · free\n":$"Cost: {units} plates + {units} gears\n";}
            else if(cablePreview!=null)cablePrompt+=$"{IndustrialCablePath.Length(from,cableHover,cableKind,cablePreview.route):0.0} / 50 m · "+(cableReason??(cableKind=="power"&&cableHover.powerSource!=0||cableKind=="water"&&from.waterTarget!=0?"Ready · replaces existing connection":"Ready"))+"\n";
            cablePrompt+=$"LMB route point / final input ({cableAnchors.Count}/{(cableKind=="belt"?16:8)}) · RMB undo · Esc exit";if(cableKind=="belt")cablePrompt+=$"\nWheel support height: {conveyorLift:0.0} m · green output / blue input";
        }
        string ConnectionBlocker(RaycastHit hit){if(hit.collider==contact){var p=world.RenderToBody(hit.point);foreach(var machine in Authority.World.machines){if(machine.pose.body!=actor.pose.body||machine.integratedLogistics)continue;var model=ColonyPrototypeLibrary.Find("machine-"+machine.definition);if(model==null)continue;var local=Quaternion.Inverse(Rotation(machine.pose))*(p-Body(machine.pose)).Float;var bounds=model.Collision().bounds;bounds.Expand(.2f);if(bounds.Contains(local))return Nice(machine.definition)+" #"+machine.id;}return "industrial machinery";}return hit.collider.name;}
        string CableObstruction(IndustrialCommand command){
            if(host==null)return null; // Data-only UI fixture has no world contacts.
            var from=Authority.World.Machine(command.source);var to=Authority.World.Machine(command.target);if(from==null||to==null)return "Missing endpoint";
            for(int span=0;span<=(command.route?.Count??0);span++){
                var a=IndustrialCablePath.Anchor(from,to,command.kind,command.route,span);var b=IndustrialCablePath.Anchor(from,to,command.kind,command.route,span+1);var previous=a;
                for(int n=1;n<=IndustrialCablePath.SamplesPerSpan;n++){
                    var p=IndustrialCablePath.Point(a,b,n/(double)IndustrialCablePath.SamplesPerSpan,command.kind);string discharge=Authority.World.FrontDischargeObstruction(p,command.kind=="power"?.025:.07);if(discharge!=null)return discharge;var origin=world.RenderBodyPosition(Body(previous));var delta=world.RenderBodyPosition(Body(p))-origin;
                    if(delta.sqrMagnitude>.00001f&&Physics.SphereCast(origin,command.kind=="power"?.025f:.07f,delta.normalized,out var hit,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))return "Route blocked by "+ConnectionBlocker(hit)+" · add routing points";
                    if(surface!=null&&Body(p).Magnitude<world.definition.radius+surface.SampleHeight(Body(p))+.08)return "Route intersects terrain · add a higher routing point";
                    previous=p;
                }
            }
            return null;
        }
        void AddConnectionControlPoint(FramePose point){if(cableSource==0)return;if(cableAnchors.Count>=(cableKind=="belt"?IndustrialConveyorPath.MaximumAnchors:IndustrialCablePath.MaximumUserAnchors)){message="Route point budget reached; undo a point or finish this run";return;}if(cableAnchors.Count>0&&cableAnchors[cableAnchors.Count-1].Distance(point)<.25){message="Move the next point at least 25 cm";return;}cableAnchors.Add(point);nextCablePreview=0;}
        void UndoConnectionControlPoint(){if(cableAnchors.Count>0)cableAnchors.RemoveAt(cableAnchors.Count-1);else cableSource=0;nextCablePreview=0;}
        void UpdateCableTool(){
            if(!cableActive)return;if(!world.IsFirstPerson||Driving){StopCableTool();return;}
            if(world.Paused||MenuOpen||world.PersonalMenuOpen||!Application.isFocused)return;
            var keys=Keyboard.current;var mouse=Mouse.current;if(cableKind=="belt"&&mouse!=null&&Mathf.Abs(mouse.scroll.ReadValue().y)>.01f){conveyorLift=Mathf.Clamp(conveyorLift+Mathf.Sign(mouse.scroll.ReadValue().y)*.25f,.5f,8);nextCablePreview=0;}if(keys?.escapeKey.wasPressedThisFrame==true){StopCableTool();return;}
            if(mouse?.rightButton.wasPressedThisFrame==true)UndoConnectionControlPoint();
            if(Time.unscaledTimeAsDouble>=nextCablePreview){nextCablePreview=Time.unscaledTimeAsDouble+.1;FindCablePreview();BuildCablePreviewMesh();}
            if(Time.unscaledTimeAsDouble<connectionToolReady||mouse?.leftButton.wasPressedThisFrame!=true)return;FindCablePreview();
            if(cableSource!=0&&(keys?.shiftKey.isPressed==true||cableHover==null)){AddConnectionControlPoint(CableAimPoint());return;}
            FindCablePreview();if(cableSource==0){if(cableHover!=null){cableSource=cableHover.id;cableAnchors.Clear();}else message="Aim at a visible output socket";}
            else if(cablePreview!=null){if(cableReason!=null)message=cableReason;else{long sequence=actor.lastSequence;Execute(cablePreview);if(actor.lastSequence>sequence)cableAnchors.Clear();}}else message="Aim at a visible destination socket";
            nextCablePreview=0;
        }
        internal static void AddCableSpan(ModularGeometry geometry,FramePose a,FramePose b,string kind,Double3 origin,Color color){
            float radius=kind=="power"?.025f:.07f;var previous=IndustrialCablePath.Point(a,b,0,kind);for(int n=1;n<=IndustrialCablePath.SamplesPerSpan;n++){
                var p=IndustrialCablePath.Point(a,b,n/(double)IndustrialCablePath.SamplesPerSpan,kind);var start=(new Double3(previous.x,previous.y,previous.z)-origin).Float;var end=(new Double3(p.x,p.y,p.z)-origin).Float;var tangent=(end-start).normalized;if(tangent.sqrMagnitude<.1f){previous=p;continue;}var up=PlanetPrecisionPolicy.Direction(new Double3(p.x,p.y,p.z));var side=Vector3.Cross(tangent,up).normalized;if(side.sqrMagnitude<.1f)side=Vector3.Cross(tangent,Mathf.Abs(tangent.y)<.9f?Vector3.up:Vector3.right).normalized;var normal=Vector3.Cross(tangent,side).normalized;
                for(int face=0;face<4;face++){float angle=face*Mathf.PI*.5f,next=(face+1)*Mathf.PI*.5f;var offset=(side*Mathf.Cos(angle)+normal*Mathf.Sin(angle))*radius;var other=(side*Mathf.Cos(next)+normal*Mathf.Sin(next))*radius;geometry.Quad(start+offset,end+offset,end+other,start+other,color);}previous=p;
            }
        }
        static void AddCable(ModularGeometry geometry,IndustrialMachine from,IndustrialMachine to,string kind,List<FramePose> route,Double3 origin,Color color){foreach(var machine in new[]{from,to}){var pin=IndustrialCablePath.Pin(machine,kind);var q=Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(new Double3(pin.x,pin.y,pin.z)));float height=(float)(IndustrialCablePath.Height(machine.definition)+(kind=="power"?.5:.22)-.2);geometry.Box((new Double3(pin.x,pin.y,pin.z)-origin).Float-q*Vector3.up*height*.5f,new Vector3(.08f,height,.08f),color,q);}for(int span=0;span<=(route?.Count??0);span++)AddCableSpan(geometry,IndustrialCablePath.Anchor(from,to,kind,route,span),IndustrialCablePath.Anchor(from,to,kind,route,span+1),kind,origin,color);}
        string PendingWireObstruction(IndustrialMachine from){
            var a=IndustrialCablePath.Pin(from,cableKind);var nodes=new List<FramePose>{IndustrialCablePath.SocketExit(from,cableKind)};nodes.AddRange(cableAnchors);nodes.Add(CableAimPoint());foreach(var b in nodes){var previous=a;for(int n=1;n<=IndustrialCablePath.SamplesPerSpan;n++){var p=IndustrialCablePath.Point(a,b,n/(double)IndustrialCablePath.SamplesPerSpan,cableKind);string discharge=Authority.World.FrontDischargeObstruction(p,cableKind=="power"?.025:.07);if(discharge!=null)return discharge;var delta=world.RenderBodyPosition(Body(p))-world.RenderBodyPosition(Body(previous));if(host!=null&&delta.sqrMagnitude>.000001f&&Physics.SphereCast(world.RenderBodyPosition(Body(previous)),cableKind=="power"?.025f:.07f,delta.normalized,out var hit,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))return "Route blocked by "+ConnectionBlocker(hit)+"; click another routing point";if(surface!=null&&Body(p).Magnitude<world.definition.radius+surface.SampleHeight(Body(p))+.08)return "Route intersects terrain; add a higher routing point";previous=p;}a=b;}return null;
        }
        void BuildCablePreviewMesh(){
            cablePreviewGeometry.Clear();if(cableKind=="belt"){BuildConveyorToolGeometry();return;}if(cableSource!=0&&Authority.World.Machine(cableSource) is IndustrialMachine from){if(cableHover==null)cableReason=PendingWireObstruction(from);if(cableReason!=null)cablePrompt+="\n"+cableReason;var color=cableReason==null?new Color(.1f,.95f,.4f,1):new Color(1,.1f,.05f,1);if(cableHover!=null)AddCable(cablePreviewGeometry,from,cableHover,cableKind,cablePreview?.route??IndustrialCablePath.WithSocketClearance(from,cableHover,cableKind,cableAnchors),cableAnchor,color);else{var a=IndustrialCablePath.Pin(from,cableKind);var exit=IndustrialCablePath.SocketExit(from,cableKind);AddCableSpan(cablePreviewGeometry,a,exit,cableKind,cableAnchor,color);a=exit;foreach(var p in cableAnchors){AddCableSpan(cablePreviewGeometry,a,p,cableKind,cableAnchor,color);a=p;}AddCableSpan(cablePreviewGeometry,a,CableAimPoint(),cableKind,cableAnchor,color);}}
            cablePreviewMesh??=new Mesh{name="Cable tool preview"};cablePreviewGeometry.BuildInto(cablePreviewMesh);
            cablePortGeometry.Clear();foreach(var m in Authority.World.machines){if(!CablePortEligible(m,cableSource==0)||actor.pose.Distance(m.pose)>20)continue;var p=ToolPin(m,cableSource==0);var color=m==cableHover?new Color(.15f,1,.4f,1):cableKind=="power"?new Color(.95f,.38f,.04f,1):new Color(.05f,.65f,.9f,1);var position=(Body(p)-cableAnchor).Float;var rotation=Rotation(m.pose);cablePortGeometry.Box(position,Vector3.one*.28f,color,rotation);cablePortGeometry.Box(position-rotation*Vector3.up*.2f,new Vector3(.06f,.4f,.06f),color,rotation);}
            cablePortMesh??=new Mesh{name="Connection socket markers"};cablePortGeometry.BuildInto(cablePortMesh);
        }
        void RenderCables(){
            if(world.Paused)return;
            var observer=Body(actor.pose);if(cableMesh==null||cableRevision!=Authority.World.connectionRevision||cableBody!=actor.pose.body||(observer-cableAnchor).Magnitude>20){
                cableAnchor=observer;cableBody=actor.pose.body;cableRevision=Authority.World.connectionRevision;cableGeometry.Clear();
                foreach(var m in Authority.World.machines){if(m.pose.body!=actor.pose.body||m.pose.frame!=actor.pose.frame||m.pose.Distance(actor.pose)>225)continue;var sourceMachine=Authority.World.Machine(m.powerSource);if(sourceMachine!=null)AddCable(cableGeometry,sourceMachine,m,"power",m.powerRoute,cableAnchor,PowerCableColor);var destination=Authority.World.Machine(m.waterTarget);if(destination!=null)AddCable(cableGeometry,m,destination,"water",m.waterRoute,cableAnchor,WaterPipeColor);}
                cableMesh??=new Mesh{name="Nearby industrial connections"};cableGeometry.BuildInto(cableMesh);nextCablePreview=0;
            }
            var matrix=Matrix4x4.Translate(world.RenderBodyPosition(cableAnchor));var bounds=new Bounds(world.RenderBodyPosition(cableAnchor),Vector3.one*600);if(cableMesh.vertexCount>0)Draw(cableMesh,prototypeMaterial,matrix,bounds);
            if(cableActive&&!MenuOpen&&!world.PersonalMenuOpen){if(cablePreviewMesh!=null&&cablePreviewMesh.vertexCount>0)Draw(cablePreviewMesh,prototypeMaterial,matrix,bounds);if(cablePortMesh!=null&&cablePortMesh.vertexCount>0)Draw(cablePortMesh,prototypeMaterial,matrix,bounds);}
        }
        void DisposeCables(){ArmyLifetime.Destroy(cableMesh);ArmyLifetime.Destroy(cablePreviewMesh);ArmyLifetime.Destroy(cablePortMesh);}
    }
}

