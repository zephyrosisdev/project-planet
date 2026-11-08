using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary {
    [Serializable] public class ColonyPrototypeMesh {
        public float[] vertices,normals,colours;public int[] triangles;
        [NonSerialized] Mesh cached;
        // Unity may destroy runtime meshes when leaving Play mode while static data survives.
        // Retain the same rebuildable geometry as imported catalogue entries, not just a cache.
        internal void SetMesh(Mesh mesh){
            var points=mesh.vertices;var ns=mesh.normals;var colors=mesh.colors;
            vertices=new float[points.Length*3];normals=new float[points.Length*3];colours=new float[points.Length*4];triangles=mesh.triangles;
            for(int i=0;i<points.Length;i++){vertices[i*3]=points[i].x;vertices[i*3+1]=points[i].y;vertices[i*3+2]=points[i].z;var normal=ns.Length==points.Length?ns[i]:Vector3.up;normals[i*3]=normal.x;normals[i*3+1]=normal.y;normals[i*3+2]=normal.z;var color=colors.Length==points.Length?colors[i]:Color.white;colours[i*4]=color.r;colours[i*4+1]=color.g;colours[i*4+2]=color.b;colours[i*4+3]=color.a;}
            cached=mesh;
        }
        internal void ShareGeometry(ColonyPrototypeMesh source){vertices=source.vertices;normals=source.normals;colours=source.colours;triangles=source.triangles;cached=source.cached;}
        public Mesh Mesh(string name){if(cached!=null)return cached;var points=new Vector3[vertices.Length/3];var ns=new Vector3[points.Length];var colors=new Color[points.Length];for(int i=0;i<points.Length;i++){points[i]=new Vector3(vertices[i*3],vertices[i*3+1],vertices[i*3+2]);ns[i]=new Vector3(normals[i*3],normals[i*3+1],normals[i*3+2]);colors[i]=new Color(colours[i*4],colours[i*4+1],colours[i*4+2],colours[i*4+3]);}cached=new Mesh{name=name,indexFormat=IndexFormat.UInt32};cached.vertices=points;cached.normals=ns;cached.colors=colors;cached.triangles=triangles;cached.RecalculateBounds();return cached;}
    }
    [Serializable] public sealed class ColonyPrototypeSolid {public Vector2[] polygon;public float bottom,top;internal BuildSolid Shape()=>new BuildSolid(polygon,bottom,top);}
    [Serializable] public sealed class ColonyPrototypeSocket {public string name,kind;public Vector3 position;public float yaw,width,depth;}
    [Serializable] public sealed class ColonyPrototypeAsset:ColonyPrototypeMesh {
        public string id,family;public Vector3 dimensions;public ColonyPrototypeSolid[] solids;public ColonyPrototypeSocket[] sockets;public ColonyPrototypeMesh lod;
        [NonSerialized] Mesh collision;
        public Mesh Collision(){if(collision!=null)return collision;var geometry=new ModularGeometry();foreach(var solid in solids)PlayerBuildingWorld.Prism(solid.Shape(),Vector3.zero,Quaternion.identity,Color.white,geometry);collision=geometry.Build(id+" walking collision");return collision;}
    }
    [Serializable] public sealed class ColonyPrototypeCatalogue {public int version;public ColonyPrototypeAsset[] assets;}
    public static class ColonyPrototypeLibrary {
        [Serializable] sealed class CollisionAsset {public string id;public Vector3 dimensions;public ColonyPrototypeSolid[] solids;public ColonyPrototypeSocket[] sockets;[NonSerialized] public ColonyPrototypeAsset profile;}
        [Serializable] sealed class CollisionCatalogue {public int version;public CollisionAsset[] assets;}
        static Dictionary<string,CollisionAsset> collisionData;
        // A collision-only DTO deliberately ignores render vertex, normal, colour and triangle arrays.
        public static ColonyPrototypeSolid[] StoredCollision(string id){
            if(collisionData==null){
                var source=Resources.Load<TextAsset>("MassiveArmy/ColonyPrototypes/Catalogue");if(source==null)return null;CollisionCatalogue data;
                try{data=JsonUtility.FromJson<CollisionCatalogue>(source.text);}finally{Resources.UnloadAsset(source);}
                if(data.version!=1)throw new InvalidOperationException("Unsupported colony collision catalogue");collisionData=new Dictionary<string,CollisionAsset>();foreach(var asset in data.assets)collisionData.Add(asset.id,asset);
            }
            return collisionData.TryGetValue(id,out var value)?value.solids:null;
        }
        // CPU metadata only. Render arrays and generated render fallbacks are never read.
        internal static ColonyPrototypeAsset StoredProfile(string id){
            if(StoredCollision(id)==null||!collisionData.TryGetValue(id,out var data))return null;if(data.profile!=null)return data.profile;
            var sockets=new List<ColonyPrototypeSocket>(data.sockets??Array.Empty<ColonyPrototypeSocket>());
            if(id=="room-WorkshopRoom"||id=="room-WarehouseRoom"){sockets.Add(new ColonyPrototypeSocket{name="Logistics input",kind="ConveyorInput",position=new Vector3(0,1,data.dimensions.z*.5f)});sockets.Add(new ColonyPrototypeSocket{name="Logistics output",kind="ConveyorOutput",position=new Vector3(0,1,-data.dimensions.z*.5f),yaw=180});}
            return data.profile=new ColonyPrototypeAsset{id=id,dimensions=data.dimensions,solids=data.solids,sockets=sockets.ToArray()};
        }
        static Dictionary<string,ColonyPrototypeAsset> assets;
        public static ColonyPrototypeAsset Find(string id){if(assets==null){var source=Resources.Load<TextAsset>("MassiveArmy/ColonyPrototypes/Catalogue");if(source==null)return null;var data=JsonUtility.FromJson<ColonyPrototypeCatalogue>(source.text);Resources.UnloadAsset(source);if(data.version!=1)throw new InvalidOperationException("Unsupported colony prototype catalogue");assets=new Dictionary<string,ColonyPrototypeAsset>();foreach(var asset in data.assets){if(asset.id=="room-WorkshopRoom"||asset.id=="room-WarehouseRoom"){var sockets=new List<ColonyPrototypeSocket>(asset.sockets);sockets.Add(new ColonyPrototypeSocket{name="Logistics input",kind="ConveyorInput",position=new Vector3(0,1,asset.dimensions.z*.5f)});sockets.Add(new ColonyPrototypeSocket{name="Logistics output",kind="ConveyorOutput",position=new Vector3(0,1,-asset.dimensions.z*.5f),yaw=180});asset.sockets=sockets.ToArray();}assets.Add(asset.id,asset);}}if(assets.TryGetValue(id,out var value))return value;value=RenewablePrototypes.Create(id)??IndustrialVehiclePrototypes.Create(id)??UtilityPrototypes.Create(id);if(value!=null)assets.Add(id,value);return value;}
        public static Material Material(string name)=>new Material(Resources.Load<Shader>("MassiveArmy/ColonyPrototypes/ColonyPrototype")){name=name,enableInstancing=true};
    }
}
