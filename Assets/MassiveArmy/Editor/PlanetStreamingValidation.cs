using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class PlanetStreamingValidation
    {
        [Serializable]sealed class Report { public string status="PASS",error="";public List<string> checks=new List<string>();public long bufferBytes;public int maximumResident,maximumLevel; }
        [MenuItem("Massive Army/Planetary/Validate Streamed Terrain")]
        public static void Run()
        {
            var report=new Report();var definition=ScriptableObject.CreateInstance<PlanetDefinition>();
            var root=new GameObject("Streaming validation"){hideFlags=HideFlags.DontSave};var camera=root.AddComponent<Camera>();
            var target=new RenderTexture(2560,1440,24);camera.targetTexture=target;camera.fieldOfView=48;camera.nearClipPlane=.1f;camera.farClipPlane=2000000;
            void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);report.checks.Add(label);}
            try{
                using(var terrain=new PlanetStreamedTerrain(definition)){
                    report.bufferBytes=terrain.BufferBytes;
                    foreach(float radius in new[]{500f,2000f,10000f,PlanetDefinition.TargetRadius}){
                        definition.radius=radius;
                        foreach(var direction in new[]{Vector3.forward,Vector3.back,Vector3.up,Vector3.down,new Vector3(1,1,1).normalized,new Vector3(-1,-1,-1).normalized}){
                            camera.transform.position=direction*(radius+30);CubeSphere.Project(direction,out var face,out _);camera.transform.rotation=Quaternion.LookRotation(-direction,CubeSphere.Right[(int)face]);
                            terrain.UpdateReferenceForValidation(camera,Vector3.zero,force:true);ValidateCoverage(terrain);
                            if(terrain.ResidentPatches==0||terrain.ResidentPatches>terrain.PatchBudget||terrain.DeepestLevel<2)throw new InvalidOperationException("Invalid resident detail at radius "+radius+" direction "+direction);
                            report.maximumResident=Mathf.Max(report.maximumResident,terrain.ResidentPatches);report.maximumLevel=Mathf.Max(report.maximumLevel,terrain.DeepestLevel);
                        }
                        Check(true,"Bounded visible patches and complete sparse coverage at poles, antipodes and cube corners; radius "+radius);
                    }
                    Check(terrain.BufferBytes==report.bufferBytes&&terrain.BufferBytes<24576,"GPU patch storage stays below 24 KiB as radius grows 240 times");
                    int revision=terrain.Revision;terrain.UpdateReferenceForValidation(camera,Vector3.zero);Check(terrain.Revision==revision,"Stationary observer reuses descriptors without rebuilding or uploading terrain");
                    int resident=terrain.ResidentPatches,level=terrain.DeepestLevel;var offset=new Vector3(1024,-512,256);camera.transform.position+=offset;terrain.UpdateReferenceForValidation(camera,offset,force:true);
                    Check(terrain.ResidentPatches==resident&&terrain.DeepestLevel==level,"Floating-origin translation preserves visible residency and detail");
                    definition.radius=2000;camera.transform.position=Vector3.forward*8000;camera.transform.rotation=Quaternion.LookRotation(Vector3.forward,Vector3.up);terrain.UpdateReferenceForValidation(camera,Vector3.zero,force:true);
                    Check(terrain.ResidentPatches==0&&terrain.LeafCount==6,"Looking away evicts all GPU draw patches and retains only six coarse CPU roots");
                    camera.transform.position=Vector3.back*2030;camera.transform.rotation=Quaternion.LookRotation(Vector3.forward,Vector3.up);terrain.UpdateReferenceForValidation(camera,Vector3.zero,force:true);ValidateCoverage(terrain);
                    Check(terrain.ResidentPatches>0&&terrain.LeafCount>6,"Visiting an evicted region regenerates detail from stable surface addresses");
                }
                using(var tiny=new PlanetStreamedTerrain(definition,6)){
                    tiny.UpdateReferenceForValidation(camera,Vector3.zero,force:true);Check(tiny.BudgetLimited&&tiny.LeafCount==6,"Exhausted budget is reported without dropping coarse coverage or allocating more storage");
                }
                var shader=Resources.Load<Shader>("MassiveArmy/PlanetTerrain");Check(shader!=null&&shader.isSupported,"Shared terrain shader supports streamed patches and existing surface layers");
                ValidateInterest();Check(true,"GPU interest follows offscreen troops across cube seams and corners at 2 km and 120 km radii; distant slots sleep and camera visits wake them");
                using(var gpu=new PlanetStreamedTerrain(definition)){
                    foreach(float radius in new[]{2000f,PlanetDefinition.TargetRadius})foreach(var direction in new[]{Vector3.up,Vector3.forward,new Vector3(1,1,1).normalized}){
                        definition.radius=radius;camera.transform.position=direction*(radius+30);CubeSphere.Project(direction,out var face,out _);camera.transform.rotation=Quaternion.LookRotation(-direction,CubeSphere.Right[(int)face]);gpu.Update(camera,Vector3.zero,force:true);var patches=gpu.ReadPatchesForValidation();
                        if(patches.Length==0||patches.Length>gpu.PatchBudget||gpu.GpuLeafCount>gpu.PatchBudget)throw new InvalidOperationException("GPU residency budget failed");
                        foreach(var patch in patches)if(patch.z<=0||patch.x< -1.0001f||patch.y< -1.0001f||patch.x+patch.z>1.0001f||patch.y+patch.z>1.0001f||patch.w<0||patch.w>5)throw new InvalidOperationException("Invalid GPU patch address");
                        var draw=new uint[4];gpu.DrawArguments.GetData(draw);if(draw[0]!=gpu.VerticesPerPatch||draw[1]!=patches.Length)throw new InvalidOperationException("GPU indirect count mismatch");
                    }
                    Check(true,"GPU residency and indirect draws remain bounded at 2 km and 120 km radii, including poles and cube corners");
                }
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(definition);}
            Directory.CreateDirectory("TestResults/Planetary/Streaming");File.WriteAllText("TestResults/Planetary/Streaming/validation.json",JsonUtility.ToJson(report,true));
            if(report.status!="PASS")throw new InvalidOperationException(report.error);Debug.Log("Planet streaming validation PASS");
        }
        static void ValidateCoverage(PlanetStreamedTerrain terrain)
        {
            var area=new double[6];var identities=new HashSet<string>();
            foreach(var p in terrain.Leaves){if(!identities.Add(p.face+":"+p.level+":"+p.x+":"+p.y))throw new InvalidOperationException("Duplicate patch address");area[p.face]+=1.0/(1L<<(p.level*2));}
            foreach(var p in terrain.Leaves)for(int level=p.level-1;level>=0;level--)if(identities.Contains(p.face+":"+level+":"+(p.x>>(p.level-level))+":"+(p.y>>(p.level-level))))throw new InvalidOperationException("Patch overlaps an active ancestor");
            foreach(double value in area)if(Math.Abs(value-1)>1e-10)throw new InvalidOperationException("Sparse quadtree leaves a hole in globe coverage");
            if(terrain.LeafCount>terrain.PatchBudget)throw new InvalidOperationException("Patch budget exceeded");
        }
        static void ValidateInterest()
        {
            var shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetEncounter"));
            try{
                using(var units=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,UnitState.Stride))
                using(var sources=new GraphicsBuffer(GraphicsBuffer.Target.Structured,6*240*240,4))
                using(var regions=new GraphicsBuffer(GraphicsBuffer.Target.Structured,6*240*240,4))
                using(var activity=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,4))
                using(var indices=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,4))
                using(var arguments=new GraphicsBuffer(GraphicsBuffer.Target.Structured,6,4))
                using(var steering=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,16)){
                    foreach(string name in new[]{"ClearInterest","MarkInterest","ExpandInterest","ClassifyInterest","BuildActiveDispatch"}){int k=shader.FindKernel(name);shader.SetBuffer(k,"_Units",units);shader.SetBuffer(k,"_InterestSources",sources);shader.SetBuffer(k,"_InterestRegions",regions);shader.SetBuffer(k,"_UnitActivity",activity);shader.SetBuffer(k,"_ActiveIndices",indices);shader.SetBuffer(k,"_ActiveDispatch",arguments);shader.SetBuffer(k,"_Steering",steering);}
                    shader.SetInt("_Count",4);shader.SetFloat("_InterestRadius",600);
                    foreach(float radius in new[]{2000f,PlanetDefinition.TargetRadius})foreach(var direction in new[]{Vector3.up,Vector3.down,Vector3.right,Vector3.forward,new Vector3(1,1,1).normalized,new Vector3(-1,-1,-1).normalized}){
                        int resolution=Mathf.Clamp(Mathf.CeilToInt(radius*2/1000),8,512);shader.SetFloat("_PlanetRadius",radius);shader.SetInt("_InterestRes",resolution);
                        var tangent=Vector3.Cross(direction,Mathf.Abs(direction.y)>.9f?Vector3.right:Vector3.up).normalized;var near=(direction+tangent*10/radius).normalized;
                        var records=new[]{new UnitState{position=CubeSphere.Encode(direction),flags=1,health=100},new UnitState{position=CubeSphere.Encode(near),flags=1|262144|524288,health=160},new UnitState{position=CubeSphere.Encode(-direction),flags=1|262144|524288,health=160},new UnitState{position=CubeSphere.Encode(near),flags=0}};
                        units.SetData(records);activity.SetData(new uint[4]);shader.SetVector("_InterestObserver",tangent);
                        void Classify(){shader.Dispatch(shader.FindKernel("ClearInterest"),(6*resolution*resolution+127)/128,1,1);shader.Dispatch(shader.FindKernel("MarkInterest"),1,1,1);shader.Dispatch(shader.FindKernel("ExpandInterest"),(6*resolution*resolution+127)/128,1,1);shader.Dispatch(shader.FindKernel("ClassifyInterest"),1,1,1);shader.Dispatch(shader.FindKernel("BuildActiveDispatch"),1,1,1);}
                        Classify();var states=new uint[4];activity.GetData(states);if(states[0]!=1||states[1]!=1||states[2]!=0||states[3]!=0)throw new InvalidOperationException("Offscreen actor interest failed at "+radius+" "+direction);
                        shader.SetVector("_InterestObserver",-direction);Classify();activity.GetData(states);if(states[2]!=1)throw new InvalidOperationException("Camera did not wake distant slot");
                        records[0].flags=0;units.SetData(records);shader.SetVector("_InterestObserver",tangent);Classify();activity.GetData(states);if(states[1]!=0||states[2]!=0)throw new InvalidOperationException("Leaving interest sources did not restore dormancy");
                    }
                }
            }finally{UnityEngine.Object.DestroyImmediate(shader);}
        }
    }
}
