using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.VFX;

namespace MassiveArmy
{
    // Fixed presentation pools; never an army mirror or a GameObject per soldier/projectile.
    public sealed class CombatPresentation : IDisposable
    {
        public const int DecalCapacity=512,EmitterCapacity=8;
        const int SparkEmitters=4,OrdinaryEnd=6;
        [StructLayout(LayoutKind.Sequential)] struct Stamp {public Vector4 positionSize,appearance,timing;}
        sealed class Emitter {public VisualEffect effect;public VFXEventAttribute attributes;public Vector3 position;public float until,previewUntil,bounds=120;public bool paused,preview;}
        readonly GpuBattle battle;readonly GameObject root;readonly Emitter[] emitters=new Emitter[EmitterCapacity];
        RtsCamera cameraRig;
        readonly Stamp[] stamps=new Stamp[DecalCapacity];readonly Plane[] planes=new Plane[6];
        const int IndexCapacity=524288;
        [StructLayout(LayoutKind.Sequential)] struct TileUpdate {public uint cell,offset;}
        readonly GraphicsBuffer stampBuffer,stampSlots,tileBuffer,coverageBuffer;readonly ComputeShader shader;readonly RenderTexture index;readonly Texture2DArray textures,normals;
        readonly Dictionary<int,List<uint>> tiles=new Dictionary<int,List<uint>>();readonly Stack<List<uint>> freeTiles=new Stack<List<uint>>();
        readonly List<int> previousTiles=new List<int>();readonly List<int> ordered=new List<int>(DecalCapacity);
        readonly uint[] tileIndices=new uint[IndexCapacity];readonly TileUpdate[] tileUpdates=new TileUpdate[IndexCapacity];
        uint sequence;
        int nextOrdinary=64,visibleCraters,bakedCraters,lastNuclear=OrdinaryEnd;float clock,nextVisibility;bool dirty,indexDirty,hasView,lastEnabled;int resolution;Vector3 renderFocus;Matrix4x4 lastView;
        const int AtlasResolution=1024;
        readonly RenderTexture craterColour,craterNormal;
        readonly List<int> craterCandidates=new List<int>();readonly int[] craterSlots=new int[64];
        readonly Vector4 craterNeutral;
        public int StoredCraters=>battle.Field.Craters.Count;
        public int ActiveDecals {get;private set;}public int VisibleDecals {get;private set;}public int BurstsStarted {get;private set;}public int DroppedBursts {get;private set;}
        public int AliveParticles {get{int count=0;foreach(var item in emitters)count+=item.effect.aliveParticleCount;return count;}}
#if UNITY_EDITOR
        internal Vector2 DecalForValidation(uint kind){foreach(var stamp in stamps)if(stamp.timing.y>0&&stamp.appearance.x==kind-1)return new Vector2(stamp.positionSize.w,stamp.timing.y);return Vector2.zero;}
        internal int DestructionSystemsForValidation(){var names=new List<string>();emitters[SparkEmitters].effect.GetParticleSystemNames(names);return names.Count;}
        internal bool CraterCachedForValidation(int id)=>Array.IndexOf(craterSlots,id)>=0;
        internal RenderTexture CraterAtlasForValidation=>craterColour;
        internal VisualEffect NuclearForValidation=>emitters[lastNuclear].effect;
#endif
        public string Diagnostics
        {
            get{
                var text=new StringBuilder();
                for(int i=0;i<emitters.Length;i++){
                    var item=emitters[i];if(item.until<=0)continue;var fx=item.effect;float lifetime=item.attributes.GetFloat("lifetime");
                    text.AppendLine($"{i}: active={fx.gameObject.activeSelf}, paused={fx.pause}, culled={fx.culled}, particles={fx.aliveParticleCount}, lifetime={lifetime}");
                    var names=new List<string>();fx.GetSpawnSystemNames(names);
                    foreach(string name in names)using(var state=fx.GetSpawnSystemInfo(name))text.AppendLine($"  {name}: playing={state.playing}, count={state.spawnCount}, time={state.totalTime}");
                }
                return text.ToString();
            }
        }
        public CombatPresentation(GpuBattle battle)
        {
            this.battle=battle;root=new GameObject("Bounded combat presentation");
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/CombatPresentation"));
            resolution=Mathf.RoundToInt(battle.Field.Size/16);
            index=new RenderTexture(new RenderTextureDescriptor(resolution,resolution){graphicsFormat=GraphicsFormat.R32_UInt,depthBufferBits=0,dimension=TextureDimension.Tex2DArray,volumeDepth=2,enableRandomWrite=true,msaaSamples=1}){name="Crater and remains spatial indices",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};index.Create();
            int clear=shader.FindKernel("ClearDecalMap");shader.SetInt("_IndexResolution",resolution);shader.SetTexture(clear,"_CombatIndexWrite",index);shader.Dispatch(clear,(resolution+7)/8,(resolution+7)/8,2);
            stampBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,DecalCapacity,48);stampBuffer.SetData(stamps);stampSlots=new GraphicsBuffer(GraphicsBuffer.Target.Structured,IndexCapacity,4);tileBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,IndexCapacity,8);
            string[] names={"NukeCrater","ZombieSplatter","HumanRemains","TankWreck","VehicleWreck","BuildingRubble"};
            var first=Resources.Load<Texture2D>("MassiveArmy/CombatArt/"+names[0]+"Albedo");if(first==null)throw new InvalidOperationException("Missing combat decal artwork");
            textures=new Texture2DArray(first.width,first.height,names.Length,first.format,true,false){name="Combat ground remains",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=2};textures.Apply(false,true);
            for(int i=0;i<names.Length;i++){var source=Resources.Load<Texture2D>("MassiveArmy/CombatArt/"+names[i]+"Albedo");if(source==null||source.width!=first.width||source.height!=first.height||source.format!=first.format)throw new InvalidOperationException("Combat decal import mismatch: "+names[i]);for(int mip=0;mip<source.mipmapCount;mip++)Graphics.CopyTexture(source,0,mip,textures,i,mip);}
            var firstNormal=Resources.Load<Texture2D>("MassiveArmy/CombatArt/"+names[0]+"Normal");
            normals=new Texture2DArray(firstNormal.width,firstNormal.height,names.Length,firstNormal.format,true,true){name="Combat decal tangent normals",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=2};normals.Apply(false,true);
            for(int i=0;i<names.Length;i++){var source=Resources.Load<Texture2D>("MassiveArmy/CombatArt/"+names[i]+"Normal");if(source==null||source.width!=firstNormal.width||source.height!=firstNormal.height||source.format!=firstNormal.format)throw new InvalidOperationException("Combat normal import mismatch: "+names[i]);for(int mip=0;mip<source.mipmapCount;mip++)Graphics.CopyTexture(source,0,mip,normals,i,mip);}
            for(int i=0;i<emitters.Length;i++){
                var go=new GameObject(i<SparkEmitters?"Pooled impact sparks":"Pooled burst and explosion");go.transform.SetParent(root.transform,false);var fx=go.AddComponent<VisualEffect>();fx.initialEventName="Dormant";
                fx.visualEffectAsset=Resources.Load<VisualEffectAsset>("MassiveArmy/CombatArt/"+(i<SparkEmitters?"CombatSparks":i<OrdinaryEnd?"CombatBurst":"NuclearBurst"));if(fx.visualEffectAsset==null)throw new InvalidOperationException("Missing combat VFX Graph");
                emitters[i]=new Emitter{effect=fx,attributes=fx.CreateVFXEventAttribute()};go.SetActive(false);
            }
            var surface=Resources.Load<CombatSurfaceLibrary>("MassiveArmy/CombatArt/SurfaceLibrary");
            coverageBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,surface.alphaCoverage.Length,4);coverageBuffer.SetData(surface.alphaCoverage);
            craterNeutral=surface.normalNeutral[0];
            RenderTexture Atlas(string name){var rt=new RenderTexture(AtlasResolution,AtlasResolution,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){name=name,enableRandomWrite=true,useMipMap=true,autoGenerateMips=false,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Repeat};rt.Create();return rt;}
            craterColour=Atlas("Permanent low resolution crater colour");craterNormal=Atlas("Permanent low resolution crater relief");
            shader.SetInt("_AtlasResolution",AtlasResolution);int clearAtlas=shader.FindKernel("ClearCraterAtlas");shader.SetTexture(clearAtlas,"_CraterColourWrite",craterColour);shader.SetTexture(clearAtlas,"_CraterNormalWrite",craterNormal);shader.Dispatch(clearAtlas,AtlasResolution/8,AtlasResolution/8,1);craterColour.GenerateMips();craterNormal.GenerateMips();
            for(int i=0;i<craterSlots.Length;i++)craterSlots[i]=-1;
            battle.Field.BindCraterAtlas(craterColour,craterNormal);
            battle.Field.BindCombatDecals(index,textures,normals,stampBuffer,stampSlots,coverageBuffer,surface.normalNeutral,resolution);battle.OnCombatVisual+=Receive;
        }
        void Receive(CombatVisualEvent e)
        {
            if(e.kind==1){dirty=true;if(battle.Settings.vfx)cameraRig?.ShakeFromBlast(new Vector2(e.positionSize.x,e.positionSize.z),e.positionSize.w);}
            if(!battle.Settings.vfx)return;
            if(e.kind>1){
                int slot=nextOrdinary++;if(nextOrdinary>=DecalCapacity)nextOrdinary=64;
                float size=Mathf.Clamp(e.positionSize.w,e.kind==1?48:1.8f,e.kind==1?400:64)*(e.kind==2||e.kind==3?2:e.kind==5?3:1),life=e.kind==1?6000:e.kind==2?450:e.kind==3?900:e.kind==4?1800:e.kind==5?1500:2400;
                var p=battle.Field.Canonical(new Vector2(e.positionSize.x,e.positionSize.z));float angle=e.Angle;
                stamps[slot]=new Stamp{positionSize=new Vector4(p.x,battle.Field.Height(p),p.y,size),appearance=new Vector4(e.kind-1,Mathf.Cos(angle),Mathf.Sin(angle),0),timing=new Vector4(clock,life,e.team==uint.MaxValue?1:0,++sequence)};
                dirty=indexDirty=true;
            }
            Burst(e);
        }
        void Burst(CombatVisualEvent e)
        {
            if(e.team!=uint.MaxValue&&battle.Settings.baseBuilding&&!battle.Settings.spectatorMode&&!battle.IsVisible(new Vector2(e.positionSize.x,e.positionSize.z),0,true))return;
            if(!GeometryUtility.TestPlanesAABB(planes,new Bounds(battle.Field.RenderPosition(e.Position,renderFocus),Vector3.one*(e.kind==1?1000:120))))return;
            // Strategic bursts have dedicated emitters; saturated ordinary combat drops cosmetics only.
            int start=e.kind==1?OrdinaryEnd:e.kind==0?0:SparkEmitters,end=e.kind==1?EmitterCapacity:e.kind==0?SparkEmitters:OrdinaryEnd;
            if(e.kind==1){int chosen=start;float oldest=float.MaxValue;for(int i=start;i<end;i++){var item=emitters[i];float remaining=item.preview?item.previewUntil-Time.unscaledTime:item.until-clock;if(remaining<oldest){oldest=remaining;chosen=i;}}start=chosen;end=chosen+1;}
            for(int i=start;i<end;i++)if(e.kind==1||(emitters[i].preview?emitters[i].previewUntil<=Time.unscaledTime:emitters[i].until<=clock)){
                var item=emitters[i];float lifetime=e.kind==1?65:e.kind==0?.65f:e.kind<=3?.4f:1.4f;
                item.bounds=e.kind==1?1600*Mathf.Clamp(e.positionSize.w/144,.33f,2.8f):120;float duration=e.kind==1?70:e.kind==0?lifetime:4.5f;
                item.position=e.Position;item.until=clock+duration;item.preview=e.team==uint.MaxValue;item.previewUntil=Time.unscaledTime+duration;var fx=item.effect;fx.gameObject.SetActive(true);fx.transform.position=battle.Field.RenderPosition(item.position,renderFocus);
                fx.transform.localScale=Vector3.one*(e.kind==1?Mathf.Clamp(e.positionSize.w/144,.33f,2.8f):1);
                fx.Reinit();item.attributes.SetVector3("position",Vector3.zero);item.attributes.SetVector3("velocity",Vector3.up*(e.kind==1?24:e.kind<=3?1:8));
                item.attributes.SetVector3("color",e.kind==2?new Vector3(.4f,.012f,.01f):e.kind==3?new Vector3(.3f,.01f,.006f):new Vector3(2,.8f,.08f));
                item.attributes.SetFloat("size",e.kind==1?Mathf.Clamp(e.positionSize.w/144,.33f,2.8f):e.kind==0?1:e.kind<=3?.2f:Mathf.Clamp(e.positionSize.w*.12f,.7f,3));item.attributes.SetFloat("lifetime",lifetime);
                fx.SendEvent("OnPlay",item.attributes);if(e.kind==1)lastNuclear=i;BurstsStarted++;return;
            }DroppedBursts++;
        }
        public void Update(Camera camera)
        {
            cameraRig??=camera.GetComponent<RtsCamera>();
            if(!battle.PresentationPaused)clock+=Mathf.Min(Time.unscaledDeltaTime,.1f);
            BakeNewCraters();
            var focus=WorldTopology.ViewFocus(camera);bool rebased=focus!=renderFocus;renderFocus=focus;var view=camera.projectionMatrix*camera.worldToCameraMatrix;
            // Camera changes recull immediately. A stationary view only needs the fog/lifetime refresh.
            float visibilityNow=Time.unscaledTime;bool recull=!hasView||view!=lastView||lastEnabled!=battle.Settings.vfx||dirty||visibilityNow>=nextVisibility;
            if(recull){hasView=true;lastView=view;lastEnabled=battle.Settings.vfx;nextVisibility=visibilityNow+.1f;GeometryUtility.CalculateFrustumPlanes(camera,planes);CacheCraters(camera,focus);int active=0,visible=0;visibleCraters=0;
            for(int i=0;i<stamps.Length;i++){
                var stamp=stamps[i];bool alive=stamp.timing.y>0&&clock-stamp.timing.x<stamp.timing.y;if(alive)active++;
                bool show=false;if(battle.Settings.vfx&&alive){var centre=battle.Field.RenderPosition(new Vector3(stamp.positionSize.x,stamp.positionSize.y,stamp.positionSize.z),focus);
                show=GeometryUtility.TestPlanesAABB(planes,new Bounds(centre,new Vector3(stamp.positionSize.w*1.5f,40,stamp.positionSize.w*1.5f)))&&(stamp.timing.z>.5||!battle.Settings.baseBuilding||battle.Settings.spectatorMode||battle.IsVisible(new Vector2(stamp.positionSize.x,stamp.positionSize.z),0,true));}
                if(show){visible++;if(i<64)visibleCraters++;}float flag=show?1:0;if(stamp.appearance.w!=flag){stamp.appearance.w=flag;stamps[i]=stamp;dirty=true;}
            }
            ActiveDecals=active;VisibleDecals=visible;}
            if(dirty){stampBuffer.SetData(stamps);dirty=false;}
            if(indexDirty){
                RebuildTileLists();indexDirty=false;
            }
            battle.Field.SetCombatDecalVisibility(VisibleDecals,clock,visibleCraters);
            // Close views need no additional terrain samples when all history fits
            // the detail pool. The atlas still covers map views and older/overflow sites.
            bool mapView=camera.orthographic&&camera.orthographicSize>=battle.Field.Size*.25f;
            battle.Field.ShowCraterAtlas(battle.Settings.vfx&&StoredCraters>0&&(StoredCraters>64||mapView));
            foreach(var item in emitters){var fx=item.effect;bool active=fx.gameObject.activeSelf;bool alive=item.preview?Time.unscaledTime<item.previewUntil:item.until>clock;if(!alive||!battle.Settings.vfx){if(active)fx.gameObject.SetActive(false);continue;}
                bool freeze=battle.PresentationPaused&&!item.preview;if(item.paused!=freeze){item.paused=freeze;fx.pause=item.paused;}
                var position=battle.Field.RenderPosition(item.position,focus);if(rebased)fx.transform.position=position;bool show=GeometryUtility.TestPlanesAABB(planes,new Bounds(position+Vector3.up*(item.bounds>120?200:0),Vector3.one*item.bounds));if(active!=show)fx.gameObject.SetActive(show);}
        }
        void BakeNewCraters()
        {
            if(bakedCraters>=StoredCraters)return;int kernel=shader.FindKernel("BakeCrater");
            shader.SetFloat("_WorldSize",battle.Field.Size);shader.SetTexture(kernel,"_CraterColourWrite",craterColour);shader.SetTexture(kernel,"_CraterNormalWrite",craterNormal);shader.SetTexture(kernel,"_CraterArt",textures);shader.SetTexture(kernel,"_CraterNormals",normals);shader.SetVector("_BakeNeutral",craterNeutral);
            for(;bakedCraters<StoredCraters;bakedCraters++){var c=battle.Field.Craters[bakedCraters];float cell=battle.Field.Size/AtlasResolution,extent=c.width*.72f;
                int x=Mathf.FloorToInt((c.position.x-extent+battle.Field.Size*.5f)/cell),z=Mathf.FloorToInt((c.position.y-extent+battle.Field.Size*.5f)/cell),size=Mathf.CeilToInt(extent*2/cell)+2;
                shader.SetInts("_BakeOrigin",x,z);shader.SetInts("_BakeSize",size,size);shader.SetVector("_BakePosition",new Vector4(c.position.x,0,c.position.y,c.width));shader.SetVector("_BakeRotation",new Vector4(Mathf.Cos(c.angle),Mathf.Sin(c.angle),0,0));shader.Dispatch(kernel,(size+7)/8,(size+7)/8,1);}
            craterColour.GenerateMips();craterNormal.GenerateMips();dirty=true;
        }
        void CacheCraters(Camera camera,Vector3 focus)
        {
            // At map scale the atlas already has almost one texel per screen pixel.
            // Release detailed slots without scanning the entire world's impact history.
            if(camera.orthographic&&camera.orthographicSize>=battle.Field.Size*.25f){for(int slot=0;slot<64;slot++)if(craterSlots[slot]>=0){craterSlots[slot]=-1;stamps[slot]=default;dirty=indexDirty=true;}return;}
            var centre=new Vector2(focus.x,focus.z);float radius=camera.orthographic?camera.orthographicSize*(camera.aspect+1/Mathf.Max(.1f,Mathf.Abs(camera.transform.forward.y)))+battle.Field.MaximumHeight+400:camera.farClipPlane;
            battle.Field.QueryCraters(centre,radius,craterCandidates);
            for(int i=craterCandidates.Count-1;i>=0;i--){var c=battle.Field.Craters[craterCandidates[i]];
                bool show=battle.Settings.vfx&&GeometryUtility.TestPlanesAABB(planes,new Bounds(battle.Field.RenderPosition(new Vector3(c.position.x,battle.Field.Height(c.position),c.position.y),focus),new Vector3(c.width*1.5f,40,c.width*1.5f)))&&(c.ignoreFog||!battle.Settings.baseBuilding||battle.Settings.spectatorMode||battle.IsVisible(c.position,0,true));
                if(!show)craterCandidates.RemoveAt(i);}
            craterCandidates.Sort((a,b)=>battle.Field.Delta(centre,battle.Field.Craters[a].position).sqrMagnitude.CompareTo(battle.Field.Delta(centre,battle.Field.Craters[b].position).sqrMagnitude));
            if(craterCandidates.Count>64)craterCandidates.RemoveRange(64,craterCandidates.Count-64);
            for(int slot=0;slot<64;slot++)if(craterSlots[slot]>=0&&!craterCandidates.Contains(craterSlots[slot])){craterSlots[slot]=-1;stamps[slot]=default;dirty=indexDirty=true;}
            foreach(int id in craterCandidates){if(Array.IndexOf(craterSlots,id)>=0)continue;int slot=Array.IndexOf(craterSlots,-1);var c=battle.Field.Craters[id];craterSlots[slot]=id;
                stamps[slot]=new Stamp{positionSize=new Vector4(c.position.x,battle.Field.Height(c.position),c.position.y,c.width),appearance=new Vector4(0,Mathf.Cos(c.angle),Mathf.Sin(c.angle),1),timing=new Vector4(0,float.MaxValue,c.ignoreFog?1:0,id+1)};dirty=indexDirty=true;}
        }
        void RebuildTileLists()
        {
            // Only bounded presentation records are indexed on the CPU, never army state.
            // Newest first permits front-to-back alpha composition and early opaque rejection.
            foreach(var list in tiles.Values){list.Clear();freeTiles.Push(list);}tiles.Clear();ordered.Clear();
            for(int i=0;i<stamps.Length;i++)if(stamps[i].timing.y>0&&clock-stamps[i].timing.x<stamps[i].timing.y)ordered.Add(i);
            ordered.Sort((a,b)=>stamps[b].timing.w.CompareTo(stamps[a].timing.w));
            foreach(int slot in ordered){var stamp=stamps[slot];float extent=stamp.positionSize.w*.72f;
                int x0=Mathf.FloorToInt((stamp.positionSize.x-extent+battle.Field.Size*.5f)/16),z0=Mathf.FloorToInt((stamp.positionSize.z-extent+battle.Field.Size*.5f)/16);
                int x1=Mathf.FloorToInt((stamp.positionSize.x+extent+battle.Field.Size*.5f)/16),z1=Mathf.FloorToInt((stamp.positionSize.z+extent+battle.Field.Size*.5f)/16);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){int key=WorldTopology.Cell(x,resolution)+WorldTopology.Cell(z,resolution)*resolution+(slot<64?0:resolution*resolution);
                    if(!tiles.TryGetValue(key,out var list)){list=freeTiles.Count>0?freeTiles.Pop():new List<uint>(4);tiles.Add(key,list);}list.Add((uint)slot);}
            }
            int updates=0,count=1;
            foreach(int key in previousTiles)if(!tiles.ContainsKey(key))tileUpdates[updates++]=new TileUpdate{cell=(uint)key,offset=0};previousTiles.Clear();
            foreach(var tile in tiles){int start=count;tileIndices[count++]=(uint)tile.Value.Count;foreach(uint slot in tile.Value)tileIndices[count++]=slot;
                tileUpdates[updates++]=new TileUpdate{cell=(uint)tile.Key,offset=(uint)start};previousTiles.Add(tile.Key);}
            stampSlots.SetData(tileIndices,0,0,count);tileBuffer.SetData(tileUpdates,0,0,updates);
            int kernel=shader.FindKernel("StampDecals");shader.SetInt("_StampCount",updates);shader.SetBuffer(kernel,"_TileUpdates",tileBuffer);shader.SetTexture(kernel,"_CombatIndexWrite",index);shader.Dispatch(kernel,(updates+63)/64,1,1);
        }
        public void Dispose(){battle.OnCombatVisual-=Receive;battle.Field.SetCombatDecalVisibility(0,clock);battle.Field.ShowCraterAtlas(false);foreach(var emitter in emitters)emitter?.attributes?.Dispose();stampBuffer.Dispose();stampSlots.Dispose();tileBuffer.Dispose();coverageBuffer.Dispose();index.Release();craterColour.Release();craterNormal.Release();ArmyLifetime.Destroy(craterColour);ArmyLifetime.Destroy(craterNormal);ArmyLifetime.Destroy(index);ArmyLifetime.Destroy(textures);ArmyLifetime.Destroy(normals);ArmyLifetime.Destroy(shader);ArmyLifetime.Destroy(root);}
    }
}
