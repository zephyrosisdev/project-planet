using UnityEngine;
namespace MassiveArmy.Planetary {
    internal static class UtilityPrototypes {
        internal static ColonyPrototypeAsset Create(string id){
            if(id!="machine-water-reservoir")return null;
            var g=new ModularGeometry();var shell=new Color(.7f,.78f,.82f,0);var blue=new Color(.025f,.32f,.55f,0);var dark=new Color(.1f,.15f,.18f,0);
            g.Box(new Vector3(0,.15f,0),new Vector3(3,.3f,3),dark);for(int side=-1;side<=1;side+=2)g.Box(new Vector3(side, .4f,0),new Vector3(.2f,.5f,2.5f),shell);
            const int sides=16;for(int n=0;n<sides;n++){float a=n*2*Mathf.PI/sides,b=(n+1)*2*Mathf.PI/sides;var p=new Vector3(Mathf.Cos(a)*1.3f,.55f,Mathf.Sin(a)*1.3f);var q=new Vector3(Mathf.Cos(b)*1.3f,.55f,Mathf.Sin(b)*1.3f);g.Quad(p,p+Vector3.up*2.5f,q+Vector3.up*2.5f,q,n%4==0?shell:blue);g.Triangle(new Vector3(0,3.05f,0),q+Vector3.up*2.5f,p+Vector3.up*2.5f,shell);g.Triangle(new Vector3(0,.55f,0),p,q,dark);}
            g.Box(new Vector3(0,3.2f,0),new Vector3(.6f,.3f,.6f),dark);g.Box(new Vector3(0,.8f,-1.4f),new Vector3(.3f,.3f,.35f),shell);g.Box(new Vector3(.8f,1.65f,-1.1f),new Vector3(.12f,1.6f,.08f),new Color(.03f,.8f,1,1));
            var asset=new ColonyPrototypeAsset{id=id,family="machine",dimensions=new Vector3(3,3.4f,3),solids=new[]{new ColonyPrototypeSolid{polygon=new[]{new Vector2(-1.5f,-1.5f),new Vector2(1.5f,-1.5f),new Vector2(1.5f,1.5f),new Vector2(-1.5f,1.5f)},bottom=0,top=3.4f}},sockets=new[]{new ColonyPrototypeSocket{name="Water",kind="Fluid",position=new Vector3(0,.8f,-1.5f)}},lod=new ColonyPrototypeMesh()};asset.SetMesh(g.Build(id));asset.lod.ShareGeometry(asset);return asset;
        }
    }
}
