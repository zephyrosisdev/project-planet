#if UNITY_EDITOR
using System;
using UnityEngine;

namespace MassiveArmy.Planetary {
    public sealed partial class PlanetSpacecraft {
        // Interior-only fixture avoids allocating fleet/weapon/trajectory simulation resources.
        PlanetSpacecraft(PlanetDevelopment world,Transform parent){
            this.world=world;origin=new FloatingOrigin(default(UniversePosition));camera=world.View;pose=new SpacePose{rotation=Quaternion.identity};
            hull=new GameObject("Door control validation interior");hull.transform.SetParent(parent,false);var shader=Shader.Find("HDRP/Lit");steel=new Material(shader);glass=new Material(shader);engine=new Material(shader);BuildInterior();
        }
        internal static void ValidateDoorControls(PlanetDevelopment world,Transform parent,Action<bool,string> check){
            var ship=new PlanetSpacecraft(world,parent);GameObject wall=null;
            try{
                Physics.SyncTransforms();
                for(int door=0;door<ship.hatches.Count;door++){
                    var hatch=ship.hatches[door];
                    for(int face=0;face<2;face++){
                        float side=face==0?-1:1;var point=ship.LocalToRender(hatch.controls[face]);var eye=point+Vector3.forward*(side*2.5f);
                        check(ship.FindInteraction(eye,-Vector3.forward*side)==door,hatch.name+": visible side button is reachable from "+(face==0?"aft":"forward"));
                    }
                    hatch.requested=true;hatch.panel.localPosition=hatch.closed+Vector3.right*12.6f;Physics.SyncTransforms();var control=ship.LocalToRender(hatch.controls[0]);
                    check(ship.FindInteraction(control-Vector3.forward*2.5f,Vector3.forward)==door&&ship.InteractionPrompt(door).Contains("CLOSE"),hatch.name+": stationary control remains usable when the door opens");
                }
                var button=ship.LocalToRender(ship.hatches[0].controls[0]);wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=button-Vector3.forward*1.2f;wall.transform.localScale=new Vector3(1,1,.3f);Physics.SyncTransforms();
                check(ship.FindInteraction(button-Vector3.forward*2.5f,Vector3.forward)<0,"Ship door buttons cannot be operated through a blocking wall");
            }finally{if(wall!=null)UnityEngine.Object.DestroyImmediate(wall);ship.DisposeInterior();ArmyLifetime.Destroy(ship.hull);ArmyLifetime.Destroy(ship.steel);ArmyLifetime.Destroy(ship.glass);ArmyLifetime.Destroy(ship.engine);}
        }
    }
}
#endif
