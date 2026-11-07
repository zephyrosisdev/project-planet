using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using MassiveArmy.Industrial;
class HaulBenchmark {
    static FramePose Point(double x,double z)=>new FramePose{body=1,y=100000,x=x,z=z};
    static void Main(string[] args){
        var world=new IndustrialWorld();for(int i=1;i<=16;i++)world.Register(i,Point(-1000-i*2,-1000));var owner=world.Actor(1);
        var trucks=new IndustrialMachine[100];var stores=new IndustrialMachine[100];var sources=new SurfaceDeposit[100];
        for(int i=0;i<100;i++){
            double x=i%10*150,z=i/10*150;var truck=new IndustrialMachine{id=world.AllocateId(),owner=1,definition="truck",pose=Point(x,z),input=new Inventory{capacity=1000}};truck.input.Add("fuel",100);trucks[i]=truck;
            var store=new IndustrialMachine{id=world.AllocateId(),owner=1,definition="store",pose=Point(x+60,z),input=new Inventory{capacity=100000000}};stores[i]=store;
            var source=new SurfaceDeposit{id=world.AllocateId(),resource="paydirt",pose=Point(x+30,z),remaining=new MaterialLot{litres=1000000,iron=200000,copper=100000}};sources[i]=source;world.machines.Add(truck);world.machines.Add(store);world.deposits.Add(source);
        }
        long firstGenerator=world.nextId;
        for(int i=0;i<900;i++){string id=i<100?"generator":i<400?"foundry":i<700?"press":"store";var machine=new IndustrialMachine{id=world.AllocateId(),owner=1,definition=id,recipe=IndustrialCatalog.Machines[id].recipe,pose=Point(5000+i%32*6,5000+i/32*6),powerSource=firstGenerator+i%100,input=new Inventory{capacity=1000000},output=new Inventory{capacity=1000000}};if(id=="generator")machine.input.Add("fuel",10000);if(id=="foundry")machine.input.Add("iron-ore",40000);if(id=="press")machine.input.Add("iron-bar",40000);world.machines.Add(machine);}
        var planning=Stopwatch.StartNew();for(int i=0;i<100;i++){owner.pose=trucks[i].pose;owner.pose.z+=8;var command=IndustrialHaulNavigation.Plan(world,1,trucks[i].id,sources[i].id,stores[i].id,null,out var error);if(command==null)throw new Exception(error);command.sequence=i+1;if(!world.Execute(1,command).accepted)throw new Exception("Haul fixture assignment failed");}planning.Stop();owner.pose=Point(-1000,-1000);
        if(args.Length>1){if(File.Exists(args[1])){world=IndustrialAuthority.Restore(File.ReadAllText(args[1]));for(int i=0;i<100;i++){trucks[i]=world.Machine(trucks[i].id);stores[i]=world.Machine(stores[i].id);}owner=world.Actor(1);}else File.WriteAllText(args[1],IndustrialAuthority.Snapshot(world));if(args.Length>2&&args[2]=="capture")return;}
        if(Array.IndexOf(args,"--slots")>=0)world.EnableInventoryWorkflow();
        var coldSave=Stopwatch.StartNew();string warmSnapshot=IndustrialAuthority.Snapshot(world);coldSave.Stop();var coldLoad=Stopwatch.StartNew();IndustrialAuthority.Restore(warmSnapshot);coldLoad.Stop();
        for(int i=0;i<600;i++)world.Step();GC.Collect();GC.WaitForPendingFinalizers();var samples=new double[3000];var watch=Stopwatch.StartNew();for(int i=0;i<samples.Length;i++){long start=Stopwatch.GetTimestamp();world.Step();samples[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;}watch.Stop();Array.Sort(samples);
        long delivered=0;int active=0,outOfFuel=0;foreach(var store in stores)delivered+=store.input.bulk.litres;foreach(var truck in trucks){if(truck.haul.active)active++;if(truck.haul.status.Contains("fuel"))outOfFuel++;}
        var saveWatch=Stopwatch.StartNew();string snapshot=IndustrialAuthority.Snapshot(world);saveWatch.Stop();var loadWatch=Stopwatch.StartNew();var restored=IndustrialAuthority.Restore(snapshot);loadWatch.Stop();if(restored.machines.Count!=1100||active!=100||outOfFuel!=0||delivered<1000000)throw new Exception("Scale fixture reduced work or lost state");
        var process=Process.GetCurrentProcess();var result=new {scope="Standalone industry CPU only: 1000 stationary machines, 100 active collection trucks, 16 observers. Not Unity render/native/GPU/fleet acceptance.",stationaryMachines=1000,activeTrucks=active,deliveredLitres=delivered,ticks=samples.Length,medianMs=samples[1500],p95Ms=samples[2850],simulationTicksPerSecond=samples.Length/watch.Elapsed.TotalSeconds,planningTotalMs=planning.Elapsed.TotalMilliseconds,processPrivateBytes=process.PrivateMemorySize64,workingSetBytes=process.WorkingSet64,managedBytes=GC.GetTotalMemory(false),snapshotBytes=Encoding.UTF8.GetByteCount(snapshot),coldSaveMs=coldSave.Elapsed.TotalMilliseconds,coldLoadMs=coldLoad.Elapsed.TotalMilliseconds,saveMs=saveWatch.Elapsed.TotalMilliseconds,loadMs=loadWatch.Elapsed.TotalMilliseconds};Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));File.WriteAllText(args[0],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    }
}
