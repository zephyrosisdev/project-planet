using System;

using System.Collections.Generic;

using UnityEngine;

namespace MassiveArmy.Planetary {

    internal sealed partial class PlayerBuildingWorld:IDisposable {

        readonly PlanetDevelopment world;readonly PlanetSurface surface;readonly PlayerBuildStorage state;readonly PlayerModuleProfile[] profiles;

        readonly Dictionary<long,int> reservedLand=new Dictionary<long,int>();readonly HashSet<long> reserveKeys=new HashSet<long>();

        readonly List<SiteIndex> sites=new List<SiteIndex>();readonly HashSet<long> queryIds=new HashSet<long>();readonly Collider[] overlaps=new Collider[48];

        static readonly BuildSolid AvatarSolid=BuildSolid.Box(-.35f,.35f,-.35f,.35f,.02f,1.8f);

        readonly HashSet<Collider> ownColliders=new HashSet<Collider>();

        readonly struct SocketAddress {internal readonly PlayerModuleRecord module;internal readonly int index;internal readonly BuildSocket socket;internal readonly Vector3 point;internal readonly float yaw;internal SocketAddress(PlayerModuleRecord m,int i,BuildSocket s){module=m;index=i;socket=s;var q=Quaternion.Euler(0,m.yaw,0);point=m.position+q*s.point;yaw=m.yaw+s.yaw;}}

        sealed class SiteIndex {

            internal PlayerBuildSite site;internal Mesh coarse;internal bool coarseDirty=true,drawCoarse;internal readonly Dictionary<long,PlayerModuleRecord> records=new Dictionary<long,PlayerModuleRecord>();

            internal readonly Dictionary<Vector3Int,List<PlayerModuleRecord>> cells=new Dictionary<Vector3Int,List<PlayerModuleRecord>>();internal readonly Dictionary<Vector3Int,List<SocketAddress>> sockets=new Dictionary<Vector3Int,List<SocketAddress>>();

            internal readonly HashSet<(long,int)> occupied=new HashSet<(long,int)>();internal readonly Dictionary<Vector3Int,Chunk> chunks=new Dictionary<Vector3Int,Chunk>();

        }

        internal struct Placement {internal SiteIndexToken site;internal Vector3 position;internal float yaw;internal long parent;internal int socket;internal Double3 anchor;internal Quaternion rotation;internal bool fresh,valid;internal string reason;}

        // The token keeps placement metadata internal without publishing the mutable spatial index.

        internal readonly struct SiteIndexToken {internal readonly object value;internal SiteIndexToken(object value){this.value=value;}}

        Placement preview;PlayerModuleKind selected;int turn;bool active;double nextPreview;int moduleCount;

        internal string PlacementPrompt=>active&&!RadialOpen?profiles[(int)selected].name.Replace('\n',' ')+" · "+preview.reason+"\nCost: "+world.PlayerModuleCostText(selected)+"\nLMB place · R rotate · RMB modules":"";
        internal bool RadialOpen {get;private set;}
        internal void SelectModule(PlayerModuleKind kind){selected=kind;turn=0;nextPreview=0;}

        internal PlayerBuildingWorld(PlanetDevelopment world,PlanetSurface surface,Shader previewShader=null){this.world=world;this.surface=surface;state=world.BuildStorage;state.playerBuildSites??=new List<PlayerBuildSite>();profiles=PlayerBuildingProfiles.Create();InitializeView(previewShader);foreach(var saved in state.playerBuildSites){saved.modules??=new List<PlayerModuleRecord>();var site=new SiteIndex{site=saved};sites.Add(site);foreach(var module in saved.modules){Index(site,module);moduleCount++;}}SyncLand();if(world.IsSurvival)world.PersistPlayerBuild();}

        static Vector3Int Cell(Vector3 p)=>new Vector3Int(Mathf.FloorToInt(p.x/3),Mathf.FloorToInt(p.y/3),Mathf.FloorToInt(p.z/3));

        static Vector3Int ChunkCell(Vector3 p)=>new Vector3Int(Mathf.FloorToInt(p.x/6),Mathf.FloorToInt(p.y/6),Mathf.FloorToInt(p.z/6));

        static Vector3 ChunkOrigin(Vector3Int c)=>new Vector3(c.x*6,c.y*6,c.z*6);

        static void Add<T>(Dictionary<Vector3Int,List<T>> map,Vector3Int key,T value){if(!map.TryGetValue(key,out var list)){list=new List<T>();map.Add(key,list);}list.Add(value);}

        void Index(SiteIndex site,PlayerModuleRecord module){ReserveLand(site,module,1);site.records[module.id]=module;Add(site.cells,Cell(module.position+Quaternion.Euler(0,module.yaw,0)*profiles[(int)module.kind].bounds.center),module);if(module.parent!=0)site.occupied.Add((module.parent,module.parentSocket));var profile=profiles[(int)module.kind];for(int i=0;i<profile.outputs.Length;i++){var socket=new SocketAddress(module,i,profile.outputs[i]);Add(site.sockets,Cell(socket.point),socket);}var key=ChunkCell(module.position);if(!site.chunks.TryGetValue(key,out var chunk)){chunk=new Chunk{site=site,key=key};site.chunks.Add(key,chunk);chunks.Add(chunk);}var profileBounds=profiles[(int)module.kind].bounds;if(module.open&&profiles[(int)module.kind].openSolids!=null){bool first=true;foreach(var solid in profiles[(int)module.kind].Shape(true)){var b=SolidBounds(solid);if(first){profileBounds=b;first=false;}else profileBounds.Encapsulate(b);}}var bounds=RotatedBounds(profileBounds,module.position-ChunkOrigin(key),Quaternion.Euler(0,module.yaw,0));if(chunk.records.Count==0)chunk.bounds=bounds;else chunk.bounds.Encapsulate(bounds);chunk.records.Add(module);chunk.dirty=true;site.coarseDirty=true;}

        void ReserveLand(SiteIndex site,PlayerModuleRecord record,int change){reserveKeys.Clear();var bounds=profiles[(int)record.kind].bounds;var q=Quaternion.Euler(0,record.yaw,0);for(int z=-1;z<=1;z+=2)for(int x=-1;x<=1;x+=2){var point=BodyPoint(site.site,record.position+q*(bounds.center+new Vector3(x*bounds.extents.x,0,z*bounds.extents.z)));var cell=world.BuildGrid.Cell(PlanetPrecisionPolicy.Direction(point));reserveKeys.Add(SettlementLandUse.Key(cell));}foreach(long key in reserveKeys){reservedLand.TryGetValue(key,out int count);count+=change;if(count<=0)reservedLand.Remove(key);else reservedLand[key]=count;}}

        void SyncLand(){state.playerBuildLand??=new List<PlanetNavCoordinate>();state.playerBuildLand.Clear();state.playerBuildLandIndex??=new HashSet<long>();state.playerBuildLandIndex.Clear();foreach(var pair in reservedLand){long key=pair.Key;state.playerBuildLand.Add(new PlanetNavCoordinate(world.definition.bodyId,(CubeFace)(key>>48),(int)((key>>24)&0xffffff),(int)(key&0xffffff)));state.playerBuildLandIndex.Add(key);}}

        Double3 BodyPoint(PlayerBuildSite site,Vector3 local)=>site.anchor+Double3.Rotate(site.rotation,new Double3(local));

        Vector3 Local(PlayerBuildSite site,Double3 point)=>Double3.Rotate(Quaternion.Inverse(site.rotation),point-site.anchor).Float;

        Quaternion Rotation(Placement p)=>p.rotation*Quaternion.Euler(0,p.yaw,0);

        Double3 BodyPoint(Placement p)=>p.anchor+Double3.Rotate(p.rotation,new Double3(p.position));

        bool TerrainRay(Ray ray,out Double3 point){var eye=world.RenderToBody(ray.origin);var d=new Double3(ray.direction);double before=0;double Gap(double t){var p=eye+d*t;return p.Magnitude-world.definition.radius-surface.SampleHeight(p);}

            double previous=Gap(0);for(int i=1;i<=24;i++){double at=i*.5,gap=Gap(at);if(previous>=0&&gap<=0){for(int k=0;k<10;k++){double mid=(before+at)*.5;if(Gap(mid)>0)before=mid;else at=mid;}point=eye+d*((before+at)*.5);return true;}before=at;previous=gap;}point=default;return false;}

        Placement FindPlacement(Ray ray){

            var profile=profiles[(int)selected];var eye=world.RenderToBody(ray.origin);float previewDistance=ConstructionPlacement.PreviewDistance(profile.bounds);var fallback=world.RenderToBody(ray.GetPoint(previewDistance));var p=new Placement{anchor=fallback,rotation=Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(fallback)),socket=-1,reason="Aim at terrain or a building socket"};

            Double3 aimed=default;bool ground=(profile.Foundation||profile.Room)&&TerrainRay(ray,out aimed);bool hit=Physics.Raycast(ray,out var physical,PlayerBuildingProfiles.Reach,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);if(hit)aimed=world.RenderToBody(physical.point);if(ground||hit){p.anchor=aimed;p.rotation=Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(aimed));}

            bool raySockets=profile.Door||!ground&&!hit;float rayLimit=hit?Mathf.Min(PlayerBuildingProfiles.Reach,physical.distance+.15f):PlayerBuildingProfiles.Reach;

            float best=2.2f*2.2f;bool snapped=false;

            foreach(var site in sites){if((eye-site.site.anchor).Magnitude>PlayerBuildingProfiles.SiteRadius+PlayerBuildingProfiles.Reach)continue;var localEye=Local(site.site,eye);var localDirection=Quaternion.Inverse(site.site.rotation)*ray.direction;

                for(int sample=0;sample<(raySockets?4:1);sample++){var local=raySockets?localEye+localDirection*(2+sample*3):Local(site.site,aimed);var cell=Cell(local);

                    for(int z=-1;z<=1;z++)for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)if(site.sockets.TryGetValue(cell+new Vector3Int(x,y,z),out var nearby))foreach(var output in nearby){if(site.occupied.Contains((output.module.id,output.index)))continue;float distance;

                        if(raySockets){var delta=output.point-localEye;float along=Vector3.Dot(delta,localDirection);if(along<0||along>rayLimit)continue;distance=(delta-localDirection*along).sqrMagnitude+along*.002f;}else distance=(output.point-local).sqrMagnitude;if(distance>best)continue;

                        for(int n=0;n<profile.inputs.Length;n++){var input=profile.inputs[(n+turn%Math.Max(1,profile.inputs.Length))%profile.inputs.Length];if(input.kind!=output.socket.kind)continue;float yaw=output.yaw-input.yaw+(input.kind==BuildSocketKind.FoundationEdge||input.kind==BuildSocketKind.FloorEdge||input.kind==BuildSocketKind.HabitatPort?180:0);

                            if(input.kind==BuildSocketKind.StairCenter||input.kind==BuildSocketKind.FloorCenter)yaw+=turn*90;else if(profile.Wall||profile.Door)yaw+=(turn%2)*180;

                            var candidate=new Placement{site=new SiteIndexToken(site),position=output.point-Quaternion.Euler(0,yaw,0)*input.point,yaw=yaw,parent=output.module.id,socket=output.index,anchor=site.site.anchor,rotation=site.site.rotation};bool valid=Validate(candidate,profile,false,out string reason);float score=distance+(valid?0:.02f)+n*.000001f;if(snapped&&score>=best)continue;best=score;candidate.valid=valid;candidate.reason=reason;p=candidate;snapped=true;

                        }

                    }

                }

            }

            if(snapped)return p;if(!profile.Foundation&&!profile.Room){p.reason="This module needs a compatible free socket";return p;}if(!ground||hit&&!ownColliders.Contains(physical.collider)&&!(physical.collider is TerrainCollider)){p.reason="Aim at clear terrain to place a standalone room or foundation";return p;}

            if(profile.Room)aimed=ConstructionPlacement.ForwardGround(world,surface,ray,aimed,previewDistance);var up=PlanetPrecisionPolicy.Direction(aimed);var heading=Vector3.ProjectOnPlane(world.View.transform.forward,up).normalized;if(heading.sqrMagnitude<.01f)heading=Vector3.ProjectOnPlane(Vector3.right,up).normalized;p=new Placement{fresh=true,anchor=profile.Room?aimed:aimed-new Double3(up)*2.5,rotation=Quaternion.LookRotation(heading,up),yaw=turn*(selected==PlayerModuleKind.FoundationTriangle?60:90),socket=-1};if(profile.Room)p.anchor=ConstructionPlacement.FitRoomSurface(surface,p.anchor,Rotation(p),profile.bounds);p.valid=Validate(p,profile,false,out p.reason);return p;

        }

        internal Placement PreviewForValidation(Ray ray,PlayerModuleKind kind,int rotation=0){selected=kind;turn=rotation;return FindPlacement(ray);}

        internal bool CommitForValidation(Placement placement,PlayerModuleKind kind){int before=moduleCount;preview=placement;selected=kind;Place();return moduleCount>before;}

        internal bool ValidateForValidation(Placement placement,PlayerModuleKind kind,out string reason)=>Validate(placement,profiles[(int)kind],false,out reason);

        bool Validate(Placement p,PlayerModuleProfile profile,bool open,out string reason,long ignore=0){reason="Ready · left click to place";if(world.BuildingPolicyError!=null){reason=world.BuildingPolicyError;return false;}if(ignore==0&&!world.CanPayPlayerModule(profile.kind)){reason="Need "+world.PlayerModuleCostText(profile.kind);return false;}if(moduleCount>=Mathf.Max(1,world.maximumPlayerBuildingModules)&&ignore==0){reason="Player building module limit reached";return false;}if(p.fresh&&sites.Count>=128){reason="Planet build-site limit reached";return false;}if(p.position.magnitude>PlayerBuildingProfiles.SiteRadius){reason="Start another foundation beyond this site's 96 m extent";return false;}

            var body=BodyPoint(p);var rotation=Rotation(p);if(world.IsFirstPerson&&ignore==0&&profile.bounds.SqrDistance(Quaternion.Inverse(rotation)*(world.PlayerForValidation.PreciseBodyPosition-body).Float)>PlayerBuildingProfiles.Reach*PlayerBuildingProfiles.Reach){reason="Move closer to the placement surface";return false;}var own= p.site.value as SiteIndex;if(own!=null&&p.parent!=0&&own.occupied.Contains((p.parent,p.socket))&&ignore==0){reason="That socket is occupied";return false;}

            foreach(var site in sites){if((body-site.site.anchor).Magnitude>PlayerBuildingProfiles.SiteRadius+16)continue;var position=Local(site.site,body);var relative=Quaternion.Inverse(site.site.rotation)*rotation;var c=Cell(position+relative*profile.bounds.center);var candidateBounds=RotatedBounds(profile.interactionBounds,position,relative);queryIds.Clear();int cellReach=profile.Room?5:4;

                for(int z=-cellReach;z<=cellReach;z++)for(int y=-cellReach;y<=cellReach;y++)for(int x=-cellReach;x<=cellReach;x++)if(site.cells.TryGetValue(c+new Vector3Int(x,y,z),out var list))foreach(var record in list){if(record.id==ignore||!queryIds.Add(record.id))continue;var other=profiles[(int)record.kind];
                    // A free socket authorizes structural joins inside this base. Adjacent
                    // foundations, wall corners and stair landings may overlap at their seams.
                    if(site==own&&p.parent!=0&&ignore==0&&!profile.Room&&!other.Room){if((record.position-position).sqrMagnitude<.0004f&&(record.kind==profile.kind||other.Wall&&profile.Wall)){reason="That module socket is already filled";return false;}continue;}
                    var q=Quaternion.Euler(0,record.yaw,0);if(!candidateBounds.Intersects(RotatedBounds(other.interactionBounds,record.position,q)))continue;float tolerance=profile.Wall&&other.Wall?.055f:.006f;

                    foreach(var a in profile.Shape(open))foreach(var b in other.Shape(record.open))if(PlayerBuildingProfiles.Overlap(a,position,relative,b,record.position,q,tolerance)){reason="Intersects another building module";return false;}

                    foreach(var a in profile.clearance)foreach(var b in other.Shape(record.open))if(PlayerBuildingProfiles.Overlap(a,position,relative,b,record.position,q)){reason="Stair headroom is obstructed";return false;}

                    foreach(var a in profile.Shape(open))foreach(var b in other.clearance)if(PlayerBuildingProfiles.Overlap(a,position,relative,b,record.position,q)){reason="Keep the stairwell clear";return false;}

                }}

            var root=world.RenderBodyPosition(body);foreach(var solid in profile.Shape(open)){var b=SolidBounds(solid);var half=Vector3.Max(b.extents-Vector3.one*.006f,Vector3.one*.001f);int count=Physics.OverlapBoxNonAlloc(root+rotation*b.center,half,overlaps,rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);if(count==overlaps.Length){reason="Too many nearby obstructions";return false;}for(int i=0;i<count;i++)if(!ownColliders.Contains(overlaps[i])&&!(world.PlayerForValidation?.OwnsCollider(overlaps[i])??false)&&!((profile.Foundation||profile.Room&&p.fresh)&&overlaps[i] is TerrainCollider)){reason="Blocked by "+overlaps[i].name;return false;}}

            if(world.IsFirstPerson){var relative=Quaternion.Inverse(p.rotation);var feet=Double3.Rotate(relative,world.PlayerForValidation.PreciseBodyPosition-p.anchor).Float;var playerBox=AvatarSolid;foreach(var solid in profile.Shape(open))if(PlayerBuildingProfiles.Overlap(solid,p.position,Quaternion.Euler(0,p.yaw,0),playerBox,feet,Quaternion.identity)){reason="Move clear of the module before placing it";return false;}}

            if(profile.Room&&p.fresh){for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){var local=p.position+Quaternion.Euler(0,p.yaw,0)*new Vector3(x*profile.bounds.extents.x,0,z*profile.bounds.extents.z);var point=p.anchor+Double3.Rotate(p.rotation,new Double3(local));double gap=point.Magnitude-world.definition.radius-surface.SampleHeight(point);if(gap<-.02||gap>.65){reason="Terrain is too uneven for this room; choose flatter ground or use foundations";return false;}}}
            if(profile.Foundation){foreach(var solid in profile.solids)foreach(var corner in solid.polygon){var v=p.position+Quaternion.Euler(0,p.yaw,0)*new Vector3(corner.x,0,corner.y);var point=p.anchor+Double3.Rotate(p.rotation,new Double3(v));double floor=world.definition.radius+(double)surface.SampleHeight(point);double gap=point.Magnitude-floor;if(gap> .1||gap< -3.05){reason="Foundation must contact terrain without burying its top";return false;}}

                var direction=PlanetPrecisionPolicy.Direction(body);var cell=world.BuildGrid.Cell(direction);if(world.Base!=null&&(world.Base.Roads.HasRoad(cell)||world.Base.Rails.HasRoad(cell)||world.Base.LandCellOccupied(cell))){reason="Keep roads, parks and farm fields clear";return false;}}

            return true;

        }

        static Bounds RotatedBounds(Bounds bounds,Vector3 position,Quaternion q){var a=q*new Vector3(bounds.extents.x,0,0);var b=q*new Vector3(0,bounds.extents.y,0);var c=q*new Vector3(0,0,bounds.extents.z);return new Bounds(position+q*bounds.center,new Vector3(Mathf.Abs(a.x)+Mathf.Abs(b.x)+Mathf.Abs(c.x),Mathf.Abs(a.y)+Mathf.Abs(b.y)+Mathf.Abs(c.y),Mathf.Abs(a.z)+Mathf.Abs(b.z)+Mathf.Abs(c.z))*2);}

        static Bounds SolidBounds(BuildSolid solid){var b=new Bounds(new Vector3(solid.polygon[0].x,solid.bottom,solid.polygon[0].y),Vector3.zero);foreach(var p in solid.polygon){b.Encapsulate(new Vector3(p.x,solid.bottom,p.y));b.Encapsulate(new Vector3(p.x,solid.top,p.y));}return b;}

        void Place(){if(world.BuildingPolicyError!=null){world.BuildNotice=world.BuildingPolicyError;return;}if(!world.CanPayPlayerModule(selected)){world.BuildNotice="Need construction resources";return;}var profile=profiles[(int)selected];if(!preview.valid){world.BuildNotice=preview.reason??"Invalid placement";return;}if(!Validate(preview,profile,false,out string reason)){world.BuildNotice=reason;return;}var site=preview.site.value as SiteIndex;if(site==null){var data=new PlayerBuildSite{id=world.AllocatePlayerBuildId(),anchor=preview.anchor,rotation=preview.rotation};state.playerBuildSites.Add(data);site=new SiteIndex{site=data};sites.Add(site);}var record=new PlayerModuleRecord{id=world.AllocatePlayerBuildId(),kind=selected,position=preview.position,yaw=preview.yaw,parent=preview.parent,parentSocket=preview.socket,resourceConstruction=true,constructionPaid=world.PlayerModuleReceipt(selected)};foreach(var paid in record.constructionPaid)record.constructionActivity+=paid.activity;site.site.modules.Add(record);Index(site,record);moduleCount++;SyncLand();BuildChunk(site.chunks[ChunkCell(record.position)]);RefreshColliders();world.PersistPlayerBuild(record);world.ClearConsumedEquipment();world.BuildNotice=profile.name.Replace('\n',' ')+" placed";nextPreview=0;}

        internal bool UndoLastPlacement(){if(world.BuildingPolicyError!=null){world.BuildNotice=world.BuildingPolicyError;return false;}SiteIndex site=null;PlayerModuleRecord record=null;foreach(var candidate in sites)if(candidate.site.modules.Count>0){var last=candidate.site.modules[candidate.site.modules.Count-1];if(record==null||last.id>record.id){site=candidate;record=last;}}if(record==null)return false;if(world.HasMountedMachines(site.site.id,record.id)){world.BuildNotice="Pack attached machinery, empty module storage and recover its belts before removing this module";return false;}if(!world.CanRefundPlayerModule(record)){world.BuildNotice="Make room for recovered construction resources";return false;}

            var profile=profiles[(int)record.kind];ReserveLand(site,record,-1);site.site.modules.Remove(record);site.records.Remove(record.id);if(record.parent!=0)site.occupied.Remove((record.parent,record.parentSocket));var c=Cell(record.position+Quaternion.Euler(0,record.yaw,0)*profile.bounds.center);if(site.cells.TryGetValue(c,out var list)){list.Remove(record);if(list.Count==0)site.cells.Remove(c);}

            for(int i=0;i<profile.outputs.Length;i++){var socket=new SocketAddress(record,i,profile.outputs[i]);if(site.sockets.TryGetValue(Cell(socket.point),out var entries)){for(int j=entries.Count-1;j>=0;j--)if(entries[j].module.id==record.id)entries.RemoveAt(j);if(entries.Count==0)site.sockets.Remove(Cell(socket.point));}}

            var key=ChunkCell(record.position);var chunk=site.chunks[key];chunk.records.Remove(record);if(chunk.records.Count==0){foreach(var slot in colliderSlots)if(slot.chunk==chunk)ReleaseSlot(slot);ArmyLifetime.Destroy(chunk.mesh);ArmyLifetime.Destroy(chunk.coarse);ArmyLifetime.Destroy(chunk.collision);site.chunks.Remove(key);chunks.Remove(chunk);}else {chunk.dirty=true;BuildChunk(chunk);}if(site.site.modules.Count==0){ArmyLifetime.Destroy(site.coarse);sites.Remove(site);state.playerBuildSites.Remove(site.site);}site.coarseDirty=true;moduleCount--;SyncLand();nextPreview=0;RefreshColliders();world.PersistPlayerBuild(record,true);world.BuildNotice="Undid last module placement";return true;

        }

        internal bool InteractDoor(){if(!world.IsFirstPerson)return false;var ray=new Ray(world.View.transform.position,world.View.transform.forward);SiteIndex bestSite=null;PlayerModuleRecord best=null;float nearest=3.5f;foreach(var site in sites){if((world.RenderToBody(ray.origin)-site.site.anchor).Magnitude>PlayerBuildingProfiles.SiteRadius+4)continue;var eye=Local(site.site,world.RenderToBody(ray.origin));var direction=Quaternion.Inverse(site.site.rotation)*ray.direction;var cell=Cell(eye);queryIds.Clear();for(int z=-2;z<=2;z++)for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)if(site.cells.TryGetValue(cell+new Vector3Int(x,y,z),out var list))foreach(var record in list){if(!queryIds.Add(record.id)||!profiles[(int)record.kind].Door)continue;var q=Quaternion.Inverse(Quaternion.Euler(0,record.yaw,0));var localRay=new Ray(q*(eye-record.position),q*direction);var bounds=profiles[(int)record.kind].bounds;bounds.Expand(new Vector3(.3f,.2f,2));if(bounds.IntersectRay(localRay,out float distance)&&distance>=0&&distance<nearest){nearest=distance;best=record;bestSite=site;}}}

            if(best==null)return false;var placement=new Placement{site=new SiteIndexToken(bestSite),position=best.position,yaw=best.yaw,anchor=bestSite.site.anchor,rotation=bestSite.site.rotation};if(!Validate(placement,profiles[(int)best.kind],!best.open,out var reason,best.id)){world.BuildNotice="Door blocked · "+reason;return true;}best.open=!best.open;bestSite.coarseDirty=true;var chunk=bestSite.chunks[ChunkCell(best.position)];chunk.dirty=true;BuildChunk(chunk);Physics.SyncTransforms();world.PersistPlayerBuild();world.BuildNotice=best.open?"Door opened":"Door closed";return true;

        }

    }

}
