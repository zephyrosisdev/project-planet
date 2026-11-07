using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MassiveArmy.Planetary {
    internal sealed partial class PlanetIndustry {
        bool placementArmed,placingPile;int placementTurn;double nextPlacement,placementInputReady;IndustrialCommand placement;string placementReason;readonly Collider[] placementOverlaps=new Collider[32];
        public bool PlacementActive=>placementArmed||cableActive||world.ShipToolActive;
        void UpdatePlacement(){
            if(!placementArmed)return;if(world.EquipmentInventory&&!placingPile&&world.PersonalInventory.Equipped!=IndustrialCatalog.MachineItem(buildNames[palette])){placementArmed=false;return;}
            if(!world.IsFirstPerson||Driving){placementArmed=false;return;}
            if(world.Paused||MenuOpen||world.PersonalMenuOpen||!Application.isFocused)return;
            var keys=Keyboard.current;
            if(keys?.escapeKey.wasPressedThisFrame==true){placementArmed=false;return;}
            if(keys?.rKey.wasPressedThisFrame==true){placementTurn=(placementTurn+1)%4;nextPlacement=0;}
            if(Time.unscaledTimeAsDouble>=nextPlacement){nextPlacement=Time.unscaledTimeAsDouble+.05;FindMachinePlacement();}
            if(Time.unscaledTimeAsDouble>=placementInputReady&&Mouse.current?.leftButton.wasPressedThisFrame==true){FindMachinePlacement();if(placementReason==null&&placement!=null){placement.sequence=actor.lastSequence+1;Execute(placement);world.ClearConsumedEquipment();nextPlacement=0;}else message=placementReason;}
        }
        void FindPilePlacement(){
            var ray=new Ray(world.View.transform.position,world.View.transform.forward);var eye=world.RenderToBody(ray.origin);Double3 point=eye+new Double3(ray.direction)*11.8;bool found=false;for(double t=.3;t<=12;t+=.1){var p=eye+new Double3(ray.direction)*t;if(p.Magnitude<=world.definition.radius+surface.SampleHeight(p)){point=surface.ProjectToSurface(p);found=true;break;}}
            if(found)point=ConstructionPlacement.ForwardGround(world,surface,ray,point,11.8f);var fitted=ConstructionPlacement.FitRoomSurface(surface,point,Rotation(Pose(point)),new Bounds(Vector3.zero,new Vector3(16,.1f,16)));bool tooSteep=(fitted-point).Magnitude>.5;point=fitted;placement=Command("pile");placement.pose=Pose(point);placementReason=!found?"Aim at clear, fairly flat ground":Authority.World.PilePlacementError(actor.id,placement.pose);
            if(placementReason!=null)return;if(tooSteep){placementReason="Choose flatter ground for the stockpile";return;}if(actor.pose.Distance(placement.pose)<11.7){placementReason="Move clear of the stockpile footprint";return;}foreach(var obstacle in Authority.World.haulObstacles)if(obstacle.pose.Distance(placement.pose)<obstacle.radius+8){placementReason="Stockpile footprint overlaps a building";return;}
        }
        void FindMachinePlacement(){
            if(placingPile){FindPilePlacement();return;}
            var ray=new Ray(world.View.transform.position,world.View.transform.forward);var eye=world.RenderToBody(ray.origin);var direction=new Double3(ray.direction);var definition=IndustrialCatalog.Machines[buildNames[palette]];var authored=ColonyPrototypeLibrary.Find("machine-"+definition.id);var footprint=authored?.Mesh(authored.id).bounds??new Bounds(Vector3.zero,new Vector3((float)definition.Width,3,(float)definition.Depth));float previewDistance=ConstructionPlacement.PreviewDistance(footprint);if(definition.Vehicle)previewDistance=Mathf.Max(previewDistance,(float)definition.Radius+3);var point=eye+direction*previewDistance;bool found=false,ground=false;
            Physics.SyncTransforms();bool physical=Physics.Raycast(ray,out var hit,12,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);float limit=physical?hit.distance+.1f:12;
            if(physical){point=world.RenderToBody(hit.point);found=true;}
            for(double t=.3;t<=limit;t+=.1){var at=eye+direction*t;if(at.Magnitude<=world.definition.radius+surface.SampleHeight(at)){point=surface.GetSurfacePositionPrecise(at.Float.normalized,0);found=true;ground=true;break;}}
            if(ground){point=ConstructionPlacement.ForwardGround(world,surface,ray,point,previewDistance);var groundPose=Pose(point);groundPose.yaw=placementTurn*90;point=ConstructionPlacement.FitRoomSurface(surface,point,Rotation(groundPose),footprint);}
            placement=Command("place");placement.definition=buildNames[palette];placement.pose=Pose(point);placement.pose.yaw=placementTurn*90;
            float best=.85f*.85f;
            foreach(var mount in Authority.World.mounts){if(mount.owner!=actor.id||mount.pose.body!=world.definition.bodyId||actor.pose.Distance(mount.pose)>12)continue;var delta=(Body(mount.pose)-eye).Float;float along=Vector3.Dot(delta,ray.direction);if(along<0||along>limit+.1f)continue;float score=(delta-ray.direction*along).sqrMagnitude;if(score>=best)continue;best=score;found=true;placement.pose=mount.pose;placement.pose.yaw+=placementTurn*90;placement.attachmentSite=mount.site;placement.attachmentModule=mount.module;placement.attachmentSocket=mount.socket;}
            placementReason=!found?"Aim at ground, a floor, or a workshop machine socket":Authority.World.PlacementError(actor.id,placement);
            if(placementReason!=null)return;
            var prototype=ColonyPrototypeLibrary.Find("machine-"+placement.definition);if(prototype==null)return;
            var rotation=Rotation(placement.pose);var origin=world.RenderBodyPosition(Body(placement.pose));
            foreach(var solid in prototype.solids){var shape=solid.Shape();var bounds=new Bounds(new Vector3(shape.polygon[0].x,shape.bottom,shape.polygon[0].y),Vector3.zero);foreach(var p in shape.polygon){bounds.Encapsulate(new Vector3(p.x,shape.bottom,p.y));bounds.Encapsulate(new Vector3(p.x,shape.top,p.y));}var extent=Vector3.Max(bounds.extents-Vector3.one*.015f,Vector3.one*.001f);int count=Physics.OverlapBoxNonAlloc(origin+rotation*bounds.center,extent,placementOverlaps,rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);if(count==placementOverlaps.Length){placementReason="Too many nearby obstructions";return;}for(int n=0;n<count;n++)if(world.PlayerForValidation.OwnsCollider(placementOverlaps[n])){placementReason="Move clear of the machine before placing it";return;}else{placementReason="Blocked by "+placementOverlaps[n].name;return;}}
        }
        void DrawPlacement(){if(world.LocalPlayerDead||world.RespawnPlacementActive||!placementArmed||placement==null||MenuOpen||world.PersonalMenuOpen||!world.IsFirstPerson)return;var model=placingPile?null:ColonyPrototypeLibrary.Find("machine-"+placement.definition);placementMaterial.SetColor("_PreviewTint",placementReason==null?new Color(.1f,1,.65f,.55f):new Color(1,.2f,.1f,.5f));var bounds=new Bounds(world.View.transform.position,Vector3.one*60);Draw(model?.Mesh(model.id)??cube,placementMaterial,Transform(placement.pose,Vector3.zero,placingPile?new Vector3(16,.12f,16):Vector3.one),bounds);}
    }
}


