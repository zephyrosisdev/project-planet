using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>One sky draw for the catalogue. Resolved discs and sky points share an ID and smoothly change angular size.</summary>
    public sealed class GalaxySkyRenderer:IDisposable
    {
        struct Star {public Vector4 position,colour,properties;}
        readonly GalaxyModel galaxy;readonly int[] nodes;readonly Star[] records;readonly GraphicsBuffer buffer;readonly Material material;
        readonly GalaxyBackdrop backdrop;
        UniversePosition anchor;double lastRefresh=-1e9;bool initialized;int count;
        public int RefreshCount {get;private set;}
        public int StarCount=>count;
        internal GalaxyBackdrop BackdropForValidation=>backdrop;
        public GalaxySkyRenderer(GalaxyModel galaxy){
            this.galaxy=galaxy;int total=0;foreach(var body in galaxy.Nodes)if(body.kind==CelestialKind.Star||body.kind==CelestialKind.BlackHole)total++;
            nodes=new int[total];records=new Star[total];for(int i=0;i<galaxy.Nodes.Length;i++)if(galaxy.Nodes[i].kind==CelestialKind.Star||galaxy.Nodes[i].kind==CelestialKind.BlackHole)nodes[count++]=i;
            buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,total,48);material=new Material(Resources.Load<Shader>("MassiveArmy/GalaxySky"));material.SetBuffer("_GalaxyStars",buffer);
            backdrop=new GalaxyBackdrop(galaxy);
        }
        internal bool NeedsRefresh(UniversePosition observer,double clock)=>!initialized||clock-lastRefresh>=Math.Max(1,galaxy.Settings.skyRefreshSeconds)&&((observer.RelativeTo(anchor).Magnitude>=galaxy.Settings.skyTravelThreshold)||clock-lastRefresh>=60);
        internal void Update(Camera view,UniversePosition observer,PlanetDefinition foreground){
            backdrop.Update(view,observer);
            double clock=Time.realtimeSinceStartupAsDouble;bool refresh=NeedsRefresh(observer,clock);
            if(refresh){anchor=observer;lastRefresh=clock;initialized=true;RefreshCount++;
                for(int i=0;i<count;i++){var body=galaxy.Nodes[nodes[i]];var d=galaxy.Origin.Add(galaxy.Current[nodes[i]].position).RelativeTo(anchor).Float;
                    records[i]=new Star{position=new Vector4(d.x,d.y,d.z,(float)body.radius),colour=body.colour,properties=new Vector4(body.kind==CelestialKind.BlackHole?1:0,(int)body.stellarClass,0,0)};
                }
            }
            int firstDirty=count,lastDirty=-1;
            // Nearby ephemerides and parallax follow the observer every frame; far entries use the throttled catalogue.
            for(int i=0;i<count;i++){
                var d=galaxy.Origin.Add(galaxy.Current[nodes[i]].position).RelativeTo(observer);
                bool near=d.Magnitude<galaxy.Settings.resolvedStarDistance*1.5;
                if(near){var f=d.Float;records[i].position=new Vector4(f.x,f.y,f.z,(float)galaxy.Nodes[nodes[i]].radius);records[i].properties.w=1;firstDirty=Math.Min(firstDirty,i);lastDirty=i;}
                else if(records[i].properties.w>0){var f=galaxy.Origin.Add(galaxy.Current[nodes[i]].position).RelativeTo(anchor).Float;records[i].position=new Vector4(f.x,f.y,f.z,(float)galaxy.Nodes[nodes[i]].radius);records[i].properties.w=0;firstDirty=Math.Min(firstDirty,i);lastDirty=i;}
            }
            if(refresh)buffer.SetData(records);else if(lastDirty>=firstDirty)buffer.SetData(records,firstDirty,firstDirty,lastDirty-firstDirty+1);
            material.SetVector("_GalaxyObserverDelta",observer.RelativeTo(anchor).Float);
            material.SetMatrix("_GalaxyVP",GL.GetGPUProjectionMatrix(view.projectionMatrix,true)*view.worldToCameraMatrix);
            material.SetVector("_GalaxyCamera",view.transform.position);material.SetVector("_GalaxyRight",view.transform.right);material.SetVector("_GalaxyUp",view.transform.up);
            material.SetVector("_GalaxyScreen",new Vector4(view.pixelWidth,view.pixelHeight,Mathf.Tan(view.fieldOfView*Mathf.Deg2Rad*.5f),0));
            var foregroundOffset=foreground.position.RelativeTo(observer).Float;material.SetVector("_GalaxyForeground",new Vector4(foregroundOffset.x,foregroundOffset.y,foregroundOffset.z,foreground.radius+Mathf.Max(foreground.ReliefBound,foreground.oceanHeight)));
        }
        public void Draw(CommandBuffer cmd){if(initialized){backdrop.Draw(cmd);cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,6,count);}}
        public void Dispose(){backdrop.Dispose();buffer.Dispose();ArmyLifetime.Destroy(material);}
    }
}
