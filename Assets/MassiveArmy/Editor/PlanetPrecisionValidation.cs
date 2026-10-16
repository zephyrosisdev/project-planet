using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public static class PlanetPrecisionValidation
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();public double tinyStepTravel,maximumCameraOffset;}
        public static void Run()
        {
            var report=new Report();Directory.CreateDirectory("TestResults/Planetary/Precision");
            void Check(bool valid,string text){if(!valid)throw new InvalidOperationException(text);report.checks.Add(text);}
            try{
                var shared=new UniversePosition(long.MaxValue-100,-123,456,new Double3(524287.9,-524287.8,.125));
                var a=new FloatingOrigin(shared);var b=new FloatingOrigin(shared.Add(new Double3(128,16,-32)));
                int shifts=0;a.Rebased+=_=>shifts++;
                var point=shared.Add(new Double3(.001,.002,.003));a.Anchor(point);
                Check(a.RenderPosition(point)==Vector3.zero,"Observer is exactly zero even beside a sector boundary near int64 limits");
                Check(shifts==1&&!a.Anchor(point)&&shifts==1,"Stationary observer does not emit redundant origin shifts");
                var otherOrigin=b.Origin;
                for(int i=0;i<1000;i++)a.Anchor(shared.Add(new Double3(i*.001,0,0)));
                Check(b.Origin.RelativeTo(otherOrigin).Magnitude==0,"One client changing its origin never shifts a second client");
                var ra=a.RenderPosition(point);var rb=b.RenderPosition(point);
                Check((ra-rb).magnitude>100,"Independent clients render the same entity at different local positions");
                Check(a.RenderToUniverse(ra).RelativeTo(point).Magnitude<.0001&&b.RenderToUniverse(rb).RelativeTo(point).Magnitude<.0001,"Both client render poses resolve to the same shared universe position within 0.1 mm");
                Check(shared.sectorX==long.MaxValue-100&&Math.Abs(shared.local.x-524287.9)<1e-9,"Render origin changes never rewrite the canonical simulation position");
                var frames=new ReferenceFrameManager{Origin=shared};var framePose=new SpacePose{position=new Double3(900,50,30),rotation=Quaternion.Euler(30,55,10),linearVelocity=new Double3(1000,0,0),angularVelocity=new Vector3(0,.2f,0)};
                frames.Register(new ReferenceFrame(42,0,FrameKind.Ship,framePose));var occupant=new SpacePose{frameId=42,position=new Double3(1.25,-4,27),rotation=Quaternion.identity};var resolved=frames.Resolve(occupant);a.Anchor(resolved.position);b.Anchor(resolved.position.Add(new Double3(400,0,0)));
                Check(a.RenderPosition(resolved.position)==Vector3.zero&&(b.RenderToUniverse(b.RenderPosition(resolved.position)).RelativeTo(resolved.position)).Magnitude<.0001,"Ship occupant uses shared frame ID/local pose with independent client origins");
                Check((frames.Resolve(occupant).linearVelocity-resolved.linearVelocity).Magnitude==0,"Camera anchoring does not change inertial or rotating-frame velocity");
                ValidateGround(report,Check,shared);
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();throw;}finally{File.WriteAllText("TestResults/Planetary/Precision/precision.json",JsonUtility.ToJson(report,true));}
        }
        static void ValidateGround(Report report,Action<bool,string> check,UniversePosition centre)
        {
            var definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.radius=120000;definition.terrainHeight=0;definition.gravitationalParameter=9.81*120000d*120000;definition.position=centre;
            var root=new GameObject("Precision observer fixture");var camera=new GameObject("Precision camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);var surface=new PlanetSurface(definition);var origin=new FloatingOrigin(centre);var player=new PlanetFirstPersonController(surface,origin,camera,root.transform){CaptureCursor=false,WalkSpeed=.001f};
            try{
                var up=new Vector3(1,1,1).normalized;player.Enter(up,Vector3.ProjectOnPlane(Vector3.forward,up).normalized);var start=player.PreciseBodyPosition;
                for(int i=0;i<6000;i++){player.Step(1f/60,Vector2.up,Vector2.zero);report.maximumCameraOffset=Math.Max(report.maximumCameraOffset,camera.transform.position.magnitude);}
                report.tinyStepTravel=(player.PreciseBodyPosition-start).Magnitude;
                check(report.tinyStepTravel>.099&&report.tinyStepTravel<.101,"6000 sub-float-ULP steps retain 0.1 m travel on a 120 km diagonal surface");
                check(Math.Abs(player.PreciseBodyPosition.Magnitude-120000.05)<.0001,"Double ground contact preserves altitude within 0.1 mm");
                check(report.maximumCameraOffset==0,"Ground FPS camera stays exactly at Unity world zero throughout movement");
                check(origin.RenderToUniverse(camera.transform.position).RelativeTo(definition.position.Add(player.PreciseBodyPosition+player.PreciseBodyPosition*(player.EyeHeight/player.PreciseBodyPosition.Magnitude))).Magnitude<1e-7,"Zero camera corresponds to the correct canonical eye pose");
                player.SetActive(false);var rig=camera.gameObject.AddComponent<PlanetRtsCamera>();rig.InputEnabled=false;rig.Initialize(surface,origin);
                foreach(var dir in new[]{Vector3.up,Vector3.down,Vector3.left,Vector3.forward,new Vector3(-1,1,-1).normalized})foreach(float distance in new[]{8f,180f,6500f,1440000f}){rig.SetFocusDirection(dir);rig.Distance=distance;rig.RefreshPose();if(camera.transform.position!=Vector3.zero)throw new InvalidOperationException("RTS camera left zero");}
                check(true,"RTS camera stays at zero through poles, surface zoom and distant strategic view");
            }finally{player.Dispose();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(definition);}
        }
    }
}
