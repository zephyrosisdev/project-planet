using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy.Planetary
{
    // Reuse the original authored sun/volume when launched from the main menu.
    public sealed class PlanetLighting : IDisposable
    {
        public Light Sun {get;}
        public Volume Atmosphere {get;}
        readonly BattleVisualSettings settings;
        readonly Quaternion savedRotation;readonly LightShadows savedShadows;readonly Color savedColour;
        readonly Light savedSun;
        readonly bool borrowed;
        readonly GameObject owned;
        readonly VolumeProfile profile;
        int lightingHash;
        GameObject companionObject;Light companion;
        internal void SetCompanion(Vector3 direction,Color colour,float relativePower){
            if(companion==null&&relativePower<=.001f)return;
            if(companion==null){companionObject=new GameObject("Binary companion starlight");companionObject.transform.SetParent(Sun.transform.parent,false);companion=companionObject.AddComponent<Light>();companion.type=LightType.Directional;companion.shadows=LightShadows.None;companionObject.AddComponent<HDAdditionalLightData>();}
            companion.enabled=relativePower>.001f;companion.transform.rotation=Quaternion.LookRotation(-direction,Vector3.up);companion.color=Color.Lerp(Color.white,colour,.25f);
            companion.GetComponent<HDAdditionalLightData>().SetIntensity(Mathf.Max(0,Sun.intensity*relativePower),LightUnit.Lux);
        }
        public PlanetLighting(Camera camera,Transform parent,Vector3 home)
        {
            settings=Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings");savedSun=RenderSettings.sun;
            var authored=UnityEngine.Object.FindAnyObjectByType<BattleLightingRig>();
            var hd=camera.GetComponent<HDAdditionalCameraData>();if(hd==null)hd=camera.gameObject.AddComponent<HDAdditionalCameraData>();
            hd.customRenderingSettings=true;hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.CustomPass]=true;hd.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.CustomPass,true);
            hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=new Color(.004f,.007f,.015f);
            if(authored!=null&&authored.sun!=null&&authored.atmosphere!=null){
                borrowed=true;Sun=authored.sun;Atmosphere=authored.atmosphere;savedRotation=Sun.transform.rotation;savedShadows=Sun.shadows;savedColour=Sun.color;
                Sun.transform.rotation=Quaternion.FromToRotation(Vector3.up,home)*savedRotation;authored.Configure(camera,true);
            }else{
                owned=new GameObject("Planet sun and atmosphere");owned.transform.SetParent(parent,false);
                Sun=owned.AddComponent<Light>();Sun.type=LightType.Directional;
                Sun.transform.rotation=Quaternion.FromToRotation(Vector3.up,home)*Quaternion.Euler(60,-35,0);
                Sun.gameObject.AddComponent<HDAdditionalLightData>();
                Atmosphere=owned.AddComponent<Volume>();Atmosphere.isGlobal=true;Atmosphere.priority=100;
                profile=ScriptableObject.CreateInstance<VolumeProfile>();Atmosphere.sharedProfile=profile;
                settings.ApplyLighting(profile,Sun);lightingHash=settings.LightingHash;
                var shadows=profile.Add<HDShadowSettings>();shadows.maxShadowDistance.Override(2600);
                shadows.cascadeShadowSplit0.Override(.45f);shadows.cascadeShadowSplit1.Override(.62f);shadows.cascadeShadowSplit2.Override(.8f);
                var hdLight=Sun.GetComponent<HDAdditionalLightData>();hdLight.SetShadowResolution(2048);hdLight.normalBias=.1f;
                var rig=owned.AddComponent<BattleLightingRig>();rig.sun=Sun;rig.atmosphere=Atmosphere;rig.Configure(camera,true);
            }
            Update();
        }
        public void Update(){
            if(!borrowed&&settings.LightingHash!=lightingHash){settings.ApplyLighting(profile,Sun);lightingHash=settings.LightingHash;}
            var current=Atmosphere.HasInstantiatedProfile()?Atmosphere.profile:Atmosphere.sharedProfile;
            settings.ApplyArmyLighting(Sun,current);
        }
        public void Dispose(){
            if(borrowed&&Sun!=null){Sun.transform.rotation=savedRotation;Sun.shadows=savedShadows;Sun.color=savedColour;settings.ApplyArmyLighting(Sun,Atmosphere.HasInstantiatedProfile()?Atmosphere.profile:Atmosphere.sharedProfile);}
            RenderSettings.sun=savedSun;ArmyLifetime.Destroy(companionObject);ArmyLifetime.Destroy(owned);ArmyLifetime.Destroy(profile);
        }
    }
}
