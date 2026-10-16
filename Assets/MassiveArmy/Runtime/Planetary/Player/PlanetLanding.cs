using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        public const float LandingMaxAltitude=600,LandingMaxSpeed=180,TakeoffRise=80;
        public bool LandingAvailable {get;private set;}
        public string LandingStatus {get;private set;}="LANDED · SPACE TO TAKE OFF";
        public Double3 LandingPosition {get;private set;}
        public Quaternion LandingRotation {get;private set;}=Quaternion.identity;
        public bool ShowLandingPreview=true;
        public bool AutoFlightActive {get;private set;}
        public float AutoFlightProgress=>AutoFlightActive?Mathf.Clamp01(autoElapsed/autoDuration):0;
        Double3 autoStart,autoEnd;Quaternion autoRotation,autoEndRotation;float autoElapsed,autoDuration;bool autoTakeoff;
        Double3 AutoPoint(float t){float eased=t*t*(3-2*t);return autoTakeoff?autoStart+(autoEnd-autoStart)*eased:LandingCurve(autoStart,autoEnd,eased);}
        void BeginAutoFlight(Double3 end,Quaternion rotation,bool takeoff){
            autoStart=pose.position;autoEnd=end;autoRotation=pose.rotation;autoEndRotation=rotation;autoTakeoff=takeoff;autoElapsed=0;
            autoDuration=takeoff?3.5f:Mathf.Clamp((float)(end-autoStart).Magnitude/45f,3.5f,12);
            SetFlightAssistance(true);AutoFlightActive=true;Landed=false;RotationLocked=false;GearDeployed=true;
            pose.linearVelocity=new Double3();pose.angularVelocity=Vector3.zero;angularVelocity=Vector3.zero;shipFrame.Pose=pose;
            LandingAvailable=false;LandingStatus=takeoff?"TAKING OFF":"LANDING";nextOrbitRefresh=0;nextArcRefresh=0;
        }
        void IntegrateAutoFlight(float dt){
            float elapsed=Mathf.Min(autoDuration,autoElapsed+dt),t=elapsed/autoDuration;
            var next=AutoPoint(t);float r=Mathf.Clamp01(t/.7f);r=r*r*(3-2*r);var rotation=Quaternion.Slerp(autoRotation,autoEndRotation,r);
            // Check both orientations while the hull turns, and the terrain footprint at the next pose.
            Physics.SyncTransforms();bool safe=HullSweep(pose.position,next,pose.rotation)&&HullSweep(pose.position,next,rotation);
            for(int z=-1;z<=1&&safe;z++)for(int x=-1;x<=1;x++){var foot=next+Double3.Rotate(rotation,new Double3(x*26,-8,z*50));var direction=foot.Float.normalized;if(foot.Magnitude<world.definition.radius+Math.Max(surface.SampleHeight(direction),world.definition.oceanHeight)-.35){safe=false;break;}}
            if(!safe){AutoFlightActive=false;pose.linearVelocity=new Double3();pose.angularVelocity=Vector3.zero;shipFrame.Pose=pose;SetFlightAssistance(true);LandingStatus="AUTO ASSIST STOPPED · PATH BLOCKED";nextLandingRefresh=Time.unscaledTime+2;return;}
            pose.linearVelocity=(next-pose.position)*(1.0/dt);var turn=rotation*Quaternion.Inverse(pose.rotation);turn.ToAngleAxis(out float angle,out Vector3 axis);if(angle>180)angle-=360;pose.angularVelocity=angle==0?Vector3.zero:axis*(angle*Mathf.Deg2Rad/dt);
            pose.position=next;pose.rotation=rotation;autoElapsed=elapsed;
            if(elapsed>=autoDuration){AutoFlightActive=false;Landed=!autoTakeoff;pose.position=autoEnd;pose.rotation=autoEndRotation;pose.linearVelocity=new Double3();pose.angularVelocity=Vector3.zero;angularVelocity=Vector3.zero;SetFlightAssistance(true);LandingStatus=Landed?"LANDED · SPACE TO TAKE OFF":"TAKEOFF COMPLETE";nextLandingRefresh=0;}
            else LandingStatus=(autoTakeoff?"TAKING OFF":"LANDING")+$" · {t*100:0}%";
            shipFrame.Pose=pose;
        }
        int landingRevision;float nextLandingRefresh;readonly Collider[] landingOverlaps=new Collider[128];readonly RaycastHit[] landingHits=new RaycastHit[128];
        readonly Vector4[] landingLines=new Vector4[160];GraphicsBuffer landingBuffer;Material landingMaterial;int landingLineCount;
        static readonly Vector3 HullCentre=new Vector3(0,-2,-7),HullHalf=new Vector3(27,5.5f,48);
        void InitializeLanding(){landingBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,landingLines.Length,16);landingMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetLanding"));}
        bool OverlapsHull(Double3 at,Quaternion rotation){int count=Physics.OverlapBoxNonAlloc(world.RenderBodyPosition(at+Double3.Rotate(rotation,new Double3(HullCentre))),HullHalf,landingOverlaps,rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);if(count==landingOverlaps.Length)return true;for(int i=0;i<count;i++)if(!Owns(landingOverlaps[i]))return true;return false;}
        bool HullSweep(Double3 from,Double3 to,Quaternion rotation){var delta=to-from;float distance=(float)delta.Magnitude;if(distance<.001f)return !OverlapsHull(to,rotation);var centre=world.RenderBodyPosition(from+Double3.Rotate(rotation,new Double3(HullCentre)));int count=Physics.BoxCastNonAlloc(centre,HullHalf,delta.Float/distance,landingHits,rotation,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);if(count==landingHits.Length)return false;for(int i=0;i<count;i++)if(!Owns(landingHits[i].collider))return false;return !OverlapsHull(to,rotation);}
        public bool TakeOff(){if(!Landed||AutoFlightActive)return false;Physics.SyncTransforms();var up=Direction;var target=pose.position+new Double3(up)*TakeoffRise;var levelRotation=Quaternion.FromToRotation(pose.rotation*Vector3.up,up)*pose.rotation;if(!HullSweep(pose.position,target,pose.rotation)||OverlapsHull(target,levelRotation)){LandingStatus="TAKEOFF BLOCKED · OVERHEAD OBSTACLE";return false;}
            BeginAutoFlight(target,levelRotation,true);Rebase();return true;
        }
        public bool TryLandingOrTakeoff(){if(!active||world.Paused||AutoFlightActive)return false;if(Landed)return TakeOff();RefreshLandingPreview();if(!LandingAvailable)return false;
            if(landingRevision!=world.definition.Edits.Revision){LandingAvailable=false;LandingStatus="LANDING RED · TERRAIN CHANGED";nextLandingRefresh=0;return false;}
            // Use exactly the displayed destination; recheck its safety without choosing another site.
            Physics.SyncTransforms();if(!CheckApproach(LandingPosition,LandingRotation)){LandingAvailable=false;LandingStatus="LANDING BLOCKED · APPROACH OBSTACLE";return false;}
            if(Speed>LandingMaxSpeed||GroundClearance>LandingMaxAltitude){LandingAvailable=false;LandingStatus="LANDING UNAVAILABLE · SLOW / DESCEND";return false;}
            BeginAutoFlight(LandingPosition,LandingRotation,false);Rebase();return true;
        }
        Double3 LandingCurve(Double3 start,Double3 end,float t){var up=end.Float.normalized;var horizontal=end-start;horizontal-=new Double3(up)*Vector3.Dot(horizontal.Float,up);var a=start+horizontal*.35;var b=end+new Double3(up)*Math.Min(180,Math.Max(20,(start-end).Magnitude*.3));double q=1-t;return start*(q*q*q)+a*(3*q*q*t)+b*(3*q*t*t)+end*(t*t*t);}
        bool CheckApproach(Double3 end,Quaternion rotation){var previous=pose.position;for(int i=1;i<=16;i++){var p=LandingCurve(pose.position,end,i/16f);if(!HullSweep(previous,p,rotation))return false;
                for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){if(x!=0&&z==0||x==0&&z!=0)continue;var foot=p+Double3.Rotate(rotation,new Double3(x*26,-8,z*50));var direction=foot.Float.normalized;if(foot.Magnitude<world.definition.radius+surface.SampleHeight(direction)-.35)return false;}
                previous=p;}return true;}
        public void RefreshLandingPreview(bool force=false){if(AutoFlightActive)return;if(!force&&Time.unscaledTime<nextLandingRefresh)return;nextLandingRefresh=Time.unscaledTime+.2f;LandingAvailable=false;
            if(Landed){if(!LandingStatus.StartsWith("TAKEOFF BLOCKED"))LandingStatus="LANDED · SPACE TO TAKE OFF";LandingPosition=pose.position;LandingRotation=pose.rotation;return;}
            landingRevision=world.definition.Edits.Revision;var up=Direction;var heading=Vector3.ProjectOnPlane(pose.rotation*Vector3.forward,up).normalized;if(heading.sqrMagnitude<.1f)heading=Vector3.ProjectOnPlane(pose.rotation*Vector3.up,up).normalized;
            var drift=Vector3.ProjectOnPlane(pose.linearVelocity.Float,up);var target=pose.position+new Double3(Vector3.ClampMagnitude(drift*.65f,140));var direction=target.Float.normalized;
            float h=surface.SampleHeight(direction);var right=Vector3.Cross(up,heading).normalized;
            float Height(float x,float z){var d=(new Double3(direction)*world.definition.radius+new Double3(right*x+heading*z)).Float.normalized;return surface.SampleHeight(d);}
            float sx=(Height(26,0)-Height(-26,0))/52,sz=(Height(0,50)-Height(0,-50))/100;var normal=(direction-right*sx-heading*sz).normalized;
            LandingRotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(heading,normal).normalized,normal);float high=0,low=0;bool water=h<=world.definition.oceanHeight;
            for(int z=-4;z<=4;z++)for(int x=-2;x<=2;x++){float px=x*13,pz=z*12.5f;float sample=Height(px,pz);water|=sample<=world.definition.oceanHeight;float residual=sample-h-sx*px-sz*pz;high=Mathf.Max(high,residual);low=Mathf.Min(low,residual);}
            LandingPosition=surface.GetSurfacePositionPrecise(direction)+new Double3(normal)*(8.3+high);
            if(GroundClearance>LandingMaxAltitude){LandingStatus="LANDING RED · DESCEND BELOW 600 m";return;}if(Speed>LandingMaxSpeed){LandingStatus="LANDING RED · SLOW BELOW 180 m/s";return;}if(water){LandingStatus="LANDING RED · WATER";return;}
            if(Vector3.Dot(normal,direction)<Mathf.Cos(8*Mathf.Deg2Rad)||high-low>2.5f){LandingStatus="LANDING RED · STEEP / UNEVEN GROUND";return;}
            Physics.SyncTransforms();if(OverlapsHull(LandingPosition,LandingRotation)||!CheckApproach(LandingPosition,LandingRotation)){LandingStatus="LANDING RED · OBSTRUCTED APPROACH";return;}
            LandingAvailable=true;LandingStatus="LANDING GREEN · SPACE TO LAND";
        }
        void LandingLine(Double3 a,Double3 b,Double3 anchor){var x=(a-anchor).Float;var y=(b-anchor).Float;landingLines[landingLineCount++]=new Vector4(x.x,x.y,x.z,1);landingLines[landingLineCount++]=new Vector4(y.x,y.y,y.z,1);}
        public void RenderLanding(){if(!active)return;RefreshLandingPreview();if(Landed||autoTakeoff&&AutoFlightActive||!ShowLandingPreview)return;landingLineCount=0;var start=pose.position;var previous=start;
            for(int i=1;i<=16;i++){var point=AutoFlightActive?AutoPoint(Mathf.Lerp(AutoFlightProgress,1,i/16f)):LandingCurve(start,LandingPosition,i/16f);LandingLine(previous,point,start);previous=point;}
            // A full-size landing footprint and hull outline show the orientation the landing transition will adopt.
            Double3 Point(float x,float y,float z)=>LandingPosition+Double3.Rotate(LandingRotation,new Double3(x,y,z));
            for(int level=0;level<2;level++){float y=level==0?-7.8f:2.5f;LandingLine(Point(-27,y,-50),Point(27,y,-50),start);LandingLine(Point(27,y,-50),Point(27,y,39),start);LandingLine(Point(27,y,39),Point(-27,y,39),start);LandingLine(Point(-27,y,39),Point(-27,y,-50),start);}
            LandingLine(Point(-27,-7.8f,-50),Point(-27,2.5f,-50),start);LandingLine(Point(27,-7.8f,-50),Point(27,2.5f,-50),start);LandingLine(Point(-27,-7.8f,39),Point(-27,2.5f,39),start);LandingLine(Point(27,-7.8f,39),Point(27,2.5f,39),start);LandingLine(Point(0,2.5f,10),Point(0,2.5f,50),start);LandingLine(Point(0,2.5f,50),Point(-8,2.5f,38),start);LandingLine(Point(0,2.5f,50),Point(8,2.5f,38),start);
            landingBuffer.SetData(landingLines,0,0,landingLineCount);landingMaterial.SetBuffer("_LandingPoints",landingBuffer);landingMaterial.SetVector("_LandingOrigin",world.RenderBodyPosition(start));landingMaterial.SetVector("_LandingScreen",new Vector4(camera.pixelWidth,camera.pixelHeight,camera.nearClipPlane,0));landingMaterial.SetMatrix("_LandingVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);landingMaterial.SetColor("_LandingColour",(LandingAvailable||AutoFlightActive)?new Color(.08f,1,.25f,.8f):new Color(1,.06f,.02f,.85f));Graphics.DrawProcedural(landingMaterial,new Bounds(Vector3.zero,Vector3.one*Mathf.Max(10000,(float)pose.position.Magnitude*8)),MeshTopology.Triangles,landingLineCount/2*6,1,camera,null,ShadowCastingMode.Off,false);
        }
        void DisposeLanding(){landingBuffer?.Dispose();ArmyLifetime.Destroy(landingMaterial);}
    }
}
