using System;
using System.Collections.Generic;

namespace MassiveArmy.Industrial {
    // These records belong to the existing world snapshot/journal. They are not an army mirror.
    [Serializable] public sealed class SurvivalPlayerRecord {
        public long actor; public string identity,name; public FramePose spawn;
        public double oxygen=100,hunger=100,thirst=100,verticalSpeed;
        public int shipSeat=-1;public bool initialSpawnPending,interiorWalking;public FramePose interiorPose;
        public float pitch; public long controlledEntity,controlledShip,attachedShip,pendingVehicleExit,lastActionSequence,nextUseTick,nextGatherTick,nextAttackTick; public uint acknowledgedInput;
    }
    [Serializable] public sealed class SurvivalSessionState {
        public string worldId,instanceId; public long serverTick;public double simulationSeconds;public int worldSeed=73129,contentVersion=1;
        public List<SurvivalPlayerRecord> players=new List<SurvivalPlayerRecord>();
        public List<SurvivalVehicleState> vehicles=new List<SurvivalVehicleState>();
        public List<SurvivalShipState> ships=new List<SurvivalShipState>();
    }
    public sealed partial class IndustrialWorld {
        public SurvivalSessionState multiplayer;
    }
    public sealed partial class IndustrialAuthority {
        long networkActor,networkSequence;
        public string ExecuteSurvivalAction(long actor,long sequence,Func<string> action){
            lock(gate){
                var player=World.multiplayer?.players.Find(p=>p.actor==actor);
                if(player==null||sequence<=player.lastActionSequence)return "Stale network action";
                networkActor=actor;networkSequence=sequence;
                try{
                    string error=action();
                    if(error==null){var who=World.Actor(actor);Append(new IndustrialJournalEntry{survivalCheckpoint=true,survival=World.multiplayer,actors=new List<IndustrialActorCheckpoint>{new IndustrialActorCheckpoint{id=actor,pose=who.pose,health=who.health,collisionRadius=who.collisionRadius,vehicleObstacle=who.vehicleObstacle,vehicleObstacleRadius=who.vehicleObstacleRadius}}});}
                    return error;
                }finally{networkActor=networkSequence=0;}
            }
        }
        // Persist new identities before accepting any transactions referring to them.
        static void ApplySurvivorRegistration(IndustrialWorld world,SurvivalRegistration registration){
            world.multiplayer??=new SurvivalSessionState{worldId=registration.worldId,worldSeed=registration.seed};
            if(world.multiplayer.worldId!=registration.worldId||world.multiplayer.worldSeed!=registration.seed)throw new System.IO.InvalidDataException("Survivor registration belongs to another world");
            var player=registration.player;
            if(world.Actor(player.actor)!=null||world.multiplayer.players.Exists(p=>p.identity==player.identity))throw new System.IO.InvalidDataException("Duplicate survivor registration");
            var actor=world.Register(player.actor,player.spawn);world.multiplayer.players.Add(player);world.multiplayer.instanceId=registration.instanceId;
            world.EnableInventoryWorkflow();
            world.ApplyInventory(new InventoryTransaction{kind="import",actor=player.actor,imports=new List<ItemStack>{new ItemStack("repair-tool",1),new ItemStack("suit-oxygen",1),new ItemStack("ration",3),new ItemStack("water",3),new ItemStack("bandage",2),new ItemStack("building-blueprint",1)}});
        }
        public SurvivalPlayerRecord RegisterSurvivor(string identity,string name,FramePose spawn,string worldId,string instanceId,int seed=73129){
            if(string.IsNullOrWhiteSpace(identity)||identity.Length>128)throw new ArgumentException("Invalid verified identity");
            if(string.IsNullOrWhiteSpace(name)||name.Length>32)throw new ArgumentException("Choose a name between 1 and 32 characters");
            foreach(char letter in name)if(char.IsControl(letter))throw new ArgumentException("Names cannot contain control characters");
            try{System.Xml.XmlConvert.VerifyXmlChars(name);}catch(System.Xml.XmlException){throw new ArgumentException("Name contains invalid Unicode characters");}
            lock(gate){
                if(World.multiplayer==null)World.multiplayer=new SurvivalSessionState{worldId=worldId,instanceId=instanceId,worldSeed=seed};
                if(World.multiplayer.worldId!=worldId||World.multiplayer.worldSeed!=seed)throw new InvalidOperationException("Save belongs to another world or seed");
                var existing=World.multiplayer.players.Find(p=>p.identity==identity);if(existing!=null)return existing;
                long id=1;foreach(var a in World.actors)id=Math.Max(id,a.id+1);
                var player=new SurvivalPlayerRecord{actor=id,identity=identity,name=name,spawn=spawn,initialSpawnPending=true};
                // Identity and starter items recover together, without replacing the checkpoint per admission.
                EnsureRespawnSnapshot();var registration=new SurvivalRegistration{player=player,instanceId=instanceId,worldId=worldId,seed=seed};
                Append(new IndustrialJournalEntry{registration=registration});ApplySurvivorRegistration(World,registration);return player;
            }
        }
    }

    [Serializable] public sealed class SurvivalRegistration {public SurvivalPlayerRecord player;public string worldId,instanceId;public int seed;}
    [Serializable] public sealed class SurvivalServerConfig {
        public string AuthenticationProfile(){using(var hash=System.Security.Cryptography.SHA256.Create())return "server_"+BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(instanceId))).Replace("-","").Substring(0,23);}
        public const int Protocol=6;public const string Content="survival-2";
        public string worldId="survival",instanceId="region-1",displayName="Synthetic Front",region="",environment="production",savePath="";
        public int maxPlayers=100,tickRate=30,snapshotRate=10,seed=73129,packetBytesPerSecond=65536;
        public double surfaceRadius=500,shipRadius=5000,proxyRadius=100000,hysteresis=100;
        public double walkSnap=3,vehicleSnap=10,shipSnap=50,walkSpeed=7;
        public void Validate(){
            if(string.IsNullOrWhiteSpace(worldId)||string.IsNullOrWhiteSpace(instanceId)||worldId.Length>64||instanceId.Length>64)throw new ArgumentException("World and instance IDs are required (maximum 64 characters)");
            if(maxPlayers<1||maxPlayers>149)throw new ArgumentOutOfRangeException(nameof(maxPlayers),"Relay capacity must leave room for the allocating server (1–149 clients)");
            if(tickRate<10||tickRate>60||snapshotRate<1||snapshotRate>tickRate)throw new ArgumentException("Invalid tick/snapshot rates");
            foreach(double n in new[]{surfaceRadius,shipRadius,proxyRadius,walkSnap,vehicleSnap,shipSnap,walkSpeed})if(!Finite(n)||n<=0)throw new ArgumentException("Distances and speeds must be finite and positive");
            if(!Finite(hysteresis)||hysteresis<0||surfaceRadius>shipRadius||shipRadius>proxyRadius||packetBytesPerSecond<4096||packetBytesPerSecond>1048576)throw new ArgumentException("Invalid interest or packet budget");
        }
        public static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
    }
    [Serializable] public struct SurvivalInput {
        public uint sequence;public long epoch;public float x,y,z,roll,lookX,lookY;public bool jump,brake,boost,toggleAssist,toggleGear;
        public bool Valid=>sequence!=0&&SurvivalServerConfig.Finite(x)&&SurvivalServerConfig.Finite(y)&&SurvivalServerConfig.Finite(z)&&SurvivalServerConfig.Finite(roll)&&SurvivalServerConfig.Finite(lookX)&&SurvivalServerConfig.Finite(lookY)&&Math.Abs(x)<=1&&Math.Abs(y)<=1&&Math.Abs(z)<=1&&Math.Abs(roll)<=1&&Math.Abs(lookX)<=180&&Math.Abs(lookY)<=180;
    }
    /// <summary>Admission and tick budget only. Packet arrival never advances simulation.</summary>
    public sealed class SurvivalInputQueue {
        readonly Queue<SurvivalInput> pending=new Queue<SurvivalInput>();
        uint newest;long rateTick=-1;int received;SurvivalInput held;int stale;
        public bool Enqueue(SurvivalInput input,long tick,int tickRate,long? expectedEpoch=null){
            if(!input.Valid||expectedEpoch.HasValue&&input.epoch!=expectedEpoch.Value||input.sequence<=newest||input.sequence-newest>4096)return false;
            long window=tick/tickRate;if(window!=rateTick){received=0;rateTick=window;}
            if(++received>tickRate*3||pending.Count>=tickRate*2)return false;
            newest=input.sequence;pending.Enqueue(input);return true;
        }
        public SurvivalInput Next(){
            if(pending.Count>0){held=pending.Dequeue();stale=0;return held;}
            // Lost packets must not leave jump/look edges or a stuck movement key.
            held.jump=held.toggleAssist=held.toggleGear=false;held.lookX=held.lookY=0;
            if(++stale>6){held.x=held.y=held.z=held.roll=0;held.boost=false;held.brake=true;}return held;
        }
        public void Clear(){pending.Clear();held=default;stale=0;}
    }
    public sealed class SurvivalTickClock {
        double remaining;public long Tick {get;private set;} public double Backlog=>remaining;
        public void Restore(long tick){if(tick<0||Tick!=0||remaining!=0)throw new InvalidOperationException("Restore the server clock before starting it");Tick=tick;}
        public int Advance(double elapsed,int rate,Action step){
            if(!SurvivalServerConfig.Finite(elapsed)||elapsed<0)throw new ArgumentOutOfRangeException(nameof(elapsed));
            remaining+=Math.Min(elapsed,.25);double dt=1.0/rate;int count=0;
            while(remaining+1e-12>=dt&&count<8){remaining-=dt;Tick++;count++;step();}return count;
        }
    }
    public sealed class SurvivalEntityTickBudget {
        long current=long.MinValue;readonly HashSet<long> advanced=new HashSet<long>();
        public bool TryAdvance(long tick,long entity){if(tick!=current){advanced.Clear();current=tick;}return advanced.Add(entity);}
    }
    // Delivery state changes only after the enclosing reliable snapshot was sent.
    public sealed class SurvivalInterestDelivery {
        public struct Fingerprint {
            public string kind,data;public FramePose pose;
            public bool Same(Fingerprint other)=>kind==other.kind&&data==other.data&&pose.body==other.pose.body&&pose.frame==other.pose.frame&&pose.x==other.pose.x&&pose.y==other.pose.y&&pose.z==other.pose.z&&pose.yaw==other.pose.yaw;
        }
        public sealed class Candidate {public long id,pendingSince,stagedAt;public Fingerprint signature;public int priority,bytes;public double distance;}
        readonly Dictionary<long,Fingerprint> delivered=new Dictionary<long,Fingerprint>();
        readonly Dictionary<long,long> deliveredAt=new Dictionary<long,long>();
        readonly Dictionary<long,long> pending=new Dictionary<long,long>();
        public bool IsCurrent(long id,Fingerprint signature){if(delivered.TryGetValue(id,out var previous)&&previous.Same(signature)){pending.Remove(id);return true;}return false;}
        public Candidate Stage(long id,string signature,long tick,int priority,double distance,int bytes)=>Stage(id,new Fingerprint{data=signature},tick,priority,distance,bytes);
        public Candidate Stage(long id,Fingerprint signature,long tick,int priority,double distance,int bytes){
            if(IsCurrent(id,signature))return null;
            if(!pending.TryGetValue(id,out long since)){since=tick;pending[id]=since;}
            return new Candidate{id=id,signature=signature,pendingSince=since,stagedAt=tick,priority=priority,distance=distance,bytes=bytes};
        }
        public static List<Candidate> Select(List<Candidate> candidates,long tick,int tickRate,int byteBudget){
            int Rank(Candidate c)=>Math.Max(0,c.priority-(int)Math.Min(int.MaxValue,Math.Max(0,tick-c.pendingSince)/Math.Max(1,tickRate*2L)));
            candidates.Sort((a,b)=>{int order=Rank(a).CompareTo(Rank(b));if(order==0)order=a.pendingSince.CompareTo(b.pendingSince);if(order==0)order=a.priority.CompareTo(b.priority);if(order==0)order=a.distance.CompareTo(b.distance);return order==0?a.id.CompareTo(b.id):order;});
            var selected=new List<Candidate>();foreach(var candidate in candidates)if(candidate.bytes>0&&candidate.bytes<=byteBudget){selected.Add(candidate);byteBudget-=candidate.bytes;}return selected;
        }
        public void Commit(IEnumerable<Candidate> changes){foreach(var change in changes){if(deliveredAt.TryGetValue(change.id,out long newer)&&newer>change.stagedAt)continue;delivered[change.id]=change.signature;deliveredAt[change.id]=change.stagedAt;pending.Remove(change.id);}}
        public List<long> Removals(ISet<long> desired,int maximum=256){var result=new List<long>();foreach(long id in delivered.Keys)if(!desired.Contains(id))result.Add(id);result.Sort();if(result.Count>maximum)result.RemoveRange(maximum,result.Count-maximum);return result;}
        public void CommitRemovals(IEnumerable<long> removed){foreach(long id in removed){delivered.Remove(id);deliveredAt.Remove(id);pending.Remove(id);}}
        public void RetainPending(ISet<long> desired){var removed=new List<long>();foreach(long id in pending.Keys)if(!desired.Contains(id))removed.Add(id);foreach(long id in removed)pending.Remove(id);}
    }
    public sealed class SurvivalInterestIndex {
        public struct Entry {public long id;public FramePose pose;public bool ship;public double extent;}
        readonly Dictionary<(int,long,long,long,long),List<Entry>> cells=new Dictionary<(int,long,long,long,long),List<Entry>>();
        readonly Dictionary<(int,long,long,long,long),List<Entry>> shipCells=new Dictionary<(int,long,long,long,long),List<Entry>>();
        readonly double size;double maximumExtent;
        public SurvivalInterestIndex(double cellSize=500){size=cellSize;}
        (int,long,long,long,long) Key(FramePose p)=>(p.body,p.frame,(long)Math.Floor(p.x/size),(long)Math.Floor(p.y/size),(long)Math.Floor(p.z/size));
        public void Clear(){cells.Clear();shipCells.Clear();maximumExtent=0;}
        public void Add(Entry entry){if(!SurvivalServerConfig.Finite(entry.extent)||entry.extent<0)throw new ArgumentOutOfRangeException(nameof(entry.extent));var key=Key(entry.pose);if(!cells.TryGetValue(key,out var bucket)){bucket=new List<Entry>();cells.Add(key,bucket);}bucket.Add(entry);maximumExtent=Math.Max(maximumExtent,entry.extent);if(entry.ship){if(!shipCells.TryGetValue(key,out var ships)){ships=new List<Entry>();shipCells.Add(key,ships);}ships.Add(entry);}}
        public void Query(FramePose observer,double radius,HashSet<long> result,bool shipsOnly=false){
            if(!SurvivalServerConfig.Finite(radius)||radius<0)throw new ArgumentOutOfRangeException(nameof(radius));
            var key=Key(observer);long r=(long)Math.Ceiling((radius+(shipsOnly?0:maximumExtent))/size);var buckets=shipsOnly?shipCells:cells;
            // Large ship-proxy queries visit occupied buckets, never millions of empty cells.
            if(r>8){foreach(var bucket in buckets.Values)foreach(var e in bucket)if((!shipsOnly||e.ship)&&observer.Distance(e.pose)<=radius+e.extent)result.Add(e.id);return;}
            for(long z=-r;z<=r;z++)for(long y=-r;y<=r;y++)for(long x=-r;x<=r;x++)if(buckets.TryGetValue((key.Item1,key.Item2,key.Item3+x,key.Item4+y,key.Item5+z),out var bucket))foreach(var e in bucket)if((!shipsOnly||e.ship)&&observer.Distance(e.pose)<=radius+e.extent)result.Add(e.id);
        }
    }
}
