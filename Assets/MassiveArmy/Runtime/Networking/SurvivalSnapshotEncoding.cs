using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MassiveArmy.Networking {
    // Entity instances are immutable for one publication. Cache public wire data
    // once, even when many subscribers need the same state.
    internal static class SurvivalSnapshotEncoding {
        static readonly List<SurvivalEntity> Empty=new List<SurvivalEntity>();
        static readonly byte[] Marker=Encoding.UTF8.GetBytes("\"changed\":[]");
        internal static byte[] Entity(SurvivalEntity entity)=>entity.encoded??(entity.encoded=Encoding.UTF8.GetBytes(JsonUtility.ToJson(entity)));
        internal static byte[] Encode(SurvivalSnapshot packet){
            var changes=packet.changed;byte[] core;
            packet.changed=Empty;
            try{core=Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));}finally{packet.changed=changes;}
            if(changes==null||changes.Count==0)return core;
            int insertion=-1;
            for(int i=0;i<=core.Length-Marker.Length;i++){
                if(core[i]!=Marker[0])continue;int j=1;
                while(j<Marker.Length&&core[i+j]==Marker[j])j++;
                if(j==Marker.Length){insertion=i+Marker.Length-1;break;}
            }
            if(insertion<0)throw new InvalidOperationException("Snapshot changes field is missing");
            int length=core.Length+changes.Count-1;
            foreach(var entity in changes)length=checked(length+Entity(entity).Length);
            var output=new byte[length];Buffer.BlockCopy(core,0,output,0,insertion);int offset=insertion;
            for(int i=0;i<changes.Count;i++){
                if(i>0)output[offset++]=(byte)',';var bytes=Entity(changes[i]);
                Buffer.BlockCopy(bytes,0,output,offset,bytes.Length);offset+=bytes.Length;
            }
            Buffer.BlockCopy(core,insertion,output,offset,core.Length-insertion);return output;
        }
    }
}
