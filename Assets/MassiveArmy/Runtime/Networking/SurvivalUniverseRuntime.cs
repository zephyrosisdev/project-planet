using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        SurvivalUniverse universe;
#if UNITY_EDITOR || UNITY_SERVER
        void InitializeUniverse(){
            var state=authority.World.universe;
            if(state==null)universe=SurvivalUniverse.CreateFromPreset(Config.worldId,Config.instanceId,Config.seed,Planetary.SurvivalWorldPreset.Load(Config.worldPresetPath),Config.systemSizeMultiplier);
            else {universe=SurvivalUniverse.Restore(state,Config.worldId,Config.seed);UnityEngine.Debug.Log("Loading saved planet definitions and scale; new-world preset overrides do not resize this save.");}
            Config.systemSizeMultiplier=universe.Settings.RuntimeSizeMultiplier;
            if(state==null)authority.InitializeSurvivalUniverse(universe.State);
            if(authority.World.multiplayer.simulationSeconds==0&&clock.Tick>0)authority.World.multiplayer.simulationSeconds=clock.Tick/30.0;
            universe.Advance(authority.World.multiplayer.simulationSeconds);
            foreach(var definition in universe.Bodies){
                if(!universe.Owns(definition.bodyId,Config.instanceId))continue;
                surfaces.Add(definition.bodyId,universe.Surface(definition.bodyId));
            }
            if(!surfaces.ContainsKey(1))throw new InvalidOperationException("This server does not own its home region");
            foreach(var actor in authority.World.actors)if(!surfaces.ContainsKey(actor.pose.body))throw new InvalidOperationException("Saved player is outside this instance's region ownership");
            foreach(var machine in authority.World.machines)if(!surfaces.ContainsKey(machine.pose.body))throw new InvalidOperationException("Saved machine is outside this instance's region ownership");
        }
#endif
        void StepUniverse(float dt){
            if(universe==null)return;
            var state=authority.World.multiplayer;state.simulationSeconds+=dt;universe.Advance(state.simulationSeconds);
            if(industryElapsed+dt<.1)return;
            var active=new HashSet<int>();foreach(var machine in authority.World.machines)active.Add(machine.pose.body);
            foreach(var peer in peers.Values)active.Add(authority.World.Actor(peer.record.actor).pose.body);
            universe.UpdatePowerEnvironments(authority.World,active);
        }
        void DisposeUniverse(){ClearEvaTracking();universe?.Dispose();universe=null;surfaces.Clear();spawnPose=null;}
    }
}

