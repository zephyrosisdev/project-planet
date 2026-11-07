using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        public bool DaylightPreviewActive {get;private set;}
        public bool DaylightPreviewRunning {get;set;}
        public float DaylightPreviewCycleSeconds {get;set;}=60;
        public float DaylightPreviewHour {get;private set;}=12;
        public bool CanPreviewDaylight=>Ready&&ResidentActive&&lighting?.Sun!=null;
        Vector3 previewUp,previewEast;Quaternion previewOriginalSun;
        public bool BeginDaylightPreview(){
            if(!Ready||lighting?.Sun==null||view==null)return false;
            if(DaylightPreviewActive)return true;
            previewOriginalSun=lighting.Sun.transform.rotation;
            previewUp=PlanetPrecisionPolicy.Direction(RenderToBody(view.transform.position));
            var toward=-lighting.Sun.transform.forward;previewEast=Vector3.ProjectOnPlane(toward,previewUp).normalized;
            if(previewEast.sqrMagnitude<.5f){previewEast=Vector3.ProjectOnPlane(view.transform.forward,previewUp).normalized;if(previewEast.sqrMagnitude<.5f)previewEast=Vector3.Cross(previewUp,Mathf.Abs(previewUp.y)<.9f?Vector3.up:Vector3.right).normalized;}
            DaylightPreviewHour=Mathf.Repeat(Mathf.Atan2(Vector3.Dot(toward,previewEast),-Vector3.Dot(toward,previewUp))*12/Mathf.PI,24);
            DaylightPreviewActive=true;ApplyDaylightPreview();return true;
        }
        public void SetDaylightPreviewHour(float hour){if(float.IsNaN(hour)||float.IsInfinity(hour)||!BeginDaylightPreview())return;DaylightPreviewHour=Mathf.Repeat(hour,24);ApplyDaylightPreview();}
        public void StopDaylightPreview(){
            if(!DaylightPreviewActive)return;DaylightPreviewActive=false;DaylightPreviewRunning=false;
            SystemRuntime?.SkyForValidation?.SetSunPreview(Vector3.zero,false);
            if(lighting?.Sun!=null)lighting.Sun.transform.rotation=previewOriginalSun;
            SystemRuntime?.UpdateStarlight();
        }
        internal static float AdvanceDaylightHour(float hour,float seconds,float cycleSeconds)=>Mathf.Repeat(hour+Mathf.Max(0,seconds)*24/Mathf.Max(5,cycleSeconds),24);
        internal static Vector3 DaylightDirection(Vector3 up,Vector3 east,float hour){float angle=hour*Mathf.PI/12;return (-up*Mathf.Cos(angle)+east*Mathf.Sin(angle)).normalized;}
        void UpdateDaylightPreview(){if(!DaylightPreviewActive)return;if(DaylightPreviewRunning&&!Paused)DaylightPreviewHour=AdvanceDaylightHour(DaylightPreviewHour,Time.unscaledDeltaTime,DaylightPreviewCycleSeconds);ApplyDaylightPreview();}
        void ApplyDaylightPreview(){
            if(lighting?.Sun==null)return;var toward=DaylightDirection(previewUp,previewEast,DaylightPreviewHour);
            lighting.Sun.transform.rotation=Quaternion.LookRotation(-toward,Mathf.Abs(Vector3.Dot(toward,previewUp))>.99f?previewEast:previewUp);
            lighting.SetCompanion(Vector3.up,Color.white,0);
            SystemRuntime?.SkyForValidation?.SetSunPreview(toward,true);
        }
    }
}
