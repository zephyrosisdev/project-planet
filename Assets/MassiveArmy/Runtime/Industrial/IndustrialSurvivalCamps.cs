using System;
using System.Collections.Generic;
namespace MassiveArmy.Industrial {
    [Serializable] public sealed class SurvivalCampPad {public int index;public FramePose pose;}
    [Serializable] public sealed class SurvivalCampShop {public int index;public string name;public FramePose pose;public Inventory stock=new Inventory{capacity=100000};}
    [Serializable] public sealed class SurvivalCampState {
        public int version=1,seed;public long id;public string name;public FramePose pose;
        public List<SurvivalCampPad> pads=new List<SurvivalCampPad>();public List<SurvivalCampShop> shops=new List<SurvivalCampShop>();
        public bool Valid {
            get {bool Point(FramePose p)=>p.body==pose.body&&p.frame==0&&SurvivalServerConfig.Finite(p.x)&&SurvivalServerConfig.Finite(p.y)&&SurvivalServerConfig.Finite(p.z)&&SurvivalServerConfig.Finite(p.yaw);
                if(version!=1||id<=0||pose.body<=0||!Point(pose)||string.IsNullOrWhiteSpace(name)||name.Length>64||pads==null||pads.Count!=2||shops==null||shops.Count!=3)return false;
                for(int i=0;i<pads.Count;i++)if(pads[i]==null||pads[i].index!=i||!Point(pads[i].pose)||pose.Distance(pads[i].pose)>300)return false;
                for(int i=0;i<shops.Count;i++)if(shops[i]==null||shops[i].index!=i||!Point(shops[i].pose)||pose.Distance(shops[i].pose)>300||shops[i].stock==null)return false;return true;
            }
        }
    }
    public sealed class SurvivalBarterOffer {
        public readonly string id;public readonly int shop;public readonly ItemStack[] cost,goods;
        internal SurvivalBarterOffer(string id,int shop,ItemStack[] cost,ItemStack[] goods){this.id=id;this.shop=shop;this.cost=cost;this.goods=goods;}
        public long Available(Inventory stock){long available=20;foreach(var item in goods)available=Math.Min(available,stock.Count(item.item)/item.count);return available;}
    }
    public static class SurvivalBarterCatalogue {
        static ItemStack S(string item,int count)=>new ItemStack(item,count);
        public static readonly SurvivalBarterOffer[] Offers={
            new SurvivalBarterOffer("ration",0,new[]{S("stone",4)},new[]{S("ration",1)}),
            new SurvivalBarterOffer("water",0,new[]{S("stone",2)},new[]{S("water",2)}),
            new SurvivalBarterOffer("bandage",0,new[]{S("stone",6)},new[]{S("bandage",1)}),
            new SurvivalBarterOffer("suit-oxygen",0,new[]{S("stone",8)},new[]{S("suit-oxygen",1)}),
            new SurvivalBarterOffer("iron",1,new[]{S("stone",8)},new[]{S("iron-bar",2)}),
            new SurvivalBarterOffer("copper",1,new[]{S("stone",8)},new[]{S("copper-bar",2)}),
            new SurvivalBarterOffer("fuel",1,new[]{S("iron-bar",4)},new[]{S("fuel",4)}),
            new SurvivalBarterOffer("sell-iron",1,new[]{S("iron-bar",2)},new[]{S("stone",4)}),
            new SurvivalBarterOffer("repair-tool",2,new[]{S("iron-bar",8),S("copper-bar",4)},new[]{S("repair-tool",1)}),
            new SurvivalBarterOffer("wire",2,new[]{S("copper-bar",2)},new[]{S("wire",2)}),
            new SurvivalBarterOffer("plate",2,new[]{S("iron-bar",2)},new[]{S("plate",2)})
        };
        public static SurvivalBarterOffer Find(string id){foreach(var offer in Offers)if(offer.id==id)return offer;return null;}
        public static Inventory InitialStock(int shop){var inventory=new Inventory{capacity=100000};foreach(var offer in Offers)if(offer.shop==shop)foreach(var item in offer.goods)if(inventory.Count(item.item)==0)inventory.Add(item.item,200);return inventory;}
    }
    public sealed partial class IndustrialWorld {
        public List<SurvivalCampState> camps=new List<SurvivalCampState>();
        internal void ValidateCamps(){var ids=new HashSet<long>();var bodies=new HashSet<int>();foreach(var camp in camps)if(camp==null||!camp.Valid||camp.id>=nextId||!ids.Add(camp.id)||!bodies.Add(camp.pose.body))throw new System.IO.InvalidDataException("Invalid or duplicate saved neutral camp");}
        public SurvivalCampState Camp(long id)=>camps.Find(c=>c.id==id);
        public bool CampReserved(FramePose pose,double margin=0){foreach(var camp in camps)if(camp.pose.body==pose.body&&InteractionDistance(camp.pose,pose)<260+margin)return true;return false;}
        string PrepareBarter(IndustrialActor actor,IndustrialCommand command,out Action apply){
            apply=null;var camp=Camp(command.target);var offer=SurvivalBarterCatalogue.Find(command.item);if(actor.health<=0||camp==null||offer==null||command.source!=offer.shop||command.count<1||command.count>20)return "Invalid camp, shop, offer or quantity";
            var player=multiplayer?.players.Find(p=>p.actor==actor.id);if(player!=null&&(player.attachedShip!=0||player.controlledEntity!=0))return "Disembark before using a camp shop";
            var shop=camp.shops[offer.shop];if(InteractionDistance(actor.pose,shop.pose)>6)return "Move within 6 m of the shop counter";
            var buyer=actor.inventory.CloneSlots();var seller=shop.stock.CloneSlots();
            foreach(var cost in offer.cost)if(buyer.Count(cost.item)<cost.count*command.count)return "Missing barter payment";else buyer.Extract(cost.item,cost.count*command.count);
            foreach(var goods in offer.goods)if(seller.Count(goods.item)<goods.count*command.count)return "Shop stock is exhausted";else seller.Extract(goods.item,goods.count*command.count);
            var deliveries=new List<ItemStack>();var payments=new List<ItemStack>();foreach(var goods in offer.goods)deliveries.Add(new ItemStack(goods.item,goods.count*command.count));foreach(var cost in offer.cost)payments.Add(new ItemStack(cost.item,cost.count*command.count));
            if(!buyer.CanAddBatch(deliveries)||!seller.CanAddBatch(payments))return "Make inventory space for this trade";
            apply=()=>{foreach(var cost in offer.cost){long count=cost.count*command.count;double activity=actor.inventory.Extract(cost.item,count);if(!shop.stock.Add(cost.item,count,activity))throw new InvalidOperationException("Barter payment capacity changed");}foreach(var goods in offer.goods){long count=goods.count*command.count;double activity=shop.stock.Extract(goods.item,count);if(!actor.inventory.Add(goods.item,count,activity))throw new InvalidOperationException("Barter delivery capacity changed");}};return null;
        }
    }
    public sealed partial class IndustrialAuthority {
        public void InitializeSurvivalCamp(SurvivalCampState camp){lock(gate){if(camp==null||!camp.Valid||camp.id!=World.nextId||World.camps.Exists(c=>c.pose.body==camp.pose.body))throw new InvalidOperationException("Invalid or duplicate neutral camp");Append(new IndustrialJournalEntry{campInitialization=camp});ApplyCampInitialization(World,camp);}}
        static void ApplyCampInitialization(IndustrialWorld world,SurvivalCampState camp){if(!camp.Valid||camp.id!=world.nextId||world.camps.Exists(c=>c.id==camp.id||c.pose.body==camp.pose.body))throw new InvalidOperationException("Invalid camp initialization receipt");world.camps.Add(camp);world.AllocateId();}
    }
}

