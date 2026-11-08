#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MassiveArmy.Industrial;
using Unity.Services.Lobbies;
using UnityEngine;

namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalSessionProbe {
        int listingClients;bool listingDeleting,listingDeleted,listingVerifying;double listingStarted;long listingTick;HashSet<ulong> listingConnections;
#if UNITY_SERVER
        internal static async Task DelayBeforeJoinCode(SurvivalNetworkSession session){
            string argument=Argument("-survival-probe-join-code-delay");if(argument==null)return;
            var probe=session.Probe;
            string allowed=Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar;
            if(probe==null||!int.TryParse(argument,out int seconds)||seconds<1||seconds>30||!session.Config.worldId.StartsWith("probe-",StringComparison.Ordinal)||!Path.GetFullPath(probe.path).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Join-code delay requires an isolated diagnostic server (1–30 seconds)");
            double started=Time.realtimeSinceStartupAsDouble;long tick=session.ProbeServerTick;
            while(Time.realtimeSinceStartupAsDouble-started<seconds){if(!session.IsServer)throw new InvalidOperationException("Transport stopped during join-code delay");await Task.Delay(100);}
            probe.report.joinCodeDelaySeconds=Time.realtimeSinceStartupAsDouble-started;probe.report.joinCodeDelayTicks=session.ProbeServerTick-tick;
            probe.report.joinCodeDelayChecked=session.IsServer&&probe.report.joinCodeDelayTicks>0;
            if(!probe.report.joinCodeDelayChecked)throw new InvalidOperationException("Server clock/transport did not run before join-code retrieval");
            probe.report.scope+=" Includes diagnostic delay before requesting the Relay code, with transport and server clock already running.";
        }
#endif
        void StartListingRecovery(){
            string clients=Argument("-survival-probe-listing-recovery");if(clients==null)return;
            report.listingRecoveryRequired=true;
            if(!int.TryParse(clients,out listingClients)||listingClients<2||listingClients>100){_ = Finish(false,"Invalid listing recovery client count");return;}
            report.scope+=" Deletes this disposable server's own Lobby, then verifies re-registration, unchanged Relay code and uninterrupted client connections.";
        }
        void UpdateListingRecovery(){
            if(!report.listingRecoveryRequired||report.listingRestored||!readyWritten||finishing)return;
            if(!listingDeleting){
                if(session.ProbeConnections.Count!=listingClients)return;
                string allowed=Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar;
                if(!session.Config.worldId.StartsWith("probe-",StringComparison.Ordinal)||!Path.GetFullPath(path).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)){_ = Finish(false,"Listing deletion is restricted to disposable diagnostic worlds");return;}
                listingDeleting=true;listingStarted=Time.realtimeSinceStartupAsDouble;listingTick=session.ProbeServerTick;listingConnections=session.ProbeConnections;report.originalLobby=session.ProbeLobbyId;
                _ = DeleteProbeListing();return;
            }
            if(!session.ProbeConnections.SetEquals(listingConnections)||session.ProbeJoinCode!=report.relayCode){_ = Finish(false,"Listing recovery changed Relay allocation or interrupted a connection");return;}
            if(Time.realtimeSinceStartupAsDouble-listingStarted>30){_ = Finish(false,"Lobby re-registration exceeded the diagnostic deadline");return;}
            string replacement=session.ProbeLobbyId;
            if(listingDeleted&&!listingVerifying&&!string.IsNullOrEmpty(replacement)&&replacement!=report.originalLobby){listingVerifying=true;_ = VerifyProbeListing(replacement);}
        }
        async Task DeleteProbeListing(){try{await LobbyService.Instance.DeleteLobbyAsync(report.originalLobby);listingDeleted=true;}catch(Exception error){_ = Finish(false,"Diagnostic Lobby deletion failed: "+error.Message);}}
        async Task VerifyProbeListing(string id){
            try{
                var lobby=await LobbyService.Instance.GetLobbyAsync(id);
                string Read(string key)=>lobby.Data!=null&&lobby.Data.TryGetValue(key,out var value)?value.Value:null;
                bool metadata=Read("relay")==report.relayCode&&Read("world")==session.Config.worldId&&Read("instance")==session.Config.instanceId&&Read("content")==SurvivalServerConfig.Content&&Read("protocol")==SurvivalServerConfig.Protocol.ToString()&&Read("players")==listingClients.ToString()&&Read("capacity")==session.Config.maxPlayers.ToString();
                if(!metadata||!session.ProbeConnections.SetEquals(listingConnections)||session.ProbeServerTick<=listingTick)throw new InvalidOperationException("Replacement metadata, connections or server clock did not match");
                report.replacementLobby=id;report.listingMetadataMatches=report.listingConnectionsPreserved=report.listingRestored=true;report.listingRecoverySeconds=Time.realtimeSinceStartupAsDouble-listingStarted;Write();
            }catch(Exception error){_ = Finish(false,"Replacement Lobby verification failed: "+error.Message);}
        }
    }
}
#endif
