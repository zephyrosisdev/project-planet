using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace MassiveArmy.EditorTools
{
    public static class TerrainLightingValidation
    {
        [Serializable] sealed class Report { public string status="PASS",error;public string[] checks;public float rotationDifference,intensityDifference,colourDifference,shadowDifference; }
        public static void Batch(){Run();var result=File.ReadAllText("TestResults/terrain-scene-lighting-validation.json");EditorApplication.Exit(result.Contains("\"status\": \"PASS\"")?0:1);}
        [MenuItem("Massive Army/Art/Validate Terrain Scene Lighting")]
        public static void Run()
        {
            var report=new Report();var checks=new List<string>();var objects=new List<UnityEngine.Object>();
            var scene=EditorSceneManager.NewPreviewScene();var previous=RenderTexture.active;
            void Require(bool condition,string label){if(!condition)throw new Exception(label);checks.Add(label);}
            GameObject Object(string name){var go=new GameObject(name){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(go,scene);objects.Add(go);return go;}
            Texture2D Mask(Color colour){var t=new Texture2D(1,1,TextureFormat.RGBA32,false,true);t.SetPixel(0,0,colour);t.Apply();objects.Add(t);return t;}
            try
            {
                var camera=Object("Terrain lighting validation camera").AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
                camera.orthographic=true;camera.orthographicSize=10;camera.nearClipPlane=.1f;camera.farClipPlane=100;
                camera.transform.position=new Vector3(0,24,-5);camera.transform.LookAt(Vector3.zero);
                var hdCamera=camera.gameObject.AddComponent<HDAdditionalCameraData>();hdCamera.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;
                hdCamera.backgroundColorHDR=Color.black;hdCamera.antialiasing=HDAdditionalCameraData.AntialiasingMode.None;
                var volume=Object("Terrain lighting validation volume").AddComponent<Volume>();volume.isGlobal=true;volume.priority=10000;
                var profile=ScriptableObject.CreateInstance<VolumeProfile>();objects.Add(profile);volume.sharedProfile=profile;
                profile.Add<Exposure>().mode.Override(ExposureMode.Fixed);profile.components.OfType<Exposure>().First().fixedExposure.Override(0);
                profile.Add<Tonemapping>().mode.Override(TonemappingMode.None);
                profile.Add<VisualEnvironment>().skyType.Override(0);profile.Add<Fog>().enabled.Override(false);
                var sun=Object("Terrain lighting validation sun").AddComponent<Light>();sun.type=LightType.Directional;sun.color=Color.white;sun.shadows=LightShadows.Soft;
                var hdLight=sun.gameObject.AddComponent<HDAdditionalLightData>();hdLight.SetIntensity(4,LightUnit.Lux);
                sun.transform.rotation=Quaternion.Euler(75,-35,0);
                var material=new Material(Resources.Load<Shader>("MassiveArmy/Terrain"));objects.Add(material);
                Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary").Apply(material);
                material.SetTexture("_LayersA",Mask(Color.clear));material.SetTexture("_LayersB",Mask(new Color(1,0,0,0)));material.SetTexture("_Splat",Mask(Color.clear));material.SetFloat("_WorldSize",16);
                var mesh=new Mesh();objects.Add(mesh);mesh.vertices=new[]{new Vector3(-8,0,-8),new Vector3(-8,0,8),new Vector3(8,0,8),new Vector3(8,0,-8)};
                mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();
                Require(mesh.tangents.Length==0,"Lighting fixture matches generated terrain without authored tangents");
                var terrain=Object("Terrain lighting validation mesh");terrain.AddComponent<MeshFilter>().sharedMesh=mesh;
                terrain.AddComponent<MeshRenderer>().sharedMaterial=material;
                var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGBHalf);target.Create();objects.Add(target);
                Texture2D Render(string name)
                {
                    var request=new RenderPipeline.StandardRequest{destination=target};
                    Require(RenderPipeline.SupportsRenderRequest(camera,request),"HDRP camera render requests supported");
                    RenderPipeline.SubmitRenderRequest(camera,request);RenderPipeline.SubmitRenderRequest(camera,request);
                    RenderTexture.active=target;var image=new Texture2D(256,256,TextureFormat.RGB24,false);objects.Add(image);
                    image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                    File.WriteAllBytes("TestResults/terrain-sun-"+name+".png",image.EncodeToPNG());return image;
                }
                Directory.CreateDirectory("TestResults");
                var high=Render("high");Require(high.GetPixels().Average(c=>c.grayscale)>.01f,"Scene directional light illuminates the actual HDRP terrain pass");
                Require(!high.GetPixels().Any(c=>c.r>.8f&&c.b>.8f&&c.g<.2f),"HDRP forward terrain renders without shader-error magenta");
                sun.transform.rotation=Quaternion.Euler(15,-35,0);var low=Render("low");report.rotationDifference=Difference(high,low);
                Require(report.rotationDifference>.01f,"Rotating the scene sun visibly changes terrain lighting");
                hdLight.SetIntensity(.2f,LightUnit.Lux);report.intensityDifference=Difference(low,Render("dim"));Require(report.intensityDifference>.01f,"Scene sun intensity affects terrain illumination");
                hdLight.SetIntensity(4,LightUnit.Lux);sun.color=Color.red;report.colourDifference=Difference(low,Render("red"));Require(report.colourDifference>.01f,"Scene sun colour affects terrain illumination");
                sun.color=Color.white;sun.enabled=false;Require(Difference(low,Render("off"))>.01f,"Disabling the scene sun removes its terrain contribution");sun.enabled=true;
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.hideFlags=HideFlags.HideAndDontSave;SceneManager.MoveGameObjectToScene(cube,scene);objects.Add(cube);
                cube.transform.position=new Vector3(0,2,0);cube.transform.localScale=new Vector3(2,4,2);
                var cubeMaterial=new Material(Shader.Find("HDRP/Lit"));objects.Add(cubeMaterial);cubeMaterial.SetColor("_BaseColor",Color.white);cube.GetComponent<MeshRenderer>().sharedMaterial=cubeMaterial;
                sun.transform.rotation=Quaternion.Euler(45,-35,0);var shadow=Render("shadow");sun.shadows=LightShadows.None;report.shadowDifference=Difference(shadow,Render("no-shadow"));
                Require(report.shadowDifference>.0002f,"Terrain receives the scene sun's shadow from a mesh occluder");
                // Reproduce the RTS camera distance, rather than only testing a nearby camera.
                camera.farClipPlane=3000;camera.transform.position=-camera.transform.forward*1400;
                var shadowSettings=profile.Add<HDShadowSettings>();shadowSettings.maxShadowDistance.Override(500);
                var rig=Object("Authored lighting rig fixture").AddComponent<BattleLightingRig>();rig.atmosphere=volume;rig.sun=sun;rig.Configure(camera,true);
                var shortRange=Render("rts-shadow-range-500");shadowSettings.maxShadowDistance.Override(2600);
                shadowSettings.cascadeShadowSplit0.Override(.45f);shadowSettings.cascadeShadowSplit1.Override(.62f);shadowSettings.cascadeShadowSplit2.Override(.8f);
                hdLight.SetShadowResolution(2048);hdLight.normalBias=.1f;
                cube.transform.localScale=new Vector3(4,8,4);cube.transform.position=new Vector3(0,4,0);
                shadowSettings.maxShadowDistance.Override(500);shortRange=Render("rts-shadow-range-500");shadowSettings.maxShadowDistance.Override(2600);
                var longRange=Render("rts-shadow-range-2600");
                Require(Difference(shortRange,longRange)>.0002f,"Authored shadow range reaches the battlefield from the 1400m RTS camera");
                rig.Configure(camera,false);var disabled=Render("rts-shadows-disabled");
                Require(Difference(longRange,disabled)>.0002f,"Runtime shadow switch removes rendered terrain shadows");
                Require(volume.sharedProfile==profile&&profile.TryGet<HDShadowSettings>(out var saved)&&saved.maxShadowDistance.value==2600,"Runtime shadow switch preserves the authored volume profile");
                rig.Configure(camera,true);Require(Difference(longRange,Render("rts-shadows-restored"))<.0002f,"Runtime shadow switch restores the same lighting");
                cube.SetActive(false);terrain.transform.localScale=Vector3.one*8;terrain.transform.position=new Vector3(35,0,20);
                camera.orthographicSize=60;camera.transform.position=terrain.transform.position-camera.transform.forward*1400;
                var model=Object("Real catalogue building shadow fixture");model.transform.position=terrain.transform.position;
                var filter=model.AddComponent<MeshFilter>();var renderer=model.AddComponent<MeshRenderer>();
                var art=Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue");
                foreach(string id in new[]{"command","factory","autocannon"}){
                    var building=art.Find(id);renderer.sharedMaterial=building.material;
                    foreach(bool far in new[]{false,true}){
                        filter.sharedMesh=far?building.far:building.near;
                        renderer.shadowCastingMode=ShadowCastingMode.On;var cast=Render(id+(far?"-far":"-near")+"-shadow");
                        renderer.shadowCastingMode=ShadowCastingMode.Off;var noCast=Render(id+(far?"-far":"-near")+"-no-shadow");
                        Require(Difference(cast,noCast)>.0001f,"Actual "+id+(far?" far":" near")+" shell casts at a nonzero world position from the RTS camera");
                    }
                }
                foreach(var definition in RtsCatalog.Load().buildings.Where(BuildingTurretGeometry.Articulated)){
                    var building=art.Find(definition.id);
                    foreach(var source in new[]{building.near,building.far}){
                        var posed=BuildingTurretGeometry.WithJoints(source);objects.Add(posed);var joints=posed.uv2;
                        Require(joints.Any(j=>j.x==1)&&joints.Any(j=>j.x==0),definition.id+" has separate fixed foundation and rotating weapon geometry in "+source.name);
                        var points=posed.vertices;Require(Enumerable.Range(0,points.Length).Where(i=>points[i].y<=6).All(i=>joints[i].x==0),definition.id+" pedestal remains fixed");
                    }
                }
                var turretMesh=BuildingTurretGeometry.WithJoints(art.Find("autocannon").near);objects.Add(turretMesh);
                filter.sharedMesh=turretMesh;renderer.sharedMaterial=art.Find("autocannon").material;renderer.shadowCastingMode=ShadowCastingMode.On;
                model.transform.rotation=Quaternion.Euler(0,90,0);camera.orthographicSize=12;
                using(var pose=new GraphicsBuffer(GraphicsBuffer.Target.Structured,GpuBattle.MaxBuildings,16)){
                    var aim=new Vector4[GpuBattle.MaxBuildings];pose.SetData(aim);var properties=new MaterialPropertyBlock();
                    properties.SetBuffer("_StructureAim",pose);properties.SetInt("_BuildingIndex",0);properties.SetFloat("_HasTurret",1);properties.SetFloat("_BuildingYaw",Mathf.PI*.5f);renderer.SetPropertyBlock(properties);
                    aim[0]=new Vector4(0,0,1,1);pose.SetData(aim);var north=Render("turret-world-north");
                    aim[0]=new Vector4(Mathf.PI*.5f,1,0,1);pose.SetData(aim);var east=Render("turret-world-east");
                    Require(Difference(north,east)>.0002f,"GPU pose rotates the rendered weapon independently of the placed building yaw");
                    var foundation=UnityEngine.Object.Instantiate(turretMesh);objects.Add(foundation);var joints=foundation.uv2;var triangles=foundation.triangles;var fixedTriangles=new List<int>();
                    for(int i=0;i<triangles.Length;i+=3)if(joints[triangles[i]].x==0&&joints[triangles[i+1]].x==0&&joints[triangles[i+2]].x==0){fixedTriangles.Add(triangles[i]);fixedTriangles.Add(triangles[i+1]);fixedTriangles.Add(triangles[i+2]);}
                    foundation.SetTriangles(fixedTriangles,0);filter.sharedMesh=foundation;
                    var fixedEast=Render("turret-foundation-east");aim[0]=new Vector4(0,0,1,1);pose.SetData(aim);var fixedNorth=Render("turret-foundation-north");
                    Require(Difference(fixedEast,fixedNorth)<.00001f,"Rendered foundation and its shadow remain identical as the weapon turns");
                }
                Require(!ShaderUtil.GetShaderMessages(material.shader).Any(m=>m.severity.ToString()=="Error"),"HDRP terrain shader compiles without errors");
            }
            catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{RenderTexture.active=previous;foreach(var obj in objects)if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);EditorSceneManager.ClosePreviewScene(scene);}
            report.checks=checks.Distinct().ToArray();File.WriteAllText("TestResults/terrain-scene-lighting-validation.json",JsonUtility.ToJson(report,true));Debug.Log("Terrain scene lighting: "+report.status);
        }
        static float Difference(Texture2D a,Texture2D b){var x=a.GetPixels();var y=b.GetPixels();float difference=0;for(int i=0;i<x.Length;i++)difference+=(Mathf.Abs(x[i].r-y[i].r)+Mathf.Abs(x[i].g-y[i].g)+Mathf.Abs(x[i].b-y[i].b))/3;return difference/x.Length;}
    }
}
