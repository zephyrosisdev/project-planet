using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MassiveArmy.EditorTools
{
    [InitializeOnLoad]
    static class TerrainLiveUpdate
    {
        static TerrainLiveUpdate()
        {
            EditorApplication.update+=Update;
        }
        static void Update()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(TerrainSurfaceLibrary.RefreshLiveMaterials())
            {
                SceneView.RepaintAll();EditorApplication.QueuePlayerLoopUpdate();
            }
        }
    }

    [CustomEditor(typeof(TerrainSurfaceLibrary))]
    sealed class TerrainSurfaceLibraryInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Changes update the running battle and Scene view preview immediately. X/Y are exposed below as tile size and normal strength.",MessageType.Info);
            DrawPropertiesExcluding(serializedObject,"m_Script","layerParameters");
            var layers=serializedObject.FindProperty("layerParameters");
            EditorGUILayout.Space();EditorGUILayout.LabelField("Surface scale and relief",EditorStyles.boldLabel);
            for(int i=0;i<Mathf.Min(layers.arraySize,TerrainSurfaceLibrary.LayerNames.Length);i++)
            {
                var element=layers.GetArrayElementAtIndex(i);Vector4 value=element.vector4Value;
                EditorGUILayout.LabelField(TerrainSurfaceLibrary.LayerNames[i],EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                float tile=EditorGUILayout.FloatField("Tile size (metres)",value.x);
                float normal=EditorGUILayout.Slider("Normal multiplier",value.y,0,2);
                if(EditorGUI.EndChangeCheck()){value.x=Mathf.Max(.1f,tile);value.y=normal;element.vector4Value=value;}
            }
            if(serializedObject.ApplyModifiedProperties())((TerrainSurfaceLibrary)target).NotifySettingsChanged();
            EditorGUILayout.Space();
            if(GUILayout.Button("Open Terrain Look Dev"))TerrainLookDev.Open();
        }
    }

    public sealed class TerrainLookDev : EditorWindow
    {
        [SerializeField] bool worldPreview;
        [SerializeField] int seed=73129,firstLayer=0,secondLayer=1;
        [SerializeField] float worldSize=2048;
        UnityEditor.Editor libraryEditor;
        Vector2 scroll;
        TerrainSurfaceLibrary library;
        static Mesh mesh;
        static Material material;
        static Texture2D layersA,layersB,splat;
        static Bounds bounds;
        static GameObject lightingRoot;
        static VolumeProfile lightingProfile;
        static readonly Dictionary<SceneView,bool> sceneLightingStates=new Dictionary<SceneView,bool>();
        public static bool PreviewEnabled=>material!=null;

        [MenuItem("Massive Army/Art/Terrain Look Dev")]
        public static void Open()=>GetWindow<TerrainLookDev>("Terrain Look Dev");
        public static void EnableConfiguredPreview()
        {
            var window=GetWindow<TerrainLookDev>("Terrain Look Dev");
            CreatePreview(window.library,window.worldPreview,window.seed,window.worldSize,window.firstLayer,window.secondLayer);
        }
        void OnEnable()
        {
            minSize=new Vector2(420,480);
            library=Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary");
            RenderPipelineManager.beginCameraRendering-=Draw;RenderPipelineManager.beginCameraRendering+=Draw;
            Camera.onPreCull-=DrawBuiltIn;Camera.onPreCull+=DrawBuiltIn;
            AssemblyReloadEvents.beforeAssemblyReload+=DisablePreview;
            EditorApplication.playModeStateChanged+=PlayModeChanged;
        }
        void OnDisable()
        {
            DisablePreview();
            RenderPipelineManager.beginCameraRendering-=Draw;Camera.onPreCull-=DrawBuiltIn;
            AssemblyReloadEvents.beforeAssemblyReload-=DisablePreview;
            EditorApplication.playModeStateChanged-=PlayModeChanged;
            if(libraryEditor!=null)DestroyImmediate(libraryEditor);
        }
        static void PlayModeChanged(PlayModeStateChange change)
        {
            if(change==PlayModeStateChange.ExitingEditMode)DisablePreview();
        }
        void OnGUI()
        {
            if(library==null){EditorGUILayout.HelpBox("Rebuild terrain textures to create the surface library.",MessageType.Warning);return;}
            bool playing=EditorApplication.isPlayingOrWillChangePlaymode;
            EditorGUILayout.HelpBox(playing?"Edit the settings below to update the running battle.":"Scene view preview uses its own daylight and exposure, with Fog Look Dev disabled. Turn it off or close this window to remove it. Terrain-only: no water, scenery or units.",MessageType.Info);
            EditorGUI.BeginDisabledGroup(playing);
            EditorGUI.BeginChangeCheck();
            bool enabled=EditorGUILayout.Toggle("Scene view preview",PreviewEnabled);
            worldPreview=EditorGUILayout.Popup("Preview",worldPreview?1:0,new[]{"Layer blend study","Biome terrain"})==1;
            if(worldPreview){seed=EditorGUILayout.IntField("World seed",seed);worldSize=Mathf.Clamp(EditorGUILayout.FloatField("World size (metres)",worldSize),128,4096);}
            else{firstLayer=EditorGUILayout.Popup("First surface",firstLayer,TerrainSurfaceLibrary.LayerNames);secondLayer=EditorGUILayout.Popup("Second surface",secondLayer,TerrainSurfaceLibrary.LayerNames);}
            bool changed=EditorGUI.EndChangeCheck();
            if(changed)
            {
                if(enabled)CreatePreview(library,worldPreview,seed,worldSize,firstLayer,secondLayer);
                else DisablePreview();
            }
            if(PreviewEnabled&&GUILayout.Button("Frame preview in Scene view"))FramePreview();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space();
            scroll=EditorGUILayout.BeginScrollView(scroll);
            UnityEditor.Editor.CreateCachedEditor(library,typeof(TerrainSurfaceLibraryInspector),ref libraryEditor);
            libraryEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }
        static void Draw(ScriptableRenderContext context,Camera camera)=>DrawPreview(camera);
        static void DrawBuiltIn(Camera camera){if(GraphicsSettings.currentRenderPipeline==null)DrawPreview(camera);}
        static void DrawPreview(Camera camera)
        {
            if(!PreviewEnabled||camera.cameraType!=CameraType.SceneView||EditorApplication.isPlayingOrWillChangePlaymode)return;
            foreach(SceneView view in SceneView.sceneViews)if(view.camera==camera)PrepareSceneLighting(view);
            Graphics.DrawMesh(mesh,Matrix4x4.identity,material,0,camera,0,null,ShadowCastingMode.Off,true,null,LightProbeUsage.BlendProbes);
        }
        static void PrepareSceneLighting(SceneView view)
        {
            if(!sceneLightingStates.ContainsKey(view))sceneLightingStates.Add(view,view.sceneLighting);
            view.sceneLighting=true;
        }
        static void CreatePreviewLighting()
        {
            lightingRoot=new GameObject("Editor-only terrain preview lighting"){hideFlags=HideFlags.HideAndDontSave};
            var sunObject=new GameObject("Terrain preview daylight"){hideFlags=HideFlags.HideAndDontSave};
            sunObject.transform.SetParent(lightingRoot.transform);sunObject.transform.rotation=Quaternion.Euler(48,-35,0);
            var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(1,.965f,.9f);sun.shadows=LightShadows.None;
            sunObject.AddComponent<HDAdditionalLightData>();sun.lightUnit=LightUnit.Lux;sun.intensity=50000;
            var volume=lightingRoot.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10000;
            lightingProfile=ScriptableObject.CreateInstance<VolumeProfile>();lightingProfile.hideFlags=HideFlags.HideAndDontSave;volume.sharedProfile=lightingProfile;
            var exposure=lightingProfile.Add<Exposure>();exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(12.5f);
            lightingProfile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            lightingProfile.Add<Fog>().enabled.Override(false);
            lightingProfile.Add<VisualEnvironment>().skyType.Override((int)SkyType.Gradient);
            var sky=lightingProfile.Add<GradientSky>();sky.top.Override(new Color(.24f,.38f,.56f));sky.middle.Override(new Color(.65f,.72f,.8f));sky.bottom.Override(new Color(.25f,.26f,.25f));sky.exposure.Override(12);
        }
        static void FramePreview()
        {
            var view=SceneView.lastActiveSceneView??GetWindow<SceneView>();
            PrepareSceneLighting(view);
            view.Show();view.Focus();view.Frame(bounds,false);view.Repaint();
        }
        public static void CreatePreview(TerrainSurfaceLibrary library,bool world,int seed,float size,int first,int second)
        {
            DisablePreview();
            FogLookDev.DisablePreview();
            try
            {
                CreatePreviewLighting();
                material=new Material(Resources.Load<Shader>("MassiveArmy/Terrain")){name="Terrain Look Dev preview",hideFlags=HideFlags.HideAndDontSave};
                library.Bind(material);
                int resolution=world?256:128,segments=world?128:1;
                size=world?size:128;
                WorldMap map=world?new WorldMap(resolution,size,seed):null;
                var a=new Color[resolution*resolution];var b=new Color[a.Length];var road=new Color[a.Length];
                for(int y=0;y<resolution;y++)for(int x=0;x<resolution;x++)
                {
                    int i=x+y*resolution;
                    if(world){map.SurfaceLayers(i,out a[i],out b[i]);road[i]=new Color(0,0,map.Road[i],map.Water[i]>0?1:0);}
                    else
                    {
                        float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.2f,.8f,(x+.5f)/resolution));
                        AddLayer(ref a[i],ref b[i],ref road[i],first,1-weight);AddLayer(ref a[i],ref b[i],ref road[i],second,weight);
                    }
                }
                layersA=Mask(a,resolution,true);layersB=Mask(b,resolution,true);splat=Mask(road,resolution,false);
                material.SetTexture("_LayersA",layersA);material.SetTexture("_LayersB",layersB);material.SetTexture("_Splat",splat);material.SetFloat("_WorldSize",size);
                material.SetFloat("_BlendSeed",seed % 100003);
                material.SetFloat("_GeographyGuided",world?1:0);
                var vertices=new Vector3[(segments+1)*(segments+1)];var uv=new Vector2[vertices.Length];var triangles=new int[segments*segments*6];int index=0;
                for(int y=0;y<=segments;y++)for(int x=0;x<=segments;x++)
                {
                    int i=x+y*(segments+1);uv[i]=new Vector2((float)x/segments,(float)y/segments);Vector2 p=(uv[i]-Vector2.one*.5f)*size;
                    vertices[i]=new Vector3(p.x,world?map.SampleHeight(p):0,p.y);
                }
                for(int y=0;y<segments;y++)for(int x=0;x<segments;x++)
                {
                    int i=x+y*(segments+1);triangles[index++]=i;triangles[index++]=i+segments+1;triangles[index++]=i+1;triangles[index++]=i+1;triangles[index++]=i+segments+1;triangles[index++]=i+segments+2;
                }
                mesh=new Mesh{name="Terrain Look Dev",hideFlags=HideFlags.HideAndDontSave,indexFormat=IndexFormat.UInt32};
                mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();bounds=mesh.bounds;
                FramePreview();
            }
            catch{DisablePreview();throw;}
        }
        static void AddLayer(ref Color a,ref Color b,ref Color road,int layer,float weight)
        {
            if(layer<4)a[layer]+=weight;else if(layer<8)b[layer-4]+=weight;else road.b+=weight;
        }
        static Texture2D Mask(Color[] pixels,int resolution,bool mips)
        {
            var t=new Texture2D(resolution,resolution,TextureFormat.RGBA32,mips,true){hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=mips?FilterMode.Trilinear:FilterMode.Bilinear};
            t.SetPixels(pixels);t.Apply(mips,true);return t;
        }
        public static void DisablePreview()
        {
            TerrainSurfaceLibrary.Unbind(material);
            foreach(var obj in new UnityEngine.Object[]{material,mesh,layersA,layersB,splat})if(obj!=null)DestroyImmediate(obj);
            material=null;mesh=null;layersA=layersB=splat=null;SceneView.RepaintAll();
            if(lightingRoot!=null)DestroyImmediate(lightingRoot);lightingRoot=null;
            if(lightingProfile!=null){foreach(var component in lightingProfile.components)if(component!=null)DestroyImmediate(component);lightingProfile.components.Clear();DestroyImmediate(lightingProfile);}lightingProfile=null;
            foreach(var previous in sceneLightingStates)if(previous.Key!=null)previous.Key.sceneLighting=previous.Value;
            sceneLightingStates.Clear();
        }

        [MenuItem("Massive Army/Art/Validate Terrain Live Editing")]
        public static void ValidateLiveEditing()
        {
            string status="PASS",error="";
            var source=Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary");
            var copy=Instantiate(source);var testMaterial=new Material(Resources.Load<Shader>("MassiveArmy/Terrain"));
            try
            {
                copy.Bind(testMaterial);
                var inspector=new SerializedObject(copy);
                inspector.FindProperty("heightBlendStrength").floatValue=.31f;
                inspector.FindProperty("heightBlendWidth").floatValue=.13f;
                inspector.FindProperty("biomeBlendWidth").floatValue=37;
                inspector.FindProperty("boundaryWarp").floatValue=19;
                inspector.FindProperty("patchStrength").floatValue=.42f;
                var tile=inspector.FindProperty("layerParameters").GetArrayElementAtIndex(0);
                Vector4 value=tile.vector4Value;value.x=11;tile.vector4Value=value;
                inspector.ApplyModifiedPropertiesWithoutUndo();
                if(!TerrainSurfaceLibrary.RefreshLiveMaterials()||Mathf.Abs(testMaterial.GetFloat("_HeightBlendStrength")-.31f)>.001f||Mathf.Abs(testMaterial.GetFloat("_HeightBlendWidth")-.13f)>.001f||Mathf.Abs(testMaterial.GetVectorArray("_LayerParameters")[0].x-11)>.001f)throw new Exception("Live settings did not reach the bound material.");
                if((testMaterial.GetVector("_BiomeBlend")-new Vector4(37,19,copy.boundaryNoiseScale,.42f)).sqrMagnitude>.0001f)throw new Exception("Natural transition settings did not update live.");
                TerrainSurfaceLibrary.Unbind(testMaterial);copy.heightBlendStrength=.9f;copy.NotifySettingsChanged();TerrainSurfaceLibrary.RefreshLiveMaterials();
                if(Mathf.Abs(testMaterial.GetFloat("_HeightBlendStrength")-.31f)>.001f)throw new Exception("Unbound material was updated.");
            }
            catch(Exception e){status="FAIL";error=e.ToString();Debug.LogException(e);}
            finally{TerrainSurfaceLibrary.Unbind(testMaterial);DestroyImmediate(testMaterial);DestroyImmediate(copy);}
            Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/terrain-live-edit-validation.json",JsonUtility.ToJson(new LiveReport{status=status,error=error},true));
            Debug.Log("Terrain live editing: "+status);
        }
        [Serializable] sealed class LiveReport{public string status,error;}

        // Render the editor preview itself, including its HDRP light and exposure.
        // Run in an isolated editor: this temporarily frames a layer study.
        public static void ValidatePreviewRendering()
        {
            string status="PASS",error="";float brightness=0;RenderTexture target=null;Texture2D image=null;
            var active=RenderTexture.active;var view=SceneView.lastActiveSceneView??GetWindow<SceneView>();bool lighting=view.sceneLighting;
            try
            {
                view.sceneLighting=false;
                Open();CreatePreview(Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary"),false,73129,128,0,1);
                if(!view.sceneLighting)throw new Exception("Preview did not enable scene lighting.");
                var camera=view.camera;camera.transform.rotation=Quaternion.Euler(55,0,0);camera.transform.position=-camera.transform.forward*300;
                camera.orthographic=true;camera.orthographicSize=90;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=1000;
                target=new RenderTexture(512,512,24,RenderTextureFormat.ARGBHalf);target.Create();
                for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
                RenderTexture.active=target;image=new Texture2D(512,512,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();
                var pixels=image.GetPixels();for(int y=220;y<292;y++)for(int x=220;x<292;x++)brightness+=pixels[x+y*512].grayscale;
                brightness/=72*72;
                Directory.CreateDirectory("TestResults");File.WriteAllBytes("TestResults/terrain-look-dev-preview.png",image.EncodeToPNG());
                if(brightness<.025f)throw new Exception("Terrain preview remains dark: "+brightness);
                DisablePreview();if(view.sceneLighting)throw new Exception("Preview did not restore the previous scene lighting toggle.");
                if(lightingRoot!=null||lightingProfile!=null)throw new Exception("Preview lighting resources leaked.");
            }
            catch(Exception e){status="FAIL";error=e.ToString();Debug.LogException(e);}
            finally{DisablePreview();view.sceneLighting=lighting;RenderTexture.active=active;if(image!=null)DestroyImmediate(image);if(target!=null){target.Release();DestroyImmediate(target);}}
            Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/terrain-look-dev-validation.json",JsonUtility.ToJson(new LiveReport{status=status,error=error},true));
            Debug.Log("Terrain Look Dev rendering: "+status+" centre brightness "+brightness);
            if(status!="PASS")throw new Exception(error);
        }
    }
}
