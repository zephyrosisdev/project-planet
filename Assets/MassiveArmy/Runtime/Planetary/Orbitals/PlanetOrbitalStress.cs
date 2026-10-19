using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    // Explicit benchmark fixture; normal games create all assets through paid launch orders.
    internal static class PlanetOrbitalStress {
        public static readonly bool Enabled=Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-orbital-stress")>=0;
        public static readonly bool Control=Enabled&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-orbital-control")>=0;
        internal static int Seed(PlanetDevelopment world){if(!Enabled)return 0;var sim=world.Base.Civilisation;var d=world.definition;
            for(int i=0;i<32;i++){double y=1-2*(i+.5)/32,r=Math.Sqrt(1-y*y),phi=i*2.399963229728653;var u=new Double3(r*Math.Cos(phi),y,r*Math.Sin(phi));var v=Double3.Cross(new Double3(0,1,0),u);v=v*(1/v.Magnitude);
                sim.Orbitals.Register(new OrbitalAssetState{id=sim.State.AllocateId(),body=sim.BodyId(d.bodyId),faction=0,kind=(i&1)==0?OrbitalPayloadKind.ResearchStation:OrbitalPayloadKind.Warehouse,radius=d.radius+Math.Max(20000,d.ReliefBound+5000),mu=d.gravitationalParameter,u=u,v=v,epoch=-20,insertion=0,lastAccounted=0});}
            return 32;
        }
    }
}
