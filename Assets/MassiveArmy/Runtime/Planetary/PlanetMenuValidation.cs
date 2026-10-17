using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary
{
    public sealed class PlanetMenuValidation : MonoBehaviour
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();}
        readonly Report report=new Report();string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-menu-validation")>=0){var host=new GameObject("Planet menu integration validation");DontDestroyOnLoad(host);host.AddComponent<PlanetMenuValidation>();}}
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);report.checks.Add(message);}
        IEnumerator Start(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-planet-output");output=i<0?Application.persistentDataPath:args[i+1];Directory.CreateDirectory(output);SceneManager.LoadScene("MassiveBattle");yield return null;yield return new WaitForSeconds(1);
            var run=Run();while(true){bool more;try{more=run.MoveNext();}catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);break;}if(!more)break;yield return run.Current;}
            File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);
        }
        IEnumerator Run()
        {
            var preset=Resources.Load<PlanetDefinition>("MassiveArmy/Planets/DefaultPlanet");Check(preset!=null&&preset.stampTerrain,"Saved default planet preset exists with terrain stamps enabled");var presetState=JsonUtility.ToJson(preset);
            var menu=FindAnyObjectByType<BattleApp>();Check(menu!=null&&menu.State=="menu","Original main menu loads from standalone scene list");menu.settings.ambientZombies=32;var originalRig=FindAnyObjectByType<BattleLightingRig>();var originalSun=originalRig.sun;var originalRotation=originalSun.transform.rotation;var originalShadows=originalSun.shadows;
            foreach(PlanetGameMode mode in Enum.GetValues(typeof(PlanetGameMode))){if(mode==PlanetGameMode.Rts)menu.StartBaseBattle(2048);else if(mode==PlanetGameMode.Swarm)menu.StartSwarmBattle(2048);else menu.StartTestBattle(2048);
                yield return new WaitForSeconds(3);var world=FindAnyObjectByType<PlanetDevelopment>();Check(world!=null&&world.Ready&&world.Base!=null&&world.sessionMode==mode,mode+" menu action launches a planetary base session");
                Check(world.definition.radius==120000&&PlanetDefinition.DefaultRadius==120000&&world.navigationResolution==128&&world.NavigationForValidation.Regional!=null&&world.StreamedTerrain!=null,mode+" launches the 120 km radius planet with streamed terrain and bounded tactical navigation");
                Check(world.definition!=preset&&world.definition.terrainSeed==menu.settings.seed&&world.definition.stampTerrain==(Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-legacy-terrain")<0||Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-stamp-terrain")>=0),mode+" uses a private preset copy with the selected match seed and terrain stamp setting");
                Check(world.LightingForValidation.Sun==originalSun,mode+" reuses the original scene sun and lighting settings");
                Check(menu.Battle==null,"Flat GPU battle is not allocated for "+mode);Check(world.Hud.Root.Q<Button>("tech")!=null&&world.Hud.Root.Q<Button>("attack-move")!=null,"Shared original HUD commands and technology entry present for "+mode);
                foreach(BuildingCategory category in Enum.GetValues(typeof(BuildingCategory))){world.Hud.ShowBuildingCategory(category);yield return null;Check(world.Hud.Root.Q<Button>("category-"+(int)category)!=null,mode+" construction category "+category);}
                Check(world.unitCount>=64,"Ambient zombie GPU slots spawn in "+mode);
                var records=new UnitState[world.unitCount];world.UnitsForValidation.GetData(records);int zombie=Array.FindIndex(records,u=>(u.packed&255)==2&&(u.flags&1)!=0);Check(zombie>=0,"GPU zombie team and alive records exist in "+mode);Check((records[zombie].flags&131072)==0,"Ambient zombies are distinct from scheduled wave identities in "+mode);var initial=records[zombie].position;yield return new WaitForSeconds(1);var moved=new UnitState[1];world.UnitsForValidation.GetData(moved,0,zombie,1);Check(CubeSphere.ArcDistance(CubeSphere.Decode(initial),CubeSphere.Decode(moved[0].position),world.definition.radius)>.1f,"GPU zombies follow the planet integration field in "+mode);

                if(mode==PlanetGameMode.Rts){
                    var saved=new UnitState[2];world.UnitsForValidation.GetData(saved,0,0,2);var pair=new UnitState[2];var spot=world.Base.Offset(new Vector2(0,100));pair[0]=new UnitState{position=CubeSphere.Encode(spot),previous=CubeSphere.Encode(spot),packed=0,health=1000,flags=5,target=uint.MaxValue};pair[1]=new UnitState{position=CubeSphere.Encode((spot+Vector3.Cross(Vector3.up,spot).normalized/world.definition.radius).normalized),packed=2u|(31u<<8),health=1,flags=5,target=uint.MaxValue};world.UnitsForValidation.SetData(pair,0,0,2);
                    yield return new WaitForSeconds(.5f);world.UnitsForValidation.GetData(pair,0,0,2);Check(pair[1].health==0&&(pair[1].flags&1)==0,"GPU combat applies simultaneous damage and clears dead zombie flags");Check(world.EncounterForValidation.DecalCountForValidation>0,"GPU death emits an original-art combat decal without CPU unit readback");int count=world.unitCount;Check(world.EncounterForValidation.Spawn(1,2,31,17)==1&&world.unitCount==count,"GPU free-slot allocator recycles dead slots without growing the high-water count");world.UnitsForValidation.SetData(saved,0,0,2);
                    // A bounded fixture at a distant pole verifies full strategic weapon reach.
                    var strategic=Vector3.up;var strategicTarget=(strategic+Vector3.right*(2100/world.definition.radius)).normalized;
                    pair[0]=new UnitState{position=CubeSphere.Encode(strategic),previous=CubeSphere.Encode(strategic),packed=(uint)world.Base.Catalog.TypeIndex("strategic_platform")<<8,health=1000,flags=13,target=uint.MaxValue};
                    pair[1]=new UnitState{position=CubeSphere.Encode(strategicTarget),previous=CubeSphere.Encode(strategicTarget),packed=2u|(31u<<8),health=1,flags=5,target=uint.MaxValue,group=17};world.UnitsForValidation.SetData(pair,0,0,2);
                    yield return new WaitForSeconds(.6f);world.UnitsForValidation.GetData(pair,0,0,2);Check(pair[1].health==0,"Strategic weapons retain 2.2 km acquisition and firing range on the 120 km radius planet");world.UnitsForValidation.SetData(saved,0,0,2);
                    var solar=world.Base.Buildings.First(b=>b.definition.id=="solar");float health=world.Base.Buildings.Sum(b=>b.health);var up=CubeSphere.Decode(solar.position);float radius=Mathf.Sqrt(solar.definition.footprintWidth*solar.definition.footprintWidth+solar.definition.footprintDepth*solar.definition.footprintDepth)*4;var outward=Vector3.ProjectOnPlane(up-world.Rig.FocusDirection,up).normalized;var attacker=new UnitState{position=CubeSphere.Encode((up+outward*(radius+10)/world.definition.radius).normalized),packed=2u|(31u<<8),health=100000,flags=1,target=uint.MaxValue,group=17};world.UnitsForValidation.SetData(new[]{attacker},0,1,1);yield return new WaitForSeconds(2);Check(world.Base.Buildings.Sum(b=>b.health)<health,"Zombie melee crosses inflated navigation margins and damages physical buildings");world.UnitsForValidation.SetData(saved,0,0,2);
                }
                if(mode==PlanetGameMode.Swarm){world.EncounterForValidation.Schedule.Advance(630);yield return new WaitForSeconds(2);Check(world.EncounterForValidation.Schedule.Active&&world.unitCount>=1064,"Original 10-minute preparation / warning schedule starts the 1000-zombie wave");Check(world.EncounterForValidation.WaveAliveForValidation>0&&world.EncounterForValidation.WaveAliveForValidation<1032,"Swarm clearance reads wave-only GPU alive counters");}
                if(mode==PlanetGameMode.AiTest){var garrison=new UnitState[world.unitCount];world.UnitsForValidation.GetData(garrison);
                    Check(world.unitCount>=96&&garrison.Any(u=>(u.packed&255)==0&&(u.flags&1)!=0)&&garrison.Any(u=>(u.packed&255)==1&&(u.flags&1)!=0)&&garrison.Any(u=>(u.packed&255)==2&&(u.flags&1)!=0),"AI test includes both regular faction garrisons and zombies");
                    Check(world.Base.Buildings.Any(b=>b.team==0&&b.definition.id=="command")&&world.Base.Buildings.Any(b=>b.team==1&&b.definition.id=="command"),"AI test retains an HQ for each faction alongside the new settlement infrastructure");
                    world.Paused=true;
                    Check(Vector3.Dot(world.Base.Homes[0],world.Base.Homes[1])<=-Mathf.Cos(31*Mathf.Deg2Rad),"AI bases occupy opposite hemispheres, at least 149 degrees apart (actual "+Vector3.Angle(world.Base.Homes[0],world.Base.Homes[1]).ToString("F1")+" degrees)");
                    Check(world.Rig.FocusDirection.sqrMagnitude>.99f&&Vector3.Dot(world.Rig.FocusDirection,world.Base.Homes[0])>.99f,"AI spectator starts at the first base with a valid camera direction");
                    var civ=world.SystemRuntime.Civilisation;var profile=civ.Planet(world.definition.bodyId).composition;
                    foreach(int faction in new[]{0,1}){
                        var nodes=world.Base.Nodes.Where(n=>Vector3.Dot(CubeSphere.Decode(n.position),world.Base.Homes[faction])>.99f).ToArray();
                        Check(nodes.Count(n=>string.IsNullOrEmpty(n.elementId))==16,"Faction "+faction+" has four deposits of each original RTS resource");
                        Check(nodes.Count(n=>!string.IsNullOrEmpty(n.elementId))==96,"Faction "+faction+" has all 96 configured elemental deposits");
                        Check(nodes.All(n=>n.remaining>0&&new PlanetSurface(world.definition).SampleHeight(CubeSphere.Decode(n.position))>=world.definition.oceanHeight),"Faction "+faction+" reserves are finite and on dry land");
                        Check(nodes.All(n=>Math.Abs(n.capacity-(string.IsNullOrEmpty(n.elementId)?20000:civ.Settings.depositCapacity*Math.Max(.1,profile.LocalAbundance(n.elementId,CubeSphere.Decode(n.position),world.definition.terrainSeed))))<.1),"Faction "+faction+" node reserves use doubled capacities");
                        foreach(var material in civ.Catalogue.resources.Where(e=>e.element&&profile.Get(e.id)>0))Check(nodes.Count(n=>n.elementId==material.id)>=4,"Faction "+faction+" has replacement deposits for "+material.id);
                        foreach(string material in new[]{"water","iron","carbon"})Check(world.Base.HasProductiveBuilding("civil_mine_"+material,faction)||nodes.Any(n=>n.elementId==material&&world.Base.Validate(world.Base.Catalog.Buildings["civil_mine_"+material],CubeSphere.Decode(n.position),0,false,faction).valid&&world.Base.Buildings.Any(b=>b.team==faction&&b.Complete&&b.definition.buildRadius>0&&CubeSphere.ArcDistance(CubeSphere.Decode(b.position),CubeSphere.Decode(n.position),world.definition.radius)<b.definition.buildRadius)),"Faction "+faction+" has an operating or immediately accessible "+material+" extractor site");
                        // Isolate finite-deposit replacement from whatever the autonomous economy already spent.
                        var wallet=world.Base.Factions[faction].wallet;var savedStock=wallet.Stock;var fixtureStock=savedStock;fixtureStock.materials=3000;wallet.Stock=fixtureStock;
                        var mine=world.Base.Buildings.FirstOrDefault(b=>b.team==faction&&!b.removed&&b.definition.id=="mine");
                        if(mine==null){Check(world.Base.TryConstructInfrastructure("mine",faction),"Faction "+faction+" builds a paid material extractor");mine=world.Base.Buildings.Last(b=>b.team==faction&&b.definition.id=="mine");}
                        var mines=world.Base.Buildings.Where(b=>b.team==faction&&!b.removed&&b.definition.id=="mine").ToArray();var reserves=mines.Select(b=>world.Base.Nodes[b.node].remaining).ToArray();
                        foreach(var exhausted in mines)world.Base.Nodes[exhausted.node].remaining=0;
                        Check(!world.Base.HasProductiveBuilding("mine",faction),"Faction "+faction+" does not mistake exhausted mines for usable production");
                        Check(!world.Base.Validate(mine.definition,CubeSphere.Decode(mine.position),0,false,faction).valid,"Faction "+faction+" cannot rebuild on an exhausted deposit");
                        Check(world.Base.TryConstructInfrastructure("mine",faction),"Faction "+faction+" can replace its exhausted mine on another deposit");
                        for(int at=0;at<mines.Length;at++)world.Base.Nodes[mines[at].node].remaining=reserves[at];wallet.Stock=savedStock;
                    }
                    foreach(int team in new[]{0,1}){
                        var inspected=world.Base.Buildings.First(b=>b.team==team&&b.definition.id=="command");world.Rig.SetFocusDirection(CubeSphere.Decode(inspected.position));world.Rig.Distance=300;world.Rig.RefreshPose();world.Base.Rebase();yield return null;
                        var screen=world.View.WorldToScreenPoint(inspected.visual.transform.position);world.Base.ClearSelection();
                        Check(screen.z>0&&world.InspectAiBuilding(new Vector2(screen.x,screen.y))&&ReferenceEquals(world.Base.Selected,inspected),"AI spectator can pick faction "+team+" buildings through the read-only selection path");
                        yield return new WaitForSeconds(.2f);var context=world.Hud.Root.Q("context");Check(context.Q<Label>("planet-building-name")?.text==inspected.definition.name.ToUpperInvariant()&&context.Q<Label>("planet-building-status")?.text.Contains("HEALTH")==true,"Read-only selection displays building identity and live health for faction "+team);
                        Check(context.Query<Button>().ToList().Count==0,"Spectator building context exposes no upgrade, training, sale or economy actions");
                        Check(!world.OrderSelected(world.Rig.FocusDirection),"AI spectator selection cannot issue unit movement orders");
                    }

                }
                world.ReturnToMenu();yield return null;yield return new WaitForSeconds(.3f);Check(menu.State=="menu"&&FindAnyObjectByType<PlanetDevelopment>()==null,"Return to menu releases "+mode+" session");Check(JsonUtility.ToJson(preset)==presetState&&preset.edits==null&&preset.stampField==null,mode+" leaves the saved preset unchanged after the session");Check(Quaternion.Angle(originalSun.transform.rotation,originalRotation)<.001f&&originalSun.shadows==originalShadows,"Returning from "+mode+" restores the original menu sun orientation and shadow setting");
            }
        }
    }
}
