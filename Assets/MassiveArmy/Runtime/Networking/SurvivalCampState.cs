using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
namespace MassiveArmy.Networking {
    [Serializable] internal sealed class SurvivalCampBeacon {public long id;public string name;public FramePose pose;public List<SurvivalCampPad> pads=new List<SurvivalCampPad>();public List<long> occupied=new List<long>();}
    [Serializable] internal sealed class SurvivalCampShopPublic {public int index;public string name;public FramePose pose;public List<long> available=new List<long>();}
    [Serializable] internal sealed class SurvivalCampPublic {public long id;public string name;public FramePose pose;public List<SurvivalCampPad> pads=new List<SurvivalCampPad>();public List<SurvivalCampShopPublic> shops=new List<SurvivalCampShopPublic>();}
    internal static class SurvivalCampPresentation {
        internal static SurvivalCampPublic Public(SurvivalCampState camp){var result=new SurvivalCampPublic{id=camp.id,name=camp.name,pose=camp.pose,pads=camp.pads};foreach(var shop in camp.shops){var view=new SurvivalCampShopPublic{index=shop.index,name=shop.name,pose=shop.pose};foreach(var offer in SurvivalBarterCatalogue.Offers)view.available.Add(offer.shop==shop.index?offer.Available(shop.stock):0);result.shops.Add(view);}return result;}
    }
}
