using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    /// <summary>Reuses dormant rigs, while bounding total live rig objects as well as active rigs.</summary>
    public sealed class VehicleRigPool:IDisposable
    {
        sealed class Entry {public VehicleRig rig;public bool used;}
        readonly List<Entry> entries=new List<Entry>();readonly Transform parent;
        int limit=4;
        public int Limit {get=>limit;set{limit=Mathf.Clamp(value,1,32);Trim();}}
        public int Allocated=>entries.Count;
        public VehicleRigPool(Transform parent){var root=new GameObject("Bounded vehicle detail pool");root.transform.SetParent(parent,false);this.parent=root.transform;}
        public VehicleRig Acquire(VehicleDefinition definition){
            foreach(var entry in entries)if(!entry.used&&entry.rig.definition==definition){entry.used=true;entry.rig.gameObject.SetActive(true);return entry.rig;}
            if(entries.Count>=limit){int free=entries.FindIndex(e=>!e.used);if(free<0)return null;ArmyLifetime.Destroy(entries[free].rig.gameObject);entries.RemoveAt(free);}
            var go=UnityEngine.Object.Instantiate(definition.prefab,parent);go.name=definition.displayName+" observer rig";var rig=go.GetComponent<VehicleRig>();entries.Add(new Entry{rig=rig,used=true});return rig;
        }
        public void Release(VehicleRig rig){foreach(var entry in entries)if(entry.rig==rig){entry.used=false;rig.gameObject.SetActive(false);break;}Trim();}
        void Trim(){for(int i=entries.Count-1;i>=0&&entries.Count>limit;i--)if(!entries[i].used){ArmyLifetime.Destroy(entries[i].rig.gameObject);entries.RemoveAt(i);}}
        public void Dispose(){ArmyLifetime.Destroy(parent.gameObject);entries.Clear();}
    }
}
