using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public static class TechnologyGraphLayout {
        public static Dictionary<string,Rect> Arrange(TechnologyDefinition[] technologies){
            var lookup=technologies.ToDictionary(x=>x.id);var depths=new Dictionary<string,int>();var visiting=new HashSet<string>();
            int Depth(string id){if(depths.TryGetValue(id,out int value))return value;if(!visiting.Add(id))throw new InvalidOperationException("Cyclic research dependency: "+id);int depth=0;foreach(string parent in lookup[id].prerequisites??Array.Empty<string>())if(lookup.ContainsKey(parent))depth=Math.Max(depth,Depth(parent)+1);visiting.Remove(id);return depths[id]=depth;}
            foreach(var t in technologies)Depth(t.id);
            var result=new Dictionary<string,Rect>();float lane=45;
            foreach(var branch in technologies.GroupBy(x=>string.IsNullOrWhiteSpace(x.branch)?"General":x.branch).OrderBy(x=>x.Key,StringComparer.Ordinal)){
                int rows=1;foreach(var column in branch.GroupBy(x=>depths[x.id])){int row=0;foreach(var tech in column.OrderBy(x=>(x.prerequisites??Array.Empty<string>()).FirstOrDefault()??"",StringComparer.Ordinal).ThenBy(x=>x.name,StringComparer.Ordinal))result.Add(tech.id,new Rect(24+column.Key*310,lane+row++*112,258,88));rows=Math.Max(rows,row);}lane+=rows*112+66;
            }return result;
        }
    }
    public sealed partial class PlanetRtsHud {
        float technologyZoom=1;Vector2 technologyPan=new Vector2(20,20);bool technologyViewInitialised;
        void BuildTechnologyPanel(VisualElement modal){
            var panel=new VisualElement{name="technology-panel"};panel.AddToClassList("panel");panel.style.width=Length.Percent(94);panel.style.height=Length.Percent(90);panel.style.maxWidth=StyleKeyword.None;panel.style.minHeight=300;modal.Add(panel);
            var toolbar=new VisualElement();toolbar.AddToClassList("row");panel.Add(toolbar);Label(toolbar,"RESEARCH · WHEEL TO ZOOM · DRAG BACKGROUND TO PAN","heading");Button(toolbar,"CLOSE",()=>{technologyOpen=false;ShowTechnology();});
            int team=Observer?Mathf.Clamp(inspectedFaction,0,world.Factions.Length-1):0;
            if(Observer){var factions=new VisualElement();factions.AddToClassList("row");panel.Add(factions);for(int i=0;i<world.Factions.Length;i++){int faction=i;Button(factions,PlanetCountries.Names[i],()=>{inspectedFaction=faction;ShowTechnology();},"technology-faction-"+i);}Label(panel,"SPECTATOR · VIEW ONLY","muted").name="technology-observer";}
            Label(panel,"AI research: "+(world.Factions[team].researching.Count>0?string.Join(", ",world.Factions[team].researching):"No research queued · requires a supplied, powered laboratory and available resources"),"muted");
            var viewport=new VisualElement{name="technology-viewport"};viewport.style.flexGrow=1;viewport.style.minHeight=200;viewport.style.overflow=Overflow.Hidden;viewport.style.backgroundColor=new Color(.015f,.025f,.04f,.95f);panel.Add(viewport);
            var canvas=new VisualElement{name="technology-canvas"};canvas.style.position=Position.Absolute;canvas.style.transformOrigin=new TransformOrigin(0,0,0);viewport.Add(canvas);
            var positions=TechnologyGraphLayout.Arrange(world.Catalog.technologies);float width=positions.Count==0?600:positions.Values.Max(x=>x.xMax)+40,height=positions.Count==0?400:positions.Values.Max(x=>x.yMax)+40;canvas.style.width=width;canvas.style.height=height;
            var drawing=new RtsUiDrawing{pickingMode=PickingMode.Ignore};drawing.StretchToParentSize();canvas.Add(drawing);
            foreach(var branch in world.Catalog.technologies.GroupBy(x=>string.IsNullOrWhiteSpace(x.branch)?"General":x.branch)){var title=Label(canvas,branch.Key.ToUpperInvariant(),"heading");title.style.position=Position.Absolute;title.style.left=24;title.style.top=branch.Min(x=>positions[x.id].yMin)-28;title.pickingMode=PickingMode.Ignore;}
            foreach(var tech in world.Catalog.technologies){var t=tech;var rect=positions[t.id];
                var card=Button(canvas,"",()=>{if(Observer)return;var lab=world.Buildings.FirstOrDefault(b=>b.team==0&&b.Complete&&b.powered&&!b.paused&&b.definition.id==t.requiredBuilding&&b.level>=t.requiredBuildingLevel);if(lab==null)world.Notice="Requires a powered "+t.requiredBuilding+" MK "+t.requiredBuildingLevel;else if(!world.Research(lab,t.id))world.Notice="Research requires prerequisites, resources and an available lab queue";RefreshTechnology();},"tech-"+t.id);
                card.AddToClassList("tech-node");card.style.position=Position.Absolute;card.style.left=rect.x;card.style.top=rect.y;card.style.width=rect.width;card.style.height=rect.height;card.style.whiteSpace=WhiteSpace.Normal;card.tooltip=t.description+"\nRequires: "+string.Join(", ",t.prerequisites);technologyCards.Add(t.id,card);
                foreach(string dependency in t.prerequisites)if(positions.TryGetValue(dependency,out var parent)){var a=new Vector2(parent.xMax,parent.center.y);var b=new Vector2(rect.xMin,rect.center.y);float middle=(a.x+b.x)*.5f;var colour=world.Factions[team].HasTech(dependency)?Color.cyan:Color.gray;drawing.Lines.Add((a,new Vector2(middle,a.y),colour));drawing.Lines.Add((new Vector2(middle,a.y),new Vector2(middle,b.y),colour));drawing.Lines.Add((new Vector2(middle,b.y),b,colour));}
            }
            void Apply(){canvas.transform.scale=new Vector3(technologyZoom,technologyZoom,1);canvas.transform.position=technologyPan;}
            void Zoom(float value,Vector2 pivot){float next=Mathf.Clamp(value,.03f,2.5f);technologyPan=pivot-(pivot-technologyPan)*(next/technologyZoom);technologyZoom=next;technologyViewInitialised=true;Apply();}
            void Fit(){technologyZoom=Mathf.Clamp(Mathf.Min(viewport.contentRect.width/width,viewport.contentRect.height/height)*.94f,.03f,1);technologyPan=new Vector2(12,12);technologyViewInitialised=true;Apply();}
            Button(toolbar,"−",()=>Zoom(technologyZoom/1.2f,viewport.contentRect.center));Button(toolbar,"+",()=>Zoom(technologyZoom*1.2f,viewport.contentRect.center));Button(toolbar,"FIT",Fit);
            viewport.RegisterCallback<GeometryChangedEvent>(_=>{if(!technologyViewInitialised)Fit();else Apply();});
            viewport.RegisterCallback<WheelEvent>(ev=>{Zoom(technologyZoom*Mathf.Exp(-ev.delta.y*.09f),viewport.WorldToLocal(ev.mousePosition));ev.StopImmediatePropagation();});
            bool dragging=false;Vector2 previous=default;int pointer=-1;
            viewport.RegisterCallback<PointerDownEvent>(ev=>{if(ev.button!=2&&ev.button!=1&&ev.target!=viewport&&ev.target!=canvas)return;dragging=true;previous=ev.position;pointer=ev.pointerId;viewport.CapturePointer(pointer);ev.StopPropagation();});
            viewport.RegisterCallback<PointerMoveEvent>(ev=>{if(!dragging)return;Vector2 current=ev.position;technologyPan+=current-previous;previous=current;technologyViewInitialised=true;Apply();ev.StopPropagation();});
            viewport.RegisterCallback<PointerUpEvent>(ev=>{if(!dragging)return;dragging=false;viewport.ReleasePointer(pointer);ev.StopPropagation();});viewport.RegisterCallback<PointerCaptureOutEvent>(_=>dragging=false);
            Apply();RefreshTechnology();
        }
    }
}
