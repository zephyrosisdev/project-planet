using System;
using System.Linq;
using UnityEngine;
namespace MassiveArmy
{
    public sealed partial class RtsWorld
    {
        void ReceiveDamage(uint[] amounts)
        {
            if(disposed)return;
            for(int i=0;i<Buildings.Count;i++){var b=Buildings[i];if(b.removed||amounts[i]==0)continue;b.health-=amounts[i];if(b.health<=0)Remove(b,true);}
            if(Battle.Settings.spectatorMode){bool cyan=Buildings.Any(b=>!b.removed&&b.team==0&&b.definition.id=="command"),red=Buildings.Any(b=>!b.removed&&b.team==1&&b.definition.id=="command");if(!cyan||!red)Outcome=!cyan&&!red?"DRAW · BOTH COMMAND CENTRES LOST":cyan?"CYAN AI WINS · RED COMMAND LOST":"RED AI WINS · CYAN COMMAND LOST";}
        }
        void Remove(RtsBuilding b,bool destroyed)
        {
            if(b.removed)return;if(destroyed)RegisterAiOutpostLoss(b);b.removed=true;b.health=0;
            RegisterFootprint(b,false);SetObstacle(b,false);RefreshWallConnections(b.position);
            if(b.node>=0)Nodes[b.node].claimedBy=-1;
            foreach(var q in b.queue){if(q.research)Factions[b.team].researching.Remove(q.id);if(!destroyed)Factions[b.team].wallet.Credit(q.paid*(1-q.Fraction));}
            b.queue.Clear();presentation.DestroyBuilding(b,destroyed);Changed?.Invoke(b);
            if(destroyed&&b.definition.id=="fuel_storage") {
                Factions[b.team].wallet.Stock.fuel*=.85f;
                foreach(var other in Buildings.Where(o=>!o.removed&&Field.Distance(o.position,b.position)<55).ToArray()){other.health-=700;if(other.health<=0)Remove(other,true);}
            }
            if(!Battle.Settings.spectatorMode&&b.definition.id=="command"&&!Buildings.Any(o=>!o.removed&&o.team==b.team&&o.definition.id=="command"))Outcome=b.team==0?"DEFEAT · COMMAND INFRASTRUCTURE LOST":"VICTORY · ENEMY COMMAND DESTROYED";
        }
        public void Update(float dt)
        {
            if(disposed||Outcome.Length>0)return;matchTime+=dt;tick+=dt;aiClock+=dt;
            foreach(var b in Buildings)if(!b.removed) {
                if(!b.Complete){float increase=dt*b.Efficiency;b.construction=Mathf.Min(b.definition.time,b.construction+increase);b.health=Mathf.Min(b.MaxHealth,b.health+b.MaxHealth*.9f/b.definition.time*increase);}
                else if(b.Upgrading&&b.powered){b.upgradeProgress+=dt*b.Efficiency;if(b.upgradeProgress>=b.upgradeTime){b.level++;b.health=Mathf.Min(b.MaxHealth,b.health+b.definition.health*.4f);b.upgradeTime=b.upgradeProgress=0;presentation.UpgradeBuilding(b);Changed?.Invoke(b);}}
                else if(!b.paused&&b.powered)TickQueue(b,dt);
            }
            if(tick>=.25f){float step=tick;tick=0;foreach(var f in Factions)Economy(f,step);Capture(step);Gates();Upload();presentation.Update(step);}
            if(EnemyEnabled&&(Battle.Settings.enemyAI||Battle.Settings.spectatorMode)&&aiClock>=3){aiClock=0;aiPending=Battle.Settings.spectatorMode?3:2;}
            if((aiPending&1)!=0){aiPending&=~1;ThinkAI(0);}else if((aiPending&2)!=0){aiPending&=~2;ThinkAI(1);}
        }
        void TickQueue(RtsBuilding b,float dt)
        {
            if(!b.repeat)b.repeatWaiting.Clear();
            for(int i=b.repeatWaiting.Count-1;i>=0;i--)if(Factions[b.team].wallet.CanAfford(Catalog.Units[b.repeatWaiting[i]].cost)&&EnqueueUnit(b,b.repeatWaiting[i]))b.repeatWaiting.RemoveAt(i);
            for(int bay=0;bay<Mathf.Min(b.Bays,b.queue.Count);bay++) {
                var q=b.queue[bay];if(b.awaitingSpawn.Contains(q))continue;q.bay=bay;
                q.progress=Mathf.Min(q.duration,q.progress+dt*b.Efficiency*(1+(b.level-1)*.2f));if(q.progress<q.duration)continue;
                if(q.research){var f=Factions[b.team];f.technologies.Add(q.id);f.researching.Remove(q.id);b.queue.Remove(q);Notice=Catalog.Technologies[q.id].name+" research complete";break;}
                var unit=Catalog.Units[q.id];Vector2 exit=b.position+BuildingGrid.Forward(b.rotation)*(b.definition.footprintDepth*4+unit.radius+12)+new Vector2(BuildingGrid.Forward(b.rotation).y,-BuildingGrid.Forward(b.rotation).x)*bay*7;
                if(unit.movement==MovementClass.Water){if(!WaterExit(b.position,b.definition.radius,out exit))continue;}
                else if(unit.movement!=MovementClass.Air)exit=StructureSafeDestination(exit,(int)unit.movement,b.team);
                Vector2 target=unit.movement==MovementClass.Water&&Field.Deployment.Costs[4*Field.Resolution*Field.Resolution+Field.Map.Index(b.rally)]>=DeploymentMap.Blocked*.5f?exit:b.rally;
                bool queued=Battle.RequestSpawn(Catalog.TypeIndex(q.id),b.team,exit,target,ok=> {
                    if(disposed)return;b.awaitingSpawn.Remove(q);if(!ok){Notice="Army capacity reached; production waiting for a vacant GPU slot";return;}
                    b.queue.Remove(q);if(!b.removed&&b.repeat)b.repeatWaiting.Add(q.id);
                },b.index,b.rallyUnit);if(queued)b.awaitingSpawn.Add(q);
            }
        }
        public float Modifier(RtsFaction f,string name)
        {
            float result=1;foreach(var id in f.technologies)foreach(var text in Catalog.Technologies[id].unlockModifiers){var parts=text.Split(':');if(parts.Length==2&&parts[0]==name&&float.TryParse(parts[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float value))result*=value;}return result;
        }
        void Economy(RtsFaction f,float dt)
        {
            var owned=Buildings.Where(b=>!b.removed&&b.Complete&&b.team==f.team).ToArray();
            f.tier=owned.Where(b=>b.definition.id=="command").Select(b=>b.level).DefaultIfEmpty(1).Max();
            f.wallet.Capacity=new ResourceCost{materials=3000,fuel=1800,rareElements=900,isotopes=200,research=2000};
            f.energyCapacity=0;f.generation=f.demand=0;
            foreach(var b in owned){var d=b.definition;
                if(d.storage>0){int resource=d.id=="fuel_storage"?1:d.id=="rare_storage"?2:d.id=="isotope_storage"?3:0;f.wallet.Capacity[resource]+=d.storage*b.level;}
                if(!b.powered)continue;f.energyCapacity+=d.battery*b.level;f.demand+=d.consumption*(1+(b.level-1)*.2f);
                if(d.generation>0){float use=d.fuelUse*b.level*dt;if(use<=0||f.wallet.Spend(new ResourceCost{fuel=use}))f.generation+=d.generation*(1+(b.level-1)*.65f);}
            }
            float net=f.generation-f.demand;
            if(net>=0)f.battery=Mathf.Min(f.energyCapacity,f.battery+net*dt);
            else{float use=Mathf.Min(f.battery,-net*dt);f.battery-=use;net+=use/Mathf.Max(.001f,dt);}
            f.efficiency=f.demand<=0?1:Mathf.Clamp((f.demand+net)/f.demand,.25f,1);f.battery=Mathf.Min(f.battery,f.energyCapacity);
            if(Battle.Statistics!=null)f.wallet.Spend(new ResourceCost{materials=Battle.Statistics[88+f.team]*dt*.2f});
            foreach(var b in owned){var d=b.definition;b.Efficiency=b.powered?f.efficiency*Modifier(f,"production")*(owned.Any(o=>o.definition.id=="logistics"&&o.powered&&Field.Distance(o.position,b.position)<220)?1.15f:1):0;if(!b.powered)continue;
                if(b.node>=0){var node=Nodes[b.node];float amount=Mathf.Min(node.remaining,d.extraction*node.rateMultiplier*b.level*b.Efficiency*Modifier(f,"extraction")*dt);node.remaining-=f.wallet.Deposit(node.resource,amount);}
                else if(d.id=="synthetic"){float amount=d.extraction*b.level*b.Efficiency*dt;if(f.wallet.Spend(new ResourceCost{materials=amount*.5f}))f.wallet.Deposit(ResourceKind.Fuel,amount);}
                if(d.researchRate>0)f.wallet.Deposit(ResourceKind.Research,d.researchRate*b.level*b.Efficiency*dt);
                if((b.repairing||(Battle.SensorData!=null&&(Battle.SensorData[b.index*4]>>16)>0))&&b.health<b.MaxHealth){float heal=Mathf.Min(b.MaxHealth-b.health,80*b.Efficiency*Modifier(f,"repair")*dt);if(f.wallet.Spend(new ResourceCost{materials=heal*.06f}))b.health+=heal;}
                if(d.id=="repair_depot")b.repairing=f.wallet.Spend(new ResourceCost{materials=2*dt});
                if(d.id=="command"&&f.wallet.Stock.materials<40)f.wallet.Deposit(ResourceKind.Materials,2*dt);
            }
            foreach(var b in Buildings.Where(b=>!b.removed&&!b.Complete&&b.team==f.team))b.Efficiency=Mathf.Max(.35f,f.efficiency);
            for(int i=0;i<5;i++)f.wallet.Stock[i]=Mathf.Min(f.wallet.Stock[i],f.wallet.Capacity[i]);
        }
        void Capture(float dt)
        {
            if(sectorCounts==null)return;
            for(int s=0;s<16;s++){uint a=sectorCounts[s],b=sectorCounts[16+s];if(a==0&&b==0)continue;if(a>0&&b>0){CaptureProgress[s]=Mathf.MoveTowards(CaptureProgress[s],0,dt);continue;}
                int team=a>0?0:1;if(SectorOwner[s]==team){CaptureProgress[s]=0;continue;}CaptureProgress[s]+=dt*Mathf.Min(3,a+b);if(CaptureProgress[s]>=12){SectorOwner[s]=team;CaptureProgress[s]=0;Factions[team].wallet.Deposit(ResourceKind.Research,35);}}
        }
        void Gates()
        {
            var sensors=Battle.SensorData;
            foreach(var b in Buildings)if(!b.removed&&b.Complete&&b.definition.id=="gate"){
                bool traversable=b.powered;
                if(traversable!=b.gateTraversable){b.gateTraversable=traversable;SetObstacle(b,true);PlacementRevision++;}
                bool open=traversable&&sensors!=null&&(sensors[b.index*4]&65535)>0;if(open==b.gateOpen)continue;
                b.gateOpen=open;presentation.SetGate(b);
            }
        }
        void Upload()
        {
            Array.Clear(gpuBuildings,0,gpuBuildings.Length);
            foreach(var b in Buildings)if(!b.removed){var d=b.definition;var f=Factions[b.team];bool enabled=b.Complete&&b.powered&&(f.efficiency>.4f||d.tier<=1);
                gpuBuildings[b.index]=new GpuStructure{position=b.position,radius=d.radius,health=b.health,armour=b.Armour,team=(uint)b.team,range=d.weaponRange*(1+(b.level-1)*.15f),damage=b.holdFire?0:d.weaponDamage*b.level,flags=(enabled?1u:0u)|((d.id=="airbase"||d.id=="helipad")?4u:0u)|((!b.holdFire&&(d.id=="missile_defence"||d.id=="point_defence"))?16u:0u)|((uint)b.priority<<8)|((uint)Mathf.Clamp(Mathf.RoundToInt(d.explosionRadius*4),0,1023)<<20),vision=d.vision*Modifier(f,"vision"),detection=enabled?d.detection*b.level*f.efficiency*Modifier(f,"detection"):0,shield=enabled?d.shield:0,level=(uint)b.level,targetMask=(uint)d.targetMask,reload=d.reload/Mathf.Max(.25f,b.Efficiency),repair=d.id=="repair_depot"&&b.repairing?60*b.Efficiency:0};}
            foreach(var f in Factions)Battle.TeamModifiers[f.team]=new Vector4(Modifier(f,"vision"),Modifier(f,"detection"),Modifier(f,"armour"),Modifier(f,"repair"));
            Battle.UploadStructures(gpuBuildings,Buildings.Count);
        }
    }
}
