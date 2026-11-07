using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace MassiveArmy.EditorTools {
    public static class VehicleRtsValidation {
        [StructLayout(LayoutKind.Sequential)]struct Command {public Vector4 position,velocity,heading;public uint id,mode,weapons,padding;}
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        public static void StartLive(){
            var app=Object.FindFirstObjectByType<BattleApp>();if(app==null||!EditorApplication.isPlaying)throw new InvalidOperationException("Enter Play mode first");
            if(Object.FindFirstObjectByType<PlanetDevelopment>()==null)typeof(BattleApp).GetMethod("StartPlanetMatch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(app,null);
        }
        static int probeId=-1;static Vector3 probeStart;
        public static void ProbeLive(){
            var world=Object.FindFirstObjectByType<PlanetDevelopment>();if(world?.Ready!=true)throw new InvalidOperationException("No ready planet");
            var units=(GraphicsBuffer)typeof(PlanetDevelopment).GetField("units",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(world);probeId=Math.Min(2,world.unitCount-1);var data=new UnitState[1];units.GetData(data,0,probeId,1);data[0].flags|=2u;units.SetData(data,0,probeId,1);probeStart=CubeSphere.Decode(data[0].position);
            var right=Vector3.ProjectOnPlane(CubeSphere.Right[Mathf.FloorToInt(data[0].position.x)],probeStart).normalized;
            bool issued=world.OrderSelected((probeStart+right*(400/world.definition.radius)).normalized);File.WriteAllText("TestResults/Vehicles/live-order.txt","issued="+issued+" status="+world.Commands.Status+" radius="+world.definition.radius+" id="+probeId);DiagnoseLive();
        }
        public static void DiagnoseLive(){
            var output=new System.Text.StringBuilder();output.AppendLine("playing="+EditorApplication.isPlaying+" paused="+EditorApplication.isPaused+" timeScale="+Time.timeScale);
            foreach(var world in Object.FindObjectsByType<PlanetDevelopment>(FindObjectsSortMode.None)){
                output.AppendLine("world="+world.name+" ready="+world.Ready+" mode="+world.sessionMode+" paused="+world.Paused+" active="+world.SimulationActive+" tick="+world.SimulationTicks+" count="+world.unitCount+" commands="+world.Commands?.Status);
                if(!world.Ready)continue;
                if(probeId>=0){var sample=new UnitState[1];((GraphicsBuffer)typeof(PlanetDevelopment).GetField("units",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(world)).GetData(sample,0,probeId,1);output.AppendLine("probe travelled="+CubeSphere.ArcDistance(probeStart,CubeSphere.Decode(sample[0].position),world.definition.radius));}
                int count=Math.Min(64,world.unitCount);var states=new UnitState[count];((GraphicsBuffer)typeof(PlanetDevelopment).GetField("units",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(world)).GetData(states,0,0,count);
                for(int i=0;i<count;i++){var u=states[i];output.AppendLine(i+" flags="+u.flags+" type="+((u.packed>>8)&255)+" group="+u.group+" pos="+u.position.ToString("R")+" goal="+u.destination.ToString("R")+" velocity="+u.velocity+" facing="+u.facing);}
            }
            File.WriteAllText("TestResults/Vehicles/live-movement.txt",output.ToString());
        }
        public static void CheckLargePlanetMotion(){
            var report=new Report();ComputeShader shader=null;PlanetDefinition definition=null;var buffers=new List<GraphicsBuffer>();
            GraphicsBuffer Buffer(int count,int stride){var b=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,stride);buffers.Add(b);return b;}
            Double3 Exact(UnitState u){int face=Mathf.FloorToInt(u.position.x);var low=(u.flags&268435456u)!=0?u.previous:Vector2.zero;var cube=new Double3(CubeSphere.Normal[face])+new Double3(CubeSphere.Right[face])*(2*((double)u.position.x-face+low.x)-1)+new Double3(CubeSphere.Up[face])*(2*((double)u.position.y+low.y)-1);return cube*(1/cube.Magnitude);}
            try{
                definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.terrainHeight=0;definition.oceanHeight=-100;definition.stampTerrain=false;
                shader=Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetDevelopment"));shader.EnableKeyword("PLANET_VEHICLES");shader.EnableKeyword("PLANET_GROUP_COMMANDS");int kernel=shader.FindKernel("Move");int cells=6*16*16;
                var units=Buffer(1,64);var costs=Buffer(cells*5,4);var field=Buffer(cells*18,4);var groups=Buffer(18,64);var values=new float[cells*5];Array.Fill(values,1);costs.SetData(values);field.SetData(new float[cells*18]);shader.SetBuffer(kernel,"_PlanetUnits",units);shader.SetBuffer(kernel,"_PlanetCosts",costs);shader.SetBuffer(kernel,"_PlanetField",field);shader.SetBuffer(kernel,"_Groups",groups);shader.SetInt("_PlanetUnitCount",1);shader.SetInt("_PlanetNavRes",16);shader.SetFloat("_PlanetSpeed",.8f);shader.SetFloat("_PlanetDt",1f/30);
                foreach(float radius in new[]{60000f,12000000f,120000000f})foreach(int face in new[]{0,1,2,3,4,5}){
                    definition.radius=radius;PlanetDevelopment.BindSurface(shader,definition);var direction=CubeSphere.Direction((CubeFace)face,new Vector2(.08718f,.18631f));var right=Vector3.ProjectOnPlane(CubeSphere.Right[face],direction).normalized;
                    var initial=new UnitState{position=CubeSphere.Encode(direction),flags=1u|1048576u,target=uint.MaxValue,health=1000};initial.previous=initial.position;var start=Exact(initial);units.SetData(new[]{initial});var orders=new GroupOrder[18];orders[0].destination=CubeSphere.Encode((direction+right*(1000/radius)).normalized);groups.SetData(orders);
                    for(int frame=0;frame<900;frame++)shader.Dispatch(kernel,1,1,1);var result=new UnitState[1];units.GetData(result);double travelled=((Exact(result[0])-start)*radius).Magnitude;
                    if(travelled<18||travelled>30)throw new InvalidOperationException("radius="+radius+" face="+face+" slow unit travelled "+travelled+"m instead of about 24m");report.checks.Add("radius="+radius+" face="+face+": "+travelled+"m at .8m/s for 30s");
                    var grid=new PlanetConstructionGrid(1,radius,32);var cell=grid.Cell(direction);for(int edge=0;edge<4;edge++){var tangent=grid.NeighbourTangent(cell,edge);if(tangent.sqrMagnitude<.99f||Math.Abs(Vector3.Dot(tangent,grid.Direction(cell)))>.001)throw new InvalidOperationException("Invalid road sign tangent at radius="+radius+" face="+face+" edge="+edge);}
                }
                report.checks.Add("72 road sign edge directions remain nonzero and tangent through 1000x scale");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();}finally{foreach(var b in buffers)b.Dispose();if(shader!=null)Object.DestroyImmediate(shader);if(definition!=null)Object.DestroyImmediate(definition);File.WriteAllText("TestResults/Vehicles/large-planet-motion.json",JsonUtility.ToJson(report,true));}
        }
        [MenuItem("Synthetic Front/Validation/Check RTS vehicle steering and authority")]
        public static void Run(){
            var report=new Report();var buffers=new List<GraphicsBuffer>();ComputeShader move=null,control=null;PlanetDefinition definition=null;GameObject fixture=null;VehicleRigPool pool=null;
            void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);report.checks.Add(text);}
            GraphicsBuffer Buffer(int count,int stride){var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,stride);buffers.Add(buffer);return buffer;}
            try{
                definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.radius=60000;definition.terrainHeight=0;definition.oceanHeight=-100;definition.stampTerrain=false;
                var surface=new PlanetSurface(definition);var up=new Vector3(.3f,.8f,.5f).normalized;var encoded=CubeSphere.Encode(up);int face=Mathf.FloorToInt(encoded.x);var right=Vector3.ProjectOnPlane(CubeSphere.Right[face],up).normalized;var forward=Vector3.Cross(right,up);var point=surface.GetSurfacePositionPrecise(up,.025);
                fixture=new GameObject("Temporary RTS vehicle validation");var world=fixture.AddComponent<PlanetDevelopment>();world.definition=definition;var camera=new GameObject("Validation camera").AddComponent<Camera>();camera.transform.SetParent(fixture.transform,false);
                var fields=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;typeof(PlanetDevelopment).GetField("origin",fields).SetValue(world,new FloatingOrigin(definition.position.Add(point)));typeof(PlanetDevelopment).GetField("view",fields).SetValue(world,camera);pool=new VehicleRigPool(fixture.transform);
                var ids=new[]{"SF-TK1","SF-MW1","SF-LUV1","SF-QB1"};var states=new UnitState[4];
                for(int i=0;i<ids.Length;i++){
                    var profile=Resources.Load<VehicleDefinition>("MassiveArmy/Vehicles/"+ids[i]);var contact=new VehicleTerrainContact(surface,p=>(p-point).Float,p=>point+new Double3(p));
                    using(var v=new VehicleController(world,contact,pool,profile,(uint)i,new SpacePose{position=point,rotation=Quaternion.LookRotation(forward,up)})){
                        v.SetDetail(true);Check(v.Detailed&&!v.LocalMovement&&!v.LocalPhysics,ids[i]+": nearby detailed AI retains GPU movement authority");
                        var state=new UnitState{position=encoded,previous=encoded,flags=1u|1048576u,health=1000,target=uint.MaxValue,facing=Mathf.PI*.5f};v.FollowGpu(state);v.Tick(0,default,null);
                        Check(Vector3.Dot(v.Motor.Pose.rotation*Vector3.forward,right)>.99f,ids[i]+": detailed rig follows GPU turn");
                        Check(v.Seats.TryEnterPlayer(0)&&v.LocalMovement&&v.LocalPhysics,ids[i]+": driver seat takes local authority");v.Seats.LeavePlayer();Check(!v.LocalPhysics,ids[i]+": driver exit returns to GPU authority");
                    }
                    states[i]=new UnitState{position=encoded,previous=encoded,flags=1u|1048576u,health=1000,target=uint.MaxValue};
                }
                move=Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetDevelopment"));PlanetDevelopment.BindSurface(move,definition);move.EnableKeyword("PLANET_VEHICLES");move.EnableKeyword("PLANET_GROUP_COMMANDS");int kernel=move.FindKernel("Move");
                int cells=6*16*16;var units=Buffer(4,64);var costs=Buffer(cells*5,4);var field=Buffer(cells*18,4);var groups=Buffer(18,64);var costsData=new float[cells*5];Array.Fill(costsData,1);costs.SetData(costsData);field.SetData(new float[cells*18]);units.SetData(states);
                var orders=new GroupOrder[18];orders[0].destination=CubeSphere.Encode((up+right*.01f).normalized);groups.SetData(orders);
                move.SetBuffer(kernel,"_PlanetUnits",units);move.SetBuffer(kernel,"_PlanetCosts",costs);move.SetBuffer(kernel,"_PlanetField",field);move.SetBuffer(kernel,"_Groups",groups);move.SetInt("_PlanetNavRes",16);move.SetInt("_PlanetUnitCount",4);move.SetFloat("_PlanetRadius",60000);move.SetFloat("_PlanetHeight",0);move.SetFloat("_PlanetOcean",-100);move.SetFloat("_PlanetSeed",0);move.SetFloat("_PlanetDt",.1f);move.SetFloat("_PlanetSpeed",20);
                for(int frame=0;frame<120;frame++)move.Dispatch(kernel,1,1,1);units.GetData(states);
                for(int i=0;i<4;i++)Check(CubeSphere.ArcDistance(up,CubeSphere.Decode(states[i].position),60000)>150&&Mathf.Abs(states[i].facing-Mathf.PI*.5f)<.1f,ids[i]+": GPU flow path moves and turns toward a lateral destination");
                orders[0].destination=CubeSphere.Encode((up-right*.01f).normalized);groups.SetData(orders);move.Dispatch(kernel,1,1,1);units.GetData(states);Check(states[0].facing< -1,"GPU retarget reverses heading");
                control=Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetVehicles"));int commandKernel=control.FindKernel("Control");var commands=Buffer(4,64);var data=new Command[4];for(int i=0;i<4;i++)data[i]=new Command{id=(uint)i,mode=1,weapons=1,position=(up*60000),heading=forward};commands.SetData(data);control.SetBuffer(commandKernel,"_PlanetUnits",units);control.SetBuffer(commandKernel,"_VehicleCommands",commands);control.SetInt("_VehicleCount",4);control.SetInt("_PlanetUnitCount",4);
                control.Dispatch(commandKernel,1,1,1);units.GetData(states);Check((states[0].flags&(4u|2097152u))==(4u|2097152u),"Local takeover stops the previous route");
                for(int i=0;i<4;i++)data[i].mode=0;commands.SetData(data);control.Dispatch(commandKernel,1,1,1);units.GetData(states);Check((states[0].flags&2097152u)==0&&(states[0].flags&4u)!=0,"Normal release stays stopped instead of resuming an old route");
                for(int i=0;i<4;i++){states[i].flags|=2097152u;states[i].flags&=~4u;data[i].mode=1;}units.SetData(states);commands.SetData(data);control.Dispatch(commandKernel,1,1,1);units.GetData(states);Check((states[0].flags&4u)==0,"A fresh GPU move order survives repeated local updates");
                for(int i=0;i<4;i++)data[i].mode=0;commands.SetData(data);control.Dispatch(commandKernel,1,1,1);move.Dispatch(kernel,1,1,1);units.GetData(states);Check((states[0].flags&(2097152u|4u))==0&&states[0].velocity.magnitude>1,"Fresh move order continues after handoff");
            }catch(Exception error){report.status="FAIL";report.error=error.ToString();}
            finally{pool?.Dispose();if(fixture!=null)Object.DestroyImmediate(fixture);if(definition!=null)Object.DestroyImmediate(definition);if(move!=null)Object.DestroyImmediate(move);if(control!=null)Object.DestroyImmediate(control);foreach(var buffer in buffers)buffer.Dispose();Directory.CreateDirectory("TestResults/Vehicles");File.WriteAllText("TestResults/Vehicles/rts-steering.json",JsonUtility.ToJson(report,true));}
        }
    }
}
