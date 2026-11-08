using System;
using System.Security.Cryptography;

namespace MassiveArmy.Industrial {
    [Serializable] public sealed class SurvivalChunk {
        public int version=1,total,offset,count;public long transfer;public string hash,payload;
    }
    public sealed class SurvivalChunkSender {
        public const int MaximumBytes=1048576,MaximumChunkBytes=8192;
        readonly byte[] bytes;readonly string hash;readonly long id;int offset;
        public long Id=>id;
        public bool Complete=>offset==bytes.Length;
        public SurvivalChunkSender(long id,byte[] bytes){
            if(id<=0||bytes==null||bytes.Length==0||bytes.Length>MaximumBytes)throw new ArgumentException("Invalid bounded snapshot transfer");
            this.id=id;this.bytes=bytes;using(var sha=SHA256.Create())hash=Convert.ToBase64String(sha.ComputeHash(bytes));
        }
        public SurvivalChunk Preview(int maximum){int count=Math.Min(bytes.Length-offset,Math.Min(maximum,MaximumChunkBytes));if(count<=0)return null;return new SurvivalChunk{transfer=id,total=bytes.Length,offset=offset,count=count,hash=hash,payload=Convert.ToBase64String(bytes,offset,count)};}
        public void Commit(SurvivalChunk chunk){if(chunk==null||chunk.transfer!=id||chunk.offset!=offset||chunk.count<=0||chunk.count>Math.Min(MaximumChunkBytes,bytes.Length-offset))throw new InvalidOperationException("Invalid transfer commit");offset+=chunk.count;}
    }
    public sealed class SurvivalChunkReceiver {
        byte[] bytes;string hash;long active,completed;int received;double last;
        public int BufferedBytes=>bytes?.Length??0;
        public void Cancel(long through){if(through<=0)return;completed=Math.Max(completed,through);if(active<=through){bytes=null;hash=null;received=0;}}
        public void Clear(){bytes=null;hash=null;active=completed=0;received=0;last=0;}
        public byte[] Accept(SurvivalChunk chunk,double now){
            if(chunk==null)return null;
            if(chunk.version!=1||chunk.transfer<=0||chunk.total<1||chunk.total>SurvivalChunkSender.MaximumBytes||chunk.offset<0||chunk.count<1||chunk.count>SurvivalChunkSender.MaximumChunkBytes||chunk.offset>chunk.total-chunk.count||chunk.hash==null||chunk.hash.Length!=44||chunk.payload==null||chunk.payload.Length!=4*((chunk.count+2)/3))throw new ArgumentException("Malformed snapshot fragment");
            if(chunk.transfer<=completed||chunk.transfer<active)return null;
            var part=Convert.FromBase64String(chunk.payload);if(part.Length!=chunk.count)throw new ArgumentException("Fragment length mismatch");
            if(bytes!=null&&now-last>600){bytes=null;received=0;}
            if(bytes==null||chunk.transfer>active){if(chunk.offset!=0)throw new ArgumentException("Missing snapshot fragment prefix");bytes=new byte[chunk.total];active=chunk.transfer;hash=chunk.hash;received=0;}
            if(bytes.Length!=chunk.total||hash!=chunk.hash)throw new ArgumentException("Snapshot fragment identity changed");
            if(chunk.offset<received){if(chunk.offset+part.Length>received)throw new ArgumentException("Overlapping fragment");for(int i=0;i<part.Length;i++)if(bytes[chunk.offset+i]!=part[i])throw new ArgumentException("Conflicting duplicate fragment");return null;}
            if(chunk.offset!=received)throw new ArgumentException("Out-of-order snapshot fragment");
            Buffer.BlockCopy(part,0,bytes,received,part.Length);received+=part.Length;last=now;
            if(received<bytes.Length)return null;
            byte[] result=bytes;using(var sha=SHA256.Create())if(Convert.ToBase64String(sha.ComputeHash(result))!=hash){bytes=null;received=0;throw new ArgumentException("Snapshot digest mismatch");}
            completed=active;bytes=null;received=0;return result;
        }
    }
    public sealed partial class IndustrialActor {internal IndustrialActor CopyState()=>(IndustrialActor)MemberwiseClone();}
}
