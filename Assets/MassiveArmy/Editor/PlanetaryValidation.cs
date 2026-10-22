using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassiveArmy.EditorTools
{
    public static class PlanetaryValidation
    {
        [InitializeOnLoadMethod]static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump()
        {
            const string path="TestResults/Planetary/request.txt";
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(path))return;
            string action;try{action=File.ReadAllText(path).Trim();File.Delete(path);}catch(IOException){return;}
            try{switch(action){case "flight-weather":PlanetFlightWeatherValidation.Run();break;case "crime-ui":PlanetCrimeUiValidation.Run();break;case "zoning":PlanetZoningValidation.Run();break;case "justice-art":PlanetJusticeArt.Build();break;case "ground-collision":PlanetGroundCollisionValidation.Run();break;case "civic-art":CivicSecurityArt.Build();break;case "modular-validate":ModularBuildingValidation.Run();break;case "modular-profiles":ModularBuildingWindow.CreateProfiles();break;case "modular-bake":ModularBuildingWindow.BakeAll(true);break;case "modular-window":ModularBuildingWindow.Open();break;case "street-validate":PlanetStreetValidation.Run();break;case "model-pack-validate":RtsModelPackValidation.Run();break;case "model-pack-window":RtsModelPackWindow.Open();break;case "ship-editor-validate":PlanetShipEditorValidation.Run();break;case "memory-release":PlanetEditorMemory.Release();break;case "memory-restore":PlanetEditorMemory.Restore();break;case "orbital-art":PlanetOrbitalArtBuilder.Build();break;case "economy-snapshot":PlanetEconomyDiagnostics.Capture();break;case "scorch-validate":PlanetScorchValidation.Run();break;case "cull-validate":PlanetCullValidation.Run();break;case "terrain-flight-validate":PlanetTerrainFlightValidation.Run();break;case "precision-validate":PlanetPrecisionValidation.Run();break;case "orbit-validate":PlanetOrbitValidation.Run();break;case "regional-validate":PlanetRegionalValidation.Run();break;case "validate":Run();break;case "command-validate":PlanetCommandValidation.Run();break;case "terrain-validate":PlanetCbtValidation.Run();break;case "streaming-validate":PlanetStreamingValidation.Run();break;case "scene":CreateScene();break;case "select-definition":SelectDefinition();break;case "build":Build();break;case "stamp-control":BuildTo("TestResults/Planetary/Stamps/LegacyBuild/PlanetDevelopment.exe","TestResults/Planetary/Stamps/build-control.txt");break;case "hq-flat-control":BuildTo("TestResults/Planetary/HqTerrain/LegacyFlatBuild/PlanetDevelopment.exe","TestResults/Planetary/HqTerrain/build-control.txt");break;case "flat-pair":BuildFlatPair();break;}if(File.Exists("TestResults/Planetary/request-error.txt"))File.Delete("TestResults/Planetary/request-error.txt");}
            catch(Exception e){Debug.LogException(e);Directory.CreateDirectory("TestResults/Planetary");File.WriteAllText("TestResults/Planetary/request-error.txt",e.ToString());}
        }
        [Serializable]sealed class Report {public string status,error,unity;public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Planetary/Create Development Scene")]
        public static void CreateScene()
        {
            if(Application.isBatchMode&&string.IsNullOrEmpty(SceneManager.GetActiveScene().path))EditorSceneManager.OpenScene("Assets/Scenes/PlanetDevelopment.unity",OpenSceneMode.Single);
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var root=new GameObject("Planetary development");SceneManager.MoveGameObjectToScene(root,scene);var world=root.AddComponent<PlanetDevelopment>();world.definition=Resources.Load<PlanetDefinition>("MassiveArmy/Planets/DefaultPlanet");world.systemDefinition=Resources.Load<PlanetSystemDefinition>("MassiveArmy/Planets/DefaultSystem");
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/PlanetDevelopment.unity");EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);AssetDatabase.SaveAssets();
        }
        [MenuItem("Massive Army/Planetary/Select Default Planet")]
        public static void SelectDefinition(){var preset=Resources.Load<PlanetDefinition>("MassiveArmy/Planets/DefaultPlanet");Selection.activeObject=preset;EditorGUIUtility.PingObject(preset);}
        public static void Build()=>BuildTo("TestResults/Planetary/Build/PlanetDevelopment.exe","TestResults/Planetary/build.txt");
        static void BuildTo(string playerPath,string reportPath)
        {
            CreateScene();Directory.CreateDirectory(Path.GetDirectoryName(playerPath));
            const BuildTarget target=BuildTarget.StandaloneWindows64;bool defaults=PlayerSettings.GetUseDefaultGraphicsAPIs(target);var apis=PlayerSettings.GetGraphicsAPIs(target);
            try{PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);PlayerSettings.SetGraphicsAPIs(target,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/PlanetDevelopment.unity","Assets/Scenes/MassiveBattle.unity"},locationPathName=playerPath,target=target,options=BuildOptions.None});
                File.WriteAllText(reportPath,report.summary.result+"; "+report.summary.totalErrors+" errors");
                if(report.summary.totalErrors>0||report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException("Planet development build failed");
            }finally{PlayerSettings.SetGraphicsAPIs(target,apis);PlayerSettings.SetUseDefaultGraphicsAPIs(target,defaults);}
        }
        public static void BuildFlatPair()
        {
            const string path="Assets/MassiveArmy/Resources/MassiveArmy/Navigation.compute";
            string current=File.ReadAllText(path);
            const BuildTarget target=BuildTarget.StandaloneWindows64;bool defaults=PlayerSettings.GetUseDefaultGraphicsAPIs(target);var apis=PlayerSettings.GetGraphicsAPIs(target);
            try{
                PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);PlayerSettings.SetGraphicsAPIs(target,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
                foreach(string label in new[]{"baseline","current"}){
                    File.WriteAllText(path,label=="baseline"?File.ReadAllText("TestResults/Planetary/BaselineNavigation.compute"):current);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
                    string destination="TestResults/Planetary/FlatBuilds/"+label+"/SyntheticFront.exe";Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/MassiveBattle.unity"},locationPathName=destination,target=target,options=BuildOptions.None});
                    File.WriteAllText("TestResults/Planetary/build-flat-"+label+".txt",report.summary.result+"; "+report.summary.totalErrors+" errors");
                    if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException("Flat "+label+" build failed");
                }
            }finally{File.WriteAllText(path,current);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);PlayerSettings.SetGraphicsAPIs(target,apis);PlayerSettings.SetUseDefaultGraphicsAPIs(target,defaults);}
        }
        [MenuItem("Massive Army/Planetary/Validate Coordinates and GPU Navigation")]
        public static void Run()
        {
            var report=new Report{status="PASS",error="",unity=Application.unityVersion};
            void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);report.checks.Add(message);}
            try{
                var origin=new UniversePosition(long.MaxValue-100,0,0,new Double3(.125,0,0));var next=origin.Add(new Double3(.001,0,0));
                Check(Math.Abs(next.RelativeTo(origin).x-.001)<1e-12,"Millimetre precision near int64 sector limit");
                var carry=new UniversePosition(2,-3,7,new Double3(UniversePosition.SectorSize*.5,-UniversePosition.SectorSize*.5-.25,0));
                Check(carry.sectorX==3&&carry.sectorY==-4&&carry.local.x==-UniversePosition.SectorSize*.5&&carry.local.y==UniversePosition.SectorSize*.5-.25,"Sector normalization at positive and negative boundaries");
                var frames=new ReferenceFrameManager{Origin=origin};frames.Register(new ReferenceFrame(1,0,FrameKind.Ship,new SpacePose{rotation=Quaternion.Euler(0,90,0),position=new Double3(0,0,100),linearVelocity=new Double3(0,0,1000),angularVelocity=new Vector3(0,1,0)}));
                var pose=new SpacePose{frameId=1,position=new Double3(10,0,0),rotation=Quaternion.identity,linearVelocity=new Double3(1,0,0)};
                var world=frames.Resolve(pose);var back=frames.ChangeFrame(frames.ChangeFrame(pose,0),1);
                Check((back.position-pose.position).Magnitude<.00001&&(back.linearVelocity-pose.linearVelocity).Magnitude<.00001,"Ship frame roundtrip preserves pose and velocity, including rotational velocity");
                var floating=new FloatingOrigin(origin){Threshold=100};Check(!floating.Update(origin.Add(new Double3(99,0,0)))&&floating.Update(origin.Add(new Double3(101,0,0))),"Floating origin rebases only past threshold");
                var scaledOrigin=new UniversePosition(123,456,0,new Double3());
                var scaled=ScaledSpace.Project(scaledOrigin.Add(new Double3(1e11,0,0)),scaledOrigin,1e6);Check(Math.Abs(scaled.radius/scaled.position.magnitude-1e-5)<1e-10,"Scaled rendering preserves distant body's angular size");
                foreach(int n in new[]{4,16,63}){
                    int seamSteps=0;
                    for(int f=0;f<6;f++)for(int y=0;y<n;y++)for(int x=0;x<n;x++){
                        var c=new PlanetNavCoordinate(1,(CubeFace)f,x,y);var d=CubeSphere.Direction(c,n);var round=CubeSphere.Cell(1,d,n);
                        CheckIndex(c.Index(n)==round.Index(n),"Cube projection roundtrip");
                        foreach(var step in new[]{Vector2Int.left,Vector2Int.right,Vector2Int.down,Vector2Int.up}){
                            var neighbour=CubeSphere.Neighbour(c,step.x,step.y,n);bool reciprocal=false;
                            foreach(var reverse in new[]{Vector2Int.left,Vector2Int.right,Vector2Int.down,Vector2Int.up})reciprocal|=CubeSphere.Neighbour(neighbour,reverse.x,reverse.y,n).Index(n)==c.Index(n);
                            CheckIndex(reciprocal,"Nonreciprocal cube edge");CheckIndex(Vector3.Angle(d,CubeSphere.Direction(neighbour,n))<180f/n,"Discontinuous cube edge");if(neighbour.face!=c.face)seamSteps++;
                        }
                    }
                    Check(seamSteps==24*n,"All 24 directed seams, corners, reciprocal edges and cell roundtrips at resolution "+n);
                }
                var environment=new PlayerEnvironmentState();var atmosphere=ScriptableObject.CreateInstance<PlanetDefinition>();
                environment.Step(atmosphere,0,10);Check(environment.Oxygen==100,"Breathable atmosphere preserves suit oxygen");
                atmosphere.hasAtmosphere=false;environment.Step(atmosphere,0,10);Check(environment.Oxygen==95,"Vacuum consumes suit oxygen");environment.Pressurized=true;environment.Step(atmosphere,1000,10);Check(environment.Oxygen==95,"Pressurized interior preserves oxygen in vacuum");UnityEngine.Object.DestroyImmediate(atmosphere);
                var inventory=new PlayerInventory{MaxDistinctItems=1};Check(inventory.Add("tool",2)&&!inventory.Add("ammo",1)&&inventory.Consume("tool",2)&&inventory.Add("ammo",1),"Inventory capacity, consumption and released slots");
                var control=new PlayerControlModeManager();int transitions=0;control.Bind(PlayerControlMode.RTS,_=>transitions++);control.Bind(PlayerControlMode.FirstPerson,_=>transitions++);Check(control.Switch(PlayerControlMode.FirstPerson,42)&&control.ControlledEntityId==42&&transitions==2&&!control.Switch(PlayerControlMode.Spacecraft),"Control mode retains entity identity and rejects unsupported modes");
                foreach(var d in new[]{new Vector3(1-2e-7f,0,1),new Vector3(-1+2e-7f,0,-1),new Vector3(1,1,1),Vector3.up,Vector3.down})Check((CubeSphere.Decode(CubeSphere.Encode(d))-d.normalized).magnitude<1e-5,"Half-open surface address retains direction at edge / corner "+d);
                ValidateGpu(Check);
                ValidateFirstPerson(Check);
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            Directory.CreateDirectory("TestResults/Planetary");File.WriteAllText("TestResults/Planetary/validation.json",JsonUtility.ToJson(report,true));Debug.Log("Planetary validation "+report.status);
            if(report.status!="PASS")throw new InvalidOperationException(report.error);
        }
        static void CheckIndex(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
        static void ValidateGpu(Action<bool,string> check)
        {
            const int n=16,cells=6*n*n;var definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.terrainHeight=0;definition.oceanHeight=-1;
            var shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetDevelopment"));
            try{
                PlanetDevelopment.BindSurface(shader,definition);shader.SetInt("_PlanetNavRes",n);shader.SetInt("_PlanetUnitCount",1);
                using(var costs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,cells*5,4))
                using(var topology=new GraphicsBuffer(GraphicsBuffer.Target.Structured,cells,16))
                using(var units=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,UnitState.Stride)){
                    int generate=shader.FindKernel("GenerateCosts");shader.SetBuffer(generate,"_PlanetCosts",costs);shader.Dispatch(generate,(cells+127)/128,1,1);
                    int seamProbe=shader.FindKernel("ValidateSeams");shader.SetBuffer(seamProbe,"_TopologyResults",topology);shader.Dispatch(seamProbe,(cells+127)/128,1,1);var seamData=new Vector4[cells];topology.GetData(seamData);
                    for(int i=0;i<cells;i++){var c=new PlanetNavCoordinate(1,(CubeFace)(i/(n*n)),i%n,(i%(n*n))/n);var steps=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.down,Vector2Int.up};for(int j=0;j<4;j++)if(CubeSphere.Neighbour(c,steps[j].x,steps[j].y,n).Index(n)!=(int)seamData[i][j])throw new InvalidOperationException("GPU seam mismatch at cell "+i+" step "+j+" expected "+CubeSphere.Neighbour(c,steps[j].x,steps[j].y,n).Index(n)+" actual "+seamData[i][j]);}
                    check(true,"CPU/GPU seam tables agree at every cell");
                    using(var nav=new PlanetPathfindingAdapter(n,1,definition.radius,costs)){
                        foreach(var goal in new[]{new Vector3(1.3f,.12f,1).normalized,Vector3.back,new Vector3(-1,-1,-1).normalized}){
                            nav.SetGoal(0,goal);nav.Relax(0,n*12);var field=new float[cells];nav.Global.GetData(field);
                            check(Array.TrueForAll(field,v=>v<1e19f),"GPU integration reaches every cube face for goal "+goal);
                            var target=CubeSphere.Cell(1,goal,n);check(field[target.Index(n)]==0,"GPU goal cell is zero");
                        }
                        var start=new Vector3(1,.12f,1.3f).normalized;var destination=new Vector3(1.3f,.12f,1).normalized;
                        nav.SetGoal(0,destination);nav.Relax(0,n*12);
                        shader.SetVector("_PlanetStart",new Vector4(CubeSphere.Encode(start).x,CubeSphere.Encode(start).y,0,0));shader.SetVector("_PlanetGoal",new Vector4(CubeSphere.Encode(destination).x,CubeSphere.Encode(destination).y,0,0));
                        int spawn=shader.FindKernel("Spawn"),move=shader.FindKernel("Move");shader.SetBuffer(spawn,"_PlanetUnits",units);shader.Dispatch(spawn,1,1,1);
                        shader.SetBuffer(move,"_PlanetUnits",units);shader.SetBuffer(move,"_PlanetCosts",costs);shader.SetBuffer(move,"_PlanetField",nav.Global);shader.SetFloat("_PlanetDt",BattleSettings.Tick);shader.SetFloat("_PlanetSpeed",16);
                        for(int i=0;i<1200;i++)shader.Dispatch(move,1,1,1);
                        var result=new UnitState[1];units.GetData(result);var direction=CubeSphere.Decode(result[0].position);
                        var finalField=new float[cells];nav.Global.GetData(finalField);var goalCell=CubeSphere.Cell(1,destination,n);var currentCell=CubeSphere.Cell(1,direction,n);
                        check(CubeSphere.ArcDistance(direction,destination,definition.radius)<2,"Existing GPU UnitState moves from +Z to +X and reaches goal, without CPU unit simulation; remaining metres="+CubeSphere.ArcDistance(direction,destination,definition.radius));
                        check(result[0].health==1000&&result[0].flags==1&&result[0].group==0,"GPU unit identity and state survive cube seam");
                        // Reuse one GPU fixture record. Every directed seam, including both corner ends.
                        int seamCases=0;
                        var steps=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.down,Vector2Int.up};
                        for(int face=0;face<6;face++)for(int edge=0;edge<4;edge++)foreach(int t in new[]{0,n/2,n-1}){
                            var source=new PlanetNavCoordinate(1,(CubeFace)face,edge<2?(edge==0?0:n-1):t,edge>=2?(edge==2?0:n-1):t);var step=steps[edge];var target=CubeSphere.Neighbour(source,step.x,step.y,n);
                            var from=CubeSphere.Direction(source,n);var to=CubeSphere.Direction(target,n);var fromUv=CubeSphere.Encode(from);var toUv=CubeSphere.Encode(to);
                            nav.SetGoal(0,to);nav.Relax(0,n*12);shader.SetBuffer(move,"_PlanetField",nav.Global);shader.SetVector("_PlanetStart",new Vector4(fromUv.x,fromUv.y,0,0));shader.SetVector("_PlanetGoal",new Vector4(toUv.x,toUv.y,0,0));shader.Dispatch(spawn,1,1,1);
                            for(int i=0;i<180;i++)shader.Dispatch(move,1,1,1);units.GetData(result);
                            if(CubeSphere.ArcDistance(CubeSphere.Decode(result[0].position),to,definition.radius)>2)throw new InvalidOperationException("GPU unit failed seam "+face+" edge "+edge+" corner parameter "+t);
                            seamCases++;
                        }
                        check(seamCases==72,"GPU unit crosses all 24 directed cube seams, at both corner ends and midpoint (72 movement cases)");
                        var tour=new[]{Vector3.forward,Vector3.right,Vector3.back,Vector3.left,Vector3.forward};var initial=CubeSphere.Encode(tour[0]);shader.SetVector("_PlanetStart",new Vector4(initial.x,initial.y,0,0));shader.Dispatch(spawn,1,1,1);
                        for(int leg=1;leg<tour.Length;leg++){
                            var uv=CubeSphere.Encode(tour[leg]);nav.SetGoal(0,tour[leg]);nav.Relax(0,n*12);shader.SetBuffer(move,"_PlanetField",nav.Global);shader.SetVector("_PlanetGoal",new Vector4(uv.x,uv.y,0,0));
                            for(int i=0;i<2400;i++)shader.Dispatch(move,1,1,1);units.GetData(result);if(CubeSphere.ArcDistance(CubeSphere.Decode(result[0].position),tour[leg],definition.radius)>2)throw new InvalidOperationException("GPU unit globe circuit failed at leg "+leg);
                        }
                        check(true,"GPU unit completes a full globe circuit through four faces with no respawn between commands");
                        var blocked=CubeSphere.Cell(1,Vector3.right,n);costs.SetData(new[]{1e20f},0,2*cells+blocked.Index(n),1);nav.SetGoal(0,Vector3.right);nav.Relax(0,n*12);var projected=new GroupOrder[1];nav.Groups.GetData(projected);var projectedCell=CubeSphere.Cell(1,CubeSphere.Decode(projected[0].destination),n);check(projectedCell.Index(n)!=blocked.Index(n),"Blocked planetary goals project to a traversable cell rather than freezing the field");
                        uint sequence=projected[0].sequence;nav.SetGoal(0,CubeSphere.Decode(projected[0].destination));nav.Groups.GetData(projected);check(projected[0].sequence==sequence,"Unchanged AI objectives do not reset converged navigation fields");nav.Invalidate();nav.Groups.GetData(projected);check(projected[0].sequence>sequence,"Terrain invalidation still rebuilds unchanged goals");

                    }
                    definition.terrainHeight=12;PlanetDevelopment.BindSurface(shader,definition);var surface=new PlanetSurface(definition);
                    int validate=shader.FindKernel("ValidateTopology");shader.SetBuffer(validate,"_TopologyResults",topology);shader.Dispatch(validate,(cells+127)/128,1,1);var samples=new Vector4[cells];topology.GetData(samples);float error=0;
                    for(int i=0;i<cells;i++){int face=i/(n*n),local=i%(n*n);var c=new PlanetNavCoordinate(1,(CubeFace)face,local%n,local/n);Vector3 expected=CubeSphere.Direction(c,n);var gpu=new Vector3(samples[i].x,samples[i].y,samples[i].z);checkIndex((expected-gpu).magnitude<1e-5f);error=Mathf.Max(error,Mathf.Abs(surface.SampleHeight(expected)-samples[i].w));}
                    check(error<.001f,"CPU/GPU directional terrain agrees within 1 mm; max error "+error);
                    int climate=shader.FindKernel("ValidateClimate");shader.SetBuffer(climate,"_TopologyResults",topology);shader.Dispatch(climate,(cells+127)/128,1,1);topology.GetData(samples);float climateError=0;
                    for(int i=0;i<cells;i++){var c=new PlanetNavCoordinate(1,(CubeFace)(i/(n*n)),i%n,(i%(n*n))/n);var d=CubeSphere.Direction(c,n);var expected=PlanetClimate.Sample(d,(definition.terrainSeed%1009)*.017f);climateError=Mathf.Max(climateError,Mathf.Abs(expected.x-samples[i].x),Mathf.Abs(expected.y-samples[i].y));}
                    check(climateError<.0001f,"CPU/GPU body-local 3D climate agrees on all six faces; max error "+climateError);
                    int seams=shader.FindKernel("ValidateSeams");shader.SetBuffer(seams,"_TopologyResults",topology);shader.Dispatch(seams,(cells+127)/128,1,1);topology.GetData(samples);
                    for(int i=0;i<cells;i++){var c=new PlanetNavCoordinate(1,(CubeFace)(i/(n*n)),i%n,(i%(n*n))/n);var steps=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.down,Vector2Int.up};for(int j=0;j<4;j++)checkIndex(CubeSphere.Neighbour(c,steps[j].x,steps[j].y,n).Index(n)==(int)samples[i][j]);}
                    check(true,"CPU/GPU neighbour transforms agree for all cells and all 24 directed seams including cube corners");
                    check(surface.Raycast(new Ray(new Vector3(0,0,800),Vector3.back),out var hit)&&Mathf.Abs(hit.magnitude-definition.radius-surface.SampleHeight(hit))<.002f,"Mathematical terrain picking matches surface height");
                    check(!surface.Raycast(new Ray(new Vector3(0,0,800),Vector3.forward),out _),"Planet picking rejects rays pointing away");
                }
            }finally{UnityEngine.Object.DestroyImmediate(shader);UnityEngine.Object.DestroyImmediate(definition);}
        }
        static void checkIndex(bool valid){if(!valid)throw new InvalidOperationException("CPU/GPU cube direction mismatch");}
        static void ValidateFirstPerson(Action<bool,string> check)
        {
            var definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.radius=30;definition.terrainHeight=0;definition.gravitationalParameter=8829;definition.position=new UniversePosition(123456789,0,0,new Double3());
            var root=new GameObject("Bounded spherical player fixture"){hideFlags=HideFlags.DontSave};var camera=new GameObject("Test camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
            var surface=new PlanetSurface(definition);var origin=new FloatingOrigin(definition.position){Threshold=64};var player=new PlanetFirstPersonController(surface,origin,camera,root.transform){CaptureCursor=false};
            try{
                foreach(var start in new[]{Vector3.up,Vector3.down,Vector3.right,Vector3.forward,new Vector3(1,1,1).normalized,new Vector3(-1,-1,-1).normalized}){
                    CubeSphere.Project(start,out var face,out _);var facing=Vector3.ProjectOnPlane(CubeSphere.Up[(int)face],start).normalized;player.Enter(start,facing);
                    bool north=false,south=false;var faces=new HashSet<CubeFace>();
                    for(int i=0;i<1100;i++){player.Step(BattleSettings.Tick,Vector2.up,Vector2.zero);var d=player.Direction;CubeSphere.Project(d,out var f,out _);faces.Add(f);north|=d.y>.99f;south|=d.y<-.99f;
                        if(float.IsNaN(d.x)||Vector3.Dot(camera.transform.up,d)<.999f)throw new InvalidOperationException("FPS orientation failed at pole/seam");
                        var relative=origin.Origin.Add(new Double3(camera.transform.position)).RelativeTo(definition.position);if(Math.Abs(relative.Magnitude-(30.05+player.EyeHeight))>.005)throw new InvalidOperationException("FPS terrain contact drifted");
                    }
                    check(faces.Count>=4,"Physical FPS avatar walks beyond a full circumference, crossing >=4 faces with stable gravity/orientation from "+start);
                    if(start==Vector3.up||start==Vector3.down)check(north&&south,"FPS walk traverses both poles without singularity");
                }
                player.SetActive(false);var rig=camera.gameObject.AddComponent<PlanetRtsCamera>();rig.InputEnabled=false;rig.Initialize(surface,origin);int rebases=0;origin.Rebased+=_=>rebases++;
                Vector3 previous=camera.transform.right;
                for(int i=0;i<=720;i++){rig.SetFocusDirection(Quaternion.AngleAxis(i*.5f,Vector3.forward)*Vector3.right);rig.Step();if(i>0&&Vector3.Dot(previous,camera.transform.right)<.99f)throw new InvalidOperationException("RTS camera flipped while crossing pole");previous=camera.transform.right;if(camera.transform.position.magnitude>origin.Threshold+.01)throw new InvalidOperationException("Camera escaped local origin region");}
                check(rebases>3,"RTS camera completes polar orbit without flips and triggers multiple bounded floating-origin rebases");
            }finally{player.Dispose();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(definition);}
        }
    }
}
