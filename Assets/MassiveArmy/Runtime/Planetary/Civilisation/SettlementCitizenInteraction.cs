using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementCitizens {
        GraphicsBuffer personPick,personPose;bool pickPersonPending,followPersonPending;
        readonly uint[] emptyPersonPick={uint.MaxValue};uint[] pickGenerations=Array.Empty<uint>();Ray queuedPersonRay;Action<ColonyState,ResidentState> queuedPersonCallback;
        internal void PickPerson(Ray ray,Action<ColonyState,ResidentState> callback){
            if(disposed||civicStates==null||poses==null||Count==0)return;
            if(pickPersonPending){queuedPersonRay=ray;queuedPersonCallback=callback;return;}
            if(pickGenerations.Length<Count)pickGenerations=new uint[Mathf.NextPowerOfTwo(Count)];for(int i=0;i<Count;i++)pickGenerations[i]=generations[i];
            personPick??=Buffer(1,4);personPick.SetData(emptyPersonPick);int kernel=shader.FindKernel("PickPerson");shader.SetBuffer(kernel,"_CivicStates",civicStates);shader.SetBuffer(kernel,"_CargoPoses",poses);shader.SetBuffer(kernel,"_NearbyPerson",personPick);
            var origin=world.RenderToBody(ray.origin);var high=origin.Float;shader.SetVector("_PersonRayHigh",high);shader.SetVector("_PersonRayLow",(origin-new Double3(high)).Float);shader.SetVector("_PersonRayDirection",ray.direction);
            shader.SetFloat("_PersonRayCone",2*Mathf.Tan(world.View.fieldOfView*Mathf.Deg2Rad*.5f)*10/Mathf.Max(1,world.View.pixelHeight));
            float limit=Mathf.Max(1,Mathf.Min(world.View.farClipPlane,world.SystemRuntime.Civilisation.Settings.civilianDrawDistance+16));Physics.SyncTransforms();if(Physics.Raycast(ray,out var occluder,limit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))limit=Mathf.Min(limit,occluder.distance+.25f);shader.SetFloat("_PersonRayLimit",limit);
            shader.SetInt("_WalkerCount",Count);shader.Dispatch(kernel,(Count+127)/128,1,1);pickPersonPending=true;
            AsyncGPUReadback.Request(personPick,result=>{pickPersonPending=false;if(disposed)return;if(queuedPersonCallback!=null){var next=queuedPersonCallback;queuedPersonCallback=null;PickPerson(queuedPersonRay,next);return;}if(result.hasError)return;uint hit=result.GetData<uint>()[0];if(hit==uint.MaxValue)return;int slot=(int)(hit&65535);if(slot<residents.Count&&residents[slot].person!=null&&generations[slot]==pickGenerations[slot])callback?.Invoke(residents[slot].colony,residents[slot].person);});
        }
        internal void TrackPerson(long person,Action<Vector3> callback){if(disposed||followPersonPending||civicStates==null||!slots.TryGetValue(person,out int slot))return;personPose??=Buffer(1,16);int kernel=shader.FindKernel("TrackPerson");shader.SetBuffer(kernel,"_CivicStates",civicStates);shader.SetBuffer(kernel,"_PersonPose",personPose);shader.SetInt("_PersonSlot",slot);shader.Dispatch(kernel,1,1,1);followPersonPending=true;uint version=generationSerial;UnityEngine.Rendering.AsyncGPUReadback.Request(personPose,result=>{followPersonPending=false;if(disposed||result.hasError||version!=generationSerial)return;var value=result.GetData<Vector4>()[0];callback(value.w>0?((Vector3)value).normalized:Vector3.zero);});}
        GraphicsBuffer nearbyPerson;bool personPending;
        internal void QueryNearbyPerson(Vector3 point,bool officerOnly,Action<ColonyState,ResidentState> callback){
            if(disposed||personPending||civicStates==null||Count==0)return;nearbyPerson??=Buffer(1,4);nearbyPerson.SetData(new[]{uint.MaxValue});int kernel=shader.FindKernel("FindNearbyPerson");shader.SetBuffer(kernel,"_CivicStates",civicStates);shader.SetBuffer(kernel,"_NearbyPerson",nearbyPerson);shader.SetVector("_PersonQueryPoint",point);shader.SetInt("_PersonQueryOfficers",officerOnly?1:0);shader.SetInt("_WalkerCount",Count);shader.Dispatch(kernel,(Count+127)/128,1,1);personPending=true;uint version=generationSerial;
            AsyncGPUReadback.Request(nearbyPerson,result=>{personPending=false;if(disposed||result.hasError||version!=generationSerial)return;uint pick=result.GetData<uint>()[0];if(pick==uint.MaxValue)return;int slot=(int)(pick&65535);if(slot>=residents.Count)return;var entry=residents[slot];if(entry.person?.alive==true&&!entry.person.recruited)callback(entry.colony,entry.person);});
        }
    }
}
