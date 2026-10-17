using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.VFX;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        public const int RocketCapacity=16;public const float RocketLifetime=45,RocketCooldown=.6f;
        struct Rocket{public Double3 position,velocity;public Vector3 thrustDirection;public float age;public bool alive;}
        readonly Rocket[] rockets=new Rocket[RocketCapacity];readonly Matrix4x4[] rocketMatrices=new Matrix4x4[RocketCapacity];readonly RaycastHit[] rocketHits=new RaycastHit[32];
        readonly VisualEffect[] rocketBursts=new VisualEffect[8];readonly VFXEventAttribute[] rocketAttributes=new VFXEventAttribute[8];readonly Double3[] burstPositions=new Double3[8];
        readonly Vector4[] rocketTrailPoints=new Vector4[RocketCapacity*2];GraphicsBuffer rocketTrailBuffer;Material rocketTrailMaterial;public bool ShowRocketVisuals=true;
        Mesh rocketMesh;Material rocketMaterial;float fireDelay;int barrel,nextBurst;
        public int RocketsFired {get;private set;}public int RocketsExploded {get;private set;}public int RocketsTimedOut {get;private set;}
        public int ActiveRockets{get{int count=0;foreach(var r in rockets)if(r.alive)count++;return count;}}
        void InitializeWeapons(){rocketTrailBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,RocketCapacity*2,16);rocketTrailMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetShipRocketTrail"));rocketMesh=Resources.Load<Mesh>("MassiveArmy/CombatArt/Rocket");rocketMaterial=new Material(steel){name="Ship rocket casing",enableInstancing=true};if(world.StrategyEnabled)for(int i=0;i<rocketBursts.Length;i++)PrepareRocketBurst(i);}
        void PrepareRocketBurst(int slot){if(rocketBursts[slot]!=null)return;var go=new GameObject("Pooled ship rocket burst");go.transform.SetParent(world.transform);var effect=rocketBursts[slot]=go.AddComponent<VisualEffect>();effect.initialEventName="Dormant";effect.visualEffectAsset=Resources.Load<VisualEffectAsset>("MassiveArmy/CombatArt/CombatBurst");rocketAttributes[slot]=effect.CreateVFXEventAttribute();burstPositions[slot]=pose.position;}
        public bool TryFireRocket(){if(!active||world.Paused||fireDelay>0)return false;int slot=Array.FindIndex(rockets,r=>!r.alive);if(slot<0)return false;
            var muzzle=new Vector3((barrel++%2==0?-1:1)*14,-2,44);Double3 start=LocalToBodyPrecise(muzzle);Vector3 heading=pose.rotation*Vector3.forward;
            // Aim both launch tubes at the cockpit sight's terrain intersection when one exists.
            var eye=LocalToBodyPrecise(PilotEye);if(surface.Raycast(new Ray(eye.Float,heading),out var hit))heading=(new Double3(hit)-start).Float.normalized;
            rockets[slot]=new Rocket{alive=true,position=start,thrustDirection=heading,velocity=new Double3(VelocityAt(muzzle))+new Double3(heading)*320};fireDelay=RocketCooldown;RocketsFired++;return true;
        }
        double RocketClearance(Double3 p){double radius=p.Magnitude;if(radius<1)return -world.definition.radius;var d=p.Float.normalized;return radius-world.definition.radius-Math.Max(surface.SampleHeight(d),world.definition.oceanHeight);}
        void ExplodeRocket(ref Rocket r,bool ground){r.alive=false;RocketsExploded++;if(!ground)RocketsTimedOut++;else world.ApplyShipRocketImpact(r.position.Float.normalized);
            int slot=nextBurst++%rocketBursts.Length;PrepareRocketBurst(slot);burstPositions[slot]=r.position;var fx=rocketBursts[slot];fx.gameObject.SetActive(true);fx.transform.SetPositionAndRotation(world.RenderBodyPosition(r.position),Quaternion.FromToRotation(Vector3.up,r.position.Float.normalized));fx.transform.localScale=Vector3.one*.65f;fx.Reinit();var attr=rocketAttributes[slot];attr.SetVector3("position",Vector3.zero);attr.SetVector3("velocity",Vector3.up*8);attr.SetVector3("color",new Vector3(3,1,.1f));attr.SetFloat("size",2);attr.SetFloat("lifetime",1.4f);fx.SendEvent("OnPlay",attr);
        }
        public void StepWeapons(float dt){if(!world.Paused){dt=Mathf.Clamp(dt,0,.1f);fireDelay=Mathf.Max(0,fireDelay-dt);if(active&&Application.isFocused&&Mouse.current?.leftButton.isPressed==true)TryFireRocket();if(ActiveRockets>0){Physics.SyncTransforms();float remaining=dt;while(remaining>0){float step=Mathf.Min(.02f,remaining);IntegrateRockets(step);remaining-=step;}}}
            for(int i=0;i<rocketBursts.Length;i++)if(rocketBursts[i]!=null){rocketBursts[i].pause=world.Paused;rocketBursts[i].transform.position=world.RenderBodyPosition(burstPositions[i]);}
        }
        internal void IntegrateRockets(float dt){for(int i=0;i<rockets.Length;i++){var r=rockets[i];if(!r.alive)continue;var start=r.position;double radius=start.Magnitude;Vector3 up=start.Float.normalized;Vector3 heading=r.thrustDirection;
                var galaxy=world.SystemRuntime?.Galaxy;var acceleration=galaxy!=null?galaxy.RelativeAcceleration(start,galaxy.NodeFor(world.definition),galaxy.Time):new Double3(up)*(-world.definition.gravitationalParameter/Math.Max(1,radius*radius));if(r.age<2)acceleration+=new Double3(heading)*60;r.velocity+=acceleration*dt;var end=start+r.velocity*dt;var delta=end-start;float distance=(float)delta.Magnitude;double fraction=2;
                // A swept physics ray handles building hulls. The ship firing the rocket is excluded.
                if(distance>1e-5f){int count=Physics.RaycastNonAlloc(new Ray(world.RenderBodyPosition(start),delta.Float/distance),rocketHits,distance,~0,QueryTriggerInteraction.Ignore);for(int h=0;h<count;h++)if(!Owns(rocketHits[h].collider))fraction=Math.Min(fraction,rocketHits[h].distance/distance);}
                // Canonical terrain/ocean sweep remains accurate when the camera origin moves.
                double previous=0;for(int probe=0;probe<=4;probe++){double t=probe*.25;if(RocketClearance(start+delta*t)<=.1){double low=previous,high=t;for(int k=0;k<12;k++){double middle=(low+high)*.5;if(RocketClearance(start+delta*middle)>.1)low=middle;else high=middle;}fraction=Math.Min(fraction,high);break;}previous=t;}
                r.position=fraction<=1?start+delta*fraction:end;r.age+=dt;if(fraction<=1)ExplodeRocket(ref r,true);else if(r.age>=RocketLifetime)ExplodeRocket(ref r,false);rockets[i]=r;
            }}
        public void RenderWeapons(){if(!ShowRocketVisuals)return;int count=0;var anchor=pose.position;foreach(var r in rockets)if(r.alive){var direction=r.velocity.Float.normalized;rocketMatrices[count]=Matrix4x4.TRS(world.RenderBodyPosition(r.position),Quaternion.FromToRotation(Vector3.up,direction),Vector3.one*.12f);float length=r.age<2?25:6;rocketTrailPoints[count*2]=new Vector4((float)(r.position.x-anchor.x-direction.x*length),(float)(r.position.y-anchor.y-direction.y*length),(float)(r.position.z-anchor.z-direction.z*length),.05f);var head=(r.position-anchor).Float;rocketTrailPoints[count*2+1]=new Vector4(head.x,head.y,head.z,r.age<2?1:.35f);count++;}
            if(count==0)return;if(rocketMesh!=null)Graphics.DrawMeshInstanced(rocketMesh,0,rocketMaterial,rocketMatrices,count,null,ShadowCastingMode.Off,false,0,camera);
            rocketTrailBuffer.SetData(rocketTrailPoints,0,0,count*2);rocketTrailMaterial.SetBuffer("_RocketPoints",rocketTrailBuffer);rocketTrailMaterial.SetVector("_RocketOrigin",world.RenderBodyPosition(anchor));rocketTrailMaterial.SetVector("_RocketScreen",new Vector4(camera.pixelWidth,camera.pixelHeight,camera.nearClipPlane,0));rocketTrailMaterial.SetMatrix("_RocketVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);Graphics.DrawProcedural(rocketTrailMaterial,new Bounds(Vector3.zero,Vector3.one*Mathf.Max(10000,(float)pose.position.Magnitude*8)),MeshTopology.Triangles,count*6,1,camera,null,ShadowCastingMode.Off,false);
        }
        internal Double3 RocketPositionForValidation{get{foreach(var r in rockets)if(r.alive)return r.position;return new Double3();}}
        internal Double3 RocketVelocityForValidation{get{foreach(var r in rockets)if(r.alive)return r.velocity;return new Double3();}}
        void DisposeWeapons(){rocketTrailBuffer?.Dispose();ArmyLifetime.Destroy(rocketTrailMaterial);foreach(var attr in rocketAttributes)attr?.Dispose();foreach(var effect in rocketBursts)if(effect!=null)ArmyLifetime.Destroy(effect.gameObject);ArmyLifetime.Destroy(rocketMaterial);}
    }
}
