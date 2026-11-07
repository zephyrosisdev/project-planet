using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public enum PersonalItemKind {Tool,Firearm,Melee,Bow,Consumable}
    [Serializable]public sealed class PersonalItemDefinition {
        public string useAction;public string id,name,description,mesh,material,ammo,goods="food",shops="food";public PersonalItemKind kind;
        public bool illegal;public int quantity=1,goodsCost=1,price=10,damage=20;public float range=3,useSeconds=.5f,health,hunger,thirst;
    }
    [Serializable]public sealed class PersonalItemCatalogue {
        public PersonalItemDefinition[] items;[NonSerialized]Dictionary<string,PersonalItemDefinition> lookup;
        public PersonalItemDefinition Find(string key){if(string.IsNullOrEmpty(key))return null;int at=key.IndexOf('#');if(at>=0)key=key.Substring(0,at);lookup??=new Dictionary<string,PersonalItemDefinition>();if(lookup.Count==0)foreach(var item in items)lookup.Add(item.id,item);if(lookup.TryGetValue(key,out var value))return value;if(MassiveArmy.Industrial.IndustrialCatalog.Items.ContainsKey(key)){value=new PersonalItemDefinition{id=key,name=MassiveArmy.Industrial.IndustrialCatalog.DisplayItem(key),kind=PersonalItemKind.Tool,useAction="equipment"};lookup[key]=value;return value;}return null;}
        public static PersonalItemCatalogue Load()=>JsonUtility.FromJson<PersonalItemCatalogue>(Resources.Load<TextAsset>("MassiveArmy/PersonalItems").text);
    }
    [Serializable]public sealed class ItemProvenance {public bool stolen,illegal;public long crime;public string seller;}
    public enum CompanionOrder {Follow,Wait,Guard}
    [Serializable]public sealed class CompanionContract {public long person,colony;public string name,body;public uint actor;public CompanionOrder order;public Vector3 waitPoint;public bool registered;}
    public sealed partial class PlayerInventory {
        public bool BlueprintGranted;public bool PersonalInitialised;public double Credits=500,WantedHeat;public string OrganizationName="Independent",OrganizationType="None";
        public readonly string[] Hotbar=new string[10];
        public readonly Dictionary<string,ItemProvenance> Provenance=new Dictionary<string,ItemProvenance>();
        public readonly List<CompanionContract> Companions=new List<CompanionContract>();
        long serial;
        public string Equipped=>UnifiedInventory?Unified.AtSlot(EquippedSlot)?.item:EquippedSlot>=0&&EquippedSlot<Hotbar.Length?Hotbar[EquippedSlot]:null;
        public bool Assign(int slot,string key){if(UnifiedInventory)return UnifiedAssign(slot,key);if(slot<0||slot>=Hotbar.Length||!items.ContainsKey(key))return false;Hotbar[slot]=key;return true;}
        public string AddPersonal(PersonalItemDefinition definition,ItemProvenance history=null){string key=definition.kind==PersonalItemKind.Firearm||definition.kind==PersonalItemKind.Melee||definition.kind==PersonalItemKind.Bow?definition.id+"#"+(++serial):definition.id;if(!Add(key,definition.quantity))return null;if(history!=null)Provenance[key]=history;return key;}
        public void ClearMissingSlots(){if(UnifiedInventory){for(int i=0;i<Hotbar.Length;i++)Hotbar[i]=Unified.AtSlot(i)?.item;return;}for(int i=0;i<Hotbar.Length;i++)if(Hotbar[i]!=null&&!items.ContainsKey(Hotbar[i]))Hotbar[i]=null;}
        void CopyPersonalFrom(PlayerInventory other){BlueprintGranted=other.BlueprintGranted;PersonalInitialised=other.PersonalInitialised;Credits=other.Credits;WantedHeat=other.WantedHeat;OrganizationName=other.OrganizationName;OrganizationType=other.OrganizationType;serial=other.serial;EquippedSlot=other.EquippedSlot;Array.Copy(other.Hotbar,Hotbar,Hotbar.Length);Provenance.Clear();foreach(var pair in other.Provenance)Provenance.Add(pair.Key,new ItemProvenance{crime=pair.Value.crime,stolen=pair.Value.stolen,illegal=pair.Value.illegal,seller=pair.Value.seller});Companions.Clear();foreach(var c in other.Companions)Companions.Add(new CompanionContract{person=c.person,colony=c.colony,name=c.name,body=c.body,actor=c.actor,order=c.order,waitPoint=c.waitPoint,registered=c.registered});}
    }
}

