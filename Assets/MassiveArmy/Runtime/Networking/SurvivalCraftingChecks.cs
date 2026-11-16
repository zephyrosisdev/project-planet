#if UNITY_EDITOR || UNITY_SERVER
using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunCraftingPresentationChecks(Action<bool,string> check){
            var wire=JsonUtility.FromJson<SurvivalSnapshot>(JsonUtility.ToJson(new SurvivalSnapshot{self=new IndustrialActor()}));wire.NormalizeReferences();check(wire.self.craft==null,"Snapshot normalization removes Unity's empty inline crafting job after JSON transport");
            wire.self.craft=new IndustrialCraftJob{id=-1,recipe="plate",progress=2};wire.NormalizeReferences();check(wire.self.craft!=null&&wire.self.craft.progress==2,"Snapshot normalization preserves real and legacy active crafting jobs");
            var actor=new IndustrialActor{id=9,inventory=new Inventory{capacity=10000}};actor.inventory.EnableSlots(40);actor.inventory.Add("iron-bar",100);var recipe=IndustrialCatalog.Recipes["plate"];
            string before=JsonUtility.ToJson(actor);check(CraftingRecipePresentation.MaxBatch(actor,recipe)==20&&JsonUtility.ToJson(actor)==before,"Crafting quantity previews enforce the batch limit without mutating private inventory");
            for(int i=0;i<31;i++)actor.craftQueue.Add(new IndustrialCraftJob{id=i+1,recipe="plate"});check(CraftingRecipePresentation.MaxBatch(actor,recipe)==1,"Crafting MAX respects the authoritative 32-job queue limit");actor.craft=new IndustrialCraftJob{id=99,recipe="plate"};check(CraftingRecipePresentation.MaxBatch(actor,recipe)==0,"A full active-plus-waiting queue disables additional crafting");actor.craft=null;actor.craftQueue.Clear();
            check(CraftingRecipePresentation.MaxBatch(actor,IndustrialCatalog.Recipes["gear"])==0&&CraftingRecipePresentation.Station("gear")!="By hand","Station recipes cannot be previewed as hand crafts");
            check(CraftingRecipePresentation.Category("deploy-truck")=="Vehicles"&&CraftingRecipePresentation.Category("module-Wall")=="Building","Offline and online browsers classify deployable vehicles and construction modules consistently");
            var host=new GameObject("Crafting browser fixture");bool previous=DiagnosticMultipleSessions;
            try{DiagnosticMultipleSessions=true;var session=host.AddComponent<SurvivalNetworkSession>();session.latest=new SurvivalSnapshot{self=actor};var root=new VisualElement();session.BuildOnlineCrafting(root);
                check(root.Q<VisualElement>("online-crafting")!=null&&root.Q<Button>("online-recipe-plate")!=null&&root.Q<IntegerField>("online-craft-quantity")!=null,"The online retained crafting browser includes recipe tiles, detail and quantity controls");
                // Detached headless elements have no panel event dispatcher. Exercise
                // filtering here; the standalone panel probe dispatches real callbacks.
                session.craftingSearch.SetValueWithoutNotify("decontamination");session.RebuildCraftingGrid();check(session.craftingTiles.Count==1&&session.craftingTiles.ContainsKey("decontamination-kit"),"Recipe search filters retained controls using display names and identifiers");
                session.craftingAvailable.SetValueWithoutNotify(true);session.RebuildCraftingGrid();check(session.craftingTiles.Count==0,"Have-ingredients filtering uses the current private inventory");session.craftingAvailable.SetValueWithoutNotify(false);session.craftingSearch.SetValueWithoutNotify("");
                session.craftingSelection="gear";session.BuildCraftingDetails();check(!session.craftingSubmit.enabledSelf&&session.craftingSubmit.text.StartsWith("REQUIRES"),"A machine-only recipe cannot dispatch a misleading hand-craft action");
                session.craftingSelection="plate";session.BuildCraftingDetails();check(session.craftingAmount.value==1,"Selecting a recipe starts a fresh bounded batch quantity");
                actor.craft=new IndustrialCraftJob{id=7,recipe="plate",progress=4};actor.craftQueue.Add(new IndustrialCraftJob{id=8,recipe="plate"});session.RefreshCraftingQueue();check(root.Q<Button>("online-craft-cancel-7")!=null&&root.Q<Button>("online-craft-cancel-8")!=null&&session.craftingProgress.value>0,"Active and waiting jobs have stable cancellation targets and authoritative progress");
                actor.craft=null;actor.craftQueue.Clear();session.RefreshCraftingQueue();check(root.Q<Button>("online-craft-cancel-7")==null&&session.craftingProgress==null,"Completed queue entries are removed without stale cancellation targets");
            }finally{DiagnosticMultipleSessions=previous;UnityEngine.Object.DestroyImmediate(host);}
        }
    }
}
#endif
