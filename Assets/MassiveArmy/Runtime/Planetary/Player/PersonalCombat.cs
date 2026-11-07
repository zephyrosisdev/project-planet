using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetEncounter {
        GraphicsBuffer personalPick,emptyPersonalCivic;
        internal void EnableCompanions()=>shader.EnableKeyword("PLANET_COMPANIONS");
        internal void PlayerStrike(Double3 eye,Vector3 direction,float range,int amount,float radius){
            if(range<=0||amount<=0)return;personalPick??=Buffer(2,4);personalPick.SetData(new[]{uint.MaxValue,uint.MaxValue});emptyPersonalCivic??=Buffer(1,48);var civic=world.Base.Citizens?.CivicStates;int count=civic!=null?world.Base.Citizens.Count:0;
            shader.SetVector("_PlayerStrikeEye",new Vector4(PlanetPrecisionPolicy.Direction(eye).x,PlanetPrecisionPolicy.Direction(eye).y,PlanetPrecisionPolicy.Direction(eye).z,(float)(eye.Magnitude-world.definition.radius)));shader.SetVector("_PlayerStrikeDirection",direction.normalized);shader.SetFloat("_PlayerStrikeRange",Mathf.Min(4000,range));shader.SetFloat("_PlayerStrikeRadius",radius);shader.SetInt("_PlayerStrikeDamage",amount);shader.SetInt("_Count",world.unitCount);shader.SetInt("_CivicCount",count);shader.SetFloat("_CivicClock",(float)(world.Base.Civilisation?.RenderTime??0));
            foreach(string name in new[]{"PickPersonalArmy","PickPersonalCivilian","ApplyPersonalStrike"}){int kernel=shader.FindKernel(name);shader.SetBuffer(kernel,"_PersonalPick",personalPick);shader.SetBuffer(kernel,"_UnitRead",world.ArmyForCivicCombat);shader.SetBuffer(kernel,"_Types",world.ArchetypesForCivicCombat);shader.SetBuffer(kernel,"_Units",world.ArmyForCivicCombat);shader.SetBuffer(kernel,"_Damage",damage);shader.SetBuffer(kernel,"_CivicStates",civic??emptyPersonalCivic);shader.SetBuffer(kernel,"_Counters",counters);}
            Dispatch("PickPersonalArmy",world.unitCount);if(count>0)Dispatch("PickPersonalCivilian",count);int apply=shader.FindKernel("ApplyPersonalStrike");shader.Dispatch(apply,1,1,1);
        }
    }
}
