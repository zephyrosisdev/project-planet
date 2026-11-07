using System;

using System.Collections.Generic;

using UnityEngine;

using UnityEngine.InputSystem;

using UnityEngine.Rendering;

namespace MassiveArmy.Planetary {

    internal sealed partial class PlayerBuildingWorld {

        sealed class Chunk {internal SiteIndex site;internal Vector3Int key;internal readonly List<PlayerModuleRecord> records=new List<PlayerModuleRecord>();internal Mesh mesh,collision,coarse;internal bool dirty=true;internal Bounds bounds;internal float distance;}

        sealed class ColliderSlot {internal GameObject root;internal MeshCollider collider;internal Chunk chunk;}

        readonly List<Chunk> chunks=new List<Chunk>(),nearChunks=new List<Chunk>();readonly List<ColliderSlot> colliderSlots=new List<ColliderSlot>();readonly Plane[] planes=new Plane[6];readonly ModularGeometry geometry=new ModularGeometry();readonly MaterialPropertyBlock ghostProperties=new MaterialPropertyBlock();

        Mesh fallbackCube;Material material,ghost;GameObject collisionRoot;double nextColliders;bool released;int menuIndex,menuPage;
        int PageStart=>menuPage==0?0:menuPage==1?10:15;
        int PageCount=>menuPage==0?10:menuPage==1?5:11;PlayerModuleKind promptKind;string prompt="",lastReason="",hudPrompt="";

        static readonly Comparison<Chunk> Closest=(a,b)=>a.distance.CompareTo(b.distance);

        void InitializeView(Shader previewShader){material=ColonyPrototypeLibrary.Material("Player modular building");material.SetColor("_BaseColor",new Color(.43f,.49f,.54f));material.SetFloat("_Metallic",.18f);material.SetFloat("_Smoothness",.32f);var shader=previewShader!=null?previewShader:Resources.Load<Shader>("MassiveArmy/PlayerBlueprint");ghost=new Material(shader!=null?shader:Shader.Find("HDRP/Unlit"));collisionRoot=new GameObject("Nearby player building collision chunks");collisionRoot.transform.SetParent(world.transform,false);

            geometry.Clear();geometry.Box(Vector3.zero,Vector3.one,Color.gray);fallbackCube=geometry.Build("Player building load fallback");fallbackCube.UploadMeshData(true);foreach(var profile in profiles){geometry.Clear();Append(profile,false,false,Vector3.zero,Quaternion.identity,geometry);profile.mesh=geometry.Build(profile.name);profile.mesh.UploadMeshData(true);if(profile.openSolids!=null){geometry.Clear();Append(profile,true,false,Vector3.zero,Quaternion.identity,geometry);profile.openMesh=geometry.Build(profile.name+" open");profile.openMesh.UploadMeshData(true);}}

        }

        static Color Colour(PlayerModuleProfile profile)=>profile.Foundation?new Color(.42f,.43f,.44f,0):profile.Door?new Color(.2f,.35f,.42f,0):new Color(.59f,.64f,.67f,0);

        static Vector3 P(Vector2 p,float y,Vector3 position,Quaternion q)=>position+q*new Vector3(p.x,y,p.y);

        internal static void Prism(BuildSolid shape,Vector3 position,Quaternion q,Color colour,ModularGeometry g){var poly=shape.polygon;for(int i=1;i<poly.Length-1;i++){g.Triangle(P(poly[0],shape.top,position,q),P(poly[i+1],shape.top,position,q),P(poly[i],shape.top,position,q),colour);g.Triangle(P(poly[0],shape.bottom,position,q),P(poly[i],shape.bottom,position,q),P(poly[i+1],shape.bottom,position,q),colour);}for(int i=0;i<poly.Length;i++){var a=poly[i];var b=poly[(i+1)%poly.Length];g.Quad(P(a,shape.bottom,position,q),P(a,shape.top,position,q),P(b,shape.top,position,q),P(b,shape.bottom,position,q),colour);}}

        static void Ramp(float x0,float x1,float z0,float z1,float y0,float y1,bool alongX,Vector3 position,Quaternion q,ModularGeometry g){Vector3 T(float x,float y,float z)=>position+q*new Vector3(x,y,z);var a=T(x0,y0,z0);var b=T(x0,alongX?y0:y1,z1);var c=T(x1,y1,z1);var d=T(x1,alongX?y1:y0,z0);g.Quad(a,b,c,d,Color.gray);var aa=T(x0,0,z0);var bb=T(x0,0,z1);var cc=T(x1,0,z1);var dd=T(x1,0,z0);g.Quad(dd,cc,bb,aa,Color.gray);g.Quad(aa,a,d,dd,Color.gray);g.Quad(dd,d,c,cc,Color.gray);g.Quad(cc,c,b,bb,Color.gray);g.Quad(bb,b,a,aa,Color.gray);}

        internal static void Append(PlayerModuleProfile profile,bool open,bool collision,Vector3 at,Quaternion rotation,ModularGeometry g){

            if(!collision&&!(profile.Door&&open)){var prototype=ColonyPrototypeLibrary.Find((profile.Room?"room-":"base-")+profile.kind);if(prototype!=null){g.AppendMesh(prototype.Mesh(prototype.id),at,rotation);return;}}
            if(collision&&profile.kind>=PlayerModuleKind.StairsStraight&&profile.kind<=PlayerModuleKind.Stairs180){if(profile.kind==PlayerModuleKind.StairsStraight)Ramp(-1.45f,1.45f,-1.5f,1.5f,0,3,false,at,rotation,g);else {Ramp(-1.5f,-.3f,-1.5f,.3f,0,1.5f,false,at,rotation,g);Prism(BuildSolid.Box(-1.5f,profile.kind==PlayerModuleKind.Stairs180?1.5f:-.3f,.3f,1.5f,0,1.5f),at,rotation,Color.gray,g);if(profile.kind==PlayerModuleKind.Stairs90)Ramp(-.3f,1.5f,.3f,1.5f,1.5f,3,true,at,rotation,g);else Ramp(.3f,1.5f,-1.5f,.3f,3,1.5f,false,at,rotation,g);}return;}

            foreach(var solid in profile.Shape(open))Prism(solid,at,rotation,Colour(profile),g);

        }

        void BuildChunk(Chunk chunk){if(!chunk.dirty&&chunk.mesh!=null)return;foreach(var slot in colliderSlots)if(slot.chunk==chunk)slot.collider.sharedMesh=null;ArmyLifetime.Destroy(chunk.mesh);ArmyLifetime.Destroy(chunk.collision);ArmyLifetime.Destroy(chunk.coarse);chunk.collision=null;geometry.Clear();var origin=ChunkOrigin(chunk.key);foreach(var record in chunk.records)Append(profiles[(int)record.kind],record.open,false,record.position-origin,Quaternion.Euler(0,record.yaw,0),geometry);chunk.mesh=geometry.Build("Player building chunk");chunk.bounds=chunk.mesh.bounds;chunk.mesh.UploadMeshData(true);geometry.Clear();geometry.Box(chunk.bounds.center,Vector3.Max(chunk.bounds.size,Vector3.one*.05f),new Color(.4f,.44f,.48f,0));chunk.coarse=geometry.Build("Distant player building bounds");chunk.coarse.UploadMeshData(true);chunk.dirty=false;foreach(var slot in colliderSlots)if(slot.chunk==chunk)BindCollision(slot,chunk);}

        void BuildSiteCoarse(SiteIndex site){geometry.Clear();foreach(var chunk in site.chunks.Values)geometry.Box(ChunkOrigin(chunk.key)+chunk.bounds.center,Vector3.Max(chunk.bounds.size,Vector3.one*.01f),new Color(.4f,.44f,.48f,0));ArmyLifetime.Destroy(site.coarse);site.coarse=geometry.Build("Batched distant player base");site.coarse.UploadMeshData(true);site.coarseDirty=false;}

        internal int PrepareCoarseForValidation(){int count=0;foreach(var site in sites){if(site.coarseDirty||site.coarse==null)BuildSiteCoarse(site);if(site.coarse!=null)count++;}return count;}

        void BindCollision(ColliderSlot slot,Chunk chunk){if(chunk.collision==null){geometry.Clear();var origin=ChunkOrigin(chunk.key);foreach(var record in chunk.records)Append(profiles[(int)record.kind],record.open,true,record.position-origin,Quaternion.Euler(0,record.yaw,0),geometry);chunk.collision=geometry.Build("Player building walking surface");}slot.collider.sharedMesh=chunk.collision;}

        internal void PrepareRespawnCollision(){nextColliders=0;PrepareCollision();}
        internal void PrepareCollision(){if(Time.unscaledTimeAsDouble>=nextColliders){nextColliders=Time.unscaledTimeAsDouble+.25;RefreshColliders();}RebaseColliders();}

        internal void RebaseColliders(){if(collisionRoot==null||colliderSlots.Count==0)return;bool enabled=world.ResidentActive&&!world.RemoteView;if(collisionRoot.activeSelf!=enabled)collisionRoot.SetActive(enabled);foreach(var slot in colliderSlots)if(slot.chunk!=null){var c=slot.chunk;var at=world.RenderBodyPosition(BodyPoint(c.site.site,ChunkOrigin(c.key)));var pose=slot.root.transform;if(pose.position!=at||pose.rotation!=c.site.site.rotation)pose.SetPositionAndRotation(at,c.site.site.rotation);}}

        void ReleaseSlot(ColliderSlot slot){if(slot.chunk!=null){slot.collider.sharedMesh=null;ArmyLifetime.Destroy(slot.chunk.collision);slot.chunk.collision=null;slot.chunk=null;}slot.root.SetActive(false);}

        void RefreshColliders(){if(chunks.Count==0&&colliderSlots.Count==0)return;if(world.RemoteView){collisionRoot.SetActive(false);return;}int limit=Mathf.Clamp(world.playerBuildingColliderChunks,1,64);for(int i=colliderSlots.Count-1;i>=limit;i--){ReleaseSlot(colliderSlots[i]);ownColliders.Remove(colliderSlots[i].collider);ArmyLifetime.Destroy(colliderSlots[i].root);colliderSlots.RemoveAt(i);}nearChunks.Clear();var eye=world.PlayerForValidation.PreciseBodyPosition;

            foreach(var c in chunks){var local=Local(c.site.site,eye)-ChunkOrigin(c.key);var b=c.mesh!=null?c.bounds:new Bounds(Vector3.one*1.5f,Vector3.one*9);c.distance=b.SqrDistance(local);if(c.distance<24*24)nearChunks.Add(c);}nearChunks.Sort(Closest);if(nearChunks.Count>limit)nearChunks.RemoveRange(limit,nearChunks.Count-limit);

            foreach(var slot in colliderSlots)if(slot.chunk!=null&&!nearChunks.Contains(slot.chunk))ReleaseSlot(slot);

            foreach(var chunk in nearChunks){bool found=false;foreach(var slot in colliderSlots)if(slot.chunk==chunk){found=true;break;}if(found)continue;ColliderSlot free=null;foreach(var slot in colliderSlots)if(slot.chunk==null){free=slot;break;}if(free==null){var go=new GameObject("Player build collision chunk");go.transform.SetParent(collisionRoot.transform,false);var collider=go.AddComponent<MeshCollider>();free=new ColliderSlot{root=go,collider=collider};colliderSlots.Add(free);ownColliders.Add(collider);}if(chunk.dirty||chunk.mesh==null)BuildChunk(chunk);free.chunk=chunk;BindCollision(free,chunk);free.root.SetActive(true);}

            RebaseColliders();Physics.SyncTransforms();

        }

        void CloseRadial(){if(!RadialOpen)return;RadialOpen=false;world.SetPersonalCursor(world.PersonalMenuOpen||world.Paused);}

        internal void Update(){released=false;bool carriedModule=world.EquippedBuildingModule(out var equippedKind);if(carriedModule&&selected!=equippedKind){selected=equippedKind;turn=0;nextPreview=0;}bool equipped=world.IsFirstPerson&&!world.LocalPlayerDead&&!world.RespawnPlacementActive&&!world.IndustrialPlacementActive&&!world.PersonalMenuOpen&&(carriedModule||!world.EquipmentInventory&&world.PersonalItems.Find(world.PersonalInventory.Equipped)?.useAction=="blueprint");if(!equipped||world.Paused||!Application.isFocused){active=false;CloseRadial();}else {active=true;var mouse=Mouse.current;var keyboard=Keyboard.current;

                if(!carriedModule&&mouse!=null&&mouse.rightButton.wasPressedThisFrame){RadialOpen=true;menuIndex=(int)selected;menuPage=menuIndex<10?0:menuIndex<15?1:2;world.SetPersonalCursor(true);}

                if(RadialOpen&&mouse!=null){var direction=mouse.position.ReadValue()-new Vector2(Screen.width*.5f,Screen.height*.5f);if(mouse.scroll.ReadValue().y!=0){menuPage=(menuPage+(mouse.scroll.ReadValue().y>0?1:2))%3;menuIndex=PageStart;}if(direction.magnitude>45)menuIndex=PageStart+PlayerBuildingProfiles.Radial(direction,PageCount);if(mouse.rightButton.wasReleasedThisFrame){selected=(PlayerModuleKind)menuIndex;turn=0;CloseRadial();nextPreview=0;}}

                if(keyboard!=null&&keyboard.zKey.wasPressedThisFrame&&!RadialOpen)UndoLastPlacement();if(keyboard!=null&&keyboard.rKey.wasPressedThisFrame&&!RadialOpen){turn=(turn+1)%12;nextPreview=0;}if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame){CloseRadial();active=false;}

                if(!RadialOpen&&active){if(Time.unscaledTimeAsDouble>=nextPreview){nextPreview=Time.unscaledTimeAsDouble+.05;Physics.SyncTransforms();preview=FindPlacement(new Ray(world.View.transform.position,world.View.transform.forward));if(selected!=promptKind||lastReason!=preview.reason){promptKind=selected;lastReason=preview.reason;prompt=profiles[(int)selected].name.Replace('\n',' ')+" · "+preview.reason+"\nCost: "+world.PlayerModuleCostText(selected);hudPrompt="BLUEPRINT · Hold RMB + scroll: categories · LMB: place · R: rotate\n"+prompt+"\nZ: undo last placement · F: open / close a built door";}}if(mouse!=null&&mouse.leftButton.wasPressedThisFrame)Place();}

            }

            if(Time.unscaledTimeAsDouble>=nextColliders){nextColliders=Time.unscaledTimeAsDouble+.25;RefreshColliders();}RebaseColliders();

            foreach(var chunk in chunks)if(chunk.dirty){var eye=Local(chunk.site.site,world.RenderToBody(world.View.transform.position))-ChunkOrigin(chunk.key);if(chunk.bounds.SqrDistance(eye)>Mathf.Pow(Mathf.Max(25,world.playerBuildingDetailDistance),2))continue;BuildChunk(chunk);break;}

            GeometryUtility.CalculateFrustumPlanes(world.View,planes);

            foreach(var site in sites){if(site.coarseDirty||site.coarse==null)BuildSiteCoarse(site);var point=world.RenderBodyPosition(site.site.anchor);var bounds=site.coarse.bounds;var worldBounds=new Bounds(point+site.site.rotation*bounds.center,Vector3.one*bounds.size.magnitude);site.drawCoarse=worldBounds.SqrDistance(world.View.transform.position)>Mathf.Pow(Mathf.Max(50,world.playerBuildingDetailDistance),2);if(site.drawCoarse&&GeometryUtility.TestPlanesAABB(planes,worldBounds))Graphics.DrawMesh(site.coarse,Matrix4x4.TRS(point,site.site.rotation,Vector3.one),material,0,world.View,0,null,ShadowCastingMode.Off,true);}

            foreach(var chunk in chunks){if(chunk.site.drawCoarse)continue;var point=world.RenderBodyPosition(BodyPoint(chunk.site.site,ChunkOrigin(chunk.key)));var bounds=new Bounds(point+chunk.site.site.rotation*chunk.bounds.center,Vector3.one*chunk.bounds.size.magnitude);if(!GeometryUtility.TestPlanesAABB(planes,bounds))continue;float distance=Vector3.Distance(world.View.transform.position,bounds.center);var mesh=distance>Mathf.Max(25,world.playerBuildingDetailDistance)?chunk.coarse:chunk.mesh;if(mesh==null){Graphics.DrawMesh(fallbackCube,Matrix4x4.TRS(bounds.center,chunk.site.site.rotation,Vector3.Max(chunk.bounds.size,Vector3.one*.01f)),material,0,world.View,0,null,ShadowCastingMode.Off,true);}else Graphics.DrawMesh(mesh,Matrix4x4.TRS(point,chunk.site.site.rotation,Vector3.one),material,0,world.View,0,null,mesh==chunk.mesh?ShadowCastingMode.On:ShadowCastingMode.Off,true);}

            if(active&&!RadialOpen){ghostProperties.SetFloat("_Valid",preview.valid?1:0);Graphics.DrawMesh(profiles[(int)selected].mesh,Matrix4x4.TRS(world.RenderBodyPosition(BodyPoint(preview)),Rotation(preview),Vector3.one),ghost,0,world.View,0,ghostProperties,ShadowCastingMode.Off,false);}

        }

        internal void DrawHud(){if(!active&&!RadialOpen)return;if(!RadialOpen){if(world.IsSurvival)return;GUI.Box(new Rect(Screen.width*.5f-300,Screen.height-150,600,95),hudPrompt);return;}

            var center=new Vector2(Screen.width*.5f,Screen.height*.5f);float radius=Mathf.Min(250,Screen.height*.3f),width=Mathf.Min(95,radius*.36f);GUI.Box(new Rect(center.x-120,center.y-30,240,60),(menuPage==0?"STRUCTURES":menuPage==1?"FLOORS & STAIRS":"COLONY ROOMS")+"\nScroll: category · release RMB: select");var previous=GUI.color;for(int n=0;n<PageCount;n++){int i=PageStart+n;float angle=n*Mathf.PI*2/PageCount;var point=center+new Vector2(Mathf.Cos(angle),-Mathf.Sin(angle))*radius;GUI.color=i==menuIndex?new Color(.2f,1,.65f):Color.white;GUI.Box(new Rect(point.x-width*.5f,point.y-25,width,50),profiles[i].name);}GUI.color=previous;

        }

        internal void ReleaseViewResources(){if(released)return;released=true;CloseRadial();foreach(var site in sites){ArmyLifetime.Destroy(site.coarse);site.coarse=null;site.coarseDirty=true;}foreach(var slot in colliderSlots)ReleaseSlot(slot);foreach(var chunk in chunks){ArmyLifetime.Destroy(chunk.mesh);ArmyLifetime.Destroy(chunk.coarse);ArmyLifetime.Destroy(chunk.collision);chunk.mesh=chunk.coarse=chunk.collision=null;chunk.dirty=true;}}

        public void Dispose(){CloseRadial();ArmyLifetime.Destroy(fallbackCube);foreach(var site in sites)ArmyLifetime.Destroy(site.coarse);foreach(var chunk in chunks){ArmyLifetime.Destroy(chunk.mesh);ArmyLifetime.Destroy(chunk.coarse);ArmyLifetime.Destroy(chunk.collision);}foreach(var profile in profiles){ArmyLifetime.Destroy(profile.mesh);ArmyLifetime.Destroy(profile.openMesh);}ArmyLifetime.Destroy(material);ArmyLifetime.Destroy(ghost);ArmyLifetime.Destroy(collisionRoot);}

    }

}



