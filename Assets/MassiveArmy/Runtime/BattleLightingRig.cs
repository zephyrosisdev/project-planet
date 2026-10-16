using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy
{
    public sealed class BattleLightingRig : MonoBehaviour
    {
        public Volume atmosphere;
        public Light sun;
        BattleVisualSettings source;
        void LateUpdate()
        {
            if(source==null)source=Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings");
            if(source!=null&&sun!=null)source.ApplyArmyLighting(sun,atmosphere!=null?(atmosphere.HasInstantiatedProfile()?atmosphere.profile:atmosphere.sharedProfile):null);
        }
        public void Configure(Camera camera,bool shadows)
        {
            RenderSettings.sun=sun;
            var hd=camera.GetComponent<HDAdditionalCameraData>();hd.customRenderingSettings=true;
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.ShadowMaps]=true;
            hd.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.ShadowMaps,shadows);
            sun.shadows=shadows?LightShadows.Soft:LightShadows.None;
        }
    }
}
