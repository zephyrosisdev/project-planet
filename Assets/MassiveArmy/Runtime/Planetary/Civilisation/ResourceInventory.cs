using System;
using System.Collections.Generic;
namespace MassiveArmy.Planetary
{
    /// <summary>Additional materials extend the original wallet; a mapped resource has exactly one balance.</summary>
    [Serializable] public sealed class ResourceInventory {
        public List<MaterialQuantity> stock=new List<MaterialQuantity>();
        [NonSerialized]ResourceWallet wallet;[NonSerialized]CivilisationCatalogue catalogue;
        public void Bind(CivilisationCatalogue definitions,ResourceWallet backing=null){catalogue=definitions;wallet=backing;}
        MaterialQuantity Entry(string id){foreach(var value in stock)if(value.resource==id)return value;var result=new MaterialQuantity(id,0);stock.Add(result);return result;}
        ElementDefinition Definition(string resource){if(catalogue==null||!catalogue.Resources.TryGetValue(resource,out var definition))throw new ArgumentException("Unknown inventory resource "+resource);return definition;}
        public double Get(string id){var definition=Definition(id);if(wallet!=null&&definition.legacy>=0)return wallet.Stock[definition.legacy];foreach(var value in stock)if(value.resource==id)return value.amount;return 0;}
        public double Capacity(string id){var definition=Definition(id);return wallet!=null&&definition.legacy>=0?wallet.Capacity[definition.legacy]:definition.capacity;}
        public double Deposit(string id,double amount){if(!double.IsFinite(amount)||amount<0)throw new ArgumentOutOfRangeException(nameof(amount));var definition=Definition(id);double accepted=Math.Min(amount,Math.Max(0,Capacity(id)-Get(id)));if(wallet!=null&&definition.legacy>=0){wallet.Deposit((ResourceKind)definition.legacy,(float)accepted);}else Entry(id).amount+=accepted;return accepted;}
        public bool Spend(string id,double amount){if(!double.IsFinite(amount)||amount<0)throw new ArgumentOutOfRangeException(nameof(amount));if(Get(id)+1e-9<amount)return false;var definition=Definition(id);if(wallet!=null&&definition.legacy>=0){var cost=new ResourceCost();cost[definition.legacy]=(float)amount;return wallet.Spend(cost);}Entry(id).amount=Math.Max(0,Get(id)-amount);return true;}
        public bool CanSpend(MaterialQuantity[] amounts,double multiplier=1){if(multiplier<0||!double.IsFinite(multiplier))return false;foreach(var value in amounts){if(value.amount<0||!double.IsFinite(value.amount))return false;double required=0;foreach(var item in amounts)if(item.resource==value.resource)required+=item.amount*multiplier;if(Get(value.resource)+1e-9<required)return false;}return true;}
        public bool Spend(MaterialQuantity[] amounts,double multiplier=1){if(!CanSpend(amounts,multiplier))return false;foreach(var value in amounts)Spend(value.resource,value.amount*multiplier);return true;}
        public double RunRecipe(IndustryRecipe recipe,double seconds,double efficiency=1){
            if(seconds<0||!double.IsFinite(seconds)||efficiency<0||!double.IsFinite(efficiency))throw new ArgumentOutOfRangeException(nameof(seconds));double batches=seconds*efficiency/recipe.seconds;
            foreach(var input in recipe.inputs)if(input.amount>0)batches=Math.Min(batches,Get(input.resource)/input.amount);
            foreach(var output in recipe.outputs)if(output.amount>0)batches=Math.Min(batches,Math.Max(0,Capacity(output.resource)-Get(output.resource))/output.amount);
            if(batches<=1e-12)return 0;foreach(var input in recipe.inputs)Spend(input.resource,input.amount*batches);foreach(var output in recipe.outputs)Deposit(output.resource,output.amount*batches);return batches;
        }
        // Call before serialising a bound inventory, without moving or duplicating its live wallet balance.
        public void CaptureWallet(){if(wallet==null)return;foreach(var resource in catalogue.resources)if(resource.legacy>=0)Entry(resource.id).amount=wallet.Stock[resource.legacy];}
        public void RestoreWallet(){if(wallet==null)return;foreach(var resource in catalogue.resources)if(resource.legacy>=0){double value=0;foreach(var item in stock)if(item.resource==resource.id){value=item.amount;break;}wallet.Stock[resource.legacy]=(float)Math.Min(wallet.Capacity[resource.legacy],Math.Max(0,value));}}
    }
}
