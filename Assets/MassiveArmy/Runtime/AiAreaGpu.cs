using UnityEngine;
namespace MassiveArmy
{
    [System.Serializable]public struct AiAreaReport{public bool valid;public uint tick,hostiles,combatants,observed,cells;}
    public sealed partial class GpuBattle
    {
        readonly Vector4[] aiAreas=new Vector4[2];readonly uint[] aiAreaRevision=new uint[2];readonly AiAreaReport[] aiAreaReports=new AiAreaReport[2];
        readonly bool[] aiAreaPending=new bool[2];readonly uint[] aiAreaNextTick=new uint[2];
        readonly GraphicsBuffer[] aiAreaResults=new GraphicsBuffer[2];ComputeShader aiAreaShader;
        public void SetAiAreaQuery(int team,Vector2 position,float radius,bool enabled)
        {if(team<0||team>1)return;aiAreas[team]=new Vector4(position.x,position.y,Mathf.Clamp(radius,32,256),enabled?1:0);aiAreaRevision[team]++;aiAreaReports[team]=default;aiAreaNextTick[team]=0;}
        public AiAreaReport GetAiAreaReport(int team)=>aiAreaReports[team];
        void RecordAiAreaQueries()
        {
            for(int team=0;team<2;team++){
                if(aiAreas[team].w==0||aiAreaPending[team]||Tick<aiAreaNextTick[team])continue;
                if(aiAreaShader==null)aiAreaShader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/AiAreaQuery"));
                if(aiAreaResults[team]==null)aiAreaResults[team]=Buffer(4,4,"AI local clearance aggregate "+team);
                var result=aiAreaResults[team];int clear=aiAreaShader.FindKernel("Clear"),scan=aiAreaShader.FindKernel("Scan");var area=aiAreas[team];
                cmd.SetComputeBufferParam(aiAreaShader,clear,"_Result",result);cmd.DispatchCompute(aiAreaShader,clear,1,1,1);
                cmd.SetComputeBufferParam(aiAreaShader,scan,"_Result",result);cmd.SetComputeBufferParam(aiAreaShader,scan,"_Units",current);cmd.SetComputeBufferParam(aiAreaShader,scan,"_Types",types);
                cmd.SetComputeBufferParam(aiAreaShader,scan,"_CellCounts",counts);cmd.SetComputeBufferParam(aiAreaShader,scan,"_CellOffsets",offsets);cmd.SetComputeBufferParam(aiAreaShader,scan,"_Indices",indices);cmd.SetComputeBufferParam(aiAreaShader,scan,"_Fog",fog);
                int ring=Mathf.CeilToInt(area.z/(Field.Size/Settings.spatialResolution))+1,side=ring*2+1;
                cmd.SetComputeIntParam(aiAreaShader,"_GridRes",Settings.spatialResolution);cmd.SetComputeIntParam(aiAreaShader,"_FogRes",FogResolution);cmd.SetComputeIntParam(aiAreaShader,"_Team",team);cmd.SetComputeIntParam(aiAreaShader,"_Ring",ring);cmd.SetComputeIntParam(aiAreaShader,"_Side",side);cmd.SetComputeIntParam(aiAreaShader,"_Active",Settings.activeUnits);
                cmd.SetComputeFloatParam(aiAreaShader,"_WorldSize",Field.Size);cmd.SetComputeVectorParam(aiAreaShader,"_Area",area);
                cmd.DispatchCompute(aiAreaShader,scan,side*side,1,1);aiAreaPending[team]=true;aiAreaNextTick[team]=Tick+30;
                int capturedTeam=team;uint revision=aiAreaRevision[team],tick=Tick;
                cmd.RequestAsyncReadback(result,r=>{if(disposed)return;aiAreaPending[capturedTeam]=false;if(r.hasError||revision!=aiAreaRevision[capturedTeam])return;var data=r.GetData<uint>();aiAreaReports[capturedTeam]=new AiAreaReport{valid=true,tick=tick,hostiles=data[0],combatants=data[1],observed=data[2],cells=data[3]};});
            }
        }
        void DisposeAiAreaQueries(){foreach(var buffer in aiAreaResults)buffer?.Dispose();ArmyLifetime.Destroy(aiAreaShader);}
    }
}
