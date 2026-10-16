using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.EditorTools
{
    public static class GpuValidation
    {
        static IEnumerator routine;
        static readonly List<string> results=new List<string>();
        static readonly List<IDisposable> allocations=new List<IDisposable>();
        static GameObject root;
        static double deadline;
        static string failure;
        static readonly uint[] IndexCounts={3,3,6,6};
        [Serializable] sealed class Report { public string unity,gpu,api,timestamp,status,error;public string[] checks; }
        public static void Start()
        {
            if(routine!=null)throw new InvalidOperationException("Validation is already running");
            results.Clear();failure="";root=new GameObject("GPU validation world");
            routine=Run();deadline=EditorApplication.timeSinceStartup+420;
            EditorApplication.update+=Advance;
        }
        public static void Batch()
        {
            BattleProject.Prepare();Start();
            EditorApplication.update+=ExitWhenDone;
        }
        static void ExitWhenDone() { if(routine==null) { EditorApplication.update-=ExitWhenDone;EditorApplication.Exit(string.IsNullOrEmpty(failure)?0:1); } }
        static void Advance()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("GPU validation timed out");
                if(routine.MoveNext())return;
            }
            catch(Exception e) { failure=e.ToString();Debug.LogException(e); }
            Finish();
        }
        static void Finish()
        {
            EditorApplication.update-=Advance;routine=null;
            foreach(var allocation in allocations)allocation.Dispose();allocations.Clear();
            if(root!=null)UnityEngine.Object.DestroyImmediate(root);
            Directory.CreateDirectory("TestResults");
            var report=new Report{unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),timestamp=DateTime.UtcNow.ToString("O"),status=string.IsNullOrEmpty(failure)?"PASS":"FAIL",error=failure,checks=results.ToArray()};
            File.WriteAllText("TestResults/gpu-validation.json",JsonUtility.ToJson(report,true));
            Debug.Log("Massive Army GPU validation: "+report.status+" ("+results.Count+" checks)");
        }
        static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message);results.Add(message);File.WriteAllLines("TestResults/validation-progress.txt",results); }
        static IEnumerator Run()
        {
            Require(Marshal.SizeOf<UnitState>()==64,"C#/HLSL unit layout is 64 bytes");
            Require(Marshal.SizeOf<UnitArchetype>()==64 && Marshal.SizeOf<ProjectileState>()==48 && Marshal.SizeOf<GroupOrder>()==64,"Archetype, projectile and command layouts match");
            int[] lengths={1,511,512,513,1023,16385,65536,262144,262145,1048576};
            foreach(int n in lengths)
            {
                var input=new uint[n];var random=new System.Random(n);
                for(int i=0;i<n;i++)input[i]=(uint)(i%7==0?0:random.Next(0,300));
                using(var counts=new GraphicsBuffer(GraphicsBuffer.Target.Structured,n,4))
                using(var output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,n,4))
                using(var scan=new GpuPrefixScan(n))
                using(var cmd=new CommandBuffer())
                {
                    counts.SetData(input);scan.Record(cmd,counts,output);Graphics.ExecuteCommandBuffer(cmd);
                    bool complete=false;uint[] actual=null;
                    AsyncGPUReadback.Request(output,r=>{if(!r.hasError)actual=r.GetData<uint>().ToArray();complete=true;});
                    while(!complete)yield return null;
                    Require(actual!=null,"Prefix readback "+n);
                    uint total=0;
                    for(int i=0;i<n;i++) { if(actual[i]!=total)throw new InvalidOperationException($"Scan {n} at {i}: {actual[i]} != {total}");total+=input[i]; }
                    results.Add("Exclusive GPU prefix scan exact for "+n+" unsorted counts");
                }
            }
            var settings=new BattleSettings{activeUnits=50000,capacity=100000,enemyAI=false,combat=false,targeting=false,projectileCapacity=1024};
            var field=new Battlefield(settings,root.transform);allocations.Add(field);
            var battle=new GpuBattle(settings,field);allocations.Insert(0,battle);
            var camera=new GameObject("Validation camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
            camera.transform.position=new Vector3(0,1800,0);camera.transform.rotation=Quaternion.Euler(90,0,0);camera.orthographic=true;camera.orthographicSize=field.Size*.49f;camera.aspect=1;camera.farClipPlane=5000;camera.enabled=false;
            bool done=false;uint[] stats=null;
            battle.ValidateSpatialGrid(r=>{stats=r;done=true;});while(!done)yield return null;
            Require(stats!=null && stats[100]==0 && stats[101]==50000,"50K unsorted units: cell offsets, scatter membership and uniqueness validated on GPU");
            battle.Update(camera,0,IndexCounts,true);
            done=false;uint[] drawArgs=null;battle.ReadDrawArguments(r=>{drawArgs=r;done=true;});while(!done)yield return null;
            Require(drawArgs!=null && drawArgs[1]+drawArgs[6]+drawArgs[11]==50000 && drawArgs[11]==50000 && drawArgs[10]==6,"GPU frustum/LOD classification generates exact 50K far-tier indirect arguments");
            foreach(var operation in new[]{SelectionOperation.Replace,SelectionOperation.Remove,SelectionOperation.Add})
            {
                battle.Select(camera,new Rect(0,0,1,1),operation,false);
                done=false;battle.RequestStatistics(r=>{stats=r;done=true;});while(!done)yield return null;
                uint expected=operation==SelectionOperation.Remove?0u:25000u;
                Require(stats!=null && stats[2]==expected,"GPU "+operation+" selection accepts only 25K cyan units");
            }
            UnitState[] before=null,after=null;
            done=false;battle.ReadUnitSample(0,128,r=>{before=r;done=true;});while(!done)yield return null;
            Vector2 picked=before[0].position;
            Vector3 screen=camera.WorldToViewportPoint(new Vector3(picked.x,field.Height(picked)+1,picked.y));
            battle.Select(camera,new Rect(screen.x,screen.y,0,0),SelectionOperation.Replace,true);
            done=false;battle.RequestStatistics(r=>{stats=r;done=true;});while(!done)yield return null;
            Require(stats!=null && stats[2]==1,"GPU click picking resolves exactly one cyan unit without a CPU unit list");
            // Isolate command and grid validation from the natural map's available formation capacity.
            var commandCosts=new float[field.Resolution*field.Resolution*5];for(int i=0;i<commandCosts.Length;i++)commandCosts[i]=1;field.Costs.SetData(commandCosts);
            battle.Select(camera,new Rect(0,0,1,1),SelectionOperation.Replace,false);
            Require(battle.IssueOrder(camera,new Vector2(420,0),ArmyOrder.Move,Vector2.zero),"Compact selected-only movement command accepted");
            for(int i=0;i<30;i++) { battle.Update(camera,BattleSettings.Tick,IndexCounts,false);if(i%5==0)yield return null; }
            done=false;battle.ReadUnitSample(0,128,r=>{after=r;done=true;});while(!done)yield return null;
            int moving=0;
            for(int i=0;i<128;i++)if(Vector2.Distance(before[i].position,after[i].position)>.1f)moving++;
            Require(moving>0,"GPU ping-pong movement changes sampled selected units");
            battle.IssueOrder(camera,Vector2.zero,ArmyOrder.Stop,Vector2.zero);
            done=false;battle.ReadUnitSample(0,128,r=>{after=r;done=true;});while(!done)yield return null;
            bool stopped=true;foreach(var u in after)stopped&=(u.flags&4)!=0 && u.velocity.sqrMagnitude<.0001f;
            Require(stopped,"GPU Stop clears velocity on selected units");
            done=false;battle.ValidateSpatialGrid(r=>{stats=r;done=true;});while(!done)yield return null;
            Require(stats!=null && stats[100]==0 && stats[101]==50000,"Spatial grid remains correct after movement and command buffer swaps (errors "+(stats==null?"null":stats[100].ToString())+", members "+(stats==null?"null":stats[101].ToString())+", details "+(stats==null?"null":string.Join(",",stats[107],stats[108],stats[109],stats[110]))+")");
            battle.Dispose();allocations.Remove(battle);
            foreach(bool shells in new[]{false,true})
            {
                var combatSettings=new BattleSettings{activeUnits=1024,capacity=1024,movement=false,enemyAI=false,projectiles=shells,projectileCapacity=1024,targetSlices=1};
                var tank=UnitArchetype.Tank;tank.damage=1000;tank.cooldown=.08f;tank.attackRange=190;tank.acquisitionRange=210;
                var combat=new GpuBattle(combatSettings,field,new[]{tank});allocations.Insert(0,combat);
                for(int i=0;i<120;i++) { combat.Update(camera,BattleSettings.Tick,IndexCounts,false);if(i%8==0)yield return null; }
                done=false;combat.RequestStatistics(r=>{stats=r;done=true;});while(!done)yield return null;
                Require(stats!=null && stats[0]+stats[1]<1024,(shells?"GPU gameplay shells":"Atomic direct damage")+" resolve health, deaths and live statistics");
                uint alive=stats[0]+stats[1];done=false;combat.ValidateSpatialGrid(r=>{stats=r;done=true;});while(!done)yield return null;
                Require(stats!=null && stats[100]==0 && stats[101]==alive,"Dead units excluded from GPU spatial grid (shells="+shells+")");
                combat.Dispose();allocations.Remove(combat);
            }
            var lifecycleChecks=FormationLifecycleChecks(field,camera);
            while(lifecycleChecks.MoveNext())yield return lifecycleChecks.Current;
            var strategicChecks=StrategicOrderChecks(field,camera);
            while(strategicChecks.MoveNext())yield return strategicChecks.Current;
            var trafficChecks=TrafficChecks(field,camera);
            while(trafficChecks.MoveNext())yield return trafficChecks.Current;
            var bottleneckChecks=BottleneckChecks(field,camera);
            while(bottleneckChecks.MoveNext())yield return bottleneckChecks.Current;
            var movementChecks=MovementChecks(field,camera);
            while(movementChecks.MoveNext())yield return movementChecks.Current;
        }
        static UnitState Fixture(Vector2 position,Vector2 destination,uint team=0,uint flags=1) => new UnitState {
            position=position,previous=position,destination=destination,packed=team,flags=flags,health=1000,target=uint.MaxValue,group=uint.MaxValue
        };
        static IEnumerator MovementChecks(Battlefield field,Camera camera)
        {
            var flat=new float[field.Resolution*field.Resolution*5];for(int i=0;i<flat.Length;i++)flat[i]=1;field.Costs.SetData(flat);
            var config=new BattleSettings{activeUnits=2,capacity=2,enemyAI=false,combat=false,targeting=false,simulationLod=false,projectileCapacity=256};
            var pair=new GpuBattle(config,field);allocations.Insert(0,pair);
            foreach(bool enemy in new[]{true,false})
            {
                pair.SetValidationUnits(new[]{Fixture(new Vector2(-18,0),new Vector2(18,0)),Fixture(new Vector2(18,0),new Vector2(-18,0),enemy?1u:0u)});
                float minDistance=100;UnitState[] sample=null;
                for(int tick=0;tick<240;tick++)
                {
                    pair.Update(camera,BattleSettings.Tick,IndexCounts,false);
                    bool done=false;pair.ReadUnitSample(0,2,r=>{sample=r;done=true;});while(!done)yield return null;
                    if(sample==null)throw new InvalidOperationException("Contact sample failed");
                    minDistance=Mathf.Min(minDistance,Vector2.Distance(sample[0].position,sample[1].position));
                }
                Require(minDistance>=4.17f,(enemy?"Opposing":"Friendly")+" head-on traffic preserves visible hull clearance (minimum "+minDistance.ToString("F3")+")");
                Require(sample[0].position.x>12 && sample[1].position.x< -12,(enemy?"Opposing":"Friendly")+" head-on traffic passes and continues toward goals");
            }
            foreach(bool enemy in new[]{false,true})
            {
                pair.SetValidationUnits(new[]{Fixture(new Vector2(enemy?-6:-4,0),new Vector2(2,0),0,17),Fixture(new Vector2(0,0),Vector2.zero,enemy?1u:0u,5)});
                float minDistance=100;UnitState[] sample=null;
                for(int tick=0;tick<80;tick++)
                {
                    pair.Update(camera,BattleSettings.Tick,IndexCounts,false);
                    bool done=false;pair.ReadUnitSample(0,2,r=>{sample=r;done=true;});while(!done)yield return null;
                    minDistance=Mathf.Min(minDistance,Vector2.Distance(sample[0].position,sample[1].position));
                }
                Require(enemy?minDistance>=4.17f:minDistance<.6f,enemy?"Parking relaxation retains enemy contact avoidance":"Tanks may pass through friendlies within seven units of their slot");
            }
            // A wall forces a long detour through a single offset opening.
            for(int y=0;y<field.Resolution;y++)if(y<160 || y>168)
                for(int x=126;x<=129;x++)flat[2*field.Resolution*field.Resolution+x+y*field.Resolution]=1e20f;
            field.Costs.SetData(flat);
            pair.SetValidationUnits(new[]{Fixture(new Vector2(-600,-200),new Vector2(-600,-200),0,3),new UnitState()});
            bool ready=false;pair.RequestStatistics(r=>ready=true);while(!ready)yield return null;
            var single=new FormationLayout(new Vector2(600,-200),Vector2.right,0);
            Require(pair.IssueOrder(camera,single.Front,ArmyOrder.Move,Vector2.zero,single),"Distant obstacle-route order accepted");
            bool crossed=false;UnitState[] route=null;float highest=-200;
            for(int tick=0;tick<6000;tick++)
            {
                pair.Update(camera,BattleSettings.Tick,IndexCounts,false);
                if(tick%8!=0)continue;
                bool done=false;pair.ReadUnitSample(0,1,r=>{route=r;done=true;});while(!done)yield return null;
                Vector2 p=route[0].position;highest=Mathf.Max(highest,p.y);
                float cell=field.Size/field.Resolution;
                if(Mathf.Abs(p.x)<cell*2 && (p.y<(160-field.Resolution*.5f)*cell || p.y>=(169-field.Resolution*.5f)*cell))throw new InvalidOperationException("Long route entered the blocking wall: "+p);
                crossed|=p.x>32;
                if(Vector2.Distance(p,single.Front)<.5f)break;
            }
            Require(crossed && highest>=(160-field.Resolution*.5f)*field.Size/field.Resolution && Vector2.Distance(route[0].position,single.Front)<.5f,"Long-distance route follows offset opening and reaches its slot (final "+route[0].position+")");
            pair.Dispose();allocations.Remove(pair);
            for(int i=0;i<flat.Length;i++)flat[i]=1;field.Costs.SetData(flat);
            config.activeUnits=config.capacity=64;
            var formation=new GpuBattle(config,field);allocations.Insert(0,formation);
            var units=new UnitState[64];for(int i=0;i<64;i++)units[i]=Fixture(new Vector2(-80+(i%8)*5.2f,-80+(i/8)*5.2f),Vector2.zero,0,3);
            formation.SetValidationUnits(units);
            var layout=new FormationLayout(new Vector2(80,80),new Vector2(.8f,.6f),6*BattleSettings.FormationSpacing+.01f);
            formation.PreviewFormation(camera,layout);
            bool previewDone=false;Vector4[] ghosts=null;
            AsyncGPUReadback.Request(formation.PreviewSlots,64*16,0,r=>{if(!r.hasError)ghosts=r.GetData<Vector4>().ToArray();previewDone=true;});while(!previewDone)yield return null;
            Require(ghosts!=null,"GPU formation preview readback");
            for(int i=0;i<64;i++)if(Vector2.Distance(new Vector2(ghosts[i].x,ghosts[i].y),layout.Slot(i,64))>.001f)throw new InvalidOperationException("Ghost slot mismatch at "+i);
            Require(formation.IssueOrder(camera,layout.Front,ArmyOrder.Move,Vector2.zero,layout),"Rotated seven-column formation command accepted");
            bool assigned=false;UnitState[] actual=null;formation.ReadUnitSample(0,64,r=>{actual=r;assigned=true;});while(!assigned)yield return null;
            for(int i=0;i<64;i++)if(Vector2.Distance(actual[i].destination,layout.Slot(i,64))>.001f)throw new InvalidOperationException("Unique GPU formation slot mismatch at "+i);
            results.Add("64 selected tanks receive distinct rotated slots matching the preview, including a partial last row");
            for(int tick=0;tick<1800;tick++) { formation.Update(camera,BattleSettings.Tick,IndexCounts,false);if(tick%12==0)yield return null; }
            assigned=false;formation.ReadUnitSample(0,64,r=>{actual=r;assigned=true;});while(!assigned)yield return null;
            int parked=0;foreach(var u in actual)if(Vector2.Distance(u.position,u.destination)<.31f)parked++;
            var diagnostics=new List<string>();for(int i=0;i<actual.Length;i++){var u=actual[i];diagnostics.Add($"{i},{u.position.x},{u.position.y},{u.destination.x},{u.destination.y},{u.velocity.x},{u.velocity.y},{u.flags},{u.group}");}File.WriteAllLines("TestResults/formation-parking.csv",diagnostics);
            Require(parked==64,"All 64 tanks reach and park in their assigned formation slots ("+parked+"/64)");
            formation.SetValidationUnits(units);
            var column=new FormationLayout(new Vector2(200,300),Vector2.right,0);
            Require(formation.IssueOrder(camera,column.Front,ArmyOrder.Move,Vector2.zero,column),"Single-column formation command accepted");
            for(int tick=0;tick<2400;tick++) { formation.Update(camera,BattleSettings.Tick,IndexCounts,false);if(tick%12==0)yield return null; }
            assigned=false;formation.ReadUnitSample(0,64,r=>{actual=r;assigned=true;});while(!assigned)yield return null;
            parked=0;for(int i=0;i<64;i++)if(Vector2.Distance(actual[i].destination,column.Slot(i,64))<.001f && Vector2.Distance(actual[i].position,actual[i].destination)<.31f)parked++;
            Require(parked==64,"All 64 tanks settle into a single column ("+parked+"/64)");
            formation.Dispose();allocations.Remove(formation);
            var concaveChecks=ConcaveChecks(field,camera,flat);
            while(concaveChecks.MoveNext())yield return concaveChecks.Current;
        }
        static IEnumerator ConcaveChecks(Battlefield field,Camera camera,float[] flat)
        {
            var config=new BattleSettings{activeUnits=16,capacity=16,enemyAI=false,combat=false,targeting=false,simulationLod=false,projectileCapacity=256};
            int r=field.Resolution,n=r*r;float cell=field.Size/r;
            foreach(int mirror in new[]{1,-1}) {
                for(int i=0;i<flat.Length;i++)flat[i]=1;
                // A riverbank cup opens away from the goal. A straight-at-goal force traps tanks inside it.
                for(int y=110;y<=145;y++)for(int x=110;x<=130;x++)if(x>=126||y<=113||y>=142) {
                    int xx=mirror==1?x:r-1-x;flat[2*n+xx+y*r]=1e20f;
                }
                field.Costs.SetData(flat);
                var battle=new GpuBattle(config,field);allocations.Insert(0,battle);
                var units=new UnitState[16];
                for(int i=0;i<16;i++) { units[i]=Fixture(new Vector2(mirror*(-70-(i%4)*5.2f),(i/4-1.5f)*5.2f),Vector2.zero,0,3);units[i].velocity=new Vector2(mirror*12,8); }
                battle.SetValidationUnits(units);bool done=false;battle.RequestStatistics(s=>done=true);while(!done)yield return null;
                var layout=new FormationLayout(new Vector2(mirror*240,0),new Vector2(0,mirror),3*BattleSettings.FormationSpacing+.01f);
                Require(battle.IssueOrder(camera,layout.Front,ArmyOrder.Move,Vector2.zero,layout),"Concave riverbank order accepted (mirror "+mirror+")");
                UnitState[] sample=null;int parked=0;float escaped=0;
                for(int tick=0;tick<4800;tick++) {
                    battle.Update(camera,BattleSettings.Tick,IndexCounts,false);if(tick%16!=0)continue;
                    done=false;battle.ReadUnitSample(0,16,s=>{sample=s;done=true;});while(!done)yield return null;
                    parked=0;
                    foreach(var u in sample) {
                        if(!SafeFixture(u.position,flat,r,field.Size))throw new InvalidOperationException("Concave detour entered river or lost hull clearance: "+u.position);
                        escaped=Mathf.Min(escaped,u.position.x*mirror);
                        if(Vector2.Distance(u.position,u.destination)<.31f)parked++;
                    }
                    if(parked==16)break;
                }
                var diagnostics=new List<string>();foreach(var u in sample)diagnostics.Add($"{u.position.x},{u.position.y},{u.destination.x},{u.destination.y},{u.flags}");File.WriteAllLines("TestResults/concave-"+mirror+".csv",diagnostics);
                Require(escaped<(110-r*.5f)*cell && parked==16,"All 16 tanks exit the concave cup away from their goal, detour with hull clearance and park (mirror "+mirror+", "+parked+"/16)");
                battle.Dispose();allocations.Remove(battle);
            }
        }
        static IEnumerator FormationLifecycleChecks(Battlefield field,Camera camera)
        {
            int r=field.Resolution,n=r*r;var costs=new float[n*5];for(int i=0;i<costs.Length;i++)costs[i]=1;field.Costs.SetData(costs);
            var config=new BattleSettings{activeUnits=64,capacity=64,enemyAI=false,combat=false,targeting=false,simulationLod=false,projectileCapacity=256};
            var battle=new GpuBattle(config,field);allocations.Insert(0,battle);
            var units=new UnitState[64];for(int i=0;i<64;i++)units[i]=Fixture(new Vector2(-200+(i%8)*5.2f,-100+(i/8)*5.2f),Vector2.zero,0,3);
            battle.SetValidationUnits(units);
            for(int i=0;i<48;i++) {
                var layout=FormationLayout.Compact(new Vector2(80+i,100),Vector2.right,64);
                battle.PreviewFormation(camera,layout);
                if(!battle.IssueOrder(camera,layout.Front,ArmyOrder.Move,Vector2.zero,layout) || battle.PreviewActive)throw new InvalidOperationException("Repeated order failed at "+i+": "+battle.Notice);
            }
            Require(true,"48 rapid formation previews and orders reuse the selected group without statistics refresh or stuck preview");
            UnitState[] sample=null;bool done=false;battle.ReadUnitSample(0,64,s=>{sample=s;done=true;});while(!done)yield return null;
            for(int i=0;i<64;i++) { units[i]=sample[i];units[i].group=(uint)(2+i/4);units[i].flags=1; }
            units[0].flags=3;battle.SetValidationUnits(units);
            var next=FormationLayout.Compact(new Vector2(240,100),Vector2.right,4);
            battle.PreviewFormation(camera,next);
            Require(!battle.IssueOrder(camera,next.Front,ArmyOrder.Move,Vector2.zero,next) && !battle.PreviewActive,"Exhausted route groups reject a partial selection and clear its preview");
            for(int i=0;i<4;i++)units[i].flags=3;battle.SetValidationUnits(units);battle.PreviewFormation(camera,next);
            uint[] args=null;done=false;AsyncGPUReadback.Request(battle.PreviewArguments,s=>{if(!s.hasError)args=s.GetData<uint>().ToArray();done=true;});while(!done)yield return null;
            Require(args!=null && args[1]==4,"Preview rebuilds selected ranks after a rejected placement and changed selection");
            Require(battle.IssueOrder(camera,next.Front,ArmyOrder.Move,Vector2.zero,next),"A full selected group retargets with all 16 player route fields occupied");
            // Block a stripe across the first lattice rows; valid slots must be unique and extend behind it.
            for(int y=r/2;y<=r/2+1;y++)for(int x=r/2-2;x<=r/2+2;x++)costs[2*n+x+y*r]=1e20f;field.Costs.SetData(costs);
            for(int i=0;i<64;i++)units[i].flags=3;battle.SetValidationUnits(units);
            var blocked=new FormationLayout(new Vector2(0,20),Vector2.right,7*BattleSettings.FormationSpacing+.01f);
            battle.PreviewFormation(camera,blocked);Vector4[] ghosts=null;done=false;AsyncGPUReadback.Request(battle.PreviewSlots,64*16,0,s=>{if(!s.hasError)ghosts=s.GetData<Vector4>().ToArray();done=true;});while(!done)yield return null;
            Require(battle.IssueOrder(camera,blocked.Front,ArmyOrder.Move,Vector2.zero,blocked),"Formation across a terrain stripe extends into clear rear rows");
            done=false;battle.ReadUnitSample(0,64,s=>{sample=s;done=true;});while(!done)yield return null;
            if(ghosts==null)throw new InvalidOperationException("Missing obstacle formation preview");
            float rear=20;
            for(int i=0;i<64;i++) {
                if(!SafeFixture(sample[i].destination,costs,r,field.Size) || Vector2.Distance(sample[i].destination,new Vector2(ghosts[i].x,ghosts[i].y))>.001f || ghosts[i].z!=0)throw new InvalidOperationException("Blocked or mismatched compacted slot "+i);
                rear=Mathf.Min(rear,sample[i].destination.y);
                for(int j=0;j<i;j++)if(Vector2.Distance(sample[i].destination,sample[j].destination)<5.19f)throw new InvalidOperationException("Merged obstacle slots "+i+", "+j);
            }
            Require(rear<blocked.Slot(63,64).y,"All 64 obstacle-adjacent slots remain distinct, hull-clear and match the extended preview");
            var prior=sample;
            for(int i=0;i<costs.Length;i++)costs[i]=1e20f;field.Costs.SetData(costs);
            battle.PreviewFormation(camera,blocked);
            Require(!battle.IssueOrder(camera,blocked.Front,ArmyOrder.Move,Vector2.zero,blocked) && !battle.PreviewActive,"Insufficient clear slots reject the order and clear preview state");
            done=false;battle.ReadUnitSample(0,64,s=>{sample=s;done=true;});while(!done)yield return null;
            for(int i=0;i<64;i++)if(sample[i].destination!=prior[i].destination || sample[i].group!=prior[i].group)throw new InvalidOperationException("Rejected placement changed a live order");
            Require(true,"Rejected terrain placement preserves every previous unit destination and route group");
            battle.Dispose();allocations.Remove(battle);
        }
        static IEnumerator StrategicOrderChecks(Battlefield field,Camera camera)
        {
            int r=field.Resolution,n=r*r;var costs=new float[n*5];for(int i=0;i<costs.Length;i++)costs[i]=1;field.Costs.SetData(costs);
            var config=new BattleSettings{activeUnits=64,capacity=64,enemyAI=false,combat=false,targeting=false,projectileCapacity=256};
            var battle=new GpuBattle(config,field);allocations.Insert(0,battle);var units=new UnitState[64];
            for(int i=0;i<64;i++) { uint team=i<32?0u:1u;units[i]=Fixture(new Vector2((i<32?-200:200)+(i%4)*5.2f,(i%32/4)*5.2f),Vector2.zero,team);units[i].group=team; }
            battle.SetValidationUnits(units);
            Require(battle.SetValidationStrategicOrder(camera,new Vector2(300,200)),"Red strategic order accepts a complete unique formation");
            bool done=false;UnitState[] sample=null;battle.ReadUnitSample(0,64,s=>{sample=s;done=true;});while(!done)yield return null;
            for(int i=32;i<64;i++) {
                if(!SafeFixture(sample[i].destination,costs,r,field.Size))throw new InvalidOperationException("Invalid red slot "+i);
                for(int j=32;j<i;j++)if(Vector2.Distance(sample[i].destination,sample[j].destination)<5.19f)throw new InvalidOperationException("Merged red slots");
            }
            Require(true,"All red strategic destinations are distinct and hull-clear");var prior=sample;
            for(int i=0;i<costs.Length;i++)costs[i]=1e20f;field.Costs.SetData(costs);
            Require(!battle.SetValidationStrategicOrder(camera,new Vector2(-300,-200)),"Red strategic objective rejects insufficient clear terrain");
            done=false;battle.ReadUnitSample(0,64,s=>{sample=s;done=true;});while(!done)yield return null;
            for(int i=0;i<64;i++)if(sample[i].destination!=prior[i].destination || sample[i].group!=prior[i].group)throw new InvalidOperationException("Rejected strategic order changed a live destination");
            Require(battle.Navigation.GetOrder(1).destination==new Vector2(300,200),"Rejected red objective preserves the previous command and route");
            battle.Dispose();allocations.Remove(battle);
        }
        static IEnumerator TrafficChecks(Battlefield field,Camera camera)
        {
            int r=field.Resolution,n=r*r;
            var costs=new float[n*5];for(int i=0;i<costs.Length;i++)costs[i]=1;
            for(int y=0;y<r;y++)if(y<r/2-3 || y>=r/2+3)
                for(int x=0;x<r;x++)costs[2*n+x+y*r]=1e20f;
            field.Costs.SetData(costs);
            var config=new BattleSettings{activeUnits=64,capacity=64,enemyAI=false,combat=false,targeting=false,simulationLod=false,projectileCapacity=256};
            var battle=new GpuBattle(config,field);allocations.Insert(0,battle);
            var units=new UnitState[64];
            for(int i=0;i<64;i++) {
                int local=i%32;float sign=i<32?1:-1;
                units[i]=Fixture(new Vector2(-sign*(120+(local/4)*5.2f),(local%4-1.5f)*5.2f),new Vector2(sign*(180+(local/4)*5.2f),(local%4-1.5f)*5.2f),i<32?0u:1u);
            }
            battle.SetValidationUnits(units);UnitState[] sample=null;float minimum=100;int crossed=0;
            for(int tick=0;tick<1800;tick++) {
                battle.Update(camera,BattleSettings.Tick,IndexCounts,false);if(tick%16!=0)continue;
                bool done=false;battle.ReadUnitSample(0,64,s=>{sample=s;done=true;});while(!done)yield return null;
                crossed=0;
                for(int i=0;i<64;i++) {
                    if((i<32?sample[i].position.x:-sample[i].position.x)>40)crossed++;
                    if(!SafeFixture(sample[i].position,costs,r,field.Size))throw new InvalidOperationException("Traffic left traversable corridor: "+sample[i].position);
                    for(int j=0;j<i;j++)minimum=Mathf.Min(minimum,Vector2.Distance(sample[i].position,sample[j].position));
                }
                if(crossed==64)break;
            }
            var lines=new List<string>();for(int i=0;i<64;i++)lines.Add($"{i},{sample[i].position.x},{sample[i].position.y},{sample[i].velocity.x},{sample[i].velocity.y}");File.WriteAllLines("TestResults/traffic.csv",lines);
            Require(crossed==64,"All 64 opposing tanks pass through a traversable corridor without traffic deadlock ("+crossed+"/64)");
            Require(minimum>=4.15f,"Dense opposing traffic maintains visible hull clearance (minimum "+minimum.ToString("F3")+")");
            battle.Dispose();allocations.Remove(battle);
        }
        static IEnumerator BottleneckChecks(Battlefield field,Camera camera)
        {
            int r=field.Resolution,n=r*r;var costs=new float[n*5];for(int i=0;i<costs.Length;i++)costs[i]=1;
            for(int y=0;y<r;y++)if(y<r/2+4 || y>=r/2+10)
                for(int x=r/2-2;x<r/2+2;x++)costs[2*n+x+y*r]=1e20f;
            field.Costs.SetData(costs);
            var config=new BattleSettings{activeUnits=64,capacity=64,enemyAI=false,combat=false,targeting=false,simulationLod=false,projectileCapacity=256};
            var battle=new GpuBattle(config,field);allocations.Insert(0,battle);
            var units=new UnitState[64];for(int i=0;i<64;i++) {
                int local=i%32;float sign=i<32?1:-1;
                units[i]=Fixture(new Vector2(-sign*(120+(local/4)*5.2f),(local%4-1.5f)*5.2f),new Vector2(sign*180,(local%4-1.5f)*5.2f-(local/4)*5.2f),i<32?0u:1u,i<32?3u:1u);
                if(i>=32)units[i].group=1;
            }
            battle.SetValidationUnits(units);
            var layout=new FormationLayout(new Vector2(180,0),Vector2.right,7*BattleSettings.FormationSpacing+.01f);
            Require(battle.IssueOrder(camera,layout.Front,ArmyOrder.Move,Vector2.zero,layout),"Dense traffic order through an offset obstacle opening accepted");
            UnitState[] sample=null;int crossed=0;float minimum=100;string minimumInfo="";
            for(int tick=0;tick<3600;tick++) {
                battle.Update(camera,BattleSettings.Tick,IndexCounts,false);if(tick%16!=0)continue;
                bool done=false;battle.ReadUnitSample(0,64,s=>{sample=s;done=true;});while(!done)yield return null;
                crossed=0;
                for(int i=0;i<64;i++) {
                    if((i<32?sample[i].position.x:-sample[i].position.x)>40)crossed++;
                    if(!SafeFixture(sample[i].position,costs,r,field.Size))throw new InvalidOperationException("Bottleneck traffic entered blocked terrain: "+sample[i].position);
                    for(int j=0;j<i;j++)if(!ParkingPair(sample[i],sample[j]) && !ParkingPair(sample[j],sample[i]))
                        { float d=Vector2.Distance(sample[i].position,sample[j].position);if(d<minimum) { minimum=d;minimumInfo=$"tick {tick}: {i} {sample[i].position} flags {sample[i].flags}, {j} {sample[j].position} flags {sample[j].flags}"; } }
                }
                if(crossed==64)break;
            }
            File.WriteAllText("TestResults/bottleneck-minimum.txt",minimumInfo);
            var lines=new List<string>();for(int i=0;i<64;i++)lines.Add($"{i},{sample[i].position.x},{sample[i].position.y},{sample[i].destination.x},{sample[i].destination.y},{sample[i].velocity.x},{sample[i].velocity.y},{sample[i].flags}");File.WriteAllLines("TestResults/bottleneck.csv",lines);
            Require(crossed==64,"All 64 opposing tanks detour through the same passable offset opening without traffic deadlock ("+crossed+"/64)");
            Require(minimum>=4.15f,"Obstacle traffic preserves hull clearance outside friendly parking (minimum "+minimum.ToString("F3")+")");
            battle.Dispose();allocations.Remove(battle);
        }
        static bool ParkingPair(UnitState a,UnitState b) => (a.packed&255)==(b.packed&255) &&
            (((a.flags&512)!=0 && (b.flags&64)!=0 && (b.flags&16)!=0 && Vector2.Distance(b.position,b.destination)<7) ||
            ((a.flags&16)!=0 && Vector2.Distance(a.position,a.destination)<7 &&
            ((a.flags&64)==0 || ((b.flags&16)!=0 && Vector2.Distance(b.position,b.destination)<7) || (b.flags&580)!=0)));
        static bool SafeFixture(Vector2 p,float[] costs,int r,float size)
        {
            for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++) {
                Vector2 q=p+new Vector2(x,y)*2.19f;int xx=Mathf.FloorToInt((q.x/size+.5f)*r),yy=Mathf.FloorToInt((q.y/size+.5f)*r);
                if(xx<0||yy<0||xx>=r||yy>=r||costs[2*r*r+xx+yy*r]>=1e19f)return false;
            }
            return true;
        }
    }
}

