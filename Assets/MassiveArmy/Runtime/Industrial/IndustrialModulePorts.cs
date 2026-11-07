using System;
using System.Collections.Generic;
namespace MassiveArmy.Industrial {
    [Serializable] public sealed class IndustrialModulePorts {public long module,site,owner;public FramePose pose,input,output;}
    public sealed partial class IndustrialWorld {
        internal void SyncModulePorts(int body,List<IndustrialModulePorts> ports){
            foreach(var port in ports){IndustrialMachine machine=null;foreach(var m in machines)if(m.integratedLogistics&&m.attachmentModule==port.module&&m.pose.body==body){machine=m;break;}
                if(machine==null){machine=new IndustrialMachine{id=AllocateId(),definition="store",owner=port.owner,integratedLogistics=true,input=new Inventory{capacity=2000},output=new Inventory{capacity=2000}};machines.Add(machine);}
                machine.pose=port.pose;machine.attachmentModule=port.module;machine.attachmentSite=port.site;machine.customConveyorPorts=true;machine.conveyorInput=port.input;machine.conveyorOutput=port.output;foreach(var belt in belts)if(belt.source==machine.id||belt.target==machine.id)belt.path=null;
            }
            for(int i=machines.Count-1;i>=0;i--){var m=machines[i];if(!m.integratedLogistics||m.externalVehicle!=0||m.pose.body!=body||ports.Exists(p=>p.module==m.attachmentModule))continue;if(m.input.Used!=0||belts.Exists(b=>b.source==m.id||b.target==m.id))throw new InvalidOperationException("Empty module logistics and recover connected belts before removing it");machines.RemoveAt(i);}connectionRevision++;
        }
    }
}

