using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MassiveArmy.Industrial;
static class StartupRecoveryChecks {
    static void Main(){
        int count=0;void Check(bool pass,string label){if(!pass)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
        var world=new IndustrialWorld();world.Register(1,new FramePose{body=1});var truck=new IndustrialMachine{id=world.AllocateId(),owner=1,definition="truck",pose=new FramePose{body=1},haul=new IndustrialHaulJob{active=true,source=3,destination=4,reserved=12}};truck.input.bulk=new MaterialLot{litres=80,iron=16,activity=5};world.machines.Add(truck);
        var path=Path.GetFullPath("TestResults/Planetary/StartupRecovery/failed-replan.xml");Directory.CreateDirectory(Path.GetDirectoryName(path));if(File.Exists(path))File.Delete(path);if(File.Exists(path+".journal"))File.Delete(path+".journal");
        using(var authority=new IndustrialAuthority(world,path)){authority.Save();Check(authority.ReplanTruck(new IndustrialHaulReplan{truck=truck.id,source=3,error="Route data budget reached"})==null,"Planning failure commits a status update");}
        var restored=IndustrialAuthority.Load(path);var vehicle=restored.Machine(truck.id);
        Check(vehicle.haul.active&&vehicle.haul.reserved==12&&vehicle.input.bulk.litres==80&&vehicle.input.bulk.activity==5,"Failed replan replay preserves the job, reservation, cargo and activity");
        Check(vehicle.haul.nextReplanTick==100&&vehicle.haul.status=="Route blocked · Route data budget reached","Failed replan cooldown and reason survive replay");
        var encoded=File.ReadAllText(path+".journal").Trim();var xml=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));Check(xml.Contains("nil=\"true\""),"New journals explicitly preserve a null route");
        xml=Regex.Replace(xml,@"<route\b[^>]*/>","");File.WriteAllText(path+".journal",Convert.ToBase64String(Encoding.UTF8.GetBytes(xml))+"\n");
        restored=IndustrialAuthority.Load(path);Check(restored.Machine(truck.id).haul.status==vehicle.haul.status,"Legacy omitted routes load as recorded planning failures");
        using(var reopened=new IndustrialAuthority(restored,path)){reopened.Save();Check(new FileInfo(path+".journal").Length==0,"An existing nonempty journal compacts after reopening");Check(reopened.ReplanTruck(new IndustrialHaulReplan{truck=truck.id,error="Still blocked"})==null,"The compacted stream accepts the next journal record");}
        var compacted=IndustrialAuthority.Load(path);Check(compacted.Machine(truck.id).haul.status=="Route blocked · Still blocked"&&compacted.Machine(truck.id).input.bulk.litres==80,"Compaction and subsequent replay retain cargo and the latest status");
        Check(restored.PrepareHaulReplan(new IndustrialHaulReplan{truck=truck.id,route=new System.Collections.Generic.List<FramePose>()},out _)!=null,"An empty successful route remains invalid");
        var copy="TestResults/Planetary/StartupRecovery/industry.xml";if(File.Exists(copy)){
            var original=IndustrialAuthority.Load(copy);var again=IndustrialAuthority.Load(copy);Check(IndustrialAuthority.Snapshot(original)==IndustrialAuthority.Snapshot(again),"The copied user snapshot and journal recover deterministically");
            Check(original.journalSequence>=469353&&original.Machine(39)?.haul!=null,"The user's blocked route is restored without discarding the world");
            Console.WriteLine("Recovered user sequence "+original.journalSequence+", "+original.machines.Count+" machines");
        }
        Console.WriteLine(count+" startup recovery checks passed");
    }
}
