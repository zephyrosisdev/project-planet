using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CombatVisualEvent
    {
        public Vector4 positionSize;public uint kind,team,seed,rotation;
        public Vector3 Position=>new Vector3(positionSize.x,positionSize.y,positionSize.z);
        public float Angle=>BitConverter.Int32BitsToSingle(unchecked((int)rotation));
    }
    public sealed partial class GpuBattle
    {
        const int StrategicVisualCapacity=256;
        GraphicsBuffer visualEvents;ComputeShader visualPresentationShader;bool visualReadbackPending;double nextVisualReadback;
        public Action<CombatVisualEvent> OnCombatVisual;
        public bool PresentationPaused {get;private set;}
        public int VisualEventsRead {get;private set;}
        void InitializeCombatVisuals()
        {
            visualEvents=Buffer(257+StrategicVisualCapacity,32,"Bounded impact/death/strategic presentation events");visualEvents.SetData(new CombatVisualEvent[257+StrategicVisualCapacity]);
            visualPresentationShader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/CombatPresentation"));
        }
        void BindCombatVisuals(ComputeShader source,int kernel)
        {
            cmd.SetComputeBufferParam(source,kernel,"_CombatEvents",visualEvents);cmd.SetComputeBufferParam(source,kernel,"_VisualFog",fog);
        }
        void CombatVisualParameters(ComputeShader source,Camera camera)
        {
            cmd.SetComputeIntParam(source,"_VisualEnabled",Settings.vfx?1:0);cmd.SetComputeIntParam(source,"_VisualUseFog",Settings.baseBuilding&&!Settings.spectatorMode?1:0);
            if(camera==null)return;
            cmd.SetComputeMatrixParam(source,"_VisualVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,false)*camera.worldToCameraMatrix);
            cmd.SetComputeVectorParam(source,"_VisualFocus",WorldTopology.ViewFocus(camera));
            float vertical=camera.orthographicSize/Mathf.Max(.1f,Mathf.Abs(camera.transform.forward.y));float horizontal=camera.orthographicSize*camera.aspect;
            cmd.SetComputeFloatParam(source,"_VisualCaptureRadius",camera.orthographic?Mathf.Sqrt(vertical*vertical+horizontal*horizontal)+Field.MaximumHeight+32:camera.farClipPlane);
        }
        void RecordCombatVisualReadback(bool paused)
        {
            PresentationPaused=paused;
            if(visualReadbackPending||Time.realtimeSinceStartupAsDouble<nextVisualReadback)return;
            visualReadbackPending=true;nextVisualReadback=Time.realtimeSinceStartupAsDouble+.05;
            int finalize=visualPresentationShader.FindKernel("FinalizeDeaths");cmd.SetComputeBufferParam(visualPresentationShader,finalize,"_CombatEvents",visualEvents);cmd.SetComputeBufferParam(visualPresentationShader,finalize,"_Types",types);cmd.SetComputeTextureParam(visualPresentationShader,finalize,"_Terrain",Field.Terrain);
            cmd.SetComputeFloatParam(visualPresentationShader,"_WorldSize",Field.Size);cmd.SetComputeIntParam(visualPresentationShader,"_NavRes",Field.Resolution);cmd.DispatchCompute(visualPresentationShader,finalize,2,1,1);
            cmd.RequestAsyncReadback(visualEvents,r=>{
                if(disposed)return;visualReadbackPending=false;if(r.hasError)return;
                var data=r.GetData<CombatVisualEvent>();var header=data[0];
                void Read(int start,uint count,int cap){for(int i=0;i<Math.Min(count,(uint)cap);i++){var e=data[start+i];if(e.kind>6)continue;
                    if(e.kind==1){uint hash=e.seed^(uint)Field.Craters.Count*747796405u^unchecked((uint)BitConverter.SingleToInt32Bits(e.positionSize.x));hash^=hash>>16;hash*=2246822519u;hash^=hash>>13;
                        float angle=(hash&0xffffff)*(Mathf.PI*2/16777216);e.rotation=unchecked((uint)BitConverter.SingleToInt32Bits(angle));
                        Field.QueueCrater(new Vector2(e.positionSize.x,e.positionSize.z),Mathf.Clamp(e.positionSize.w,48,400),angle,e.team==uint.MaxValue);}
                    VisualEventsRead++;OnCombatVisual?.Invoke(e);}}
                Read(257,header.seed,StrategicVisualCapacity);Read(129,header.team,128);Read(1,header.kind,128);
            });
            int clear=visualPresentationShader.FindKernel("ClearEvents");cmd.SetComputeBufferParam(visualPresentationShader,clear,"_CombatEvents",visualEvents);cmd.DispatchCompute(visualPresentationShader,clear,1,1,1);
        }
        public void ReportStructureDestruction(Vector3 position,float size)
        {
            if(Settings.vfx)OnCombatVisual?.Invoke(new CombatVisualEvent{positionSize=new Vector4(position.x,position.y,position.z,size),kind=6,seed=Tick});
        }
        void DisposeCombatVisuals(){OnCombatVisual=null;visualEvents?.Dispose();if(Application.isPlaying)UnityEngine.Object.Destroy(visualPresentationShader);else UnityEngine.Object.DestroyImmediate(visualPresentationShader);}
    }
}
