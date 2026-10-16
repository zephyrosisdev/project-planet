using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassiveArmy
{
    // Immutable terrain/deployment data, built once while loading. Never a CPU unit mirror.
    public sealed class DeploymentMap
    {
        public const float Blocked=1e20f;
        public readonly WorldMap Map;
        public readonly float[] Costs;
        public readonly float[] Clearance;
        public readonly int ConnectedCells;
        public readonly int[] Components;
        readonly int n;
        public static DeploymentMap Create(BattleSettings settings,out Vector2[] slots)
        {
            if(settings.preview!=null){slots=settings.baseBuilding?new Vector2[settings.activeUnits]:settings.preview.Deployment.Slots(settings.activeUnits,settings.preview.Starts);if(slots==null)throw new InvalidOperationException("Requested army does not fit this world. Choose a larger map or lower army count.");return settings.preview.Deployment;}
            var map=new WorldMap(settings.navigationResolution,settings.worldSize,settings.seed,settings.world);
            var deployment=new DeploymentMap(map);slots=settings.baseBuilding?new Vector2[settings.activeUnits]:deployment.Slots(settings.activeUnits);
            if(slots==null)throw new InvalidOperationException("Requested army does not fit this world. Choose a larger map or lower army count.");return deployment;
        }
        public DeploymentMap(WorldMap map)
        {
            Map=map;n=map.Height.Length;Costs=new float[n*5];Clearance=new float[n];Components=new int[n];
            for(int m=0;m<5;m++) {
                for(int i=0;i<n;i++) {
                    float depth=map.WaterData(i).g,slope=map.Slope[i];
                    bool blocked=depth>.8f||slope>(m==0?1.2f:m==1?.45f:.6f)||map.RockObstacle[i];
                    Costs[m*n+i]=m==3?1:m==4?(depth>2?1:Blocked):blocked?Blocked:
                        Mathf.Lerp(1+slope*2+map.Forest[i]*(m==0?.6f:m==1?2.5f:3.5f)+(map.Water[i]>0?2:0),m==0?.85f:.55f,map.Road[i]);
                }
                // Preserve every traversable island and label wrapped connected components.
                if(m==2)LabelComponents();
            }
            var queue=new int[n];int head=0,tail=0;
            for(int i=0;i<n;i++) {
                if(Costs[2*n+i]>=Blocked*.5f) { Clearance[i]=0;queue[tail++]=i; }
                else { Clearance[i]=n;ConnectedCells++; }
            }
            while(head<tail) {
                int i=queue[head++],x=i%map.Resolution,y=i/map.Resolution;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++) {
                    int xx=WorldTopology.Cell(x+dx,map.Resolution),yy=WorldTopology.Cell(y+dy,map.Resolution);
                    int j=xx+yy*map.Resolution;if(Clearance[j]>Clearance[i]+1) { Clearance[j]=Clearance[i]+1;queue[tail++]=j; }
                }
            }
            // A soft bank/rock margin makes the integration route prefer open ground before reaching a corner.
            for(int m=0;m<3;m++)for(int i=0;i<n;i++)if(Costs[m*n+i]<Blocked*.5f)
                Costs[m*n+i]+=Mathf.Max(0,4-Clearance[i])*.65f;
        }
        void LabelComponents()
        {
            int r=Map.Resolution,offset=2*n,label=0;
            var labels=Components;var queue=new int[n];
            for(int start=0;start<n;start++) {
                if(labels[start]!=0||Costs[offset+start]>=Blocked*.5f)continue;
                int head=0,tail=1;queue[0]=start;labels[start]=++label;
                while(head<tail) {
                    int i=queue[head++],x=i%r,y=i/r;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++) {
                        int xx=WorldTopology.Cell(x+dx,r),yy=WorldTopology.Cell(y+dy,r);if(dx==0&&dy==0)continue;
                        int j=xx+yy*r;if(labels[j]!=0||Costs[offset+j]>=Blocked*.5f)continue;
                        if(dx!=0&&dy!=0&&(Costs[offset+WorldTopology.Index(x+dx,y,r)]>=Blocked*.5f||Costs[offset+WorldTopology.Index(x,y+dy,r)]>=Blocked*.5f))continue;
                        labels[j]=label;queue[tail++]=j;
                    }
                }
            }
        }
        public bool Safe(Vector2 p)
        {
            // Covers the visible tracks' half-diagonal, plus a margin (larger than the contact radius).
            const float radius=2.3f; // Extra 0.1m over GPU movement clearance avoids cell-edge rounding differences.
            for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                if(Costs[2*n+Map.Index(p+new Vector2(x,y)*radius)]>=Blocked*.5f)return false;
            return true;
        }
        public Vector2[] Slots(int count,Vector2[] starts=null)
        {
            if(count<0)throw new ArgumentOutOfRangeException(nameof(count));
            int cells=Mathf.FloorToInt(Map.Size/BattleSettings.FormationSpacing);
            if((long)cells*cells<count)return null;
            float pitch=Map.Size/cells;var result=new Vector2[count];var used=new HashSet<int>();
            int[] needs={(count+1)/2,count/2};int offset=0;
            for(int team=0;team<2;team++){
                Vector2 anchor=starts!=null?starts[team]:Map.Sites[team==0?0:8];int ax=WorldTopology.Cell(Mathf.FloorToInt((anchor.x/Map.Size+.5f)*cells),cells),ay=WorldTopology.Cell(Mathf.FloorToInt((anchor.y/Map.Size+.5f)*cells),cells);
                int found=0;
                void TrySlot(int dx,int dy){
                    if(found>=needs[team])return;
                    int x=WorldTopology.Cell(ax+dx,cells),y=WorldTopology.Cell(ay+dy,cells),index=x+y*cells;if(used.Contains(index))return;
                    Vector2 p=new Vector2((x+.5f)*pitch-Map.Size*.5f,(y+.5f)*pitch-Map.Size*.5f);
                    if(!Safe(p))return;used.Add(index);result[offset+found++]=p;
                }
                TrySlot(0,0);
                // Visit only each ring perimeter, keeping million-slot deployment O(area).
                for(int ring=1;ring<=cells/2+1&&found<needs[team];ring++){
                    for(int dx=-ring;dx<=ring&&found<needs[team];dx++){TrySlot(dx,-ring);TrySlot(dx,ring);}
                    for(int dy=-ring+1;dy<ring&&found<needs[team];dy++){TrySlot(-ring,dy);TrySlot(ring,dy);}
                }
                if(found<needs[team])return null;offset+=found;
            }
            return result;
        }
    }
}
