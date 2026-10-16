using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
namespace MassiveArmy.EditorTools
{
    public static class BattleLightingSetup
    {
        public const string ProfilePath="Assets/MassiveArmy/Resources/MassiveArmy/BattleAtmosphereProfile.asset";
        [MenuItem("Massive Army/Lighting/Create or Select Scene Lighting")]
        public static void Run()
        {
            if(EditorApplication.isPlaying)throw new System.InvalidOperationException("Exit play mode before editing scene lighting.");
            var scene=SceneManager.GetSceneByPath("Assets/Scenes/MassiveBattle.unity");
            if(!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/Scenes/MassiveBattle.unity",OpenSceneMode.Additive);
            BattleLightingRig rig=null;foreach(var root in scene.GetRootGameObjects()){rig=root.GetComponentInChildren<BattleLightingRig>();if(rig!=null)break;}
            if(rig==null){var go=new GameObject("Battle lighting · edit sun and global volume");SceneManager.MoveGameObjectToScene(go,scene);rig=go.AddComponent<BattleLightingRig>();}
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if(profile==null){
                profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,ProfilePath);
                var lightGo=new GameObject("Battle sun");lightGo.transform.SetParent(rig.transform,false);lightGo.transform.rotation=Quaternion.Euler(48,-35,0);rig.sun=lightGo.AddComponent<Light>();rig.sun.type=LightType.Directional;
                Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings").ApplyLighting(profile,rig.sun);
                profile.TryGet<VisualEnvironment>(out var environment);environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
                var shadows=profile.Add<HDShadowSettings>();shadows.maxShadowDistance.Override(2600);shadows.cascadeShadowSplitCount.Override(4);
                shadows.cascadeShadowSplit0.Override(.45f);shadows.cascadeShadowSplit1.Override(.62f);shadows.cascadeShadowSplit2.Override(.8f);
                var sunData=rig.sun.GetComponent<HDAdditionalLightData>();sunData.SetShadowResolution(2048);sunData.normalBias=.1f;
                var water=profile.Add<WaterRendering>();water.enable.Override(true);water.ambientProbeDimmer.Override(1);
                foreach(var component in profile.components){component.hideFlags=HideFlags.None;AssetDatabase.AddObjectToAsset(component,profile);EditorUtility.SetDirty(component);}EditorUtility.SetDirty(profile);
            }
            if(rig.sun==null){var go=new GameObject("Battle sun");go.transform.SetParent(rig.transform,false);go.transform.rotation=Quaternion.Euler(48,-35,0);rig.sun=go.AddComponent<Light>();rig.sun.type=LightType.Directional;var data=go.AddComponent<HDAdditionalLightData>();data.SetIntensity(50000,LightUnit.Lux);data.SetShadowResolution(2048);data.normalBias=.1f;rig.sun.shadows=LightShadows.Soft;}
            if(rig.atmosphere==null){var go=new GameObject("Battle global volume");go.transform.SetParent(rig.transform,false);rig.atmosphere=go.AddComponent<Volume>();}
            rig.atmosphere.isGlobal=true;rig.atmosphere.sharedProfile=profile;
            var previous=SceneManager.GetActiveScene();SceneManager.SetActiveScene(scene);RenderSettings.sun=rig.sun;EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);SceneManager.SetActiveScene(previous);
            AssetDatabase.SaveAssets();Selection.activeGameObject=rig.gameObject;
        }
        public static void Batch(){Run();EditorApplication.Exit(0);}
    }
}
