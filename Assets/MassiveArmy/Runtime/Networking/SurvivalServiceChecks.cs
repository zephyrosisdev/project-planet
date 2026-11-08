#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MassiveArmy.Industrial;
using Unity.Services.Core;
using Unity.Services.Lobbies;

namespace MassiveArmy.Networking {
    internal static class SurvivalServiceChecks {
        sealed class Backend:ISurvivalLobbyBackend {
            internal int creates,heartbeats,updates,deletes,lastPopulation;internal string lastCode,lastDeleted;
            internal Exception heartbeatError,updateError;internal TaskCompletionSource<string> pendingCreate;internal TaskCompletionSource<bool> pendingUpdate;
            public Task<string> Create(SurvivalServerConfig config,string code,string region,int population){creates++;lastCode=code;lastPopulation=population;return pendingCreate?.Task??Task.FromResult("lobby-"+creates);}
            public Task Heartbeat(string id){heartbeats++;var error=heartbeatError;heartbeatError=null;return error==null?Task.CompletedTask:Task.FromException(error);}
            public Task Population(string id,int count){updates++;lastPopulation=count;var error=updateError;updateError=null;return error!=null?Task.FromException(error):pendingUpdate?.Task??Task.CompletedTask;}
            public Task Delete(string id){deletes++;lastDeleted=id;return Task.CompletedTask;}
        }
        static void Done(Task task){if(!task.IsCompleted)throw new InvalidOperationException("Fault fixture unexpectedly requires the Unity event loop");task.GetAwaiter().GetResult();}
        internal static void Run(Action<bool,string> check){
            check(SurvivalServiceRetry.Transient(new RequestFailedException(CommonErrorCodes.TransportError,"transport"))&&SurvivalServiceRetry.Transient(new LobbyServiceException(LobbyExceptionReason.ServiceUnavailable,"unavailable")),"Discovery retries recognized transport and temporary service failures");
            check(!SurvivalServiceRetry.Transient(new LobbyServiceException(LobbyExceptionReason.Unauthorized,"unauthorized"))&&!SurvivalServiceRetry.Transient(new LobbyServiceException(LobbyExceptionReason.InvalidArgument,"invalid")),"Discovery does not retry permanent authorization or argument errors");
            int attempts=0;var delays=new List<int>();Func<int,Task> delay=ms=>{delays.Add(ms);return Task.CompletedTask;};
            var retry=SurvivalServiceRetry.Run(()=>{attempts++;return attempts<3?Task.FromException<int>(new TimeoutException()):Task.FromResult(7);},()=>false,delay);Done(retry);
            check(attempts==3&&retry.Result==7&&delays.Count==2&&delays[0]==2000&&delays[1]==4000,"Startup retry is bounded to three attempts with 2/4 second backoff");
            attempts=0;bool failed=false;try{Done(SurvivalServiceRetry.Run<int>(()=>{attempts++;return Task.FromException<int>(new LobbyServiceException(LobbyExceptionReason.Unauthorized,"denied"));},()=>false,delay));}catch(LobbyServiceException){failed=true;}
            check(failed&&attempts==1,"Permanent startup failures are reported without repeated requests");
            attempts=0;failed=false;try{Done(SurvivalServiceRetry.Run<int>(()=>{attempts++;return Task.FromException<int>(new TimeoutException());},()=>false,delay));}catch(TimeoutException){failed=true;}
            check(failed&&attempts==3,"Persistent transient startup failure exhausts its bounded retry budget");
            attempts=0;bool cancelled=false;failed=false;try{Done(SurvivalServiceRetry.Run<int>(()=>{attempts++;return Task.FromException<int>(new TimeoutException());},()=>cancelled,ms=>{cancelled=true;return Task.CompletedTask;}));}catch(OperationCanceledException){failed=true;}
            check(failed&&attempts==1,"Cancellation during backoff prevents the next cloud operation");

            double now=0;var backend=new Backend();var warnings=new List<string>();
            var config=new SurvivalServerConfig{maxPlayers=8};
            SurvivalLobbyRegistration Registration(Backend target)=>new SurvivalLobbyRegistration(target,config,"SAME-CODE","test-region",()=>now,warnings.Add,delay);
            var registration=Registration(backend);Done(registration.Register());
            check(registration.LobbyId=="lobby-1"&&backend.lastPopulation==0&&backend.lastCode=="SAME-CODE","Initial Lobby registration retains session identity and Relay code");
            registration.SetPopulation(8);now=2;Done(registration.Pump());Done(registration.Pump());
            check(backend.updates==1&&backend.lastPopulation==8,"Population publication is coalesced and rate limited");
            backend.heartbeatError=new LobbyServiceException(LobbyExceptionReason.LobbyNotFound,"deleted");now=10;Done(registration.Pump());
            check(registration.LobbyId==null&&backend.creates==1&&!registration.Suspended,"A missing Lobby schedules rediscovery without stopping the session");
            now=11;Done(registration.Pump());check(backend.creates==1,"Missing-listing recovery respects backoff");
            now=12;Done(registration.Pump());
            check(registration.LobbyId=="lobby-2"&&backend.creates==2&&backend.lastCode=="SAME-CODE"&&backend.lastPopulation==8&&registration.LastError==null,"A replacement Lobby advertises the existing Relay code and current population");
            backend.heartbeatError=new LobbyServiceException(LobbyExceptionReason.ServiceUnavailable,"temporary");now=22;Done(registration.Pump());int heartbeats=backend.heartbeats;
            now=23;Done(registration.Pump());check(backend.heartbeats==heartbeats&&registration.LobbyId=="lobby-2","Temporary heartbeat failure retains the allocation/listing and backs off");
            now=24;Done(registration.Pump());check(registration.LastError==null&&backend.creates==2,"Heartbeat recovery clears failure status without creating another listing");

            backend.pendingUpdate=new TaskCompletionSource<bool>();registration.SetPopulation(6);now=26;var updating=registration.Pump();registration.SetPopulation(5);Done(registration.Pump());
            check(!updating.IsCompleted&&backend.updates==2,"Only one discovery request is in flight while population changes");
            backend.pendingUpdate.SetResult(true);Done(updating);backend.pendingUpdate=null;now=28;Done(registration.Pump());
            check(backend.lastPopulation==5&&backend.updates==3,"An in-flight population update cannot erase a newer population value");
            backend.updateError=new LobbyServiceException(LobbyExceptionReason.Forbidden,"revoked");registration.SetPopulation(4);now=30;Done(registration.Pump());int requests=backend.updates+backend.heartbeats+backend.creates;now=100;Done(registration.Pump());
            check(registration.Suspended&&backend.updates+backend.heartbeats+backend.creates==requests,"Permanent runtime discovery errors suspend retries without touching gameplay");
            Done(registration.Close());Done(registration.Close());Done(registration.Pump());
            check(backend.deletes==1&&registration.LobbyId==null,"Repeated shutdown deletes the registered listing once and prevents further work");

            var lateBackend=new Backend{pendingCreate=new TaskCompletionSource<string>()};var late=Registration(lateBackend);var creating=late.Register();var closing=late.Close();lateBackend.pendingCreate.SetResult("late-lobby");
            failed=false;try{Done(creating);}catch(OperationCanceledException){failed=true;}Done(closing);
            check(failed&&lateBackend.deletes==1&&lateBackend.lastDeleted=="late-lobby"&&late.LobbyId==null,"Shutdown waits for an in-flight create and removes its late Lobby response");
        }
    }
}
#endif
