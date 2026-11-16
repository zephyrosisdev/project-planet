using System;
using System.Collections.Generic;
using System.Text;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        VisualElement craftingPanel,craftingGrid,craftingDetails,craftingQueue;TextField craftingSearch;Toggle craftingAvailable;IntegerField craftingAmount;DropdownField craftingStation;
        Label craftingFeedback;Button craftingSubmit;ProgressBar craftingProgress;Action updateCraftingDetails;
        string craftingCategory="All",craftingSelection="plate",craftingSignature="",craftingQueueSignature="",craftingStationsSignature="";long craftingMachine;bool industryTools;double nextCraftingRefresh;
        readonly Dictionary<string,Button> craftingTiles=new Dictionary<string,Button>();readonly List<long> craftingStationIds=new List<long>();
        static Label CraftText(VisualElement parent,string value,string css="industry-text"){var label=new Label(value);label.AddToClassList(css);parent.Add(label);return label;}
        static VisualElement CraftBox(VisualElement parent,string css){var box=new VisualElement();box.AddToClassList(css);parent.Add(box);return box;}
        static Button CraftButton(VisualElement parent,string value,Action action,string name=null){var button=new Button(action){text=value,name=name};button.AddToClassList("industry-button");parent.Add(button);return button;}
        IndustrialMachine CraftingMachine(){if(craftingMachine==0||!clientMachines.TryGetValue(craftingMachine,out var machine)||machine.owner!=ActorId||ClientDistance(machine.pose)>12)return null;return machine;}
        internal bool CraftingTextCaptured {get {if((!industryOpen||industryTools)&&!campMenuOpen||inventoryRoot==null)return false;for(var focused=inventoryRoot.focusController?.focusedElement as VisualElement;focused!=null;focused=focused.parent)if(focused is TextField||focused is IntegerField)return true;return false;}}
        void BuildOnlineCrafting(VisualElement root){
            var sheet=Resources.Load<StyleSheet>("MassiveArmy/UI/Industry");if(sheet!=null)root.styleSheets.Add(sheet);
            craftingPanel=CraftBox(root,"industry-menu");craftingPanel.name="online-crafting";craftingPanel.style.display=DisplayStyle.None;
            craftingPanel.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());craftingPanel.RegisterCallback<WheelEvent>(e=>e.StopPropagation());
            var heading=CraftBox(craftingPanel,"industry-heading");CraftText(heading,"CRAFTING & PRODUCTION","industry-title");CraftButton(heading,"CLOSE [F4 / ESC]",()=>{industryOpen=false;UpdateNetworkCursor();});
            var tabs=CraftBox(craftingPanel,"industry-tabs");CraftButton(tabs,"INVENTORY [I]",()=>{industryOpen=false;inventoryOpen=true;UpdateNetworkCursor();});CraftButton(tabs,"CONSTRUCTION / MACHINES / SHIPS",()=>{industryTools=true;craftingPanel.style.display=DisplayStyle.None;});
            craftingFeedback=CraftText(craftingPanel,"","industry-muted");craftingFeedback.name="online-craft-feedback";
            CraftButton(tabs,"CAMPS / PADS / BARTER [F7]",OpenCampMenu);
            var toolbar=CraftBox(craftingPanel,"industry-toolbar");craftingSearch=new TextField("SEARCH"){name="online-craft-search"};craftingSearch.style.flexGrow=1;toolbar.Add(craftingSearch);craftingAvailable=new Toggle("Have ingredients"){name="online-craft-available"};toolbar.Add(craftingAvailable);
            craftingSearch.RegisterValueChangedCallback(_=>RebuildCraftingGrid());craftingAvailable.RegisterValueChangedCallback(_=>RebuildCraftingGrid());
            craftingStation=new DropdownField("CRAFT AT",new List<string>{"By hand"},0){name="online-craft-station"};craftingPanel.Add(craftingStation);craftingStation.RegisterValueChangedCallback(_=>{int index=craftingStation.index;craftingMachine=index>=0&&index<craftingStationIds.Count?craftingStationIds[index]:0;RebuildCraftingGrid();BuildCraftingDetails();});
            var body=CraftBox(craftingPanel,"industry-body");var categories=new ScrollView();categories.AddToClassList("industry-categories");body.Add(categories);foreach(string category in CraftingRecipePresentation.Categories){string chosen=category;CraftButton(categories,category.ToUpperInvariant(),()=>{craftingCategory=chosen;RebuildCraftingGrid();});}
            var recipes=new ScrollView{name="online-craft-recipes"};recipes.AddToClassList("industry-recipe-scroll");body.Add(recipes);craftingGrid=CraftBox(recipes,"industry-recipe-grid");var details=new ScrollView();details.AddToClassList("industry-details");body.Add(details);craftingDetails=details;
            var queueScroll=new ScrollView();queueScroll.AddToClassList("industry-queue");craftingPanel.Add(queueScroll);craftingQueue=queueScroll;
            craftingSignature=craftingQueueSignature=craftingStationsSignature="";nextCraftingRefresh=0;RebuildCraftingGrid();BuildCraftingDetails();
        }
        bool CraftingRecipeVisible(RecipeDefinition recipe){var machine=CraftingMachine();return !IndustrialCatalog.RetiredConstructionRecipe(recipe.id)&&(craftingMachine==0||machine!=null&&IndustrialCatalog.Supports(machine.definition,recipe.id));}
        void RebuildCraftingGrid(){
            if(craftingGrid==null)return;craftingGrid.Clear();craftingTiles.Clear();var machine=CraftingMachine();var stock=craftingMachine==0?latest?.self?.inventory:machine?.input;string search=craftingSearch.value.Trim();var recipes=new List<RecipeDefinition>(IndustrialCatalog.Recipes.Values);recipes.Sort((a,b)=>{int first=IndustrialCatalog.Handcraft.Contains(a.id)?0:1,second=IndustrialCatalog.Handcraft.Contains(b.id)?0:1;return first!=second?first.CompareTo(second):StringComparer.OrdinalIgnoreCase.Compare(CraftingRecipePresentation.Name(a.id),CraftingRecipePresentation.Name(b.id));});
            foreach(var recipe in recipes){string id=recipe.id;if(!CraftingRecipeVisible(recipe)||craftingCategory!="All"&&CraftingRecipePresentation.Category(id)!=craftingCategory||search.Length>0&&CraftingRecipePresentation.Name(id).IndexOf(search,StringComparison.OrdinalIgnoreCase)<0&&id.IndexOf(search,StringComparison.OrdinalIgnoreCase)<0||craftingAvailable.value&&(stock==null||!stock.Has(recipe.input)))continue;
                var tile=CraftButton(craftingGrid,"",()=>{craftingSelection=id;BuildCraftingDetails();},"online-recipe-"+id);tile.AddToClassList("industry-recipe-tile");tile.EnableInClassList("station-recipe",!IndustrialCatalog.Handcraft.Contains(id));tile.EnableInClassList("selected",id==craftingSelection);CraftText(tile,CraftingRecipePresentation.Name(id),"industry-item-name");CraftText(tile,CraftingRecipePresentation.Station(id),"industry-item-requirement");craftingTiles.Add(id,tile);}
            if(craftingTiles.Count==0)CraftText(craftingGrid,"No recipes match these filters.");
        }
        void CraftCommand(string kind,long target=0,string item=null,long count=0,long source=0){if(PrivateStateReady&&!ChatInputCaptured)Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind=kind,target=target,item=item,count=count,source=source}});}
        void BuildCraftingDetails(){
            if(craftingDetails==null)return;craftingDetails.Clear();craftingAmount=null;updateCraftingDetails=null;foreach(var tile in craftingTiles)tile.Value.EnableInClassList("selected",tile.Key==craftingSelection);
            if(!IndustrialCatalog.Recipes.TryGetValue(craftingSelection,out var recipe)||!CraftingRecipeVisible(recipe)){CraftText(craftingDetails,"Select a recipe to inspect its ingredients and requirements.");return;}
            CraftText(craftingDetails,CraftingRecipePresentation.Name(recipe.id).ToUpperInvariant(),"industry-title");CraftText(craftingDetails,CraftingRecipePresentation.Category(recipe.id)+" · "+CraftingRecipePresentation.Station(recipe.id),"industry-muted");
            foreach(var output in recipe.output)CraftText(craftingDetails,"OUTPUT  "+CraftingRecipePresentation.Name(output.item)+" ×"+output.count);
            CraftText(craftingDetails,$"TIME  {recipe.ticks*.1:0.0}s per craft · POWER {recipe.power:0.##} kW · WATER {recipe.water} L");
            var ingredients=CraftBox(craftingDetails,"industry-ingredients");var ingredientLabels=new List<Label>();foreach(var input in recipe.input)ingredientLabels.Add(CraftText(ingredients,""));
            craftingAmount=new IntegerField("Quantity"){value=1,name="online-craft-quantity"};craftingDetails.Add(craftingAmount);craftingAmount.style.display=craftingMachine==0?DisplayStyle.Flex:DisplayStyle.None;
            if(craftingMachine==0){var row=CraftBox(craftingDetails,"industry-row");CraftButton(row,"−",()=>craftingAmount.value=Math.Max(1,craftingAmount.value-1));CraftButton(row,"+",()=>craftingAmount.value=Math.Min(20,craftingAmount.value+1));CraftButton(row,"MAX",()=>craftingAmount.value=Math.Max(1,CraftingRecipePresentation.MaxBatch(latest?.self,recipe)));}
            craftingSubmit=CraftButton(craftingDetails,"",()=>{if(craftingMachine==0)CraftCommand("craft",item:recipe.id,count:craftingAmount.value);else CraftCommand("recipe",craftingMachine,recipe.id);},"online-craft-submit");craftingSubmit.AddToClassList("industry-primary");
            var note=CraftText(craftingDetails,"","industry-muted");ProgressBar machineProgress=null;Label machineStatus=null;
            if(craftingMachine!=0){machineProgress=new ProgressBar{lowValue=0,highValue=1};craftingDetails.Add(machineProgress);machineStatus=CraftText(craftingDetails,"");CraftButton(craftingDetails,"MANAGE INPUT / OUTPUT INVENTORY",()=>{industryOpen=false;inventoryOpen=true;UpdateNetworkCursor();});CraftButton(craftingDetails,"TOGGLE PRODUCTION",()=>CraftCommand("toggle",craftingMachine));}
            updateCraftingDetails=()=>{var machine=CraftingMachine();var stock=craftingMachine==0?latest?.self?.inventory:machine?.input;int count=craftingMachine==0?Math.Max(1,Math.Min(20,craftingAmount.value)):1;for(int i=0;i<recipe.input.Length;i++){var input=recipe.input[i];long have=stock?.Count(input.item)??0,need=input.count*count;ingredientLabels[i].text=$"{CraftingRecipePresentation.Name(input.item)}    {have:N0} / {need:N0}";ingredientLabels[i].EnableInClassList("missing",have<need);}
                if(craftingMachine==0){bool hand=IndustrialCatalog.Handcraft.Contains(recipe.id);int maximum=CraftingRecipePresentation.MaxBatch(latest?.self,recipe);craftingSubmit.text=hand?$"CRAFT ×{count}":"REQUIRES "+CraftingRecipePresentation.Station(recipe.id).ToUpperInvariant();craftingSubmit.SetEnabled(PrivateStateReady&&hand&&maximum>=count);note.text=hand?(maximum<count?"Insufficient ingredients, queue space or inventory capacity.":"Ingredients and output slots are reserved by the server. Equip crafted placeables from your action bar."):"Place the required machine, then select it above while within 12 m. Supply its input inventory to produce this recipe.";}
                else {craftingSubmit.text="SET PRODUCTION RECIPE";craftingSubmit.SetEnabled(PrivateStateReady&&machine!=null&&machine.progress==0&&machine.reserved.Count==0);note.text=machine==null?"Machine is no longer in range.":"Production uses machine input, power and water. Missing ingredients do not prevent selecting a recipe. Busy machines must finish their current batch first.";if(machine!=null){int ticks=IndustrialCatalog.Recipes.TryGetValue(machine.recipe??"",out var active)?active.ticks:1;machineProgress.value=Mathf.Clamp01(machine.progress/(float)Math.Max(1,ticks));machineProgress.title=CraftingRecipePresentation.Name(machine.recipe);machineStatus.text=(machine.enabled?"Enabled":"Paused")+" · "+machine.status+$"\nWater {machine.water:N0} L · Available power {machine.powerAvailable:0.##} kW";}}};
            craftingAmount.RegisterValueChangedCallback(e=>{craftingAmount.SetValueWithoutNotify(Math.Max(1,Math.Min(20,e.newValue)));updateCraftingDetails?.Invoke();});updateCraftingDetails();
        }
        void RefreshCraftingQueue(){
            var actor=latest.self;var signature=new StringBuilder();signature.Append(actor.craft?.id??0);foreach(var job in actor.craftQueue)signature.Append('/').Append(job.id);string key=signature.ToString();
            if(key!=craftingQueueSignature){craftingQueueSignature=key;craftingQueue.Clear();CraftText(craftingQueue,$"HAND CRAFTING QUEUE · {actor.craftQueue.Count+(actor.craft==null?0:1)} / 32","industry-section");craftingProgress=null;
                if(actor.craft==null)CraftText(craftingQueue,"Nothing queued. Production continues while this menu is closed.");else {craftingProgress=new ProgressBar{lowValue=0,highValue=1};craftingQueue.Add(craftingProgress);}
                void Job(IndustrialCraftJob job){long id=job.id;var row=CraftBox(craftingQueue,"industry-row");CraftText(row,CraftingRecipePresentation.Name(job.recipe)+" #"+id);CraftButton(row,"CANCEL / REFUND",()=>CraftCommand("cancel-craft",id),"online-craft-cancel-"+id);}
                if(actor.craft!=null)Job(actor.craft);foreach(var job in actor.craftQueue)Job(job);
            }
            if(craftingProgress!=null&&actor.craft!=null&&IndustrialCatalog.Recipes.TryGetValue(actor.craft.recipe,out var recipe)){craftingProgress.value=Mathf.Clamp01(actor.craft.progress/(float)recipe.ticks);craftingProgress.title=$"{CraftingRecipePresentation.Name(recipe.id)} · {Math.Max(0,recipe.ticks-actor.craft.progress)*.1:0.0}s remaining (simulation time)";}
        }
        void RefreshOnlineCrafting(){
            if(craftingPanel==null)return;bool open=industryOpen&&!industryTools&&latest?.self!=null&&latest.self.health>0;craftingPanel.style.display=open?DisplayStyle.Flex:DisplayStyle.None;if(!open)return;craftingPanel.SetEnabled(PrivateStateReady&&!ChatInputCaptured);if(!PrivateStateReady)return;
            if(Time.unscaledTimeAsDouble<nextCraftingRefresh)return;nextCraftingRefresh=Time.unscaledTimeAsDouble+.1;craftingFeedback.text=Status;
            var machines=new List<IndustrialMachine>();foreach(var machine in clientMachines.Values)if(machine.owner==ActorId&&ClientDistance(machine.pose)<=12){bool supported=false;foreach(var recipe in IndustrialCatalog.Recipes.Values)if(!IndustrialCatalog.RetiredConstructionRecipe(recipe.id)&&IndustrialCatalog.Supports(machine.definition,recipe.id)){supported=true;break;}if(supported)machines.Add(machine);}machines.Sort((a,b)=>a.id.CompareTo(b.id));
            var stationKey=new StringBuilder();foreach(var machine in machines)stationKey.Append(machine.id).Append('/');string stationSignature=stationKey.ToString();if(stationSignature!=craftingStationsSignature){craftingStationsSignature=stationSignature;craftingStationIds.Clear();craftingStationIds.Add(0);var names=new List<string>{"By hand / all recipes"};foreach(var machine in machines){craftingStationIds.Add(machine.id);names.Add(CraftingRecipePresentation.Name(machine.definition)+" #"+machine.id);}if(!craftingStationIds.Contains(craftingMachine))craftingMachine=0;craftingStation.choices=names;craftingStation.SetValueWithoutNotify(names[craftingStationIds.IndexOf(craftingMachine)]);RebuildCraftingGrid();BuildCraftingDetails();}
            var stock=craftingMachine==0?latest.self.inventory:CraftingMachine()?.input;var inventoryKey=new StringBuilder();inventoryKey.Append(craftingMachine);if(stock!=null)foreach(var stack in stock.items)inventoryKey.Append('/').Append(stack.item).Append(':').Append(stack.count);string inventorySignature=inventoryKey.ToString();if(inventorySignature!=craftingSignature){craftingSignature=inventorySignature;if(craftingAvailable.value)RebuildCraftingGrid();}
            updateCraftingDetails?.Invoke();RefreshCraftingQueue();
        }
    }
}


