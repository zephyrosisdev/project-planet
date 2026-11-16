using System;
using System.Collections.Generic;
using MassiveArmy.Networking;
using UnityEngine;
namespace MassiveArmy.Planetary {
    // A handful of static camp props on graphical clients only. Headless contact
    // geometry comes from SurvivalCampGeometry, never from render objects.
    internal sealed class SurvivalCampView:IDisposable {
        readonly PlanetDevelopment world;readonly SurvivalCampPublic camp;readonly GameObject root;readonly Material[] materials;readonly List<Mesh> meshes=new List<Mesh>();
        internal SurvivalCampView(PlanetDevelopment world,SurvivalCampPublic camp){this.world=world;this.camp=camp;root=new GameObject(camp.name);root.transform.SetParent(world.transform,false);materials=new Material[4];var colors=new[]{new Color(.14f,.19f,.23f),new Color(.19f,.35f,.4f),new Color(.95f,.55f,.12f),new Color(.045f,.055f,.065f)};for(int i=0;i<materials.Length;i++){materials[i]=new Material(Shader.Find("HDRP/Lit"));materials[i].SetColor("_BaseColor",colors[i]);materials[i].SetFloat("_Smoothness",.15f);}
            foreach(var part in SurvivalCampGeometry.Parts(camp)){var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Camp shop fitting";cube.transform.SetParent(root.transform,false);cube.transform.localPosition=part.center;cube.transform.localScale=part.size;cube.GetComponent<MeshRenderer>().sharedMaterial=materials[part.material];}
            foreach(var pad in camp.pads)BuildPad(pad.pose);Present();
        }
        void BuildPad(MassiveArmy.Industrial.FramePose pad){var surface=world.CampSurface;var origin=SurvivalCampGeneration.Point(camp.pose);var inverse=Quaternion.Inverse(SurvivalCampGeneration.Rotation(camp.pose));var center=SurvivalCampGeneration.Point(pad);var rotation=SurvivalCampGeneration.Rotation(pad);
            Vector3 Point(float x,float z,double height){var at=surface.ProjectToSurface(center+Double3.Rotate(rotation,new Double3(x,0,z)),height);return Double3.Rotate(inverse,at-origin).Float;}
            var vertices=new List<Vector3>();var triangles=new List<int>();void Quad(float x0,float x1,float z0,float z1,double height){int at=vertices.Count;vertices.Add(Point(x0,z0,height));vertices.Add(Point(x1,z0,height));vertices.Add(Point(x0,z1,height));vertices.Add(Point(x1,z1,height));triangles.AddRange(new[]{at,at+2,at+1,at+1,at+2,at+3});}
            void Flush(string name,int material){var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);var go=new GameObject(name);go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=materials[material];vertices.Clear();triangles.Clear();}
            for(int x=0;x<12;x++)for(int z=0;z<14;z++)Quad(-60+x*10,-50+x*10,-70+z*10,-60+z*10,.025);Flush("Terrain conforming landing and launch pad",3);
            for(int i=0;i<14;i++){Quad(-60,-58,-70+i*10,-60+i*10,.06);Quad(58,60,-70+i*10,-60+i*10,.06);}for(int i=0;i<12;i++){Quad(-60+i*10,-50+i*10,-70,-68,.06);Quad(-60+i*10,-50+i*10,68,70,.06);}for(int i=0;i<4;i++){Quad(-17,-14,-20+i*10,-10+i*10,.06);Quad(14,17,-20+i*10,-10+i*10,.06);}Quad(-14,14,-1.5f,1.5f,.06);Flush("Landing pad markings",2);
        }
        internal void Present(){root.transform.SetPositionAndRotation(world.RenderBodyPosition(SurvivalCampGeneration.Point(camp.pose)),SurvivalCampGeneration.Rotation(camp.pose));}
        public void Dispose(){ArmyLifetime.Destroy(root);foreach(var mesh in meshes)ArmyLifetime.Destroy(mesh);foreach(var material in materials)ArmyLifetime.Destroy(material);}
    }
}

