using System;
using System.Collections.Generic;
namespace MassiveArmy.Industrial {
    [Serializable] public sealed class IndustrialPowerEnvironment {public int body;public double sunX,sunY=1,sunZ,wind;}
    public sealed partial class IndustrialWorld {
        public List<IndustrialPowerEnvironment> powerEnvironments=new List<IndustrialPowerEnvironment>();
        readonly Dictionary<int,IndustrialPowerEnvironment> powerEnvironmentIndex=new Dictionary<int,IndustrialPowerEnvironment>();
        IndustrialPowerEnvironment PowerEnvironment(int body){if(powerEnvironmentIndex.Count!=powerEnvironments.Count){powerEnvironmentIndex.Clear();foreach(var value in powerEnvironments)powerEnvironmentIndex[value.body]=value;}return powerEnvironmentIndex.TryGetValue(body,out var result)?result:null;}
        readonly List<IndustrialMachine> powerBatteries=new List<IndustrialMachine>();
        public void SetPowerEnvironment(int body,double x,double y,double z,double wind){if(!Finite(x)||!Finite(y)||!Finite(z)||!Finite(wind))throw new ArgumentException("Invalid power environment");var env=PowerEnvironment(body);if(env==null){env=new IndustrialPowerEnvironment{body=body};powerEnvironments.Add(env);powerEnvironmentIndex[body]=env;}double length=Math.Sqrt(x*x+y*y+z*z);env.sunX=length>0?x/length:0;env.sunY=length>0?y/length:0;env.sunZ=length>0?z/length:0;env.wind=Math.Max(0,Math.Min(1,wind));}
        void BeginPower(){
            powerBudget.Clear();powerBatteries.Clear();foreach(var m in machines){if(m.definition!="generator"&&m.definition!="solar-panel"&&m.definition!="wind-turbine"&&m.definition!="battery")continue;m.powerAvailable=m.powerUsed=m.chargePower=0;if(m.definition=="battery")powerBatteries.Add(m);if(!m.enabled||m.integrity<=0)continue;
                if(m.definition=="generator"){if(m.progress==0&&m.input.Remove("fuel",1))m.progress=100;m.powerAvailable=m.progress>0?m.Definition.generation:0;if(m.progress>0)m.progress--;m.status=m.powerAvailable>0?"Generating":"Needs fuel";}
                else if(m.definition=="solar-panel"||m.definition=="wind-turbine"){
                    var env=PowerEnvironment(m.pose.body);if(env==null){m.status="Awaiting environment";continue;}
                    if(m.definition=="solar-panel"){double length=Math.Sqrt(m.pose.x*m.pose.x+m.pose.y*m.pose.y+m.pose.z*m.pose.z);double sunlight=length>0?(m.pose.x*env.sunX+m.pose.y*env.sunY+m.pose.z*env.sunZ)/length:0;m.powerAvailable=m.Definition.generation*Math.Max(0,Math.Min(1,(sunlight-.05)/.95));m.status=m.powerAvailable>0?"Solar generation":"No sunlight";}
                    else{m.powerAvailable=m.Definition.generation*env.wind;m.status=m.powerAvailable>0?"Wind generation":"No usable wind";}
                }else if(m.definition=="battery"){m.energy=Math.Max(0,Math.Min(MachineDefinition.BatteryCapacity,m.energy));m.powerAvailable=Math.Min(m.Definition.generation,m.energy/.1);m.status=m.energy>0?"Battery ready":"Battery empty";}
                else continue;
                powerBudget[m.id]=m.powerAvailable;
            }
        }
        void FinishPower(){
            foreach(var battery in powerBatteries){if(!battery.enabled||battery.integrity<=0)continue;double available=powerBudget.TryGetValue(battery.id,out var left)?left:0;battery.powerUsed=Math.Max(0,battery.powerAvailable-available);battery.energy=Math.Max(0,battery.energy-battery.powerUsed*.1);
                var source=Machine(battery.powerSource);if(source!=null&&source.definition!="battery"&&powerBudget.TryGetValue(source.id,out double surplus)){double charge=Math.Min(30,Math.Min(surplus,(MachineDefinition.BatteryCapacity-battery.energy)/(.1*.9)));battery.chargePower=Math.Max(0,charge);battery.energy+=battery.chargePower*.1*.9;powerBudget[source.id]-=battery.chargePower;}
                battery.status=battery.chargePower>0?"Charging":battery.powerUsed>0?"Discharging":battery.energy>=MachineDefinition.BatteryCapacity-.001?"Battery full":battery.energy>0?"Battery ready":"Battery empty";
            }
        }
    }
}
