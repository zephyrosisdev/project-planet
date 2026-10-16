using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    public sealed class RtsPresentation : IDisposable
    {
        readonly RtsWorld world;
        readonly RtsModelArt modelArt;
        readonly MaterialPropertyBlock teamProperties=new MaterialPropertyBlock();
        readonly GameObject root;
        readonly Material hull,trim,enemyTrim;
        readonly Dictionary<string,Mesh> meshes=new Dictionary<string,Mesh>();
        readonly Dictionary<Mesh,Mesh> turretMeshes=new Dictionary<Mesh,Mesh>();
        ResourceNodeDecals resourceDecals;
        readonly Dictionary<int,bool> known=new Dictionary<int,bool>();
        static Mesh cube,cylinder;
        public RtsPresentation(RtsWorld world,Transform parent)
        {
            this.world=world;modelArt=Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue");root=new GameObject("RTS base infrastructure");root.transform.SetParent(parent,false);
            Shader shader=Resources.Load<Shader>("MassiveArmy/Structure");
            hull=Material(shader,new Color(.075f,.12f,.17f));trim=Material(shader,new Color(.02f,1.1f,1.8f));enemyTrim=Material(shader,new Color(1.7f,.025f,.2f));
            if(cube==null){cube=Primitive(PrimitiveType.Cube);cylinder=Primitive(PrimitiveType.Cylinder);}

        }
        static Material Material(Shader shader,Color color){var m=new Material(shader);m.SetColor("_Tint",color);return m;}
        static Mesh Primitive(PrimitiveType type){var go=GameObject.CreatePrimitive(type);Mesh mesh=go.GetComponent<MeshFilter>().sharedMesh;ArmyLifetime.Destroy(go);return mesh;}
        GameObject MeshObject(string name,Transform parent,Mesh mesh,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;return go;
        }
        public void CreateBuilding(RtsBuilding b)
        {
            world.Battle.ResetStructureAim(b.index);
            var go=new GameObject(b.definition.name);go.transform.SetParent(root.transform,false);b.visual=go;
            float h=world.Field.Height(b.position);for(int k=0;k<4;k++){Vector2 offset=new Vector2((k%2==0?-1:1)*b.definition.radius*.7f,(k<2?-1:1)*b.definition.radius*.7f);h=Mathf.Max(h,world.Field.Height(b.position+offset));}
            go.transform.position=new Vector3(b.position.x,h+1,b.position.y);go.transform.rotation=Quaternion.Euler(0,b.rotation*90,0);UpgradeBuilding(b);
            if(modelArt?.Find(b.definition.id)!=null)go.transform.position=new Vector3(b.position.x,RtsModelArt.FoundationHeight(world.Field,b.definition,b.position,b.rotation),b.position.y);
        }
        Mesh Model(BuildingDefinition d,int level,bool lights,bool far,int connections=0)
        {
            string key=d.id+":"+level+":"+lights+":"+far+":"+connections;if(meshes.TryGetValue(key,out var cached))return cached;
            var parts=new List<CombineInstance>();float r=d.radius;
            void Box(float x,float y,float z,float sx,float sy,float sz,float yaw=0)=>parts.Add(new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(new Vector3(x,y,z),Quaternion.Euler(0,yaw,0),new Vector3(sx,sy,sz))});
            void Tube(float x,float y,float z,float sx,float sy,float sz)=>parts.Add(new CombineInstance{mesh=cylinder,transform=Matrix4x4.TRS(new Vector3(x,y,z),Quaternion.identity,new Vector3(sx,sy,sz))});
            if(lights){Box(0,2,0,r*1.8f,.5f,r*1.8f);Box(0,7+level*2,0,r*.65f,.5f,r*.65f);for(int k=0;k<level;k++)Box(-r*.7f+k*3,3,r*.92f,1,2,.4f);}
            else {
                Box(0,0,0,r*1.9f,3,r*1.9f);
                switch(d.category){
                    case BuildingCategory.Core:Box(0,5,0,r*1.3f,8,r*1.3f);Box(0,14,0,r*.6f,14,r*.6f);Box(0,25,0,1,12,1);break;
                    case BuildingCategory.Production:Box(0,5,0,r*1.5f,8,r*1.6f);for(int k=0;k<level;k++)Box(-r*.55f+k*r*.55f,3,r*.7f,r*.4f,5,r*.75f);if(d.id=="airbase"||d.id=="helipad")Box(0,2,0,r*1.8f,.4f,r*1.8f);if(d.id=="naval_yard")Box(r*.7f,14,0,2,25,2);break;
                    case BuildingCategory.Research:Tube(0,7,0,r*1.3f,7,r*1.3f);for(int k=0;k<level+1;k++)Box((k-1)*5,17+k*2,0,1,15+k*4,1);break;
                    case BuildingCategory.Power:
                        if(d.id=="wind"){Tube(0,14,0,2,14,2);Box(0,28,0,r*2,.8f,1,25);Box(0,28,0,1,r*2,.8f);}
                        else if(d.id=="solar"){for(int k=0;k<3;k++)Box((k-1)*r*.55f,4,0,r*.5f,1,r*1.6f);}
                        else{Tube(0,8,0,r*1.2f,8,r*1.2f);Tube(r*.6f,13,0,r*.3f,13,r*.3f);}break;
                    case BuildingCategory.Economy:
                        if(d.terrain==TerrainRule.Node){Box(0,3,0,r*1.4f,4,r*1.4f);Box(-r*.5f,12,0,2,20,2);Box(r*.5f,12,0,2,20,2);Box(0,22,0,r*1.2f,2,2);}
                        else Tube(0,7,0,r*1.3f,7,r*1.3f);break;
                    case BuildingCategory.Defence:
                        if(d.id=="wall"){
                            parts.Clear();Box(0,4,0,2,8,2);
                            int mask=connections<=0?5:connections;
                            if((mask&1)!=0)Box(2,4,0,4,8,2);
                            if((mask&2)!=0)Box(0,4,2,2,8,4);
                            if((mask&4)!=0)Box(-2,4,0,4,8,2);
                            if((mask&8)!=0)Box(0,4,-2,2,8,4);
                        }
                        else if(d.id=="gate"){parts.Clear();Box(-10,4,0,4,8,4);Box(10,4,0,4,8,4);}
                        else{Tube(0,4,0,r*1.5f,3,r*1.5f);Box(0,8,0,r*.8f,4,r*.8f);Box(0,8,r*.65f,2,2,r*1.5f);}break;
                    default:Box(0,5,0,r*1.5f,7,r*1.5f);Tube(0,14,0,r*.55f,7,r*.55f);break;
                }
                if(!far&&d.detection>0){Tube(0,21,0,r*.9f,1,r*.9f);Box(0,14,0,1,14,1);}
                if(!far&&level>1)for(int k=0;k<level-1;k++)Box(r*.75f,6,-r*.5f+k*5,4,9,4);
            }
            var mesh=new Mesh{name=key,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(parts.ToArray(),true,true);
            if(d.id!="wall"){
                var vertices=mesh.vertices;var bounds=mesh.bounds;
                float sx=d.footprintWidth*8*.94f/Mathf.Max(.01f,bounds.size.x),sz=d.footprintDepth*8*.94f/Mathf.Max(.01f,bounds.size.z);
                for(int i=0;i<vertices.Length;i++){vertices[i].x=(vertices[i].x-bounds.center.x)*sx;vertices[i].z=(vertices[i].z-bounds.center.z)*sz;}
                mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
            }
            meshes.Add(key,mesh);return mesh;
        }
        public void UpgradeBuilding(RtsBuilding b)
        {
            if(b.visual==null)return;for(int i=b.visual.transform.childCount-1;i>=0;i--)ArmyLifetime.Destroy(b.visual.transform.GetChild(i).gameObject);
            var art=modelArt?.Find(b.definition.id);
            if(art!=null){
                Mesh near=art.walls!=null&&art.walls.Length==16?art.walls[b.wallConnections<1?5:b.wallConnections]:art.near;
                bool turret=BuildingTurretGeometry.Articulated(b.definition);
                Mesh Joint(Mesh source){if(!turret)return source;if(!turretMeshes.TryGetValue(source,out var posed)){posed=BuildingTurretGeometry.WithJoints(source);turretMeshes.Add(source,posed);}return posed;}
                near=Joint(near);
                var closeArt=MeshObject("UV armoured shell",b.visual.transform,near,art.material);
                var farArt=MeshObject("UV distant shell",b.visual.transform,art.walls!=null&&art.walls.Length==16?near:Joint(art.far),art.material);
                var lodArt=b.visual.GetComponent<LODGroup>();if(lodArt==null)lodArt=b.visual.AddComponent<LODGroup>();lodArt.SetLODs(new[]{new LOD(.025f,new[]{closeArt.GetComponent<Renderer>()}),new LOD(.002f,new[]{farArt.GetComponent<Renderer>()})});lodArt.RecalculateBounds();
                if(art.gateLeaf!=null)MeshObject("Gate leaf",b.visual.transform,art.gateLeaf,art.material);
                teamProperties.SetColor("_TeamColor",b.team==0?new Color(.02f,.72f,1):new Color(1,.035f,.12f));
                teamProperties.SetBuffer("_StructureAim",world.Battle.StructureAimBuffer);teamProperties.SetInt("_BuildingIndex",b.index);teamProperties.SetFloat("_HasTurret",turret?1:0);teamProperties.SetFloat("_BuildingYaw",b.rotation*Mathf.PI*.5f);
                foreach(var renderer in b.visual.GetComponentsInChildren<Renderer>())renderer.SetPropertyBlock(teamProperties);
                SetGate(b);return;
            }
            var close=MeshObject("Armoured core and foundation",b.visual.transform,Model(b.definition,b.level,false,false,b.wallConnections),hull);
            var neon=MeshObject("Technology markings",b.visual.transform,Model(b.definition,b.level,true,false,b.wallConnections),b.team==0?trim:enemyTrim);
            var distant=MeshObject("Distant silhouette",b.visual.transform,Model(b.definition,b.level,false,true,b.wallConnections),hull);
            var lod=b.visual.GetComponent<LODGroup>();if(lod==null)lod=b.visual.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.025f,new[]{close.GetComponent<Renderer>(),neon.GetComponent<Renderer>()}),new LOD(.002f,new[]{distant.GetComponent<Renderer>()})});lod.RecalculateBounds();
            if(b.definition.id=="gate"){
                var leaf=MeshObject("Gate leaf",b.visual.transform,cube,hull);leaf.transform.localPosition=new Vector3(0,4,0);leaf.transform.localScale=new Vector3(16,8,2);SetGate(b);
            }
        }
        public void SetGate(RtsBuilding b){
            var leaf=b.visual!=null?b.visual.transform.Find("Gate leaf"):null;
            if(leaf!=null&&modelArt?.Find(b.definition.id)?.gateLeaf!=null){leaf.localScale=new Vector3(1,b.gateOpen?.015f:1,1);return;}
            if(leaf!=null){leaf.localScale=new Vector3(16,b.gateOpen?.1f:8,2);leaf.localPosition=new Vector3(0,b.gateOpen?.05f:4,0);}
        }
        public void CreateNodes()=>resourceDecals=new ResourceNodeDecals(world);
        public void RefreshTerrain(Bounds region)
        {
            foreach(var b in world.Buildings)if(!b.removed&&b.visual!=null){
                Vector2 delta=world.Field.Delta(new Vector2(region.center.x,region.center.z),b.position);
                if(Mathf.Abs(delta.x)>region.extents.x+b.definition.radius||Mathf.Abs(delta.y)>region.extents.z+b.definition.radius)continue;
                float height=RtsModelArt.FoundationHeight(world.Field,b.definition,b.position,b.rotation);
                if(modelArt?.Find(b.definition.id)==null){height=world.Field.Height(b.position);for(int k=0;k<4;k++){Vector2 offset=new Vector2((k%2==0?-1:1)*b.definition.radius*.7f,(k<2?-1:1)*b.definition.radius*.7f);height=Mathf.Max(height,world.Field.Height(b.position+offset));}height+=1;}
                var position=b.visual.transform.position;position.y=height;b.visual.transform.position=position;
            }
        }
        internal int VisibleResourceDecals=>resourceDecals?.VisibleCount??0;
        internal bool ResourceDecalVisible(int id)=>resourceDecals?.Visible(id)??false;
        public void Rebase(Camera camera)
        {
            var rig=camera.GetComponent<RtsCamera>();if(rig==null)return;
            resourceDecals?.Update(camera);
            foreach(var b in world.Buildings)if(!b.removed&&b.visual!=null){Vector3 p=b.visual.transform.position;p.x=b.position.x;p.z=b.position.y;b.visual.transform.position=rig.Nearest(p);}
        }
        public void Update(float dt)
        {
            foreach(var b in world.Buildings)if(!b.removed&&b.visual!=null){
                bool visible=b.team==0||world.Battle.IsVisible(b.position);if(visible)known[b.index]=true;
                b.visual.SetActive(visible||(known.TryGetValue(b.index,out bool seen)&&seen));
                b.visual.transform.localScale=new Vector3(1,b.Complete?1:Mathf.Lerp(.15f,1,b.construction/b.definition.time),1);
            }
        }
        public void DestroyBuilding(RtsBuilding b,bool effects)
        {
            known.Remove(b.index);if(effects&&(b.team==0||world.Battle.IsVisible(b.position)))world.Battle.ReportStructureDestruction(b.visual.transform.position,Mathf.Max(b.definition.footprintWidth,b.definition.footprintDepth)*BuildingGrid.TileSize);
            ArmyLifetime.Destroy(b.visual);b.visual=null;
        }
        public void Dispose(){resourceDecals?.Dispose();foreach(var mesh in meshes.Values)ArmyLifetime.Destroy(mesh);foreach(var mesh in turretMeshes.Values)ArmyLifetime.Destroy(mesh);foreach(var m in new[]{hull,trim,enemyTrim})ArmyLifetime.Destroy(m);ArmyLifetime.Destroy(root);}
    }
}
