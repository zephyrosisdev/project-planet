using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    // Roads paint terrain directly. Only railway tracks retain independently published meshes.
    internal sealed class SettlementRoadRenderer:IDisposable {
        internal const int ChunkTiles=128,FirstPublishTiles=32;
        readonly SettlementRoadSurface paint;readonly PlanetDevelopment world;readonly PlanetSurface surface;readonly Dictionary<int,Mesh> meshes=new Dictionary<int,Mesh>();
        readonly Plane[] planes=new Plane[6];IEnumerator build;int road=-1,rail=-1,terrain=-1;bool disposed;
        internal int PublishedChunks=>meshes.Count;
        internal SettlementRoadRenderer(PlanetDevelopment world){this.world=world;paint=new SettlementRoadSurface(world);surface=new PlanetSurface(world.definition);}
        internal bool Ready=>build==null&&road==world.Base.Roads.Revision&&rail==world.Base.Rails.Revision&&terrain==world.definition.Edits.Revision;
        internal void Update(){
            if(disposed)return;paint.Update();var local=world.Base;
            // Finish the current snapshot rather than restarting forever while settlements add roads.
            if(build==null&&(rail!=local.Rails.Revision||terrain!=world.definition.Edits.Revision)){
                road=local.Roads.Revision;rail=local.Rails.Revision;terrain=world.definition.Edits.Revision;build=Build();
            }
            road=local.Roads.Revision;if(build==null)return;if(!Advance(build,local.Civilisation.Settings.roadMeshBudgetMilliseconds)){(build as IDisposable)?.Dispose();build=null;}
        }
        internal static bool Advance(IEnumerator iterator,double budgetMilliseconds){double start=Stopwatch.GetTimestamp(),budget=Math.Max(.1,budgetMilliseconds)*Stopwatch.Frequency/1000.0;for(int work=0;work<32&&Stopwatch.GetTimestamp()-start<budget;work++)if(!iterator.MoveNext())return false;return true;}
        internal static bool PublishPartial(bool hasCompleteChunk,int built,int total)=>!hasCompleteChunk&&built==FirstPublishTiles&&total>FirstPublishTiles;
        internal static Mesh Upload(string name,List<Vector3> vertices,List<Vector3> normals,List<Vector3> low,List<Vector4> uv,List<Vector2> metrics,List<Color> colours,List<int> indices){var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colours);mesh.SetUVs(0,uv);mesh.SetUVs(1,metrics);mesh.SetUVs(2,low);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;}
        IEnumerator Build(){
            var rails=new List<RoadTile>(world.Base.Rails.Tiles);var chunks=new List<(int key,bool rail,int first,int count,float distance,int offset)>();var camera=world.RenderToBody(world.View.transform.position).Float;
            void Add(List<RoadTile> tiles,bool track){for(int first=0;first<tiles.Count;first+=ChunkTiles){int count=Math.Min(ChunkTiles,tiles.Count-first);float distance=float.MaxValue;int offset=0;foreach(int sample in new[]{first,first+count/2,first+count-1}){var tile=tiles[sample];var point=world.Base.Grid.Direction(tile.cell)*(world.definition.radius+tile.height);float candidate=(point-camera).sqrMagnitude;if(candidate<distance){distance=candidate;offset=sample-first;}}chunks.Add(((track?1<<29:0)+first/ChunkTiles,track,first,count,distance,offset));}}
            Add(rails,true);chunks.Sort((a,b)=>{int d=a.distance.CompareTo(b.distance);return d!=0?d:a.key.CompareTo(b.key);});var live=new HashSet<int>();foreach(var chunk in chunks)live.Add(chunk.key);
            var obsolete=new List<int>();foreach(var pair in meshes)if(!live.Contains(pair.Key))obsolete.Add(pair.Key);foreach(int key in obsolete){ArmyLifetime.Destroy(meshes[key]);meshes.Remove(key);}
            foreach(var chunk in chunks){var vertices=new List<Vector3>();var normals=new List<Vector3>();var low=new List<Vector3>();var uv=new List<Vector4>();var metrics=new List<Vector2>();var colours=new List<Color>();var indices=new List<int>();var tiles=rails;
                bool hasCompleteChunk=meshes.ContainsKey(chunk.key);
                for(int i=0;i<chunk.count;i++){Append(world.Base.Grid,surface,tiles[chunk.first+(chunk.offset+i)%chunk.count],chunk.rail,vertices,normals,low,uv,metrics,colours,indices);if(PublishPartial(hasCompleteChunk,i+1,chunk.count))Publish();yield return null;}
                Publish();yield return null;
                void Publish(){var mesh=Upload("Streamed road chunk "+chunk.key,vertices,normals,low,uv,metrics,colours,indices);if(meshes.TryGetValue(chunk.key,out var previous))ArmyLifetime.Destroy(previous);meshes[chunk.key]=mesh;}
            }
        }
        internal static void Append(PlanetConstructionGrid grid,PlanetSurface surface,RoadTile tile,bool rail,List<Vector3> vertices,List<Vector3> normals,List<Vector3> low,List<Vector4> uv,List<Vector2> metrics,List<Color> colours,List<int> indices){
            int steps=Math.Max(2,Mathf.CeilToInt(grid.TileSize/4)),start=vertices.Count;var p00=surface.ProjectToSurface(grid.PointPrecise(tile.cell.face,tile.cell.x,tile.cell.y),.22);var p10=surface.ProjectToSurface(grid.PointPrecise(tile.cell.face,tile.cell.x+1,tile.cell.y),.22);var p01=surface.ProjectToSurface(grid.PointPrecise(tile.cell.face,tile.cell.x,tile.cell.y+1),.22);var span=new Vector2((float)(p10-p00).Magnitude,(float)(p01-p00).Magnitude);
            for(int y=0;y<=steps;y++)for(int x=0;x<=steps;x++){float u=x/(float)steps,v=y/(float)steps;var d=grid.PointPrecise(tile.cell.face,tile.cell.x+u,tile.cell.y+v);var p=surface.ProjectToSurface(d,.22);var high=p.Float;vertices.Add(high);low.Add((p-new Double3(high)).Float);normals.Add(d.Float);uv.Add(new Vector4(u,v,rail?9:8,tile.connections));metrics.Add(span);colours.Add(Color.white);}
            for(int y=0;y<steps;y++)for(int x=0;x<steps;x++){int a=start+y*(steps+1)+x;indices.Add(a);indices.Add(a+steps+1);indices.Add(a+1);indices.Add(a+1);indices.Add(a+steps+1);indices.Add(a+steps+2);}
        }
        internal void Draw(CommandBuffer commands,Material material){if(meshes.Count==0)return;GeometryUtility.CalculateFrustumPlanes(world.View,planes);float maxDistance=world.Base.Grid.CarriagewayWidth*Mathf.Max(1,world.View.pixelHeight)/(2*Mathf.Tan(world.View.fieldOfView*Mathf.Deg2Rad*.5f)*.35f);foreach(var mesh in meshes.Values){var bounds=mesh.bounds;bounds.center+=world.PlanetRenderCentre;bounds.Expand(64);if(bounds.SqrDistance(world.View.transform.position)>maxDistance*maxDistance)continue;if(GeometryUtility.TestPlanesAABB(planes,bounds))commands.DrawMesh(mesh,Matrix4x4.identity,material);}}
        internal void Release(){paint.Release();(build as IDisposable)?.Dispose();build=null;foreach(var mesh in meshes.Values)ArmyLifetime.Destroy(mesh);meshes.Clear();road=rail=terrain=-1;}
        public void Dispose(){disposed=true;Release();paint.Dispose();}
    }
}
