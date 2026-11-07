using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementCitizens {
        struct LeisureArea {public Vector4 centre,right,up,low;}
        GraphicsBuffer leisureAreas;LeisureArea[] leisureData;
        readonly Dictionary<int,SettlementLandTile> leisureTargets=new Dictionary<int,SettlementLandTile>();readonly List<int> leisureKeys=new List<int>();
        readonly Dictionary<int,int> leisureEntrances=new Dictionary<int,int>();int leisureRoadRevision=-1;
        bool TryLeisure(ColonyState c,ResidentState p,double time,RtsBuilding home,out int key,out int posture){key=-1;posture=7;var sim=world.Base.Civilisation;if(home==null||!sim.Settings.ambientSettlementLife||sim.Routine(c,p,time)!=ResidentRoutine.Community)return false;
            var parks=world.Base.LandUse.Parks;if(parks.Count==0)return false;long day=(long)Math.Floor(time/Math.Max(60,sim.Settings.settlementDaySeconds));var plan=new SettlementDailySchedule(p.id,p.workplace,day);ulong friends=unchecked((ulong)(p.household!=0?p.household:p.id/4)+(ulong)day*7919UL);int first=(int)(SettlementDailySchedule.Hash(friends)%(uint)parks.Count);
            for(int i=0;i<Math.Min(16,parks.Count);i++){int index=(first+i)%parks.Count;var tile=parks[index];if(tile.faction!=c.faction||CubeSphere.ArcDistance(CubeSphere.Decode(home.position),world.Base.Grid.Direction(tile.cell),world.definition.radius)>Math.Min(1200,sim.Settings.civilianDrawDistance))continue;key=-2-index;if(Entrance(key,tile)<0)continue;posture=7+plan.parkPose;if(!leisureTargets.ContainsKey(key)&&leisureKeys.Count<Math.Min(32,Math.Max(1,goalCapacity/2))){leisureTargets[key]=tile;leisureKeys.Add(key);}return leisureTargets.ContainsKey(key);}return false;
        }
        int Entrance(int key,SettlementLandTile tile){var local=world.Base;if(leisureRoadRevision!=local.Roads.Revision){leisureEntrances.Clear();leisureRoadRevision=local.Roads.Revision;}if(leisureEntrances.TryGetValue(key,out int cached))return cached;
            for(int radius=1;radius<=8;radius++)for(int edge=0;edge<4;edge++)for(int s=-radius;s<=radius;s++){int x=tile.cell.x+(edge<2?(edge==0?-radius:radius):s),y=tile.cell.y+(edge<2?s:(edge==2?-radius:radius));if(x<0||y<0||x>=local.Grid.Resolution||y>=local.Grid.Resolution)continue;int node=local.Roads.Node(local.Grid.Direction(new PlanetNavCoordinate(world.definition.bodyId,tile.cell.face,x,y)));if(node>=0&&(int)local.Roads.Nodes[node].position.w==local.CombatTeam(tile.faction)){leisureEntrances[key]=node;return node;}}
            leisureEntrances[key]=-1;return -1;
        }
        Goal ParkGoal(int slot){int key=leisureKeys[slot];var tile=leisureTargets[key];var local=world.Base;var exact=local.Grid.PointPrecise(tile.cell.face,tile.cell.x+.5,tile.cell.y+.5);var d=exact.Float.normalized;var precise=new PlanetSurface(world.definition).ProjectToSurface(exact,.25);var high=precise.Float;var up=d.normalized;var right=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var forward=Vector3.Cross(right,up);double half=local.Grid.TileSize*.38;var surface=new PlanetSurface(world.definition);
            var east=surface.ProjectToSurface(exact+new Double3(right)*(half/world.definition.radius),.25);var west=surface.ProjectToSurface(exact-new Double3(right)*(half/world.definition.radius),.25);var north=surface.ProjectToSurface(exact+new Double3(forward)*(half/world.definition.radius),.25);var south=surface.ProjectToSurface(exact-new Double3(forward)*(half/world.definition.radius),.25);right=((east-west)*(1/(2*half))).Float;float northSlope=Vector3.Dot(((north-south)*(1/(2*half))).Float,up);var low=(precise-new Double3(high)).Float;
            leisureData[slot]=new LeisureArea{centre=new Vector4(high.x,high.y,high.z,local.Grid.TileSize*.38f),right=right,up=up,low=new Vector4(low.x,low.y,low.z,northSlope)};localGoals[key]=slot;return new Goal{node=Entrance(key,tile),team=local.CombatTeam(tile.faction),active=1,pad=1};
        }
    }
}
