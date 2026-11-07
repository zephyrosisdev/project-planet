using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using Object=UnityEngine.Object;
namespace MassiveArmy.EditorTools {
    public static class ShipReentryValidation {
        static bool returnToEdit,running;
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();public int ionPixels,hotPixels;public int aliveParticles;}
        [InitializeOnLoadMethod]static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||running)return;const string play="TestResults/Planetary/Reentry/gpu-play-request.txt";if(File.Exists(play)){File.Delete(play);if(!EditorApplication.isPlaying){SessionState.SetBool("ShipReentryGpuCheck",true);EditorApplication.isPlaying=true;return;}Run();return;}if(SessionState.GetBool("ShipReentryGpuCheck",false)&&EditorApplication.isPlaying){SessionState.SetBool("ShipReentryGpuCheck",false);returnToEdit=true;Run();return;}const string request="TestResults/Planetary/Reentry/validate-request.txt";if(!File.Exists(request))return;File.Delete(request);Run();}
        [MenuItem("Synthetic Front/Diagnostics/Validate ship re-entry heat and GPU plasma")]
        public static void Run(){
            if(running)return;if(!EditorApplication.isPlaying){SessionState.SetBool("ShipReentryGpuCheck",true);EditorApplication.isPlaying=true;return;}
            running=true;
            var report=new Report();Scene scene=default;Camera camera=null;VisualEffect effect=null;RenderTexture target=null;Material hull=null;VolumeProfile profile=null;int frames=0,stage=0;Color32[] control=null;
            void Check(bool valid,string label){if(!valid)throw new InvalidOperationException(label);report.checks.Add(label);}
            void Finish(){EditorApplication.update-=Tick;running=false;if(scene.IsValid()&&scene.isLoaded){if(EditorApplication.isPlaying)SceneManager.UnloadSceneAsync(scene);else EditorSceneManager.CloseScene(scene,true);}if(target!=null){target.Release();Object.DestroyImmediate(target);}if(hull!=null)Object.DestroyImmediate(hull);if(profile!=null)Object.DestroyImmediate(profile);Directory.CreateDirectory("TestResults/Planetary/Reentry");File.WriteAllText("TestResults/Planetary/Reentry/validation.json",JsonUtility.ToJson(report,true));Debug.Log("Ship re-entry validation "+report.status+": "+report.checks.Count+" checks");if(returnToEdit){returnToEdit=false;EditorApplication.isPlaying=false;}}
            void Fail(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);Finish();}
            Color32[] Capture(string name){var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var data=image.GetPixels32();File.WriteAllBytes("TestResults/Planetary/Reentry/"+name+".png",image.EncodeToPNG());return data;}finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}}
            int Difference(Color32[] image){int count=0;for(int i=0;i<image.Length;i++)if(Math.Abs(image[i].r-control[i].r)+Math.Abs(image[i].g-control[i].g)+Math.Abs(image[i].b-control[i].b)>30)count++;return count;}
            void Tick(){try{
                if(effect.gameObject.activeSelf)effect.Simulate(1f/30,1);
                RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
                if(++frames<35)return;frames=0;
                if(stage==0){control=Capture("NoPlasma");effect.gameObject.SetActive(true);effect.SetFloat("EmissionRate",1500);effect.SetFloat("Intensity",.8f);effect.SetFloat("Heat",.12f);effect.SetVector3("FlowDirection",Vector3.forward);effect.SetVector3("HalfExtents",new Vector3(28,7,46));effect.Reinit();effect.Play();stage=1;return;}
                if(stage==1){var data=Capture("Ionization");report.ionPixels=Difference(data);report.aliveParticles=effect.aliveParticleCount;var names=new List<string>();effect.GetSpawnSystemNames(names);foreach(var name in names){var state=effect.GetSpawnSystemInfo(name);Debug.Log("Reentry spawner "+name+" playing "+state.playing+" spawn "+state.spawnCount+" rate "+effect.GetFloat("EmissionRate"));}Debug.Log("Reentry GPU particles "+report.aliveParticles+" culled "+effect.culled);Check(report.ionPixels>300,"The actual GPU VFX Graph produces a visible ionized windward envelope");effect.SetFloat("Heat",.95f);stage=2;return;}
                var hot=Capture("HotReentry");report.hotPixels=Difference(hot);report.aliveParticles=effect.aliveParticleCount;Check(report.hotPixels>300,"High heat produces visible hot plasma around the craft");Check(report.aliveParticles<=2048,"The effect stays within its fixed 2048 GPU particle budget");Finish();
            }catch(Exception e){Fail(e);}}
            try{
                Directory.CreateDirectory("TestResults/Planetary/Reentry");var state=new ShipReentryThermalState();for(int i=0;i<300;i++)state.Advance(.05f,false,1,80000,30000,5000);Check(state.heat==0&&state.ionization==0,"Airless bodies do not generate atmospheric heat or plasma");
                for(int i=0;i<300;i++)state.Advance(.05f,true,1,80000,90000,5000);Check(state.density==0&&state.heat==0,"Flying above the atmospheric boundary does not heat the ship");
                for(int i=0;i<1200;i++)state.Advance(.05f,true,1,80000,0,360);Check(state.heat<.03f&&state.ionization==0,"Ordinary assisted flight speed does not create a re-entry fireball");
                state=new ShipReentryThermalState();state.Advance(.05f,true,1,80000,64000,5000);Check(state.ionization>0&&state.heat<.1f,"Fast upper-atmosphere entry starts ionization before substantial stored heat");
                for(int i=0;i<200;i++)state.Advance(.05f,true,1,80000,12000,5000);Check(state.heat>.75f,"Fast dense-air entry accumulates substantial heat over time");float peak=state.heat;for(int i=0;i<300;i++)state.Advance(.05f,false,0,80000,90000,5000);Check(state.heat>0&&state.heat<peak&&state.ionization==0,"Stored heat cools gradually after leaving the atmosphere and plasma stops");
                var asset=AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/MassiveArmy/Resources/MassiveArmy/ShipArt/ReentryPlasma.vfx");Check(asset!=null,"An authored, editable re-entry VFX Graph asset exists");
                var previousScene=SceneManager.GetActiveScene();scene=EditorApplication.isPlaying?SceneManager.CreateScene("Ship reentry GPU fixture"):EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(previousScene);var cameraHost=new GameObject("Isolated re-entry preview camera");SceneManager.MoveGameObjectToScene(cameraHost,scene);camera=cameraHost.AddComponent<Camera>();camera.enabled=false;camera.targetDisplay=7;camera.cameraType=CameraType.Game;camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.transform.position=new Vector3(85,30,115);camera.transform.LookAt(new Vector3(0,0,-3));camera.fieldOfView=42;camera.nearClipPlane=.1f;camera.farClipPlane=1000;var hd=cameraHost.AddComponent<HDAdditionalCameraData>();hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=new Color(.001f,.002f,.004f);hd.volumeLayerMask=1<<29;var volumeHost=new GameObject("Isolated preview exposure");volumeHost.layer=29;SceneManager.MoveGameObjectToScene(volumeHost,scene);var volume=volumeHost.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10000;profile=ScriptableObject.CreateInstance<VolumeProfile>();var exposure=profile.Add<Exposure>();exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(0);volume.sharedProfile=profile;
                hull=new Material(Shader.Find("HDRP/Unlit"));hull.SetColor("_UnlitColor",new Color(.055f,.075f,.1f));
                foreach(var piece in new[]{(new Vector3(0,1,-14),new Vector3(52,11,49)),(new Vector3(0,-1,24),new Vector3(19,7,29))}){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(go,scene);go.transform.position=piece.Item1;go.transform.localScale=piece.Item2;go.GetComponent<Renderer>().sharedMaterial=hull;}
                var host=new GameObject("Isolated bounded GPU plasma");SceneManager.MoveGameObjectToScene(host,scene);effect=host.AddComponent<VisualEffect>();effect.visualEffectAsset=asset;Check(effect.HasFloat("Heat")&&effect.HasFloat("Intensity")&&effect.HasFloat("EmissionRate")&&effect.HasVector3("FlowDirection")&&effect.HasVector3("HalfExtents"),"Runtime heat, flow and bounds are real exposed VFX Graph properties");host.SetActive(false);
                target=new RenderTexture(1280,800,24,RenderTextureFormat.ARGBHalf);target.Create();camera.targetTexture=target;Debug.Log("Reentry preview pipeline "+GraphicsSettings.currentRenderPipeline+" device "+SystemInfo.graphicsDeviceType+" shader "+hull.shader.isSupported+" camera mask "+camera.overrideSceneCullingMask);EditorApplication.update+=Tick;
            }catch(Exception e){Fail(e);}
        }
    }
}
