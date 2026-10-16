using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace MassiveArmy.EditorTools
{
    public static class CombatArtBuilder
    {
        const string Root="Assets/MassiveArmy/Resources/MassiveArmy/CombatArt";
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        static Assembly vfx;
        static Type T(string name)=>vfx.GetType((name=="VisualEffectAssetEditorUtility"?"UnityEditor.":"UnityEditor.VFX.")+name,true);
        static object Get(object o,string name){var property=o.GetType().GetProperty(name,Flags);return property!=null?property.GetValue(o):Field(o.GetType(),name).GetValue(o);}
        static object Call(object o,string name,params object[] args)
        {
            Type type=o as Type??o.GetType();
            foreach(var method in type.GetMethods(Flags).Where(m=>m.Name==name&&!m.IsGenericMethod)){
                var parameters=method.GetParameters();if(parameters.Length<args.Length||parameters.Skip(args.Length).Any(p=>!p.IsOptional))continue;
                if(parameters.Take(args.Length).Where((p,i)=>args[i]!=null&&!p.ParameterType.IsInstanceOfType(args[i])).Any())continue;
                var values=parameters.Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray();return method.Invoke(o is Type?null:o,values);
            }throw new MissingMethodException(type.FullName,name);
        }
        static FieldInfo Field(Type t,string name){for(;t!=null;t=t.BaseType){var f=t.GetField(name,Flags|BindingFlags.DeclaredOnly);if(f!=null)return f;}throw new MissingFieldException(name);}
        static void Setting(object o,string name,object value){var field=Field(o.GetType(),name);if(value is string text&&field.FieldType.IsEnum)value=Enum.Parse(field.FieldType,text);Call(o,"SetSettingValue",name,value);}
        static object Node(string name)=>ScriptableObject.CreateInstance(T(name));
        static void Add(object parent,object child)=>Call(parent,"AddChild",child);
        static object[] Slots(object o)=>((IEnumerable)Get(o,"inputSlots")).Cast<object>().ToArray();
        static void SlotValue(object slot,object value)
        {
            Type type=(Type)Get(Get(slot,"property"),"type");
            // Space-aware VFX vectors are wrapper structs, not UnityEngine.Vector3.
            // Serializing a bare Vector3 into one silently deserializes back to zero.
            if(value is Vector3 vector&&type!=typeof(Vector3)){var wrapped=Activator.CreateInstance(type);type.GetFields(Flags).First(f=>!f.IsStatic&&f.FieldType==typeof(Vector3)).SetValue(wrapped,vector);value=wrapped;}
            slot.GetType().GetProperty("value",Flags).SetValue(slot,value);
        }
        static void Value(object o,int index,object value)=>SlotValue(Slots(o)[index],value);
        static void NamedValue(object o,string name,object value)=>SlotValue(Slots(o).First(s=>(string)Get(Get(s,"property"),"name")==name),value);
        static object Attribute(object parent,string name,bool inherited=false)
        {
            var block=Node("Block.SetAttribute");Setting(block,"attribute",name);if(inherited)Setting(block,"Source","Source");Add(parent,block);return block;
        }
        [MenuItem("Massive Army/Art/Build Combat Rocket and VFX Graphs")]
        public static void Build()
        {
            Directory.CreateDirectory(Root);AssetDatabase.Refresh();BuildRocket();BuildParticleTexture("CombatDustTexture",false);BuildParticleTexture("CombatSparkTexture",true);
            vfx=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="Unity.VisualEffectGraph.Editor");
            BuildGraph("CombatSparks",true);BuildGraph("CombatBurst",false);BuildNuclear();AssetDatabase.SaveAssets();
            Directory.CreateDirectory("TestResults/CombatPresentation");File.WriteAllText("TestResults/CombatPresentation/art-build.txt","Rocket, streak sparks and flame/dust VFX Graph assets built");
        }
        static void BuildGraph(string name,bool sparks)
        {
            string path=Root+"/"+name+".vfx";
            var asset=AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path)??(VisualEffectAsset)Call(T("VisualEffectAssetEditorUtility"),"CreateNewAsset",path);
            var resource=Call(T("VisualEffectObjectExtensions"),"GetResource",asset);var graph=Call(T("VisualEffectResourceExtensions"),"GetGraph",resource);
            foreach(var child in ((IEnumerable)Get(graph,"children")).Cast<object>().ToArray())Call(graph,"RemoveChild",child);
            var spawn=Node("VFXBasicSpawner");var init=Node("VFXBasicInitialize");var update=Node("VFXBasicUpdate");var output=Node("VFXPlanarPrimitiveOutput");
            int row=0;foreach(var context in new[]{spawn,init,update,output}){Add(graph,context);Field(context.GetType(),"m_UIPosition").SetValue(context,new Vector2(0,row++*450));}
            Call(init,"LinkFrom",spawn);Call(update,"LinkFrom",init);Call(output,"LinkFrom",update);
            var data=Call(init,"GetData");Setting(data,"capacity",128u);Setting(data,"boundsMode","Manual");
            var space=data.GetType().GetProperty("space",Flags);space.SetValue(data,Enum.Parse(space.PropertyType,"Local"));
            var boxType=((Type)Get(Get(Slots(init)[0],"property"),"type"));var box=Activator.CreateInstance(boxType);Field(boxType,"size").SetValue(box,new Vector3(256,256,256));Value(init,0,box);
            var burst=Node("VFXSpawnerBurst");Add(spawn,burst);Value(burst,0,sparks?32f:24f);
            foreach(string attribute in new[]{"position","color","size","lifetime","velocity"})Attribute(init,attribute,true);
            var random=Attribute(init,"velocity");Setting(random,"Composition","Add");Setting(random,"Random","PerComponent");Value(random,0,sparks?new Vector3(-18,2,-18):new Vector3(-5,0,-5));Value(random,1,sparks?new Vector3(18,25,18):new Vector3(5,9,5));
            if(sparks){Value(Attribute(init,"scaleX"),0,.12f);Value(Attribute(init,"scaleY"),0,2.5f);}
            Add(update,Node("Block.Gravity"));
            var fade=Node("Block.AttributeFromCurve");Setting(fade,"attribute","alpha");Setting(fade,"Mode","Uniform");Add(update,fade);Value(fade,0,AnimationCurve.Linear(0,1,1,0));
            var orient=Node("Block.Orient");Setting(orient,"mode",sparks?"AlongVelocity":"FaceCameraPlane");Add(output,orient);
            Setting(output,"blendMode","Additive");
            Setting(output,"useBaseColorMap","ColorAndAlpha");
            NamedValue(output,"mainTexture",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/"+(sparks?"CombatSparkTexture":"CombatDustTexture")+".asset"));
            if(!sparks)BuildDust(graph);
            Call(T("VisualEffectResourceExtensions"),"WriteAssetWithSubAssets",resource);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
        }
        static void BuildDust(object graph)
        {
            var spawn=Node("VFXBasicSpawner");var init=Node("VFXBasicInitialize");var update=Node("VFXBasicUpdate");var output=Node("VFXPlanarPrimitiveOutput");
            int row=0;foreach(var context in new[]{spawn,init,update,output}){Add(graph,context);Field(context.GetType(),"m_UIPosition").SetValue(context,new Vector2(650,row++*450));}
            Call(init,"LinkFrom",spawn);Call(update,"LinkFrom",init);Call(output,"LinkFrom",update);
            var data=Call(init,"GetData");Setting(data,"capacity",64u);Setting(data,"boundsMode","Manual");var space=data.GetType().GetProperty("space",Flags);space.SetValue(data,Enum.Parse(space.PropertyType,"Local"));
            var boxType=(Type)Get(Get(Slots(init)[0],"property"),"type");var box=Activator.CreateInstance(boxType);Field(boxType,"size").SetValue(box,new Vector3(256,256,256));Value(init,0,box);
            var burst=Node("VFXSpawnerBurst");Add(spawn,burst);Value(burst,0,12f);
            Attribute(init,"position",true);Attribute(init,"size",true);var size=Attribute(init,"size");Setting(size,"Composition","Multiply");Value(size,0,3f);
            Value(Attribute(init,"lifetime"),0,4.5f);Value(Attribute(init,"color"),0,new Vector3(.3f,.25f,.19f));
            var velocity=Attribute(init,"velocity");Setting(velocity,"Random","PerComponent");Value(velocity,0,new Vector3(-9,1,-9));Value(velocity,1,new Vector3(9,5,9));
            var drag=Node("Block.Drag");Add(update,drag);Value(drag,0,.6f);
            var fade=Node("Block.AttributeFromCurve");Setting(fade,"attribute","alpha");Setting(fade,"Mode","Uniform");Add(update,fade);Value(fade,0,new AnimationCurve(new Keyframe(0,0),new Keyframe(.12f,.18f),new Keyframe(1,0)));
            var growth=Node("Block.AttributeFromCurve");Setting(growth,"attribute","size");Setting(growth,"Mode","Uniform");Setting(growth,"Composition","Multiply");Add(output,growth);Value(growth,0,AnimationCurve.Linear(0,1,1,2));
            var orient=Node("Block.Orient");Setting(orient,"mode","FaceCameraPlane");Add(output,orient);Setting(output,"blendMode","Alpha");Setting(output,"sort","Off");Setting(output,"useBaseColorMap","ColorAndAlpha");
            NamedValue(output,"mainTexture",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/CombatDustTexture.asset"));
        }
        static void BuildParticleTexture(string name,bool spark)
        {
            const int size=64;var texture=new Texture2D(size,size,TextureFormat.RGBA32,true,true){name=name,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){float u=(x+.5f)/size*2-1,v=(y+.5f)/size*2-1;float edge=Mathf.Clamp01(1-u*u-v*v);float alpha=spark?Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(u)),3)*Mathf.Clamp01((1-Mathf.Abs(v))*5):Mathf.Pow(edge,1.6f)*(.35f+.65f*Mathf.PerlinNoise(x*.11f+17,y*.11f+31));pixels[x+y*size]=new Color(1,1,1,alpha);}
            texture.SetPixels(pixels);texture.Apply(true,false);string path=Root+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(existing!=null){EditorUtility.CopySerialized(texture,existing);UnityEngine.Object.DestroyImmediate(texture);}else AssetDatabase.CreateAsset(texture,path);
        }
        [MenuItem("Massive Army/Art/Build Nuclear VFX Graph")]
        public static void BuildNuclearOnly(){vfx=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="Unity.VisualEffectGraph.Editor");BuildNuclear();AssetDatabase.SaveAssets();}
        static void BuildNuclear()
        {
            BuildCloudTexture();
            string path=Root+"/NuclearBurst.vfx";
            var asset=AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path)??(VisualEffectAsset)Call(T("VisualEffectAssetEditorUtility"),"CreateNewAsset",path);
            var resource=Call(T("VisualEffectObjectExtensions"),"GetResource",asset);var graph=Call(T("VisualEffectResourceExtensions"),"GetGraph",resource);
            foreach(var child in ((IEnumerable)Get(graph,"children")).Cast<object>().ToArray())Call(graph,"RemoveChild",child);
            // Fixed GPU particle counts. All cloud particles start near ground, rise slowly,
            // retain density through the middle of their life, then disperse.
            NuclearSystem(graph,0,12,65,new Vector3(-32,8,-32),new Vector3(32,16,32),new Vector3(-.35f,5.5f,-.35f),new Vector3(.35f,7,.35f),85,new Vector3(.38f,.35f,.30f),
                new AnimationCurve(new Keyframe(0,.35f),new Keyframe(.08f,.75f),new Keyframe(.65f,.65f),new Keyframe(1,0)),
                new AnimationCurve(new Keyframe(0,.5f),new Keyframe(.15f,1.2f),new Keyframe(.65f,1.6f),new Keyframe(1,2.3f)),false,false);
            NuclearSystem(graph,1,12,60,new Vector3(-8,0,-8),new Vector3(8,8,8),new Vector3(-.12f,1,-.12f),new Vector3(.12f,6.5f,.12f),32,new Vector3(.32f,.29f,.25f),
                new AnimationCurve(new Keyframe(0,.65f),new Keyframe(.65f,.6f),new Keyframe(1,0)),AnimationCurve.Linear(0,1,1,2.2f),false,false);
            NuclearSystem(graph,2,16,18,new Vector3(-55,3,-55),new Vector3(55,9,55),new Vector3(-13,1,-13),new Vector3(13,3,13),65,new Vector3(.38f,.31f,.23f),
                new AnimationCurve(new Keyframe(0,.95f),new Keyframe(.25f,.8f),new Keyframe(1,0)),AnimationCurve.Linear(0,1,1,2.4f),false,false);
            NuclearSystem(graph,3,1,.45f,Vector3.up*12,Vector3.up*12,Vector3.zero,Vector3.zero,480,new Vector3(15,12,8),AnimationCurve.Linear(0,1,1,0),AnimationCurve.Linear(0,1,1,1.3f),true,false);
            var ring=new Texture2D(128,128,TextureFormat.RGBA32,true,true){name="Expanding nuclear shock front",wrapMode=TextureWrapMode.Clamp};var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++){float r=new Vector2((x+.5f)/64-1,(y+.5f)/64-1).magnitude;pixels[x+y*128]=new Color(1,1,1,Mathf.Exp(-Mathf.Pow((r-.85f)*55,2)));}ring.SetPixels(pixels);ring.Apply();
            string ringPath=Root+"/NuclearShockTexture.asset";var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(ringPath);if(existing!=null){EditorUtility.CopySerialized(ring,existing);UnityEngine.Object.DestroyImmediate(ring);}else AssetDatabase.CreateAsset(ring,ringPath);
            NuclearSystem(graph,4,1,2.8f,Vector3.up*20,Vector3.up*20,Vector3.zero,Vector3.zero,1,new Vector3(2,1.7f,1.3f),AnimationCurve.Linear(0,.8f,1,0),AnimationCurve.Linear(0,15,1,750),true,true);
            Call(T("VisualEffectResourceExtensions"),"WriteAssetWithSubAssets",resource);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
        }
        static void NuclearSystem(object graph,int column,int count,float lifetime,Vector3 p0,Vector3 p1,Vector3 v0,Vector3 v1,float size,Vector3 colour,AnimationCurve alpha,AnimationCurve growth,bool additive,bool ring)
        {
            var spawn=Node("VFXBasicSpawner");var init=Node("VFXBasicInitialize");var update=Node("VFXBasicUpdate");var output=Node("VFXPlanarPrimitiveOutput");
            int row=0;foreach(var context in new[]{spawn,init,update,output}){Add(graph,context);Field(context.GetType(),"m_UIPosition").SetValue(context,new Vector2(column*650,row++*450));}
            Call(init,"LinkFrom",spawn);Call(update,"LinkFrom",init);Call(output,"LinkFrom",update);
            var data=Call(init,"GetData");Setting(data,"capacity",(uint)(count>1?32:1));Setting(data,"boundsMode","Manual");var space=data.GetType().GetProperty("space",Flags);space.SetValue(data,Enum.Parse(space.PropertyType,"Local"));
            var boxType=(Type)Get(Get(Slots(init)[0],"property"),"type");var box=Activator.CreateInstance(boxType);Field(boxType,"center").SetValue(box,new Vector3(0,250,0));Field(boxType,"size").SetValue(box,new Vector3(1600,1400,1600));Value(init,0,box);
            var burst=Node("VFXSpawnerBurst");Add(spawn,burst);Value(burst,0,(float)count);
            var position=Attribute(init,"position");Setting(position,"Random","PerComponent");Value(position,0,p0);Value(position,1,p1);
            var velocity=Attribute(init,"velocity");Setting(velocity,"Random","PerComponent");Value(velocity,0,v0);Value(velocity,1,v1);
            Value(Attribute(init,"size"),0,size);Value(Attribute(init,"color"),0,colour);Value(Attribute(init,"lifetime"),0,lifetime);
            var fade=Node("Block.AttributeFromCurve");Setting(fade,"attribute","alpha");Setting(fade,"Mode","Uniform");Add(update,fade);Value(fade,0,alpha);
            var grow=Node("Block.AttributeFromCurve");Setting(grow,"attribute","size");Setting(grow,"Mode","Uniform");Setting(grow,"Composition","Multiply");Add(output,grow);Value(grow,0,growth);
            var orient=Node("Block.Orient");Setting(orient,"mode",ring?"Advanced":"FaceCameraPlane");if(ring){Setting(orient,"axes","XY");Value(orient,0,Vector3.right);Value(orient,1,Vector3.forward);}Add(output,orient);
            Setting(output,"useAlphaClipping",true);NamedValue(output,"alphaThreshold",.01f);
            Setting(output,"blendMode",additive?"Additive":"Alpha");Setting(output,"sort","Off");Setting(output,"useBaseColorMap","ColorAndAlpha");NamedValue(output,"mainTexture",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/"+(ring?"NuclearShockTexture":additive?"CombatDustTexture":"NuclearCloudTexture")+".asset"));
        }
        static void BuildCloudTexture()
        {
            const int size=128;var image=new Texture2D(size,size,TextureFormat.RGBA32,true,true){name="Soft turbulent nuclear smoke",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){float u=(x+.5f)/size*2-1,v=(y+.5f)/size*2-1,r=u*u+v*v;
                float noise=.55f*Mathf.PerlinNoise(u*4+13,v*4+27)+.3f*Mathf.PerlinNoise(u*11+39,v*11+17)+.15f*Mathf.PerlinNoise(u*23+41,v*23+57);
                float density=Mathf.Clamp01((1-r+(noise-.5f)*.5f)*4),shade=Mathf.Clamp01(.48f+.35f*Mathf.Sqrt(Mathf.Clamp01(1-r))+.3f*noise-.12f*u+.12f*v);
                pixels[x+y*size]=new Color(shade,shade,shade,density*Mathf.Clamp01(.55f+noise));}
            image.SetPixels(pixels);image.Apply();string path=Root+"/NuclearCloudTexture.asset";var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(existing!=null){EditorUtility.CopySerialized(image,existing);UnityEngine.Object.DestroyImmediate(image);}else AssetDatabase.CreateAsset(image,path);
        }
        public static void ExportShaders()
        {
            vfx=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="Unity.VisualEffectGraph.Editor");Directory.CreateDirectory("TestResults/CombatRefinement");
            foreach(string name in new[]{"CombatSparks","CombatBurst","NuclearBurst"}){var asset=AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Root+"/"+name+".vfx");var resource=Call(T("VisualEffectObjectExtensions"),"GetResource",asset);int count=(int)Call(resource,"GetShaderSourceCount");for(int i=0;i<count;i++)File.WriteAllText("TestResults/CombatRefinement/"+name+"-"+i+".hlsl",(string)Call(resource,"GetShaderSource",i));}
        }
        public static void ValidateShaders()
        {
            ExportShaders();string spark=string.Join("\n",Directory.GetFiles("TestResults/CombatRefinement","CombatSparks-*.hlsl").Select(File.ReadAllText));string burst=string.Join("\n",Directory.GetFiles("TestResults/CombatRefinement","CombatBurst-*.hlsl").Select(File.ReadAllText));
            if(!spark.Contains("float3(-18, 2, -18)")||!spark.Contains("float3(18, 25, 18)")||!burst.Contains("float3(-9, 1, -9)")||!burst.Contains("float3(9, 5, 9)"))throw new InvalidOperationException("Compiled VFX velocity ranges were lost during serialization");
            File.WriteAllText("TestResults/CombatRefinement/shader-validation.txt","PASS: compiled spark and dust velocity ranges survive serialization");
        }
        static void BuildRocket()
        {
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var colours=new List<Color>();var indices=new List<int>();
            void Triangle(Vector3 a,Vector3 b,Vector3 c,Color colour){int n=vertices.Count;Vector3 normal=Vector3.Cross(b-a,c-a).normalized;vertices.AddRange(new[]{a,b,c});normals.AddRange(new[]{normal,normal,normal});colours.AddRange(new[]{colour,colour,colour});indices.AddRange(new[]{n,n+1,n+2});}
            const int sides=12;float[] heights={-8,-6,5,8,11};float[] radii={1.1f,1.4f,1.4f,1.1f,0};
            for(int ring=0;ring<heights.Length-1;ring++)for(int i=0;i<sides;i++){
                float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;Vector3 A=new Vector3(Mathf.Sin(a)*radii[ring],heights[ring],Mathf.Cos(a)*radii[ring]),B=new Vector3(Mathf.Sin(b)*radii[ring],heights[ring],Mathf.Cos(b)*radii[ring]),C=new Vector3(Mathf.Sin(a)*radii[ring+1],heights[ring+1],Mathf.Cos(a)*radii[ring+1]),D=new Vector3(Mathf.Sin(b)*radii[ring+1],heights[ring+1],Mathf.Cos(b)*radii[ring+1]);
                Color colour=ring==0||ring==2?new Color(1,0,0):new Color(.65f,.7f,.75f);Triangle(A,B,C,colour);Triangle(B,D,C,colour);
            }
            for(int i=0;i<4;i++){float angle=i*Mathf.PI*.5f;Vector3 side=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));Vector3 a=side*1.2f+Vector3.down*3,b=side*4+Vector3.down*8,c=side*1.2f+Vector3.down*8;Triangle(a,b,c,new Color(.2f,.25f,.3f));Triangle(c,b,a,new Color(.2f,.25f,.3f));}
            for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;Triangle(Vector3.down*8,new Vector3(Mathf.Sin(b)*1.1f,-8,Mathf.Cos(b)*1.1f),new Vector3(Mathf.Sin(a)*1.1f,-8,Mathf.Cos(a)*1.1f),new Color(.06f,.07f,.08f));}
            var mesh=new Mesh{name="Strategic rocket with pointed nose and four fins"};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colours);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
            string path=Root+"/Rocket.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing!=null){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);}else AssetDatabase.CreateAsset(mesh,path);
        }
    }
}
