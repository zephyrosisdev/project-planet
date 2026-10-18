using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsBase {
        internal static bool GatePassage(PlanetConstructionGrid.Footprint area,PlanetNavCoordinate cell)=>area.Contains(cell)&&((area.rotation&1)==0?cell.x==area.x+area.width/2:cell.y==area.y+area.depth/2);
        public void SetGate(RtsBuilding b,bool open){if(b.removed||b.definition.id!="gate")return;b.gateOpen=open;Roads?.RefreshBuildings();fixture.RefreshBuildingCosts(Buildings);UpdateGateVisuals();NotifyChanged();Notice=open?"Gate open: road traffic and ground units may pass":"Gate closed: road traffic waits and ground units are blocked";}
        sealed class GateVisual {public BoxCollider leaf;public Transform mesh;public bool open;}
        readonly System.Collections.Generic.Dictionary<int,GateVisual> gateVisuals=new System.Collections.Generic.Dictionary<int,GateVisual>();
        void UpdateGateVisuals(){foreach(var b in Buildings)if(!b.removed&&b.definition.id=="gate"){
            bool open=b.gateOpen&&b.Complete;if(!gateVisuals.TryGetValue(b.index,out var visual)){
                visual=new GateVisual{leaf=b.visual.GetComponent<BoxCollider>(),mesh=b.visual.transform.Find("Gate leaf"),open=!open};gateVisuals.Add(b.index,visual);
                if(visual.leaf!=null)for(int side=-1;side<=1;side+=2){var pillar=b.visual.AddComponent<BoxCollider>();pillar.center=visual.leaf.center+Vector3.right*side*Mathf.Max(0,visual.leaf.size.x*.5f-.5f);pillar.size=new Vector3(1,visual.leaf.size.y,visual.leaf.size.z);}
            }
            if(visual.open==open)continue;visual.open=open;if(visual.leaf!=null)visual.leaf.enabled=!open;if(visual.mesh!=null)visual.mesh.gameObject.SetActive(!open);
        }}
        internal bool BuildLocalDefences(int team,int cycle){
            // A compact HQ strongpoint, rather than an unbudgeted wall around an expanding city.
            var hq=Buildings.Find(b=>b.team==team&&!b.removed&&b.definition.id=="command");if(hq==null)return false;
            int count=0;foreach(var b in Buildings)if(b.team==team&&!b.removed&&(b.definition.id=="wall"||b.definition.id=="gate"))count++;
            if(count>=9)return false;var area=Area(hq);var d=Catalog.Buildings[count==0?"gate":"wall"];if(!AiCanSpend(team,d.cost))return false;
            int[] offsets={0,-2,2,-3,3,-4,4,-5,5};int x=offsets[count];
            var p=Grid.Point(area.face,area.x+area.width*.5+x,area.y+area.depth+5.5);bool built=ConstructOriented(d,team,p,0);
            if(built&&count==0){var gate=Buildings[Buildings.Count-1];gate.gateOpen=true;var colony=Settlements?[team];if(colony!=null)colony.council.decision="Build a gated HQ strongpoint and defensive turrets";}
            return built;
        }
    }
}
