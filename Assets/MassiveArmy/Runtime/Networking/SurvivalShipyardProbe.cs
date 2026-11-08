#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.IO;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalSessionProbe {
        int shipStep;SurvivalAction shipReplay;double shipReplayAt;
        ShipAssembly ProbeShip(){return session.Visible.TryGetValue(factorySpec.shipEntity*8+6,out var entity)?JsonUtility.FromJson<ShipAssembly>(entity.data):null;}
        bool ShipBuilt(ShipAssembly ship)=>ship!=null&&ship.modules.Count==factorySpec.shipModules+1&&ship.Module(factorySpec.shipAdded)!=null&&ship.wires.Count==factorySpec.shipWires+1&&ship.wires.Exists(wire=>wire.target==factorySpec.shipAdded);
        bool ShipStock(bool recovered){var inventory=session.Latest.self.inventory;return inventory.Count("hull-panel")==24-(recovered?0:12)&&inventory.Count("electronics")==16-(recovered?0:8)&&inventory.Count("wire")==100-(recovered?0:factorySpec.shipWireCost);}
        bool ShipyardOwner(bool recover){
            var ship=ProbeShip();if(ship==null)return false;
            switch(shipStep){
                case 0:
                    if(recover){if(!ShipBuilt(ship))return false;report.shipyardRestored=true;}
                    if(!FactoryWalkTo(factorySpec.shipGoal))return false;
                    FactorySend(recover?new IndustrialCommand{kind="ship-unwire",source=factorySpec.shipCore,target=factorySpec.shipAdded}:new IndustrialCommand{kind="ship-place",source=factorySpec.shipParent,item="forward",definition="cockpit",operationRole="aft"});shipStep=1;return false;
                case 1:
                    if(!FactoryReplied(true))return false;
                    FactorySend(recover?new IndustrialCommand{kind="ship-remove",source=factorySpec.shipAdded}:new IndustrialCommand{kind="ship-wire",source=factorySpec.shipCore,target=factorySpec.shipAdded});shipStep=2;return false;
                case 2:
                    if(!FactoryReplied(true))return false;
                    if(!recover){shipReplay=factoryRequest;session.RepeatProbeAction(shipReplay);shipReplayAt=Time.realtimeSinceStartupAsDouble;}shipStep=3;return false;
                case 3:
                    if(recover){if(ship.modules.Count!=factorySpec.shipModules||ship.wires.Count!=factorySpec.shipWires||!ShipStock(true)||!File.Exists(FactoryMarker("ship-observer-recovered")))return false;report.shipyardRecovered=report.shipyardConserved=true;return true;}
                    if(!ShipBuilt(ship)||!ShipStock(false)||Time.realtimeSinceStartupAsDouble-shipReplayAt<1||!File.Exists(FactoryMarker("ship-observer-created")))return false;
                    report.shipyardCreated=report.shipyardConserved=true;return true;
            }return false;
        }
        bool ShipyardObserver(bool recover){
            var ship=ProbeShip();if(ship==null)return false;
            foreach(var module in ship.modules)if(module.paid.Count!=0){_ = Finish(false,"Foreign ship construction receipt leaked");return false;}
            foreach(var wire in ship.wires)if(wire.paid.Count!=0){_ = Finish(false,"Foreign power receipt leaked");return false;}report.shipyardPrivacy=true;
            if(recover){
                if(ShipBuilt(ship)){report.shipyardRestored=true;return false;}
                if(report.shipyardRestored&&ship.modules.Count==factorySpec.shipModules&&ship.wires.Count==factorySpec.shipWires){report.shipyardRecovered=true;File.WriteAllText(FactoryMarker("ship-observer-recovered"),"ship recovery replicated");return true;}return false;
            }
            switch(shipStep){
                case 0:if(!ShipBuilt(ship))return false;report.shipyardCreated=true;FactorySend(new IndustrialCommand{kind="ship-place",source=factorySpec.shipParent,item="forward",definition="cockpit",operationRole="aft"});shipStep=1;return false;
                case 1:if(!FactoryReplied(false))return false;FactorySend(new IndustrialCommand{kind="ship-remove",source=factorySpec.shipAdded});shipStep=2;return false;
                case 2:if(!FactoryReplied(false))return false;File.WriteAllText(FactoryMarker("ship-observer-created"),"foreign ship edits rejected");shipStep=3;return true;
                case 3:return true;
            }return false;
        }
    }
}
#endif
