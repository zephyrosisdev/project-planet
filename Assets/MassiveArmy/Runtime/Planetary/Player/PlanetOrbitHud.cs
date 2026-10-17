using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        const int OrbitSamples=160;
        readonly Vector2[] orbitMap=new Vector2[OrbitSamples];
        PlanetOrbit orbit;float nextOrbitRefresh;double mapExtent;
        string orbitReadout,orbitStatus;Color orbitColor;
        static string Distance(double value)=>Math.Abs(value)>=1000?$"{value/1000:0.00} km":$"{value:0} m";
        static string Duration(double seconds)=>seconds>=3600?$"{seconds/3600:0.00} h":$"{seconds/60:0.0} min";
        void RefreshOrbit()
        {
            if(RefreshGalaxyOrbit())return;
            orbit=PlanetOrbit.Calculate(pose.position,pose.linearVelocity,world.definition.gravitationalParameter);
            double surfaceRadius=world.definition.radius+Math.Max(world.definition.ReliefBound,world.definition.oceanHeight)+(GearDeployed?8:5);
            bool impact=orbit.SurfaceThreatAhead(surfaceRadius);
            orbitStatus=Landed?"LANDED":!orbit.Valid?"RADIAL / FREEFALL":impact?"SURFACE INTERSECTION":!orbit.Bound?"ESCAPE TRAJECTORY":"BOUND ORBIT · TERRAIN CLEAR";
            if(!Landed&&FlightAssistEnabled)orbitStatus=GroundClearance<6000?"ASSISTED · TERRAIN CLEARANCE HOLD":"ASSISTED SPACE · DRIFT CONTROL";
            orbitColor=Landed?Color.gray:!orbit.Valid||impact?new Color(1,.45f,.25f):!orbit.Bound?new Color(1,.8f,.3f):new Color(.25f,1,.75f);
            orbitReadout=$"L · ORBIT ATTITUDE HOLD {(RotationLocked?"ON":"OFF")}\nALTITUDE       {Distance(pose.position.Magnitude-world.definition.radius)}\nSPEED          {Speed:0.0} m/s\nRADIAL         {orbit.RadialSpeed:+0.0;-0.0;0.0} m/s\nTANGENTIAL     {orbit.TangentialSpeed:0.0} m/s\nCIRCULAR / ESC {orbit.CircularSpeed:0.0} / {orbit.EscapeSpeed:0.0} m/s";
            if(orbit.Valid)orbitReadout+=$"\nPERIAPSIS ALT  {Distance(orbit.Periapsis-world.definition.radius)}\nAPOAPSIS ALT   {(orbit.Bound?Distance(orbit.Apoapsis-world.definition.radius):"UNBOUNDED")}\nECCENTRICITY   {orbit.Eccentricity:0.0000}\nPERIOD         {(orbit.Bound?Duration(orbit.Period):"—")}";
            else orbitReadout+=FlightAssistEnabled?"\nV disables assist for orbital flight.":"\nNo orbital plane: build tangential velocity.";
            mapExtent=Math.Max(world.definition.radius*1.3,pose.position.Magnitude*1.15);
            // Keep the planet legible on escape and near-parabolic paths; clipped arcs are labelled.
            double cap=Math.Max(world.definition.radius*4,pose.position.Magnitude*2);
            if(orbit.Valid){if(orbit.Bound)mapExtent=Math.Min(cap,Math.Max(mapExtent,orbit.Apoapsis*1.1));
                double limit=orbit.Bound?Math.PI:Math.Acos(Math.Max(-1,Math.Min(1,-1/Math.Max(1,orbit.Eccentricity))))-.025;
                for(int i=0;i<OrbitSamples;i++){double theta=-limit+2*limit*i/(OrbitSamples-1);double r=orbit.Parameter/(1+orbit.Eccentricity*Math.Cos(theta));orbitMap[i]=new Vector2((float)(r*Math.Cos(theta)/mapExtent),(float)(-r*Math.Sin(theta)/mapExtent));}}
        }
        RtsUiController pilotUi;VisualElement flightPanel,landingPanel;Label statusLabel,readoutLabel,progradeLabel,peLabel,apLabel,weaponLabel,landingLabel,landingInfo;
        RtsUiDrawing mapDrawing,progradeDrawing,landingDrawing;bool pilotVisible;
        internal void SetPilotRenderTarget(RenderTexture target)=>pilotUi.SetRenderTarget(target);
        void InitializePilotHud(){pilotUi=new RtsUiController(hull.transform,BuildPilotHud,UpdatePilotHud,()=>active);pilotUi.Attach();}
        Label PilotLabel(VisualElement parent,string text,int size=14){var label=new Label(text);label.style.fontSize=size;label.style.color=Color.white;label.style.whiteSpace=WhiteSpace.Normal;label.pickingMode=PickingMode.Ignore;parent.Add(label);return label;}
        void BuildPilotHud(VisualElement root)
        {
            root.Clear();root.pickingMode=PickingMode.Ignore;
            flightPanel=new VisualElement{pickingMode=PickingMode.Ignore};flightPanel.style.position=UnityEngine.UIElements.Position.Absolute;flightPanel.style.right=16;flightPanel.style.top=110;flightPanel.style.width=366;flightPanel.style.paddingLeft=16;flightPanel.style.paddingRight=16;flightPanel.style.paddingTop=14;flightPanel.style.paddingBottom=16;flightPanel.style.backgroundColor=new Color(.035f,.07f,.11f,.94f);root.Add(flightPanel);
            weaponLabel=PilotLabel(flightPanel,"ROCKETS READY",12);PilotLabel(flightPanel,"FLIGHT TRAJECTORY / GRAVITY",16);statusLabel=PilotLabel(flightPanel,"");statusLabel.style.marginTop=6;
            mapDrawing=new RtsUiDrawing();mapDrawing.style.height=280;mapDrawing.style.width=334;flightPanel.Add(mapDrawing);
            peLabel=PilotLabel(mapDrawing,"Pe",12);apLabel=PilotLabel(mapDrawing,"Ap",12);peLabel.style.position=apLabel.style.position=UnityEngine.UIElements.Position.Absolute;
            PilotLabel(flightPanel,"Orbit-plane map · white = ship · clipped at edge",12);
            readoutLabel=PilotLabel(flightPanel,"");readoutLabel.style.marginTop=12;readoutLabel.style.whiteSpace=WhiteSpace.NoWrap;
            var note=PilotLabel(flightPanel,"Left mouse: rockets (45 s fail-safe).\nSpace: land / take off · R/C: climb.\nV: flight assist · L: orbital coast.\nGreen outline: valid assisted landing site.\nOrbit map: coasting reference (assist off).",12);note.style.marginTop=14;
            landingPanel=new VisualElement{pickingMode=PickingMode.Ignore};landingPanel.style.position=UnityEngine.UIElements.Position.Absolute;landingPanel.style.left=16;landingPanel.style.bottom=90;landingPanel.style.width=340;landingPanel.style.paddingLeft=14;landingPanel.style.paddingRight=14;landingPanel.style.paddingTop=12;landingPanel.style.paddingBottom=12;landingPanel.style.backgroundColor=new Color(.025f,.06f,.08f,.9f);root.Add(landingPanel);
            landingLabel=PilotLabel(landingPanel,"",15);landingInfo=PilotLabel(landingPanel,"",12);landingDrawing=new RtsUiDrawing();landingDrawing.style.height=90;landingDrawing.style.width=300;landingPanel.Add(landingDrawing);PilotLabel(landingPanel,"Landing approach · side view",11);
            progradeDrawing=new RtsUiDrawing();progradeDrawing.style.position=UnityEngine.UIElements.Position.Absolute;progradeDrawing.StretchToParentSize();root.Add(progradeDrawing);progradeLabel=PilotLabel(root,"PROGRADE",12);progradeLabel.style.position=UnityEngine.UIElements.Position.Absolute;
            pilotVisible=!active;nextOrbitRefresh=0;UpdatePilotHud();
        }
        void MapLine(Vector2 a,Vector2 b,Color color)=>mapDrawing.Lines.Add((a,b,color));
        void MapDot(Vector2 p,Color color){MapLine(p+Vector2.left*3,p+Vector2.right*3,color);MapLine(p+Vector2.up*3,p+Vector2.down*3,color);}
        void UpdateOrbitMap()
        {
            if(DrawGalaxyOrbitMap())return;
            mapDrawing.Lines.Clear();Vector2 center=new Vector2(167,140);const float size=126;
            float planet=(float)(world.definition.radius/mapExtent)*size;
            for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64,b=(i+1)*Mathf.PI*2/64;MapLine(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*planet,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*planet,new Color(.3f,.55f,.8f));}
            peLabel.style.display=apLabel.style.display=DisplayStyle.None;
            if(orbit.Valid){for(int i=1;i<OrbitSamples;i++)if(orbitMap[i-1].sqrMagnitude<=1.1f&&orbitMap[i].sqrMagnitude<=1.1f)MapLine(center+orbitMap[i-1]*size,center+orbitMap[i]*size,orbitColor);
                Vector2 here=new Vector2((float)(orbit.Radius*Math.Cos(orbit.CurrentAnomaly)/mapExtent),(float)(-orbit.Radius*Math.Sin(orbit.CurrentAnomaly)/mapExtent));MapDot(center+here*size,Color.white);
                var pe=center+Vector2.right*(float)(orbit.Periapsis/mapExtent)*size;if(orbit.Periapsis<mapExtent){MapDot(pe,Color.cyan);peLabel.style.display=DisplayStyle.Flex;peLabel.style.left=pe.x+5;peLabel.style.top=pe.y;}
                if(orbit.Bound&&orbit.Apoapsis<mapExtent){var ap=center-Vector2.right*(float)(orbit.Apoapsis/mapExtent)*size;MapDot(ap,Color.cyan);apLabel.style.display=DisplayStyle.Flex;apLabel.style.left=ap.x-22;apLabel.style.top=ap.y;}}
            else MapDot(center+Vector2.right*(float)(pose.position.Magnitude/mapExtent)*size,Color.white);
            mapDrawing.MarkDirtyRepaint();
        }
        void UpdatePilotHud()
        {
            if(flightPanel==null)return;
            if(pilotVisible!=active){pilotVisible=active;pilotUi.Root.style.display=active?DisplayStyle.Flex:DisplayStyle.None;nextOrbitRefresh=0;}
            if(!active)return;
            Prediction();
            if(FlightAssistEnabled)RefreshLandingPreview();landingPanel.style.display=LandingPreviewVisible?DisplayStyle.Flex:DisplayStyle.None;var landingColor=(LandingAvailable||AutoFlightActive)?Color.green:Landed&&!LandingStatus.StartsWith("TAKEOFF BLOCKED")?new Color(.65f,.85f,1):new Color(1,.25f,.15f);landingLabel.text=LandingStatus;landingLabel.style.color=landingColor;landingInfo.text=$"CLEARANCE {GroundClearance:0} m · SPEED {Speed:0} m/s\nV · FLIGHT ASSIST {(FlightAssistEnabled?"ON":"OFF")}";
            landingDrawing.Lines.Clear();if(LandingPreviewVisible){var from=pose.position;var delta=LandingPosition-from;var up=LandingPosition.Float.normalized;var flat=Vector3.ProjectOnPlane(delta.Float,up);float run=Mathf.Max(100,flat.magnitude),drop=Mathf.Max(10,-Vector3.Dot(delta.Float,up));var forward=flat.sqrMagnitude>.01f?flat.normalized:Vector3.ProjectOnPlane(pose.rotation*Vector3.forward,up).normalized;Vector2 previous=Vector2.zero;
                for(int i=0;i<=16;i++){var d=((AutoFlightActive?AutoPoint(Mathf.Lerp(AutoFlightProgress,1,i/16f)):LandingCurve(from,LandingPosition,i/16f))-from).Float;var point=new Vector2(30+Vector3.Dot(d,forward)/run*220,10-Vector3.Dot(d,up)/drop*60);if(i>0)landingDrawing.Lines.Add((previous,point,landingColor));previous=point;}landingDrawing.Lines.Add((previous+new Vector2(-18,5),previous+new Vector2(18,5),landingColor));}landingDrawing.MarkDirtyRepaint();
            weaponLabel.text=$"ROCKETS {ActiveRockets}/{RocketCapacity} AIRBORNE · {(ActiveRockets>=RocketCapacity?"WAIT FOR DETONATION":fireDelay>0?"RELOADING":"READY")}";
            if(orbitReadout==null||Time.unscaledTime>=nextOrbitRefresh){RefreshOrbit();nextOrbitRefresh=Time.unscaledTime+.2f;statusLabel.text=orbitStatus;statusLabel.style.color=FlightAssistEnabled&&!Landed?new Color(.25f,1,.75f):orbitColor;readoutLabel.text=orbitReadout;UpdateOrbitMap();}
            progradeDrawing.Lines.Clear();var sight=new Vector2(pilotUi.Root.resolvedStyle.width*.5f,pilotUi.Root.resolvedStyle.height*.5f);progradeDrawing.Lines.Add((sight+Vector2.left*5,sight+Vector2.right*5,Color.green));progradeDrawing.Lines.Add((sight+Vector2.up*5,sight+Vector2.down*5,Color.green));var hudVelocity=HudVelocity();progradeLabel.style.display=hudVelocity.Magnitude>1?DisplayStyle.Flex:DisplayStyle.None;
            if(hudVelocity.Magnitude>1&&pilotUi.Root.resolvedStyle.width>0&&pilotUi.Root.resolvedStyle.height>0){var v=camera.transform.InverseTransformDirection(hudVelocity.Float.normalized);var c=new Vector2(pilotUi.Root.resolvedStyle.width*.5f,pilotUi.Root.resolvedStyle.height*.5f);Vector2 marker=c+new Vector2(v.x,-v.y)*90;var color=v.z>=0?Color.green:new Color(1,.6f,.2f);progradeDrawing.Lines.Add((marker+Vector2.left*9,marker+Vector2.right*9,color));progradeDrawing.Lines.Add((marker+Vector2.up*9,marker+Vector2.down*9,color));progradeLabel.style.left=marker.x+12;progradeLabel.style.top=marker.y-12;progradeLabel.text=v.z>=0?"PROGRADE":"PROGRADE AFT";progradeLabel.style.color=color;}
            progradeDrawing.MarkDirtyRepaint();
        }
    }
}
