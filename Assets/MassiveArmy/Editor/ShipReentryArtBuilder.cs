using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;
namespace MassiveArmy.EditorTools {
    public static partial class CombatArtBuilder {
        const string ShipArt="Assets/MassiveArmy/Resources/MassiveArmy/ShipArt";
        [InitializeOnLoadMethod]static void InstallShipReentry(){EditorApplication.update-=PumpShipReentry;EditorApplication.update+=PumpShipReentry;}
        static void PumpShipReentry(){const string request="TestResults/Planetary/Reentry/build-request.txt";if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(request))return;File.Delete(request);try{BuildShipReentry();}catch(Exception e){Debug.LogException(e);File.WriteAllText("TestResults/Planetary/Reentry/build-error.txt",e.ToString());}}
        [MenuItem("Synthetic Front/Art/Build ship re-entry VFX Graph")]
        public static void BuildShipReentry(){
            Directory.CreateDirectory(ShipArt);vfx=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="Unity.VisualEffectGraph.Editor");
            var texture=new Texture2D(128,128,TextureFormat.RGBA32,true,true){name="Re-entry turbulent photon envelope",filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp};var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++){float u=(x+.5f)/64-1,w=(y+.5f)/64-1,r=u*u+w*w,n=Mathf.PerlinNoise(u*5+21,w*5+13);float alpha=Mathf.Clamp01(1-r)*Mathf.Clamp01((1-r+(n-.5f)*.5f)*3);pixels[x+y*128]=new Color(1,1,1,alpha);}
            texture.SetPixels(pixels);texture.Apply();string texturePath=ShipArt+"/ReentryPhotonTexture.asset";var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);if(existing!=null){EditorUtility.CopySerialized(texture,existing);UnityEngine.Object.DestroyImmediate(texture);texture=existing;}else AssetDatabase.CreateAsset(texture,texturePath);
            string path=ShipArt+"/ReentryPlasma.vfx";var asset=AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path)??(VisualEffectAsset)Call(T("VisualEffectAssetEditorUtility"),"CreateNewAsset",path);var resource=Call(T("VisualEffectObjectExtensions"),"GetResource",asset);var graph=Call(T("VisualEffectResourceExtensions"),"GetGraph",resource);
            foreach(var child in ((IEnumerable)Get(graph,"children")).Cast<object>().ToArray())Call(graph,"RemoveChild",child);
            var spawn=Node("VFXBasicSpawner");var init=Node("VFXBasicInitialize");var update=Node("VFXBasicUpdate");var output=Node("VFXPlanarPrimitiveOutput");int row=0;foreach(var context in new[]{spawn,init,update,output}){Add(graph,context);Field(context.GetType(),"m_UIPosition").SetValue(context,new Vector2(0,row++*450));}Call(init,"LinkFrom",spawn);Call(update,"LinkFrom",init);Call(output,"LinkFrom",update);
            var data=Call(init,"GetData");Setting(data,"capacity",2048u);Setting(data,"boundsMode","Manual");var space=data.GetType().GetProperty("space",Flags);space.SetValue(data,Enum.Parse(space.PropertyType,"Local"));var boxType=(Type)Get(Get(Slots(init)[0],"property"),"type");var box=Activator.CreateInstance(boxType);Field(boxType,"size").SetValue(box,new Vector3(320,160,400));Value(init,0,box);
            object Parameter(string name,Type type,object value){var p=Node("VFXParameter");Call(p,"Init",type);Setting(p,"m_ExposedName",name);Setting(p,"m_Exposed",true);Add(graph,p);var slot=((IEnumerable)Get(p,"outputSlots")).Cast<object>().First();SlotValue(slot,value);return slot;}
            var rate=Parameter("EmissionRate",typeof(float),0f);var intensity=Parameter("Intensity",typeof(float),0f);var heat=Parameter("Heat",typeof(float),0f);var direction=Parameter("FlowDirection",typeof(Vector3),Vector3.forward);var extents=Parameter("HalfExtents",typeof(Vector3),new Vector3(28,7,46));
            void Link(object parent,string name,object source){var target=Slots(parent).First(s=>((string)Get(Get(s,"property"),"name")).TrimStart('_')==name);Call(target,"Link",source);}
            var emission=Node("VFXSpawnerConstantRate");Add(spawn,emission);Call(Slots(emission)[0],"Link",rate);
            foreach(var pair in new[]{(init,"ReentryInitialize.hlsl"),(update,"ReentryUpdate.hlsl")}){var block=Node("Block.CustomHLSL");block.GetType().GetProperty("sourceCode",Flags).SetValue(block,File.ReadAllText(ShipArt+"/"+pair.Item2));Add(pair.Item1,block);Call(block,"ResyncSlots",true);Link(block,"FlowDirection",direction);Link(block,"HalfExtents",extents);Link(block,"Intensity",intensity);Link(block,"Heat",heat);}
            var orient=Node("Block.Orient");Setting(orient,"mode","AlongVelocity");Add(output,orient);Setting(output,"blendMode","Additive");Setting(output,"useBaseColorMap","ColorAndAlpha");NamedValue(output,"mainTexture",texture);
            Call(T("VisualEffectResourceExtensions"),"WriteAssetWithSubAssets",resource);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);AssetDatabase.SaveAssets();Directory.CreateDirectory("TestResults/Planetary/Reentry");int sources=(int)Call(resource,"GetShaderSourceCount");for(int i=0;i<sources;i++)File.WriteAllText("TestResults/Planetary/Reentry/GraphShader-"+i+".hlsl",(string)Call(resource,"GetShaderSource",i));File.WriteAllText("TestResults/Planetary/Reentry/build.txt","Built player-ship local-space GPU plasma graph; capacity 2048; "+sources+" shader sources; custom windward envelope, advection and photon emission.");Debug.Log("Ship re-entry VFX Graph built");
        }
    }
}
