using System;
using System.Collections.Generic;

namespace MassiveArmy.Industrial {
    // Static universe metadata belongs in the existing world checkpoint/journal,
    // outside the frequently journalled dynamic multiplayer session record.
    [Serializable] public sealed class SurvivalUniverseState {
        public int version=1,seed,homeBody=1;public string worldId,generator="kepler-1",systemJson,galaxyJson;
        public List<SurvivalBodyDefinition> bodies=new List<SurvivalBodyDefinition>();
        public List<SurvivalRegionOwnership> regions=new List<SurvivalRegionOwnership>();
    }
    [Serializable] public sealed class SurvivalBodyDefinition {public int body;public string stableId,json;}
    [Serializable] public sealed class SurvivalRegionOwnership {public int body;public string instance;public long epoch=1;}
    public sealed partial class IndustrialWorld {public SurvivalUniverseState universe;}
    public sealed partial class IndustrialAuthority {
        public void InitializeSurvivalUniverse(SurvivalUniverseState state){
            lock(gate){
                if(state==null||state.version!=1||state.bodies==null||state.bodies.Count==0||state.regions==null||state.regions.Count!=state.bodies.Count)throw new ArgumentException("Invalid persistent universe");
                if(World.universe!=null)throw new InvalidOperationException("Persistent universe is already initialized");
                Append(new IndustrialJournalEntry{universe=state});World.universe=state;
            }
        }
    }
}
