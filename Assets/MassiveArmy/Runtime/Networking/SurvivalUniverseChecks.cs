#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    internal static class SurvivalUniverseChecks {
        internal static void Run(Action<bool,string> check){
            int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length,cameras=Resources.FindObjectsOfTypeAll<Camera>().Length;
            SurvivalUniverse original=null,restored=null,client=null;IndustrialAuthority authority=null;
            const string path="TestResults/Multiplayer/universe-native.xml";
            try{
                foreach(string suffix in new[]{"",".journal",".bak"})if(File.Exists(path+suffix))File.Delete(path+suffix);
                original=SurvivalUniverse.Create("universe-test","region-test",73129,SurvivalNetworkSession.ServerDefinition(73129));
                check(original.Bodies.Length>1&&original.Definition(1)!=null&&original.State.regions.Count==original.Bodies.Length,"Headless universe definitions reserve stable identities and explicit region ownership for every generated body");
                check(original.Owns(1,"region-test")&&!original.Owns(1,"different-instance"),"Region ownership cannot be inferred from visibility or a client's requested instance");
                var world=new IndustrialWorld();authority=new IndustrialAuthority(world,path);authority.Save();authority.InitializeSurvivalUniverse(original.State);
                authority.Dispose();authority=null;world=IndustrialAuthority.Load(path);
                check(world.universe!=null&&world.universe.bodies.Count==original.Bodies.Length,"Universe initialization recovers through the existing flushed industrial journal before its next checkpoint");
                restored=SurvivalUniverse.Restore(world.universe,"universe-test",73129);original.Advance(1234.5);restored.Advance(1234.5);
                bool same=true;foreach(var body in original.Bodies){var copy=restored.Definition(body.bodyId);same&=copy!=null&&copy.terrainSeed==body.terrainSeed&&copy.radius==body.radius&&copy.displayName==body.displayName&&(copy.position.RelativeTo(body.position)).Magnitude<1e-6;}
                check(same,"Saved body definitions and prescribed celestial motion restore independently of rendering or visibility");
                client=SurvivalUniverse.FromManifest(original.Manifest("region-test"));client.Advance(1234.5);bool manifestMatches=true;foreach(var body in original.Bodies)manifestMatches&=client.Definition(body.bodyId)!=null&&client.Definition(body.bodyId).position.RelativeTo(body.position).Magnitude<.01;
                check(manifestMatches&&JsonUtility.ToJson(original.Manifest("region-test")).Length<16384,"Bounded discovery manifest reconstructs the same celestial frames on a client without render resources");
                var updated=JsonUtility.ToJson(original.Definition(original.Bodies[1].bodyId));check(client.ApplyDefinition(updated).bodyId==original.Bodies[1].bodyId,"An authoritative region definition updates an existing stable client identity");
                var p=new Double3(4,original.Definition(1).radius+10,5);var v=new Double3(1,2,3);var start=p;var velocity=v;int target=original.Bodies[1].bodyId;original.Reframe(1,target,ref p,ref v);original.Reframe(target,1,ref p,ref v);
                check((p-start).Magnitude<.01&&(v-velocity).Magnitude<1e-6,"Body reference changes conserve canonical position and velocity through a round trip");
                bool refused=false;try{SurvivalUniverse.Restore(world.universe,"wrong-world",73129);}catch(InvalidDataException){refused=true;}
                check(refused,"Universe restore rejects an incompatible persistent world identity");
                check(original.Surface(target)!=null&&original.Surface(target)==original.Surface(target)&&original.Surface(target)!=original.Surface(1),"Independent occupied planets share cached CPU terrain queries without sharing surfaces");
                check(meshes==Resources.FindObjectsOfTypeAll<Mesh>().Length&&materials==Resources.FindObjectsOfTypeAll<Material>().Length&&cameras==Resources.FindObjectsOfTypeAll<Camera>().Length,"Universe scheduling, persistence and CPU surfaces allocate no render resources");
            }finally{authority?.Dispose();client?.Dispose();restored?.Dispose();original?.Dispose();}
        }
    }
}
#endif
