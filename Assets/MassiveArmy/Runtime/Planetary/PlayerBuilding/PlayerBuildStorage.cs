using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Planetary {
    [Serializable] public sealed class PlayerBuildStorage {
        public List<PlayerBuildSite> playerBuildSites=new List<PlayerBuildSite>();
        public List<PlanetNavCoordinate> playerBuildLand=new List<PlanetNavCoordinate>();
        [NonSerialized] public HashSet<long> playerBuildLandIndex=new HashSet<long>();
    }
    public sealed partial class PlanetDevelopment {
        PlayerBuildStorage survivalBuildStorage,legacyBuildStorage;PlanetConstructionGrid survivalBuildGrid;
        internal PlayerBuildStorage BuildStorage {
            get {
                if(IsSurvival){if(survivalBuildStorage==null){var saved=IndustrialAuthority?.World.buildStates.Find(x=>x.body==definition.bodyId);survivalBuildStorage=saved!=null?JsonUtility.FromJson<PlayerBuildStorage>(saved.json):new PlayerBuildStorage();survivalBuildStorage.playerBuildLandIndex??=new HashSet<long>();}return survivalBuildStorage;}
                if(legacyBuildStorage==null){var data=SystemRuntime?.Civilisation?.Planet(definition.bodyId);if(data==null)return null;data.playerBuildSites??=new List<PlayerBuildSite>();data.playerBuildLand??=new List<PlanetNavCoordinate>();data.playerBuildLandIndex??=new HashSet<long>();legacyBuildStorage=new PlayerBuildStorage{playerBuildSites=data.playerBuildSites,playerBuildLand=data.playerBuildLand,playerBuildLandIndex=data.playerBuildLandIndex};}return legacyBuildStorage;
            }
        }
        internal PlanetConstructionGrid BuildGrid=>Base?.Grid??(survivalBuildGrid??=new PlanetConstructionGrid(definition.bodyId,definition.radius,PlanetConstructionGrid.AuthoredTileSize));
        internal string BuildNotice {set{if(Base!=null)Base.Notice=value;else SurvivalNotice=value;}}
        internal long AllocatePlayerBuildId()=>IsSurvival?IndustrialAuthority.World.AllocateId():SystemRuntime.Civilisation.State.AllocateId();
        internal bool CreativeConstruction=>IsSurvival&&IndustrialAuthority?.World.creativeMode==true;
        internal ItemStack[] PlayerModuleCost(PlayerModuleKind kind)=>EquipmentInventory?new[]{IndustrialCatalog.S(IndustrialCatalog.ModuleItem(kind.ToString()),1)}:IndustrialCatalog.ModuleCost(kind.ToString());
        internal string PlayerModuleCostText(PlayerModuleKind kind){if(CreativeConstruction)return "CREATIVE · free";var text=new System.Text.StringBuilder();foreach(var item in PlayerModuleCost(kind))text.Append(item.count).Append(' ').Append(item.item).Append("  ");return text.ToString();}
        internal bool CanPayPlayerModule(PlayerModuleKind kind)=>!IsSurvival||BuildingPolicyError==null&&(EquipmentInventory?PersonalInventory.Equipped==IndustrialCatalog.ModuleItem(kind.ToString())&&IndustrialAuthority.World.Actor(1).inventory.Count(PersonalInventory.Equipped)>0:CreativeConstruction||IndustrialAuthority.World.Actor(1).inventory.Has(PlayerModuleCost(kind)));
        internal List<ItemStack> PlayerModuleReceipt(PlayerModuleKind kind)=>IsSurvival?IndustrialConstruction.Receipt(IndustrialAuthority.World.Actor(1).inventory,PlayerModuleCost(kind),CreativeConstruction&&!EquipmentInventory):new List<ItemStack>();
        internal List<ItemStack> PlayerModuleRefund(PlayerModuleRecord record){if(record.resourceConstruction)return record.constructionPaid;string item=record.constructionItem??"stone";int count=record.constructionCount>0?record.constructionCount:2;if(IndustrialCatalog.RetiredConstructionRecipe(item)&&IndustrialCatalog.Recipes.TryGetValue(item,out var recipe)){var result=IndustrialConstruction.WithActivity(recipe.input,record.constructionActivity);foreach(var stack in result)stack.count*=count;return result;}return new List<ItemStack>{new ItemStack(item,count){activity=record.constructionActivity}};}
        internal bool CanRefundPlayerModule(PlayerModuleRecord record)=>!IsSurvival||IndustrialAuthority.World.Actor(1).inventory.CanAddBatch(PlayerModuleRefund(record));
        internal bool HasMountedMachines(long site,long module)=>IsSurvival&&IndustrialAuthority.World.machines.Exists(x=>x.attachmentSite==site&&x.attachmentModule==module&&(!x.integratedLogistics||x.input.Used>0||IndustrialAuthority.World.belts.Exists(b=>b.source==x.id||b.target==x.id)));
        internal void PersistPlayerBuild(PlayerModuleRecord record=null,bool refund=false){if(!IsSurvival)return;var payment=record==null?new List<ItemStack>():refund?PlayerModuleRefund(record):record.constructionPaid;PlayerBuildProjection.Commit(IndustrialAuthority,1,definition.bodyId,BuildStorage,payment,refund);}
    }
}

