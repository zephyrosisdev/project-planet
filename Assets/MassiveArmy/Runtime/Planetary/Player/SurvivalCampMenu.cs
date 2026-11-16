using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Networking;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    internal sealed class SurvivalCampMenuState {
        internal List<SurvivalCampBeacon> beacons=new List<SurvivalCampBeacon>();internal IReadOnlyDictionary<long,SurvivalCampPublic> visible;
        internal FramePose player,shipPose;internal Inventory inventory;internal long ship,camp;internal bool ready,pilot,landed,automatic,onFoot;internal string status;
    }
    internal sealed class SurvivalCampMenu {
        readonly Func<SurvivalCampMenuState> read;readonly Action<long,int> land;readonly Action launch,abort;readonly Action<long,int,string,int> barter;readonly VisualElement body;readonly Label feedback;readonly IntegerField amount;readonly List<Action<SurvivalCampMenuState>> refresh=new List<Action<SurvivalCampMenuState>>();string signature="";double next;
        static Label Text(VisualElement parent,string text,string css="industry-text"){var label=new Label(text);label.AddToClassList(css);parent.Add(label);return label;}
        static Button Button(VisualElement parent,string text,Action click,string name=null){var button=new Button(click){text=text,name=name};button.AddToClassList("industry-button");parent.Add(button);return button;}
        internal SurvivalCampMenu(VisualElement parent,Func<SurvivalCampMenuState> read,Action<long,int> land,Action launch,Action abort,Action<long,int,string,int> barter){
            this.read=read;this.land=land;this.launch=launch;this.abort=abort;this.barter=barter;
            Text(parent,"AUTO LAND: approach within 1 km, below 600 m clearance and 180 m/s. Keep pads and approaches clear. Barter at a kiosk within 6 m.","industry-muted");feedback=Text(parent,"");
            var tools=new VisualElement();tools.AddToClassList("industry-toolbar");parent.Add(tools);Button(tools,"LAUNCH FROM PAD",()=>launch(),"camp-launch");Button(tools,"CANCEL PAD APPROACH",()=>abort(),"camp-abort");amount=new IntegerField("Trade quantity"){value=1,name="camp-trade-quantity"};tools.Add(amount);amount.RegisterValueChangedCallback(e=>amount.SetValueWithoutNotify(Math.Max(1,Math.Min(20,e.newValue))));
            body=new ScrollView{name="camp-directory"};body.style.flexGrow=1;body.style.minHeight=0;parent.Add(body);
        }
        static string Cost(ItemStack[] items,int count){var text=new List<string>();foreach(var item in items)text.Add(CraftingRecipePresentation.Name(item.item)+" ×"+item.count*count);return string.Join(" + ",text);}
        internal void Refresh(bool force=false){if(!force&&Time.unscaledTimeAsDouble<next)return;next=Time.unscaledTimeAsDouble+.1;var state=read();if(state==null)return;feedback.text=state.status??"";string key="";foreach(var camp in state.beacons)key+=camp.id+":"+(state.visible?.ContainsKey(camp.id)==true)+";";
            if(key!=signature||force){signature=key;body.Clear();refresh.Clear();if(state.beacons.Count==0)Text(body,"No suitable camp has been charted in this arrival region.");
                foreach(var beacon in state.beacons){long id=beacon.id;var heading=Text(body,beacon.name,"industry-title");refresh.Add(data=>{var current=data.beacons.Find(b=>b.id==id);if(current!=null)heading.text=current.name+" · "+(data.player.Distance(current.pose)/1000).ToString("0.00")+" km";});
                    for(int p=0;p<beacon.pads.Count;p++){int pad=p;var row=new VisualElement();row.AddToClassList("industry-row");body.Add(row);var status=Text(row,"");var button=Button(row,"AUTO LAND · PAD "+(p+1),()=>land(id,pad),"camp-land-"+id+"-"+pad);refresh.Add(data=>{var current=data.beacons.Find(b=>b.id==id);if(current==null)return;long occupant=current.occupied.Count>pad?current.occupied[pad]:0;double distance=(data.pilot?data.shipPose:data.player).Distance(current.pads[pad].pose);status.text=$"PAD {pad+1} · {distance:0} m · "+(occupant==0?"AVAILABLE":occupant==data.ship?"YOUR SHIP":"OCCUPIED / RESERVED");button.SetEnabled(data.ready&&data.pilot&&!data.landed&&!data.automatic&&(occupant==0||occupant==data.ship)&&distance<=1000);});}
                    if(state.visible==null||!state.visible.TryGetValue(id,out var camp)){Text(body,"Approach the camp to view its barter kiosks and current stock.","industry-muted");continue;}
                    foreach(var shop in camp.shops){int shopIndex=shop.index;Text(body,shop.name.ToUpperInvariant(),"industry-section");for(int i=0;i<SurvivalBarterCatalogue.Offers.Length;i++){var offer=SurvivalBarterCatalogue.Offers[i];if(offer.shop!=shopIndex)continue;int offerIndex=i;var row=new VisualElement();row.AddToClassList("industry-row");body.Add(row);var detail=Text(row,"");var button=Button(row,"BARTER",()=>barter(id,shopIndex,offer.id,amount.value),"camp-trade-"+id+"-"+offer.id);refresh.Add(data=>{if(data.visible==null||!data.visible.TryGetValue(id,out var current)){button.SetEnabled(false);return;}var currentShop=current.shops.Find(s=>s.index==shopIndex);long available=currentShop!=null&&currentShop.available.Count>offerIndex?currentShop.available[offerIndex]:0;int count=amount.value;bool payment=data.inventory!=null;foreach(var cost in offer.cost)payment&=(data.inventory?.Count(cost.item)??0)>=cost.count*count;bool near=currentShop!=null&&data.player.Distance(currentShop.pose)<=6;detail.text=Cost(offer.cost,count)+" → "+Cost(offer.goods,count)+" · "+(available==0?"OUT OF STOCK":near?"stock ready":"approach kiosk");button.SetEnabled(data.ready&&data.onFoot&&near&&payment&&available>=count);});}}
                }
            }
            body.SetEnabled(state.ready);foreach(var update in refresh)update(state);var launchButton=body.parent.Q<Button>("camp-launch");launchButton?.SetEnabled(state.ready&&state.pilot&&state.landed&&state.camp!=0);body.parent.Q<Button>("camp-abort")?.SetEnabled(state.ready&&state.pilot&&state.automatic&&state.camp!=0);
        }
    }
    public sealed partial class PlanetDevelopment {internal PlanetSurface CampSurface=>surface;}
}
