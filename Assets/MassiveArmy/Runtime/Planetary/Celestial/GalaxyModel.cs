using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public enum CelestialKind { BlackHole,Barycentre,Star,Planet,Moon,Asteroid,Artificial }
    public enum StellarClass { RedDwarf,YellowDwarf,BlueStar,RedGiant,WhiteDwarf }
    public sealed class CelestialBody
    {
        public string stableId;
        public double Mass=>mu/6.67430e-11;
        public double SurfaceGravity=>radius>0?mu/(radius*radius):0;
        public double rotationRate;
        public CelestialEnvironment environment;
        public PlanetResourceProfile composition;
        public int id,parent=-1,system=-1,planet=-1;
        public string name;
        public CelestialKind kind;
        public StellarClass stellarClass;
        public double mu,radius,semiMajor,eccentricity,phase,meanMotion;
        public Quaternion plane=Quaternion.identity;
        public Color colour=Color.white;
    }
    public struct CelestialState {public Double3 position,velocity,acceleration;}
    /// <summary>Seeded, prescribed Kepler hierarchy. Ships feel summed Newtonian sources, never a switched primary's gravity.</summary>
    public sealed class GalaxyModel
    {
        public readonly GalaxyDefinition Settings;
        public readonly CelestialBody[] Nodes;
        public readonly int[] PlanetNodes,SystemNodes;
        public readonly CelestialState[] Current;
        public readonly Vector3 HomeSunDirection;
        public UniversePosition Origin {get;private set;}
        public double Time {get;private set;}
        readonly Dictionary<int,int> planetLookup=new Dictionary<int,int>();
        readonly CelestialState[] scratch;
        internal CelestialState[] IntegrationStates=>scratch;
        readonly int[][] systemSources;
        readonly double[] systemMass,systemExtent;
        public GalaxyModel(GalaxyDefinition settings,PlanetSystemDefinition local,PlanetDefinition home,out PlanetDefinition[] planets,Vector3? startingSunDirection=null)
        {
            Settings=settings;uint random=unchecked((uint)(settings.seed^local.seed^home.terrainSeed))|1;
            uint Next(){random^=random<<13;random^=random>>17;random^=random<<5;return random;}
            double Value()=>(Next()>>8)/16777216.0;
            int systems=Mathf.Clamp(settings.starSystems,2,256),perSystem=Mathf.Clamp(settings.planetsPerSystem,1,6);
            var nodes=new List<CelestialBody>();var defs=new List<PlanetDefinition>();var indices=new List<int>();
            nodes.Add(new CelestialBody{id=0,name="Galactic black hole",kind=CelestialKind.BlackHole,mu=Math.Max(1e16,settings.blackHoleMu),radius=Math.Max(1000,2*settings.blackHoleMu/(299792458.0*299792458.0))});
            SystemNodes=new int[systems];systemSources=new int[systems][];systemMass=new double[systems];systemExtent=new double[systems];
            for(int s=0;s<systems;s++){
                double minimumGalaxyRadius=Math.Max(settings.systemOrbitRadius,Math.Max(500000,settings.binarySeparation)*12)*Math.Pow(1.7,perSystem-1)*systems;
                double ring=Math.Max(minimumGalaxyRadius,Math.Max(settings.systemOrbitRadius*100,settings.galaxyRadius))*(.18+.82*Math.Sqrt((s+.5)/systems));
                double angle=s%Mathf.Clamp(settings.spiralArms,2,6)*Math.PI*2/Mathf.Clamp(settings.spiralArms,2,6)+Math.Log(ring/Math.Max(1,settings.galaxyRadius))*.9+(Value()-.5)*.3;
                int bary=nodes.Count;SystemNodes[s]=bary;
                nodes.Add(new CelestialBody{id=bary,name=s==0?"Home binary":"System "+s,kind=CelestialKind.Barycentre,parent=0,system=s,semiMajor=ring,phase=angle,plane=Quaternion.Euler((float)(Value()-.5)*6,0,(float)(Value()-.5)*6),meanMotion=Math.Sqrt(nodes[0].mu/(ring*ring*ring))});
                bool binary=s==0||Value()<settings.binaryFraction;
                double massA=Math.Max(1e13,settings.stellarMu)*(.6+Value()*.8),massB=binary?massA*(.4+Value()*.6):0,total=massA+massB;
                double separation=Math.Max(500000,settings.binarySeparation),phase=Value()*Math.PI*2,n=Math.Sqrt(total/(separation*separation*separation));
                var sources=new List<int>();
                for(int k=0;k<(binary?2:1);k++){
                    int index=nodes.Count;var type=(StellarClass)(Next()%5);double mu=k==0?massA:massB;
                    double radius=type==StellarClass.RedGiant?160000:type==StellarClass.WhiteDwarf?22000:type==StellarClass.BlueStar?95000:65000;
                    var colour=type==StellarClass.RedDwarf?new Color(1,.3f,.12f):type==StellarClass.RedGiant?new Color(1,.52f,.2f):type==StellarClass.BlueStar?new Color(.4f,.65f,1):type==StellarClass.WhiteDwarf?new Color(.8f,.9f,1):new Color(1,.88f,.58f);
                    nodes.Add(new CelestialBody{id=index,parent=bary,system=s,name=(s==0?"Home":("S"+s))+" "+(k==0?"A":"B"),kind=CelestialKind.Star,stellarClass=type,colour=colour,mu=mu,radius=radius,semiMajor=binary?separation*(k==0?massB:massA)/total:0,meanMotion=n,phase=phase+k*Math.PI});sources.Add(index);
                }
                double first=Math.Max(settings.systemOrbitRadius,separation*12);
                for(int p=0;p<perSystem;p++){
                    int pi=defs.Count,index=nodes.Count;var d=s==0&&p==0?home:UnityEngine.Object.Instantiate(home);
                    if(pi!=0){d.bodyId=home.bodyId+pi;d.terrainSeed=(int)(Next()&0x7fffffff);d.radius=Mathf.Lerp(Mathf.Max(20000,local.radiusRange.x),Mathf.Max(20000,local.radiusRange.y),(float)Value());d.terrainHeight=d.radius*.015f;d.oceanHeight=Mathf.Lerp(-200,120,(float)Value());d.gravitationalParameter=9.81*d.radius*d.radius;d.atmosphereHeight=d.radius/12;}
                    d.displayName=pi==0?home.displayName:("S"+s+" · Planet "+(p+1));d.name=d.displayName;
                    double a=first*Math.Pow(1.7,p),e=p==0?0:Value()*.12;
                    // Circumbinary planets orbit the common barycentre, outside the binary separation.
                    nodes.Add(new CelestialBody{id=index,parent=bary,system=s,planet=pi,name=d.displayName,kind=CelestialKind.Planet,mu=d.gravitationalParameter,radius=d.radius,semiMajor=a,eccentricity=e,meanMotion=Math.Sqrt(total/(a*a*a)),phase=pi==0?Math.Atan2(-1.3,-1):Value()*Math.PI*2,plane=Quaternion.Euler((float)(Value()-.5)*8,0,(float)(Value()-.5)*8)});
                    indices.Add(index);defs.Add(d);sources.Add(index);systemExtent[s]=Math.Max(systemExtent[s],a*(1+e));
                }
                systemSources[s]=sources.ToArray();foreach(int i in sources)systemMass[s]+=nodes[i].mu;
            }
            Nodes=nodes.ToArray();PlanetNodes=indices.ToArray();planets=defs.ToArray();Current=new CelestialState[Nodes.Length];scratch=new CelestialState[Nodes.Length];
            HomeSunDirection=(startingSunDirection??new Vector3(1,.12f,1.3f)).normalized;
            Evaluate(0,Current);var homeOffset=Current[PlanetNodes[0]].position-Current[SystemNodes[0]].position;
            var daylight=Quaternion.FromToRotation(homeOffset.Float.normalized,-HomeSunDirection);
            for(int i=0;i<Nodes.Length;i++)if(Nodes[i].system==0&&Nodes[i].kind!=CelestialKind.Barycentre)Nodes[i].plane=daylight*Nodes[i].plane;
            Evaluate(0,Current);Origin=home.position.Add(Current[PlanetNodes[0]].position*-1);
            for(int i=0;i<planets.Length;i++)planetLookup.Add(planets[i].bodyId,PlanetNodes[i]);
            Publish(0,planets);
        }
        public int NodeFor(PlanetDefinition body)=>planetLookup.TryGetValue(body.bodyId,out int index)?index:-1;
        public void Evaluate(double time,CelestialState[] states){
            for(int i=0;i<Nodes.Length;i++){
                var body=Nodes[i];var state=body.parent<0?new CelestialState():states[body.parent];
                if(body.semiMajor>0){double m=Math.IEEERemainder(body.phase+body.meanMotion*time,Math.PI*2),e=body.eccentricity,E=m;
                    if(e>0)for(int k=0;k<8;k++){double correction=(E-e*Math.Sin(E)-m)/(1-e*Math.Cos(E));E-=correction;if(Math.Abs(correction)<1e-13)break;}
                    double c=Math.Cos(E),s=Math.Sin(E),q=Math.Sqrt(1-e*e),a=body.semiMajor,den=1-e*c,rate=body.meanMotion/den;
                    var p=Double3.Rotate(body.plane,new Double3(a*(c-e),0,a*q*s));
                    var v=Double3.Rotate(body.plane,new Double3(-a*s*rate,0,a*q*c*rate));
                    state.position+=p;state.velocity+=v;state.acceleration+=p*(-body.meanMotion*body.meanMotion/(den*den*den));
                }
                states[i]=state;
            }
        }
        public void Publish(double time,PlanetDefinition[] planets){Time=time;Evaluate(time,Current);for(int p=0;p<planets.Length;p++){var state=Current[PlanetNodes[p]];planets[p].position=Origin.Add(state.position);planets[p].velocity=state.velocity;}}
        public Double3 Acceleration(Double3 position,CelestialState[] states){
            Double3 Force(int i){var d=states[i].position-position;double r=Math.Max(Nodes[i].radius,d.Magnitude);return d*(Nodes[i].mu/Math.Max(1,r*r*r));}
            Double3 result=Force(0);
            for(int s=0;s<SystemNodes.Length;s++){
                var d=states[SystemNodes[s]].position-position;double r=Math.Max(1,d.Magnitude),extent=systemExtent[s];
                double blend=Math.Max(0,Math.Min(1,(r/Math.Max(1,extent)-6)/2));blend=blend*blend*(3-2*blend);
                if(blend>0)result+=d*(systemMass[s]*blend/(r*r*r));
                if(blend<1)foreach(int i in systemSources[s])result+=Force(i)*(1-blend);
            }
            return result;
        }
        public Double3 RelativeAcceleration(Double3 local,int reference,double time){Evaluate(time,scratch);return Acceleration(scratch[reference].position+local,scratch)-scratch[reference].acceleration;}
        // Velocity Verlet with a bounded substep chosen from the strongest local dynamical timescale.
        public void Integrate(ref Double3 p,ref Double3 v,int reference,double time,double dt,Double3 thrust){
            double remaining=dt;
            while(remaining>1e-9){Evaluate(time,scratch);var absolute=scratch[reference].position+p;double h=Math.Min(remaining,SafeStep(absolute,scratch));
                var a=Acceleration(absolute,scratch)-scratch[reference].acceleration+thrust;var next=p+v*h+a*(h*h*.5);
                Evaluate(time+h,scratch);var b=Acceleration(scratch[reference].position+next,scratch)-scratch[reference].acceleration+thrust;
                p=next;v+=(a+b)*(h*.5);remaining-=h;time+=h;
            }
        }
        public double SafeStep(Double3 p,CelestialState[] states){double h=600;
            for(int i=0;i<Nodes.Length;i++){if(Nodes[i].mu<=0)continue;double r=Math.Max(Nodes[i].radius,(states[i].position-p).Magnitude);h=Math.Min(h,.025*Math.Sqrt(r*r*r/Nodes[i].mu));}return Math.Max(.01,h);
        }
        // Local dynamical influence (mu/r^3), rather than uniform acceleration shared by the whole system.
        public int Strongest(Double3 p,CelestialState[] states,out double share,out int second){double sum=0,best=-1,next=-1;int index=0;second=0;
            for(int i=0;i<Nodes.Length;i++){if(Nodes[i].mu<=0)continue;double r=Math.Max(Nodes[i].radius,(states[i].position-p).Magnitude),g=Nodes[i].mu/(r*r*r);sum+=g;if(g>best){next=best;second=index;best=g;index=i;}else if(g>next){next=g;second=i;}}share=best/Math.Max(1e-30,sum);return index;}
    }
}
