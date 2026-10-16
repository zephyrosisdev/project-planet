using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MassiveArmy
{
    // Must match ArmyCommon.hlsl. ID is the stable index; no CPU mirror is maintained.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct UnitState
    {
        public Vector2 position, previous, velocity, destination;
        public float facing, turret, cooldown;
        public uint health, packed, flags, target, group;
        public const int Stride = 64;
    }

    public enum MovementClass { Infantry, LightVehicle, HeavyVehicle, Air, Water }
    public enum ArmyOrder { Move, AttackMove, Attack, Stop }
    public enum SelectionOperation { Replace, Add, Remove }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct UnitArchetype
    {
        public float speed, attackRange, acquisitionRange, cooldown;
        public uint health, damage, movement, weapon;
        public float turnSpeed, radius, projectileSpeed, aimTolerance;
        public uint model, sprite;
        public float nearDistance, farDistance;
        public const int Stride = 64;
        public static UnitArchetype Tank => new UnitArchetype {
            speed = 16, attackRange = 62, acquisitionRange = 82, cooldown = 1.8f,
            health = 1000, damage = 180, movement = (uint)MovementClass.HeavyVehicle,
            weapon = 1, turnSpeed = 3, radius = 2.1f, projectileSpeed = 120,
            aimTolerance = .22f, nearDistance = 380, farDistance = 1000
        };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct GroupOrder
    {
        public Vector2 destination, localOrigin;
        public uint sequence, movement, order, team;
        public Vector2 right;
        public float spacing, width;
        public uint count, columns;
        public float arrivalRadius;
        public uint padding;
        public const int Stride = 64;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ProjectileState
    {
        public Vector3 position;
        public float lifetime;
        public Vector3 velocity;
        public uint target;
        public uint damage, team, alive, owner;
        public const int Stride = 48;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct UnitOperations
    {
        public float fuel,ammo,timer;
        public uint state;
        public Vector2 home,mission;
        public uint homeBuilding,carrier,payload,capacity;
        public const int Stride=48;
    }

    [Serializable]
    public sealed class BattleSettings
    {
        public int seed = 73129;
        public WorldGenerationSettings world=new WorldGenerationSettings();
        [NonSerialized] public WorldSetupResult preview;
        public int activeUnits = 50000, capacity = 50000;
        public int projectileCapacity = 32768;
        public float worldSize = 4096;
        public int navigationResolution = 256, spatialResolution = 128;
        public int targetSlices = 16, neighbourBudget = 48, targetBudget = 192;
        public bool movement = true, navigation = true, spatialGrid = true;
        public bool targeting = true, combat = true, projectiles = true, selection = true;
        public bool terrain = true, trees = true, vfx = true, meshes = true, impostors = true;
        public bool shadows=true;
        public bool enemyAI = true, simulationLod = true, crowdPressure = true;
        public bool baseBuilding,logistics,swarmMode,spectatorMode;
        public int aiCyanProfile=7,aiRedProfile=7;
        public int ambientZombies=100000;
        public bool ZombiesEnabled=>baseBuilding&&(ambientZombies>0||swarmMode);
        public const int Groups = 18, CoarseResolution = 32, LocalResolution = 64;
        public const int PreviewGroup = Groups;
        public const float FormationSpacing = 5.2f, FormationArrivalRadius = 7;
        public static float RequiredWorldSize(int units) => Mathf.Max(2048,Mathf.Ceil(Mathf.Sqrt(Mathf.Max(1,units))*FormationSpacing*2/64)*64);
        public void ScaleWorld()
        {
            worldSize=world.size;seed=world.seed;
            navigationResolution=worldSize>4096?512:256;
            spatialResolution=Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(worldSize/16)),128,256);
        }
        public const float Tick = 1f / 30;
        public void Validate()
        {
            ambientZombies=Mathf.Clamp(ambientZombies,0,100000);
            activeUnits = Mathf.Clamp(activeUnits, 1, 1000000);
            capacity = Mathf.Clamp(Mathf.Max(capacity, activeUnits), activeUnits, 1000000);
            spatialResolution = Mathf.Clamp(Mathf.NextPowerOfTwo(spatialResolution), 16, 256);
            navigationResolution = Mathf.Clamp(Mathf.NextPowerOfTwo(navigationResolution), 64, 512);
            projectileCapacity = Mathf.Clamp(projectileCapacity, 256, 131072);
            targetSlices = Mathf.Clamp(targetSlices, 1, 64);
        }
    }

    public static class MemoryBudget
    {
        public static long Bytes(BattleSettings s)
        {
            long cells = (long)s.spatialResolution * s.spatialResolution;
            long nav = (long)s.navigationResolution * s.navigationResolution;
            long contactResolution=Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(s.worldSize/4)),64,1024);
            long contactCells=contactResolution*contactResolution;
            long constructionGrid=BuildingGrid.Resolution(s.worldSize);
            return GpuBattle.MaxBuildings*16L+(s.baseBuilding&&(s.ambientZombies>0||s.swarmMode)?nav*(28+56+8)+16384*4+1024*16+256*4+8*8+28:0)+(s.baseBuilding?FogWakeField.ResourceBytes+nav*(s.ZombiesEnabled?10:5)*4+constructionGrid*constructionGrid*4:4)+s.capacity * (128L + 4 + 4 + 12 + 4 + 8 + 8 + 4) + s.activeUnits*8L + 47*4
                + s.projectileCapacity * (48L + 4)
                + cells * 12 + ScanBytes(cells)
                + contactCells*12 + ScanBytes(contactCells) + (long)GpuCrowd.GridResolution(s.worldSize)*GpuCrowd.GridResolution(s.worldSize)*28 + ScanBytes(s.activeUnits)
                + nav * (5 * 4 + 16 + 4 + 4 + 4 + 8 + 16)
                + 36000L*36 + 7*20
                + BattleSettings.Groups * (nav*8 + BattleSettings.CoarseResolution*BattleSettings.CoarseResolution*4L)
                + (BattleSettings.Groups+1) * GroupOrder.Stride + BattleSettings.Groups*4
                + s.capacity*88L + 2L*GpuBattle.FogResolution*GpuBattle.FogResolution*12 + GpuBattle.MaxBuildings*92L + 32*48 + 64*36 + 1024*8 + 64*8 + 2*cells*4
                + 4096*16 + 20 + 128*4 + 4*20 + 5*4 + (s.baseBuilding?(s.ZombiesEnabled?32:31):1)*(UnitArchetype.Stride+UnitTraits.Stride) + 4;
        }
        static long ScanBytes(long count)
        {
            long bytes=0;
            do { count=(count+511)/512;bytes+=count*8; }while(count>1);
            return bytes;
        }

        public static string Report(BattleSettings s) => $"{Bytes(s) / 1048576.0:F1} MiB buffers + maps (meshes, HDRP, RTs excluded)";
    }
}
