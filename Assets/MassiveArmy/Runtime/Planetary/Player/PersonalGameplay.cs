using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        readonly System.Collections.Generic.Dictionary<string,Action<PlanetDevelopment,string,PersonalItemDefinition>> personalActions=new System.Collections.Generic.Dictionary<string,Action<PlanetDevelopment,string,PersonalItemDefinition>>();
        public PlayerInventory PlayerItems=>player?.Inventory;public PlayerEnvironmentState PlayerStatus=>player?.Environment;
        public void RegisterPersonalItemAction(string id,Action<PlanetDevelopment,string,PersonalItemDefinition> action){if(string.IsNullOrWhiteSpace(id)||action==null)throw new ArgumentException("Item action needs an id and handler");personalActions[id]=action;}
        PersonalItemCatalogue personalItems;PersonalGameplayHud personalHud;RtsUiController survivalUi;
        RtsBuilding personalVenue;ResidentState personalSpeaker;ColonyState personalColony;
        double nextVenue,nextCompanion,nextSearch,nextUse;float usingItem;string usingKey,heldKey;GameObject heldItem;Material heldMaterial;bool heldOwnMaterial;
        internal bool PersonalMenuOpen=>ShipyardOpen||LocalPlayerDead||GroundLootOpen||RespawnPointMenuOpen||personalHud?.Open==true||IndustrialMenuOpen;
        internal PlayerInventory PersonalInventory=>player.Inventory;
        internal PlayerEnvironmentState PersonalVitals=>player.Environment;
        internal PersonalItemCatalogue PersonalItems=>personalItems??=PersonalItemCatalogue.Load();
        string PersonalPrompt=>IsFirstPerson&&personalVenue!=null?"E · "+personalVenue.definition.name+" · I inventory · O organisation":"";
        void UpdatePersonalGameplay(){
            if(player==null)return;
            if(IsSurvival&&survivalUi==null){survivalUi=new RtsUiController(transform,root=>{root.Clear();personalHud?.Dispose();personalHud=new PersonalGameplayHud(this,root);},()=>{},()=>IsFirstPerson);survivalUi.Attach();}
            if(!IsSurvival){if(hud==null)return;personalHud??=new PersonalGameplayHud(this,hud.Root);}
            if(personalHud==null)return;UpdateRespawnGameplay();personalHud.SetVisible((IsFirstPerson||IsDriving)&&!LocalPlayerDead);if(LocalPlayerDead){usingItem=0;if(heldItem!=null)heldItem.SetActive(false);return;}
            if(!IsFirstPerson&&!IsDriving){usingItem=0;if(personalHud.Open)personalHud.Close();if(heldItem!=null)heldItem.SetActive(false);return;}
            var inventory=player.Inventory;if(!inventory.PersonalInitialised){inventory.PersonalInitialised=true;inventory.Add("ration",3);inventory.Add("water",3);inventory.Add("bandage",2);inventory.Assign(0,"repair-tool");inventory.Assign(1,"ration");inventory.Assign(2,"water");inventory.Assign(3,"bandage");}
            if(!inventory.BlueprintGranted){if(inventory.Add("building-blueprint",1)){inventory.BlueprintGranted=true;for(int slot=4;slot<10;slot++)if(inventory.Hotbar[slot]==null){inventory.Assign(slot,"building-blueprint");break;}}}
            var keyboard=Keyboard.current;var mouse=Mouse.current;
            if(!IsSurvival&&Time.unscaledTime>=nextVenue){nextVenue=Time.unscaledTime+.3;personalVenue=null;personalColony=null;float best=float.MaxValue;
                foreach(var building in Base.Buildings)if(!building.removed&&building.Complete&&Venue(building)!=null){float gap=CubeSphere.ArcDistance(player.Direction,CubeSphere.Decode(building.position),definition.radius);if(gap<best&&gap<Mathf.Max(12,Base.FootprintRadius(building.definition)+8)){best=gap;personalVenue=building;personalColony=Base.Settlements[building.team];}}
            }
            if(keyboard!=null&&Application.isFocused){if(!personalHud.TextFocused&&keyboard.iKey.wasPressedThisFrame){if(IndustrialMenuOpen||GroundLootOpen)personalHud.ShowInventory();else if(PersonalMenuOpen)personalHud.Close();else personalHud.ShowInventory();}if(!personalHud.TextFocused&&keyboard.oKey.wasPressedThisFrame){if(personalHud.OrganizationOpen)personalHud.Close();else personalHud.ShowOrganization();}if(keyboard.escapeKey.wasPressedThisFrame&&PersonalMenuOpen)personalHud.Close();
                if(!IsSurvival&&!personalHud.TextFocused&&keyboard.eKey.wasPressedThisFrame){if(personalVenue!=null)personalHud.ShowVenue(personalVenue,personalColony,Venue(personalVenue));else Base.Citizens?.QueryNearbyPerson(player.BodyPosition,false,(colony,person)=>{personalColony=colony;personalSpeaker=person;personalHud.ShowPerson(colony,person);});}
                if(!PersonalMenuOpen)for(int slot=0;slot<10;slot++)if((slot==0?keyboard.digit1Key:slot==1?keyboard.digit2Key:slot==2?keyboard.digit3Key:slot==3?keyboard.digit4Key:slot==4?keyboard.digit5Key:slot==5?keyboard.digit6Key:slot==6?keyboard.digit7Key:slot==7?keyboard.digit8Key:slot==8?keyboard.digit9Key:keyboard.digit0Key).wasPressedThisFrame){inventory.EquippedSlot=slot;usingItem=0;}
            }
            inventory.ClearMissingSlots();UpdateEquippedConstruction();inventorySlotUi?.Refresh();PresentHeldItem();
            var equipped=PersonalItems.Find(inventory.Equipped);
            if(!Paused&&!IsDriving&&!IndustrialDriving&&!IndustrialPlacementActive&&!RespawnPlacementActive&&!PersonalMenuOpen&&Application.isFocused&&mouse!=null&&mouse.leftButton.isPressed&&equipped!=null&&Time.time>=nextUse){
                if(equipped.kind==PersonalItemKind.Consumable){if(usingKey!=inventory.Equipped){usingKey=inventory.Equipped;usingItem=0;}usingItem+=Time.deltaTime;if(usingItem>=equipped.useSeconds){UsePersonalItem(inventory.Equipped,equipped);usingItem=0;nextUse=Time.time+.25;}}
                else if(mouse.leftButton.wasPressedThisFrame||equipped.useSeconds<.2f){UsePersonalItem(inventory.Equipped,equipped);nextUse=Time.time+Mathf.Max(.07f,equipped.useSeconds);}
            }else usingItem=0;
            if(!IsSurvival&&!Paused&&Time.unscaledTime>=nextCompanion){nextCompanion=Time.unscaledTime+.5;foreach(var member in inventory.Companions){if(member.body!=CivilisationBody())continue;movement.EnableKeyword("PLANET_COMPANIONS");encounter?.EnableCompanions();Vector3 goal=member.order==CompanionOrder.Follow?player.Direction:member.waitPoint;commands?.OrderCompanion(member.actor,goal,!member.registered);member.registered=true;}}
            if(!IsSurvival&&!Paused&&Time.unscaledTime>=nextSearch&&!PersonalMenuOpen){nextSearch=Time.unscaledTime+12;Base.Citizens?.QueryNearbyPerson(player.BodyPosition,true,(colony,officer)=>SearchPersonalItems(colony,officer));}
            personalHud.Refresh(usingItem,equipped?.useSeconds??1);
        }
        string CivilisationBody()=>SystemRuntime?.Civilisation?.BodyId(definition.bodyId)??definition.bodyId.ToString();
        internal void SetPersonalCursor(bool menu){menu|=LocalPlayerDead;bool piloted=IsFirstPerson||IsDriving||IsPiloting;Cursor.lockState=menu||!piloted?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=menu||!piloted;usingItem=0;nextUse=Time.time+.25;}
        string Venue(RtsBuilding b){switch(b.definition.id){case "civil_gun_shop":return "gun";case "civil_blade_shop":return "blade";case "civil_pawn_shop":return "pawn";case "civil_pub":return "pub";case "civil_food_shop":case "civil_drinks_shop":case "civil_market":return "food";case "civil_pharmacy":return "pharmacy";case "civil_black_market":return "blackmarket";default:return Base.Settlements?[b.team]?.crime.businesses.Exists(x=>x.building==b.persistentId&&!x.shutDown)==true?"blackmarket":null;}}
        internal bool PlayerHasPubLease(RtsBuilding b,int room=0){var installation=Base.Civilisation?.Installation(Base,b.persistentId);return installation!=null&&installation.playerRentUntil>Base.Civilisation.SimulationTime&&(room==0||installation.playerRentalRoom==room);}
        internal void RentPubApartment(RtsBuilding b,int room){var installation=Base.Civilisation?.Installation(Base,b.persistentId);var colony=Base.Settlements[b.team];if(installation==null||room<1||room>2||!CivilisationRuntime.PubOpen(Base,b)||player.Inventory.Credits<5)return;int npcRoom=installation.playerRentUntil>Base.Civilisation.SimulationTime&&installation.playerRentalRoom==1?2:1;bool npc=colony.households.Exists(h=>h.home==b.persistentId);if(npc&&room==npcRoom){Base.Notice="That apartment is occupied by a local household";return;}player.Inventory.Credits-=5;colony.council.treasury+=5;installation.playerRentalRoom=room;installation.playerRentUntil=Math.Max(Base.Civilisation.SimulationTime,installation.playerRentUntil)+Math.Max(60,Base.Civilisation.Settings.settlementDaySeconds);Base.Notice="Rented apartment "+room+" until "+Base.Civilisation.CivicDate(installation.playerRentUntil);personalHud.RefreshPanel();}
        internal bool Offered(PersonalItemDefinition item,string shop)=>((","+item.shops+",").Contains(","+shop+",")||shop=="pub"&&item.kind==PersonalItemKind.Consumable)&&(!item.illegal||shop=="blackmarket"||shop=="gang");
        internal int ShopStock(RtsBuilding building,PersonalItemDefinition item){var store=Base.Civilisation?.Installation(Base,building.persistentId);return store==null?0:(int)Math.Min(99,store.inventory.Get(item.goods)/Math.Max(1,item.goodsCost));}
        internal void BuyPersonalItem(RtsBuilding building,ColonyState colony,string shop,PersonalItemDefinition item){
            double price=item.price*(shop=="blackmarket"?.65:shop=="pawn"?.8:1);var inventory=player.Inventory;var stock=Base.Civilisation.Installation(Base,building.persistentId);
            if(shop=="gun"&&!colony.civilianWeaponsLegal&&item.kind==PersonalItemKind.Firearm){Base.Notice="Civilian firearms are restricted by settlement law";return;}
            if(shop=="pub"&&!CivilisationRuntime.PubOpen(Base,building)){Base.Notice="Public house closed · opens 11:00–23:00 with staff";return;}
            if(stock==null||!building.powered||building.paused||inventory.Credits<price||ShopStock(building,item)<=0){Base.Notice="Purchase unavailable: shop supply, opening status or credits";return;}
            long evidence=0;bool stolen=(shop=="blackmarket"||shop=="pawn")&&(item.kind==PersonalItemKind.Firearm||item.kind==PersonalItemKind.Bow)&&PlanetResourceProfile.Random(building.persistentId+":"+item.id+":"+inventory.Items.Count)<(shop=="blackmarket"?.6:.15);
            if(stolen)evidence=colony.crime.cases.Find(x=>x.offence==Offence.Murder&&!x.closed)?.id??0;
            string key=inventory.AddPersonal(item,new ItemProvenance{stolen=stolen,illegal=item.illegal,crime=evidence,seller=building.definition.name});if(key==null){Base.Notice="Inventory is full";return;}
            stock.inventory.Spend(item.goods,Math.Max(1,item.goodsCost));inventory.Credits-=price;colony.council.treasury+=price;Base.Notice="Purchased "+item.name;personalHud.RefreshPanel();
        }
        internal void BuyStreetWeapon(ColonyState colony,ResidentState seller){
            if(seller.crime.weapons<=0||player.Inventory.Credits<70)return;var definition=PersonalItems.Find(seller.id%4==0?"smg":"service-pistol");long evidence=colony.crime.cases.Find(x=>x.offence==Offence.Murder&&!x.closed)?.id??0;
            if(player.Inventory.AddPersonal(definition,new ItemProvenance{stolen=true,illegal=definition.illegal,crime=evidence,seller=seller.name})==null)return;seller.crime.weapons--;seller.savings+=70;player.Inventory.Credits-=70;Base.Notice="Bought a used weapon. Its provenance is uncertain.";personalHud.RefreshPanel();
        }
        void SearchPersonalItems(ColonyState colony,ResidentState officer){
            if(officer==null||officer.role!=CivilianRole.Officer)return;foreach(var entry in player.Inventory.Provenance.ToArray()){
                var definition=PersonalItems.Find(entry.Key);if(!player.Inventory.Items.ContainsKey(entry.Key)||definition==null)continue;bool contraband=entry.Value.illegal||!colony.civilianWeaponsLegal&&definition.kind==PersonalItemKind.Firearm;
                bool forensic=(entry.Value.stolen||entry.Value.crime>0)&&PlanetResourceProfile.Random(officer.id+":"+entry.Key+":"+(long)Time.time)<.4;if(!contraband&&!forensic)continue;
                player.Inventory.Consume(entry.Key,1);player.Inventory.ClearMissingSlots();player.Inventory.WantedHeat+=entry.Value.crime>0?40:10;
                colony.crime.cases.Add(new CrimeCase{id=Base.Civilisation.State.AllocateId(),offence=entry.Value.crime>0?Offence.Murder:Offence.Possession,suspect=-1,officer=officer.id,reported=true,time=Base.Civilisation.SimulationTime,place=CubeSphere.Encode(player.Direction),evidence=entry.Value.crime>0?.55:.8,outcome="Player item seized: "+entry.Key+(entry.Value.crime>0?"; forensic link to case #"+entry.Value.crime:"; unlawful or stolen weapon")});colony.crime.reportedCrimes++;Base.Notice="Police seized "+definition.name+(entry.Value.crime>0?" linked to murder case #"+entry.Value.crime:" during a search");break;
            }
        }
        internal void RecruitPersonal(ColonyState colony,ResidentState person){
            var inventory=player.Inventory;if(!person.alive||person.recruited||CivilisationRuntime.Confined(person)||person.age<18||inventory.Companions.Count>=4||inventory.Credits<50)return;
            string weapon=inventory.Items.Keys.FirstOrDefault(x=>x!=inventory.Equipped&&PersonalItems.Find(x)?.kind==PersonalItemKind.Firearm);if(weapon==null){Base.Notice="Recruitment requires 50 credits and a spare firearm to equip your guard";return;}
            movement.EnableKeyword("PLANET_COMPANIONS");encounter?.EnableCompanions();uint actor=(uint)unitCount;var east=Vector3.Cross(Mathf.Abs(player.Direction.y)>.9f?Vector3.right:Vector3.up,player.Direction).normalized;var point=(player.Direction+east*(6/definition.radius)).normalized;
            if(!TrySpawnUnit(0,point)){Base.Notice="No safe space for a recruit";return;}commands.OrderCompanion(actor,player.Direction,true);inventory.Consume(weapon,1);inventory.ClearMissingSlots();inventory.Credits-=50;person.savings+=50;person.recruited=true;person.crime.weapons++;person.workplace=0;person.role=CivilianRole.Survivor;
            inventory.Companions.Add(new CompanionContract{person=person.id,colony=colony.id,body=CivilisationBody(),actor=actor,name=person.name,order=CompanionOrder.Follow,registered=true});var gang=Base.Settlements[0]?.crime.gangs.Find(x=>x.leader==-1);if(inventory.OrganizationType=="Gang"&&gang!=null&&!gang.members.Contains(person.id))gang.members.Add(person.id);Base.Notice=person.name+" joined your organisation";personalHud.RefreshPanel();
        }
        internal void CompanionCommand(CompanionContract member,CompanionOrder order){member.order=order;member.waitPoint=player.Direction;if(member.body==CivilisationBody())commands.OrderCompanion(member.actor,member.waitPoint,false);}
        internal void FormPersonalOrganization(string name,string kind){if(string.IsNullOrWhiteSpace(name))return;player.Inventory.OrganizationName=name.Trim().Substring(0,Math.Min(48,name.Trim().Length));player.Inventory.OrganizationType=kind;var colony=Base.Settlements[0];if(colony!=null&&kind=="Gang"&&!colony.crime.gangs.Exists(x=>x.leader==-1))colony.crime.gangs.Add(new GangState{id=Base.Civilisation.State.AllocateId(),leader=-1,name=player.Inventory.OrganizationName,members=player.Inventory.Companions.Select(x=>x.person).ToList()});if(colony!=null&&kind=="Business"&&!colony.enterprises.Exists(x=>x.owner==-1))colony.enterprises.Add(new EnterpriseState{id=Base.Civilisation.State.AllocateId(),owner=-1,name=player.Inventory.OrganizationName});personalHud.RefreshPanel();}
        void UsePersonalItem(string key,PersonalItemDefinition item){
            if(!player.Inventory.Items.ContainsKey(key))return;
            if(EquipmentInventory&&(key=="radiation-suit"||key=="decontamination-kit")){industry.UseProtectionItem(key);return;}
            if(!string.IsNullOrEmpty(item.useAction)&&personalActions.TryGetValue(item.useAction,out var action)){action(this,key,item);return;}
            if(item.kind==PersonalItemKind.Consumable){if(!player.Inventory.Consume(key,1))return;var state=player.Environment;state.Health=Mathf.Clamp(state.Health+item.health,0,100);state.Hunger=Mathf.Clamp(state.Hunger+item.hunger,0,100);state.Thirst=Mathf.Clamp(state.Thirst+item.thirst,0,100);if(item.id=="suit-oxygen")state.Oxygen=Mathf.Min(100,state.Oxygen+60);if(Base!=null)Base.Notice="Used "+item.name;else SurvivalNotice="Used "+item.name;return;}
            if(item.kind==PersonalItemKind.Tool)return;if(MilitaryPolicyError!=null){SurvivalNotice=MilitaryPolicyError;return;}if(!string.IsNullOrEmpty(item.ammo)&&!player.Inventory.Consume(item.ammo,1)){if(Base!=null)Base.Notice="Out of "+item.ammo;else SurvivalNotice="Out of "+item.ammo;return;}
            float range=item.range;var ray=new Ray(view.transform.position,view.transform.forward);if(Physics.Raycast(ray,out var hit,range,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))range=hit.distance;
            // Terrain occlusion is checked on the CPU only along this one shot. Army
            // target selection and damage stay GPU-authoritative and event-driven.
            for(float d=1;d<range;d+=Mathf.Max(1,range/64)){var p=RenderToBody(ray.GetPoint(d));if(p.Magnitude<definition.radius+surface.SampleHeight(PlanetPrecisionPolicy.Direction(p))){range=d;break;}}
            encounter?.PlayerStrike(player.PreciseBodyPosition+new Double3(player.Direction)*player.EyeHeight,ray.direction,range,item.damage,item.kind==PersonalItemKind.Melee?.7f:.35f);if(Base!=null)Base.Notice=item.name;else SurvivalNotice=item.name;
        }
        void PresentHeldItem(){
            string key=player.Inventory.Equipped;var item=PersonalItems.Find(key);if(key!=heldKey){if(heldOwnMaterial&&heldMaterial!=null)Destroy(heldMaterial);heldOwnMaterial=false;heldKey=key;if(heldItem!=null)Destroy(heldItem);heldItem=null;if(item!=null&&item.kind!=PersonalItemKind.Consumable){heldItem=GameObject.CreatePrimitive(PrimitiveType.Cube);heldItem.name="Player equipped "+item.name;heldItem.layer=2;var collider=heldItem.GetComponent<Collider>();collider.enabled=false;Destroy(collider);heldItem.transform.SetParent(view.transform,false);heldItem.transform.localPosition=new Vector3(.27f,-.24f,.55f);heldItem.transform.localScale=item.kind==PersonalItemKind.Melee?new Vector3(.04f,.04f,.65f):new Vector3(.13f,.11f,.35f);var mesh=string.IsNullOrEmpty(item.mesh)?null:Resources.Load<Mesh>(item.mesh);if(mesh!=null)heldItem.GetComponent<MeshFilter>().sharedMesh=mesh;heldMaterial=string.IsNullOrEmpty(item.material)?null:Resources.Load<Material>(item.material);if(heldMaterial==null){heldMaterial=new Material(Shader.Find("HDRP/Lit")??Shader.Find("Standard"));heldOwnMaterial=true;}if(item.useAction=="blueprint"){heldItem.transform.localScale=new Vector3(.3f,.015f,.24f);heldMaterial.SetColor("_BaseColor",new Color(.025f,.2f,.55f));}heldItem.GetComponent<Renderer>().sharedMaterial=heldMaterial;}}
            if(heldItem!=null)heldItem.SetActive(!PersonalMenuOpen&&!IsDriving&&!IndustrialDriving&&!IndustrialPlacementActive&&!RespawnPlacementActive&&!EquippedBuildingModule(out _));
        }
        void DisposePersonalGameplay(){playerConstruction?.Dispose();personalHud?.Dispose();survivalUi?.Dispose();if(heldItem!=null)Destroy(heldItem);if(heldMaterial!=null&&heldOwnMaterial)Destroy(heldMaterial);}
    }
    public sealed partial class CivilisationRuntime {
        public static ResidentState PubBartender(ColonyState colony,RtsBuilding pub)=>colony.residents.Find(p=>p.alive&&!p.recruited&&p.age>=18&&!Confined(p)&&p.workplace==pub.persistentId);
        public static bool PubOpen(PlanetRtsBase local,RtsBuilding pub){var simulation=local.Civilisation;return simulation!=null&&simulation.LocalHour>=11&&simulation.LocalHour<23&&pub.Complete&&!pub.removed&&pub.powered&&!pub.paused&&PubBartender(local.Settlements[pub.team],pub)!=null;}
    }

}
