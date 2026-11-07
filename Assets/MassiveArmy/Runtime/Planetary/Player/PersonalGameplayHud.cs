using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public sealed partial class PersonalGameplayHud:IDisposable {
        readonly PlanetDevelopment world;readonly VisualElement root,panel,content;readonly Label vitals,status,title,drag;readonly ProgressBar healthBar,hungerBar,thirstBar;readonly Button[] slots=new Button[10];
        readonly PlanetNavigationCompass navigationCompass;
        string mode="inventory",shop,dragKey;RtsBuilding venue;ColonyState colony;ResidentState person;double nextRefresh;
        public bool TextFocused {get{var element=root.panel?.focusController?.focusedElement as VisualElement;while(element!=null){if(element is TextField||element is IntegerField||element.ClassListContains("unity-base-text-field"))return true;element=element.parent;}return false;}}
        public bool Open=>panel.style.display==DisplayStyle.Flex;internal VisualElement InventorySurface=>panel;
        public PersonalGameplayHud(PlanetDevelopment world,VisualElement parent){
            this.world=world;root=new VisualElement{name="personal-gameplay",pickingMode=PickingMode.Ignore};root.StretchToParentSize();parent.Add(root);navigationCompass=new PlanetNavigationCompass(world);root.Add(navigationCompass);
            root.RegisterCallback<NavigationMoveEvent>(e=>{if(!Open){e.PreventDefault();e.StopImmediatePropagation();}},TrickleDown.TrickleDown);
            var meters=new VisualElement();meters.style.position=Position.Absolute;meters.style.left=18;meters.style.bottom=90;meters.style.width=380;root.Add(meters);vitals=new Label();vitals.style.color=Color.white;vitals.style.backgroundColor=new Color(.015f,.025f,.04f,.9f);vitals.style.paddingLeft=10;vitals.style.paddingRight=10;meters.Add(vitals);
            healthBar=new ProgressBar{title="Health",lowValue=0,highValue=100};hungerBar=new ProgressBar{title="Hunger",lowValue=0,highValue=100};thirstBar=new ProgressBar{title="Thirst",lowValue=0,highValue=100};foreach(var meter in new[]{healthBar,hungerBar,thirstBar}){meter.style.height=17;meters.Add(meter);}
            var crosshair=new Label("+"){pickingMode=PickingMode.Ignore};crosshair.style.position=Position.Absolute;crosshair.style.left=Length.Percent(50);crosshair.style.top=Length.Percent(50);crosshair.style.color=Color.white;root.Add(crosshair);
            var bar=new VisualElement();bar.style.position=Position.Absolute;bar.style.left=Length.Percent(15);bar.style.width=Length.Percent(70);bar.style.bottom=18;bar.style.flexDirection=FlexDirection.Row;bar.style.justifyContent=Justify.Center;root.Add(bar);
            for(int i=0;i<slots.Length;i++){int slot=i;var button=Action(bar,"",()=>world.PersonalInventory.EquippedSlot=slot);button.focusable=false;button.tabIndex=-1;button.name="personal-slot-"+slot;button.userData=slot;button.style.width=95;button.style.height=60;button.style.whiteSpace=WhiteSpace.Normal;slots[i]=button;}
            panel=new VisualElement{name="personal-inventory"};panel.AddToClassList("panel");panel.style.position=Position.Absolute;panel.style.left=Length.Percent(8);panel.style.top=Length.Percent(9);panel.style.width=Length.Percent(84);panel.style.height=Length.Percent(76);panel.style.backgroundColor=new Color(.02f,.035f,.055f,.98f);panel.style.display=DisplayStyle.None;root.Add(panel);
            title=Text(panel,"","heading");var tabs=new VisualElement();tabs.style.flexDirection=FlexDirection.Row;panel.Add(tabs);Action(tabs,"INVENTORY [I]",ShowInventory);if(world.IsSurvival){Action(tabs,"CRAFTING",world.ShowIndustrialCrafting);Action(tabs,"EMPIRE [O]",ShowOrganization);}else Action(tabs,"ORGANISATION [O]",ShowOrganization);Action(tabs,"CLOSE",Close);if(world.IsSurvival)Action(tabs,"MAIN MENU",world.ReturnToMenu);
            status=Text(panel,"Drag an inventory item onto a numbered slot. Number keys equip; hold LMB to use.");content=new ScrollView();content.style.flexGrow=1;panel.Add(content);
            drag=new Label{pickingMode=PickingMode.Ignore};drag.style.position=Position.Absolute;drag.style.display=DisplayStyle.None;drag.style.color=Color.yellow;root.Add(drag);
            world.AttachIndustryUi(parent);world.AttachInventoryUi(parent);if(world.EquipmentInventory)world.BindInventoryHotbar(slots,panel);world.AttachDeathUi(parent);
            root.RegisterCallback<PointerMoveEvent>(e=>{if(dragKey==null)return;drag.style.left=e.position.x+12;drag.style.top=e.position.y+12;});
            root.RegisterCallback<PointerUpEvent>(e=>{if(dragKey==null)return;var target=root.panel.Pick(e.position);while(target!=null){if(target.userData is int slot&&slot>=0&&slot<10){world.PersonalInventory.Assign(slot,dragKey);break;}target=target.parent;}dragKey=null;drag.style.display=DisplayStyle.None;root.ReleasePointer(e.pointerId);Refresh(0,1);});
        }
        static Label Text(VisualElement parent,string value,string style="body"){var label=new Label(value);label.AddToClassList(style);label.style.whiteSpace=WhiteSpace.Normal;label.style.color=Color.white;parent.Add(label);return label;}
        static Button Action(VisualElement parent,string value,Action action){var button=new Button(action){text=value};button.AddToClassList("action");button.style.color=Color.white;button.style.backgroundColor=new Color(.15f,.2f,.23f);parent.Add(button);return button;}
        public void SetVisible(bool value){root.style.display=value?DisplayStyle.Flex:DisplayStyle.None;}
        public void Close(){panel.style.display=DisplayStyle.None;dragKey=null;drag.style.display=DisplayStyle.None;world.SetPersonalCursor(false);}
        void Show(string value){world.CloseShipyardMenu();world.CloseGroundLoot();world.CloseRespawnPointMenu();world.CancelRespawnPlacement();world.CloseIndustryMenu();mode=value;panel.style.display=DisplayStyle.Flex;world.SetPersonalCursor(true);RefreshPanel();}
        public void ShowInventory()=>Show("inventory");public void ShowOrganization()=>Show("organization");
        public void ShowVenue(RtsBuilding building,ColonyState state,string kind){venue=building;colony=state;shop=kind;person=null;Show("venue");}
        public void ShowPerson(ColonyState state,ResidentState resident){person=resident;colony=state;venue=null;Show("person");}
        public void Refresh(float progress,float duration){
            navigationCompass.Refresh();var inventory=world.PersonalInventory;var v=world.PersonalVitals;healthBar.value=v.Health;hungerBar.value=v.Hunger;thirstBar.value=v.Thirst;vitals.text=$"HEALTH {v.Health:0}%   HUNGER {v.Hunger:0}%   THIRST {v.Thirst:0}%   O2 {v.Oxygen:0}%\n"+(world.IsSurvival?"I inventory / F interact / O empire\n"+world.TribeHudHint():$"Credits {inventory.Credits:0} / Wanted {inventory.WantedHeat:0} / I inventory / E talk/shop / O organisation")+(progress>0?$"\nUSING ITEM {Mathf.Clamp01(progress/Mathf.Max(.1f,duration))*100:0}%":"");
            for(int i=0;i<10;i++){string key=inventory.Hotbar[i];var item=world.PersonalItems.Find(key);int count=inventory.UnifiedInventory?(int)(inventory.Unified.AtSlot(i)?.count??0):key!=null&&inventory.Items.TryGetValue(key,out int value)?value:0;slots[i].text=(i==9?"0":(i+1).ToString())+" · "+(item?.name??"Empty")+(count>1?" ×"+count:"");slots[i].style.borderBottomWidth=3;slots[i].style.borderBottomColor=inventory.EquippedSlot==i?Color.cyan:Color.gray;}
            if(world.IsSurvival&&OrganizationOpen&&governmentRevision!=(Gov?.revision??-1)&&!TextFocused)RefreshPanel();
            if(Open&&Time.unscaledTime>=nextRefresh){nextRefresh=Time.unscaledTime+1;status.text=$"{world.Base?.Notice??world.SurvivalNotice}\nDrag items to slots; number keys equip; LMB uses the equipped item.";}
        }
        public void RefreshPanel(){
            if(!Open)return;content.Clear();var inventory=world.PersonalInventory;
            if(mode=="inventory"){if(world.EquipmentInventory){title.text="INVENTORY";world.BuildBackpackSlots(content);return;}
                title.text="INVENTORY";foreach(var entry in inventory.Items){string key=entry.Key;var item=world.PersonalItems.Find(key);var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;content.Add(row);var button=Action(row,(item?.name??key)+" ×"+entry.Value,()=>{});button.style.width=310;button.tooltip=item?.description??key;
                    button.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;dragKey=key;drag.text=item?.name??key;drag.style.display=DisplayStyle.Flex;root.CapturePointer(e.pointerId);e.StopPropagation();});
                    if(inventory.Provenance.TryGetValue(key,out var history))Text(row,(history.illegal?"RESTRICTED · ":"")+"Seller: "+history.seller+" · serial "+key,"muted");
                }if(world.IsSurvival)world.AppendIndustrialInventory(content);return;
            }
            if(mode=="organization"&&world.IsSurvival){BuildGovernment();return;}
            if(mode=="organization"){
                title.text=inventory.OrganizationName+" · "+inventory.OrganizationType;var name=new TextField("Name"){value=inventory.OrganizationName};content.Add(name);var type=new DropdownField("Organisation",new System.Collections.Generic.List<string>{"Gang","Business","Security Company","Club"},0);content.Add(type);Action(content,"FORM / RENAME",()=>world.FormPersonalOrganization(name.value,type.value));
                Text(content,"Up to four guards. Recruit adults at pubs or by talking to them. Bring 50 credits and a spare firearm for each guard.");
                foreach(var c in inventory.Companions){var member=c;Text(content,member.name+" · "+member.order,"heading");var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;content.Add(row);Action(row,"FOLLOW / CALL",()=>{world.CompanionCommand(member,CompanionOrder.Follow);RefreshPanel();});Action(row,"WAIT HERE",()=>{world.CompanionCommand(member,CompanionOrder.Wait);RefreshPanel();});Action(row,"GUARD HERE",()=>{world.CompanionCommand(member,CompanionOrder.Guard);RefreshPanel();});}return;
            }
            if(mode=="person"){
                title.text=person.name;Text(content,"Talk · "+person.background+" · "+person.relationship);Action(content,"ASK ABOUT THE NEIGHBOURHOOD",()=>{world.Base.Notice=colony.crime.reportedCrimes>0?"People are talking about "+colony.crime.reportedCrimes+" reported crimes.":"It has been quiet around here.";});
                if(person.age>=18&&!person.recruited)Action(content,"RECRUIT · 50 CREDITS + SPARE FIREARM",()=>world.RecruitPersonal(colony,person));if(person.crime.weapons>0&&(person.crime.gang!=0||person.crime.activity!=CrimeActivity.None))Action(content,"BUY A USED GUN · 70 CREDITS · UNCERTAIN HISTORY",()=>world.BuyStreetWeapon(colony,person));return;
            }
            title.text=venue.definition.name;Text(content,"Purchases consume the shop's supplied inventory. Pawn and street weapons can have an uncertain history.","muted");
            foreach(var item in world.PersonalItems.items)if(world.Offered(item,shop)){var definition=item;int stock=world.ShopStock(venue,item);double price=item.price*(shop=="blackmarket"?.65:shop=="pawn"?.8:1);var button=Action(content,item.name+" · "+price.ToString("0")+" credits · stock "+stock,()=>world.BuyPersonalItem(venue,colony,shop,definition));button.SetEnabled(stock>0&&inventory.Credits>=price&&venue.powered&&!venue.paused&&(shop!="pub"||CivilisationRuntime.PubOpen(world.Base,venue))&&(colony.civilianWeaponsLegal||shop!="gun"||item.kind!=PersonalItemKind.Firearm));}
            if(shop=="pub"){Text(content,CivilisationRuntime.PubOpen(world.Base,venue)?"OPEN · 11:00–23:00":"CLOSED · 11:00–23:00 · requires employed staff","heading");var bartender=CivilisationRuntime.PubBartender(colony,venue);if(bartender!=null)Action(content,"TALK TO BARTENDER "+bartender.name,()=>ShowPerson(colony,bartender));Text(content,"UPSTAIRS APARTMENTS · 5 CREDITS / DAY","heading");var installation=world.Base.Civilisation.Installation(world.Base,venue.persistentId);for(int i=1;i<=2;i++){int room=i;bool yours=world.PlayerHasPubLease(venue,room);int npcRoom=world.PlayerHasPubLease(venue,1)?2:1;bool npc=room==npcRoom&&colony.households.Exists(h=>h.home==venue.persistentId);Text(content,"Apartment "+room+": "+(yours?"Rented by you until "+world.Base.Civilisation.CivicDate(installation.playerRentUntil):npc?"Local household rental":"Available"));Action(content,yours?"EXTEND APARTMENT "+room:"RENT APARTMENT "+room,()=>world.RentPubApartment(venue,room)).SetEnabled(!npc&&CivilisationRuntime.PubOpen(world.Base,venue));}}
        }
        public void Dispose(){if(root.panel!=null)root.RemoveFromHierarchy();}
    }
}
