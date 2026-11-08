using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        string placingMachine;int placementTurn;double nextPlacementProbe;FramePose? placementPose;GameObject placementGhost;
        string ValidatePlacement(IndustrialActor actor,IndustrialCommand command){
            if(command.kind=="pack"&&authority.World.multiplayer.vehicles.Exists(v=>v.entity==command.target&&v.driver!=0))return "Leave the driver seat before packing this vehicle";
            if(command.kind!="place"&&command.kind!="pile")return null;
            var pose=command.pose;if(pose.frame!=0)return ValidateShipMachine(actor,command);if(pose.body!=actor.pose.body||pose.frame!=0||!SurvivalServerConfig.Finite(pose.x)||!SurvivalServerConfig.Finite(pose.y)||!SurvivalServerConfig.Finite(pose.z)||!SurvivalServerConfig.Finite(pose.yaw))return "Choose supported ground in the current region";
            if(command.attachmentModule!=0)return ValidateMountedPlacement(actor,command);
            if(actor.pose.Distance(pose)>12)return "Aim within construction reach";
            var surface=surfaces[pose.body];var point=SurvivalVehicleSimulation.Position(pose);var ground=surface.ProjectToSurface(point,.05);var up=PlanetPrecisionPolicy.Direction(ground);
            if((ground-point).Magnitude>.5||surface.SampleHeight(up)<surface.Definition.oceanHeight+.2||Vector3.Dot(surface.SampleNormal(up),up)<.8f)return "Choose dry supported ground";
            double width=16,depth=16;if(command.kind=="place"){
                if(!IndustrialCatalog.Machines.TryGetValue(command.definition??"",out var definition))return "Unknown machine";width=definition.Width;depth=definition.Depth;
            }
            var rotation=Quaternion.FromToRotation(Vector3.up,up)*Quaternion.Euler(0,(float)Math.Round(pose.yaw/90)*90,0);
            for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2){var foot=ground+Double3.Rotate(rotation,new Double3(x*width*.5,0,z*depth*.5));if(Math.Abs(surface.ProjectToSurface(foot).Magnitude-ground.Magnitude)>1.5)return "Level the construction footprint first";}
            foreach(var ship in authority.World.multiplayer.ships)if(ship.pose.Distance(pose)<ship.collisionRadius+Math.Sqrt(width*width+depth*depth)*.5)return "Keep the ship hull and boarding area clear";
            if(BuildingAuthority.Overlaps(pose.body,ground,rotation,new[]{BuildSolid.Box((float)-width*.5f,(float)width*.5f,(float)-depth*.5f,(float)depth*.5f,.02f,command.kind=="place"?(float)IndustrialCablePath.Height(command.definition):2)},Math.Sqrt(width*width+depth*depth)+8))return "Keep ground placement clear of buildings";
            command.pose=new FramePose{body=pose.body,x=ground.x,y=ground.y,z=ground.z,yaw=(float)Math.Round(pose.yaw/90)*90};return null;
        }
        string ValidateMountedPlacement(IndustrialActor actor,IndustrialCommand command){
            if(command.kind!="place"||!IndustrialCatalog.Machines.TryGetValue(command.definition??"",out var definition))return "Choose a mountable machine";
            var mount=authority.World.mounts.Find(m=>m.owner==actor.id&&m.site==command.attachmentSite&&m.module==command.attachmentModule&&m.socket==command.attachmentSocket);if(mount==null||mount.pose.frame!=0||actor.pose.Distance(mount.pose)>12)return "Owned construction mount unavailable or out of reach";
            int turn=(int)Math.Round(Mathf.DeltaAngle(mount.pose.yaw,command.pose.yaw)/90);var pose=mount.pose;pose.yaw=Mathf.Repeat(mount.pose.yaw+turn*90,360);command.pose=pose;
            var centre=SurvivalVehicleSimulation.Position(pose);var rotation=Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(centre))*Quaternion.Euler(0,pose.yaw,0);var width=(float)definition.Width;var depth=(float)definition.Depth;
            if(BuildingAuthority.Overlaps(pose.body,centre,rotation,new[]{BuildSolid.Box(-width*.5f,width*.5f,-depth*.5f,depth*.5f,.02f,(float)IndustrialCablePath.Height(definition.id))},Math.Sqrt(width*width+depth*depth)+8,mount.module))return "Machine intersects another building section";
            foreach(var ship in authority.World.multiplayer.ships)if(ship.pose.Distance(pose)<ship.collisionRadius+Math.Sqrt(width*width+depth*depth)*.5)return "Keep the ship hull clear";
            return null;
        }
        void BeginMachinePlacement(ItemStack stack,string definition){
            if(stack.slot!=0)Submit(new SurvivalAction{kind="inventory-move",from=new InventoryAddress{slot=stack.slot},to=new InventoryAddress{slot=0},count=stack.count});
            Submit(new SurvivalAction{kind="slot",slot=0});placingMachine=definition;placementTurn=0;placementPose=null;inventoryOpen=industryOpen=false;UpdateNetworkCursor();nextPlacementProbe=0;
        }
        void PlacementInput(){
            if(placingMachine==null||ChatInputCaptured||ClientMenuOpen||!Application.isFocused)return;var keys=Keyboard.current;var mouse=Mouse.current;
            if(keys?.rKey.wasPressedThisFrame==true){placementTurn=(placementTurn+1)%4;nextPlacementProbe=0;}
            if(mouse?.rightButton.wasPressedThisFrame==true){placingMachine=null;placementPose=null;return;}
            if(Time.realtimeSinceStartupAsDouble>=nextPlacementProbe||mouse?.leftButton.wasPressedThisFrame==true){
                nextPlacementProbe=Time.realtimeSinceStartupAsDouble+.1;
                if(predicted?.interior==true&&predicted.ship!=null){
                    placementPose=null;
                    if(IndustrialWorld.RespawnItem(placingMachine)||IndustrialCatalog.Machines.ContainsKey(placingMachine)){
                        var ship=predicted.ship;var camera=clientWorld.RenderToBody(clientWorld.View.transform.position);var local=SurvivalShipInteriorModel.Local(ship,new FramePose{body=ship.pose.body,x=camera.x,y=camera.y,z=camera.z});
                        var direction=Quaternion.Inverse(ShipRotation(ship))*clientWorld.View.transform.forward;
                        if(direction.y<-.01f){double distance=(predicted.pose.y-local.y)/direction.y;if(distance>0&&distance<=12){var pose=local;pose.x+=direction.x*distance;pose.y=predicted.pose.y;pose.z+=direction.z*distance;pose.yaw=placementTurn*90;placementPose=pose;}}
                    }
                }else{
                var surface=new PlanetSurface(clientWorld.definition);
                var ray=new Ray(clientWorld.RenderToBody(clientWorld.View.transform.position).Float,clientWorld.View.transform.forward);
                if(surface.Raycast(ray,out var hit)){var p=surface.ProjectToSurface(new Double3(hit),.05);var pose=new FramePose{body=clientWorld.definition.bodyId,x=p.x,y=p.y,z=p.z,yaw=placementTurn*90};placementPose=latest.self.pose.Distance(pose)<=12?pose:(FramePose?)null;}else placementPose=null;
                }
            }
            if(mouse?.leftButton.wasPressedThisFrame==true&&placementPose.HasValue){Submit(IndustrialWorld.RespawnItem(placingMachine)?new SurvivalAction{kind="respawn-place",item=placingMachine,command=new IndustrialCommand{pose=placementPose.Value}}:new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="place",definition=placingMachine,pose=placementPose.Value}});placingMachine=null;placementPose=null;}
        }
        void RenderPlacement(){
            if(placingMachine==null||!placementPose.HasValue){if(placementGhost!=null)placementGhost.SetActive(false);return;}
            if(placementGhost==null){placementGhost=GameObject.CreatePrimitive(PrimitiveType.Cube);placementGhost.name="Network construction preview";Destroy(placementGhost.GetComponent<Collider>());placementGhost.GetComponent<Renderer>().sharedMaterial=machineMaterial;placementGhost.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
            placementGhost.SetActive(true);var pose=placementPose.Value;var p=SurvivalVehicleSimulation.Position(pose);var up=PlanetPrecisionPolicy.Direction(p);bool respawn=IndustrialWorld.RespawnItem(placingMachine);var scale=respawn?new Vector3(1.5f,.4f,2.5f):new Vector3((float)IndustrialCatalog.Machines[placingMachine].Width,2.5f,(float)IndustrialCatalog.Machines[placingMachine].Depth);
            if(pose.frame!=0&&predicted?.ship?.entity==pose.frame){var ship=predicted.ship;var world=SurvivalShipInteriorModel.World(ship,pose);p=SurvivalVehicleSimulation.Position(world);up=ShipRotation(ship)*Vector3.up;placementGhost.transform.SetPositionAndRotation(clientWorld.RenderBodyPosition(p)+up*(scale.y*.5f),ShipRotation(ship)*Quaternion.Euler(0,pose.yaw,0));placementGhost.transform.localScale=scale;return;}
            placementGhost.transform.SetPositionAndRotation(clientWorld.RenderBodyPosition(p)+up*(scale.y*.5f),Quaternion.FromToRotation(Vector3.up,up)*Quaternion.Euler(0,pose.yaw,0));placementGhost.transform.localScale=scale;
        }
        void MachineControlsGUI(){
            foreach(var machine in clientMachines.Values){if(machine.owner!=ActorId||ClientDistance(machine.pose)>12)continue;
                GUILayout.Label(machine.definition+" #"+machine.id+" · "+machine.status);
                GUILayout.BeginHorizontal();if(GUILayout.Button(machine.enabled?"DISABLE":"ENABLE"))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="toggle",target=machine.id}});
                if(machine.Definition.Vehicle&&GUILayout.Button("DRIVE")){Submit(new SurvivalAction{kind="enter-driver",entity=machine.id});industryOpen=false;UpdateNetworkCursor();}
                if(GUILayout.Button("PACK"))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="pack",target=machine.id}});GUILayout.EndHorizontal();
                foreach(var stack in latest.self.inventory.items){if(GUILayout.Button("Deposit "+stack.item+" ×"+Math.Min(10,stack.count)))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="deposit-item",target=machine.id,item=stack.item,count=Math.Min(10,stack.count)}});}
                foreach(var stack in machine.input.items){if(GUILayout.Button("Take input "+stack.item+" ×"+Math.Min(10,stack.count)))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="withdraw-input",source=machine.id,item=stack.item,count=Math.Min(10,stack.count)}});}
                foreach(var stack in machine.output.items){if(GUILayout.Button("Take output "+stack.item+" ×"+Math.Min(10,stack.count)))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="withdraw-item",source=machine.id,item=stack.item,count=Math.Min(10,stack.count)}});}
                foreach(var recipe in IndustrialCatalog.Recipes.Values)if(IndustrialCatalog.Supports(machine.definition,recipe.id)&&!IndustrialCatalog.RetiredConstructionRecipe(recipe.id)&&GUILayout.Button("Recipe: "+recipe.id))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="recipe",target=machine.id,item=recipe.id}});
            }
        }
    }
}
