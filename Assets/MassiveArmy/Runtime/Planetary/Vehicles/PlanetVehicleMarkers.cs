using UnityEngine;
using UnityEngine.UIElements;

namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetVehiclePool
    {
        VisualElement markerRoot,markerParent;
        RtsUiDrawing markerDrawing;
        readonly Label[] markerLabels=new Label[Capacity];
        float nextMarkerUpdate;readonly Vector2[] markerPositions=new Vector2[Capacity];
        static readonly Color MarkerColour=new Color(.15f,.85f,1);
        public static bool ShowStrategicMarker(bool alive,uint team,float projectedPixels)=>alive&&team==0&&projectedPixels<32;
        void DisposeMarkers(){markerRoot?.RemoveFromHierarchy();markerRoot=markerParent=null;}
        void BuildMarkers(VisualElement parent){
            DisposeMarkers();markerParent=parent;
            markerRoot=new VisualElement{name="friendly-vehicle-markers",pickingMode=PickingMode.Ignore};markerRoot.style.position=Position.Absolute;markerRoot.StretchToParentSize();parent.Add(markerRoot);
            markerDrawing=new RtsUiDrawing{pickingMode=PickingMode.Ignore};markerDrawing.StretchToParentSize();markerRoot.Add(markerDrawing);
            for(int i=0;i<Capacity;i++){
                var label=new Label{name="friendly-vehicle-"+i,pickingMode=PickingMode.Ignore};label.style.position=Position.Absolute;label.style.fontSize=12;label.style.color=MarkerColour;label.style.backgroundColor=new Color(.015f,.025f,.04f,.85f);label.style.paddingLeft=4;label.style.paddingRight=4;label.style.display=DisplayStyle.None;
                markerLabels[i]=label;markerRoot.Add(label);
            }
        }
        public void TickMarkers(){
            var parent=world.Hud?.Root;if(parent==null){DisposeMarkers();return;}
            if(parent!=markerParent)BuildMarkers(parent);
            bool rts=!world.IsFirstPerson&&!world.IsPiloting&&!world.IsDriving;
            markerRoot.style.display=rts?DisplayStyle.Flex:DisplayStyle.None;if(!rts)return;
            if(Time.unscaledTime<nextMarkerUpdate)return;nextMarkerUpdate=Time.unscaledTime+.1f;
            float width=parent.resolvedStyle.width,height=parent.resolvedStyle.height;if(width<100||height<100)return;
            var camera=world.View;var projection=camera.nonJitteredProjectionMatrix;var eye=world.RenderToBody(camera.transform.position);var centre=new Vector2(width*.5f,height*.5f);
            markerDrawing.Lines.Clear();int markerCount=0;
            for(int i=0;i<Capacity;i++){
                var label=markerLabels[i];label.style.display=DisplayStyle.None;if(i>=Count)continue;var vehicle=vehicles[i];
                var target=vehicle.Motor.Pose.position+Double3.Rotate(vehicle.Motor.Pose.rotation,new Double3(0,vehicle.Definition.size.y*.5f,0));
                var view=camera.worldToCameraMatrix.MultiplyPoint3x4(world.RenderBodyPosition(target));
                float pixels=vehicle.Definition.size.magnitude*Mathf.Abs(projection.m11)*height*.5f/Mathf.Max(1,Mathf.Abs(view.z));
                if(!ShowStrategicMarker(vehicle.Alive,vehicle.OwnerTeam,pixels))continue;
                var clip=projection*new Vector4(view.x,view.y,view.z,1);float w=Mathf.Abs(clip.w)>.0001f?clip.w:.0001f;
                var point=new Vector2((clip.x/w*.5f+.5f)*width,(.5f-clip.y/w*.5f)*height);var bearing=point-centre;if(view.z>=0)bearing=-bearing;if(bearing.sqrMagnitude<1)bearing=Vector2.down;
                bool horizon=PlanetHeadquartersHud.BehindHorizon(eye,target,world.definition.radius+world.definition.oceanHeight);
                bool edge=view.z>=0||horizon||point.x<35||point.x>width-185||point.y<110||point.y>height-180;
                if(edge){float scale=Mathf.Min((bearing.x<0?centre.x-35:width-185-centre.x)/Mathf.Max(.001f,Mathf.Abs(bearing.x)),(bearing.y<0?centre.y-110:height-180-centre.y)/Mathf.Max(.001f,Mathf.Abs(bearing.y)));point=centre+bearing*scale;}
                var anchor=point;
                for(int attempt=0;attempt<Capacity;attempt++){
                    bool overlap=false;for(int previous=0;previous<markerCount;previous++)if(Mathf.Abs(markerPositions[previous].y-point.y)<28&&Mathf.Abs(markerPositions[previous].x-point.x)<175){overlap=true;break;}
                    if(!overlap)break;point.y+=30;if(point.y>height-180){point.y=110;point.x=Mathf.Min(width-185,point.x+180);}
                }
                markerPositions[markerCount++]=point;if((point-anchor).sqrMagnitude>1)MarkerLine(anchor,point);
                DrawVehicleIcon(point,vehicle.Definition.locomotion==VehicleLocomotion.Walker);
                if(edge){var direction=bearing.normalized;var side=new Vector2(-direction.y,direction.x);MarkerLine(point+direction*20,point+direction*13+side*4);MarkerLine(point+direction*20,point+direction*13-side*4);}
                label.style.display=DisplayStyle.Flex;label.style.left=Mathf.Clamp(point.x+16,4,width-180);label.style.top=Mathf.Clamp(point.y-8,4,height-50);
                double distance=(target-eye).Magnitude;label.text=vehicle.Definition.id+" · "+(distance>=1000?(distance/1000).ToString("0.0")+" km":distance.ToString("0")+" m")+(vehicle.Parking.Parked?"\nCARGO":horizon?"\nBEYOND HORIZON":edge?"\nOFFSCREEN":"");
            }
            markerDrawing.MarkDirtyRepaint();
        }
        void MarkerLine(Vector2 a,Vector2 b)=>markerDrawing.Lines.Add((a,b,MarkerColour));
        void DrawVehicleIcon(Vector2 p,bool mech){
            void Line(float x,float y,float a,float b)=>MarkerLine(p+new Vector2(x,y),p+new Vector2(a,b));
            void Box(float x,float y,float w,float h){Line(x,y,x+w,y);Line(x+w,y,x+w,y+h);Line(x+w,y+h,x,y+h);Line(x,y+h,x,y);}
            if(mech){Box(-3,-12,6,5);Box(-6,-6,12,9);Line(-6,-4,-10,2);Line(6,-4,10,2);Line(-4,3,-6,11);Line(4,3,6,11);Line(-6,11,-10,11);Line(6,11,10,11);}
            else{Box(-11,-3,22,8);Line(-7,-3,-4,-9);Line(-4,-9,5,-9);Line(5,-9,9,-3);Box(-8,5,4,4);Box(5,5,4,4);}
        }
    }
}

