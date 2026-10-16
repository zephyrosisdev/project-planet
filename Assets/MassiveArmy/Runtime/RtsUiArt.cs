using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassiveArmy
{
    public sealed class RtsUiArt : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public string kind,id;
            public Texture2D portrait;
            public VectorImage icon;
        }
        public Entry[] entries=Array.Empty<Entry>();
        Dictionary<string,Entry> lookup;
        public Entry Find(string kind,string id)
        {
            if(lookup==null){lookup=new Dictionary<string,Entry>();foreach(var e in entries)lookup.Add(e.kind+":"+e.id,e);}
            return lookup.TryGetValue(kind+":"+id,out var found)?found:null;
        }
    }
}
