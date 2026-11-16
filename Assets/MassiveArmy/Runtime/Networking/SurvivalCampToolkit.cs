using MassiveArmy.Planetary;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        bool campMenuOpen;VisualElement campPanel;SurvivalCampMenu campMenu;
        void OpenCampMenu(){campMenuOpen=true;inventoryOpen=industryOpen=networkMenu=false;UpdateNetworkCursor();}
        void BuildOnlineCampMenu(VisualElement root){campPanel=CraftBox(root,"industry-menu");campPanel.name="online-camps";campPanel.style.display=DisplayStyle.None;var heading=CraftBox(campPanel,"industry-heading");CraftText(heading,"NEUTRAL CAMPS · LANDING & BARTER","industry-title");CraftButton(heading,"CLOSE [F7 / ESC]",()=>{campMenuOpen=false;UpdateNetworkCursor();});campMenu=new SurvivalCampMenu(campPanel,ReadCampMenu,(camp,pad)=>Submit(new SurvivalAction{kind="camp-land",entity=camp,slot=pad}),()=>Submit(new SurvivalAction{kind="camp-launch"}),()=>Submit(new SurvivalAction{kind="camp-abort"}),(camp,shop,offer,count)=>CraftCommand("camp-barter",camp,offer,count,shop));}
        SurvivalCampMenuState ReadCampMenu(){if(latest?.self==null)return null;var ship=predicted?.ship;return new SurvivalCampMenuState{beacons=latest.campBeacons,visible=clientCamps,player=latest.self.pose,inventory=latest.self.inventory,ship=ship?.entity??0,shipPose=ship?.pose??latest.self.pose,camp=ship?.landingCamp??0,ready=PrivateStateReady&&!ChatInputCaptured,pilot=ship!=null&&ship.owner==ActorId&&latest.vitals.controlledShip==ship.entity,landed=ship?.landed==true,automatic=ship?.autoFlight==true,onFoot=latest.vitals.attachedShip==0&&latest.vitals.controlledEntity==0,status=Status};}
        void RefreshOnlineCampMenu(){if(campPanel==null)return;campPanel.style.display=campMenuOpen?DisplayStyle.Flex:DisplayStyle.None;if(campMenuOpen)campMenu.Refresh();}
    }
}

