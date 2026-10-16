using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy
{
    public sealed partial class GpuBattle
    {
        readonly Vector2[] factionObjectives=new Vector2[2];readonly bool[] factionAdvance=new bool[2];
        Vector2 regularAiObjective;bool regularAiAdvance;
        public void RequestAiAdvance(int team,Vector2 objective){if(Settings.spectatorMode){RequestFactionAdvance(team,objective);return;}if(!Settings.baseBuilding||!Settings.enemyAI||team!=1)return;regularAiObjective=Field.Canonical(objective);regularAiAdvance=true;}
        public void RequestFactionAdvance(int team,Vector2 objective){if(!Settings.spectatorMode)return;factionObjectives[team]=Field.Canonical(objective);factionAdvance[team]=true;}
        void RecordFactionAdvances()
        {
            if(!Settings.spectatorMode&&regularAiAdvance){regularAiAdvance=false;if(Navigation.SetFactionOrder(cmd,1,1,2,regularAiObjective))Navigation.Reset(cmd,1);Dispatch("AssignClearanceOrders",Settings.activeUnits);}
            if(!Settings.spectatorMode)return;
            for(int team=0;team<2;team++)if(factionAdvance[team]){
                factionAdvance[team]=false;
                for(int movement=0;movement<5;movement++){
                    Vector2 target=movement==3?factionObjectives[team]:Field.TraversableDestination(factionObjectives[team]);
                    if(movement==4){bool found=false;for(int ring=0;ring<40&&!found;ring++)for(int k=0;k<16&&!found;k++){
                        float angle=k*Mathf.PI/8;var p=Field.Canonical(factionObjectives[team]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*ring*Field.Size/Field.Resolution);
                        if(Field.Deployment.Costs[4*Field.Resolution*Field.Resolution+Field.Map.Index(p)]<DeploymentMap.Blocked*.5f){target=p;found=true;}}
                        if(!found)continue;
                    }
                    int group=2+team*5+movement;if(Navigation.SetFactionOrder(cmd,group,team,movement,target))Navigation.Reset(cmd,group);
                }
                cmd.SetComputeIntParam(shader,"_FactionTeam",team);Dispatch("AssignFactionOrders",Settings.activeUnits);
            }
        }
    }
    public sealed partial class BattleApp
    {
        int observedTeam;
        public bool Spectator=>settings.spectatorMode;
    }
}
