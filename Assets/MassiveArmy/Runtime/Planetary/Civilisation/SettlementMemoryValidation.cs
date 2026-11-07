using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    // Standalone equivalent-scenario comparison of the saved old and bounded GPU
    // cargo solvers. No CPU army state and no change to the million-unit path.
    public sealed class SettlementMemoryValidation:MonoBehaviour {
        struct Node {public Vector4 position;public int west,east,south,north;}
        struct Job {public Vector4 start,goal;public uint version,startNode,goalNode,flags;public Vector4 parameters;}
        struct Summary {public uint version,arrived;public float distance;public uint status;}
        [Serializable]sealed class Result {public string implementation;public int roads,vehicles,frames,completed,searchSlots;public double medianMs,p95Ms,ticksPerSecond,seconds;public long searchBytes,bufferBytes,unityAllocatedBytes,unityReservedBytes,graphicsDriverBytes,workingSetBytes,privateBytes;}
        [Serializable]sealed class Report {public string status="PASS",error="",scope="Standalone synthetic cargo navigation; same graph, fleet, fixed ticks and retarget schedule. No army/rendering performance claim.";public List<string> checks=new List<string>();public List<Result> results=new List<Result>();}
        [StructLayout(LayoutKind.Sequential)]struct MemoryCounters {public uint size,pageFaults;public UIntPtr peakWorkingSet,workingSet,peakPaged,paged,peakNonPaged,nonPaged,pagefile,peakPagefile,privateBytes;}
        [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")]static extern bool GetProcessMemoryInfo(IntPtr process,out MemoryCounters counters,uint size);
        readonly Report report=new Report();string output;int errors;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"-memory-navigation-validation")>=0)new GameObject("Memory navigation validation").AddComponent<SettlementMemoryValidation>();}
        void Awake(){DontDestroyOnLoad(gameObject);var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-planet-output");output=at>=0?args[at+1]:"MemoryValidation";Directory.CreateDirectory(output);Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;Application.runInBackground=true;Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors++;}
        IEnumerator Start(){var iterator=Run();while(true){bool more=false;object current=null;try{more=iterator.MoveNext();if(more)current=iterator.Current;}catch(Exception e){report.status="FAIL";report.error=e.ToString();break;}if(!more)break;yield return current;}
            if(errors>0){report.status="FAIL";report.error+=" Runtime errors: "+errors;}File.WriteAllText(Path.Combine(output,"comparison.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);
        }
        void Check(bool condition,string description){if(!condition)throw new InvalidOperationException(description);report.checks.Add(description);}
        IEnumerator Run(){
            ValidateArchive();Check(SettlementCargoNavigation.SearchSlotCount(65536,256,16,64)==16,"Cargo scratch is pooled independently of fleet capacity");
            Check(SettlementCitizens.FlowGoalCount(131072,2000,128,32)==32,"Pedestrian flow cache obeys 32 MiB budget");
            foreach(bool pooled in new[]{false,true}){
                var routine=Scenario(pooled,65536,256,30000,64,120);while(routine.MoveNext())yield return routine.Current;
            }
            var saturated=Scenario(true,4096,96,1200,64,120,false,4);while(saturated.MoveNext())yield return saturated.Current;
            var longRoute=Scenario(true,8192,1,1900,4096,0);while(longRoute.MoveNext())yield return longRoute.Current;
            var invalid=Scenario(true,128,1,200,64,0,true);while(invalid.MoveNext())yield return invalid.Current;
            Check(report.results[1].searchBytes<report.results[0].searchBytes*4/5,"Equivalent fleet uses less than eighty percent of baseline cargo scratch memory");
        }
        void ValidateArchive(){string root=Path.Combine(output,"ArchiveFixture");var store=new SettlementHistoryStore(root);
            foreach(long id in new[]{1L,1023,1024,1025,1000000}){string json="{\"id\":"+id+",\"name\":\"Test α\"}";store.Put(12,"deaths",id,json);Check(store.Get(12,"deaths",id)==json,"Indexed archive roundtrip across bucket boundary: "+id);}
            var portable=new SettlementHistoryStore(Path.Combine(output,"PortableFixture"));
            var death=new DeathRecord{id=90,resident=91,name="History citizen",interred=true};var person=new ResidentState{id=91,name="History citizen",alive=false,parentA=75};var crime=new CrimeCase{id=92,suspect=91,closed=true};
            portable.Put(7,"deaths",90,JsonUtility.ToJson(death));portable.Put(7,"residents",91,JsonUtility.ToJson(person));portable.Put(7,"cases",92,JsonUtility.ToJson(crime));
            var state=new UniverseState{historyArchive="test"};state.colonies.Add(new ColonyState{id=7,archivedDeaths=1,archivedResidents=1,archivedCases=1});
            var expanded=CivilisationRuntime.ExpandForSave(state,portable);Check(expanded.colonies[0].deaths.Count==1&&expanded.colonies[0].residents[0].parentA==75&&expanded.colonies[0].crime.cases.Count==1,"Portable saves include archived identities, relations, deaths and cases");
            Check(state.colonies[0].residents.Count==0&&state.colonies[0].archivedResidents==1,"Save export does not repopulate the live working set");
            bool rejected=false;try{CivilisationRuntime.ExpandForSave(state,new SettlementHistoryStore(Path.Combine(output,"MissingArchive")));}catch(InvalidDataException){rejected=true;}Check(rejected,"Missing archives cannot silently produce an incomplete save");
            Check(store.Get(12,"deaths",99)==null,"Archive missing ID returns no record");store.Put(12,"deaths",1024,"replacement");Check(store.Get(12,"deaths",1024)=="replacement","Archive index follows latest committed version");
            int count=0;foreach(var json in store.All(12,"deaths"))count++;Check(count==5,"Archive export enumerates each indexed ID once");
        }
        IEnumerator Scenario(bool pooled,int roadCount,int fleet,int frames,int trip,int retarget,bool invalidGoal=false,int maximumSearches=256){
            var shader=Instantiate(Resources.Load<ComputeShader>(pooled?"MassiveArmy/SettlementRoad":"MassiveArmy/Validation/MemoryBaselineRoad"));if(shader==null)throw new Exception("Missing navigation shader");
            var owned=new List<GraphicsBuffer>();GraphicsBuffer B(int count,int stride){var b=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,stride);owned.Add(b);return b;}
            int budget=192;var arguments=Environment.GetCommandLineArgs();int budgetAt=Array.IndexOf(arguments,"-cargo-memory-mib");if(budgetAt>=0&&int.TryParse(arguments[budgetAt+1],out int requestedBudget))budget=Math.Max(1,requestedBudget);
            int stride=Mathf.NextPowerOfTwo(roadCount),capacity=Mathf.NextPowerOfTwo(fleet),pool=pooled?SettlementCargoNavigation.SearchSlotCount(stride,capacity,maximumSearches,budget):capacity;
            var nodes=B(stride,32);var jobs=B(capacity,64);var work=B(capacity,pooled?64:48);var seen=B(stride*pool,pooled?16:4);var parents=pooled?null:B(stride*pool,4);var queue=pooled?null:B(stride*pool,4);var distances=pooled?null:B(stride*pool,4);var summary=B(capacity,16);var poses=B(capacity,80);
            GraphicsBuffer routes=null,owners=null,epochs=null;if(pooled){routes=B(capacity*SettlementCargoNavigation.RouteStride,4);owners=B(pool,4);epochs=B(pool,4);}
            try{
                var graph=new Node[stride];for(int i=0;i<roadCount;i++)graph[i]=new Node{position=new Vector4(i*10,120000,0,0),west=i>0?i-1:-1,east=i+1<roadCount?i+1:-1,south=-1,north=-1};nodes.SetData(graph);
                int navigate=shader.FindKernel("Navigate"),clear=shader.FindKernel("ClearSearch");shader.SetBuffer(navigate,"_RoadNodes",nodes);shader.SetBuffer(navigate,"_RailNodes",nodes);shader.SetBuffer(navigate,"_RoadJobs",jobs);if(!pooled){shader.SetBuffer(navigate,"_RoadParents",parents);shader.SetBuffer(navigate,"_RoadQueue",queue);shader.SetBuffer(navigate,"_RoadDistances",distances);}shader.SetBuffer(navigate,"_RoadSummary",summary);shader.SetBuffer(navigate,"_CargoPoses",poses);
                foreach(int k in new[]{navigate,clear}){shader.SetBuffer(k,pooled?"_RoadSearch":"_RoadSeen",seen);shader.SetBuffer(k,"_RoadWork",work);if(pooled){shader.SetBuffer(k,"_RoadRoutes",routes);shader.SetBuffer(k,"_SearchOwners",owners);shader.SetBuffer(k,"_SearchEpochs",epochs);}}
                shader.SetInt("_RoadCount",roadCount);shader.SetInt("_RailCount",roadCount);shader.SetInt("_RoadStride",stride);shader.SetInt("_SearchSlots",pool);shader.SetInt("_RouteStride",SettlementCargoNavigation.RouteStride);shader.SetInt("_CargoCount",fleet);shader.SetInt("_RoadRevision",1);shader.SetInt("_RaiderCampCount",0);shader.SetFloat("_RoadLaneOffset",2);shader.SetFloat("_CargoDelta",1f/30);
                SettlementCargoNavigation.DispatchStorage(shader,clear,Math.Max(stride*pool,capacity));
                var input=new Job[capacity];void Upload(int generation){for(int i=0;i<fleet;i++){int start=i%Math.Max(1,roadCount-trip-1);if(retarget>0)start=(i*251+generation*17)%(roadCount-trip-1);input[i]=new Job{start=graph[start].position,goal=graph[start+trip].position,startNode=(uint)start,goalNode=invalidGoal?uint.MaxValue:(uint)(start+trip),version=(uint)generation,parameters=new Vector4(1200,0,120000,11)};}jobs.SetData(input);}
                Upload(1);for(int i=0;i<300;i++){if(pooled)shader.SetInt("_CargoDispatch",i+1);shader.Dispatch(navigate,(fleet+31)/32,1,1);yield return null;}SettlementCargoNavigation.DispatchStorage(shader,clear,Math.Max(stride*pool,capacity));Upload(2);
                var samples=new List<double>(frames);var clock=Stopwatch.StartNew();double previous=clock.Elapsed.TotalMilliseconds;
                for(int frame=0;frame<frames;frame++){
                    if(retarget>0&&frame>0&&frame%retarget==0)Upload(2+frame/retarget);
                    if(pooled&&frame==45&&retarget==0){
                        var oldWork=work;var oldPoses=poses;var oldRoutes=routes;work=B(capacity,64);poses=B(capacity,80);routes=B(capacity*SettlementCargoNavigation.RouteStride,4);
                        int copy=shader.FindKernel("CopySearch");foreach(int k in new[]{navigate,clear,copy}){shader.SetBuffer(k,"_RoadWork",work);shader.SetBuffer(k,"_CargoPoses",poses);shader.SetBuffer(k,"_RoadRoutes",routes);}
                        SettlementCargoNavigation.DispatchStorage(shader,clear,Math.Max(stride*pool,capacity));shader.SetInt("_PreviousSlots",capacity);shader.SetBuffer(copy,"_PreviousWork",oldWork);shader.SetBuffer(copy,"_PreviousPoses",oldPoses);shader.SetBuffer(copy,"_PreviousRoutes",oldRoutes);SettlementCargoNavigation.DispatchStorage(shader,copy,capacity*SettlementCargoNavigation.RouteStride);
                        oldWork.Dispose();oldPoses.Dispose();oldRoutes.Dispose();owned.Remove(oldWork);owned.Remove(oldPoses);owned.Remove(oldRoutes);
                    }
                    if(frame==90)shader.SetInt("_RoadRevision",2);
                    if(pooled)shader.SetInt("_CargoDispatch",frame+301);shader.Dispatch(navigate,(fleet+31)/32,1,1);var frameFence=AsyncGPUReadback.Request(summary);frameFence.WaitForCompletion();yield return null;double now=clock.Elapsed.TotalMilliseconds;samples.Add(now-previous);previous=now;
                }
                var readback=AsyncGPUReadback.Request(summary);while(!readback.done)yield return null;Check(!readback.hasError,"GPU navigation result readback succeeds");var values=readback.GetData<Summary>();int completed=0;for(int i=0;i<fleet;i++)if(values[i].arrived!=0&&values[i].version==input[i].version)completed++;
                if(completed!=(invalidGoal?0:fleet)){
                    var raw=new uint[capacity*(pooled?16:12)];work.GetData(raw);var debug=new System.Text.StringBuilder();
                    for(int i=0;i<Math.Min(8,fleet);i++){debug.Append("slot "+i+" summary "+values[i].version+","+values[i].arrived+","+values[i].distance+","+values[i].status+" work ");for(int j=0;j<(pooled?16:12);j++)debug.Append(raw[i*(pooled?16:12)+j]+",");debug.AppendLine();}
                    File.WriteAllText(Path.Combine(output,"gpu-debug.txt"),debug.ToString());
                }
                if(invalidGoal)Check(values[0].status==3,"Missing road endpoint fails safely after buffer reconstruction");
                Check(completed==(invalidGoal?0:fleet),(pooled?"Pooled":"Baseline")+" fleet finishes every current mission; trip "+trip);
                samples.Sort();using var process=Process.GetCurrentProcess();process.Refresh();GetProcessMemoryInfo(GetCurrentProcess(),out var memory,(uint)Marshal.SizeOf<MemoryCounters>());long bytes=0;foreach(var b in owned)bytes+=(long)b.count*b.stride;
                report.results.Add(new Result{implementation=pooled?"bounded":"baseline",roads=roadCount,vehicles=fleet,frames=frames,completed=completed,searchSlots=pool,medianMs=samples[samples.Count/2],p95Ms=samples[(int)Math.Ceiling(samples.Count*.95)-1],ticksPerSecond=frames/clock.Elapsed.TotalSeconds,seconds=clock.Elapsed.TotalSeconds,searchBytes=(long)stride*pool*16+(pooled?pool*8:0),bufferBytes=bytes,unityAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),unityReservedBytes=Profiler.GetTotalReservedMemoryLong(),graphicsDriverBytes=Profiler.GetAllocatedMemoryForGraphicsDriver(),workingSetBytes=(long)memory.workingSet.ToUInt64(),privateBytes=(long)memory.privateBytes.ToUInt64()});
            }finally{foreach(var b in owned)b.Dispose();Destroy(shader);}
        }
    }
}

