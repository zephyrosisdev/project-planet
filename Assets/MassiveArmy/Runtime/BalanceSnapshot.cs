using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        [Serializable]sealed class BalanceTeam{public string team,profile;public int tier,buildings,outposts,walls,gates,extractors,queues,aiDecisions;public uint army;public ResourceCost stock,capacity;public float generation,demand,remainingOwnedResources;public string[] technologies;}
        [Serializable]sealed class BalanceRecord{public string mode,outcome,gpu,unity;public BattleSettings settings;public float matchSeconds,fps;public uint zombies;public long measuredAllocatedBytes,measuredReservedBytes;public BalanceTeam[] teams;}
        public string LastBalanceReport{get;private set;}="";
        bool balanceOutcomeSaved;
        public void ExportBalanceSnapshot()
        {
            if(Base==null||!settings.spectatorMode)return;
            var record=new BalanceRecord{mode="AI spectator test",outcome=Base.Outcome,gpu=SystemInfo.graphicsDeviceName,unity=Application.unityVersion,settings=settings,matchSeconds=Base.TimeElapsed,fps=Telemetry.Fps,zombies=Battle.ZombieAlive,measuredAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),measuredReservedBytes=Profiler.GetTotalReservedMemoryLong(),teams=new BalanceTeam[2]};
            for(int t=0;t<2;t++){var faction=Base.Factions[t];var owned=Base.Buildings.Where(b=>!b.removed&&b.team==t).ToArray();record.teams[t]=new BalanceTeam{team=t==0?"CYAN":"RED",profile=faction.profile.ToString(),tier=faction.tier,buildings=owned.Length,outposts=owned.Count(b=>b.definition.id=="outpost"),walls=owned.Count(b=>b.definition.id=="wall"),gates=owned.Count(b=>b.definition.id=="gate"),extractors=owned.Count(b=>b.node>=0),queues=owned.Sum(b=>b.queue.Count),aiDecisions=Base.AIDecisions[t],army=Battle.Statistics==null?0:Battle.Statistics[t],stock=faction.wallet.Stock,capacity=faction.wallet.Capacity,generation=faction.generation,demand=faction.demand,remainingOwnedResources=owned.Where(b=>b.node>=0).Sum(b=>Base.Nodes[b.node].remaining),technologies=faction.technologies.OrderBy(s=>s).ToArray()};}
            string folder=Path.Combine(Application.persistentDataPath,"BalanceTests");Directory.CreateDirectory(folder);LastBalanceReport=Path.Combine(folder,$"test-{settings.seed}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");File.WriteAllText(LastBalanceReport,JsonUtility.ToJson(record,true));Base.Notice="Balance snapshot saved: "+LastBalanceReport;
        }
    }
}
