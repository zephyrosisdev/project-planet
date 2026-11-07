using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Planetary {
    internal sealed partial class PlanetIndustry {
        internal static Double3 DepositPosition(PlanetSurface surface,Vector3 focus,int index,int seed=0,int attempt=0){
            var anchor=surface.GetSurfacePositionPrecise(focus);var up=PlanetPrecisionPolicy.Direction(anchor);var axis=Mathf.Abs(up.y)>.9f?Vector3.right:Vector3.up;var east=Vector3.Cross(axis,up).normalized;var north=Vector3.Cross(up,east);
            var random=new System.Random(unchecked(seed*397+index*7919+attempt*104729));double angle=index*2.3999632297+random.NextDouble()*.65+(seed%1009)*.0062+attempt*.73,radius=90+index*40+random.NextDouble()*65+attempt*18;
            return surface.ProjectToSurface(anchor+new Double3(east)*(Math.Cos(angle)*radius)+new Double3(north)*(Math.Sin(angle)*radius));
        }
        bool TryDepositPosition(Vector3 focus,int index,List<FramePose> occupied,out FramePose result){
            for(int attempt=0;attempt<64;attempt++){
                var p=DepositPosition(surface,focus,index,world.terrainSeed,attempt);result=Pose(p);
                if(surface.SampleHeight(p)<world.definition.oceanHeight+.2||Authority.World.HaulBlocked(result,0)!=null)continue;
                bool clear=true;foreach(var other in occupied)if(result.Distance(other)<80){clear=false;break;}if(!clear)continue;
                var up=PlanetPrecisionPolicy.Direction(p);var east=Vector3.Cross(Mathf.Abs(up.y)>.9f?Vector3.right:Vector3.up,up).normalized;var north=Vector3.Cross(up,east);
                for(int n=0;n<8;n++){double a=n*Math.PI/4;var q=p+new Double3(east)*(Math.Cos(a)*8)+new Double3(north)*(Math.Sin(a)*8);if(surface.SampleHeight(q)<world.definition.oceanHeight+.2||Math.Abs(surface.ProjectToSurface(q).Magnitude-p.Magnitude)>3){clear=false;break;}}
                if(clear)return true;
            }result=default;return false;
        }
        void SpreadLegacyDeposits(){
            if(surface==null)return;var legacy=new List<SurfaceDeposit>();foreach(var d in Authority.World.deposits)if(d.pose.body==world.definition.bodyId&&d.pose.frame==0&&d.layoutVersion==0)legacy.Add(d);
            if(legacy.Count<2)return;var anchor=legacy[0].pose;foreach(var d in legacy)if(anchor.Distance(d.pose)>40)return;
            var focus=PlanetPrecisionPolicy.Direction(Body(anchor));var occupied=new List<FramePose>();foreach(var d in Authority.World.deposits)if(!legacy.Contains(d))occupied.Add(d.pose);var positions=new Dictionary<long,FramePose>();
            for(int i=0;i<legacy.Count;i++){if(!TryDepositPosition(focus,i,occupied,out var pose)){message="Resource spread deferred: no clear dry site found";return;}positions.Add(legacy[i].id,pose);occupied.Add(pose);}
            foreach(var machine in Authority.World.machines)if(machine.haul?.active==true&&positions.ContainsKey(machine.haul.source)){var owner=Authority.World.Actor(machine.owner);Authority.Execute(owner.id,new IndustrialCommand{sequence=owner.lastSequence+1,kind="haul-stop",target=machine.id});}
            Authority.RepositionSurfaceDeposits(world.definition.bodyId,d=>positions.TryGetValue(d.id,out var at)?at:d.pose,1);message="Resource sites spread out; use Auto operation / Replan for collection";
        }
        void SeedDeposits(Vector3 focus,bool missingSuppliesOnly=false){
            var occupied=new List<FramePose>();foreach(var d in Authority.World.deposits)occupied.Add(d.pose);
            for(int n=0;n<6;n++){
                if(missingSuppliesOnly&&(n<3||Authority.World.deposits.Exists(d=>d.pose.body==world.definition.bodyId&&d.resource==(n==3?"stone":n==4?"fuel":"water"))))continue;
                if(!TryDepositPosition(focus,n,occupied,out var pose)){message="No clear dry site for resource "+n+"; try another landing region";continue;}
                var material=new MaterialLot{litres=50000};if(n==0)material.iron=10000;if(n==1)material.copper=10000;if(n==2){material.radioactive=5000;material.activity=25000;}
                Authority.World.deposits.Add(new SurfaceDeposit{id=Authority.World.AllocateId(),layoutVersion=1,pose=pose,remaining=material,resource=n==3?"stone":n==4?"fuel":n==5?"water":"paydirt"});occupied.Add(pose);
            }
        }
    }
}
