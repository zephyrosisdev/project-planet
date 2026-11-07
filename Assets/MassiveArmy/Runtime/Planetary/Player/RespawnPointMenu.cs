using System;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    internal sealed class RespawnPointMenu:IDisposable {
        readonly PlanetDevelopment world;readonly VisualElement root;readonly TextField name;readonly Label status;long id;
        internal bool Open=>root.style.display==DisplayStyle.Flex;
        internal RespawnPointMenu(PlanetDevelopment world,VisualElement parent){this.world=world;root=new VisualElement{name="respawn-point-interaction"};root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");root.style.position=Position.Absolute;root.style.left=Length.Percent(25);root.style.top=Length.Percent(25);root.style.width=Length.Percent(50);root.style.paddingLeft=root.style.paddingRight=root.style.paddingTop=root.style.paddingBottom=20;root.style.backgroundColor=new Color(.025f,.04f,.06f,.98f);root.style.display=DisplayStyle.None;parent.Add(root);root.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());name=new TextField("Respawn point name"){maxLength=48};root.Add(name);status=new Label();status.style.whiteSpace=WhiteSpace.Normal;root.Add(status);root.Add(new Button(()=>Manage(false)){text="SAVE NAME"});root.Add(new Button(()=>Manage(true)){text="PICK UP"});root.Add(new Button(Close){text="CLOSE [ESC]"});}
        internal void Show(RespawnPoint point){id=point.id;name.value=point.name;root.style.display=DisplayStyle.Flex;root.BringToFront();Refresh();world.SetPersonalCursor(true);}
        internal void Refresh(){var point=world.IndustrialAuthority.World.RespawnPoint(id);if(point==null){Close();return;}status.text=world.RespawnLocationName(point)+"\n"+(point.RemainingSeconds(world.IndustrialAuthority.World.tick)>0?$"Ready in {Math.Ceiling(point.RemainingSeconds(world.IndustrialAuthority.World.tick))} seconds":"Ready")+"\nPicking up and replacing this point starts a fresh 2-minute cooldown.";}
        void Manage(bool pack){var point=world.IndustrialAuthority.World.RespawnPoint(id);if(point==null||!world.TryResolveRespawnPose(point.pose,out _,out _)){status.text="Respawn point is unavailable";return;}string error=world.IndustrialAuthority.ManageRespawnPoint(1,id,world.RespawnBodyPose(point.pose),pack?null:name.value);if(error!=null){status.text=error;return;}world.IndustrialAuthority.Save();Close();}
        internal void Close(){root.style.display=DisplayStyle.None;world.SetPersonalCursor(false);}
        public void Dispose()=>root.RemoveFromHierarchy();
    }
}

