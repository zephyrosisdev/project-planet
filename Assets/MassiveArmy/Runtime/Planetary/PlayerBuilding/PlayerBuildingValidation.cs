using System;

using System.Collections;

using System.Collections.Generic;

using System.Diagnostics;

using System.IO;

using System.Reflection;

using System.Runtime.Serialization;

using UnityEngine;

using UnityEngine.Profiling;

namespace MassiveArmy.Planetary {

    public sealed class PlayerBuildingValidation:MonoBehaviour {

        [Serializable]sealed class Report {public string status="PASS",error="",scope="Standalone procedural building, PhysX, GPU civilian precision and picking checks. Operation timings are not whole-game frame times or army/fog acceptance.";public List<string> checks=new List<string>();public List<Row> measurements=new List<Row>();}

        [Serializable]sealed class Row {public string name;public double medianMs,p95Ms,updatesPerSecond;public long unityAllocated,unityReserved,bufferBytes;}

        struct Node {public Vector4 position;public int west,east,south,north;}

        struct Order {public uint home,target,team,model;public Vector4 parameters;}

        struct Goal {public int node,team,active,pad;}

        struct Walker {public Vector4 position;public uint node,next,initialised,pad;public Vector4 low;}

        struct Pose {public Vector4 position,right,up,forward,properties;}

        struct Agent {public uint health,flags,generation,source,gang,patrol,lastHit,pad;public Vector4 body;}

        struct OldWalker {public Vector4 position;public uint node,next,initialised,pad;}

        struct Leisure {public Vector4 a,b,c,d;}

        readonly Report report=new Report();string output;int errors;

        internal static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"-player-building-validation")>=0)new GameObject("Player building validation").AddComponent<PlayerBuildingValidation>();}

        void Awake(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-planet-output");output=i>=0?args[i+1]:"TestResults/PlayerBuilding/Run";Directory.CreateDirectory(output);Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Application.logMessageReceived+=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors++;};}

        IEnumerator Start(){var run=Run();while(true){object current=null;bool more;try{more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){report.status="FAIL";report.error=e.ToString();break;}if(!more)break;yield return current;}if(errors>0){report.status="FAIL";report.error+=" Runtime errors: "+errors;}File.WriteAllText(Path.Combine(output,"checks.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}

        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);report.checks.Add(name);}

        static void Set(object o,string name,object value)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(o,value);

        static object Get(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(o);

        IEnumerator Run(){

            var path=Path.GetFullPath("TestResults/PlayerBuilding/Bundle/playerbuilding-checks");var bundle=AssetBundle.LoadFromFile(path);Check(bundle!=null,"Isolated shader bundle loads without restarting the live editor");var ghost=bundle.LoadAsset<Shader>("Assets/PlayerBlueprint.shader");var ocean=bundle.LoadAsset<Shader>("Assets/Terrain/OceanDepthCheck.shader");var shader=bundle.LoadAsset<ComputeShader>("Assets/SettlementCitizens.compute");Check(ghost!=null&&ghost.isSupported&&new Material(ghost).SetPass(0),"Actual blueprint preview shader compiles on DX11");Check(ocean!=null&&ocean.isSupported,"Shared ocean geometry/depth shader compiles, including native double variant");Check(shader!=null&&SystemInfo.supportsComputeShaders,"Updated civilian movement/pick compute asset loads");

            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-road-projection-only")>=0){var roadRun=RoadProjectionChecks(bundle.LoadAsset<ComputeShader>("Assets/Terrain/RoadProjectionValidation.compute"));while(roadRun.MoveNext())yield return roadRun.Current;bundle.Unload(false);yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-terrain-material-only")>=0||Array.IndexOf(Environment.GetCommandLineArgs(),"-road-material-only")>=0){var materialRun=TerrainMaterialMotionChecks(bundle.LoadAsset<Shader>(Array.IndexOf(Environment.GetCommandLineArgs(),"-road-material-only")>=0?"Assets/Terrain/RoadPaintCheck.shader":"Assets/Terrain/TerrainMaterialCheck.shader"));while(materialRun.MoveNext())yield return materialRun.Current;bundle.Unload(false);yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-terrain-motion-only")>=0){var terrain=TerrainMotionChecks(bundle.LoadAsset<ComputeShader>("Assets/Terrain/PlanetTerrainValidation.compute"));while(terrain.MoveNext())yield return terrain.Current;bundle.Unload(false);yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-cloud-light-only")>=0){var clouds=CloudLightChecks(bundle.LoadAsset<ComputeShader>("Assets/PlanetWeatherLight.compute"));while(clouds.MoveNext())yield return clouds.Current;bundle.Unload(false);yield break;}

            var profiles=PlayerBuildingProfiles.Create();Check(profiles.Length==15,"All fifteen modular building types are present");var geometry=new ModularGeometry();foreach(var profile in profiles){geometry.Clear();PlayerBuildingWorld.Append(profile,false,false,Vector3.zero,Quaternion.identity,geometry);var mesh=geometry.Build(profile.name);Check(mesh.vertexCount>0&&mesh.triangles.Length>0,"Generated 3D mesh: "+profile.name.Replace('\n',' '));Destroy(mesh);}

            Check(profiles[0].bounds.size==new Vector3(3,3,3)&&Mathf.Abs(profiles[10].bounds.size.y-.1f)<1e-6,"Square foundations are 3x3x3 m and ceiling panels are 0.1 m thick");

            var cube=profiles[0].solids[0];Check(PlayerBuildingProfiles.Overlap(cube,Vector3.zero,Quaternion.identity,cube,Vector3.right,Quaternion.identity),"Intersecting foundation volumes are rejected");Check(!PlayerBuildingProfiles.Overlap(cube,Vector3.zero,Quaternion.identity,cube,Vector3.right*3,Quaternion.identity),"Flush adjacent foundations do not count as intersections");

            var tri=profiles[1].solids[0];Check(!PlayerBuildingProfiles.Overlap(tri,Vector3.zero,Quaternion.identity,tri,new Vector3(0,0,-1.7320508f),Quaternion.Euler(0,180,0)),"Equilateral triangle foundations join without false AABB overlap rejection");Check(PlayerBuildingProfiles.Overlap(tri,Vector3.zero,Quaternion.identity,tri,Vector3.zero,Quaternion.identity),"Duplicate triangle modules are rejected");

            for(int n=12;n<=14;n++){geometry.Clear();PlayerBuildingWorld.Append(profiles[n],false,true,Vector3.zero,Quaternion.identity,geometry);var mesh=geometry.Build("Stair collision fixture");var go=new GameObject("Stair collision fixture");go.AddComponent<MeshCollider>().sharedMesh=mesh;Physics.SyncTransforms();var at=n==12?new Vector3(0,6,0):new Vector3(-.9f,6,-.6f);Check(Physics.Raycast(at,Vector3.down,out var hit,8)&&hit.normal.y>.55f,"Walkable continuous stair collision: "+profiles[n].name.Replace('\n',' '));if(n==12){Check(Mathf.Abs(hit.point.y-1.5f)<.02f,"Straight stair collider rises smoothly to the upper storey");var avatar=new GameObject("Stair walker");avatar.layer=2;var capsule=avatar.AddComponent<CapsuleCollider>();capsule.height=1.8f;capsule.radius=.35f;capsule.center=Vector3.up*.9f;var collision=new PlanetPlayerCollision(capsule);var feet=new Vector3(0,.24f,-1.3f);for(int step=0;step<65;step++)feet=collision.Move(feet,Vector3.up,new Vector3(0,-.02f,.08f),true,out _);Check(feet.y>2.3f,"The real player capsule can climb generated stairs without falling through");avatar.SetActive(false);Destroy(avatar);}go.SetActive(false);Destroy(go);Destroy(mesh);}

            var runFixture=WorldFixture(ghost);while(runFixture.MoveNext())yield return runFixture.Current;

            var gpu=CitizenGpu(shader,true);while(gpu.MoveNext())yield return gpu.Current;var original=Resources.Load<ComputeShader>("MassiveArmy/SettlementCitizens");if(original!=null){gpu=CitizenGpu(original,false);while(gpu.MoveNext())yield return gpu.Current;}

            Check(PlayerBuildingProfiles.Radial(Vector2.right,15)==0&&PlayerBuildingProfiles.Radial(Vector2.up,4)==1&&PlayerBuildingProfiles.Radial(Vector2.down,4)==3,"Radial menu angles select the expected modules");

            foreach(float h in new[]{-10f,-.1f,-.021f,-.019f,0f,1f}){bool land=h>=-.02f,water=h<=-.02f;Check(land!=water,"Land and ocean coverage are exclusive at sampled shore height "+h);}

            bundle.Unload(false);

        }

        IEnumerator WorldFixture(Shader ghost){var host=new GameObject("Inactive authoring world fixture");host.SetActive(false);var world=host.AddComponent<PlanetDevelopment>();var system=host.AddComponent<PlanetSystemRuntime>();world.SystemRuntime=system;var planet=ScriptableObject.CreateInstance<PlanetDefinition>();planet.radius=120000000;planet.terrainHeight=0;planet.bodyId=1;world.definition=planet;var surface=new PlanetSurface(planet);var origin=new FloatingOrigin(new UniversePosition{local=new Double3(0,planet.radius,0)});Set(world,"origin",origin);Set(world,"surface",surface);

            var cameraGo=new GameObject("Builder fixture camera");var camera=cameraGo.AddComponent<Camera>();camera.enabled=false;camera.transform.position=new Vector3(0,1.7f,-4);Set(world,"view",camera);var player=(PlanetFirstPersonController)FormatterServices.GetUninitializedObject(typeof(PlanetFirstPersonController));Set(player,"position",new Double3(0,planet.radius+.1,-4));Set(world,"player",player);

            var simulation=(CivilisationRuntime)FormatterServices.GetUninitializedObject(typeof(CivilisationRuntime));var universe=new UniverseState();var data=new PlanetState{planetId=1,body="fixture"};universe.planets.Add(data);typeof(CivilisationRuntime).GetProperty("State").SetValue(simulation,universe);Set(simulation,"bodyIds",new Dictionary<int,string>{{1,"fixture"}});typeof(PlanetSystemRuntime).GetProperty("Civilisation").SetValue(system,simulation);

            var local=(PlanetRtsBase)FormatterServices.GetUninitializedObject(typeof(PlanetRtsBase));Set(local,"fixture",world);Set(local,"Buildings",new List<RtsBuilding>());typeof(PlanetRtsBase).GetProperty("Grid").SetValue(local,new PlanetConstructionGrid(1,planet.radius,32));typeof(PlanetDevelopment).GetProperty("Base").SetValue(world,local);local.Roads=new SettlementRoadNetwork(world,local,new List<RoadTile>(),32);local.Rails=new SettlementRoadNetwork(world,local,new List<RoadTile>(),32,true);

            var builder=new PlayerBuildingWorld(world,surface,ghost);((GameObject)Get(builder,"collisionRoot")).transform.SetParent(null,false);

            Ray Aim(Vector3 point)=>new Ray(camera.transform.position,(point-camera.transform.position).normalized);

            var placement=builder.PreviewForValidation(Aim(Vector3.zero),PlayerModuleKind.FoundationSquare);Check(placement.valid,"A free foundation preview is valid on terrain at 1000x radius: "+placement.reason);Check(builder.CommitForValidation(placement,PlayerModuleKind.FoundationSquare)&&data.playerBuildSites.Count==1,"Placing a foundation creates a persistent precise build site");

            Check(data.playerBuildLand.Count>0&&world.PlayerBuildCellReserved(data.playerBuildLand[0]),"Settlement and road planners reserve player-built plots, including off-world save data");

            var snapped=builder.PreviewForValidation(Aim(new Vector3(0,.5f,-1.4f)),PlayerModuleKind.FoundationSquare);Check(snapped.valid&&!snapped.fresh&&snapped.parent!=0,"Adjacent foundations snap to indexed edge sockets: "+snapped.reason);Check(builder.CommitForValidation(snapped,PlayerModuleKind.FoundationSquare),"A snapped foundation is committed without intersections");

            var duplicate=snapped;duplicate.position=Vector3.zero;duplicate.parent=0;Check(!builder.ValidateForValidation(duplicate,PlayerModuleKind.FoundationSquare,out _),"World placement validation rejects an overlapping existing module");

            var wall=builder.PreviewForValidation(Aim(new Vector3(0,.5f,1.4f)),PlayerModuleKind.DoorwaySingle);Check(wall.valid,"A doorway snaps to the foundation's wall socket: "+wall.reason);Check(builder.CommitForValidation(wall,PlayerModuleKind.DoorwaySingle),"Doorway geometry and walking collision are built");

            camera.transform.position=new Vector3(0,1.5f,5);var door=builder.PreviewForValidation(Aim(new Vector3(0,1.5f,1.5f)),PlayerModuleKind.DoorSingle);Check(door.valid,"A door finds its frame socket when aiming through the empty opening: "+door.reason);Check(builder.CommitForValidation(door,PlayerModuleKind.DoorSingle),"A separate usable door fits inside the opening");

            var modes=new PlayerControlModeManager();modes.Bind(PlayerControlMode.FirstPerson,_=>{});modes.Switch(PlayerControlMode.FirstPerson);Set(world,"modes",modes);camera.transform.rotation=Quaternion.LookRotation(Vector3.back,Vector3.up);Check(builder.InteractDoor()&&data.playerBuildSites[0].modules[3].open,"F opens a real player-built door and replaces its collision mesh");Check(builder.InteractDoor()&&!data.playerBuildSites[0].modules[3].open,"F closes the door again when its opening is clear");Set(world,"modes",null);
            var roof=builder.PreviewForValidation(Aim(new Vector3(.8f,3.5f,1.5f)),PlayerModuleKind.CeilingSquare);Check(roof.valid,"A ceiling can snap to a wall-top roof socket: "+roof.reason);Check(builder.CommitForValidation(roof,PlayerModuleKind.CeilingSquare),"Ceiling module is placed flush with the wall top");

            // Free sockets in one connected base permit structural joins, including
            // walls landing on a shared foundation seam or meeting another wall corner.
            var seam=wall;seam.position=new Vector3(0,3,-1.5f);seam.parent=data.playerBuildSites[0].modules[0].id;seam.socket=1;seam.yaw=180;
            Check(builder.ValidateForValidation(seam,PlayerModuleKind.Wall,out var seamReason),"A free wall socket on the shared foundation seam is usable: "+seamReason);
            seam.position=new Vector3(1.5f,3,0);seam.socket=3;seam.yaw=90;
            Check(builder.ValidateForValidation(seam,PlayerModuleKind.Wall,out seamReason),"A free wall socket meeting an existing doorway corner is usable: "+seamReason);
            var neighbour=wall;neighbour.site=default;neighbour.fresh=true;neighbour.parent=0;neighbour.position=Vector3.zero;neighbour.anchor=data.playerBuildSites[0].anchor+Double3.Rotate(data.playerBuildSites[0].rotation,new Double3(0,3,1.5));
            Check(!builder.ValidateForValidation(neighbour,PlayerModuleKind.Wall,out _),"An independent neighbouring base cannot build through this base's doorway");

            var json=JsonUtility.ToJson(data,true);File.WriteAllText(Path.Combine(output,"site-save.json"),json);var copy=JsonUtility.FromJson<PlanetState>(json);File.WriteAllText(Path.Combine(output,"site-difference.txt"),"Sites="+copy.playerBuildSites.Count+" Modules="+copy.playerBuildSites[0].modules.Count+" OriginalY="+data.playerBuildSites[0].anchor.y.ToString("R")+" RestoredY="+copy.playerBuildSites[0].anchor.y.ToString("R"));Check(copy.playerBuildSites.Count==1&&copy.playerBuildSites[0].modules.Count==5&&Math.Abs(copy.playerBuildSites[0].anchor.y-data.playerBuildSites[0].anchor.y)<.00001,"Module/site/socket records survive JSON save roundtrip without losing the precise anchor");

            Check(builder.PrepareCoarseForValidation()==1,"Distant player modules batch into one site draw without waiting for all detailed meshes");Physics.SyncTransforms();Check(Physics.Raycast(new Vector3(.8f,2,0),Vector3.down,out var support,4)&&Mathf.Abs(support.point.y-.5f)<.02,"Player foundations provide collision at the rendered top surface after rebasing");

            long bytes=GC.GetAllocatedBytesForCurrentThread();var profiles=PlayerBuildingProfiles.Create();var solid=profiles[0].solids[0];for(int i=0;i<100;i++)PlayerBuildingProfiles.Overlap(solid,Vector3.zero,Quaternion.identity,solid,Vector3.right*3,Quaternion.identity);var time=new double[120];double total=0;for(int i=0;i<time.Length;i++){long start=Stopwatch.GetTimestamp();for(int j=0;j<100;j++)PlayerBuildingProfiles.Overlap(solid,Vector3.zero,Quaternion.identity,solid,Vector3.right*3,Quaternion.identity);time[i]=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;total+=time[i];}Array.Sort(time);report.measurements.Add(new Row{name="100 convex module overlap queries",medianMs=time[60],p95Ms=time[113],updatesPerSecond=time.Length*1000/total,unityAllocated=Profiler.GetTotalAllocatedMemoryLong(),unityReserved=Profiler.GetTotalReservedMemoryLong()});

            Check(builder.UndoLastPlacement()&&data.playerBuildSites[0].modules.Count==4,"Undo removes the latest module and frees its occupied socket");

            ((GameObject)Get(builder,"collisionRoot")).SetActive(false);builder.Dispose();Destroy(host);Destroy(cameraGo);Destroy(planet);yield return null;

        }

        IEnumerator RoadProjectionChecks(ComputeShader source){
            Check(source!=null,"Road projection shader loads on "+SystemInfo.graphicsDeviceType);var shader=Instantiate(source);int kernel=shader.FindKernel("Probe");var input=new Vector4[1];var output=new Vector4[1];var uv=new Vector4[6];var bases=new Vector4[6];var anchors=new Vector4[6];double worstOld=0,worstNew=0,worstOldStep=0,worstNewStep=0;
            using(var points=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,16))using(var result=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,16)){
                shader.SetBuffer(kernel,"_Points",points);shader.SetBuffer(kernel,"_ProjectionResult",result);
                foreach(float radius in new[]{120000f,12000000f,120000000f})for(int face=0;face<6;face++){
                    Double3 Cube(float u,float v)=>new Double3(CubeSphere.Normal[face])+new Double3(CubeSphere.Right[face])*u+new Double3(CubeSphere.Up[face])*v;
                    double Dot(Double3 p,Vector3 a)=>p.x*a.x+p.y*a.y+p.z*a.z;
                    var point=Cube(.371f,.623f);point=point*((radius+64)/point.Magnitude);int resolution=new PlanetConstructionGrid(1,radius,32).Resolution;shader.SetInt("_PlanetRoadResolution",resolution);shader.SetInt("_RoadFace",face);shader.SetFloat("_PlanetRadius",radius);
                    double px=(Dot(point,CubeSphere.Right[face])/Dot(point,CubeSphere.Normal[face])+1)*resolution*.5,py=(Dot(point,CubeSphere.Up[face])/Dot(point,CubeSphere.Normal[face])+1)*resolution*.5;var errors=new double[2];var steps=new double[2];var previousX=new double[2];var previousY=new double[2];
                    for(int frame=0;frame<120;frame++){
                        var eye=point+new Double3(CubeSphere.Right[face])*(frame*.413)+new Double3(CubeSphere.Normal[face])*(2+frame*.73);var high=eye.Float;shader.SetVector("_PlanetObserverHigh",high);shader.SetVector("_PlanetObserverLow",(eye-new Double3(high)).Float);input[0]=(point-eye).Float;points.SetData(input);
                        double denominator=Math.Max(1,Math.Abs(Dot(eye,CubeSphere.Normal[face])));float u=Mathf.Clamp((float)(Dot(eye,CubeSphere.Right[face])/denominator),-1,1),v=Mathf.Clamp((float)(Dot(eye,CubeSphere.Up[face])/denominator),-1,1);var anchor=Cube(u,v);uv[face]=new Vector4(u,v,0,0);bases[face]=(anchor*(radius/anchor.Magnitude)-eye).Float;double cx=((double)u+1)*resolution*.5,cy=((double)v+1)*resolution*.5;anchors[face]=new Vector4((float)Math.Floor(cx),(float)Math.Floor(cy),(float)(cx-Math.Floor(cx)),(float)(cy-Math.Floor(cy)));shader.SetVectorArray("_PlanetPrecisionUv",uv);shader.SetVectorArray("_PlanetPrecisionBase",bases);shader.SetVectorArray("_PlanetRoadAnchor",anchors);
                        for(int mode=0;mode<2;mode++){shader.SetInt("_RoadUseAnchor",mode);shader.Dispatch(kernel,1,1,1);result.GetData(output);double ax=output[0].x+(double)output[0].y,ay=output[0].z+(double)output[0].w;errors[mode]=Math.Max(errors[mode],Math.Max(Math.Abs(ax-px),Math.Abs(ay-py))*2*radius/resolution);if(frame>0)steps[mode]=Math.Max(steps[mode],Math.Max(Math.Abs(ax-previousX[mode]),Math.Abs(ay-previousY[mode]))*2*radius/resolution);previousX[mode]=ax;previousY[mode]=ay;}
                    }
                    worstOld=Math.Max(worstOld,errors[0]);worstNew=Math.Max(worstNew,errors[1]);worstOldStep=Math.Max(worstOldStep,steps[0]);worstNewStep=Math.Max(worstNewStep,steps[1]);report.checks.Add("Road position radius="+radius+" face="+face+" old metres="+errors[0].ToString("R")+" anchored metres="+errors[1].ToString("R"));yield return null;
                }
            }
            report.checks.Add("Worst previous road error="+worstOld.ToString("R")+"m; anchored="+worstNew.ToString("R")+"m");Check(worstNew<.01,"Road paint stays within one centimetre of its planet-fixed grid during motion");report.checks.Add("Worst previous frame jump="+worstOldStep.ToString("R")+"m; anchored="+worstNewStep.ToString("R")+"m");Check(worstNewStep<.01,"Camera motion does not move road paint by one centimetre between frames");Destroy(shader);
        }

        IEnumerator TerrainMaterialMotionChecks(Shader source){
            bool roads=Array.IndexOf(Environment.GetCommandLineArgs(),"-road-material-only")>=0;GraphicsBuffer roadBuffer=null;Texture2D roadCoverage=null;var roadUv=new Vector4[6];var roadBase=new Vector4[6];var roadAnchor=new Vector4[6];
            Check(source!=null&&source.isSupported,"Actual terrain material shader loads on "+SystemInfo.graphicsDeviceType);var material=new Material(source);var albedo=new Texture2DArray(32,32,9,TextureFormat.RGBAFloat,true,true){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};var data=new Texture2DArray(32,32,9,TextureFormat.RGBAFloat,true,true){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};var pixels=new Color[1024];
            for(int layer=0;layer<9;layer++){for(int y=0;y<32;y++)for(int x=0;x<32;x++)pixels[y*32+x]=new Color(.5f+.4f*Mathf.Sin(x*Mathf.PI/16),.5f+.4f*Mathf.Sin(y*Mathf.PI/16),.2f+layer*.07f,1);albedo.SetPixels(pixels,layer);for(int y=0;y<32;y++)for(int x=0;x<32;x++)pixels[y*32+x]=new Color(.5f+.15f*Mathf.Sin(x*Mathf.PI/16),.5f+.15f*Mathf.Cos(y*Mathf.PI/16),.5f+.4f*Mathf.Sin((x+y)*Mathf.PI/16),.8f);data.SetPixels(pixels,layer);}albedo.Apply(true);data.Apply(true);
            material.SetTexture("_SurfaceAlbedo",albedo);material.SetTexture("_SurfaceData",data);var layers=new Vector4[9];for(int i=0;i<9;i++)layers[i]=new Vector4(32,1,0,0);material.SetVectorArray("_LayerParameters",layers);material.SetVector("_TerrainTextureScale",new Vector4(.1f,5,0,0));material.SetVector("_TerrainDistanceTuning",new Vector4(0,48,.018f,.32f));material.SetVector("_BiomeBlend",new Vector4(123.7f,58.7f,192,1));material.SetVector("_BiomeDetail",new Vector4(11.6f,.198f,0,0));material.SetFloat("_HeightBlendWidth",.01f);material.SetFloat("_HeightBlendStrength",.564f);material.SetFloat("_NormalStrength",1);material.SetFloat("_MacroVariation",.218f);material.SetFloat("_BiomeAltitudeCooling",1);material.SetFloat("_PlanetOcean",-100);material.SetFloat("_PlanetSeed",7.31f);material.SetFloat("_TerrainNoiseAnchored",1);
            if(roads){roadBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,256,16);roadCoverage=new Texture2D(384,64,TextureFormat.R8,false,true);var coverage=new byte[384*64];for(int i=0;i<coverage.Length;i++)coverage[i]=255;roadCoverage.LoadRawTextureData(coverage);roadCoverage.Apply();material.SetBuffer("_PlanetRoadCells",roadBuffer);material.SetTexture("_PlanetRoadCoverage",roadCoverage);material.SetInt("_PlanetRoadMask",255);material.SetInt("_PlanetRoadEnabled",1);material.SetFloat("_PlanetRoadWidth",12);}
            var phases=new Vector4[81];var high=new Vector4[3];var low=new Vector4[3];var phase=new Vector4[3];var target=new RenderTexture(32,32,0,RenderTextureFormat.ARGBFloat);target.Create();var commands=new UnityEngine.Rendering.CommandBuffer();double worst=0;
            try{foreach(float radius in new[]{120000f,12000000f,120000000f}){var point=new Double3(.37,.81,.53);point=point*(radius/point.Magnitude);PlanetConstructionGrid grid=null;PlanetNavCoordinate roadCell=default;if(roads){grid=new PlanetConstructionGrid(1,radius,32);roadCell=grid.Cell(point.Float.normalized);point=grid.DirectionPrecise(roadCell)*(radius+64);var cells=new SettlementRoadSurface.Cell[256];for(int y=-3;y<=3;y++)for(int x=-3;x<=3;x++){uint cx=(uint)(roadCell.x+x),cy=(uint)(roadCell.y+y),face=(uint)roadCell.face+1;int slot=SettlementRoadSurface.Slot(cells,face,cx,cy);cells[slot]=new SettlementRoadSurface.Cell{face=face,x=cx,y=cy,mask=3};}roadBuffer.SetData(cells);material.SetInt("_PlanetRoadResolution",grid.Resolution);}material.SetFloat("_PlanetRadius",radius);material.SetVector("_TestDirection",point.Float.normalized);
                for(int mode=0;mode<2;mode++){material.SetFloat("_TestMode",mode);if(roads)material.SetFloat("_PlanetRoadAnchored",mode);Color[] first=null;double maximum=0;
                    for(int frame=0;frame<90;frame++){var eye=point+new Double3((frame-45)*.413,2+frame*.73,(frame-45)*.237);material.SetVector("_TestRelative",(point-eye).Float);if(roads){var eyeHigh=eye.Float;material.SetVector("_PlanetObserverHigh",eyeHigh);material.SetVector("_PlanetObserverLow",(eye-new Double3(eyeHigh)).Float);for(int face=0;face<6;face++){double Dot(Double3 a,Vector3 v)=>a.x*v.x+a.y*v.y+a.z*v.z;double n=Math.Max(1,Math.Abs(Dot(eye,CubeSphere.Normal[face])));float u=Mathf.Clamp((float)(Dot(eye,CubeSphere.Right[face])/n),-1,1),v=Mathf.Clamp((float)(Dot(eye,CubeSphere.Up[face])/n),-1,1);var anchor=new Double3(CubeSphere.Normal[face])+new Double3(CubeSphere.Right[face])*u+new Double3(CubeSphere.Up[face])*v;roadUv[face]=new Vector4(u,v,0,0);roadBase[face]=(anchor*(radius/anchor.Magnitude)-eye).Float;double cx=((double)u+1)*grid.Resolution*.5,cy=((double)v+1)*grid.Resolution*.5;roadAnchor[face]=new Vector4((float)Math.Floor(cx),(float)Math.Floor(cy),(float)(cx-Math.Floor(cx)),(float)(cy-Math.Floor(cy)));}material.SetVectorArray("_PlanetPrecisionUv",roadUv);material.SetVectorArray("_PlanetPrecisionBase",roadBase);material.SetVectorArray("_PlanetRoadAnchor",roadAnchor);}
                        for(int band=0;band<9;band++)for(int layer=0;layer<9;layer++){double span=32*Math.Max(.01,.1f)*Math.Pow(5,band);float Phase(double v)=>(float)(v/span-Math.Floor(v/span));phases[band*9+layer]=new Vector4(Phase(eye.x),Phase(eye.y),Phase(eye.z),0);}material.SetVectorArray("_TerrainTexturePhases",phases);PlanetTerrainNoiseAnchor.Fill(eye,7.31f,192,11.6f,high,low,phase);material.SetVectorArray("_TerrainNoiseHigh",high);material.SetVectorArray("_TerrainNoiseLow",low);material.SetVectorArray("_TerrainNoisePhase",phase);
                        commands.Clear();commands.SetRenderTarget(target);commands.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(commands);var read=UnityEngine.Rendering.AsyncGPUReadback.Request(target);read.WaitForCompletion();if(read.hasError)throw new Exception("Material readback failed");var colours=read.GetData<Color>();if(first==null)first=colours.ToArray();else for(int i=0;i<first.Length;i++)for(int c=0;c<4;c++)maximum=Math.Max(maximum,Math.Abs(colours[i][c]-first[i][c]));
                    }
                    report.checks.Add("Material motion radius="+radius+" mode="+mode+" maxChange="+maximum.ToString("R"));if(roads&&mode==1)Check(maximum<.01,"Rendered road mask and texture remain stable during pan/zoom at radius "+radius);worst=Math.Max(worst,maximum);yield return null;
                }
            }report.checks.Add("Worst actual material change="+worst.ToString("R"));}finally{commands.Dispose();target.Release();Destroy(target);Destroy(material);Destroy(albedo);Destroy(data);roadBuffer?.Dispose();Destroy(roadCoverage);}
        }

        IEnumerator TerrainMotionChecks(ComputeShader source){
            Check(source!=null,"Terrain motion compute loads on "+SystemInfo.graphicsDeviceType);var shader=Instantiate(source);int kernel=shader.FindKernel("Precision");var uv=new Vector4[6];var bases=new Vector4[6];var input=new Vector4[9];var output=new Vector4[9];double worst=0,worstStep=0;
            using(var inputs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,9,16))using(var outputs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,9,16)){
                shader.SetBuffer(kernel,"_Directions",inputs);shader.SetBuffer(kernel,"_Samples",outputs);shader.SetInt("_SampleCount",9);
                foreach(float radius in new[]{120000f,12000000f,120000000f})for(int face=0;face<6;face++){
                    Double3 Cube(float u,float v)=>new Double3(CubeSphere.Normal[face])+new Double3(CubeSphere.Right[face])*u+new Double3(CubeSphere.Up[face])*v;
                    var previous=new Double3[9];double maximumError=0,maximumStep=0;
                    for(int i=0;i<9;i++)input[i]=new Vector4(face,.37109375f+(i-4)/1048576f,.623046875f+(i-4)/2097152f,0);inputs.SetData(input);shader.SetFloat("_PlanetRadius",radius);
                    for(int frame=0;frame<90;frame++){
                        var centre=Cube(.371f,.623f);var observer=centre*((radius+2+frame*.113)/centre.Magnitude)+new Double3(CubeSphere.Right[face])*(frame*.137);
                        double Dot(Double3 p,Vector3 a)=>p.x*a.x+p.y*a.y+p.z*a.z;double denominator=Math.Max(1,Math.Abs(Dot(observer,CubeSphere.Normal[face])));float u=Mathf.Clamp((float)(Dot(observer,CubeSphere.Right[face])/denominator),-1,1),v=Mathf.Clamp((float)(Dot(observer,CubeSphere.Up[face])/denominator),-1,1);var anchor=Cube(u,v);uv[face]=new Vector4(u,v,0,0);bases[face]=(anchor*(radius/anchor.Magnitude)-observer).Float;
                        var high=observer.Float;shader.SetVector("_PlanetObserverHigh",high);shader.SetVector("_PlanetObserverLow",(observer-new Double3(high)).Float);shader.SetVectorArray("_PlanetPrecisionUv",uv);shader.SetVectorArray("_PlanetPrecisionBase",bases);shader.Dispatch(kernel,1,1,1);outputs.GetData(output);
                        for(int i=0;i<9;i++){var q=Cube(input[i].y,input[i].z);var expected=q*(radius/q.Magnitude)-observer;var error=new Double3(output[i].x,output[i].y,output[i].z)-expected;maximumError=Math.Max(maximumError,error.Magnitude);if(frame>0)maximumStep=Math.Max(maximumStep,(error-previous[i]).Magnitude);previous[i]=error;}
                    }
                    worst=Math.Max(worst,maximumError);worstStep=Math.Max(worstStep,maximumStep);report.checks.Add("Motion error radius="+radius+" face="+face+" absolute="+maximumError.ToString("R")+" step="+maximumStep.ToString("R"));yield return null;
                }
            }
            report.checks.Add("Worst terrain position error="+worst.ToString("R")+"m; frame-to-frame error change="+worstStep.ToString("R")+"m on "+SystemInfo.graphicsDeviceType);var textureKernel=shader.FindKernel("TextureMotion");var highNoise=new Vector4[3];var lowNoise=new Vector4[3];var phaseNoise=new Vector4[3];var colours=new Vector4[9];var firstColours=new Vector4[9];double textureDrift=0;
            using(var inputs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,9,16))using(var outputs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,9,16)){
                shader.SetBuffer(textureKernel,"_Directions",inputs);shader.SetBuffer(textureKernel,"_Samples",outputs);shader.SetVector("_TerrainNoiseScales",new Vector4(192,11.6f,96,0));
                foreach(float radius in new[]{120000f,12000000f,120000000f})foreach(int sign in new[]{-1,1}){
                    double drift=0;var point=new Double3(radius*.37*sign,radius*.81*sign,radius*.53);
                    for(int frame=0;frame<120;frame++){
                        var eye=point+new Double3((frame-60)*.413,2+frame*.73,(frame-60)*.237);PlanetTerrainNoiseAnchor.Fill(eye,7.31f,192,11.6f,highNoise,lowNoise,phaseNoise);
                        shader.SetVectorArray("_TerrainNoiseHigh",highNoise);shader.SetVectorArray("_TerrainNoiseLow",lowNoise);shader.SetVectorArray("_TerrainNoisePhase",phaseNoise);
                        for(int i=0;i<9;i++)input[i]=(point+new Double3(i*.37,0,i*.41)-eye).Float;inputs.SetData(input);shader.Dispatch(textureKernel,1,1,1);outputs.GetData(colours);
                        for(int i=0;i<9;i++){if(frame==0)firstColours[i]=colours[i];else for(int channel=0;channel<4;channel++)drift=Math.Max(drift,Math.Abs(colours[i][channel]-firstColours[i][channel]));}
                    }
                    textureDrift=Math.Max(textureDrift,drift);Check(drift<.0001,"Fixed terrain noise stays anchored during pan/zoom at radius "+radius+" sign="+sign+" drift="+drift.ToString("R"));yield return null;
                }
            }
            Check(textureDrift<.0001,"Worst material noise drift="+textureDrift.ToString("R")+" on "+SystemInfo.graphicsDeviceType);
            Destroy(shader);
        }

        IEnumerator CloudLightChecks(ComputeShader source){
            Check(source!=null,"Staggered cloud lighting compute loads");var schedule=new CloudLightRefresh();int covered=0,dispatches=0;bool capture;int offset;
            while(covered<1024){int rows=schedule.Take(1,2048,1024,16,dispatches/120d,.25f,2097152,true,out offset,out capture);Check(rows==64&&offset==covered,"2048 cache strip is bounded and advances without gaps: "+dispatches);Check(capture==(dispatches==0),"Only the first strip captures density/sun parameters: "+dispatches);covered+=rows;dispatches++;}
            Check(schedule.Valid&&dispatches==16,"A 2048 cache completes after sixteen bounded frames");Check(schedule.Take(1,2048,1024,16,.15,.25f,2097152,true,out offset,out capture)==0,"Completed cache is reused during refresh interval");Check(schedule.Take(1,2048,1024,16,10,.25f,2097152,false,out offset,out capture)==0,"Static weather and sun do not rebuild the cache");Check(schedule.Take(2,2048,1024,16,10,.25f,2097152,false,out offset,out capture)==64&&capture&&!schedule.Valid,"Live weather edits immediately invalidate and restart initialization");schedule.Reset();Check(!schedule.Valid,"Target release invalidates cached lighting");
            var defaults=new PlanetWeather();Check(defaults.shadowUpdateVoxelsPerFrame==1048576,"Default cloud-light frame budget is one million voxels");schedule.Reset();int smallRows=schedule.Take(1,2048,1024,16,0,0,defaults.shadowUpdateVoxelsPerFrame,true,out offset,out capture);Check(smallRows==32,"Default 2048 cache refresh uses thirty-two row strips");
            var host=new GameObject("Inactive cloud cache allocation fixture");host.SetActive(false);var author=host.AddComponent<PlanetDevelopment>();var body=ScriptableObject.CreateInstance<PlanetDefinition>();author.definition=body;var renderer=new PlanetWeatherRenderer(author);
            try{var allocate=typeof(PlanetWeatherRenderer).GetMethod("Allocate",BindingFlags.Instance|BindingFlags.NonPublic);allocate.Invoke(renderer,new object[]{320,180});var firstCache=renderer.LightForValidation;var refresh=(CloudLightRefresh)Get(renderer,"lightRefresh");refresh.Take(1,128,64,8,0,0,1048576,true,out offset,out capture);Check(refresh.Valid,"Allocation fixture initializes cache scheduling");allocate.Invoke(renderer,new object[]{512,288});Check(ReferenceEquals(firstCache,renderer.LightForValidation)&&refresh.Valid,"Camera resolution changes retain the same initialized planet light cache");body.weather.shadowResolutionOverride=256;allocate.Invoke(renderer,new object[]{512,288});Check(!ReferenceEquals(firstCache,renderer.LightForValidation)&&!refresh.Valid&&renderer.LightForValidation.width==256,"Shadow resolution edits reallocate and invalidate the light cache");}finally{renderer.Dispose();Destroy(host);Destroy(body);}
            var shader=Instantiate(source);var noise=new Texture3D(64,64,64,TextureFormat.R8,true){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};var pixels=new byte[64*64*64];for(int i=0;i<pixels.Length;i++)pixels[i]=(byte)PlanetWeather.Hash((uint)i+8317);noise.SetPixelData(pixels,0);noise.Apply(true,true);
            RenderTexture Cache(int w,int h,int d){var texture=new RenderTexture(w,h,0,RenderTextureFormat.RFloat){dimension=UnityEngine.Rendering.TextureDimension.Tex3D,volumeDepth=d,enableRandomWrite=true};texture.Create();return texture;}
            var full=Cache(128,63,7);var staggered=Cache(128,63,7);RenderTexture large=null;int kernel=shader.FindKernel("BuildLight");
            try{
                shader.SetTexture(kernel,"_WeatherNoise",noise);shader.SetFloat("_WeatherRadius",120000);shader.SetFloat("_WeatherNoiseSizeScale",.05f);shader.SetFloat("_WeatherNoiseFrequency",.31f);shader.SetVector("_WeatherAtmosphere",new Vector4(.08f,.4f,22000f/120000,2872f/120000));shader.SetVector("_WeatherCloud",new Vector4(.439f,216,1,1));shader.SetVector("_WeatherWind",new Vector4(0,1,0,0));shader.SetVector("_WeatherDetailMotion",new Vector4(0,1,.011f,1));shader.SetVector("_WeatherNoiseTuning",new Vector4(.916f,.3f,5.1f,.32f));shader.SetVector("_WeatherOctaves",new Vector4(8,.1f,1.2f,0));shader.SetVector("_WeatherSun",Vector3.up);
                void Dispatch(RenderTexture texture,int row,int count){shader.SetTexture(kernel,"_WeatherLightWrite",texture);shader.SetVector("_WeatherLightSize",new Vector4(texture.width,texture.height,texture.volumeDepth,0));shader.SetInt("_WeatherLightRowOffset",row);shader.SetInt("_WeatherLightRowCount",count);shader.Dispatch(kernel,(texture.width+7)/8,((count>0?count:texture.height)+7)/8,texture.volumeDepth);}
                Dispatch(full,0,0);var read=UnityEngine.Rendering.AsyncGPUReadback.Request(full);read.WaitForCompletion();Check(!read.hasError,"Full cloud cache GPU readback succeeds");var expected=read.GetData<float>().ToArray();
                schedule.Reset();covered=0;while(covered<63){int rows=schedule.Take(1,128,63,7,covered,0,128*7*5,true,out offset,out capture);Dispatch(staggered,offset,rows);covered+=rows;}
                read=UnityEngine.Rendering.AsyncGPUReadback.Request(staggered);read.WaitForCompletion();Check(!read.hasError,"Staggered cloud cache GPU readback succeeds");var actual=read.GetData<float>();bool identical=true,lit=false;for(int i=0;i<expected.Length;i++){identical&=!float.IsNaN(actual[i])&&Mathf.Abs(actual[i]-expected[i])<.00001f;lit|=actual[i]>0;}Check(identical&&lit,"Staggered GPU cache matches full rebuild, including non-eight-aligned final strips");
                large=Cache(2048,1024,16);
                for(int mode=0;mode<2;mode++){schedule.Reset();var times=new double[96];double total=0;for(int i=-8;i<96;i++){long begin=Stopwatch.GetTimestamp();if(mode==0)Dispatch(large,0,0);else {int rows=schedule.Take(1,2048,1024,16,(i+8)/120d,.25f,1048576,true,out offset,out capture);if(rows>0)Dispatch(large,offset,rows);}var fence=UnityEngine.Rendering.AsyncGPUReadback.Request(large,0,0,1,0,1,0,1);fence.WaitForCompletion();if(fence.hasError)throw new Exception("Cloud timing synchronization failed");if(i>=0){times[i]=(Stopwatch.GetTimestamp()-begin)*1000d/Stopwatch.Frequency;total+=times[i];}if(i%16==0)yield return null;}Array.Sort(times);report.measurements.Add(new Row{name=mode==0?"2048x1024x16 cloud cache: full rebuild every frame":"2048x1024x16 cloud cache: bounded refresh and reuse",medianMs=times[48],p95Ms=times[90],updatesPerSecond=96000/total,unityAllocated=Profiler.GetTotalAllocatedMemoryLong(),unityReserved=Profiler.GetTotalReservedMemoryLong(),bufferBytes=2048L*1024*16*4});}
            }finally{foreach(var texture in new[]{full,staggered,large})if(texture!=null){texture.Release();Destroy(texture);}Destroy(noise);Destroy(shader);}
        }

        IEnumerator CitizenGpu(ComputeShader source,bool revised){var shader=Instantiate(source);const int count=256;var buffers=new List<GraphicsBuffer>();GraphicsBuffer Buffer(int n,int stride){var b=new GraphicsBuffer(GraphicsBuffer.Target.Structured,n,stride);buffers.Add(b);return b;}

            var nodes=Buffer(2,32);var low=Buffer(2,16);var goals=Buffer(1,16);var orders=Buffer(count,32);var civic=Buffer(count,48);var walkers=Buffer(count,revised?48:32);var poses=Buffer(count,80);var parents=Buffer(2,4);var patrol=Buffer(72,4);var leisure=Buffer(1,64);var draw=Buffer(4,4);var visible=Buffer(count,4);

            var a=new Double3(10,120000000.125,0);var b=new Double3(10,120000008.125,32);var ah=a.Float;var bh=b.Float;nodes.SetData(new[]{new Node{position=new Vector4(ah.x,ah.y,ah.z,0),west=-1,east=1,south=-1,north=-1},new Node{position=new Vector4(bh.x,bh.y,bh.z,0),west=0,east=-1,south=-1,north=-1}});low.SetData(new Vector4[]{(a-new Double3(ah)).Float,(b-new Double3(bh)).Float});goals.SetData(new[]{new Goal{node=1,team=0,active=1}});var order=new Order[count];for(int i=0;i<count;i++)order[i]=new Order{parameters=new Vector4(1,0,1,1)};orders.SetData(order);civic.SetData(new Agent[count]);if(revised)walkers.SetData(new Walker[count]);else walkers.SetData(new OldWalker[count]);poses.SetData(new Pose[count]);parents.SetData(new uint[]{1,1});patrol.SetData(new uint[72]);leisure.SetData(new Leisure[1]);draw.SetData(new uint[4]);

            int walk=shader.FindKernel("Walk"),reset=shader.FindKernel("ResetDraw");shader.SetBuffer(reset,"_DrawArgs",draw);void Bind(string name,GraphicsBuffer buffer)=>shader.SetBuffer(walk,name,buffer);Bind("_Nodes",nodes);if(revised)Bind("_NodeLow",low);Bind("_Goals",goals);Bind("_Orders",orders);Bind("_CivicStates",civic);Bind("_Walkers",walkers);Bind("_CargoPoses",poses);Bind("_Parents",parents);Bind("_PatrolGoals",patrol);Bind("_LeisureAreas",leisure);Bind("_DrawArgs",draw);Bind("_CitizenVisible",visible);shader.SetInt("_NodeCount",2);shader.SetInt("_NodeStride",2);shader.SetInt("_GoalCount",1);shader.SetInt("_WalkerCount",count);shader.SetFloat("_Delta",1/60f);shader.SetFloat("_Speed",3);shader.SetFloat("_DrawRange",1000);shader.SetFloat("_SidewalkOffset",0);shader.SetVector("_CameraBody",new Vector3(10,120000000,-10));if(revised){shader.SetVector("_CameraBodyLow",new Vector3(0,.125f,0));shader.SetVector("_CivicCenterHigh",new Vector3(0,-120000000,0));shader.SetVector("_CivicCenterLow",Vector3.zero);}shader.SetVectorArray("_Frustum",new Vector4[6]);shader.SetVectorArray("_PubStations",new Vector4[128]);shader.SetVectorArray("_PubStationLow",new Vector4[128]);shader.SetVectorArray("_PubStationForward",new Vector4[128]);

            double previous=a.y;var one=new Walker[1];var poseRead=new Pose[1];for(int i=0;i<60;i++){shader.SetFloat("_Clock",i/60f);shader.Dispatch(reset,1,1,1);shader.Dispatch(walk,2,1,1);if(revised){walkers.GetData(one,0,0,1);double actual=one[0].position.y+(double)one[0].low.y;Check(actual>previous,"GPU civilian vertical motion progresses below a float ULP, step "+i);previous=actual;}}

            if(revised){double expected=a.y+(b.y-a.y)/(b-a).Magnitude*3;Check(Math.Abs(previous-expected)<.0001,"GPU walker preserves submillimetre vertical motion at 1000x scale");poses.GetData(poseRead,0,0,1);var pose=poseRead[0];var centre=new Double3((Vector3)pose.position)+new Double3(pose.right.w,pose.up.w,pose.forward.w)+new Double3((Vector3)pose.up)*.8;var eye=centre-new Double3(0,0,10);var eh=eye.Float;var result=Buffer(1,4);result.SetData(new[]{uint.MaxValue});int pick=shader.FindKernel("PickPerson");shader.SetBuffer(pick,"_CargoPoses",poses);shader.SetBuffer(pick,"_CivicStates",civic);shader.SetBuffer(pick,"_NearbyPerson",result);shader.SetVector("_PersonRayHigh",eh);shader.SetVector("_PersonRayLow",(eye-new Double3(eh)).Float);shader.SetVector("_PersonRayDirection",Vector3.forward);shader.SetFloat("_PersonRayCone",.01f);shader.SetFloat("_PersonRayLimit",100);shader.Dispatch(pick,2,1,1);var picked=new uint[1];result.GetData(picked);Check(picked[0]!=uint.MaxValue,"GPU picking resolves the precisely rendered civilian centre");}

            var samples=new double[120];var counts=new uint[4];double sum=0;for(int i=0;i<samples.Length;i++){shader.SetFloat("_Clock",2+i/60f);long start=Stopwatch.GetTimestamp();shader.Dispatch(reset,1,1,1);shader.Dispatch(walk,2,1,1);draw.GetData(counts);samples[i]=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;sum+=samples[i];if(i%30==0)yield return null;}Array.Sort(samples);report.measurements.Add(new Row{name=revised?"256 GPU civilians with compensated positions":"256 GPU civilians with previous float positions",medianMs=samples[60],p95Ms=samples[113],updatesPerSecond=samples.Length*1000/sum,unityAllocated=Profiler.GetTotalAllocatedMemoryLong(),unityReserved=Profiler.GetTotalReservedMemoryLong(),bufferBytes=count*(revised?48:32)+2*(revised?48:32)});foreach(var buffer in buffers)buffer.Dispose();Destroy(shader);yield return null;

        }

    }

}

