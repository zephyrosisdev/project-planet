using System;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    [Serializable] public sealed class WalkerLegRig
    {
        public Transform upper, lower, foot;
        [NonSerialized] public Vector3 target, anchor, start, pole;
        [NonSerialized] public Vector3 normal=Vector3.up;
        [NonSerialized] public float progress;
        [NonSerialized] public WalkerFootState state;
    }
    /// <summary>Art references only. Simulation belongs to the bounded vehicle controller, never to individual bones.</summary>
    public sealed class VehicleRig : MonoBehaviour
    {
        public VehicleDefinition definition;
        public Transform torso, muzzle;
        public WalkerLegRig[] legs=Array.Empty<WalkerLegRig>();
        public Transform[] wheels=Array.Empty<Transform>();
        public Renderer[] articulatedRenderers=Array.Empty<Renderer>();
        public GameObject distantProxy;
        public Collider chassis;
        void OnDrawGizmosSelected(){
            if(definition==null)return;
            Gizmos.matrix=transform.localToWorldMatrix;Gizmos.color=Color.cyan;Gizmos.DrawWireCube(Vector3.up*definition.size.y*.5f,definition.size);
            foreach(var seat in definition.seats){Gizmos.color=Color.green;Gizmos.DrawWireSphere(seat.eye,.15f);Gizmos.DrawLine(seat.entry,seat.exit);}
            foreach(var leg in definition.legs){Gizmos.color=Color.yellow;Gizmos.DrawWireSphere(leg.hip,Mathf.Min(leg.maxReach,leg.upperLength+leg.lowerLength));Gizmos.DrawLine(leg.hip,leg.stance);Gizmos.DrawWireCube(leg.stance,new Vector3(leg.footWidth,.08f,leg.footLength));}
            Gizmos.matrix=Matrix4x4.identity;
        }
    }
}
