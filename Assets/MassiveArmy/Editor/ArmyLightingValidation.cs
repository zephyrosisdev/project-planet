using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace MassiveArmy.EditorTools
{
    public static class ArmyLightingValidation
    {
        [Serializable] sealed class Report {public string result;public int checks;public string[] failures;}
        public static void Run()
        {
            var failures=new List<string>();int checks=0;void Check(bool valid,string label){checks++;if(!valid)failures.Add(label);}
            var objects=new List<UnityEngine.Object>();var scene=EditorSceneManager.NewPreviewScene();var previous=RenderTexture.active;
            GameObject Object(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);objects.Add(go);return go;}
            using(var units=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,UnitState.Stride))
            using(var traits=new GraphicsBuffer(GraphicsBuffer.Target.Structured,32,UnitTraits.Stride))
            using(var visible=new GraphicsBuffer(GraphicsBuffer.Target.Structured,3,4))
            using(var projectiles=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,ProjectileState.Stride))
            using(var scenery=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,32))
            using(var args=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,1,GraphicsBuffer.IndirectDrawIndexedArgs.size))
            using(var art=new ArtUnitRenderer()){
                try{
                    var camera=Object("Shared army light validation").AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.orthographicSize=5;camera.nearClipPlane=.1f;camera.farClipPlane=100;
                    camera.transform.position=new Vector3(0,12,-16);camera.transform.LookAt(new Vector3(0,1,0));
                    var hd=camera.gameObject.AddComponent<HDAdditionalCameraData>();hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=Color.black;hd.antialiasing=HDAdditionalCameraData.AntialiasingMode.None;
                    var profile=ScriptableObject.CreateInstance<VolumeProfile>();objects.Add(profile);profile.Add<Exposure>().mode.Override(ExposureMode.Fixed);profile.TryGet<Exposure>(out var exposure);exposure.fixedExposure.Override(12.5f);profile.Add<Tonemapping>().mode.Override(TonemappingMode.None);profile.Add<VisualEnvironment>().skyType.Override(0);profile.Add<Fog>().enabled.Override(false);
                    var volume=Object("Validation volume").AddComponent<Volume>();volume.isGlobal=true;volume.priority=10000;volume.sharedProfile=profile;
                    var light=Object("Validation sun").AddComponent<Light>();light.type=LightType.Directional;light.color=Color.white;light.intensity=50000;light.transform.rotation=Quaternion.Euler(48,-35,0);
                    var settings=ScriptableObject.CreateInstance<BattleVisualSettings>();objects.Add(settings);
                    var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGBHalf);objects.Add(target);target.Create();
                    var read=new Texture2D(256,256,TextureFormat.RGBAFloat,false,true);objects.Add(read);
                    var terrain=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);objects.Add(terrain);terrain.SetPixel(0,0,Color.clear);terrain.Apply();
                    var data=new UnitTraits[32];for(int i=0;i<data.Length;i++)data[i].scale=Vector4.one;traits.SetData(data);visible.SetData(new uint[3]);projectiles.SetData(new ProjectileState[1]);
                    var quad=ArmyMeshes.Quad();objects.Add(quad);var legacy=ArmyMeshes.Tank(true);objects.Add(legacy);var atlas=ArmyMeshes.Atlas();objects.Add(atlas);
                    float Render(Material material,Mesh mesh,bool indirect){
                        settings.ApplyArmyLighting(light,profile);
                        if(indirect){material.SetMatrix("_BattleVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);args.SetData(new[]{new GraphicsBuffer.IndirectDrawIndexedArgs{indexCountPerInstance=mesh.GetIndexCount(0),instanceCount=1}});}
                        for(int frame=0;frame<2;frame++){
                            var rp=new RenderParams(material){camera=camera,worldBounds=new Bounds(Vector3.zero,Vector3.one*100),shadowCastingMode=ShadowCastingMode.Off};
                            if(indirect)Graphics.RenderMeshIndirect(rp,mesh,args);else Graphics.RenderMesh(rp,mesh,0,Matrix4x4.identity);
                            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
                        }
                        RenderTexture.active=target;read.ReadPixels(new Rect(0,0,256,256),0,0);read.Apply();float sum=0;foreach(var c in read.GetPixels())sum+=c.grayscale;return sum/(256*256);
                    }
                    void Compare(Material material,Mesh mesh,bool indirect,string name){light.enabled=true;light.intensity=50000;exposure.compensation.Override(0);exposure.fixedExposure.Override(12.5f);float high=Render(material,mesh,indirect);light.intensity=500;float low=Render(material,mesh,indirect);light.intensity=50000;exposure.fixedExposure.Override(16.5f);float dark=Render(material,mesh,indirect);exposure.fixedExposure.Override(12.5f);exposure.compensation.Override(-4);float compensated=Render(material,mesh,indirect);light.enabled=false;float off=Render(material,mesh,indirect);
                        // HDRP adds a small black/dither floor even to an empty black frame.
                        float contribution=high-off;
                        Check(contribution>.00001f,name+" renders visible geometry");Check(low-off<contribution*.12f,name+" follows scene sun intensity");Check(dark-off<contribution*.12f,name+" follows camera exposure");Check(off<.0001f&&Shader.GetGlobalVector("_ArmyDirectLight").sqrMagnitude==0&&Shader.GetGlobalFloat("_ArmyAmbientLight")==0,name+" is dark with sun and sky disabled");
                        Check(compensated-off<contribution*.12f,name+" follows exposure compensation");
                        File.AppendAllText("TestResults/CombatSurface/lighting-values.txt",$"{name}: high={high}, low={low}, exposed={dark}, off={off}\n");}
                    Directory.CreateDirectory("TestResults/CombatSurface");File.WriteAllText("TestResults/CombatSurface/lighting-values.txt","");
                    foreach(bool authored in new[]{false,true})for(int mode=0;mode<3;mode++)foreach(int type in authored?new[]{0,4,5,31}:new[]{0}){
                        var material=new Material(Resources.Load<Shader>(authored?"MassiveArmy/ArtUnits":"MassiveArmy/Units")){enableInstancing=true};objects.Add(material);if(authored)art.Bind(material,mode);
                        units.SetData(new[]{new UnitState{health=1000,flags=1,packed=(uint)((type<<8)|(type==31?2:0)),target=uint.MaxValue,group=uint.MaxValue}});
                        material.SetBuffer("_Units",units);material.SetBuffer("_Traits",traits);material.SetBuffer("_Visible",visible);material.SetBuffer("_VisibleProjectiles",visible);material.SetBuffer("_Projectiles",projectiles);material.SetTexture("_Terrain",terrain);material.SetTexture("_WaterMap",terrain);material.SetTexture("_Atlas",atlas);
                        material.SetInt("_Mode",mode);material.SetInt("_Capacity",1);material.SetFloat("_WorldSize",64);material.SetVector("_CameraRight",camera.transform.right);material.SetVector("_CameraUp",camera.transform.up);material.SetVector("_CameraPosition",camera.transform.position);material.SetFloat("_RenderInterpolation",1);
                        Compare(material,mode==2?quad:authored?(mode==0?art.NearMesh:art.FarMesh):legacy,true,(authored?"Catalogue":"Legacy")+" type "+type+" LOD "+mode);
                    }
                    var catalogue=Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue");var building=catalogue.Find("autocannon");Compare(building.material,building.near,false,"Catalogue building near");Compare(building.material,building.far,false,"Catalogue building far");
                    var fallback=new Material(Resources.Load<Shader>("MassiveArmy/Structure"));objects.Add(fallback);fallback.SetColor("_Tint",Color.gray);Compare(fallback,legacy,false,"Fallback structure");
                    var sceneryMaterial=new Material(Resources.Load<Shader>("MassiveArmy/Scenery")){enableInstancing=true};objects.Add(sceneryMaterial);scenery.SetData(new[]{new SceneryInstance{positionScale=new Vector4(0,0,0,1),tintYaw=new Vector4(.3f,.5f,.1f,0)}});sceneryMaterial.SetBuffer("_Instances",scenery);sceneryMaterial.SetBuffer("_Visible",visible);sceneryMaterial.SetFloat("_WorldSize",64);Compare(sceneryMaterial,legacy,true,"Landscape scenery");
                }catch(Exception e){failures.Add(e.ToString());}
                finally{RenderTexture.active=previous;EditorSceneManager.ClosePreviewScene(scene);foreach(var obj in objects)if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);}
            }
            Directory.CreateDirectory("TestResults/CombatSurface");File.WriteAllText("TestResults/CombatSurface/lighting-validation.json",JsonUtility.ToJson(new Report{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray()},true));
            var rig=UnityEngine.Object.FindAnyObjectByType<BattleLightingRig>();if(rig!=null&&rig.sun!=null)Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings").ApplyArmyLighting(rig.sun,rig.atmosphere.sharedProfile);
        }
    }
}
