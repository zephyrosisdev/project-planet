using System;
using System.Threading.Tasks;
using MassiveArmy.Industrial;
using Unity.Services.Core;
using Unity.Services.Lobbies;

namespace MassiveArmy.Networking {
    internal static class SurvivalServiceRetry {
        internal static bool Transient(Exception error){
            if(error is TimeoutException)return true;
            if(!(error is RequestFailedException failure))return false;
            int code=failure.ErrorCode;
            if(code==CommonErrorCodes.TransportError||code==CommonErrorCodes.Timeout||code==CommonErrorCodes.ServiceUnavailable||code==CommonErrorCodes.TooManyRequests)return true;
            if(code<15000||code>=17000)return false;
            int status=code%1000;return status==408||status==429||status==500||status==502||status==503||status==504;
        }
        internal static bool MissingLobby(Exception error)=>error is LobbyServiceException lobby&&(lobby.Reason==LobbyExceptionReason.LobbyNotFound||lobby.Reason==LobbyExceptionReason.EntityNotFound)||error is RequestFailedException failure&&failure.ErrorCode==CommonErrorCodes.NotFound;
        internal static async Task<T> Run<T>(Func<Task<T>> operation,Func<bool> cancelled,Func<int,Task> delay=null){
            delay??=milliseconds=>Task.Delay(milliseconds);
            for(int attempt=0;;attempt++){
                if(cancelled())throw new OperationCanceledException("Session closed");
                try{return await operation();}
                catch(Exception error) when(attempt<2&&Transient(error)){
                    if(cancelled())throw new OperationCanceledException("Session closed");
                    await delay(2000<<attempt);
                }
            }
        }
    }
    // Only discovery is recreated. Relay allocation and the authoritative session
    // remain owned by SurvivalServices/SurvivalNetworkSession throughout recovery.
    internal interface ISurvivalLobbyBackend {
        Task<string> Create(SurvivalServerConfig config,string code,string region,int population);
        Task Heartbeat(string id);
        Task Population(string id,int count);
        Task Delete(string id);
    }
    internal sealed class SurvivalLobbyRegistration {
        readonly ISurvivalLobbyBackend backend;readonly SurvivalServerConfig config;
        readonly string code,region;readonly Func<double> now;readonly Func<int,Task> delay;readonly Action<string> warning;
        Task active=Task.CompletedTask,closeTask;bool busy,disposed;int population,advertisedPopulation,failures;
        double nextHeartbeat,nextUpdate,nextRetry;
        internal string LobbyId {get;private set;}
        internal string LastError {get;private set;}
        internal bool Suspended {get;private set;}
        internal SurvivalLobbyRegistration(ISurvivalLobbyBackend backend,SurvivalServerConfig config,string code,string region,Func<double> now,Action<string> warning,Func<int,Task> delay=null){this.backend=backend;this.config=config;this.code=code;this.region=region;this.now=now;this.warning=warning;this.delay=delay;}
        internal Task Register(){if(disposed)throw new ObjectDisposedException(nameof(SurvivalLobbyRegistration));if(busy||LobbyId!=null)throw new InvalidOperationException("Registration already started");busy=true;return active=RegisterCore();}
        async Task RegisterCore(){try{await SurvivalServiceRetry.Run(async()=>{await Create();return true;},()=>disposed,delay);}finally{busy=false;}}
        async Task Create(){
            if(disposed)throw new OperationCanceledException("Session closed");
            int count=population;string id=await backend.Create(config,code,region,count);
            if(string.IsNullOrEmpty(id))throw new InvalidOperationException("Lobby creation returned no identity");
            LobbyId=id; // Close waits for this task, including a late create response.
            if(disposed)throw new OperationCanceledException("Session closed");
            advertisedPopulation=count;nextHeartbeat=now()+10;nextUpdate=now()+2;Recovered();
        }
        internal void SetPopulation(int count){population=Math.Max(0,Math.Min(config.maxPlayers,count));}
        internal Task Pump(){
            if(disposed||busy||Suspended||now()<nextRetry)return Task.CompletedTask;
            if(LobbyId!=null&&now()<nextHeartbeat&&(population==advertisedPopulation||now()<nextUpdate))return Task.CompletedTask;
            busy=true;return active=PumpCore();
        }
        async Task PumpCore(){
            string id=LobbyId;
            try{
                if(id==null){await Create();return;}
                if(now()>=nextHeartbeat){await backend.Heartbeat(id);nextHeartbeat=now()+10;}
                else {int count=population;await backend.Population(id,count);advertisedPopulation=count;nextUpdate=now()+2;}
                if(!disposed)Recovered();
            }catch(Exception error){
                if(disposed)return;
                bool missing=SurvivalServiceRetry.MissingLobby(error);
                if(missing&&LobbyId==id)LobbyId=null;
                failures=Math.Min(5,failures+1);nextRetry=now()+Math.Min(30,2*(1<<(failures-1)));
                Suspended=!missing&&!SurvivalServiceRetry.Transient(error);
                string message=(Suspended?"Lobby discovery suspended: ":"Lobby discovery retry: ")+error.Message;
                if(LastError!=message)warning?.Invoke(message);LastError=message;
            }finally{busy=false;}
        }
        void Recovered(){if(LastError!=null)warning?.Invoke("Lobby discovery restored");LastError=null;failures=0;nextRetry=0;}
        internal Task Close(){disposed=true;return closeTask??=CloseCore();}
        async Task CloseCore(){
            try{await active;}catch(Exception){/* Startup/cancellation is reported by its caller. */}
            string id=LobbyId;LobbyId=null;if(id==null)return;
            try{await SurvivalServiceRetry.Run(async()=>{await backend.Delete(id);return true;},()=>false,delay);}
            catch(Exception error){if(!SurvivalServiceRetry.MissingLobby(error))warning?.Invoke("Lobby cleanup: "+error.Message);}
        }
    }
}
