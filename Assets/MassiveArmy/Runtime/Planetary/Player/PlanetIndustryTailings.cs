using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    internal sealed partial class PlanetIndustry {
        Mesh tailingsMoundMesh;Material tailingsMaterial;readonly List<Matrix4x4> tailingsMounds=new List<Matrix4x4>(),tailingsChutes=new List<Matrix4x4>();
        internal static Mesh CreateTailingsMoundMesh(){var g=new ModularGeometry();for(int i=0;i<16;i++){float a=i*Mathf.PI*2/16,b=(i+1)*Mathf.PI*2/16;var p=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var q=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));g.Triangle(p,Vector3.up*.7f,q,Color.white);}return g.Build("Shared front tailings mound");}
        void RenderTailings(Bounds bounds){
            if(!Authority.World.miningWorkflow)return;tailingsMoundMesh??=CreateTailingsMoundMesh();tailingsMaterial??=Mat(Shader.Find("HDRP/Lit")??Shader.Find("Standard"),new Color(.32f,.3f,.26f));tailingsMounds.Clear();tailingsChutes.Clear();
            foreach(var sluice in Authority.World.machines){if(sluice.definition!="sluice"||sluice.pose.body!=actor.pose.body||sluice.pose.Distance(actor.pose)>225)continue;var q=Rotation(sluice.pose);var start=world.RenderBodyPosition(Body(sluice.pose))+q*new Vector3(1,.6f,-2);var outlet=IndustrialWorld.TailingsPose(sluice);var end=world.RenderBodyPosition(Body(outlet))+q*Vector3.up*.05f;var delta=end-start;if(delta.sqrMagnitude>.01f)tailingsChutes.Add(Matrix4x4.TRS((start+end)*.5f,Quaternion.LookRotation(delta,q*Vector3.up),new Vector3(.5f,.12f,delta.magnitude)));
                tailingsChutes.Add(Matrix4x4.TRS(world.RenderBodyPosition(Body(outlet))+q*Vector3.up*.01f,q,new Vector3(3.8f,.02f,3.8f)));long volume=IndustrialWorld.TailingsCount(sluice.frontTailings);if(volume<=0)continue;float radius=(float)IndustrialWorld.TailingsRadius(volume);tailingsMounds.Add(Matrix4x4.TRS(world.RenderBodyPosition(Body(outlet)),q,Vector3.one*radius));
            }
            void Batch(List<Matrix4x4> batch,Mesh mesh){for(int offset=0;offset<batch.Count;offset+=instances.Length){int count=Math.Min(instances.Length,batch.Count-offset);batch.CopyTo(offset,instances,0,count);Graphics.RenderMeshInstanced(new RenderParams(tailingsMaterial){camera=world.View,worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off},mesh,0,instances,count);}}
            Batch(tailingsMounds,tailingsMoundMesh);Batch(tailingsChutes,cube);
        }
        void DisposeTailings(){ArmyLifetime.Destroy(tailingsMoundMesh);ArmyLifetime.Destroy(tailingsMaterial);}
    }
}
