using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    public sealed partial class GpuBattle
    {
#if UNITY_EDITOR
        public void SetCombatValidationDamage(uint[] values){if(values.Length>256)throw new InvalidOperationException("Small validation fixture only");damage.SetData(values);}
        public void ResolveCombatValidationMissile(Camera camera,ProjectileState missile)
            =>ResolveCombatValidationMissiles(camera,new[]{missile});
        public void ResolveCombatValidationMissiles(Camera camera,ProjectileState[] values)
        {
            if(values.Length>32)throw new ArgumentException("Bounded missile fixture only");
            missiles.SetData(values);cmd.Clear();Parameters(camera);CombatVisualParameters(rtsShader,camera);RtsDispatch("MissileOperations",32);Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
        }
#endif
    }
    public sealed partial class BattleApp
    {
        internal void WriteCombatBenchmarkEvidence(string path)
        {
            var fx=armyRenderer.CombatEffects;
            File.WriteAllText(path,$"{{\"gpuEvents\":{Battle.VisualEventsRead},\"activeDecals\":{fx.ActiveDecals},\"visibleDecals\":{fx.VisibleDecals},\"burstsStarted\":{fx.BurstsStarted},\"droppedBursts\":{fx.DroppedBursts}}}");
        }
        [Serializable] sealed class CombatValidationReport {public string result;public int checks,events,particles,decals,bursts,dropped;public string[] failures;}
        public IEnumerator ValidateCombatPresentation(string folder)
        {
#if UNITY_EDITOR
            var failures=new List<string>();int checks=0;void Check(bool valid,string label){checks++;if(!valid)failures.Add(label);}
            Directory.CreateDirectory(folder);Paused=true;var home=Base.BasePosition(0);rig.FocusBase(home);
            float groundBefore=field.Height(home),blockedBefore=Base.NavigationCost(home,2);
            var config=new BattleSettings{baseBuilding=true,capacity=256,activeUnits=256,ambientZombies=0,spectatorMode=true,enemyAI=false,movement=false,targeting=false,combat=false,navigation=false,logistics=false,projectileCapacity=128,worldSize=field.Size};
            var roster=new UnitArchetype[4];for(int i=0;i<4;i++)roster[i]=UnitArchetype.Tank;roster[0].movement=0;roster[1].movement=2;roster[2].movement=1;roster[3].movement=0;
            var events=new List<CombatVisualEvent>();var fixture=new GpuBattle(config,field,roster);fixture.OnCombatVisual=events.Add;
            var units=new UnitState[256];for(int i=0;i<4;i++){Vector2 p=field.Canonical(home+new Vector2(i*8-12,45));units[i]=new UnitState{position=p,previous=p,health=1000,flags=1u|(i==3?262144u:0),packed=(uint)((i<<8)|(i==3?2:0)),target=uint.MaxValue,group=uint.MaxValue};}
            fixture.SetValidationUnits(units);var damage=new uint[256];for(int i=0;i<4;i++)damage[i]=2000;fixture.SetCombatValidationDamage(damage);
            Vector2 impact=home+new Vector2(30,40);fixture.Projectiles.SetData(new[]{new ProjectileState{position=new Vector3(impact.x,field.Height(impact)+1,impact.y),velocity=Vector3.right*20,lifetime=.01f,alive=1,owner=uint.MaxValue,target=uint.MaxValue}});
            fixture.Update(View,.04f,new uint[]{3,3,6,6},false);
            fixture.ResolveCombatValidationMissile(View,new ProjectileState{position=new Vector3(impact.x,field.Height(impact),impact.y),velocity=new Vector3(impact.x,field.Height(impact),impact.y),lifetime=.1f,alive=1,damage=5000,target=120});
            double deadline=Time.realtimeSinceStartupAsDouble+8;while(events.Count<6&&Time.realtimeSinceStartupAsDouble<deadline){fixture.Update(View,0,new uint[]{3,3,6,6},true);yield return null;}
            foreach(uint kind in new uint[]{0,1,2,3,4,5})Check(events.Any(e=>e.kind==kind),"Actual GPU event emitted for kind "+kind);
            Check(events.Count(e=>e.kind>=2)==4,"Death events occur exactly once per killed fixture unit");Check(events.Count<=289,"GPU event output stays bounded");int previewEvents=events.Count;
            Array.Clear(units,0,units.Length);units[4]=new UnitState{position=impact,previous=impact,health=1000,flags=1,packed=1,target=uint.MaxValue,group=uint.MaxValue};fixture.SetValidationUnits(units);
            fixture.Projectiles.SetData(new[]{new ProjectileState{position=new Vector3(impact.x,field.Height(impact)+1,impact.y),velocity=Vector3.right,lifetime=1,alive=1,owner=0,target=4,team=0,damage=125}});fixture.Update(View,.04f,new uint[]{3,3,6,6},false);
            uint health=uint.MaxValue;bool read=false;AsyncGPUReadback.Request(fixture.Units,UnitState.Stride,4*UnitState.Stride,r=>{if(!r.hasError)health=r.GetData<UnitState>()[0].health;read=true;});while(!read)yield return null;
            Check(health==875,"Projectile SRV aliases preserve real shell damage");deadline=Time.realtimeSinceStartupAsDouble+8;while(events.Count<previewEvents+1&&Time.realtimeSinceStartupAsDouble<deadline){fixture.Update(View,0,new uint[]{3,3,6,6},true);yield return null;}Check(events.Skip(previewEvents).Any(e=>e.kind==0),"Real shell hit emits an impact event");int volleyStart=events.Count;
            var volley=new ProjectileState[32];for(int i=0;i<volley.Length;i++)volley[i]=new ProjectileState{position=new Vector3(impact.x,field.Height(impact),impact.y),velocity=new Vector3(impact.x,field.Height(impact),impact.y),lifetime=.1f,alive=1,damage=1,target=120};
            fixture.ResolveCombatValidationMissiles(View,volley);deadline=Time.realtimeSinceStartupAsDouble+8;while(events.Count<volleyStart+32&&Time.realtimeSinceStartupAsDouble<deadline){fixture.Update(View,0,new uint[]{3,3,6,6},true);yield return null;}
            Check(events.Skip(volleyStart).Count(e=>e.kind==1)==32,"All 32 simultaneous nuclear detonations retain strategic events");
            Check(events.Skip(volleyStart).Select(e=>e.rotation).Distinct().Count()>24,"Simultaneous crater events receive varied stable rotations");
            int backlogStart=events.Count;for(int wave=0;wave<3;wave++)fixture.ResolveCombatValidationMissiles(View,volley);
            deadline=Time.realtimeSinceStartupAsDouble+8;while(events.Count<backlogStart+96&&Time.realtimeSinceStartupAsDouble<deadline){fixture.Update(View,0,new uint[]{3,3,6,6},true);yield return null;}
            Check(events.Skip(backlogStart).Count(e=>e.kind==1)==96,"Strategic GPU backlog retains three volleys before one readback");
            Array.Clear(units,0,units.Length);units[4]=new UnitState{position=impact,previous=impact,health=1000,flags=1,packed=1,target=uint.MaxValue,group=uint.MaxValue};fixture.SetValidationUnits(units);fixture.SetCombatValidationDamage(new uint[256]);fixture.ResetValidationFreeSlots(units);
            int testStart=events.Count;fixture.RequestTestNuke(impact);fixture.Update(View,0,new uint[]{3,3,6,6},true);read=false;health=uint.MaxValue;AsyncGPUReadback.Request(fixture.Units,UnitState.Stride,4*UnitState.Stride,r=>{if(!r.hasError)health=r.GetData<UnitState>()[0].health;read=true;});while(!read)yield return null;
            Check(health==0,"Cursor-test nuke applies real GPU damage while paused");deadline=Time.realtimeSinceStartupAsDouble+8;while(!events.Skip(testStart).Any(e=>e.kind==1)&&Time.realtimeSinceStartupAsDouble<deadline){fixture.Update(View,0,new uint[]{3,3,6,6},true);yield return null;}
            Check(events.Skip(testStart).Any(e=>e.kind==1&&e.team==uint.MaxValue&&field.Distance(new Vector2(e.positionSize.x,e.positionSize.z),impact)<.01f),"Test nuke emits the correct cursor position and preview marker");Check(fixture.TestNukesDetonated==1,"One test request creates one detonation");
            Vector2 hiddenImpact=field.Canonical(home+Vector2.left*350);float hiddenGround=field.Height(hiddenImpact);
            fixture.Settings.vfx=false;fixture.OnCombatVisual=null;fixture.RequestTestNuke(hiddenImpact);fixture.Update(View,0,new uint[]{3,3,6,6},true);
            deadline=Time.realtimeSinceStartupAsDouble+8;while(field.Height(hiddenImpact)>hiddenGround-2&&Time.realtimeSinceStartupAsDouble<deadline){fixture.Update(View,0,new uint[]{3,3,6,6},true);yield return null;}
            Check(field.Height(hiddenImpact)<hiddenGround-2,"Real off-camera nuke deforms ground with VFX disabled and no presentation subscriber");fixture.Dispose();
            var rocket=Resources.Load<Mesh>("MassiveArmy/CombatArt/Rocket");Check(rocket!=null&&rocket.vertexCount>100&&rocket.bounds.size.y>15,"Rocket mesh has body, pointed nose and fins");
            Check(rocket!=null&&Vector3.Dot(new Vector3(rocket.vertices[0].x,0,rocket.vertices[0].z),rocket.normals[0])>0&&rocket.normals[rocket.vertexCount-1].y<-.9f,"Rocket body and cap face outward");
            // Replay the real fixture events at readable spaced positions for render validation.
            for(int i=0;i<previewEvents;i++){var e=events[i];Vector2 p=home+new Vector2((i%3-1)*36,(i/3)*38+20);e.positionSize=new Vector4(p.x,field.Height(p)+1,p.y,e.kind==1?80:e.kind>=4?12:e.kind>=2?4:2);if(e.kind==1)field.QueueCrater(p,80,e.Angle,true);Battle.OnCombatVisual?.Invoke(e);}
            Battle.ReportStructureDestruction(new Vector3(home.x-65,field.Height(home+Vector2.left*65),home.y),24);
            bool combat=settings.combat,movement=settings.movement,targeting=settings.targeting,ai=settings.enemyAI;settings.combat=settings.movement=settings.targeting=settings.enemyAI=false;Paused=false;
            int particles=0;bool earlyCapture=false;double particleStart=Time.realtimeSinceStartupAsDouble,particleDeadline=particleStart+1.2;while(Time.realtimeSinceStartupAsDouble<particleDeadline){yield return null;particles=Math.Max(particles,armyRenderer.CombatEffects.AliveParticles);if(!earlyCapture&&Time.realtimeSinceStartupAsDouble-particleStart>.2){earlyCapture=true;ScreenCapture.CaptureScreenshot(Path.GetFullPath(folder+"/sparks-dust-preview.png"));}}Check(particles>0,"VFX Graph actually spawns GPU particles");
            var zombieMark=armyRenderer.CombatEffects.DecalForValidation(2);var humanMark=armyRenderer.CombatEffects.DecalForValidation(3);Check(zombieMark.x==8&&humanMark.x==8,"Zombie and humanoid marks render at twice the fixture width");
            Check(zombieMark.y==450&&humanMark.y==900&&armyRenderer.CombatEffects.DecalForValidation(1).y==float.MaxValue&&armyRenderer.CombatEffects.DecalForValidation(4).y==1800&&armyRenderer.CombatEffects.DecalForValidation(5).y==1500&&armyRenderer.CombatEffects.DecalForValidation(6).y==2400,"Crater marks are permanent and other remains retain extended lifetimes");
            Check(armyRenderer.CombatEffects.DestructionSystemsForValidation()==2,"Destruction graph contains separate flame and dust systems");
            File.WriteAllText(folder+"/vfx-diagnostics.txt",armyRenderer.CombatEffects.Diagnostics);
            Check(armyRenderer.CombatEffects.BurstsStarted>0,"Impact/death/strategic events start pooled bursts");
            Check(armyRenderer.CombatEffects.ActiveDecals>=6,"Crater and all remains categories persist as terrain decals");
            Check(armyRenderer.CombatEffects.ActiveDecals<=CombatPresentation.DecalCapacity,"Ground decal pool stays bounded");
            Check(armyRenderer.CombatEffects.DecalForValidation(5).x==36,"Vehicle wreck artwork uses three times the fixture width");
            Check(field.TerrainRevision>0,"Nuclear events deform the shared terrain");
            Check(UnityEngine.Object.FindObjectsByType<UnityEngine.VFX.VisualEffect>(FindObjectsInactive.Include).Length==CombatPresentation.EmitterCapacity,"VFX emitter pool stays fixed");
            // Capture warmed graphs without the HUD covering the short impact burst.
            uiRoot.style.opacity=0;rig.FocusBase(home+Vector2.up*50);Vector2 sparkPoint=home+new Vector2(-35,70);Battle.OnCombatVisual?.Invoke(new CombatVisualEvent{kind=0,positionSize=new Vector4(sparkPoint.x,field.Height(sparkPoint)+1,sparkPoint.y,1)});
            yield return new WaitForSecondsRealtime(.16f);ScreenCapture.CaptureScreenshot(Path.GetFullPath(folder+"/sparks-dust-preview.png"));yield return new WaitForEndOfFrame();yield return null;uiRoot.style.opacity=1;rig.FocusBase(home);
            Paused=true;var displayMissiles=new ProjectileState[32];Vector2 rocketPoint=home+new Vector2(55,65);displayMissiles[0]=new ProjectileState{position=new Vector3(rocketPoint.x,field.Height(rocketPoint)+20,rocketPoint.y),velocity=new Vector3(rocketPoint.x+100,0,rocketPoint.y),lifetime=6,alive=1};Battle.SetValidationMissiles(displayMissiles);yield return null;
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(folder+"/combat-preview.png"));yield return new WaitForEndOfFrame();yield return null;Battle.SetValidationMissiles(new ProjectileState[32]);
            rig.FocusSector(field.Canonical(home+Vector2.one*(field.Size*.35f)));yield return null;Check(armyRenderer.CombatEffects.VisibleDecals==0,"Moving the camera immediately culls distant ground marks");
            rig.FocusBase(home);yield return null;Check(armyRenderer.CombatEffects.VisibleDecals>=6,"Returning the camera immediately restores surviving ground marks");
            settings.vfx=false;yield return null;Check(armyRenderer.CombatEffects.VisibleDecals==0&&UnityEngine.Object.FindObjectsByType<UnityEngine.VFX.VisualEffect>(FindObjectsInactive.Exclude).Length==0,"Effects toggle immediately hides marks and emitter objects");
            settings.vfx=true;yield return null;Check(armyRenderer.CombatEffects.VisibleDecals>=6,"Effects toggle restores surviving marks while paused");
            Vector3 cursorWorld=new Vector3(home.x+25,field.Height(home+Vector2.right*25),home.y);Vector2 cursorScreen=View.WorldToScreenPoint(cursorWorld);int beforeTest=Battle.TestNukesDetonated;Check(TryTestNukeAtCursor(cursorScreen),"N-key cursor handler accepts battlefield ground");yield return null;yield return null;Check(Battle.TestNukesDetonated==beforeTest+1&&field.Distance(Battle.LastTestNuke,new Vector2(cursorWorld.x,cursorWorld.z))<1,"Cursor handler detonates at the ground raycast position");Check(!TryTestNukeAtCursor(new Vector2(5,Screen.height-5)),"N-key cursor handler ignores HUD controls");
            // Verify a strategic event still gets a crater after ordinary events saturate the budget.
            field.QueueCrater(home,144,0);field.ApplyQueuedCraters(Battle.Navigation);
            Check(field.Height(home)<groundBefore-2,"Crater height map lowers the actual ground surface");
            Check(Base.NavigationCost(home,2)==blockedBefore,"Crater updates preserve existing command-centre navigation obstacles");
            float once=field.Height(home);field.QueueCrater(home,144,0);field.ApplyQueuedCraters(Battle.Navigation);
            Check(Mathf.Abs(field.Height(home)-once)<.001f,"Repeated impacts do not infinitely deepen the same crater");
            Vector2 rimSite=field.Canonical(home+new Vector2(400,500));float rimBefore=field.Height(rimSite+Vector2.right*(.39f*144));field.QueueCrater(rimSite,144,0,true);field.ApplyQueuedCraters(Battle.Navigation);
            Check(field.Height(rimSite+Vector2.right*(.39f*144))>rimBefore+2,"Crater edge forms a broad raised ridge above original ground");
            using(var heightProbe=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4)){
                var heightShader=Resources.Load<ComputeShader>("MassiveArmy/CombatPresentation");int kernel=heightShader.FindKernel("ValidateHeight");heightShader.SetFloat("_WorldSize",field.Size);heightShader.SetInt("_NavRes",field.Resolution);heightShader.SetTexture(kernel,"_Terrain",field.Terrain);heightShader.SetVector("_CheckPosition",new Vector4(home.x,home.y,0,0));heightShader.SetBuffer(kernel,"_CheckHeight",heightProbe);heightShader.Dispatch(kernel,1,1,1);
                read=false;float gpuHeight=0;AsyncGPUReadback.Request(heightProbe,r=>{if(!r.hasError)gpuHeight=r.GetData<float>()[0];read=true;});while(!read)yield return null;
                Check(Mathf.Abs(gpuHeight-field.Height(home))<.001f,"GPU unit height sampling agrees with CPU picking and terrain height after deformation");
            }
            for(int i=0;i<700;i++)Battle.OnCombatVisual?.Invoke(new CombatVisualEvent{kind=2,positionSize=new Vector4(home.x,field.Height(home),home.y,2)});
            Battle.OnCombatVisual?.Invoke(new CombatVisualEvent{kind=1,positionSize=new Vector4(home.x+60,field.Height(home),home.y,80)});yield return null;
            Check(armyRenderer.CombatEffects.ActiveDecals<=512,"Burst of deaths cannot grow the decal pool");Check(armyRenderer.CombatEffects.DroppedBursts>0,"Overload drops cosmetic bursts instead of adding emitters");
            int oldId=field.Craters.Count;Vector2 oldSite=field.Canonical(home+new Vector2(700,700));field.QueueCrater(oldSite,144,.73f,true);
            for(int i=0;i<96;i++)field.QueueCrater(field.Canonical(home+new Vector2((i%12-6)*200,(i/12-4)*200)),144,i*.37f,true);
            Check(field.Craters.Count==oldId+97,"More than 64 later impacts retain every crater record");yield return null;
            rig.FocusBase(oldSite);yield return null;Check(armyRenderer.CombatEffects.CraterCachedForValidation(oldId),"Old crater regains a detailed decal after pool eviction");
            var atlas=armyRenderer.CombatEffects.CraterAtlasForValidation;read=false;float atlasAlpha=0;int px=Mathf.FloorToInt((oldSite.x/field.Size+.5f)*atlas.width),py=Mathf.FloorToInt((oldSite.y/field.Size+.5f)*atlas.height);
            AsyncGPUReadback.Request(atlas,0,px,1,py,1,0,1,TextureFormat.RGBA32,r=>{if(!r.hasError)atlasAlpha=r.GetData<Color32>()[0].a/255f;read=true;});while(!read)yield return null;
            Check(atlasAlpha>.5f,"Permanent GPU atlas retains old crater colour independently of the detail pool");
            rig.FullMap();yield return null;Check(armyRenderer.CombatEffects.ActiveDecals<=512,"Whole-map crater view keeps the detail pool bounded");
            settings.vfx=false;int disabledId=field.Craters.Count;field.QueueCrater(oldSite+Vector2.right*150,144,1.3f,true);yield return null;settings.vfx=true;rig.FocusBase(oldSite+Vector2.right*150);yield return null;
            Check(armyRenderer.CombatEffects.CraterCachedForValidation(disabledId),"Impacts recorded with effects disabled regain decals when enabled");
            rig.FocusBase(oldSite);Vector3 focusBefore=rig.Focus;rig.ShakeFromBlast(oldSite,144);rig.UpdateForFrame();Check(rig.Focus==focusBefore,"Blast camera shake leaves the logical focus unchanged");
            Vector3 shaken=rig.transform.position;rig.ShakeFromBlast(field.Canonical(oldSite+Vector2.one*1500),144);rig.UpdateForFrame();Check(Vector3.Distance(shaken,rig.transform.position)<.01f,"Distant blasts do not add camera shake");
            var nukeEvent=new CombatVisualEvent{kind=1,team=uint.MaxValue,positionSize=new Vector4(oldSite.x,field.Height(oldSite),oldSite.y,144)};Battle.OnCombatVisual?.Invoke(nukeEvent);yield return null;yield return null;
            var nuclear=armyRenderer.CombatEffects.NuclearForValidation;var nuclearNames=new List<string>();nuclear.GetParticleSystemNames(nuclearNames);Check(nuclearNames.Count==5,"Nuclear graph has cloud cap, stem, covering dust, flash and shockwave systems");
            bool previousSpectator=settings.spectatorMode;settings.spectatorMode=true;bool previousFog=volumetricFog!=null&&volumetricFog.enabled;if(volumetricFog!=null)volumetricFog.enabled=false;rig.ResetOverview();rig.FocusSector(oldSite);uiRoot.style.opacity=0;yield return new WaitForSecondsRealtime(.5f);ScreenCapture.CaptureScreenshot(Path.GetFullPath(folder+"/nuclear-dust.png"));yield return new WaitForEndOfFrame();
            foreach(var effect in UnityEngine.Object.FindObjectsByType<UnityEngine.VFX.VisualEffect>(FindObjectsInactive.Exclude))if(effect.visualEffectAsset.name=="NuclearBurst")effect.Simulate(.1f,100);
            yield return null;yield return null;ScreenCapture.CaptureScreenshot(Path.GetFullPath(folder+"/nuclear-cloud-10s.png"));yield return new WaitForEndOfFrame();
            Check(nuclear.aliveParticleCount>=24,"Mushroom cap and stem remain alive beyond the original short burst");
            foreach(var effect in UnityEngine.Object.FindObjectsByType<UnityEngine.VFX.VisualEffect>(FindObjectsInactive.Exclude))if(effect.visualEffectAsset.name=="NuclearBurst")effect.Simulate(.1f,300);
            yield return null;yield return null;ScreenCapture.CaptureScreenshot(Path.GetFullPath(folder+"/nuclear-cloud-40s.png"));yield return new WaitForEndOfFrame();Check(nuclear.aliveParticleCount>=24,"Mushroom cloud lingers through forty seconds");uiRoot.style.opacity=1;settings.spectatorMode=previousSpectator;if(volumetricFog!=null)volumetricFog.enabled=previousFog;
            settings.combat=combat;settings.movement=movement;settings.targeting=targeting;settings.enemyAI=ai;
            var effects=armyRenderer.CombatEffects;File.WriteAllText(folder+"/validation.json",JsonUtility.ToJson(new CombatValidationReport{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray(),events=events.Count,particles=particles,decals=effects.ActiveDecals,bursts=effects.BurstsStarted,dropped=effects.DroppedBursts},true));
#else
            throw new InvalidOperationException("Editor validation only");
#endif
        }
    }
}
