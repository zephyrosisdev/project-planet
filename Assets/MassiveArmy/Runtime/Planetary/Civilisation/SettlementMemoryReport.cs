using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
namespace MassiveArmy.Planetary {
    [Serializable]public sealed class SettlementMemoryReport {
        [Serializable]public sealed class World {
            public int planet,roads,rails,people,deadBiographies,cargo,buildings,cargoSearchSlots,archivedPeople,archivedDeaths,archivedCases;
            public bool resident;
            public long cargoBuffers,cargoScratch,citizenCoreBuffers,citizenFlows;
        }
        public string scope="Allocated tracked settlement GPU buffers; excludes army, terrain, weather, optional citizen crime buffers, meshes and materials. Unity totals have different accounting scope.";
        public long unityAllocatedBytes,unityReservedBytes,managedUsedBytes,graphicsDriverBytes;
        public string historyFailure;
        public List<World> worlds=new List<World>();
    }
    public sealed partial class CivilisationRuntime {
        public SettlementMemoryReport CaptureMemoryUsage(){
            var report=new SettlementMemoryReport{unityAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),unityReservedBytes=Profiler.GetTotalReservedMemoryLong(),managedUsedBytes=Profiler.GetMonoUsedSizeLong(),graphicsDriverBytes=Profiler.GetAllocatedMemoryForGraphicsDriver(),historyFailure=HistoryFailure};
            foreach(var binding in locals.Values){var local=binding.local;var row=new SettlementMemoryReport.World{planet=local.World.definition.bodyId,resident=local.World.ResidentActive,roads=local.Roads.Tiles.Count,rails=local.Rails.Tiles.Count,buildings=local.Buildings.Count,cargoBuffers=local.CargoNavigation?.BufferBytes??0,cargoScratch=local.CargoNavigation?.SearchStorageBytes??0,cargoSearchSlots=local.CargoNavigation?.SearchSlots??0,citizenCoreBuffers=local.Citizens?.BufferBytes??0,citizenFlows=local.Citizens?.FlowStorageBytes??0};
                foreach(var c in binding.colonies)if(c!=null){row.people+=c.residents.Count;row.cargo+=c.cargoVehicles.Count;foreach(var person in c.residents)if(!person.alive)row.deadBiographies++;row.archivedPeople+=c.archivedResidents;row.archivedCases+=c.archivedCases;row.archivedDeaths+=c.archivedDeaths;}
                report.worlds.Add(row);
            }return report;
        }
        public void WriteMemoryUsage(string path)=>File.WriteAllText(path,JsonUtility.ToJson(CaptureMemoryUsage(),true));
    }
}
