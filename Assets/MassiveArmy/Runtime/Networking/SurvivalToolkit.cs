using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassiveArmy.Networking {
    // Retained controls for the existing validated command presenters. Elements are
    // reused across refreshes; click receipts include row context so changing or
    // reordered inventories cannot redirect a click to another item.
    internal sealed class SurvivalToolkit:IDisposable {
        sealed class Scope {internal VisualElement element;internal int next;internal string context="";}
        readonly Stack<Scope> scopes=new Stack<Scope>();
        readonly HashSet<string> clicks=new HashSet<string>();
        VisualElement root;string lastBox="";bool enabled=true;
        internal float ViewWidth=>root.resolvedStyle.width>0?root.resolvedStyle.width:1920;
        internal float ViewHeight=>root.resolvedStyle.height>0?root.resolvedStyle.height:1080;
        internal bool Enabled {get=>enabled;set=>enabled=value;}
        internal SurvivalToolkit(VisualElement root){this.root=root;root.Clear();root.pickingMode=PickingMode.Ignore;}
        internal void Begin(){scopes.Clear();scopes.Push(new Scope{element=root});enabled=true;}
        void Trim(){var s=scopes.Peek();while(s.element.childCount>s.next)s.element.RemoveAt(s.element.childCount-1);}
        internal void End(){while(scopes.Count>1){Trim();scopes.Pop();}Trim();clicks.Clear();}
        T Next<T>(Func<T> create) where T:VisualElement {
            var s=scopes.Peek();int index=s.next++;T e=null;
            if(index<s.element.childCount){e=s.element[index] as T;if(e!=null&&typeof(T)==typeof(VisualElement)&&e.GetType()!=typeof(VisualElement))e=null;if(e==null)s.element.RemoveAt(index);}
            if(e==null){e=create();s.element.Insert(index,e);}e.SetEnabled(enabled);return e;
        }
        static void Panel(VisualElement e){e.style.backgroundColor=new Color(.025f,.055f,.085f,.96f);e.style.paddingLeft=14;e.style.paddingRight=14;e.style.paddingTop=12;e.style.paddingBottom=12;e.style.borderTopWidth=e.style.borderBottomWidth=1;e.style.borderTopColor=e.style.borderBottomColor=new Color(.05f,.6f,.75f,.7f);}
        static void At(VisualElement e,Rect r){e.style.position=Position.Absolute;e.style.left=r.x;e.style.top=r.y;e.style.width=Mathf.Max(120,r.width);e.style.height=Mathf.Max(24,r.height);}
        internal void Box(Rect rect,string text){lastBox=text;if(string.IsNullOrEmpty(text))return;var label=Next(()=>new Label());label.text=text;label.pickingMode=PickingMode.Ignore;At(label,rect);Panel(label);label.style.color=Color.white;label.style.whiteSpace=WhiteSpace.Normal;label.style.fontSize=15;}
        internal void Label(Rect rect,string text){var label=Next(()=>new Label());label.text=text;label.pickingMode=PickingMode.Ignore;At(label,rect);label.style.whiteSpace=WhiteSpace.Normal;label.style.color=Color.white;label.style.fontSize=14;}
        internal void BeginArea(Rect rect){var e=Next(()=>new VisualElement());At(e,rect);Panel(e);e.pickingMode=PickingMode.Position;scopes.Push(new Scope{element=e,context=lastBox});}
        internal void EndArea(){Trim();scopes.Pop();}
        internal void BeginHorizontal(){var e=Next(()=>new VisualElement());e.style.flexDirection=FlexDirection.Row;e.style.flexWrap=Wrap.Wrap;e.style.alignItems=Align.Center;scopes.Push(new Scope{element=e,context=scopes.Peek().context});}
        internal void EndHorizontal(){Trim();scopes.Pop();}
        internal Vector2 BeginScrollView(Vector2 unused){var e=Next(()=>new ScrollView());e.style.flexGrow=1;e.style.minHeight=0;scopes.Push(new Scope{element=e.contentContainer,context=scopes.Peek().context});return e.scrollOffset;}
        internal void EndScrollView(){Trim();scopes.Pop();}
        internal void Space(float size){var e=Next(()=>new VisualElement());e.style.height=size;e.pickingMode=PickingMode.Ignore;}
        internal void Label(string text){var e=Next(()=>new Label());e.text=text;e.style.whiteSpace=WhiteSpace.Normal;e.style.color=new Color(.8f,.9f,.96f);e.style.fontSize=15;e.style.flexGrow=1;e.style.marginTop=5;e.style.marginBottom=5;e.pickingMode=PickingMode.Ignore;scopes.Peek().context=text;}
        internal float Width(float width)=>width;
        internal bool Button(string text,float width=0){
            var scope=scopes.Peek();string path="";foreach(var s in scopes)path+="/"+s.next+":"+s.context;
            string key=path+"/"+text;var e=Next(()=>new Button());e.text=text;e.userData=key;e.style.minHeight=30;e.style.marginBottom=5;e.style.marginRight=5;e.style.flexShrink=0;e.style.fontSize=14;
            if(width>0)e.style.width=width;else e.style.width=StyleKeyword.Auto;
            if(e.name!="command"){e.name="command";e.clicked+=()=>{if(e.enabledInHierarchy)clicks.Add((string)e.userData);};}
            return enabled&&clicks.Remove(key);
        }
        internal int SelectionGrid(int selected,string[] labels,int columns){BeginHorizontal();for(int i=0;i<labels.Length;i++)if(Button((selected==i?"✓ ":"")+labels[i],50))selected=i;EndHorizontal();return selected;}
        sealed class Edit<T>{internal bool pending;internal T value;}
        internal float HorizontalSlider(float value,float low,float high){var e=Next(()=>{var slider=new Slider(low,high);var edit=new Edit<float>();slider.userData=edit;slider.RegisterValueChangedCallback(change=>{edit.pending=true;edit.value=change.newValue;});return slider;});var state=(Edit<float>)e.userData;if(state.pending){state.pending=false;return state.value;}e.SetValueWithoutNotify(value);return value;}
        internal string TextField(string value,int maximum){var e=Next(()=>{var field=new TextField{maxLength=maximum};var edit=new Edit<string>();field.userData=edit;field.RegisterValueChangedCallback(change=>{edit.pending=true;edit.value=change.newValue;});return field;});var state=(Edit<string>)e.userData;e.maxLength=maximum;if(state.pending){state.pending=false;return state.value;}e.SetValueWithoutNotify(value);return value;}
        public void Dispose(){root?.Clear();root=null;clicks.Clear();scopes.Clear();}
    }
    public sealed partial class SurvivalNetworkSession {
        internal bool NetworkToolkitReady=>networkToolkit?.Root!=null;
        double nextToolkitRefresh;int toolkitMode=-1;
        RtsUiController networkToolkit;SurvivalToolkit networkUi;
        void RefreshNetworkToolkit(){
            if(HeadlessProbeClient||!Online||latest?.self==null)return;
            if(networkToolkit==null){networkToolkit=new RtsUiController(transform,root=>networkUi=new SurvivalToolkit(root),DrawNetworkToolkit,()=>Online);networkToolkit.Attach();}
            int mode=(inventoryOpen?1:0)|(industryOpen?2:0)|(networkMenu?4:0)|(ChatInputCaptured?8:0)|(latest.self.health<=0?16:0);
            if(ClientBaseline||mode!=toolkitMode||Time.unscaledTimeAsDouble>=nextToolkitRefresh){toolkitMode=mode;nextToolkitRefresh=Time.unscaledTimeAsDouble+.1;networkToolkit.Tick();}TickOnlineInventory();
        }
        void DisposeNetworkToolkit(){DisposeOnlineInventory();networkUi?.Dispose();networkUi=null;networkToolkit?.Dispose();networkToolkit=null;}
    }
}
