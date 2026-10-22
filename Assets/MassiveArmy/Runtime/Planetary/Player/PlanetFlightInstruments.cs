using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public static class PlanetFlightInstrumentMath {
        public static bool Bank(Quaternion rotation,Vector3 radialUp,out float degrees){var forward=rotation*Vector3.forward;var level=Vector3.ProjectOnPlane(radialUp,forward);degrees=0;if(level.sqrMagnitude<.0001f)return false;degrees=Vector3.SignedAngle(level,rotation*Vector3.up,forward);return true;}
        public static double TapeStep(double altitude)=>Math.Pow(10,Math.Max(1,Math.Floor(Math.Log10(Math.Max(100,Math.Abs(altitude))))-1));
    }
    public sealed partial class PlanetSpacecraft {
        readonly Label[] altitudeGraduations=new Label[5];Label altimeterLabel,bankLabel,flightSpeedLabel;float flowPhase;int instrumentPlanet=-1;float nextInstrumentReference;
        void BuildFlightInstruments(VisualElement root){for(int i=0;i<altitudeGraduations.Length;i++){var mark=altitudeGraduations[i]=PilotLabel(root,"",11);mark.style.position=UnityEngine.UIElements.Position.Absolute;mark.style.width=60;mark.style.unityTextAlign=TextAnchor.MiddleRight;}altimeterLabel=PilotLabel(root,"",14);bankLabel=PilotLabel(root,"",13);flightSpeedLabel=PilotLabel(root,"",13);foreach(var label in new[]{altimeterLabel,bankLabel,flightSpeedLabel}){label.style.position=UnityEngine.UIElements.Position.Absolute;label.style.color=new Color(.3f,1,.75f);}altimeterLabel.name="pilot-altimeter";bankLabel.name="pilot-bank";flightSpeedLabel.name="pilot-velocity";}
        void FlightReference(out Double3 radial,out Double3 velocity,out double radius,out string name){radial=pose.position;velocity=pose.linearVelocity;radius=world.definition.radius;name=world.definition.displayName;var model=world.SystemRuntime?.Galaxy;if(model==null)return;
            var absolute=model.Current[model.NodeFor(world.definition)].position+pose.position;
            if(Time.unscaledTime>=nextInstrumentReference||instrumentPlanet<0){nextInstrumentReference=Time.unscaledTime+.2f;double closest=double.MaxValue;foreach(int node in model.PlanetNodes){double distance=(absolute-model.Current[node].position).Magnitude-model.Nodes[node].radius;if(distance<closest){closest=distance;instrumentPlanet=node;}}}
            if(instrumentPlanet>=0){radial=absolute-model.Current[instrumentPlanet].position;velocity=pose.linearVelocity+world.definition.velocity-model.Current[instrumentPlanet].velocity;radius=model.Nodes[instrumentPlanet].radius;name=model.Nodes[instrumentPlanet].name;}
        }
        void UpdateFlightInstruments(Vector2 centre){if(altimeterLabel==null)return;FlightReference(out var radial,out var velocity,out double radius,out string name);double altitude=radial.Magnitude-radius;var up=radial.Float.normalized;bool near=altitude<radius*20;var tint=new Color(.3f,1,.75f,.8f);var draw=progradeDrawing.Lines;
            void Line(Vector2 a,Vector2 b)=>draw.Add((a,b,tint));
            float tapeX=centre.x+230;altimeterLabel.style.left=tapeX+12;altimeterLabel.style.top=centre.y-30;altimeterLabel.text=near?$"ALT {Distance(altitude)}\n{name}\nASL · V/S {FormatFlightSpeed(CruiseDot(velocity,new Double3(up)))}":"ALT —\nOutside planetary range";
            Line(new Vector2(tapeX,centre.y-120),new Vector2(tapeX,centre.y+120));double step=PlanetFlightInstrumentMath.TapeStep(altitude),first=Math.Floor(altitude/step);
            if(near)for(int i=-5;i<=5;i++){float y=centre.y-(float)((first+i-altitude/step)*24);Line(new Vector2(tapeX-((i+(long)first)%5==0?14:7),y),new Vector2(tapeX,y));}
            for(int i=0;i<altitudeGraduations.Length;i++){double value=(first+(i-2)*2)*step;var mark=altitudeGraduations[i];mark.style.display=near?DisplayStyle.Flex:DisplayStyle.None;mark.style.left=tapeX-80;mark.style.top=centre.y-(float)((value-altitude)/step*24)-7;mark.text=Distance(value);}
            Line(new Vector2(tapeX-20,centre.y-6),new Vector2(tapeX-10,centre.y));Line(new Vector2(tapeX-10,centre.y),new Vector2(tapeX-20,centre.y+6));
            bool bankValid=near&&PlanetFlightInstrumentMath.Bank(pose.rotation,up,out _);PlanetFlightInstrumentMath.Bank(pose.rotation,up,out float bank);bankLabel.style.left=centre.x-75;bankLabel.style.top=centre.y-210;bankLabel.text=bankValid?$"BANK {bank:+0.0;-0.0;0.0}°":"BANK —";
            const float rollRadius=100;var rollCentre=centre+Vector2.up*20;for(int i=-60;i<=60;i+=15){float angle=i*Mathf.Deg2Rad;var normal=new Vector2(Mathf.Sin(angle),-Mathf.Cos(angle));Line(rollCentre+normal*rollRadius,rollCentre+normal*(rollRadius+(i%30==0?10:5)));}
            if(bankValid){float angle=-bank*Mathf.Deg2Rad;var normal=new Vector2(Mathf.Sin(angle),-Mathf.Cos(angle));var tip=rollCentre+normal*rollRadius;var tangent=new Vector2(-normal.y,normal.x);Line(tip,tip+normal*12+tangent*6);Line(tip,tip+normal*12-tangent*6);}
            var local=camera.transform.InverseTransformDirection(velocity.Float);float speed=(float)velocity.Magnitude;flightSpeedLabel.style.left=centre.x+155;flightSpeedLabel.style.top=centre.y+132;flightSpeedLabel.text=$"{FormatFlightSpeed(speed)}\nPLANET FRAME";
            // Instrument flow, not physical debris. Fixed 24 strokes, reversible
            // with velocity and stationary at rest; capped at warp for readability.
            flowPhase=(flowPhase+Time.unscaledDeltaTime*Mathf.Min(12,speed/100))%1024;
            if(speed>.1f)for(int i=0;i<24;i++){float sideways=local.x/speed,vertical=local.y/speed,forward=local.z/speed;float x=Mathf.Repeat(i*.381966f-flowPhase*sideways*.2f,1),y=Mathf.Repeat(i*.618034f+flowPhase*(forward+vertical)*.2f,1);
                var point=new Vector2(tapeX-125+x*32,centre.y+(y-.5f)*220);var delta=new Vector2(-sideways*7,(forward+vertical)*7);Line(point,point+delta);
            }
        }
    }
}
