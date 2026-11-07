using System;
using UnityEngine;
using UnityEngine.UIElements;
using MassiveArmy.Industrial;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        [Header("Navigation HUD")] public bool showNavigationCompass=true;
        internal bool CompassOrder(out UniversePosition position,out string name){
            position=default;name=null;var g=Government;if(g==null||trackedTribeOrder==0)return false;TribeOrder order=null;foreach(var candidate in g.orders)if(candidate.id==trackedTribeOrder){order=candidate;break;}if(order==null||!g.OrderLive(order)||!g.OrderVisible(1,order)||order.location.frame!=0)return false;
            PlanetDefinition body=definition;if(body.bodyId!=order.location.body){body=null;if(SystemRuntime?.Bodies!=null)foreach(var candidate in SystemRuntime.Bodies)if(candidate.bodyId==order.location.body){body=candidate;break;}}if(body==null)return false;
            position=body.position.Add(new Double3(order.location.x,order.location.y,order.location.z));name=order.kind+" · "+order.text;return true;
        }
    }
    /// <summary>Retained UI only; bounded ephemeris references, no GPU readbacks or world marker objects.</summary>
    internal sealed class PlanetNavigationCompass:VisualElement {
        const int PlanetLimit=12,MarkerLimit=14;
        enum Icon {Planet,Ship,Order}
        struct Marker {internal long id;internal float x,y,size,bearing,pitch;internal Color color;internal Icon icon;internal bool hidden;}
        sealed class Tape:VisualElement {
            internal readonly Marker[] markers=new Marker[MarkerLimit];internal int count;internal float heading;
            internal Tape(){pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;}
            internal static float Arc(float x,float width)=>56+(float)NavigationCompassMath.ArcHeight((x-width*.5f)/Mathf.Max(1,width*.5f-18));
            void Draw(MeshGenerationContext context){float width=contentRect.width;if(width<50)return;var p=context.painter2D;float half=width*.5f-18;
                p.lineWidth=1;p.strokeColor=new Color(.35f,.65f,.8f,.55f);p.BeginPath();for(int i=0;i<=48;i++){float x=18+i*(width-36)/48;var point=new Vector2(x,Arc(x,width));if(i==0)p.MoveTo(point);else p.LineTo(point);}p.Stroke();
                for(int absolute=0;absolute<360;absolute+=15){float bearing=(float)NavigationCompassMath.Wrap(absolute-heading);float x=width*.5f+bearing/180*half,y=Arc(x,width);p.BeginPath();p.MoveTo(new Vector2(x,y));p.LineTo(new Vector2(x,y+(absolute%90==0?10:5)));p.Stroke();}
                p.strokeColor=new Color(.8f,.95f,1);p.lineWidth=2;p.BeginPath();p.MoveTo(new Vector2(width*.5f-5,47));p.LineTo(new Vector2(width*.5f,52));p.LineTo(new Vector2(width*.5f+5,47));p.Stroke();
                for(int i=0;i<count;i++){var m=markers[i];if(m.hidden)continue;var c=new Vector2(m.x,m.y);float r=m.size*.5f;p.lineWidth=1.6f;p.strokeColor=m.color;
                    if(m.icon==Icon.Planet){Circle(p,c,r,18);p.BeginPath();p.MoveTo(c+new Vector2(-r*1.35f,r*.45f));p.LineTo(c+new Vector2(r*1.35f,-r*.45f));p.Stroke();}
                    else if(m.icon==Icon.Ship){p.BeginPath();p.MoveTo(c+new Vector2(0,-r));p.LineTo(c+new Vector2(r,r));p.LineTo(c+new Vector2(0,r*.45f));p.LineTo(c+new Vector2(-r,r));p.ClosePath();p.Stroke();}
                    else {p.BeginPath();p.MoveTo(c+new Vector2(0,-r));p.LineTo(c+new Vector2(r,0));p.LineTo(c+new Vector2(0,r));p.LineTo(c+new Vector2(-r,0));p.ClosePath();p.Stroke();}
                    if(Mathf.Abs(m.pitch)>4){float sign=m.pitch>0?-1:1;var tip=c+Vector2.up*(sign*(r+5));p.BeginPath();p.MoveTo(tip+new Vector2(-3,-sign*3));p.LineTo(tip);p.LineTo(tip+new Vector2(3,-sign*3));p.Stroke();}
                    if(Mathf.Abs(m.bearing)>150){float sign=Mathf.Sign(m.bearing);var tip=c+Vector2.right*(sign*(r+5));p.BeginPath();p.MoveTo(tip+new Vector2(-sign*3,-3));p.LineTo(tip);p.LineTo(tip+new Vector2(-sign*3,3));p.Stroke();}
                    if(Mathf.Abs(m.bearing)<3&&Mathf.Abs(m.pitch)<4){p.strokeColor=new Color(1,.85f,.35f);Circle(p,c,r+4,20);}
                }
            }
            static void Circle(Painter2D painter,Vector2 centre,float radius,int steps){painter.BeginPath();for(int i=0;i<=steps;i++){float angle=i*Mathf.PI*2/steps;var point=centre+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;if(i==0)painter.MoveTo(point);else painter.LineTo(point);}painter.Stroke();}
        }
        readonly PlanetDevelopment world;readonly bool flight;readonly Tape tape=new Tape();readonly Label readout=new Label(),reference=new Label();readonly Label[] cardinals=new Label[4];
        readonly PlanetDefinition[] planets=new PlanetDefinition[PlanetLimit];readonly double[] distances=new double[PlanetLimit];readonly string[] names=new string[MarkerLimit];readonly double[] ranges=new double[MarkerLimit];readonly CompassProjection[] projections=new CompassProjection[MarkerLimit];readonly long[] previousIds=new long[MarkerLimit];readonly float[] previousBearings=new float[MarkerLimit];int previousCount;
        Vector3 lastForward=Vector3.forward,lastNorth=Vector3.forward;float nextSelection,nextText,lastWidth=-1;int planetCount,omitted,overlapCount,focus=-1,previousFocus=-2,selectedNode=-1;PlanetDefinition lastBody;
        internal PlanetNavigationCompass(PlanetDevelopment world,bool flight=false){
            this.world=world;this.flight=flight;name="navigation-compass";pickingMode=PickingMode.Ignore;style.position=Position.Absolute;style.top=flight?86:14;style.height=138;style.display=DisplayStyle.None;style.backgroundColor=new Color(.015f,.03f,.045f,.66f);style.borderBottomWidth=1;style.borderBottomColor=new Color(.2f,.55f,.7f,.45f);
            tape.style.position=Position.Absolute;tape.style.left=0;tape.style.right=0;tape.style.top=5;tape.style.height=95;Add(tape);
            string[] labels={"N","E","S","W"};for(int i=0;i<4;i++){var label=cardinals[i]=new Label(labels[i]){pickingMode=PickingMode.Ignore};label.style.position=Position.Absolute;label.style.width=28;label.style.fontSize=11;label.style.color=new Color(.7f,.85f,.95f);label.style.unityTextAlign=TextAnchor.MiddleCenter;Add(label);}
            foreach(var label in new[]{readout,reference}){label.pickingMode=PickingMode.Ignore;label.style.position=Position.Absolute;label.style.left=8;label.style.right=8;label.style.unityTextAlign=TextAnchor.MiddleCenter;label.style.color=Color.white;label.style.fontSize=11;label.style.overflow=Overflow.Hidden;label.style.textOverflow=TextOverflow.Ellipsis;label.enableRichText=false;Add(label);}readout.style.top=100;reference.style.top=117;reference.style.fontSize=9;reference.style.color=new Color(.5f,.7f,.8f);
        }
        static double Dot(Double3 v,Vector3 n)=>v.x*n.x+v.y*n.y+v.z*n.z;
        void SelectPlanets(UniversePosition observer){var model=world.SystemRuntime?.Galaxy;int desired=world.ShipForValidation?.NavigationTargetNode??-1;if(Time.unscaledTime<nextSelection&&lastBody==world.definition&&selectedNode==desired)return;nextSelection=Time.unscaledTime+.5f;lastBody=world.definition;selectedNode=desired;planetCount=omitted=0;
            var bodies=world.SystemRuntime?.Bodies;int referenceNode=model?.NodeFor(world.definition)??-1;int system=referenceNode>=0?model.Nodes[referenceNode].system:-1;
            if(bodies==null){AddPlanet(world.definition,observer,-1,true);return;}
            foreach(var body in bodies){if(body==null)continue;int node=model?.NodeFor(body)??-1;bool selected=node>=0&&node==desired;if(model!=null&&system>=0&&node>=0&&model.Nodes[node].system!=system&&!selected)continue;AddPlanet(body,observer,node,selected);}
        }
        void AddPlanet(PlanetDefinition body,UniversePosition observer,int node,bool selected){
            double range=body.position.RelativeTo(observer).Magnitude;if(body.bodyId==world.definition.bodyId&&range-body.radius<1000)return;
            double score=selected?-1:Math.Max(0,range-body.radius);int at=0;while(at<planetCount&&distances[at]<=score)at++;if(at>=PlanetLimit){omitted++;return;}if(planetCount==PlanetLimit)omitted++;int end=Math.Min(planetCount,PlanetLimit-1);for(int i=end;i>at;i--){planets[i]=planets[i-1];distances[i]=distances[i-1];}planets[at]=body;distances[at]=score;planetCount=Math.Min(PlanetLimit,planetCount+1);
        }
        void AddMarker(long id,Double3 delta,Vector3 right,Vector3 forward,Vector3 up,double pitch,string title,double range,Icon icon,Color color){if(tape.count>=MarkerLimit)return;var projection=NavigationCompassMath.Project(Dot(delta,right),Dot(delta,forward),Dot(delta,up),pitch);if(!projection.valid)return;for(int old=0;old<previousCount;old++)if(previousIds[old]==id){projection=new CompassProjection(NavigationCompassMath.StableBearing(projection.bearing,previousBearings[old]),projection.pitchError,projection.scale,true);break;}int at=tape.count++;projections[at]=projection;names[at]=title;ranges[at]=Math.Max(0,range);tape.markers[at]=new Marker{id=id,bearing=(float)projection.bearing,pitch=(float)projection.pitchError,size=21*(float)projection.scale,color=color,icon=icon};}
        public void Refresh(){
            bool visible=world.showNavigationCompass&&world.Ready&&world.ResidentActive&&!world.LocalPlayerDead&&!world.PersonalMenuOpen&&(flight?world.IsPiloting:world.IsFirstPerson||world.IsDriving);style.display=visible?DisplayStyle.Flex:DisplayStyle.None;if(!visible||world.View==null||parent==null)return;
            float parentWidth=parent.resolvedStyle.width;if(!(parentWidth>0))return;float width=Mathf.Max(1,Mathf.Min(parentWidth-24,Mathf.Clamp(parentWidth*.48f,300,740)));if(width<180){style.display=DisplayStyle.None;return;}if(width!=lastWidth){style.width=width;style.left=(parentWidth-width)*.5f;lastWidth=width;}
            var camera=world.View.transform;var observer=world.ObserverPosition;var bodyOffset=observer.RelativeTo(world.definition.position);bool local=bodyOffset.Magnitude<world.definition.radius*20;var up=local?PlanetPrecisionPolicy.Direction(bodyOffset):Vector3.up;
            var north=Vector3.ProjectOnPlane(Vector3.up,up);if(north.sqrMagnitude<1e-6f)north=Vector3.ProjectOnPlane(lastNorth,up);if(north.sqrMagnitude<1e-6f)north=Vector3.ProjectOnPlane(Vector3.forward,up);north.Normalize();lastNorth=north;
            var flat=Vector3.ProjectOnPlane(camera.forward,up);float flatLength=flat.magnitude;if(flatLength<.015f)flat=Vector3.ProjectOnPlane(lastForward,up);if(flat.sqrMagnitude<1e-6f)flat=north;flat.Normalize();lastForward=flat;var right=Vector3.Cross(up,flat).normalized;double pitch=Math.Atan2(Vector3.Dot(camera.forward,up),flatLength)*180/Math.PI;var east=Vector3.Cross(up,north);float heading=Mathf.Repeat(Mathf.Atan2(Vector3.Dot(flat,east),Vector3.Dot(flat,north))*Mathf.Rad2Deg,360);tape.heading=heading;
            previousCount=tape.count;for(int i=0;i<previousCount;i++){previousIds[i]=tape.markers[i].id;previousBearings[i]=tape.markers[i].bearing;}tape.count=0;var carrierWorld=world.SystemRuntime?.RespawnCarrierWorld??world;var ship=carrierWorld?.ShipForValidation;
            if(!flight&&ship!=null&&carrierWorld.HasRespawnCarrier){var delta=ship.Position.RelativeTo(observer);if(delta.Magnitude>3)AddMarker(-1,delta,right,flat,up,pitch,"YOUR SHIP",delta.Magnitude,Icon.Ship,new Color(.45f,1,.9f));}
            if(world.CompassOrder(out var orderPosition,out string orderName)){var delta=orderPosition.RelativeTo(observer);AddMarker(-2,delta,right,flat,up,pitch,orderName,delta.Magnitude,Icon.Order,new Color(1,.75f,.3f));}
            SelectPlanets(observer);var model=world.SystemRuntime?.Galaxy;
            for(int i=0;i<planetCount;i++){var body=planets[i];if(body==null)continue;var delta=body.position.RelativeTo(observer);bool selected=model!=null&&model.NodeFor(body)==selectedNode;AddMarker((long)body.bodyId+1,delta,right,flat,up,pitch,body.displayName,Math.Max(0,delta.Magnitude-body.radius),Icon.Planet,selected?new Color(1,.85f,.4f):new Color(.6f,.8f,1));}
            focus=-1;double best=double.MaxValue;float half=width*.5f-18;overlapCount=0;
            for(int i=0;i<tape.count;i++){var m=tape.markers[i];m.x=width*.5f+m.bearing/180*half;int lane=0;for(;lane<3;lane++){bool occupied=false;for(int j=0;j<i;j++)if(!tape.markers[j].hidden&&Mathf.Abs(tape.markers[j].x-m.x)<24&&Mathf.Abs(tape.markers[j].y-(Tape.Arc(m.x,width)-13-lane*20))<14){occupied=true;break;}if(!occupied)break;}m.hidden=lane>=3;m.y=Tape.Arc(m.x,width)-13-Math.Min(2,lane)*20;if(m.hidden)overlapCount++;tape.markers[i]=m;
                double score=Math.Abs(m.bearing)+Math.Abs(m.pitch)*.15;if(!m.hidden&&Math.Abs(m.bearing)<20&&score<best){best=score;focus=i;}}
            for(int i=0;i<cardinals.Length;i++){float bearing=(float)NavigationCompassMath.Wrap(i*90-heading),x=width*.5f+bearing/180*half;cardinals[i].style.left=x-14;cardinals[i].style.top=Tape.Arc(x,width)+15;}
            if(Time.unscaledTime>=nextText||focus!=previousFocus){nextText=Time.unscaledTime+.1f;previousFocus=focus;if(focus>=0){var p=projections[focus];string range=ranges[focus]>=1e9?$"{ranges[focus]/1e9:0.00} Gm":ranges[focus]>=1000?$"{ranges[focus]/1000:0.0} km":$"{ranges[focus]:0} m";string cue=Math.Abs(p.pitchError)<4?(Math.Abs(p.bearing)<3?"ALIGNED":$"TURN {(p.bearing<0?"LEFT":"RIGHT")} {Math.Abs(p.bearing):0}°"):p.pitchError>0?$"PITCH UP {Math.Abs(p.pitchError):0}°":$"PITCH DOWN {Math.Abs(p.pitchError):0}°";readout.text=names[focus]+" · "+range+" · "+cue;}else readout.text="Turn toward a marker · pitch toward it to enlarge its icon";reference.text=$"360° COMPASS · {(local?"LOCAL NORTH":"SPACE NORTH")} · {heading:000}°"+(omitted>0?$" · +{omitted} other bodies":"")+(overlapCount>0?$" · {overlapCount} overlapping markers":"");}
            tape.MarkDirtyRepaint();
        }
    }
}
