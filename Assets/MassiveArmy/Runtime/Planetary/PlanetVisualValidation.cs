using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy.Planetary
{
    [DefaultExecutionOrder(250)]
    public sealed class PlanetVisualValidation : MonoBehaviour
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();public float sunRotation,intensity,colour,shadows,normals,patchiness,warp,heightBlend,transitionWidth,sunLux,ambient,sunCosine;public int directionalLights;public Vector4 armyDirect;public uint[] drawArguments;public float materialRadius;public string keywords;public Matrix4x4 projection;}
        readonly Report report=new Report();PlanetDevelopment world;RenderTexture target;string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-visual-validation")>=0)new GameObject("Planet visual validation").AddComponent<PlanetVisualValidation>();}
        void Check(bool valid,string label){if(!valid)throw new InvalidOperationException(label);report.checks.Add(label);}
        IEnumerator Start(){var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-planet-output");output=index>=0?args[index+1]:Application.persistentDataPath;Directory.CreateDirectory(output);
            yield return null;world=FindAnyObjectByType<PlanetDevelopment>();world.Paused=true;world.Rig.InputEnabled=false;world.View.GetComponent<HDAdditionalCameraData>().antialiasing=HDAdditionalCameraData.AntialiasingMode.None;
            target=new RenderTexture(1024,1024,24,RenderTextureFormat.ARGBHalf);target.Create();world.View.enabled=false;world.View.targetTexture=target;
            // Allow the streamed patches, HDRP sky lighting and shadow atlas to settle.
            for(int i=0;i<12;i++)yield return new WaitForEndOfFrame();
            var run=Run();while(true){bool more;try{more=run.MoveNext();}catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);break;}if(!more)break;yield return run.Current;}
            File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);
        }
        Color[] Capture(string name){var request=new RenderPipeline.StandardRequest{destination=target};RenderPipeline.SubmitRenderRequest(world.View,request);RenderPipeline.SubmitRenderRequest(world.View,request);return Read(target,name);}
        Color[] Read(RenderTexture source,string name){var previous=RenderTexture.active;RenderTexture.active=source;var image=new Texture2D(source.width,source.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();RenderTexture.active=previous;var pixels=image.GetPixels();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);return pixels;}
        static float Difference(Color[] a,Color[] b){double sum=0;for(int i=0;i<a.Length;i++)sum+=Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b);return (float)(sum/(a.Length*3));}
        IEnumerator Run(){
            Check(world.definition.radius==120000&&world.StreamedTerrain!=null,"Visual fixture uses the actual streamed 120 km radius world");
            var sun=world.LightingForValidation.Sun;var hd=sun.GetComponent<HDAdditionalLightData>();var rotation=sun.transform.rotation;float lux=sun.intensity;var colour=sun.color;var shadows=sun.shadows;
            report.sunLux=lux;report.ambient=Shader.GetGlobalFloat("_ArmyAmbientLight");report.armyDirect=Shader.GetGlobalVector("_ArmyDirectLight");report.sunCosine=Vector3.Dot(world.Rig.FocusDirection,-sun.transform.forward);report.directionalLights=Shader.GetGlobalInt("_DirectionalLightCount");
            report.drawArguments=new uint[4];world.StreamedTerrain.DrawArguments.GetData(report.drawArguments);report.materialRadius=world.TerrainMaterial.GetFloat("_PlanetRadius");report.keywords=string.Join(",",world.TerrainMaterial.shaderKeywords);report.projection=world.TerrainMaterial.GetMatrix("_PlanetVP");
            world.TerrainMaterial.SetFloat("_PlanetLightingDebug",1);yield return new WaitForEndOfFrame();Capture("terrain-coverage-diagnostic");world.TerrainMaterial.SetFloat("_PlanetLightingDebug",0);yield return new WaitForEndOfFrame();
            var original=Capture("sun-and-blended-ground");ValidateOcclusion();double brightness=0;foreach(var pixel in original)brightness+=pixel.grayscale;Check(brightness/original.Length>.03,"Scene sun visibly illuminates the planet terrain");
            sun.transform.rotation=Quaternion.AngleAxis(120,world.Rig.FocusDirection)*rotation*Quaternion.Euler(90,0,0);yield return new WaitForEndOfFrame();report.sunRotation=Difference(original,Capture("rotated-sun"));Check(report.sunRotation>.01,"Rotating the scene directional light changes terrain illumination");sun.transform.rotation=rotation;
            hd.SetIntensity(lux*.02f,LightUnit.Lux);yield return new WaitForEndOfFrame();report.intensity=Difference(original,Capture("dim-sun"));Check(report.intensity>.01,"Directional light intensity reaches the planet's HDRP material");hd.SetIntensity(lux,LightUnit.Lux);
            sun.color=Color.red;yield return new WaitForEndOfFrame();report.colour=Difference(original,Capture("red-sun"));Check(report.colour>.01,"Directional light colour reaches the planet's HDRP material");sun.color=colour;
            sun.shadows=LightShadows.None;yield return new WaitForEndOfFrame();report.shadows=Difference(original,Capture("no-building-shadows"));Check(report.shadows>.00005,"Radial building meshes cast scene-sun shadows onto the planetary terrain");sun.shadows=shadows;
            var material=world.TerrainMaterial;float normal=material.GetFloat("_NormalStrength");material.SetFloat("_NormalStrength",0);yield return new WaitForEndOfFrame();report.normals=Difference(original,Capture("without-normal-maps"));Check(report.normals>.0001,"Existing terrain normal maps visibly affect scene lighting");material.SetFloat("_NormalStrength",normal);
            float heightBlend=material.GetFloat("_HeightBlendStrength");material.SetFloat("_HeightBlendStrength",0);yield return new WaitForEndOfFrame();report.heightBlend=Difference(original,Capture("without-height-blending"));Check(report.heightBlend>.0001,"Existing texture-height competition remains active between terrain layers");material.SetFloat("_HeightBlendStrength",heightBlend);
            var blend=material.GetVector("_BiomeBlend");var pure=blend;pure.w=0;material.SetVector("_BiomeBlend",pure);yield return new WaitForEndOfFrame();report.patchiness=Difference(original,Capture("without-patch-competition"));Check(report.patchiness>.001,"Saved patch-strength control restores stochastic competition between related terrain layers");
            material.SetVector("_BiomeBlend",blend);
            var home=world.Rig.FocusDirection;var surface=new PlanetSurface(world.definition);float closest=float.MaxValue;Vector3 transition=home;
            const int samples=32;for(int face=0;face<6;face++)for(int y=0;y<samples;y++)for(int x=0;x<samples;x++){
                var d=CubeSphere.Direction(new PlanetNavCoordinate(world.definition.bodyId,(CubeFace)face,x,y),samples);var climate=PlanetClimate.Sample(d,(world.definition.terrainSeed%1009)*.017f);
                float error=Mathf.Abs(climate.y-.60f);if(climate.x>.4f&&surface.SampleHeight(d)>world.definition.oceanHeight+4&&error<closest){closest=error;transition=d;}
            }
            world.Rig.SetFocusDirection(transition);sun.transform.rotation=Quaternion.FromToRotation(home,transition)*rotation;for(int i=0;i<6;i++)yield return new WaitForEndOfFrame();var wide=Capture("wide-biome-transition");pure=blend;pure.y=0;material.SetVector("_BiomeBlend",pure);yield return new WaitForEndOfFrame();report.warp=Difference(wide,Capture("without-boundary-warp"));Check(report.warp>.001,"Saved world-metre boundary-warp control affects an actual biome transition");pure=blend;pure.x=0;material.SetVector("_BiomeBlend",pure);yield return new WaitForEndOfFrame();report.transitionWidth=Difference(wide,Capture("narrow-biome-transition"));Check(report.transitionWidth>.0001,"Original transition-width setting changes the world-metre biome boundary blend");material.SetVector("_BiomeBlend",blend);world.Rig.SetFocusDirection(home);sun.transform.rotation=rotation;
        }
        void ValidateOcclusion(){
            var shader=Resources.Load<Shader>("MassiveArmy/PlanetScaledBody");var material=new Material(shader);var rt=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);rt.Create();
            material.SetFloat("_ArmyAmbientLight",1);material.SetVector("_ArmyDirectLight",Vector4.zero);material.SetFloat("_PlanetHeight",0);var previous=RenderTexture.active;
            try{
                foreach(var view in new[]{Quaternion.identity,Quaternion.Euler(70,130,20),Quaternion.Euler(-80,-45,0)})foreach(var offset in new[]{Vector3.zero,new Vector3(2048,-4096,1024)}){
                    var vp=GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60,1,1,2000000),true)*Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(offset,view,Vector3.one).inverse;
                    material.SetMatrix("_PlanetVP",vp);material.SetVector("_CameraPosition",offset);
                    Color[] Render(Vector3 remote,float radius,bool foreground,string name){Graphics.SetRenderTarget(rt);GL.Clear(true,true,Color.black);
                        void Draw(Vector3 physical,float physicalRadius,Vector3 scaled,float scaledRadius,Color tint,Vector4 occluder){material.SetVector("_ScaledCentre",offset+view*scaled);material.SetFloat("_ScaledRadius",scaledRadius);var actual=view*physical;material.SetVector("_PhysicalBody",new Vector4(actual.x,actual.y,actual.z,physicalRadius));material.SetVector("_ForegroundSphere",occluder);material.SetColor("_BodyColour",tint);material.SetPass(0);Graphics.DrawProceduralNow(MeshTopology.Triangles,36*16*16,1);}
                        var main=new Vector3(0,0,300000);Draw(main,120000,main,120000,Color.green,Vector4.zero);var mainRelative=view*main;float factor=Mathf.Min(1,10000/remote.magnitude);
                        Draw(remote,radius,remote*factor,radius*factor,new Color(1,0,1),foreground?new Vector4(mainRelative.x,mainRelative.y,mainRelative.z,120000):Vector4.zero);return Read(rt,name);
                    }
                    var allBehind=Render(new Vector3(0,0,40000000),10000000,true,"occluded-"+report.checks.Count);int green=0;foreach(var p in allBehind)if(p.g>.1f&&p.r<.05f&&p.b<.05f)green++;Check(green>100,"Occlusion fixture renders a visible foreground sphere before checking masking");int purple=0;foreach(var p in allBehind)if(p.r>.1f&&p.b>.1f&&p.g<.05f)purple++;Check(purple==0,"A physically distant body remains behind the main planet through orbit rotation and floating-origin offsets");
                    var partial=new Vector3(Mathf.Sin(25*Mathf.Deg2Rad),0,Mathf.Cos(25*Mathf.Deg2Rad))*40000000;var reference=Render(partial,10000000,false,"unmasked-limb-"+report.checks.Count);var masked=Render(partial,10000000,true,"masked-limb-"+report.checks.Count);int before=0,after=0;for(int i=0;i<reference.Length;i++){if(reference[i].r>.1f&&reference[i].b>.1f&&reference[i].g<.05f)before++;if(masked[i].r>.1f&&masked[i].b>.1f&&masked[i].g<.05f)after++;}Check(after>50&&after<before*.9f,"Physical occlusion clips only the overlapping part of the distant body's limb");
                    var front=Render(new Vector3(0,0,150000),10000,true,"foreground-body-"+report.checks.Count);purple=0;foreach(var p in front)if(p.r>.1f&&p.b>.1f&&p.g<.05f)purple++;Check(purple>30,"A physically nearer celestial body is correctly allowed in front");
                }
            }finally{RenderTexture.active=previous;Destroy(material);rt.Release();Destroy(rt);}
        }
        void OnDestroy(){if(target!=null){target.Release();Destroy(target);}}
    }
}
