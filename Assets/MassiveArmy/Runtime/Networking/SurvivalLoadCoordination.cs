#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.IO;
using MassiveArmy.Industrial;
using UnityEngine;
namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalLoadProbe {
        [Serializable] sealed class LoadBarrier {public int version=1,total,workers;public FramePose hub;public long tick;}
        string coordination;long coordinatedStart;
        void ConfigureWorkers(int count){
            if(!int.TryParse(Argument("-survival-load-workers")??"1",out report.workerCount)||report.workerCount<1||report.workerCount>10||!int.TryParse(Argument("-survival-load-worker")??"0",out report.workerIndex)||report.workerIndex<0||report.workerIndex>=report.workerCount)throw new ArgumentException("Invalid load worker assignment");
            report.totalRequested=checked(count*report.workerCount);if(report.totalRequested>100)throw new ArgumentException("Load workers exceed the 100-client validation goal");
            if(report.workerCount==1)return;coordination=Path.GetFullPath(Argument("-survival-load-coordination")??"");var allowed=Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar;if(!coordination.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Worker coordination must use disposable Live paths");Directory.CreateDirectory(coordination);
        }
        string BarrierPath(string name)=>Path.Combine(coordination,name+".json");
        void Barrier(string name,FramePose pose,long tick){var path=BarrierPath(name);if(File.Exists(path))return;var value=new LoadBarrier{total=report.totalRequested,workers=report.workerCount,hub=pose,tick=tick};File.WriteAllText(path+".tmp",JsonUtility.ToJson(value));File.Move(path+".tmp",path);}
        LoadBarrier ReadBarrier(string name){var value=JsonUtility.FromJson<LoadBarrier>(File.ReadAllText(BarrierPath(name)));if(value?.version!=1||value.total!=report.totalRequested||value.workers!=report.workerCount)throw new InvalidDataException("Load workers disagree about their shared measurement");return value;}
        bool AllBarriers(string suffix){for(int worker=0;worker<report.workerCount;worker++)if(!File.Exists(BarrierPath("worker-"+worker+suffix)))return false;return true;}
        bool CoordinateWorkers(double now){
            Barrier("worker-"+report.workerIndex+"-ready",bots[0].session.Latest.self.pose,bots[0].report.lastTick);
            if(!rallyAssigned){
                if(report.workerIndex==0&&AllBarriers("-ready"))Barrier("hub",bots[0].session.Latest.self.pose,bots[0].report.lastTick);
                if(!File.Exists(BarrierPath("hub"))){report.status="WAITING FOR WORKERS";return false;}
                AssignRally(ReadBarrier("hub").hub);rallyStarted=now;rallyAssigned=true;
            }
            report.rallied=0;long newest=0,oldest=long.MaxValue;foreach(var bot in bots){if(bot.session.Latest.self.pose.Distance(bot.rallyGoal.Value)<=2)report.rallied++;newest=Math.Max(newest,bot.report.lastTick);oldest=Math.Min(oldest,bot.report.lastTick);}
            if(windowStart==0&&report.rallied!=report.requested){report.status="RALLYING";return false;}
            Barrier("worker-"+report.workerIndex+"-rallied",hub,newest);
            if(coordinatedStart==0){
                if(report.workerIndex==0&&AllBarriers("-rallied")){long latest=0;for(int worker=0;worker<report.workerCount;worker++)latest=Math.Max(latest,ReadBarrier("worker-"+worker+"-rallied").tick);Barrier("start",hub,latest+10*bots[0].session.Config.tickRate);}
                if(!File.Exists(BarrierPath("start"))){report.status="WAITING FOR RALLY";return false;}coordinatedStart=ReadBarrier("start").tick;
            }
            if(oldest<coordinatedStart){report.status="SETTLING";return false;}return true;
        }
    }
}
#endif
