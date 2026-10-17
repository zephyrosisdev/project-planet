using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsHud {
        void BuildDistrictToggle(VisualElement toolbar){
            var key=new VisualElement{name="district-legend"};key.AddToClassList("panel");key.style.position=Position.Absolute;key.style.left=400;key.style.top=225;key.style.width=200;key.style.display=DisplayStyle.None;front.Q("hud").Add(key);ui.Block(key);Label(key,"DISTRICT KEY","heading");
            foreach(SettlementDistrict district in Enum.GetValues(typeof(SettlementDistrict))){var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;key.Add(row);var swatch=new VisualElement();var colour=SettlementDistricts.Colour(district);colour.a=1;swatch.style.backgroundColor=colour;swatch.style.width=12;swatch.style.height=12;swatch.style.marginRight=8;row.Add(swatch);Label(row,district.ToString(),"body");}
            Button(toolbar,"DISTRICTS",()=>{world.ToggleDistrictOverlay();key.style.display=world.DistrictOverlay?DisplayStyle.Flex:DisplayStyle.None;},"districts-toggle");
        }
    }
}
