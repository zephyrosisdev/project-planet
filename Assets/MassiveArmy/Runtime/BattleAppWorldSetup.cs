using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        bool worldSetupOpen;
        int setupGroup;
        Vector2 setupScroll;
        string setupMessage="",seedEntry="",presetPath="";
        Task<WorldSetupResult> previewTask;
        CancellationTokenSource previewCancellation;
        Texture2D previewImage;
        static readonly string[] Groups={"Land / coast","Relief","Climate","Water","Vegetation","Roads","Resources / starts"};
        static readonly string[][] Controls={
            new[]{"oceanCoverage","continents","islandAbundance","islandSize","islandClustering","coastRoughness"},
            new[]{"mountainCoverage","ridgeLength","reliefHeight","foothillWidth","valleyWidth","erosionStrength"},
            new[]{"temperature","rainfall","climateVariation","rainShadow"},
            new[]{"riverAbundance","riverWidth","lakeAbundance","lakeSize","basinScale","fordFrequency"},
            new[]{"forestCoverage","forestPatchSize","forestClustering","clearingFrequency","shrubAbundance","rockAbundance"},
            new[]{"roadDensity","roadLoops","roadWidth","roadCurvature","flatRoutePreference"},
            new[]{"resourceAbundance","resourceRichness","resourceClustering","startBalance","startSeparation"}};
        void InvalidateWorldPreview()
        {
            previewCancellation?.Cancel();settings.preview=null;setupMessage="Settings changed: generate a new preview.";
            ArmyLifetime.Destroy(previewImage);previewImage=null;
        }
        void CancelWorldPreview()
        {
            previewCancellation?.Cancel();previewCancellation?.Dispose();previewCancellation=null;
            ArmyLifetime.Destroy(previewImage);previewImage=null;
        }
        void BeginWorldPreview()
        {
            if(previewTask!=null&&!previewTask.IsCompleted)return;
            try{settings.world.Validate();}catch(Exception e){setupMessage=e.Message;return;}
            previewCancellation?.Dispose();previewCancellation=new CancellationTokenSource();
            var snapshot=settings.world.Copy();var token=previewCancellation.Token;var catalog=RtsCatalog.Load();
            previewTask=Task.Run(()=>WorldSetupResult.Generate(snapshot,token,catalog),token);setupMessage="Generating terrain, drainage and balanced starts…";
        }
        void PollWorldPreview()
        {
            if(previewTask==null||!previewTask.IsCompleted)return;
            var completed=previewTask;previewTask=null;
            if(completed.IsCanceled){setupMessage="Preview cancelled.";return;}
            if(completed.IsFaulted){setupMessage=completed.Exception.GetBaseException().Message;return;}
            var result=completed.Result;if(result.InputKey!=settings.world.Key)return;
            string progression;try{progression=TransportProgression.Validate(result,RtsCatalog.Load());}catch(Exception e){setupMessage=e.Message;return;}
            settings.preview=result;ArmyLifetime.Destroy(previewImage);
            int r=result.Map.Resolution;previewImage=new Texture2D(r,r,TextureFormat.RGBA32,false);var pixels=new Color[r*r];
            for(int i=0;i<pixels.Length;i++)pixels[i]=result.Map.MapColour(i);
            previewImage.SetPixels(pixels);previewImage.Apply();setupMessage="Validated · generator 4 · attempt "+(result.Attempt+1)+" · seed "+result.Map.Seed;
        }
    }
}
