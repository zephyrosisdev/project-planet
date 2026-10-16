using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy
{
    // Runtime-only pipeline copy: graphics scaling never changes generation or saved HDRP assets.
    public sealed class BattleRenderQuality : MonoBehaviour
    {
        public float Scale=1;
        RenderPipelineAsset original;HDRenderPipelineAsset pipeline;
        public void Initialize(Camera camera,float scale)
        {
            Scale=Mathf.Clamp(scale,.5f,1);original=QualitySettings.renderPipeline;
            var source=GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;if(source==null)return;
            pipeline=Instantiate(source);pipeline.name="Runtime battle graphics quality";pipeline.hideFlags=HideFlags.DontSave;
            var settings=pipeline.currentPlatformRenderPipelineSettings;var dynamic=settings.dynamicResolutionSettings;
            dynamic.enabled=true;dynamic.forceResolution=false;dynamic.minPercentage=50;dynamic.maxPercentage=100;dynamic.dynResType=DynamicResolutionType.Software;
            settings.dynamicResolutionSettings=dynamic;pipeline.currentPlatformRenderPipelineSettings=settings;
            QualitySettings.renderPipeline=pipeline;camera.GetComponent<HDAdditionalCameraData>().allowDynamicResolution=true;
            DynamicResolutionHandler.SetDynamicResScaler(()=>Scale*100,DynamicResScalePolicyType.ReturnsPercentage);
        }
        void OnDestroy(){if(pipeline==null)return;if(QualitySettings.renderPipeline==pipeline)QualitySettings.renderPipeline=original;DynamicResolutionHandler.SetDynamicResScaler(()=>100,DynamicResScalePolicyType.ReturnsPercentage);ArmyLifetime.Destroy(pipeline);pipeline=null;}
    }
}
