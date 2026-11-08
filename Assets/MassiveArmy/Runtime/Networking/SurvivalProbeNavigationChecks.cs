#if UNITY_EDITOR || UNITY_SERVER
using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunProbeNavigationChecks(Action<bool,string> check){
            var go=new GameObject("Collision-aware diagnostic controls fixture");var session=go.AddComponent<SurvivalNetworkSession>();var definition=ScriptableObject.CreateInstance<PlanetDefinition>();
            try{
                definition.radius=10000;definition.terrainHeight=0;definition.stampTerrain=false;session.HeadlessProbeClient=true;session.headlessDefinition=definition;session.Config=new SurvivalServerConfig();
                session.predicted=new SurvivalMotion{actor=1,pose=new FramePose{body=1,x=1.36,y=10000.05,z=-1.36},grounded=true,forwardZ=1};session.latest=new SurvivalSnapshot{self=new IndustrialActor{id=1}};
                session.clientContacts.Put(new SurvivalContacts.Shape{id=1,body=1,origin=new Double3(0,10000,0),rotation=Quaternion.identity,radius=4,solids=new[]{BuildSolid.Box(-1,1,-1,1,0,3)}});
                var goal=new FramePose{body=1,x=-3,y=10000.05,z=20};var before=session.predicted.pose;var control=session.ProbeWalkingControls(goal);var moving=session.predicted.Copy();
                for(int i=0;i<10;i++)session.StepWalking(moving,new SurvivalInput{sequence=(uint)(i+1),x=control.x,y=control.y},new PlanetSurface(definition),1f/30,7,false);
                check(control.x>0&&moving.pose.Distance(goal)<before.Distance(goal)-.05,"Diagnostic walking steers around a convex machine corner through ordinary collision-checked inputs");
                check(session.predicted.pose.Distance(before)==0,"Diagnostic route evaluation does not move the live player, change authority or advance simulation time");
            }finally{session.headlessDefinition=null;DestroyImmediate(definition);DestroyImmediate(go);}
        }
    }
}
#endif
