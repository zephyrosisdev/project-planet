using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetRtsHud
    {
        VisualElement resourceTooltip;Label resourceTooltipText;
        Double3[] hoverCentres=Array.Empty<Double3>();int hoverTerrainRevision=-1;
        void BuildResourceHover(){
            resourceTooltip=front.Q("tooltip");resourceTooltip.Clear();resourceTooltip.pickingMode=PickingMode.Ignore;resourceTooltip.style.width=320;
            resourceTooltipText=new Label{name="resource-node-tooltip",pickingMode=PickingMode.Ignore};resourceTooltipText.AddToClassList("body");resourceTooltip.Add(resourceTooltipText);
        }
        internal ResourceNode ResourceAt(Vector2 screen){
            int terrainRevision=fixture.definition.Edits.Revision;
            if(hoverCentres.Length!=world.Nodes.Count||hoverTerrainRevision!=terrainRevision){
                hoverCentres=new Double3[world.Nodes.Count];for(int i=0;i<world.Nodes.Count;i++)hoverCentres[i]=fixture.Rig.Surface.GetSurfacePositionPrecise(CubeSphere.Decode(world.Nodes[i].position));hoverTerrainRevision=terrainRevision;
            }
            var ray=fixture.View.ScreenPointToRay(screen);var observer=fixture.ObserverPosition.RelativeTo(fixture.definition.position);ResourceNode nearest=null;float closest=float.MaxValue;
            for(int i=0;i<world.Nodes.Count;i++){
                var node=world.Nodes[i];var up=CubeSphere.Decode(node.position);float denominator=Vector3.Dot(ray.direction,up);if(denominator>=-.0001f)continue;
                var relative=(hoverCentres[i]-observer).Float;float distance=Vector3.Dot(relative,up)/denominator;if(distance<0||distance>=closest||distance>fixture.View.farClipPlane)continue;
                var offset=ray.direction*distance-relative;var right=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var forward=Vector3.Cross(up,right);var dimensions=BuildingGrid.ResourceDimensions(node.kind,world.Catalog);
                if(Mathf.Abs(Vector3.Dot(offset,right))>dimensions.x*BuildingGrid.TileSize*.5f||Mathf.Abs(Vector3.Dot(offset,forward))>dimensions.y*BuildingGrid.TileSize*.5f)continue;
                nearest=node;closest=distance;
            }return nearest??world.Natural?.Pick(screen);
        }
        internal void UpdateResourceHover(Vector2 screen){
            if(resourceTooltip==null)return;
            if(fixture.IsFirstPerson||fixture.IsPiloting||ModalOpen||Blocks(screen)){resourceTooltip.style.display=DisplayStyle.None;return;}
            var node=ResourceAt(screen);if(node==null){resourceTooltip.style.display=DisplayStyle.None;return;}
            string name=node.kind==NodeKind.RareEarth?"Rare earth":node.kind==NodeKind.RichMetal?"Rich metal":node.kind.ToString();
            if(!string.IsNullOrEmpty(node.elementId)&&world.Civilisation!=null&&world.Civilisation.Catalogue.Resources.TryGetValue(node.elementId,out var element))name=element.name;
            string claim=node.remaining<=0?"DEPLETED":node.claimedBy<0?"UNCLAIMED":"CLAIMED";
            resourceTooltipText.text=$"{name.ToUpperInvariant()} DEPOSIT\nRemaining: {node.remaining:N0} / {node.capacity:N0}\nQuality: {node.quality:P0}  -  Yield: {node.rateMultiplier:0.00}x\n{claim}";
            resourceTooltip.style.display=DisplayStyle.Flex;
            var point=RuntimePanelUtils.ScreenToPanel(front.panel,new Vector2(screen.x,Screen.height-screen.y));
            resourceTooltip.style.left=Mathf.Clamp(point.x+18,8,Mathf.Max(8,front.contentRect.width-336));resourceTooltip.style.top=Mathf.Clamp(point.y+20,8,Mathf.Max(8,front.contentRect.height-140));
        }
        void RefreshResourceHover(){var mouse=Mouse.current;if(mouse==null||!Application.isFocused){if(resourceTooltip!=null)resourceTooltip.style.display=DisplayStyle.None;return;}UpdateResourceHover(mouse.position.ReadValue());}
    }
}
