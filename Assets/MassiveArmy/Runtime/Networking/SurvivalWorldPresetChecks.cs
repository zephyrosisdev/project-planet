#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunWorldPresetChecks(Action<bool,string> check){
            var home=ScriptableObject.CreateInstance<PlanetDefinition>();var system=ScriptableObject.CreateInstance<PlanetSystemDefinition>();var galaxy=ScriptableObject.CreateInstance<GalaxyDefinition>();var preset=ScriptableObject.CreateInstance<SurvivalWorldPreset>();SurvivalUniverse offline=null,server=null;GameObject host=null;IndustrialAuthority store=null;
            string folder=Path.GetFullPath("TestResults/Multiplayer/WorldPresetChecks");Directory.CreateDirectory(folder);string path=Path.Combine(folder,"preset.json");
            try{
                home.radius=25000;home.terrainHeight=37;home.oceanHeight=-17;home.atmosphereHeight=5678;home.gravitationalParameter=2.5*25000*25000;home.weather.cloudAltitude=1234;home.weather.cloudThickness=456;home.weather.coverage=.321f;home.weather.physicalAtmosphere=true;home.displayName="Preset home";
                system.sizeMultiplier=3;system.radiusRange=new Vector2(25000,40000);system.planetCount=2;galaxy.starSystems=2;galaxy.planetsPerSystem=2;system.galaxy=galaxy;preset.presetId="native-preset";preset.revision=7;preset.homePlanet=home;preset.system=system;
                var data=preset.Capture();string hash=data.Fingerprint();File.WriteAllText(path,JsonUtility.ToJson(data,true));var imported=SurvivalWorldPreset.Load(path);
                check(imported.Fingerprint()==hash&&!JsonUtility.ToJson(data).Contains("instanceID"),"World presets export/import all public settings without process-local Unity object references");
                offline=SurvivalUniverse.CreateFromPreset("preset-native","offline",31415,data);server=SurvivalUniverse.CreateFromPreset("preset-native","server",31415,imported);
                var planet=server.Definition(1);check(planet.radius==75000&&planet.terrainHeight==37&&planet.oceanHeight==-17&&planet.atmosphereHeight==5678&&planet.weather.cloudAltitude==1234&&planet.weather.cloudThickness==456&&Math.Abs(planet.weather.coverage-.321f)<.0001,"New shared worlds preserve authored terrain, ocean, atmosphere and clouds without dedicated-server overrides");
                check(Math.Abs(planet.gravitationalParameter/(planet.radius*(double)planet.radius)-2.5)<1e-8&&home.radius==25000&&system.radiusRange.x==25000,"Scaling preserves authored gravity and does not mutate source planet/system assets");
                bool equal=offline.Bodies.Length==server.Bodies.Length;foreach(var body in offline.Bodies)equal&=JsonUtility.ToJson(body)==JsonUtility.ToJson(server.Definition(body.bodyId));check(equal&&server.Bodies.Length==4,"Offline and dedicated generation from the same preset, seed and scale produce identical canonical planet definitions");
                using(var explicitScale=SurvivalUniverse.CreateFromPreset("override-native","server",31415,data,2))check(explicitScale.Definition(1).radius==50000&&explicitScale.Settings.RuntimeSizeMultiplier==2&&explicitScale.Definition(1).atmosphereHeight==5678,"An explicit new-world multiplier overrides the preset once without scaling local atmosphere heights");
                var legacy=ReadServerConfig("{\"maxPlayers\":2,\"systemSizeMultiplier\":10}");var inherit=ReadServerConfig("{\"maxPlayers\":2}");check(legacy.systemSizeMultiplier==10&&inherit.systemSizeMultiplier==0,"Existing explicit scale configs stay explicit; omitted/zero scales inherit the shared preset");
                var frozen=IndustrialAuthority.Restore(IndustrialAuthority.Snapshot(new IndustrialWorld{universe=offline.State,multiplayer=new SurvivalSessionState{worldId="preset-native",instanceId="offline",worldSeed=31415}}));
                check(SurvivalWorldPresetData.Parse(frozen.universe.presetJson).Fingerprint()==hash,"The existing world snapshot retains versioned preset content and its reproducible fingerprint");
                home.radius=777;home.weather.cloudAltitude=9999;preset.revision=8;check(preset.Capture().Fingerprint()!=hash&&data.Fingerprint()==hash,"Later authoring edits create a new preset fingerprint without changing a captured world definition");
                host=new GameObject("Saved preset restoration fixture");var session=host.AddComponent<SurvivalNetworkSession>();session.Config=new SurvivalServerConfig{worldId="preset-native",instanceId="offline",seed=31415,systemSizeMultiplier=99,worldPresetPath=Path.Combine(folder,"missing.json")};store=new IndustrialAuthority(frozen);session.authority=store;session.InitializeUniverse();
                check(session.universe.Definition(1).radius==75000&&session.universe.Definition(1).weather.cloudAltitude==1234&&session.Config.systemSizeMultiplier==3,"Saved worlds ignore later scale/preset edits and restore without the original exported file");
                check(session.PublicConfig().worldPresetPath==""&&session.PublicConfig().savePath=="","Client configuration does not disclose operator preset/save paths");session.DisposeUniverse();session.authority=null;store.Dispose();store=null;DestroyImmediate(host);host=null;
                host=new GameObject("New preset server fixture");session=host.AddComponent<SurvivalNetworkSession>();session.Config=new SurvivalServerConfig{worldId="new-preset-native",instanceId="server",seed=31415,systemSizeMultiplier=2,worldPresetPath=path};store=new IndustrialAuthority(new IndustrialWorld{multiplayer=new SurvivalSessionState{worldId="new-preset-native",instanceId="server",worldSeed=31415}});session.authority=store;session.InitializeUniverse();check(session.universe.Definition(1).radius==50000&&session.authority.World.universe.presetJson==JsonUtility.ToJson(data),"Dedicated startup consumes an exported shared preset and journals the resolved universe");
                session.DisposeUniverse();session.authority=null;store.Dispose();store=null;DestroyImmediate(host);host=null;
                string save=Path.Combine(folder,"journal-world.xml");foreach(string suffix in new[]{"",".journal",".bak"})if(File.Exists(save+suffix))File.Delete(save+suffix);
                using(var journal=new IndustrialAuthority(new IndustrialWorld(),save)){journal.Save();journal.InitializeSurvivalUniverse(offline.State);}check(IndustrialAuthority.Load(save).universe.presetJson==JsonUtility.ToJson(data),"Shared preset definitions recover through the industrial journal before a following checkpoint");
                var invalid=JsonUtility.FromJson<SurvivalWorldPresetData>(JsonUtility.ToJson(data));invalid.formatVersion=2;bool rejected=false;try{invalid.Validate();}catch(InvalidDataException){rejected=true;}check(rejected,"Unknown preset format versions are rejected before generation");
                check(PlanetDevelopment.OfflineSavePath(31415,"")!=PlanetDevelopment.OfflineSavePath(31415,"large-test-10"),"Offline large-world slots cannot overwrite the legacy seed-based world");
                bool traversal=false;try{PlanetDevelopment.OfflineSavePath(31415,"../old-world");}catch(ArgumentException){traversal=true;}check(traversal,"Offline save slot names cannot escape their intended directory");
                var resource=SurvivalWorldPreset.Load();check(resource.presetId=="default-survival"&&resource.revision>=1,"The shipped shared preset is available to both client and dedicated builds");
            }finally{if(host!=null){var session=host.GetComponent<SurvivalNetworkSession>();session.DisposeUniverse();session.authority=null;DestroyImmediate(host);}store?.Dispose();server?.Dispose();offline?.Dispose();DestroyImmediate(preset);DestroyImmediate(galaxy);DestroyImmediate(system);DestroyImmediate(home);}
        }
    }
}
#endif
