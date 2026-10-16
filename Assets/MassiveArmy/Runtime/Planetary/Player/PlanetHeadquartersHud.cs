using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary
{
    /// <summary>Two infrastructure markers; no army readback or physics raycasts.</summary>
    public sealed class PlanetHeadquartersHud:IDisposable
    {
        internal struct MarkerState{public bool visible,edge,occluded;public Vector2 position;public double distance;}
        internal int GeometryRebuilds {get;private set;}
        internal int CacheMissReason {get;private set;}
        internal readonly MarkerState[] States=new MarkerState[2];
        readonly PlanetDevelopment world;readonly PlanetSurface surface;readonly Label[] labels=new Label[2];readonly Color[] colours={new Color(.15f,.85f,1),new Color(1,.35f,.25f)};
        readonly RtsBuilding[] headquarters=new RtsBuilding[2];VisualElement parent,overlay;RtsUiDrawing drawing;float nextRefresh;bool haveView,pendingText;Double3 previousEye;Quaternion previousRotation;Matrix4x4 previousProjection;Vector2 previousSize;int previousTerrainRevision=-1;
        public PlanetHeadquartersHud(PlanetDevelopment world){this.world=world;surface=new PlanetSurface(world.definition);}
        static double Dot(Double3 a,Double3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        internal static bool BehindHorizon(Double3 eye,Double3 target,double radius){var ray=target-eye;double length=Dot(ray,ray);if(length<1)return false;double t=-Dot(eye,ray)/length;if(t<=0||t>=1)return false;var nearest=eye+ray*t;return Dot(nearest,nearest)<radius*radius;}
        void Build(VisualElement root){haveView=false;overlay?.RemoveFromHierarchy();parent=root;overlay=new VisualElement{name="headquarters-markers",pickingMode=PickingMode.Ignore};overlay.style.position=Position.Absolute;overlay.StretchToParentSize();root.Add(overlay);drawing=new RtsUiDrawing();drawing.StretchToParentSize();overlay.Add(drawing);
            for(int i=0;i<2;i++){var label=new Label{name="team-"+i+"-hq-marker",pickingMode=PickingMode.Ignore};label.style.position=Position.Absolute;label.style.fontSize=13;label.style.color=colours[i];label.style.backgroundColor=new Color(.015f,.025f,.04f,.85f);label.style.paddingLeft=5;label.style.paddingRight=5;label.style.paddingTop=2;label.style.paddingBottom=2;labels[i]=label;overlay.Add(label);}
        }
        public void Tick(){var root=world.Hud?.Root;if(root==null||world.Base==null)return;if(root!=parent)Build(root);float width=root.resolvedStyle.width,height=root.resolvedStyle.height;if(width<100||height<100)return;
            bool refresh=Time.unscaledTime>=nextRefresh,changed=false;if(refresh){nextRefresh=Time.unscaledTime+.2f;for(int i=0;i<2;i++){RtsBuilding found=null;foreach(var b in world.Base.Buildings)if(!b.removed&&b.team==i&&b.definition.id=="command"){found=b;break;}changed|=found!=headquarters[i];headquarters[i]=found;}}
            var eye=world.RenderToBody(world.View.transform.position);var rotation=world.View.transform.rotation;var projection=world.View.nonJitteredProjectionMatrix;var size=new Vector2(width,height);int terrainRevision=world.definition.Edits.Revision;
            for(int i=0;i<2;i++)changed|=States[i].visible&&headquarters[i]!=null&&headquarters[i].removed;
            // Retain the UI mesh while an RTS camera is stationary. Repainting this vector every frame
            // unnecessarily invalidates the shared front panel even though both infrastructure pins are fixed.
            bool first=!haveView;int reason=(first?1:0)|(changed?2:0)|(terrainRevision!=previousTerrainRevision?4:0)|(size!=previousSize?8:0)|((eye-previousEye).Magnitude>=.01?16:0)|(Quaternion.Angle(rotation,previousRotation)>=.01f?32:0)|(!projection.Equals(previousProjection)?64:0)|(refresh&&pendingText?128:0);if(reason==0)return;CacheMissReason=reason;
            haveView=true;previousEye=eye;previousRotation=rotation;previousProjection=projection;previousSize=size;previousTerrainRevision=terrainRevision;pendingText=!(refresh||first||changed);
            drawing.Lines.Clear();var center=new Vector2(width*.5f,height*.5f);float left=80,right=width-180,top=100,bottom=height-190;
            for(int i=0;i<2;i++){var b=headquarters[i];bool alive=b!=null&&!b.removed;labels[i].style.display=alive?DisplayStyle.Flex:DisplayStyle.None;if(!alive){States[i]=default;continue;}
                var target=surface.GetSurfacePositionPrecise(CubeSphere.Decode(b.position),35);var view=world.View.worldToCameraMatrix.MultiplyPoint3x4(world.RenderBodyPosition(target));var clip=projection*new Vector4(view.x,view.y,view.z,1);float w=Mathf.Abs(clip.w)>.0001f?clip.w:.0001f;var viewport=new Vector3(clip.x/w*.5f+.5f,clip.y/w*.5f+.5f,-view.z);var point=new Vector2(viewport.x*width,(1-viewport.y)*height);var bearing=point-center;if(viewport.z<0)bearing=-bearing;if(bearing.sqrMagnitude<1)bearing=Vector2.down;
                bool occluded=BehindHorizon(eye,target,world.definition.radius+world.definition.oceanHeight),edge=viewport.z<=0||occluded||point.x<left||point.x>right||point.y<top||point.y>bottom;
                if(edge){float scale=Mathf.Min((bearing.x<0?center.x-left:right-center.x)/Mathf.Max(.001f,Mathf.Abs(bearing.x)),(bearing.y<0?center.y-top:bottom-center.y)/Mathf.Max(.001f,Mathf.Abs(bearing.y)));point=center+bearing*scale;}
                // Separate nearly coincident team labels without changing the icon's actual bearing.
                float labelY=point.y+13;if(i==1&&States[0].visible&&(point-States[0].position).sqrMagnitude<10000)labelY+=42;
                labels[i].style.left=Mathf.Clamp(point.x-70,8,width-175);labels[i].style.top=Mathf.Clamp(labelY,8,height-55);double distance=(target-eye).Magnitude;
                if(refresh||first||changed)labels[i].text=$"TEAM {i+1} HQ  {(distance>=1000?(distance/1000).ToString("0.0")+" km":distance.ToString("0")+" m")}"+(occluded?"\nBEYOND HORIZON":edge?"\nOFFSCREEN":"");
                var colour=colours[i];void Line(Vector2 a,Vector2 c)=>drawing.Lines.Add((a,c,colour));
                if(edge){var forward=bearing.normalized;var side=new Vector2(-forward.y,forward.x);Line(point+forward*9,point-forward*7+side*7);Line(point+forward*9,point-forward*7-side*7);Line(point-forward*7+side*7,point-forward*7-side*7);}
                else{Line(point+Vector2.up*9,point+Vector2.right*9);Line(point+Vector2.right*9,point+Vector2.down*9);Line(point+Vector2.down*9,point+Vector2.left*9);Line(point+Vector2.left*9,point+Vector2.up*9);}
                States[i]=new MarkerState{visible=true,edge=edge,occluded=occluded,position=point,distance=distance};
            }GeometryRebuilds++;drawing.MarkDirtyRepaint();
        }
        public void Dispose(){overlay?.RemoveFromHierarchy();parent=null;overlay=null;}
    }
}
