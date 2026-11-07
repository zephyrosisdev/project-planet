using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
namespace MassiveArmy.Planetary {
    // Immutable historical records. Fixed-size on-disk index buckets provide direct
    // ID lookup without retaining a growing index or completed records in RAM.
    internal sealed class SettlementHistoryStore {
        readonly string root;
        internal SettlementHistoryStore(string root){this.root=root;}
        static uint Hash(byte[] bytes){uint h=2166136261;foreach(byte b in bytes)h=(h^b)*16777619;return h;}
        string Folder(long colony,string kind){if(colony<=0||kind!="deaths"&&kind!="cases"&&kind!="residents")throw new ArgumentException("Invalid archive key");return Path.Combine(root,colony.ToString(),kind);}
        internal void Put(long colony,string kind,long id,string json){
            if(id<=0)throw new ArgumentException("Invalid history ID");string existing=Get(colony,kind,id);if(existing==json)return;string folder=Folder(colony,kind);Directory.CreateDirectory(folder);byte[] bytes=Encoding.UTF8.GetBytes(json);if(bytes.Length>1048576)throw new InvalidDataException("History record exceeds 1 MiB");uint hash=Hash(bytes);
            // The record is durable before its index is committed. An interrupted
            // append leaves an unreachable tail; the live record is retained on error.
            long offset;using(var stream=new FileStream(Path.Combine(folder,"records.bin"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read)){
                offset=stream.Length;stream.Position=offset;stream.Write(bytes,0,bytes.Length);stream.Flush(true);
            }
            using(var stream=new FileStream(Path.Combine(folder,(id/1024)+".idx"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read))using(var writer=new BinaryWriter(stream)){
                stream.Position=(id%1024)*16;writer.Write(offset);writer.Write(bytes.Length);writer.Write(hash);writer.Flush();stream.Flush(true);
            }
        }
        internal string Get(long colony,string kind,long id){
            if(id<=0)return null;string folder=Folder(colony,kind),index=Path.Combine(folder,(id/1024)+".idx");if(!File.Exists(index))return null;
            using var stream=new FileStream(index,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);long position=(id%1024)*16;if(position+16>stream.Length)return null;stream.Position=position;
            using var reader=new BinaryReader(stream);return ReadRecord(folder,reader);
        }
        static string ReadRecord(string folder,BinaryReader index){
            long offset=index.ReadInt64();int length=index.ReadInt32();uint hash=index.ReadUInt32();if(length==0)return null;
            if(offset<0||length<0||length>1048576)throw new InvalidDataException("Invalid history index");
            using var data=new FileStream(Path.Combine(folder,"records.bin"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite);if(offset+length>data.Length)throw new InvalidDataException("Incomplete history record");data.Position=offset;
            var bytes=new byte[length];int read=0;while(read<length){int n=data.Read(bytes,read,length-read);if(n==0)throw new EndOfStreamException();read+=n;}
            if(Hash(bytes)!=hash)throw new InvalidDataException("History checksum mismatch");return Encoding.UTF8.GetString(bytes);
        }
        internal IEnumerable<string> All(long colony,string kind){
            string folder=Folder(colony,kind);if(!Directory.Exists(folder))yield break;
            foreach(string path in Directory.EnumerateFiles(folder,"*.idx")){
                using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var reader=new BinaryReader(stream);
                for(int i=0;i<1024&&stream.Position+16<=stream.Length;i++){string json=ReadRecord(folder,reader);if(json!=null)yield return json;}
            }
        }
    }
    public sealed partial class CivilisationRuntime {
        sealed class HistoryWrite {internal ColonyState colony;internal string kind,json;internal long id;internal object record;}
        SettlementHistoryStore historyStore;Task historyTask;List<HistoryWrite> historyPending;int historyTurn,historyScan;double nextHistory;string historyFailure;
        internal string HistoryFailure=>historyFailure;
        SettlementHistoryStore History {
            get {if(historyStore==null){if(!Guid.TryParseExact(State.historyArchive,"N",out _))State.historyArchive=Guid.NewGuid().ToString("N");historyStore=new SettlementHistoryStore(Path.Combine(Application.persistentDataPath,"SettlementHistory",State.historyArchive));}return historyStore;}
        }
        void CompleteHistory(){
            if(historyTask==null||!historyTask.IsCompleted)return;
            if(historyTask.IsFaulted){historyFailure=historyTask.Exception.GetBaseException().Message;nextHistory=SimulationTime+60;}
            else foreach(var item in historyPending){
                // A record changed while the writer ran remains live for another pass.
                if(JsonUtility.ToJson(item.record)!=item.json)continue;
                if(item.kind=="cases"){var q=(CrimeCase)item.record;if(q.reported){var suspect=Person(item.colony,q.suspect);if(suspect!=null)suspect.crime.knownToPolice=true;}}
                if(item.kind=="deaths"){if(item.colony.deaths.Remove((DeathRecord)item.record))item.colony.archivedDeaths++;}
                else if(item.kind=="residents"){if(item.colony.residents.Remove((ResidentState)item.record))item.colony.archivedResidents++;}
                else if(item.colony.crime.cases.Remove((CrimeCase)item.record))item.colony.archivedCases++;
                var c=item.colony;if(c.residents.Capacity>Math.Max(256,c.residents.Count*4))c.residents.TrimExcess();if(c.deaths.Capacity>Math.Max(256,c.deaths.Count*4))c.deaths.TrimExcess();if(c.crime.cases.Capacity>Math.Max(256,c.crime.cases.Count*4))c.crime.cases.TrimExcess();
            }
            historyPending=null;historyTask=null;
        }
        void MaintainHistory(){
            CompleteHistory();if(historyTask!=null||SimulationTime<nextHistory||State.colonies.Count==0)return;nextHistory=SimulationTime+5;
            var c=State.colonies[historyTurn++%State.colonies.Count];int keep=Math.Max(64,Settings.recentHistoryRecords);var batch=new List<HistoryWrite>(32);
            int deaths=Math.Max(0,c.deaths.Count-keep),cases=Math.Max(0,c.crime.cases.Count-keep);int candidates=deaths+cases;
            for(int scanned=0;scanned<Math.Min(64,candidates)&&batch.Count<31;scanned++){
                int i=(int)(((long)historyScan+scanned)%candidates);
                if(i<deaths){var d=c.deaths[i];bool active=false;foreach(var vehicle in c.cargoVehicles)if(vehicle.funeralDeath==d.id){active=true;break;}
                    if(!d.interred||!d.discovered||active||c.security.activeFuneral==d.id)continue;
                    bool referenced=false;foreach(var q in c.crime.cases)if(q.suspect==d.resident||q.victim==d.resident||q.officer==d.resident){referenced=true;break;}
                    if(referenced)continue;
                    batch.Add(new HistoryWrite{colony=c,kind="deaths",id=d.id,record=d,json=JsonUtility.ToJson(d)});
                    var person=Person(c,d.resident);if(person!=null&&!person.alive&&batch.Count<32)batch.Add(new HistoryWrite{colony=c,kind="residents",id=person.id,record=person,json=JsonUtility.ToJson(person)});
                }else {var q=c.crime.cases[i-deaths];if(!q.closed)continue;bool held=false;foreach(var p in c.residents)if(p.alive&&p.id==q.suspect&&Confined(p)){held=true;break;}if(held)continue;
                    batch.Add(new HistoryWrite{colony=c,kind="cases",id=q.id,record=q,json=JsonUtility.ToJson(q)});
                }
            }
            historyScan=candidates==0?0:(historyScan+64)%candidates;if(batch.Count==0)return;
            var store=History;historyPending=batch;
            historyTask=Task.Run(()=>{foreach(var item in batch)store.Put(item.colony.id,item.kind,item.id,item.json);});
        }
        // Explicit history queries are asynchronous; UI updates never trigger disk reads.
        public Task<string> ReadArchivedResident(long colony,long id){var store=History;return Task.Run(()=>store.Get(colony,"residents",id));}
        public Task<string> ReadArchivedHistory(long colony,long id,bool death){var store=History;return Task.Run(()=>store.Get(colony,death?"deaths":"cases",id));}
        UniverseState PortableHistorySnapshot(){
            if(historyTask!=null){try{historyTask.GetAwaiter().GetResult();}catch{}}CompleteHistory();foreach(var colony in State.colonies)colony.inventory.CaptureWallet();
            if(historyStore==null&&string.IsNullOrEmpty(State.historyArchive))return State;
            return ExpandForSave(State,History);
        }
        internal static UniverseState ExpandForSave(UniverseState state,SettlementHistoryStore store){
            var copy=JsonUtility.FromJson<UniverseState>(JsonUtility.ToJson(state));
            foreach(var c in copy.colonies){
                int storedDeaths=0,storedCases=0,storedPeople=0;var deaths=new HashSet<long>();foreach(var d in c.deaths)deaths.Add(d.id);
                foreach(string json in store.All(c.id,"deaths")){storedDeaths++;var d=JsonUtility.FromJson<DeathRecord>(json);if(deaths.Add(d.id))c.deaths.Add(d);}
                var cases=new HashSet<long>();foreach(var q in c.crime.cases)cases.Add(q.id);
                foreach(string json in store.All(c.id,"cases")){storedCases++;var q=JsonUtility.FromJson<CrimeCase>(json);if(cases.Add(q.id))c.crime.cases.Add(q);}
                var people=new HashSet<long>();foreach(var person in c.residents)people.Add(person.id);
                foreach(string json in store.All(c.id,"residents")){storedPeople++;var person=JsonUtility.FromJson<ResidentState>(json);if(people.Add(person.id))c.residents.Add(person);}
                if(storedDeaths<c.archivedDeaths||storedCases<c.archivedCases||storedPeople<c.archivedResidents)throw new InvalidDataException("Settlement history files are missing; save aborted to preserve history");
                c.deaths.Sort((a,b)=>a.time.CompareTo(b.time));c.crime.cases.Sort((a,b)=>a.time.CompareTo(b.time));c.archivedDeaths=c.archivedCases=c.archivedResidents=0;
            }
            copy.historyArchive="";return copy;
        }
    }
}
