using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy
{
    /// <summary>The original category catalogue and virtualized rows, shared by both world backends.</summary>
    public sealed class RtsConstructionPanel
    {
        readonly VisualElement host;readonly RtsUiArt artwork;readonly VisualTreeAsset template;readonly Texture2D glow;
        readonly Action<BuildingDefinition> place;readonly Action<int> choose;readonly Action close;
        readonly Dictionary<int,Button> categories=new Dictionary<int,Button>();
        public ListView List {get;private set;}
        public IReadOnlyDictionary<int,Button> Categories=>categories;
        RtsCatalog catalog;RtsFaction faction;int category=-1;string availability="";
        readonly bool readOnly;
        public RtsConstructionPanel(VisualElement host,RtsUiArt artwork,VisualTreeAsset template,Texture2D glow,Action<BuildingDefinition> place,Action<int> choose,Action close,bool readOnly=false)
        {
            this.host=host;this.artwork=artwork;this.template=template;this.glow=glow;this.place=place;this.choose=choose;this.close=close;this.readOnly=readOnly;Build();
        }
        static Label Label(VisualElement parent,string text,string css){var label=new Label(text);foreach(string name in css.Split(' '))label.AddToClassList(name);label.pickingMode=PickingMode.Ignore;parent.Add(label);return label;}
        static VisualElement Box(VisualElement parent,string css){var box=new VisualElement();box.AddToClassList(css);parent.Add(box);return box;}
        void Build(){host.Clear();host.Insert(0,new RtsUiFrame());var heading=Box(host,"row");heading.style.flexShrink=0;var title=Label(heading,"CONSTRUCTION\nNETWORK","heading grow");title.style.minWidth=0;title.style.flexShrink=1;var cancel=new Button(){text="×",name="×",userData=close,tooltip="Close construction [B / Escape]"};cancel.AddToClassList("action");cancel.AddToClassList("close");cancel.style.flexShrink=0;cancel.RegisterCallback<ClickEvent>(e=>{close();e.StopPropagation();});heading.Add(cancel);
            var row=Box(host,"wrap");foreach(BuildingCategory c in Enum.GetValues(typeof(BuildingCategory))){int index=(int)c;var button=new Button(()=>choose(index)){name="category-"+index};button.AddToClassList("action");button.AddToClassList("category");var icon=new Image{vectorImage=Resources.Load<VectorImage>("MassiveArmy/UI/Icons/category-"+index),pickingMode=PickingMode.Ignore};icon.AddToClassList("action-icon");button.Add(icon);Label(button,c.ToString().ToUpperInvariant(),"card-name");row.Add(button);categories[index]=button;}
            List=new ListView{fixedItemHeight=94,selectionType=SelectionType.None};host.Add(List);
            List.makeItem=()=>{var holder=template.CloneTree();var button=holder.Q<Button>();button.clicked+=()=>{if(!readOnly&&button.userData is BuildingDefinition definition)place(definition);};holder.Q<Image>("highlight").image=glow;holder.Q<Image>("portrait").scaleMode=ScaleMode.ScaleToFit;return holder;};
            List.bindItem=(holder,index)=>{var d=(BuildingDefinition)List.itemsSource[index];var button=holder.Q<Button>();button.name="build-"+d.id;button.userData=d;bool unlocked=faction.HasTech(d.requiredTech)&&faction.tier>=d.tier;button.SetEnabled(unlocked&&!readOnly);holder.Q<Image>("portrait").image=artwork.Find("building",d.id)?.portrait;holder.Q<Label>("name").text=d.name.ToUpperInvariant();holder.Q<Label>("cost").text=d.cost+" · "+d.time+"s";holder.Q<Label>("lock").text=readOnly?"SPECTATOR · VIEW ONLY":unlocked?"":"LOCKED · T"+d.tier+" · "+(string.IsNullOrEmpty(d.requiredTech)?"Upgrade command era":catalog.Technologies[d.requiredTech].name);};
        }
        public void Refresh(RtsCatalog catalog,RtsFaction faction,int selected){this.catalog=catalog;this.faction=faction;string key=faction.tier+":"+string.Join("",catalog.technologies.Select(t=>faction.HasTech(t.id)?"1":"0"));if(category==selected&&availability==key)return;
            foreach(var pair in categories)pair.Value.EnableInClassList("active",pair.Key==selected);bool changed=category!=selected;category=selected;availability=key;
            if(changed||List.itemsSource==null){List.userData=selected;List.itemsSource=catalog.buildings.Where(d=>(int)d.category==selected).ToList();}else List.RefreshItems();
        }
    }
}
