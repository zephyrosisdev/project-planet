using System;
using System.Collections.Generic;
namespace MassiveArmy.Industrial {
    public sealed partial class IndustrialWorld {
        void StepReservoir(IndustrialMachine reservoir){var dest=Machine(reservoir.waterTarget);if(dest==null||dest==reservoir){reservoir.status="Connect water output";return;}if(reservoir.water<=0){reservoir.status="Reservoir empty · fill manually or supply a pump";return;}long amount=Math.Min(10,Math.Min(reservoir.water,10000-dest.water));if(amount<=0){reservoir.status="Destination reservoir full";return;}reservoir.water-=amount;dest.water+=amount;reservoir.status="Feeding water";}
        void StepPump(IndustrialMachine m,Dictionary<long,double> available){
                    var dest=Machine(m.waterTarget);if(dest==null||dest==m){m.status="Connect water output to a destination";return;}
                    if(!available.TryGetValue(m.powerSource,out double pumpPower)||pumpPower<2){m.status="No power · needs 2 kW";return;}
                    long room=Math.Min(10,10000-dest.water);if(room<=0){m.status="Destination reservoir full";return;}
                    long carried=Math.Min(room,m.input.Count("water"));var supply=m.waterIntake==0?null:Deposit(m.waterIntake);long drawn=supply!=null&&supply.resource=="water"&&supply.remaining.activity==0&&m.pose.Distance(supply.pose)<=8?Math.Min(room-carried,supply.remaining.litres):0;
                    if(carried+drawn<=0){m.status=supply!=null&&supply.remaining.litres==0?"Water deposit depleted":"Load water or select a nearby WATER intake";return;}
                    m.input.Remove("water",carried);if(drawn>0){supply.remaining.Take(drawn);supply.removed+=drawn;}dest.water+=carried+drawn;available[m.powerSource]-=2;m.powerUsed=2;m.status="Pumping";return;
                
        }
    }
}
