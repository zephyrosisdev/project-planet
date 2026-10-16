using System;
using UnityEngine;
using UnityEngine.VFX;
namespace MassiveArmy.Planetary
{
    // Two strategic VFX emitters, reused from the original combat graph.
    public sealed class PlanetNuclearPresentation:IDisposable
    {
        readonly PlanetDevelopment world;readonly PlanetSurface surface;readonly VisualEffect[] effects=new VisualEffect[2];readonly Vector3[] directions=new Vector3[2];readonly VFXEventAttribute[] attributes=new VFXEventAttribute[2];VisualEffectAsset graph;int next;
        public PlanetNuclearPresentation(PlanetDevelopment world){this.world=world;surface=new PlanetSurface(world.definition);if(world.StrategyEnabled)for(int i=0;i<effects.Length;i++)Prepare(i);}
        void Prepare(int slot){if(effects[slot]!=null)return;if(graph==null)graph=Resources.Load<VisualEffectAsset>("MassiveArmy/CombatArt/NuclearBurst");var go=new GameObject("Planet nuclear mushroom cloud");go.transform.SetParent(world.transform);var fx=effects[slot]=go.AddComponent<VisualEffect>();fx.initialEventName="Dormant";fx.visualEffectAsset=graph;attributes[slot]=fx.CreateVFXEventAttribute();directions[slot]=world.Rig.FocusDirection;Place(slot);}
        public void Play(Vector3 direction,float radius){int slot=next++%2;Prepare(slot);var fx=effects[slot];directions[slot]=direction;Place(slot);fx.transform.localScale=Vector3.one*Mathf.Clamp(radius/144,.33f,2.8f);fx.Reinit();var attr=attributes[slot];attr.SetVector3("position",Vector3.zero);attr.SetVector3("velocity",Vector3.up*24);attr.SetVector3("color",new Vector3(2,.8f,.08f));attr.SetFloat("size",Mathf.Clamp(radius/144,.33f,2.8f));attr.SetFloat("lifetime",65);fx.SendEvent("OnPlay",attr);}
        void Place(int i){var d=directions[i];effects[i].transform.SetPositionAndRotation(world.RenderBodyPosition(surface.GetSurfacePositionPrecise(d)),Quaternion.FromToRotation(Vector3.up,d));}
        public void Update(){for(int i=0;i<effects.Length;i++)if(effects[i]!=null)Place(i);}
        public void Dispose(){foreach(var attr in attributes)attr?.Dispose();foreach(var fx in effects)if(fx!=null)ArmyLifetime.Destroy(fx.gameObject);}
    }
}
