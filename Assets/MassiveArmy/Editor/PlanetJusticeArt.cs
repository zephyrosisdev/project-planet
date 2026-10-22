using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class PlanetJusticeArt {
        [MenuItem("Massive Army/Art/Create justice and market buildings")]
        public static void Build(){ModularBuildingWindow.CreateProfiles();var ids=new[]{"civil_county_jail","civil_courthouse","civil_federal_prison","civil_prison_wing","civil_prison_yard","civil_customs","civil_distillery","civil_chemical_plant","civil_off_licence"};var profiles=new ModularBuildingProfile[ids.Length];
            for(int i=0;i<ids.Length;i++){var p=AssetDatabase.LoadAssetAtPath<ModularBuildingProfile>(ModularBuildingWindow.Root+"/Profiles/"+ids[i]+".asset");profiles[i]=p;p.enabledForGeneration=true;bool prison=ids[i]=="civil_federal_prison"||ids[i]=="civil_prison_wing"||ids[i]=="civil_county_jail",yard=ids[i]=="civil_prison_yard",industrial=ids[i]=="civil_distillery"||ids[i]=="civil_chemical_plant";p.use=prison?ModularUse.Prison:industrial?ModularUse.Workshop:ModularUse.Office;p.floors=prison||ids[i]=="civil_courthouse"?2:1;p.pitchedRoof=false;p.gardenWalls=prison||yard;p.securePerimeter=p.gardenWalls;p.gardenHeight=3.4f;p.floorHeight=3.2f;
                p.width=ids[i]=="civil_federal_prison"?28:ids[i]=="civil_prison_wing"?22:prison?24:yard?12:ids[i]=="civil_off_licence"?10:24;p.depth=ids[i]=="civil_federal_prison"?30:ids[i]=="civil_prison_wing"?26:prison?26:yard?14:ids[i]=="civil_off_licence"?12:24;p.gardenSize=ids[i]=="civil_federal_prison"?new Vector2(52,52):new Vector2(40,40);p.exterior=prison?new Color(.46f,.49f,.51f):industrial?new Color(.53f,.5f,.43f):new Color(.66f,.63f,.55f);
                if(ids[i]=="civil_courthouse")p.rooms=new[]{new ModularRoomSettings{name="Courtroom"},new ModularRoomSettings{name="Witness waiting"},new ModularRoomSettings{name="Legal aid"},new ModularRoomSettings{name="Court records"}};
                if(yard)p.rooms=new[]{new ModularRoomSettings{name="Recreation equipment"},new ModularRoomSettings{name="Education / rehabilitation"},new ModularRoomSettings{name="Medical support"},new ModularRoomSettings{name="Yard staff"}};
                EditorUtility.SetDirty(p);
            }AssetDatabase.SaveAssets();ModularBuildingWindow.BakeAll(false,null,profiles);
        }
    }
}
