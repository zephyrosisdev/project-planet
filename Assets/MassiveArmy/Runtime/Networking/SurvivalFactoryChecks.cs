#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunFactoryChecks(Action<bool,string> check){
            if(Active!=null)throw new InvalidOperationException("Factory checks require no active session");
            int cameras=Resources.FindObjectsOfTypeAll<Camera>().Length,meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length,objects=Resources.FindObjectsOfTypeAll<GameObject>().Length;
            var go=new GameObject("Headless factory fixture");var session=go.AddComponent<SurvivalNetworkSession>();PlanetDefinition definition=null;
            try{
                session.Config=new SurvivalServerConfig();definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.bodyId=1;definition.radius=10000;definition.terrainHeight=0;definition.oceanHeight=-35;definition.stampTerrain=false;session.surfaces.Add(1,new PlanetSurface(definition));
                var world=new IndustrialWorld{multiplayer=new SurvivalSessionState{worldId="factory-fixture"}};session.authority=new IndustrialAuthority(world);
                var actor=world.Register(1,new FramePose{body=1,y=10000,z=-10});actor.inventory.Add("plate",100);actor.inventory.Add("gear",100);
                var record=new SurvivalPlayerRecord{actor=1,identity="factory-owner",name="Builder"};world.multiplayer.players.Add(record);session.peers.Add(1,new Peer{record=record,motion=new SurvivalMotion{actor=1,pose=actor.pose,name="Builder"}});
                IndustrialMachine Machine(string kind,double x,double z){var machine=new IndustrialMachine{id=world.AllocateId(),owner=1,definition=kind,pose=new FramePose{body=1,y=10000,x=x,z=z}};world.machines.Add(machine);return machine;}
                var source=Machine("store",0,10);var target=Machine("store",0,-10);var generator=Machine("generator",8,10);var consumer=Machine("store",8,-10);var pump=Machine("pump",-8,10);var waterTarget=Machine("store",-8,-10);
                source.input.Add("iron-bar",3);var cached=new SurvivalEntity{kind="machine",id=source.id*8+2,pose=source.pose};PrepareMachinePublication(cached,source);string privateWire=cached.privateData,publicWire=cached.data;PrepareMachinePublication(cached,source);
                check(object.ReferenceEquals(privateWire,cached.privateData)&&object.ReferenceEquals(publicWire,cached.data)&&JsonUtility.FromJson<IndustrialMachine>(publicWire).input.Used==0&&JsonUtility.FromJson<IndustrialMachine>(privateWire).input.Count("iron-bar")==3&&source.input.Count("iron-bar")==3,"One immutable machinery publication shares cached public/private encodings without exposing or mutating authoritative inventory");
                IndustrialCommand Command(string kind,long from,long to)=>new IndustrialCommand{kind=kind,source=from,target=to,route=new List<FramePose>()};
                var belt=Command("belt",source.id,target.id);check(session.ValidateFactoryConnection(actor,belt)==null,"A valid ground conveyor passes headless terrain and stored-prism contacts");
                var blocker=Machine("store",0,0);check(session.ValidateFactoryConnection(actor,Command("belt",source.id,target.id))!=null,"Stored CPU collision prisms reject a conveyor through another machine");world.machines.Remove(blocker);
                var buried=Command("power",generator.id,consumer.id);buried.route.Add(new FramePose{body=1,x=8,y=9995,z=0});check(session.ValidateFactoryConnection(actor,buried)!=null,"Headless cable validation rejects terrain penetration");
                world.multiplayer.ships.Add(new SurvivalShipState{entity=900,owner=1,pose=new FramePose{body=1,y=10000},collisionRadius=3});check(session.ValidateFactoryConnection(actor,Command("belt",source.id,target.id))!=null,"Headless routes keep clear of authoritative ship collision envelopes");world.multiplayer.ships.Clear();
                var power=Command("power",generator.id,consumer.id);check(session.ValidateFactoryConnection(actor,power)==null,"Automatic power socket exits clear actual stored endpoint prisms");
                var water=Command("water",pump.id,waterTarget.id);check(session.ValidateFactoryConnection(actor,water)==null,"Automatic water socket exits clear actual stored endpoint prisms");
                power.sequence=1;water.sequence=2;belt.sequence=3;check(session.authority.Execute(1,power).accepted&&session.authority.Execute(1,water).accepted&&session.authority.Execute(1,belt).accepted,"Prepared factory commands execute through the existing authority");
                source.input.Add("iron-bar",10);source.constructionPaid.Add(new ItemStack("plate",10));
                SurvivalSnapshot captured=null;session.Publish((client,packet,payload)=>{if(client==1)captured=JsonUtility.FromJson<SurvivalSnapshot>(Encoding.UTF8.GetString(payload));return payload.Length+4;});
                int links=0;foreach(var entity in captured.changed)if(entity.kind=="factory-link"){links++;var link=JsonUtility.FromJson<SurvivalFactoryLink>(entity.data);check(entity.id<0&&captured.changed.Exists(e=>e.id==link.source*8+2)&&captured.changed.Exists(e=>e.id==link.target*8+2),"AOI link entry includes its endpoint presentation state");}
                check(JsonUtility.FromJson<IndustrialMachine>(captured.changed.Find(e=>e.id==source.id*8+2).data).constructionPaid.Count==0,"Endpoint subscriptions redact construction receipts outside interaction reach");
                check(links==3,"Power, water and conveyor links share bounded AOI publication");
                var wire=captured.changed.Find(e=>e.kind=="factory-link"&&JsonUtility.FromJson<SurvivalFactoryLink>(e.data).kind=="belt");
                check(!wire.data.Contains("items")&&!wire.data.Contains("constructionPaid")&&!wire.data.Contains("activity")&&!wire.data.Contains("inventory"),"Public conveyor geometry excludes cargo, payment receipts and private inventory");
                session.clientMachines.Clear();session.clientFactoryLinks.Clear();session.factoryEndpointPoses.Clear();session.ReceiveFactoryLinks(new SurvivalSnapshot{changed=new List<SurvivalEntity>{wire}});session.factoryMeshDirty=false;
                var endpoint=captured.changed.Find(e=>e.id==source.id*8+2);var entry=new SurvivalSnapshot{changed=new List<SurvivalEntity>{endpoint}};session.ReceiveMachineStates(entry);session.ReceiveFactoryLinks(entry);
                check(session.factoryMeshDirty,"A delayed endpoint entry invalidates geometry after its link arrived first");
                string save=Path.Combine("TestResults/Multiplayer","native-backup-lock-"+Guid.NewGuid().ToString("N")+".xml");Exception readerError=null;
                using(var saved=new IndustrialAuthority(new IndustrialWorld(),save)){
                    saved.World.Register(1,new FramePose{body=1});saved.Save();saved.Save();using(var held=new ManualResetEventSlim()){
                        var reader=new Thread(()=>{try{using(var file=new FileStream(save+".bak",FileMode.Open,FileAccess.Read,FileShare.Read)){held.Set();Thread.Sleep(40);}}catch(Exception e){readerError=e;held.Set();}});reader.Start();held.Wait();
                        try{saved.World.Actor(1).health=75;saved.Save();}finally{reader.Join();}
                    }
                }
                check(readerError==null&&IndustrialAuthority.Load(save).Actor(1).health==75,"Native atomic checkpoint replacement recovers after a transient backup-reader lock");
            }finally{session.authority?.Dispose();session.authority=null;DestroyImmediate(go);if(definition!=null)DestroyImmediate(definition);}
            check(cameras==Resources.FindObjectsOfTypeAll<Camera>().Length&&meshes==Resources.FindObjectsOfTypeAll<Mesh>().Length&&materials==Resources.FindObjectsOfTypeAll<Material>().Length&&objects==Resources.FindObjectsOfTypeAll<GameObject>().Length,"Headless factory contacts allocate no camera, render mesh, material or lasting GameObject");
        }
    }
}

#endif
