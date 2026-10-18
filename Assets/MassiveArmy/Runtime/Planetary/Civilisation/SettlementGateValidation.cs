using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementHouseholdValidation {
        struct CargoTestJob {public Vector4 start,goal;public uint version,startNode,goalNode,flags;public Vector4 parameters;}
        struct CargoTestPose {public Vector4 position,right,up,forward,properties;}
        struct CargoTestSummary {public uint version,arrived;public float distance;public uint status;}
        void ValidateCargoGate(){
            var shader=Instantiate(Resources.Load<ComputeShader>("MassiveArmy/SettlementRoad"));
            var nodes=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,32);var jobs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,64);var work=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,48);var seen=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4);var parents=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4);var queue=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4);var distances=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4);var summary=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,16);var poses=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,80);
            try {
                int clear=shader.FindKernel("ClearSearch"),navigate=shader.FindKernel("Navigate");shader.SetInt("_RoadStride",2);shader.SetInt("_SearchSlots",1);shader.SetInt("_RoadCount",2);shader.SetInt("_CargoCount",1);shader.SetFloat("_CargoDelta",.5f);shader.SetBuffer(clear,"_RoadSeen",seen);shader.SetBuffer(clear,"_RoadWork",work);shader.Dispatch(clear,1,1,1);
                shader.SetBuffer(navigate,"_RoadNodes",nodes);shader.SetBuffer(navigate,"_RoadJobs",jobs);shader.SetBuffer(navigate,"_RoadWork",work);shader.SetBuffer(navigate,"_RoadSeen",seen);shader.SetBuffer(navigate,"_RoadParents",parents);shader.SetBuffer(navigate,"_RoadQueue",queue);shader.SetBuffer(navigate,"_RoadDistances",distances);shader.SetBuffer(navigate,"_RoadSummary",summary);shader.SetBuffer(navigate,"_CargoPoses",poses);
                Vector4 a=new Vector4(0,120000,0,0),b=new Vector4(8,120000,0,0);
                void Topology(bool open,int revision){nodes.SetData(new[]{new SettlementRoadNetwork.GpuNode{position=a,west=-1,east=open?1:-1,south=-1,north=-1},new SettlementRoadNetwork.GpuNode{position=b,west=open?0:-1,east=-1,south=-1,north=-1}});shader.SetInt("_RoadRevision",revision);}
                var job=new CargoTestJob{start=a,goal=b,version=1,startNode=0,goalNode=1,parameters=new Vector4(1,0,120000,0)};jobs.SetData(new[]{job});Topology(true,1);shader.Dispatch(navigate,1,1,1);var p=new CargoTestPose[1];poses.GetData(p);float moving=p[0].position.x;Check(moving>0&&moving<8,"GPU cargo begins travelling on the open road passage");
                Topology(false,2);for(int i=0;i<16;i++)shader.Dispatch(navigate,1,1,1);poses.GetData(p);Check(Mathf.Abs(p[0].position.x-moving)<.001,"Closing a gate stops an already moving GPU cargo mission without resetting its pose");
                Topology(true,3);for(int i=0;i<20;i++)shader.Dispatch(navigate,1,1,1);var events=new CargoTestSummary[1];summary.GetData(events);poses.GetData(p);Check(events[0].arrived==1&&Mathf.Abs(p[0].position.x-8)<.001,"Reopening the gate resumes and completes the same cargo mission");
                job.start=b;job.goal=a;job.version=2;job.startNode=1;job.goalNode=0;jobs.SetData(new[]{job});Topology(false,4);shader.Dispatch(navigate,1,1,1);summary.GetData(events);Check(events[0].status==3&&events[0].arrived==0,"A cargo search started behind a closed gate reports blocked rather than teleporting");
                Topology(true,5);for(int i=0;i<20;i++)shader.Dispatch(navigate,1,1,1);summary.GetData(events);poses.GetData(p);Check(events[0].version==2&&events[0].arrived==1&&Mathf.Abs(p[0].position.x)<.001,"Opening a gate retries a previously blocked GPU road search with a fresh visit epoch");
            } finally {foreach(var buffer in new[]{nodes,jobs,work,seen,parents,queue,distances,summary,poses})buffer.Dispose();Destroy(shader);}
        }
    }
}
