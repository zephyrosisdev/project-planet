using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MassiveArmy.Industrial;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MassiveArmy.Networking {
    internal sealed class SurvivalServices {
        public const string Product="synthetic-front-survival";
        readonly IUnityServices registry;
        internal SurvivalServices(IUnityServices registry=null){this.registry=registry;}
        IAuthenticationService Authentication=>registry==null?AuthenticationService.Instance:registry.GetAuthenticationService();
        Unity.Services.Relay.IRelayService Relay=>registry==null?RelayService.Instance:registry.GetRelayService();
        Unity.Services.Lobbies.ILobbyService Lobby=>registry==null?LobbyService.Instance:registry.GetLobbyService();
        internal string AccessToken=>Authentication.AccessToken;
        string joinedLobby,allocationRegion;Guid allocationId;SurvivalLobbyRegistration registration;
        public string LobbyId=>Server?registration?.LobbyId:joinedLobby;
        public string JoinCode {get;private set;}
        public bool Server {get;private set;}bool disposed;
        public string DiscoveryStatus=>registration?.LastError;
        public async Task Initialize(string environment,string profile){
            if(registry==null){if(UnityServices.State!=ServicesInitializationState.Initialized)await UnityServices.InitializeAsync(new InitializationOptions().SetEnvironmentName(environment));}
            else if(registry.State!=ServicesInitializationState.Initialized)await registry.InitializeAsync(new InitializationOptions().SetEnvironmentName(environment));
            if(!Authentication.IsSignedIn){Authentication.SwitchProfile(profile);await Authentication.SignInAnonymouslyAsync();}
        }
        public async Task Allocate(UnityTransport transport,SurvivalServerConfig config){
            Server=true;
            var allocation=await SurvivalServiceRetry.Run(()=>Relay.CreateAllocationAsync(config.maxPlayers,string.IsNullOrWhiteSpace(config.region)?null:config.region),()=>disposed);
            if(disposed)throw new OperationCanceledException("Session closed");
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation,"dtls"));
            allocationId=allocation.AllocationId;allocationRegion=allocation.Region;
        }
        public async Task Advertise(SurvivalServerConfig config,int population){
            if(disposed)throw new OperationCanceledException("Session closed");
            JoinCode=await SurvivalServiceRetry.Run(()=>Relay.GetJoinCodeAsync(allocationId),()=>disposed);
            if(disposed)throw new OperationCanceledException("Session closed");
            registration=new SurvivalLobbyRegistration(new LobbyBackend(),config,JoinCode,allocationRegion,()=>Time.realtimeSinceStartupAsDouble,Debug.LogWarning);
            registration.SetPopulation(population);
            await registration.Register();
        }
        sealed class LobbyBackend:ISurvivalLobbyBackend {
            public async Task<string> Create(SurvivalServerConfig config,string code,string region,int population){
                var lobby=await LobbyService.Instance.CreateLobbyAsync(config.displayName,config.maxPlayers+1,new CreateLobbyOptions{Data=new Dictionary<string,DataObject>{
                {"product",new DataObject(DataObject.VisibilityOptions.Public,Product,DataObject.IndexOptions.S1)},
                {"protocol",new DataObject(DataObject.VisibilityOptions.Public,SurvivalServerConfig.Protocol.ToString(),DataObject.IndexOptions.S2)},
                {"content",new DataObject(DataObject.VisibilityOptions.Public,SurvivalServerConfig.Content)},
                {"relay",new DataObject(DataObject.VisibilityOptions.Public,code)},
                {"world",new DataObject(DataObject.VisibilityOptions.Public,config.worldId)},
                {"instance",new DataObject(DataObject.VisibilityOptions.Public,config.instanceId)},
                {"region",new DataObject(DataObject.VisibilityOptions.Public,region)},
                {"players",new DataObject(DataObject.VisibilityOptions.Public,population.ToString())},
                {"capacity",new DataObject(DataObject.VisibilityOptions.Public,config.maxPlayers.ToString())}
                }});return lobby.Id;
            }
            public Task Heartbeat(string id)=>LobbyService.Instance.SendHeartbeatPingAsync(id);
            public async Task Population(string id,int count){await LobbyService.Instance.UpdateLobbyAsync(id,new UpdateLobbyOptions{Data=new Dictionary<string,DataObject>{{"players",new DataObject(DataObject.VisibilityOptions.Public,count.ToString())}}});}
            public Task Delete(string id)=>LobbyService.Instance.DeleteLobbyAsync(id);
        }
        public async Task<List<Lobby>> Query(){
            var response=await Lobby.QueryLobbiesAsync(new QueryLobbiesOptions{Count=50,Filters=new List<QueryFilter>{
                new QueryFilter(QueryFilter.FieldOptions.S1,Product,QueryFilter.OpOptions.EQ),
                new QueryFilter(QueryFilter.FieldOptions.S2,SurvivalServerConfig.Protocol.ToString(),QueryFilter.OpOptions.EQ)
            }});return response.Results;
        }
        public async Task Join(UnityTransport transport,string code,string lobbyId){
            if(!string.IsNullOrEmpty(lobbyId)){
                var lobby=await Lobby.JoinLobbyByIdAsync(lobbyId);
                joinedLobby=lobby.Id;if(disposed){await Leave();throw new OperationCanceledException("Connection cancelled");}if(!lobby.Data.TryGetValue("relay",out var relay))throw new InvalidOperationException("Server has no Relay code");code=relay.Value;
            }
            var allocation=await Relay.JoinAllocationAsync(code.Trim().ToUpperInvariant());
            if(disposed)throw new OperationCanceledException("Connection cancelled");transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation,"dtls"));
        }
        public Task Population(int count){registration?.SetPopulation(count);return Task.CompletedTask;}
        public void Tick(){if(!disposed&&Server)_ = registration?.Pump();}
        public async Task Leave(){
            disposed=true;if(Server){if(registration!=null)await registration.Close();return;}
            var id=joinedLobby;joinedLobby=null;if(id==null)return;
            try{await Lobby.RemovePlayerAsync(id,Authentication.PlayerId);}
            catch(Exception e){Debug.LogWarning("Lobby cleanup: "+e.Message);}
        }
    }
}
