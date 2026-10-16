using System.Linq;
using UnityEngine.UIElements;
namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        bool observerBuilt;
        void RefreshObserver()
        {
            Visible(constructionUi,true);
            if(!observerBuilt||constructionUi.Q<Label>("observer-0")==null){
                observerBuilt=true;constructionList=null;categoryButtons.Clear();buildKey="";constructionUi.Clear();constructionUi.Insert(0,new RtsUiFrame());L(constructionUi,"AI BALANCE TEST\nSPECTATOR","heading");
                var row=Box(constructionUi,"row");B(row,"CYAN BASE",()=>{observedTeam=0;rig.FocusBase(Base.BasePosition(0));},"core","grow");B(row,"RED BASE",()=>{observedTeam=1;rig.FocusBase(Base.BasePosition(1));},"core","grow");
                B(constructionUi,"FULL MAP",()=>rig.FullMap(),"map");L(constructionUi,"Left-click either team's buildings to inspect.\nCamera, pause and GPU lab remain available.","muted");
                B(constructionUi,"EXPORT BALANCE SNAPSHOT",ExportBalanceSnapshot,"research");
                var scroll=new ScrollView();constructionUi.Add(scroll);L(scroll,"","body","observer-0");L(scroll,"","body","observer-1");
            }
            for(int team=0;team<2;team++){
                var faction=Base.Factions[team];var buildings=Base.Buildings.Where(b=>!b.removed&&b.team==team).ToArray();var stats=Battle.Statistics;
                Value("observer-"+team,$"{(team==0?"CYAN":"RED")} AI · {faction.profile}\nTier {faction.tier} · Army {(stats==null?0:stats[team]):N0}\nBuildings {buildings.Length} · Outposts {buildings.Count(b=>b.definition.id=="outpost")}\nResources: {buildings.Count(b=>b.node>=0)} extractors\nPower {faction.generation:0}/{faction.demand:0} MW · {faction.efficiency:P0}\nWalls {buildings.Count(b=>b.definition.id=="wall")} · Gates {buildings.Count(b=>b.definition.id=="gate")}\nQueues {buildings.Sum(b=>b.queue.Count)} · AI cycles {Base.AIDecisions[team]}\n{Base.AIActivity[team]}\nTech {faction.technologies.Count}: {string.Join(", ",faction.technologies.OrderBy(t=>t))}\n");
            }
        }
    }
}
