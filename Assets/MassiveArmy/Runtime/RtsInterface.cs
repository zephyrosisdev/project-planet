using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        [Serializable] public sealed class BaseHotkeyBindings
        {
            public Key command=Key.F1,barracks=Key.F2,factories=Key.F3,airbases=Key.F4,naval=Key.F5,construction=Key.B,technology=Key.T;
        }
        public BaseHotkeyBindings baseHotkeys=new BaseHotkeyBindings();
        public RtsWorld Base {get;private set;}
        public SwarmMode Swarm {get;private set;}
        public RtsBuilding SelectedBuilding {get;private set;}
        BuildingDefinition placing;
        RtsBuilding formationBuilding;
        GameObject ghost;
        Material ghostMaterial;
        bool modelGhost;
        readonly List<GameObject> wallGhosts=new List<GameObject>();
        Vector2 ghostPoint,wallStart,buildScroll,contextScroll,techScroll;
        bool buildOpen,techOpen,wallDragging,editingRoute;
        readonly List<Vector2> patrolPoints=new List<Vector2>();
        int buildCategory,searchPattern=6,placementRotation;
        readonly WallPreviewRouter wallRouter=new WallPreviewRouter();
        readonly List<int> wallRoute=new List<int>();
        readonly Dictionary<int,bool> wallBuildability=new Dictionary<int,bool>();
        int routeStart=-1,routeEnd=-1,routeRevision=-1;
        double nextWallRoute;
        float placementWheel;
        bool placementWheelConsumed;
        string wallRouteFailure="";
        GameObject placementGrid;
        Mesh placementGridMesh;
        Vector2 gridCentre;
        bool placementGridReady;
        string selectedTechnology="mechanized";
        PlacementResult placement;
        Texture2D fogMap;
        double nextFogMap;
        public void StartBaseBattle(int capacity=50000)
        {
            StartBuildingMatch(capacity,false);
        }
        public void StartSwarmBattle(int capacity=50000)
        {
            StartBuildingMatch(capacity,true);
        }
        public void StartTestBattle(int capacity=50000)=>StartBuildingMatch(capacity,false,true);
        // Explicit historical fixture for the existing flat-world regression harnesses.
        public void StartFlatValidationBattle(int capacity=50000,bool swarm=false,bool spectator=false)=>StartBuildingMatch(capacity,swarm,spectator,true);
        void StartBuildingMatch(int capacity,bool swarm,bool spectator=false,bool flatValidation=false)
        {
            if(State=="loading")return;settings.baseBuilding=true;settings.swarmMode=swarm;settings.spectatorMode=spectator;settings.activeUnits=32;settings.capacity=Mathf.Clamp(capacity+settings.ambientZombies,256,1000000);settings.ScaleWorld();settings.Validate();
            if(flatValidation)activeLoad=StartCoroutine(LoadBattle());else StartPlanetMatch();
        }
        void SendOrder(Vector2 p,ArmyOrder order,Vector2 mouse,FormationLayout? layout=null)
        {
            if(settings.spectatorMode)return;
            if(Base!=null)Battle.IssueCombinedOrder(View,p,order,mouse,layout);else Battle.IssueOrder(View,p,order,mouse,layout);
        }
        void BaseHotkeys(Keyboard k)
        {
            if(k[baseHotkeys.construction].wasPressedThisFrame)buildOpen=!buildOpen;if(k[baseHotkeys.technology].wasPressedThisFrame){CancelGestures();techOpen=!techOpen;}
            string[] ids={"command","barracks","factory","airbase","naval_yard"};
            var keys=new[]{k[baseHotkeys.command],k[baseHotkeys.barracks],k[baseHotkeys.factories],k[baseHotkeys.airbases],k[baseHotkeys.naval]};
            for(int i=0;i<keys.Length;i++)if(keys[i].wasPressedThisFrame){SelectedBuilding=Base.Buildings.FirstOrDefault(b=>!b.removed&&b.team==0&&b.definition.id==ids[i]);if(SelectedBuilding!=null){closedBuilding=null;rig.FocusSector(SelectedBuilding.position);}}
            var groups=new[]{k.digit0Key,k.digit1Key,k.digit2Key,k.digit3Key,k.digit4Key,k.digit5Key,k.digit6Key,k.digit7Key,k.digit8Key,k.digit9Key};
            for(int i=0;i<groups.Length;i++)if(groups[i].wasPressedThisFrame){SelectedBuilding=null;Battle.ControlGroup(i,k.ctrlKey.isPressed,k.shiftKey.isPressed);}
        }
        bool BasePointer(RtsPointerInput input,Vector2 mouse)
        {
            if(Base==null)return false;
            if(editingRoute&&!OverHud(mouse)&&input.type==PointerPhase.Down){
                if(input.button==0&&rig.GroundPoint(mouse,out var waypoint)&&patrolPoints.Count<16){patrolPoints.Add(waypoint);Base.Notice="Patrol: "+patrolPoints.Count+" / 16 waypoints · right-click to begin";}
                if(input.button==1){if(Battle.ScoutRoute(patrolPoints)){editingRoute=false;Base.Notice="Route patrol assigned to selected reconnaissance units";}else Base.Notice=patrolPoints.Count<2?"Add at least two waypoints":Battle.Notice;}
                return true;
            }
            if(placing!=null){
                if(input.type==PointerPhase.Down&&input.button==1){CancelPlacement();return true;}
                if(!OverHud(mouse)&&input.button==0){
                    if(input.type==PointerPhase.Down){if(placing.id=="wall"){wallStart=ghostPoint;wallDragging=true;}else if(placement.valid){var built=Base.Construct(placing.id,0,ghostPoint,rotation:placementRotation);if(built!=null&&!input.shift)CancelPlacement();}return true;}
                    if(input.type==PointerPhase.Up&&wallDragging){PlaceWallChain();wallDragging=false;if(!input.shift)CancelPlacement();return true;}
                }
                return !OverHud(mouse);
            }
            if(OverHud(mouse))return false;
            if(input.type==PointerPhase.Down&&input.button==0&&!attackArmed&&!attackUnitArmed){var picked=Base.Pick(View,mouse);if(picked!=null){SelectedBuilding=picked;closedBuilding=null;dragging=false;forming=false;Battle.CancelPreview();Battle.Select(View,new Rect(-1,-1,0,0),SelectionOperation.Replace,false);if(picked.definition.buildRadius>0)buildOpen=true;return true;}}
            if(input.type==PointerPhase.Down&&input.button==1&&SelectedBuilding!=null&&SelectedBuilding.team==0){
                if(rig.GroundPoint(mouse,out var p)){var factory=SelectedBuilding;factory.rally=p;factory.rallyUnit=-1;Battle.PickRallyUnit(View,mouse,id=>{if(!factory.removed){factory.rallyUnit=id;Base.Notice=id<0?"Rally point set on terrain":"Rally follows friendly unit / group at production completion";}});}
                return true;
            }
            return false;
        }
        void BeginPlacement(BuildingDefinition d)
        {
            if(settings.spectatorMode)return;
            CancelPlacement();placing=d;placementRotation=0;placementWheel=0;
            var artwork=Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue")?.Find(d.id);modelGhost=artwork!=null;
            if(modelGhost){ghost=new GameObject("Building shell placement preview");ghost.AddComponent<MeshFilter>().sharedMesh=artwork.near;ghost.AddComponent<MeshRenderer>();
                if(artwork.gateLeaf!=null){var leaf=new GameObject("Gate leaf preview");leaf.transform.SetParent(ghost.transform,false);leaf.AddComponent<MeshFilter>().sharedMesh=artwork.gateLeaf;leaf.AddComponent<MeshRenderer>();}}
            else{ghost=GameObject.CreatePrimitive(PrimitiveType.Cube);ArmyLifetime.Destroy(ghost.GetComponent<Collider>());}
            ghostMaterial=new Material(Resources.Load<Shader>("MassiveArmy/Ghost"));ghost.GetComponent<Renderer>().sharedMaterial=ghostMaterial;
            foreach(var renderer in ghost.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=ghostMaterial;
            ghost.transform.localScale=modelGhost?Vector3.one:new Vector3(d.footprintWidth*8,10,d.footprintDepth*8);CreatePlacementGrid();forming=dragging=false;Battle.CancelPreview();
        }
        void CancelPlacement()
        {
            placing=null;wallDragging=false;editingRoute=false;patrolPoints.Clear();ArmyLifetime.Destroy(ghost);ghost=null;ArmyLifetime.Destroy(ghostMaterial);ghostMaterial=null;
            foreach(var g in wallGhosts)ArmyLifetime.Destroy(g);wallGhosts.Clear();
            ArmyLifetime.Destroy(placementGrid);placementGrid=null;ArmyLifetime.Destroy(placementGridMesh);placementGridMesh=null;
            wallRoute.Clear();routeStart=routeEnd=routeRevision=-1;placementGridReady=false;gridCentre=Vector2.zero;
        }
        void RefreshWallRoute(bool force=false)
        {
            var wall=Base.Catalog.Buildings["wall"];
            var start=BuildingGrid.Anchor(wall,wallStart,field.Size);var end=BuildingGrid.Anchor(wall,ghostPoint,field.Size);
            int a=BuildingGrid.CellIndex(start.x,start.y,field.Size),b=BuildingGrid.CellIndex(end.x,end.y,field.Size);
            double now=Time.realtimeSinceStartupAsDouble;
            if(!force&&a==routeStart&&b==routeEnd&&Base.PlacementRevision==routeRevision&&now<nextWallRoute)return;
            routeStart=a;routeEnd=b;routeRevision=Base.PlacementRevision;nextWallRoute=now+.1;wallBuildability.Clear();
            bool Buildable(int cell){if(!wallBuildability.TryGetValue(cell,out bool valid)){valid=Base.WallCellBuildable(cell,0);wallBuildability[cell]=valid;}return valid;}
            wallRouter.Find(a,b,BuildingGrid.Resolution(field.Size),Buildable,wallRoute,out wallRouteFailure);
        }
        void PlaceWallChain()
        {
            RefreshWallRoute(true);
            if(wallRoute.Count==0){Base.Notice=wallRouteFailure;return;}
            Base.ConstructWallChain(wallRoute,0);
        }
        void RotatePlacement()
        {
            placementWheelConsumed=false;
            if(placing==null||Mouse.current==null)return;
            float wheel=Mouse.current.scroll.ReadValue().y;
            if(InputSystem.settings.scrollDeltaBehavior!=InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms&&(Application.platform==RuntimePlatform.WindowsPlayer||Application.platform==RuntimePlatform.WindowsEditor))wheel/=120;
            ApplyPlacementWheel(wheel,OverHud(Mouse.current.position.ReadValue()),Application.isFocused);
        }
        void ApplyPlacementWheel(float wheel,bool pointerBlocked,bool focused)
        {
            if(placing==null||!focused||pointerBlocked||settingsOpen||biomeOpen||techOpen||(ui?.TextFocused??false))return;
            placementWheelConsumed|=Mathf.Abs(wheel)>.001f;
            placementWheel+=wheel;int steps=(int)placementWheel;placementWheel-=steps;
            placementRotation=BuildingGrid.Rotation(placementRotation+steps);
        }
        void CreatePlacementGrid()
        {
            placementGrid=new GameObject("8 metre construction grid");placementGrid.AddComponent<MeshFilter>();placementGrid.AddComponent<MeshRenderer>().sharedMaterial=ghostMaterial;
            placementGridMesh=new Mesh{name="Local construction grid"};placementGrid.GetComponent<MeshFilter>().sharedMesh=placementGridMesh;
        }
        void UpdatePlacementGrid()
        {
            var cell=BuildingGrid.Snap(Base.Catalog.Buildings["wall"],ghostPoint,field.Size);
            if(placementGridReady&&field.Distance(cell,gridCentre)<.01f)return;gridCentre=cell;
            var vertices=new List<Vector3>();var indices=new List<int>();
            for(int z=-12;z<=12;z++)for(int x=-12;x<=12;x++){
                Vector2 a=cell+new Vector2(x*8-4,z*8-4);
                foreach(var delta in new[]{new Vector2(8,0),new Vector2(0,8)}){
                    Vector2 b=a+delta;int i=vertices.Count;
                    vertices.Add(new Vector3(a.x-cell.x,field.Height(a)+.5f,a.y-cell.y));vertices.Add(new Vector3(b.x-cell.x,field.Height(b)+.5f,b.y-cell.y));indices.Add(i);indices.Add(i+1);
                }
            }
            placementGridMesh.Clear();placementGridMesh.SetVertices(vertices);placementGridMesh.SetIndices(indices.ToArray(),MeshTopology.Lines,0);placementGridMesh.RecalculateBounds();placementGridReady=true;
        }
        void UpdatePlacement(Vector2? pointerPosition=null)
        {
            if(placing==null||ghost==null||Mouse.current==null)return;
            Vector2 cursor=pointerPosition??Mouse.current.position.ReadValue();bool blocked=OverHud(cursor);
            ghost.SetActive(!blocked);placementGrid.SetActive(!blocked);
            if(blocked){foreach(var g in wallGhosts)g.SetActive(false);return;}
            if(!rig.GroundPoint(cursor,out ghostPoint)){ghost.SetActive(false);placementGrid.SetActive(false);return;}
            if(placing.terrain==TerrainRule.Node){var n=Base.Nodes.Where(n=>placing.nodes.Contains(n.kind)).OrderBy(n=>field.Distance(n.position,ghostPoint)).FirstOrDefault();if(n!=null&&field.Distance(n.position,ghostPoint)<30)ghostPoint=n.position;}
            if(placing.id=="gate")placementRotation=Base.GateRotation(ghostPoint,0,placementRotation);
            ghostPoint=Base.SnapPlacement(placing,ghostPoint,placementRotation);
            placement=Base.ValidatePlacement(placing,0,ghostPoint,rotation:placementRotation);
            ghost.transform.rotation=Quaternion.Euler(0,placementRotation*90,0);
            ghost.transform.position=rig.Nearest(new Vector3(ghostPoint.x,modelGhost?RtsModelArt.FoundationHeight(field,placing,ghostPoint,placementRotation):field.Height(ghostPoint)+5,ghostPoint.y));
            UpdatePlacementGrid();placementGrid.transform.position=rig.Nearest(new Vector3(gridCentre.x,0,gridCentre.y));
            if(wallDragging){
                RefreshWallRoute();placement=wallRoute.Count>0?Base.ValidateWallChain(wallRoute,0):new PlacementResult(false,wallRouteFailure);
                while(wallGhosts.Count<wallRoute.Count){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);ArmyLifetime.Destroy(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=ghostMaterial;wallGhosts.Add(g);}
                for(int i=0;i<wallGhosts.Count;i++){wallGhosts[i].SetActive(i<wallRoute.Count);if(i<wallRoute.Count){Vector2 p=BuildingGrid.CellCentre(wallRoute[i],field.Size);wallGhosts[i].transform.position=rig.Nearest(new Vector3(p.x,field.Height(p)+4,p.y));wallGhosts[i].transform.localScale=new Vector3(8,8,8);wallGhosts[i].transform.rotation=Quaternion.identity;}}
            }else foreach(var g in wallGhosts)g.SetActive(false);
            Color colour=!placement.valid?new Color(1,.15f,.12f):placement.disadvantaged?Color.yellow:new Color(.05f,.8f,1);ghostMaterial.SetColor("_Tint",colour);
        }
    }
}
