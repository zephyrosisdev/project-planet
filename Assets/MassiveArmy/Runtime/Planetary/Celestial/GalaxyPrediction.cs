using System;
using System.Diagnostics;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    /// <summary>Bounded work per rendered frame. Published trajectories are complete snapshots, with no per-sample allocations.</summary>
    public sealed class GalaxyPrediction
    {
        public const int Samples=160;
        public readonly Double3[] Positions=new Double3[Samples];
        public readonly Double3[] PrimaryPositions=new Double3[Samples];
        readonly Double3[] pending=new Double3[Samples],primaryPending=new Double3[Samples];
        readonly CelestialState[] states;
        readonly GalaxyModel model;
        readonly Stopwatch timer=new Stopwatch();
        int step,reference,pendingPrimary,pendingSecond,pendingFrame;Double3 p,v;double startTime,time,spacing;
        bool running;float next;UniversePosition anchor;
        public int Count {get;private set;}
        public int Primary {get;private set;}
        public int Secondary {get;private set;}
        public int FrameNode {get;private set;}
        public double PrimaryShare {get;private set;}
        double pendingShare;
        public string Status {get;private set;}="PREDICTING MULTI-BODY PATH";
        public double Horizon {get;private set;}
        public UniversePosition Anchor=>anchor;
        public double Timestamp {get;private set;}
        public bool Impact {get;private set;}
        bool pendingImpact,pendingBound;string pendingStatus;double referenceSurface;
        public double MaxSliceMs {get;private set;}
        public GalaxyPrediction(GalaxyModel model){this.model=model;states=new CelestialState[model.Nodes.Length];}
        public void Invalidate(){running=false;Count=0;next=0;}
        public void Tick(PlanetDefinition body,SpacePose pose,bool landed){
            if(landed){running=false;Count=0;Status="LANDED";return;}
            if(!running&&UnityEngine.Time.unscaledTime>=next){
                reference=model.NodeFor(body);referenceSurface=body.radius+Math.Max(body.ReliefBound,body.oceanHeight)+8;startTime=time=model.Time;model.Evaluate(time,states);var here=states[reference].position+pose.position;
                pendingPrimary=model.Strongest(here,states,out pendingShare,out pendingSecond);
                pendingFrame=pendingPrimary;
                if(model.Nodes[pendingPrimary].kind==CelestialKind.Star&&model.Nodes[pendingSecond].kind==CelestialKind.Star&&model.Nodes[pendingPrimary].parent==model.Nodes[pendingSecond].parent)pendingFrame=model.Nodes[pendingPrimary].parent;
                var primary=model.Nodes[pendingPrimary];var rel=here-states[pendingPrimary].position;var vel=pose.linearVelocity+states[reference].velocity-states[pendingPrimary].velocity;
                var osculating=PlanetOrbit.Calculate(rel,vel,primary.mu);double horizon=osculating.Valid&&osculating.Bound?osculating.Period:Math.Max(300,rel.Magnitude/Math.Max(1,vel.Magnitude)*2);
                pendingBound=osculating.Valid&&osculating.Bound;horizon=Math.Max(60,Math.Min(model.Settings.predictionHorizon,horizon));spacing=horizon/(Samples-1);
                p=pose.position;v=pose.linearVelocity;step=0;pendingImpact=false;pendingStatus="PREDICTED COAST · "+(pendingShare<.95?"MULTI-BODY":"PRIMARY DOMINATED");running=true;
            }
            if(!running)return;timer.Restart();
            while(step<Samples){
                double target=startTime+step*spacing;
                while(time<target-1e-8){model.Evaluate(time,states);double h=Math.Min(target-time,model.SafeStep(states[reference].position+p,states));model.Integrate(ref p,ref v,reference,time,h,new Double3());time+=h;
                    var integrated=model.IntegrationStates;var point=integrated[reference].position+p;
                    if(CheckImpact(point,integrated)){pending[step]=point;primaryPending[step]=integrated[pendingFrame].position;step++;Publish();MaxSliceMs=Math.Max(MaxSliceMs,timer.Elapsed.TotalMilliseconds);return;}
                    if(timer.Elapsed.TotalMilliseconds>=model.Settings.predictionBudgetMs){MaxSliceMs=Math.Max(MaxSliceMs,timer.Elapsed.TotalMilliseconds);return;}}
                model.Evaluate(time,states);var absolute=states[reference].position+p;pending[step]=absolute;primaryPending[step]=states[pendingFrame].position;step++;
                CheckImpact(absolute,states);
                if(pendingImpact||step==Samples){Publish();break;}
                if(timer.Elapsed.TotalMilliseconds>=model.Settings.predictionBudgetMs)break;
            }
            MaxSliceMs=Math.Max(MaxSliceMs,timer.Elapsed.TotalMilliseconds);
        }
        bool CheckImpact(Double3 point,CelestialState[] ephemeris){
            for(int i=0;i<model.Nodes.Length;i++){var node=model.Nodes[i];if(node.kind==CelestialKind.Barycentre)continue;
                double surface=i==reference?referenceSurface:node.radius*(node.planet>=0?1.03:1);var delta=point-ephemeris[i].position;
                if(Math.Abs(delta.x)>surface||Math.Abs(delta.y)>surface||Math.Abs(delta.z)>surface)continue;
                if(delta.Magnitude<=surface){pendingImpact=true;pendingStatus="PREDICTED IMPACT · "+node.name;return true;}
            }return false;
        }
        void Publish(){
            Count=step;Array.Copy(pending,Positions,step);Array.Copy(primaryPending,PrimaryPositions,step);Primary=pendingPrimary;Secondary=pendingSecond;FrameNode=pendingFrame;PrimaryShare=pendingShare;Impact=pendingImpact;Horizon=time-startTime;Timestamp=startTime;anchor=model.Origin;Status=pendingStatus;
            if(!Impact&&Count>1){double initial=(Positions[0]-PrimaryPositions[0]).Magnitude,final=(Positions[Count-1]-PrimaryPositions[Count-1]).Magnitude;
                if(final>initial*1.5)Status="OUTBOUND / ENCOUNTER POSSIBLE";
                else if(pendingBound&&final<initial*1.5&&PrimaryShare>=.95)Status="PREDICTED BOUND · FINITE WINDOW";
            }
            running=false;next=UnityEngine.Time.unscaledTime+.5f;
        }
    }
}
