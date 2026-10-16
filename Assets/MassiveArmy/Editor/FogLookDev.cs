using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy.EditorTools
{
    public sealed class FogLookDev : EditorWindow
    {
        GameObject root;
        Battlefield field;GpuBattle battle;ArmyRenderer renderer;VolumetricFogOfWar fog;
        SceneView scene;
        bool previousRefresh;
        BattleVisualSettings look;UnityEditor.Editor lookEditor;
        Material sliceMaterial;RenderTexture sliceImage;
        float slice=.15f;
        bool noiseSlice;
        double nextFrame;
        [MenuItem("Massive Army/Art/Fog Look Dev")]
        public static void Open()=>GetWindow<FogLookDev>("Fog Look Dev");
        public static void DisablePreview(){foreach(var window in Resources.FindObjectsOfTypeAll<FogLookDev>())window.Stop();}
        void OnEnable(){look=Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings");SceneView.duringSceneGui+=DrawTestUnit;EditorApplication.update+=Animate;AssemblyReloadEvents.beforeAssemblyReload+=Stop;EditorApplication.playModeStateChanged+=PlayState;}
        void PlayState(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingEditMode)Stop();}
        void OnDisable(){Stop();if(lookEditor!=null)DestroyImmediate(lookEditor);lookEditor=null;SceneView.duringSceneGui-=DrawTestUnit;EditorApplication.update-=Animate;AssemblyReloadEvents.beforeAssemblyReload-=Stop;EditorApplication.playModeStateChanged-=PlayState;}
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Editor-only smog preview. A friendly test unit moves in a circle and leaves persistent wakes. Close / disable to release the preview GPU resources.",MessageType.Info);
            GUI.enabled=!EditorApplication.isPlaying;
            if(GUILayout.Button(root==null?"Enable Scene view preview":"Disable preview")){if(root==null)StartPreview();else Stop();}
            GUI.enabled=true;
            if(look!=null){if(lookEditor==null)lookEditor=UnityEditor.Editor.CreateEditor(look);lookEditor.OnInspectorGUI();}
            noiseSlice=EditorGUILayout.Toggle("Show 3D noise instead of wakes",noiseSlice);
            slice=EditorGUILayout.Slider("Diagnostic height slice",slice,0,1);
            if(sliceImage!=null)GUILayout.Label(sliceImage,GUILayout.Width(Mathf.Min(position.width-25,360)),GUILayout.Height(240));
            EditorGUILayout.LabelField("Wake resources",(FogWakeField.ResourceBytes/(1024f*1024f)).ToString("0.0")+" MiB / 64 MiB cap");
        }
        void StartPreview()
        {
            scene=SceneView.lastActiveSceneView;if(scene==null)throw new InvalidOperationException("Open a Scene view first.");
            try{
                TerrainLookDev.DisablePreview();root=new GameObject("Editor-only fog look development"){hideFlags=HideFlags.HideAndDontSave};
                var cfg=new WorldGenerationSettings{size=2048,oceanCoverage=0,riverAbundance=0,lakeAbundance=0,reliefHeight=0,mountainCoverage=0,forestCoverage=0,roadDensity=0,rockAbundance=0};
                var settings=new BattleSettings{world=cfg,baseBuilding=true,activeUnits=64,capacity=64,projectileCapacity=1024,enemyAI=false};settings.ScaleWorld();
                field=new Battlefield(settings,root.transform);battle=new GpuBattle(settings,field,RtsCatalog.Load().units.Select(u=>u.Gpu).ToArray());renderer=new ArmyRenderer(battle);
                var sunObject=new GameObject("Preview sun");sunObject.transform.SetParent(root.transform);sunObject.transform.rotation=Quaternion.Euler(48,-35,0);var light=sunObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=look.sunLux;light.color=look.sunColour;
                sunObject.AddComponent<HDAdditionalLightData>();
                fog=root.AddComponent<VolumetricFogOfWar>();fog.Initialize(battle,scene.camera,light,look);
                scene.LookAtDirect(new Vector3(0,field.Height(Vector2.zero),0),Quaternion.Euler(55,0,0),240);scene.orthographic=true;previousRefresh=scene.sceneViewState.alwaysRefresh;scene.sceneViewState.alwaysRefresh=true;
                sliceMaterial=new Material(Resources.Load<Shader>("MassiveArmy/FogVolumeSlice"));sliceImage=new RenderTexture(384,384,0);sliceImage.Create();nextFrame=0;
            }catch{Stop();throw;}
        }
        void Animate()
        {
            if(root==null||scene==null||EditorApplication.isCompiling||EditorApplication.timeSinceStartup<nextFrame)return;
            nextFrame=EditorApplication.timeSinceStartup+1f/30;
            float angle=(float)EditorApplication.timeSinceStartup*.2f;Vector2 p=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*100;
            var unit=new UnitState{position=p,previous=p,destination=p,velocity=new Vector2(-Mathf.Sin(angle),Mathf.Cos(angle))*20,health=100,flags=1,group=uint.MaxValue,target=uint.MaxValue};
            var fixture=new UnitState[64];fixture[0]=unit;battle.SetValidationUnits(fixture);
            // Run reconnaissance without stepping movement; the fixture itself animates
            // the unit. Its visible mist and dispersal must match a real match.
            battle.Update(scene.camera,0,renderer.IndexCounts,false);renderer.Render(scene.camera);field.UpdateLod(scene.camera,true);
            if(fog.WakeVolume!=null){sliceMaterial.SetTexture("_Volume",noiseSlice?fog.NoiseVolume:fog.WakeVolume);sliceMaterial.SetFloat("_Slice",slice);sliceMaterial.SetFloat("_NoiseSlice",noiseSlice?1:0);Graphics.Blit(null,sliceImage,sliceMaterial);}
            scene.Repaint();Repaint();EditorApplication.QueuePlayerLoopUpdate();
        }
        void DrawTestUnit(SceneView current)
        {
            if(root==null||current!=scene||field==null)return;float angle=(float)EditorApplication.timeSinceStartup*.2f;
            Vector2 p=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*100;Vector3 location=new Vector3(p.x,field.Height(p)+3,p.y);
            Handles.color=Color.cyan;Handles.DrawWireDisc(location,Vector3.up,5);Handles.Label(location+Vector3.up*10,"Moving friendly wake emitter");
        }
        void Stop()
        {
            fog?.Detach();renderer?.Dispose();battle?.Dispose();field?.Dispose();renderer=null;battle=null;field=null;fog=null;
            if(scene!=null)scene.sceneViewState.alwaysRefresh=previousRefresh;
            if(root!=null)DestroyImmediate(root);root=null;if(sliceImage!=null){sliceImage.Release();DestroyImmediate(sliceImage);}sliceImage=null;if(sliceMaterial!=null)DestroyImmediate(sliceMaterial);sliceMaterial=null;
        }
    }
}
