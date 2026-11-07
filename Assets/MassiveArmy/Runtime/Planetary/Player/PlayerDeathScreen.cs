using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    internal sealed class PlayerDeathScreen:IDisposable {
        readonly PlanetDevelopment world;internal readonly VisualElement Root;internal readonly ScrollView Points;readonly Label message,selection;readonly Button respawn;
        readonly Dictionary<long,(Button button,Label timer)> cards=new Dictionary<long,(Button,Label)>();long selected;long signature=long.MinValue;double nextRefresh;
        internal PlayerDeathScreen(PlanetDevelopment world,VisualElement parent){
            this.world=world;Root=new VisualElement{name="player-death-screen"};Root.StretchToParentSize();Root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");Root.style.backgroundColor=new Color(.015f,.022f,.03f,.96f);Root.style.justifyContent=Justify.Center;Root.style.paddingLeft=Length.Percent(7);Root.style.paddingRight=Length.Percent(7);Root.style.display=DisplayStyle.None;parent.Add(Root);
            Root.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());Root.RegisterCallback<NavigationSubmitEvent>(e=>e.StopPropagation());
            var title=Text(Root,"YOU DIED",44);title.style.color=new Color(1,.38f,.26f);Text(Root,"CHOOSE WHERE TO RESPAWN",22);Text(Root,"Each sleeping bag or pod has its own 2-minute cooldown. Scroll the respawn points with the mouse wheel.",15);
            Points=new ScrollView(ScrollViewMode.Horizontal){name="respawn-point-scroll",horizontalScrollerVisibility=ScrollerVisibility.Auto,verticalScrollerVisibility=ScrollerVisibility.Hidden};Points.style.height=195;Points.style.marginTop=24;Points.style.marginBottom=20;Points.contentContainer.style.flexDirection=FlexDirection.Row;Points.contentContainer.style.flexWrap=Wrap.NoWrap;Root.Add(Points);
            Points.RegisterCallback<WheelEvent>(e=>{Scroll(e.delta);e.PreventDefault();e.StopImmediatePropagation();},TrickleDown.TrickleDown);
            selection=Text(Root,"",19);message=Text(Root,"",15);respawn=new Button(()=>{message.text=world.RequestPlayerRespawn(selected)??"Preparing respawn…";}){name="respawn-confirm",text="RESPAWN"};respawn.style.backgroundColor=new Color(.08f,.72f,.62f);respawn.style.color=Color.black;respawn.style.fontSize=18;respawn.style.height=52;respawn.style.width=300;respawn.style.marginTop=16;Root.Add(respawn);
        }
        static Label Text(VisualElement parent,string text,int size){var label=new Label(text);label.style.fontSize=size;label.style.whiteSpace=WhiteSpace.Normal;label.style.color=Color.white;label.style.marginBottom=8;parent.Add(label);return label;}
        internal void Scroll(Vector2 delta){float amount=Mathf.Abs(delta.x)>Mathf.Abs(delta.y)?delta.x:delta.y;Points.scrollOffset=new Vector2(Mathf.Clamp(Points.scrollOffset.x+amount*35,0,Mathf.Max(0,Points.horizontalScroller.highValue)),0);}
        void Card(long id,string name,string location){var button=new Button(()=>{selected=id;Refresh(true);}){name="respawn-point-"+id};button.style.backgroundColor=new Color(.065f,.095f,.12f);button.style.borderTopWidth=button.style.borderLeftWidth=button.style.borderRightWidth=1;button.style.borderTopColor=button.style.borderLeftColor=button.style.borderRightColor=new Color(.16f,.22f,.26f);button.style.width=240;button.style.height=145;button.style.flexShrink=0;button.style.marginRight=12;button.style.alignItems=Align.FlexStart;Text(button,name.ToUpperInvariant(),18);Text(button,location,13);var timer=Text(button,"",17);Points.Add(button);cards[id]=(button,timer);}
        internal void Show(){Root.style.display=DisplayStyle.Flex;Root.BringToFront();Refresh(true);}
        internal void Hide(){Root.style.display=DisplayStyle.None;message.text="";}
        internal void Feedback(string value)=>message.text=value;
        internal void Refresh(bool force=false){
            if(!world.LocalPlayerDead)return;if(!force&&Time.unscaledTimeAsDouble<nextRefresh)return;nextRefresh=Time.unscaledTimeAsDouble+.1;
            var data=world.IndustrialAuthority?.World;if(data==null)return;long next=17;unchecked{foreach(var point in data.respawnPoints)if(point.owner==1){next=next*1099511628211L^point.id;next=next*1099511628211L^(point.name?.GetHashCode()??0);}}
            if(next!=signature){signature=next;var offset=Points.scrollOffset;Points.Clear();cards.Clear();Card(0,"Surface spawn",world.definition.displayName+" · no cooldown");foreach(var point in data.respawnPoints)if(point.owner==1)Card(point.id,point.name,world.RespawnLocationName(point));Points.scrollOffset=offset;if(!cards.ContainsKey(selected))selected=0;}
            foreach(var pair in cards){var point=data.RespawnPoint(pair.Key);double remaining=point?.RemainingSeconds(data.tick)??0;string unavailable=world.RespawnAvailability(point);pair.Value.timer.text=unavailable??(remaining>0?$"COOLDOWN  {(long)Math.Ceiling(remaining)/60:00}:{Math.Ceiling(remaining)%60:00}":"READY");pair.Value.timer.style.color=unavailable!=null?Color.gray:remaining>0?new Color(1,.68f,.25f):new Color(.2f,.9f,.72f);pair.Value.button.style.borderBottomWidth=4;pair.Value.button.style.borderBottomColor=selected==pair.Key?new Color(.1f,.85f,.8f):Color.clear;pair.Value.button.SetEnabled(remaining<=0&&unavailable==null&&!world.RespawnPending);}
            var chosen=data.RespawnPoint(selected);selection.text=selected==0?"Surface spawn":chosen?.name??"Respawn point removed";respawn.SetEnabled(!world.RespawnPending&&(selected==0||chosen!=null&&chosen.owner==1&&chosen.readyAtTick<=data.tick&&world.RespawnAvailability(chosen)==null));respawn.text=world.RespawnPending?"PREPARING…":"RESPAWN";
        }
        public void Dispose()=>Root.RemoveFromHierarchy();
    }
}




