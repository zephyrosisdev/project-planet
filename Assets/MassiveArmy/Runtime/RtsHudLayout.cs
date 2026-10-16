using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassiveArmy
{
    // Shared retained HUD for both terrain backends. Gameplay actions are supplied by the session.
    public sealed class RtsHudLayout
    {
        public readonly Dictionary<string,Label> Labels=new Dictionary<string,Label>();
        public readonly Dictionary<string,Button> Actions=new Dictionary<string,Button>();
        readonly VisualElement root;readonly Action<string> dispatch;readonly RtsUiArt art;
        readonly Texture2D glow;
        public RtsHudLayout(VisualElement root,Action<string> dispatch)
        {
            this.root=root;this.dispatch=dispatch;art=Resources.Load<RtsUiArt>("MassiveArmy/UI/FrontArt");glow=Resources.Load<Texture2D>("MassiveArmy/UI/ControlHighlight");
            foreach(string id in new[]{"top","bottom"}){root.Q(id).Insert(0,new RtsUiFrame());}
            var brand=Host("brand");Icon(brand,"emblem","emblem");var title=Box(brand);Label(title,"SYNTHETIC FRONT","brand-title");Label(title,"","card-meta","faction");
            var resources=Host("resources");var template=Resources.Load<VisualTreeAsset>("MassiveArmy/UI/ResourceCell");
            string[] ids={"materials","fuel","rare","isotopes","research"},names={"MATERIALS","FUEL","RARE","ISOTOPES","RESEARCH"};
            for(int i=0;i<5;i++){var cell=template.CloneTree();cell.AddToClassList("resource");resources.Add(cell);cell.Q<Image>("icon").vectorImage=Resources.Load<VectorImage>("MassiveArmy/UI/Icons/"+ids[i]);cell.Q<Label>("title").text=names[i];Labels["resource-"+i]=cell.Q<Label>("value");}
            var actions=Host("top-actions");Label(actions,"","muted","fps");var row=Box(actions,"row");Button(row,"MAP [M]","map");Button(row,"PAUSE","pause-game");Button(row,"MENU","menu");
            Label(Host("grid-status"),"","notice","power");var quick=Host("quick-actions");Button(quick,"CONSTRUCT [B]","construct","construct","grow");Button(quick,"TECH TREE [T]","tech","research","grow");Button(quick,"SUPPLY","supply-toggle","supply","grow");Button(quick,"DETAILS","context-reopen","units");
            var notices=Host("notifications");foreach(string id in new[]{"notice","placement","swarm","outcome"})Label(notices,"","notice",id);
            var selected=Host("selection");Label(selected,"","heading","selected");Label(selected,"SHIFT add · CTRL remove\nRight-drag formation","muted","selection-help");
            var commands=Host("commands");Button(commands,"MOVE [RMB]","move","move");Button(commands,"ATTACK-MOVE","attack-move","target");Button(commands,"ATTACK","attack","attack");Button(commands,"STOP","stop","stop");
            var help=Host("camera-help");Label(help,"ARROWS / MIDDLE DRAG\nWHEEL ZOOM\nN: TEST NUKE","muted","camera-help-text");Label(help,"","muted","territory");
            var scout=Host("scout");scout.Insert(0,new RtsUiFrame());Label(scout,"RECON & SUPPORT","heading");row=Box(scout,"row");Button(row,"LOAD","load","transport","grow");Button(row,"UNLOAD","unload","transport","grow");Button(scout,"CARRIER FIGHTER","carrier","aircraft");
            var pattern=new DropdownField("Search",new List<string>{"Spiral","Grid","Sector","Perimeter","Route","Auto Explore","Observe","Follow Army"},5){name="search-pattern"};scout.Add(pattern);Button(scout,"SCOUT ORDER","scout","target");
            var developer=Host("developer");developer.Insert(0,new RtsUiFrame());Label(developer,"GPU LAB / F1","heading");Label(developer,"","body","developer-stats");var scroll=new ScrollView();developer.Add(scroll);foreach(string marker in BattleTelemetry.Markers)Label(scroll,"","muted","marker-"+marker);Button(developer,"BENCHMARK 25 s","benchmark","research");Button(developer,"SUBSYSTEM SWITCHES","settings","settings");Label(developer,"","muted","benchmark-report");
        }
        VisualElement Host(string id){var host=root.Q(id);host.Clear();return host;}
        static VisualElement Box(VisualElement parent,string style=""){var box=new VisualElement();foreach(string c in style.Split(' '))if(c.Length>0)box.AddToClassList(c);parent.Add(box);return box;}
        void Label(VisualElement parent,string text,string style,string key=null){var label=new Label(text){pickingMode=PickingMode.Ignore};label.AddToClassList(style);parent.Add(label);if(key!=null)Labels[key]=label;}
        void Icon(VisualElement parent,string id,string style){var image=new Image{vectorImage=Resources.Load<VectorImage>("MassiveArmy/UI/Icons/"+id),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};image.AddToClassList(style);parent.Add(image);}
        void Button(VisualElement parent,string text,string id,string icon=null,string style=""){
            Action action=()=>dispatch(id);var button=new Button(action){name=id,userData=action};button.AddToClassList("action");foreach(string c in style.Split(' '))if(c.Length>0)button.AddToClassList(c);var image=new Image{image=glow,pickingMode=PickingMode.Ignore};image.AddToClassList("control-glow");button.Add(image);if(icon!=null)Icon(button,icon,"action-icon");Label(button,text,"card-name");parent.Add(button);Actions[id]=button;
        }
        public void Value(string key,string value){if(Labels.TryGetValue(key,out var label))label.text=value;}
    }
}
