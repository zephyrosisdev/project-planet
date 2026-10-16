using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        public IEnumerator ValidateResourceDecals(string folder)
        {
            if(!Application.isEditor)throw new InvalidOperationException("Editor validation only");
            Directory.CreateDirectory(folder);var failures=new List<string>();int checks=0;
            void Check(bool valid,string label){checks++;if(!valid)failures.Add(label);}
            var node=Base.Nodes.First(n=>Battle.IsVisible(n.position,0,true));rig.FocusBase(node.position);Base.Rebase(View);
            yield return null;yield return null;
            Check(Base.ResourceDecalVisible(node.id),"Known node inside camera frustum is visible while paused");
            Check(Base.VisibleResourceDecals<Base.Nodes.Count,"Off-camera nodes are culled");
            Check(!world.GetComponentsInChildren<Transform>(true).Any(t=>Enum.GetNames(typeof(NodeKind)).Contains(t.name)),"Resource cylinders and per-node render objects are absent");
            float remaining=node.remaining;node.remaining=0;Base.Rebase(View);Check(!Base.ResourceDecalVisible(node.id),"Depleted node hides immediately while paused");node.remaining=remaining;
            rig.FocusSector(field.Canonical(node.position+Vector2.one*500));Base.Rebase(View);Check(!Base.ResourceDecalVisible(node.id),"Camera movement culls a formerly visible node while paused");
            var unknown=Base.Nodes.FirstOrDefault(n=>!Battle.IsVisible(n.position,0,true));
            if(unknown!=null){rig.FocusSector(unknown.position);Base.Rebase(View);Check(!Base.ResourceDecalVisible(unknown.id),"Unexplored resources remain hidden even inside the camera frustum");}
            foreach(var n in Base.Nodes)foreach(var d in Base.Catalog.buildings.Where(d=>d.terrain==TerrainRule.Node&&d.nodes.Contains(n.kind)))
                Check(field.Distance(n.position,Base.SnapPlacement(d,n.position))<.001f,"Live extractor aligns with resource: "+n.kind);
            rig.FocusBase(node.position);Base.Rebase(View);yield return null;
            // Render through the actual HDRP terrain pass to catch missing shader bindings/textures.
            var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGBHalf);target.Create();var previous=View.targetTexture;bool wasEnabled=View.enabled;View.enabled=false;View.targetTexture=target;
            field.SetResourceDecalVisibility(0);
            yield return null;
            RenderPipeline.SubmitRenderRequest(View,new RenderPipeline.StandardRequest{destination=target});
            var plain=AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32);plain.WaitForCompletion();var before=plain.GetData<Color32>().ToArray();
            var image=new Texture2D(512,512,TextureFormat.RGBA32,false);image.SetPixels32(before);image.Apply();File.WriteAllBytes(folder+"/terrain-without-decals.png",image.EncodeToPNG());
            field.SetResourceDecalVisibility(Base.VisibleResourceDecals);
            yield return null;
            RenderPipeline.SubmitRenderRequest(View,new RenderPipeline.StandardRequest{destination=target});
            var read=AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32);read.WaitForCompletion();Check(!read.hasError,"Decal terrain pass renders and completes GPU readback");
            var after=read.GetData<Color32>().ToArray();int changed=0;for(int i=0;i<before.Length;i++)if(Math.Abs(before[i].r-after[i].r)+Math.Abs(before[i].g-after[i].g)+Math.Abs(before[i].b-after[i].b)>8)changed++;
            Check(changed>0&&changed<before.Length*.15f,"Decal rendering changes only a small resource patch of the terrain");
            image.SetPixels32(after);image.Apply();File.WriteAllBytes(folder+"/terrain-with-decals.png",image.EncodeToPNG());ArmyLifetime.Destroy(image);
            View.targetTexture=previous;View.enabled=wasEnabled;target.Release();ArmyLifetime.Destroy(target);
            File.WriteAllText(folder+"/runtime-validation.json",JsonUtility.ToJson(new ResourceDecalReport{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray()},true));
            rig.FocusBase(Base.BasePosition(0));
        }
        [Serializable] sealed class ResourceDecalReport {public string result;public int checks;public string[] failures;}
    }
}
