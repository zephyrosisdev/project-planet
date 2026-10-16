using System;
using System.Linq;
using UnityEngine;

namespace MassiveArmy
{
    public sealed partial class RtsWorld
    {
        public bool TryFindPlacement(string id,int team,out Vector2 position)
        {
            var d=Catalog.Buildings[id];
            if(d.terrain==TerrainRule.Node){
                foreach(var node in Nodes.Where(n=>n.claimedBy<0&&n.remaining>0&&d.nodes.Contains(n.kind)).OrderBy(n=>Field.Distance(n.position,BasePosition(team))))
                    if(AIPlacementAllowed(d,team,node.position).valid){position=SnapPlacement(d,node.position);return true;}
            }else{
                var anchors=Buildings.Where(b=>!b.removed&&b.team==team&&b.Complete&&b.definition.buildRadius>0).Select(b=>b.position).ToList();
                anchors.AddRange(Enumerable.Range(0,16).Where(s=>SectorOwner[s]==team).Select(SectorPosition));
                int offset=aiThinking?aiPlans[team].cycle%Math.Max(1,anchors.Count):0;
                foreach(var anchor in anchors.Skip(offset).Concat(anchors.Take(offset)))for(int pass=0;pass<9;pass++)for(int k=0;k<16;k++){
                    if(aiThinking&&aiPlacementBudget<=0){position=Vector2.zero;return false;}
                    int ring=1+(pass+(aiThinking?aiPlans[team].cycle%9:0))%9;
                    float angle=k*Mathf.PI/8+(float)(aiThinking?aiPlans[team].random:random).NextDouble()*.15f;Vector2 p=anchor+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*ring*24;
                    if(AIPlacementAllowed(d,team,p).valid){position=SnapPlacement(d,p);return true;}
                }
            }
            position=Vector2.zero;return false;
        }
        public RtsBuilding Pick(Camera camera,Vector2 mouse)
        {
            var rig=camera.GetComponent<RtsCamera>();if(rig==null)return null;var ray=camera.ScreenPointToRay(mouse);
            RtsBuilding picked=null;float nearest=float.MaxValue;
            foreach(var b in Buildings)if(!b.removed&&b.visual!=null&&(b.team==0||Battle.IsVisible(b.position))){
                var size=BuildingGrid.Dimensions(b.definition,b.rotation);float top=8;
                foreach(var renderer in b.visual.GetComponentsInChildren<Renderer>())top=Mathf.Max(top,renderer.bounds.max.y-b.visual.transform.position.y);
                var centre=rig.Nearest(new Vector3(b.position.x,b.visual.transform.position.y+top*.5f,b.position.y));
                var bounds=new Bounds(centre,new Vector3(size.x*8,top,size.y*8));
                if(bounds.IntersectRay(ray,out float distance)&&distance<nearest){nearest=distance;picked=b;}
            }return picked;
        }
    }
}
