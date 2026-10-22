using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetEncounter {
        GraphicsBuffer civicClaims,boundCivicStructures;bool civicEnabled;int boundCivicVersion=-1;
        readonly bool civicCombatControl=Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-civic-combat-control")>=0;
        void PrepareCivicCombat(){var citizens=world.Base.Citizens;civicEnabled=!civicCombatControl&&citizens?.CivicStates!=null&&citizens.Count>0;if(!civicEnabled)return;
            bool initialise=civicClaims==null;if(initialise)civicClaims=Buffer(capacity,4);
            if(boundCivicVersion!=citizens.CivicVersion||boundCivicStructures!=structures){boundCivicVersion=citizens.CivicVersion;boundCivicStructures=structures;
                foreach(string name in new[]{"InitialiseCivicClaims","CivicFindTargets"}){int k=shader.FindKernel(name);shader.SetBuffer(k,"_Units",world.ArmyForCivicCombat);shader.SetBuffer(k,"_Types",world.ArchetypesForCivicCombat);shader.SetBuffer(k,"_Structures",structures);shader.SetBuffer(k,"_Steering",steering);shader.SetBuffer(k,"_CivicStates",citizens.CivicStates);shader.SetBuffer(k,"_CivicClaims",civicClaims);shader.SetBuffer(k,"_SnapshotRead",snapshot);shader.SetBuffer(k,"_HeadsRead",heads);shader.SetBuffer(k,"_NextRead",next);shader.SetBuffer(k,"_Damage",damage);shader.SetBuffer(k,"_Counters",counters);shader.SetBuffer(k,"_CrowdRead",crowdGrid);shader.SetBuffer(k,"_ContactHeadsRead",contactHeads);shader.SetBuffer(k,"_ContactNextRead",contactNext);shader.SetBuffer(k,"_NavCosts",navigation.Costs);}
            }shader.SetInt("_CivicCount",citizens.Count);shader.SetFloat("_CivicClock",(float)world.SystemRuntime.Civilisation.RenderTime);if(initialise)Dispatch("InitialiseCivicClaims",capacity);
        }
        void FindCivicTargets(){if(civicEnabled)Dispatch("CivicFindTargets",world.Base.Citizens.Count);}
        void ApplyCivicDamage(){if(civicEnabled)world.Base.Citizens.ReadDeaths();}
        void DisposeCivicCombat()=>civicClaims?.Dispose();
    }
}
