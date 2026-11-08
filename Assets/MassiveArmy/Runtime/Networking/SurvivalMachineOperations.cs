using System;
using MassiveArmy.Industrial;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        void MachineOperationsGUI(IndustrialMachine machine){
            void Command(string kind,long source=0,long count=0)=>Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind=kind,source=source,target=machine.id,count=count}});
            if(machine.Definition.shielding>0&&GUILayout.Button(machine.closed?"OPEN SHIELDING":"CLOSE SHIELDING"))Command("shield");
            if(machine.definition=="pump")foreach(var entity in visible.Values)if(entity.kind=="deposit"){
                var deposit=JsonUtility.FromJson<SurfaceDeposit>(entity.data);if(deposit.resource=="water"&&machine.pose.Distance(deposit.pose)<=8&&GUILayout.Button("Connect water intake #"+deposit.id))Command("pump-intake",deposit.id);
            }
            if(machine.definition=="excavator")foreach(var entity in visible.Values)if(entity.kind=="deposit"){
                var deposit=JsonUtility.FromJson<SurfaceDeposit>(entity.data);if(machine.pose.Distance(deposit.pose)<=8&&GUILayout.Button("Excavate #"+deposit.id))Command("dig-vehicle",deposit.id,Math.Min(200,deposit.remaining.litres));
            }
            if(machine.Definition.storage||machine.definition=="sluice"){
                long deposit=Math.Min(100,latest.self.inventory.bulk.litres);if(deposit>0&&GUILayout.Button("Deposit pay dirt ×"+deposit))Command("bulk-transfer",0,deposit);
                long withdraw=Math.Min(100,machine.input.bulk.litres);if(withdraw>0&&GUILayout.Button("Take pay dirt ×"+withdraw))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="bulk-transfer",source=machine.id,count=withdraw}});
            }
            if(machine.definition=="sluice"||machine.definition=="water-reservoir"){
                long water=Math.Min(100,latest.self.inventory.Count("water"));if(water>0&&GUILayout.Button("Fill water ×"+water))Command("fill-water",0,water);
                if(machine.water>0&&GUILayout.Button("Drain clean water"))Command("drain-water");
            }
            if(machine.definition=="battery"&&machine.energy>0&&GUILayout.Button("Discharge stored energy"))Command("drain-energy");
        }
        void PileOperationsGUI(){
            foreach(var entity in visible.Values)if(entity.kind=="pile"&&latest.self.pose.Distance(entity.pose)<=12){var pile=JsonUtility.FromJson<DirtPile>(entity.data);if(pile.owner!=ActorId)continue;GUILayout.Label("STOCKPILE #"+pile.id);
                if(!pile.tailingsPile){long take=Math.Min(100,pile.material.litres),put=Math.Min(100,latest.self.inventory.bulk.litres);if(take>0&&GUILayout.Button("Scoop pay dirt ×"+take))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="scoop",source=pile.id,count=take}});if(put>0&&GUILayout.Button("Dump pay dirt ×"+put))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="dump",target=pile.id,count=put}});}
            }
        }
        void CraftQueueGUI(){
            void Job(IndustrialCraftJob job){if(job!=null&&GUILayout.Button("CANCEL CRAFT · "+job.recipe+" #"+job.id))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="cancel-craft",target=job.id}});}
            Job(latest.self.craft);foreach(var job in latest.self.craftQueue)Job(job);
        }
    }
}
