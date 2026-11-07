using System;
using UnityEngine;
using UnityEngine.VFX;
namespace MassiveArmy.Planetary {
    [Serializable]public struct ShipReentryThermalState {
        public float heat,density,ionization,heatingRate;
        public void Advance(float dt,bool atmosphere,float pressure,float height,double altitude,double speed,float tide=1){
            dt=Mathf.Clamp(dt,0,.1f);density=0;ionization=0;heatingRate=0;
            if(atmosphere&&pressure>0&&height>0&&altitude<height&&speed<1e7){
                float h=Mathf.Max(0,(float)altitude),top=height*Mathf.Min(1,.98f+(tide-1)*.25f);
                density=pressure*Mathf.Exp(-h/Mathf.Max(1,height*.125f))*tide*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(top*.7f,top,h)));
                float v=(float)Math.Max(0,speed),root=Mathf.Sqrt(density);
                heatingRate=Mathf.Min(6,root*Mathf.Pow(v/2500,3));
                ionization=Mathf.Clamp01(root*Mathf.Pow(v/1100,2)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(650,1200,v)));
            }
            heat=Mathf.Clamp01(heat*Mathf.Exp(-(.06f+.08f*heat*heat)*dt)+heatingRate*.22f*dt);
        }
    }
    public sealed partial class PlanetSpacecraft {
        ShipReentryThermalState reentry;VisualEffect reentryVfx;double nextReentryTide;Vector4 reentryGravity;bool reentryAssetWarning;int reentryViewFrame=-1;
        public ShipReentryThermalState ReentryThermalState=>reentry;
        internal string ReentryReadout=>reentry.heat>.005f||reentry.ionization>.005f?$"\nREENTRY HEAT {reentry.heat*100:0}% · IONIZATION {reentry.ionization*100:0}%"+(reentry.heat>.75f?" · OVERHEAT":""):"";
        void StepReentry(float elapsed){
            if(world.Paused)return;FlightReference(out var radial,out var velocity,out _,out _);var body=world.definition;var model=world.SystemRuntime?.Galaxy;
            if(model!=null&&instrumentPlanet>=0){int index=model.Nodes[instrumentPlanet].planet;if(index>=0&&index<world.SystemRuntime.Bodies.Length)body=world.SystemRuntime.Bodies[index];}
            if(Time.unscaledTimeAsDouble>=nextReentryTide){reentryGravity=PlanetAtmosphericTides.GravityAxis(world,body);nextReentryTide=Time.unscaledTimeAsDouble+1;}
            var sun=world.LightingForValidation?.Sun;float tide=PlanetAtmosphericTides.Multiplier(body.weather,radial.Float.normalized,sun!=null?-sun.transform.forward:Vector3.up,reentryGravity);
            reentry.Advance(VehicleMotor.LocalStepDuration(elapsed),body.hasAtmosphere,body.surfacePressure,body.atmosphereHeight,radial.Magnitude-body.radius,CruiseActive?0:velocity.Magnitude,tide);
        }
        void PresentReentry(){
            if(reentryViewFrame==Time.frameCount)return;reentryViewFrame=Time.frameCount;
            float intensity=Mathf.Max(reentry.ionization,reentry.heat*Mathf.Clamp01(Mathf.Sqrt(reentry.density)*3));
            if(!world.ResidentActive||world.RemoteView)return;
            if(reentryVfx==null&&intensity>.005f){var asset=Resources.Load<VisualEffectAsset>("MassiveArmy/ShipArt/ReentryPlasma");if(asset==null){if(!reentryAssetWarning){reentryAssetWarning=true;Debug.LogWarning("Ship re-entry graph missing. Build it with Synthetic Front/Art/Build ship re-entry VFX Graph.");}return;}var go=new GameObject("Player ship GPU re-entry plasma (2048 particles)");go.transform.SetParent(hull.transform,false);reentryVfx=go.AddComponent<VisualEffect>();reentryVfx.initialEventName="OnPlay";reentryVfx.visualEffectAsset=asset;}
            if(reentryVfx==null)return;reentryVfx.pause=world.Paused;
            if(intensity<.001f){if(reentryVfx.gameObject.activeSelf){reentryVfx.Stop();reentryVfx.gameObject.SetActive(false);}return;}
            if(!reentryVfx.gameObject.activeSelf){reentryVfx.gameObject.SetActive(true);reentryVfx.Reinit();}
            FlightReference(out _,out var velocity,out _,out _);var flow=Quaternion.Inverse(pose.rotation)*velocity.Float.normalized;
            reentryVfx.SetFloat("EmissionRate",Mathf.Lerp(60,1600,intensity));reentryVfx.SetFloat("Intensity",intensity);reentryVfx.SetFloat("Heat",reentry.heat);reentryVfx.SetVector3("FlowDirection",flow.sqrMagnitude>.1f?flow:Vector3.forward);reentryVfx.SetVector3("HalfExtents",new Vector3(28,7,46));
        }
        internal void CopyReentryStateFrom(PlanetSpacecraft other){reentry=other.reentry;ReleaseReentryView();}
        internal void ReleaseReentryView(){if(reentryVfx!=null){reentryVfx.gameObject.SetActive(false);ArmyLifetime.Destroy(reentryVfx.gameObject);}reentryVfx=null;reentryViewFrame=-1;}
    }
}
