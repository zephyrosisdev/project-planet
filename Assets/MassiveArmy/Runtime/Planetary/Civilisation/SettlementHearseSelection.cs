using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementCargoNavigation {
        GraphicsBuffer pickResult;bool pickPending;uint pickSerial;
        internal static int NearestRoad(PlanetRtsBase local,Vector2 point,int team){var direction=CubeSphere.Decode(point);float best=float.MaxValue;int node=-1;for(int i=0;i<local.Roads.Nodes.Length;i++){var road=local.Roads.Nodes[i];if((int)road.position.w!=team)continue;float score=(direction-((Vector3)road.position).normalized).sqrMagnitude;if(score<best){best=score;node=i;}}return node;}
        public void PickHearse(Ray ray,Action<CargoVehicleState> selected){if(disposed||poses==null||pickPending)return;pickPending=true;uint requestId=++pickSerial;
            if(pickResult==null)pickResult=Buffer(1,4);pickResult.SetData(new[]{uint.MaxValue});int k=shader.FindKernel("PickHearse");shader.SetBuffer(k,"_CargoPoses",poses);shader.SetBuffer(k,"_PickResult",pickResult);shader.SetInt("_PickCount",vehicles.Count);shader.SetVector("_PickOrigin",ray.origin-world.PlanetRenderCentre);shader.SetVector("_PickDirection",ray.direction);shader.Dispatch(k,(vehicles.Count+127)/128,1,1);
            AsyncGPUReadback.Request(pickResult,result=>{pickPending=false;if(disposed||result.hasError||requestId!=pickSerial)return;uint hit=result.GetData<uint>()[0];if(hit==uint.MaxValue)return;int slot=(int)(hit&65535);if(slot<vehicles.Count&&vehicles[slot].funeral)selected(vehicles[slot]);});
        }
    }
}
