using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MassiveArmy.Planetary;
namespace MassiveArmy.EditorTools {
    public static class PlanetEconomyDiagnostics {
        [Serializable]sealed class Snapshot {public string mode;public int seed;public double clock;public float speed;public uint tick;public Building[] buildings;public ColonyState[] colonies;}
        [Serializable]sealed class Building {public long id;public int team,node,builders,workers;public string type;public bool removed,complete,pending,roadSuspended;public float progress,food,efficiency;}
        public static void Capture(){var world=UnityEngine.Object.FindAnyObjectByType<PlanetDevelopment>();if(world?.Base==null)return;var local=world.Base;
            var snapshot=new Snapshot{mode=world.sessionMode.ToString(),seed=world.definition.terrainSeed,clock=local.Civilisation?.SimulationTime??0,speed=world.SimulationSpeed,tick=world.SimulationTicks,colonies=local.Settlements,buildings=local.Buildings.Select(b=>new Building{id=b.persistentId,team=b.team,node=b.node,type=b.definition.id,removed=b.removed,complete=b.Complete,pending=b.awaitingProjectMaterials,roadSuspended=b.roadSuspended,progress=b.construction,food=b.FoodEfficiency,efficiency=b.Efficiency,builders=b.constructionWorkers,workers=b.WorkersAssigned}).ToArray()};
            Directory.CreateDirectory("TestResults/Planetary");File.WriteAllText("TestResults/Planetary/live-economy.json",JsonUtility.ToJson(snapshot,true));
        }
    }
}
