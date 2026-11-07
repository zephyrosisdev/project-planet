using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    // A bounded nearby field list, rendered procedurally. No objects per crop or tractor.
    internal sealed class SettlementFields:IDisposable {
        struct Field {public Vector4 centre,low,right,forward,up,crop;}
        readonly PlanetDevelopment world;readonly Material material;readonly List<SettlementLandTile> selected=new List<SettlementLandTile>();
        readonly Dictionary<long,Field> geometry=new Dictionary<long,Field>();
        GraphicsBuffer buffer;Field[] data;int scan,capacity,count,revision=-1,terrainRevision=-1,clumps;double next;Vector3 lastCamera;
        internal SettlementFields(PlanetDevelopment world){this.world=world;material=new Material(Resources.Load<Shader>("MassiveArmy/SettlementFields"));}
        internal void Update(){var sim=world.Base.Civilisation;var settings=sim.Settings;if(!settings.ambientSettlementLife||settings.visibleCropTiles==0){count=0;return;}if(Time.realtimeSinceStartupAsDouble<next)return;next=Time.realtimeSinceStartupAsDouble+.25;
            int wanted=Math.Clamp(settings.visibleCropTiles,1,512);if(capacity!=wanted){buffer?.Dispose();capacity=wanted;buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,capacity,96);data=new Field[capacity];material.SetBuffer("_Fields",buffer);revision=-1;}
            Vector3 camera=world.RenderToBody(world.View.transform.position).Float;
            if(revision!=world.Base.LandUse.Revision||terrainRevision!=world.definition.Edits.Revision||(camera-lastCamera).sqrMagnitude>4096){revision=world.Base.LandUse.Revision;terrainRevision=world.definition.Edits.Revision;lastCamera=camera;selected.Clear();geometry.Clear();scan=0;}
            var all=world.Base.LandUse.Tiles;double range=(settings.cropDrawDistance+world.definition.ReliefBound)* (settings.cropDrawDistance+world.definition.ReliefBound);
            // Scan a bounded page; no sorting or height sampling across every farm field.
            for(int probe=0;probe<Math.Min(256,all.Count);probe++){var tile=all[scan++%all.Count];if(tile.kind!=SettlementLandKind.Farm||selected.Contains(tile))continue;double distance=Vector3.SqrMagnitude(world.Base.Grid.Direction(tile.cell)*world.definition.radius-camera);if(distance>range)continue;
                int at=0;while(at<selected.Count&&Vector3.SqrMagnitude(world.Base.Grid.Direction(selected[at].cell)*world.definition.radius-camera)<distance)at++;if(at<capacity){selected.Insert(at,tile);if(selected.Count>capacity){geometry.Remove(selected[capacity].id);selected.RemoveAt(capacity);}}}
            count=0;clumps=Math.Clamp(settings.cropClumpsPerSide,2,12);var surface=new PlanetSurface(world.definition);
            var cameraPrecise=world.RenderToBody(world.View.transform.position);
            for(int i=0;i<Math.Min(capacity,selected.Count);i++){var tile=selected[i];if(!geometry.TryGetValue(tile.id,out var frame)){var exact=world.Base.Grid.PointPrecise(tile.cell.face,tile.cell.x+.5,tile.cell.y+.5);var d=exact.Float.normalized;var p=surface.ProjectToSurface(exact,.25);var high=p.Float;var up=d.normalized;var right=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var forward=Vector3.Cross(right,up);double half=world.Base.Grid.TileSize*.47;
                    var east=surface.ProjectToSurface(exact+new Double3(right)*(half/world.definition.radius),.25);var west=surface.ProjectToSurface(exact-new Double3(right)*(half/world.definition.radius),.25);var north=surface.ProjectToSurface(exact+new Double3(forward)*(half/world.definition.radius),.25);var south=surface.ProjectToSurface(exact-new Double3(forward)*(half/world.definition.radius),.25);
                    frame=new Field{centre=new Vector4(high.x,high.y,high.z,(float)half),low=(p-new Double3(high)).Float,right=((east-west)*(1/(2*half))).Float,forward=((north-south)*(1/(2*half))).Float,up=up};geometry[tile.id]=frame;}
                if((new Double3((Vector3)frame.centre)+new Double3((Vector3)frame.low)-cameraPrecise).Magnitude>settings.cropDrawDistance)continue;var colour=tile.crop=="wheat"?new Color(.72f,.65f,.25f):new Color(.3f,.6f,.18f);frame.crop=new Vector4((float)tile.field.growth,(float)tile.harvestAt,colour.r,colour.g);data[count++]=frame;
            }if(count>0)buffer.SetData(data,0,0,count);
        }
        internal void Draw(CommandBuffer cmd){if(count==0||buffer==null)return;var centre=world.RenderToBody(Vector3.zero)*-1;var high=centre.Float;material.SetVector("_CentreHigh",high);material.SetVector("_CentreLow",(centre-new Double3(high)).Float);material.SetVector("_Camera",world.View.transform.position);material.SetFloat("_Clock",(float)world.Base.Civilisation.RenderTime);material.SetInt("_Clumps",clumps);material.SetFloat("_HarvestSeconds",Mathf.Max(1,world.Base.Civilisation.Settings.harvestSweepSeconds));material.SetMatrix("_VP",GL.GetGPUProjectionMatrix(world.View.projectionMatrix,true)*world.View.worldToCameraMatrix);cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,clumps*clumps*12+252,count);}
        internal void Release(){buffer?.Dispose();buffer=null;capacity=count=0;selected.Clear();geometry.Clear();revision=-1;}
        public void Dispose(){Release();ArmyLifetime.Destroy(material);}
    }
}
