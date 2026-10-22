using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassiveArmy
{
    public sealed class RtsModelArt : ScriptableObject
    {
        [Serializable] public sealed class Building
        {
            public string id;
            public Mesh near,far,gateLeaf;
            public Mesh distant;
            public bool customLods;
            public Mesh[] walls;
            public Material material;
        }
        public Building[] buildings=Array.Empty<Building>();
        public string[] unitIds=Array.Empty<string>();
        public TextAsset[] unitNear,unitFar;
        public Texture2DArray albedo,metallic,normal,impostors;
        public Vector4[] unitBounds;
        Dictionary<string,Building> lookup;
        public static RtsModelArt Load(){var selection=Resources.Load<RtsModelSelection>("MassiveArmy/ModelArt/Selection");return selection!=null&&selection.catalogue!=null?selection.catalogue:Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue");}
        public static float FoundationHeight(Battlefield field,BuildingDefinition definition,Vector2 position,int rotation)
        {
            float x=definition.footprintWidth*8*.45f,z=definition.footprintDepth*8*.45f;
            if((rotation&1)!=0){float swap=x;x=z;z=swap;}
            float height=field.Height(position);
            for(int i=0;i<4;i++)height=Mathf.Max(height,field.Height(position+new Vector2((i&1)==0?-x:x,(i&2)==0?-z:z)));
            return height-.4f;
        }
        public Building Find(string id)
        {
            if(lookup==null){lookup=new Dictionary<string,Building>();foreach(var b in buildings)lookup.Add(b.id,b);}
            return lookup.TryGetValue(id,out var value)?value:null;
        }
        public Building FindBuilding(BuildingDefinition definition)
        {
            var own=Find(definition.id);if(own!=null)return own;
            var fallback=Find(definition.mesh);
            // Shared default geometry is not an override of every definition that uses it.
            // Resolve old baked packs too, without requiring the player to bake them again.
            return fallback?.customLods==true?Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue")?.Find(definition.mesh):fallback;
        }
    }
}
