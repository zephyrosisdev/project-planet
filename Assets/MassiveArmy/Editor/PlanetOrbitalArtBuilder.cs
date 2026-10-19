using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class PlanetOrbitalArtBuilder {
        [MenuItem("Massive Army/Planetary/Build Launch Pad Art")]
        public static void Build(){
            const string folder="Assets/MassiveArmy/Resources/MassiveArmy/ModelArt/Orbitals";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);var cube=primitive.GetComponent<MeshFilter>().sharedMesh;var pieces=new List<CombineInstance>();
            void Box(Vector3 centre,Vector3 size)=>pieces.Add(new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(centre,Quaternion.identity,size)});
            Box(new Vector3(0,.6f,0),new Vector3(47,1.2f,47));Box(new Vector3(0,1.5f,0),new Vector3(18,1.8f,18));
            for(int i=0;i<4;i++){float x=(i&1)==0?-9:9,z=(i&2)==0?-8:8;Box(new Vector3(x,4,z),new Vector3(2,6,2));}
            Box(new Vector3(0,11,0),new Vector3(4,18,4));Box(new Vector3(0,21,0),new Vector3(2.8f,2,2.8f));Box(new Vector3(0,22.5f,0),new Vector3(1.4f,1,1.4f));
            Box(new Vector3(12,13,0),new Vector3(3,24,3));Box(new Vector3(8,21,0),new Vector3(8,2,3));Box(new Vector3(-17,3,15),new Vector3(8,5,9));
            var mesh=new Mesh{name="Launch pad gantry and payload"};mesh.CombineMeshes(pieces.ToArray(),true,true);Object.DestroyImmediate(primitive);
            Mesh SaveMesh(string path,Mesh value){var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);Object.DestroyImmediate(value);return old;}
            var near=SaveMesh(folder+"/LaunchPad.asset",mesh);var material=AssetDatabase.LoadAssetAtPath<Material>(folder+"/LaunchPad.mat");if(material==null){material=new Material(Shader.Find("HDRP/Lit")){name="Launch complex steel"};material.SetColor("_BaseColor",new Color(.36f,.4f,.44f));material.SetFloat("_Metallic",.3f);AssetDatabase.CreateAsset(material,folder+"/LaunchPad.mat");}
            var art=AssetDatabase.LoadAssetAtPath<RtsModelArt>("Assets/MassiveArmy/Resources/MassiveArmy/ModelArt/Catalogue.asset");var list=new List<RtsModelArt.Building>(art.buildings);list.RemoveAll(b=>b.id=="civil_launch_pad");list.Add(new RtsModelArt.Building{id="civil_launch_pad",near=near,far=near,material=material});art.buildings=list.ToArray();EditorUtility.SetDirty(art);AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset("Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/ShaderGraph/Water.shadergraph",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            File.WriteAllText("TestResults/Planetary/Orbitals/art.txt","Launch pad geometry saved; HDRP water graph reimported");
        }
    }
}
