using UnityEngine;

namespace MassiveArmy.Planetary {

    public sealed partial class PlanetDevelopment {

        [Header("Player modular building")]

        [Range(1,8192)]public int maximumPlayerBuildingModules=8192;

        [Range(1,64)]public int playerBuildingColliderChunks=32;

        [Min(25)]public float playerBuildingDetailDistance=250;

        PlayerBuildingWorld playerConstruction;PlayerBuildStorage playerBuildLandState;

        internal string BlueprintPlacementPrompt=>playerConstruction?.PlacementPrompt??"";
        internal bool PlayerBlueprintRadialOpen=>playerConstruction?.RadialOpen==true;

        internal bool PlayerBuildCellReserved(PlanetNavCoordinate cell){var data=playerBuildLandState??(playerBuildLandState=BuildStorage);if(data?.playerBuildLand==null||data.playerBuildLand.Count==0)return false;if(data.playerBuildLandIndex==null){data.playerBuildLandIndex=new System.Collections.Generic.HashSet<long>();foreach(var c in data.playerBuildLand)data.playerBuildLandIndex.Add(SettlementLandUse.Key(c));}return data.playerBuildLandIndex.Contains(SettlementLandUse.Key(cell));}

        void PreparePlayerBuilding(){if((!IsSurvival&&Base?.Civilisation==null)||player==null||IsSurvival&&IndustrialAuthority==null)return;var data=playerBuildLandState??(playerBuildLandState=BuildStorage);if(playerConstruction==null&&data?.playerBuildSites?.Count>0)playerConstruction=new PlayerBuildingWorld(this,surface);playerConstruction?.PrepareCollision();}

        void UpdatePlayerBuilding(){if((!IsSurvival&&Base?.Civilisation==null)||player==null||IsSurvival&&IndustrialAuthority==null)return;if(playerConstruction==null){if(!IsFirstPerson||PersonalMenuOpen||!EquippedBuildingModule(out _)&&PersonalItems.Find(PersonalInventory.Equipped)?.useAction!="blueprint")return;playerConstruction=new PlayerBuildingWorld(this,surface);}playerConstruction.Update();}

    }

}

