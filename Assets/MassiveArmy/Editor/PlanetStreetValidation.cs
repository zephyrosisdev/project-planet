using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace MassiveArmy.EditorTools {
    public static class PlanetStreetValidation {
        struct Node {public Vector4 position;public int west,east,south,north;}
        struct Job {public Vector4 start,goal;public uint version,startNode,goalNode,flags;public Vector4 parameters;}
        struct Pose {public Vector4 position,right,up,forward,properties;}
        struct Summary {public uint version,arrived;public float distance;public uint status;}
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Planetary/Validate street scale and GPU lanes")]
        public static void Run(){
            var report=new Report();var owned=new List<GraphicsBuffer>();ComputeShader shader=null;
            void Check(bool pass,string message){if(!pass)throw new InvalidOperationException(message);report.checks.Add(message);}
            GraphicsBuffer Buffer(int count,int stride){var b=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,stride);owned.Add(b);return b;}
            try{
                var grid=new PlanetConstructionGrid(1,120000,32);var old=new PlanetConstructionGrid(1,120000,8);var definition=new BuildingDefinition{footprintWidth=5,footprintDepth=5};
                Check(old.CarriagewayWidth<old.TileSize&&old.RoadLaneOffset==0,"Legacy saved grids retain a narrow centreline road instead of overlapping their existing buildings");
                Check(grid.PresentationScale==4&&grid.Resolution*4==old.Resolution,"32-metre construction grid is four times the original scale");
                Check(Mathf.Abs(grid.FootprintRadius(definition)/old.FootprintRadius(definition)-4)<.001,"Collision and navigation footprint radii grow with building size");
                Check(PlanetConstructionGrid.RoadWidth>=8*.34f*4&&PlanetConstructionGrid.RoadWidth*.5f>5.43f,"Two six-metre lanes fit the existing largest ground vehicle and exceed four times the old road width");
                for(int face=0;face<6;face++)foreach(var direction in new[]{new Vector2(0,0),new Vector2(.999f,.999f),new Vector2(-.999f,.999f)}){
                    var cell=grid.Cell(CubeSphere.Direction((CubeFace)face,direction));var a=grid.Point(cell.face,cell.x,cell.y);var b=grid.Point(cell.face,cell.x+1,cell.y);var c=grid.Point(cell.face,cell.x,cell.y+1);
                    Check(Mathf.Min(Vector3.Distance(a,b),Vector3.Distance(a,c))*grid.Radius>PlanetConstructionGrid.RoadWidth+2,"Carriageway and shoulders fit the curved tile on face "+face);
                }
                shader=Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/SettlementRoad"));int clear=shader.FindKernel("ClearSearch"),navigate=shader.FindKernel("Navigate");
                var nodes=Buffer(4,32);var jobs=Buffer(2,64);var work=Buffer(2,48);var seen=Buffer(8,4);var parents=Buffer(8,4);var queue=Buffer(8,4);var distances=Buffer(8,4);var summary=Buffer(2,16);var poses=Buffer(2,80);
                foreach(var binding in new[]{("_RoadNodes",nodes),("_RailNodes",nodes),("_RoadJobs",jobs),("_RoadWork",work),("_RoadSeen",seen),("_RoadParents",parents),("_RoadQueue",queue),("_RoadDistances",distances),("_RoadSummary",summary),("_CargoPoses",poses)})shader.SetBuffer(navigate,binding.Item1,binding.Item2);
                shader.SetBuffer(clear,"_RoadSeen",seen);shader.SetBuffer(clear,"_RoadWork",work);shader.SetInt("_RoadStride",4);shader.SetInt("_SearchSlots",2);shader.SetInt("_StorageWidth",128);shader.SetInt("_RoadCount",4);shader.SetInt("_RailCount",4);shader.SetInt("_CargoCount",2);shader.SetInt("_RoadRevision",1);shader.SetFloat("_CargoDelta",.1f);shader.SetFloat("_RoadLaneOffset",PlanetConstructionGrid.LaneOffset);
                var points=new[]{new Vector4(-64,120000,0,0),new Vector4(0,120000,0,0),new Vector4(64,120000,0,0),new Vector4(0,120000,64,0)};
                Node N(int i,int w,int e,int n)=>new Node{position=points[i],west=w,east=e,south=-1,north=n};
                var graph=new[]{N(0,-1,1,-1),N(1,0,2,3),N(2,1,-1,-1),N(3,-1,-1,-1)};graph[3].south=1;nodes.SetData(graph);
                Job J(int a,int b,uint version,uint flags=0)=>new Job{start=points[a],goal=points[b],startNode=(uint)a,goalNode=(uint)b,version=version,flags=flags,parameters=new Vector4(12,0,120000,11)};
                jobs.SetData(new[]{J(0,2,1),J(2,0,1)});shader.Dispatch(clear,1,1,1);var p=new Pose[2];float closest=float.MaxValue;
                for(int step=0;step<100;step++){shader.Dispatch(navigate,1,1,1);poses.GetData(p);float gap=Vector3.Distance(p[0].position,p[1].position);closest=Mathf.Min(closest,gap);if(step==20)Check(p[0].position.z<0&&p[1].position.z>0,"Opposing GPU traffic keeps to opposite sides of the road");}
                Check(closest>=5.43f+.4f,"Opposing vehicles pass with clearance, without CPU vehicle transforms");
                jobs.SetData(new[]{J(0,3,2),J(3,0,2)});shader.Dispatch(navigate,1,1,1);poses.GetData(p);var previous=p[0].position;float maximumStep=0;
                for(int step=0;step<110;step++){shader.Dispatch(navigate,1,1,1);poses.GetData(p);maximumStep=Mathf.Max(maximumStep,Vector3.Distance(previous,p[0].position));previous=p[0].position;}
                Check(maximumStep<1.5f,"Lane joins remain continuous through a ninety-degree corner");
                var events=new Summary[2];summary.GetData(events);Check(events[0].arrived==1&&events[1].arrived==1,"Both opposing routes finish at their intended road ports");
                jobs.SetData(new[]{J(0,2,3),J(2,0,3)});for(int i=0;i<10;i++)shader.Dispatch(navigate,1,1,1);poses.GetData(p);previous=p[0].position;
                graph[0].east=-1;graph[1].west=-1;nodes.SetData(graph);shader.SetInt("_RoadRevision",2);for(int i=0;i<20;i++)shader.Dispatch(navigate,1,1,1);poses.GetData(p);
                Check(Vector3.Distance(previous,p[0].position)<.001f,"A closed road preserves the GPU lane pose without adding the lane offset repeatedly");
                graph[0].east=1;graph[1].west=0;nodes.SetData(graph);shader.SetInt("_RoadRevision",3);jobs.SetData(new[]{J(0,2,4,16),J(2,0,4,16)});shader.Dispatch(navigate,1,1,1);poses.GetData(p);
                Check(Mathf.Abs(p[0].position.z)<.001f&&Mathf.Abs(p[1].position.z)<.001f,"Rail traffic retains its track centreline");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{foreach(var buffer in owned)buffer.Dispose();if(shader!=null)Object.DestroyImmediate(shader);}
            Directory.CreateDirectory("TestResults/Planetary/StreetScale");File.WriteAllText("TestResults/Planetary/StreetScale/validation.json",JsonUtility.ToJson(report,true));Debug.Log("Street scale validation "+report.status);if(report.status!="PASS")throw new InvalidOperationException(report.error);
        }
    }
}
