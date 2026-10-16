using System;
using System.Collections.Generic;
namespace MassiveArmy.Planetary
{
    public enum PlayerControlMode { RTS,FirstPerson,GroundVehicle,Aircraft,Spacecraft,Turret }
    /// <summary>One ownership switch. No unit reconstruction or simulation-state reset.</summary>
    public sealed class PlayerControlModeManager
    {
        readonly Dictionary<PlayerControlMode,Action<bool>> bindings=new Dictionary<PlayerControlMode,Action<bool>>();
        public PlayerControlMode Mode {get;private set;}=PlayerControlMode.RTS;
        public uint ControlledEntityId {get;private set;}=uint.MaxValue;
        public event Action<PlayerControlMode> Changed;
        public void Bind(PlayerControlMode mode,Action<bool> activate){bindings.Add(mode,activate);}
        public bool Switch(PlayerControlMode mode,uint entityId=uint.MaxValue)
        {
            if(!bindings.TryGetValue(mode,out var next))return false;
            if(mode==Mode&&entityId==ControlledEntityId)return true;
            if(bindings.TryGetValue(Mode,out var previous))previous(false);
            Mode=mode;ControlledEntityId=entityId;next(true);Changed?.Invoke(mode);return true;
        }
    }
    public sealed class PlayerInventory
    {
        readonly Dictionary<string,int> items=new Dictionary<string,int>();
        public int MaxDistinctItems=32;
        public IReadOnlyDictionary<string,int> Items=>items;
        public bool Add(string id,int count)
        {
            if(string.IsNullOrWhiteSpace(id)||count<=0)return false;
            items.TryGetValue(id,out int old);if(old==0&&items.Count>=MaxDistinctItems)return false;
            if(old>int.MaxValue-count)return false;items[id]=old+count;return true;
        }
        internal void CopyFrom(PlayerInventory other){items.Clear();MaxDistinctItems=other.MaxDistinctItems;foreach(var item in other.items)items.Add(item.Key,item.Value);}
        public bool Consume(string id,int count){if(count<=0||!items.TryGetValue(id,out int old)||old<count)return false;if(old==count)items.Remove(id);else items[id]=old-count;return true;}
    }
    public sealed class PlayerEnvironmentState
    {
        public float Health=100,Oxygen=100,ConsumptionPerSecond=.5f;
        public bool Pressurized;
        public void Step(PlanetDefinition body,float altitude,float dt)
        {
            if(dt<=0)return;
            if(!Pressurized&&!body.BreathableAt(altitude)){Oxygen=UnityEngine.Mathf.Max(0,Oxygen-ConsumptionPerSecond*dt);if(Oxygen==0)Health=UnityEngine.Mathf.Max(0,Health-5*dt);}
        }
    }
}
