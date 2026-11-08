using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Networking {
    using Planetary;
    [Serializable] internal sealed class SurvivalUniverseManifest {
        public int version=1,seed;public string world,instance,system,galaxy,home;
    }
    // Definition-only Kepler scheduling. Rendering, terrain GPU allocation and
    // army/civilisation runtimes remain the responsibility of the viewed client.
    internal sealed class SurvivalUniverse:IDisposable {
        [Serializable] sealed class Parameters {
            public int seed,planetCount;public Vector2 radiusRange;public float spacing,scale,activationAltitude,sleepHysteresis,preparationDistance,detailActivationAltitude,detailActivationRadius,preparationRadius;
            internal static Parameters From(PlanetSystemDefinition d)=>new Parameters{seed=d.seed,planetCount=d.planetCount,radiusRange=d.radiusRange,spacing=d.spacing,scale=d.RuntimeSizeMultiplier,activationAltitude=d.activationAltitude,sleepHysteresis=d.sleepHysteresis,preparationDistance=d.preparationDistance,detailActivationAltitude=d.detailActivationAltitude,detailActivationRadius=d.detailActivationRadius,preparationRadius=d.preparationRadius};
            internal PlanetSystemDefinition Create(){var d=ScriptableObject.CreateInstance<PlanetSystemDefinition>();d.seed=seed;d.planetCount=planetCount;d.radiusRange=radiusRange;d.spacing=spacing;d.RuntimeSizeMultiplier=scale;d.activationAltitude=activationAltitude;d.sleepHysteresis=sleepHysteresis;d.preparationDistance=preparationDistance;d.detailActivationAltitude=detailActivationAltitude;d.detailActivationRadius=detailActivationRadius;d.preparationRadius=preparationRadius;return d;}
        }
        internal readonly SurvivalUniverseState State;internal readonly GalaxyModel Galaxy;internal readonly PlanetDefinition[] Bodies;
        readonly PlanetSystemDefinition settings;readonly Dictionary<int,PlanetDefinition> definitions=new Dictionary<int,PlanetDefinition>();readonly Dictionary<int,PlanetSurface> surfaces=new Dictionary<int,PlanetSurface>();
        readonly CelestialState[] sweepStates;bool disposed;
        SurvivalUniverse(SurvivalUniverseState state,PlanetSystemDefinition settings,PlanetDefinition home,bool restore){
            State=state;this.settings=settings;
            if(settings.galaxy!=null&&settings.galaxy.enabledInGame){Galaxy=new GalaxyModel(settings.galaxy,settings,home,out var bodies);Bodies=bodies;sweepStates=new CelestialState[Galaxy.Nodes.Length];}
            else Bodies=settings.Generate(home);
            if(restore){
                if(state.bodies.Count!=Bodies.Length)throw new InvalidDataException("Saved universe body count does not match its generator");
                foreach(var body in Bodies){var saved=state.bodies.Find(b=>b.body==body.bodyId);if(saved==null||saved.json==null||saved.json.Length>1048576)throw new InvalidDataException("Saved universe body definition is missing or oversized");JsonUtility.FromJsonOverwrite(saved.json,body);Validate(body);}
            }
            foreach(var body in Bodies){Validate(body);if(definitions.ContainsKey(body.bodyId))throw new InvalidDataException("Duplicate universe body identity");definitions.Add(body.bodyId,body);}
            if(Galaxy!=null)foreach(var node in Galaxy.Nodes){node.stableId=Key(state.worldId,"celestial",node.id);if(node.planet>=0){var body=Bodies[node.planet];node.mu=body.gravitationalParameter;node.radius=body.radius;node.name=body.displayName;}}
            if(!restore)foreach(var body in Bodies)state.bodies.Add(new SurvivalBodyDefinition{body=body.bodyId,stableId=Key(state.worldId,"body",body.bodyId),json=JsonUtility.ToJson(body)});
            Advance(0);
        }
        static string Key(string world,string kind,int id)=>world.Length+":"+world+":"+kind+":"+id;
        static void Validate(PlanetDefinition d){if(d.bodyId<=0||!SurvivalServerConfig.Finite(d.radius)||d.radius<10||!SurvivalServerConfig.Finite(d.terrainHeight)||!SurvivalServerConfig.Finite(d.gravitationalParameter)||d.gravitationalParameter<=0)throw new InvalidDataException("Invalid canonical universe body");}
        internal static SurvivalUniverse Create(string world,string instance,int seed,PlanetDefinition home,float scale=1){
            if(string.IsNullOrWhiteSpace(world)||string.IsNullOrWhiteSpace(instance)||home==null||!SurvivalServerConfig.Finite(scale)||scale<1||scale>1000)throw new ArgumentException("Invalid universe configuration");
            var template=Resources.Load<PlanetSystemDefinition>("MassiveArmy/Planets/DefaultSystem");if(template==null)throw new InvalidOperationException("Default planetary system is missing");
            var settings=template.CreateScaledCopy(scale);var state=new SurvivalUniverseState{worldId=world,seed=seed,homeBody=home.bodyId,systemJson=JsonUtility.ToJson(Parameters.From(settings)),galaxyJson=settings.galaxy==null?null:JsonUtility.ToJson(settings.galaxy)};
            var universe=new SurvivalUniverse(state,settings,home,false);foreach(var body in universe.Bodies)state.regions.Add(new SurvivalRegionOwnership{body=body.bodyId,instance=instance});return universe;
        }
        internal static SurvivalUniverse Restore(SurvivalUniverseState state,string world,int seed){
            if(state==null||state.version!=1||state.generator!="kepler-1"||state.worldId!=world||state.seed!=seed||state.bodies==null||state.bodies.Count<1||state.bodies.Count>1536)throw new InvalidDataException("Saved universe identity, version or generator is incompatible");
            var parameters=JsonUtility.FromJson<Parameters>(state.systemJson);if(parameters==null||!SurvivalServerConfig.Finite(parameters.scale)||parameters.scale<1||parameters.scale>1000)throw new InvalidDataException("Invalid saved universe parameters");
            var settings=parameters.Create();if(!string.IsNullOrEmpty(state.galaxyJson)){settings.galaxy=ScriptableObject.CreateInstance<GalaxyDefinition>();JsonUtility.FromJsonOverwrite(state.galaxyJson,settings.galaxy);}
            if(state.regions==null||state.regions.Count!=state.bodies.Count)throw new InvalidDataException("Saved universe region ownership is incomplete");
            var ids=new HashSet<int>();foreach(var body in state.bodies)if(body==null||body.body<=0||!ids.Add(body.body)||body.stableId!=Key(world,"body",body.body))throw new InvalidDataException("Invalid saved body identity");
            var regions=new HashSet<int>();foreach(var region in state.regions)if(region==null||!ids.Contains(region.body)||!regions.Add(region.body)||string.IsNullOrWhiteSpace(region.instance)||region.epoch<1)throw new InvalidDataException("Invalid saved region ownership");
            var saved=state.bodies.Find(b=>b.body==state.homeBody);if(saved==null)throw new InvalidDataException("Saved home body is missing");var home=ScriptableObject.CreateInstance<PlanetDefinition>();JsonUtility.FromJsonOverwrite(saved.json,home);return new SurvivalUniverse(state,settings,home,true);
        }
        internal PlanetDefinition Definition(int body)=>definitions.TryGetValue(body,out var definition)?definition:null;
        internal SurvivalUniverseManifest Manifest(string instance)=>new SurvivalUniverseManifest{seed=State.seed,world=State.worldId,instance=instance,system=State.systemJson,galaxy=State.galaxyJson,home=State.bodies.Find(b=>b.body==State.homeBody).json};
        internal static SurvivalUniverse FromManifest(SurvivalUniverseManifest manifest){
            if(manifest==null||manifest.version!=1||string.IsNullOrWhiteSpace(manifest.world)||string.IsNullOrWhiteSpace(manifest.instance)||manifest.system?.Length>16384||manifest.galaxy?.Length>16384||manifest.home?.Length>16384)throw new InvalidDataException("Invalid universe manifest");
            var parameters=JsonUtility.FromJson<Parameters>(manifest.system);if(parameters==null||!SurvivalServerConfig.Finite(parameters.scale)||parameters.scale<1||parameters.scale>1000||parameters.planetCount<1||parameters.planetCount>1536)throw new InvalidDataException("Invalid universe generation parameters");
            var settings=parameters.Create();if(!string.IsNullOrEmpty(manifest.galaxy)){settings.galaxy=ScriptableObject.CreateInstance<GalaxyDefinition>();JsonUtility.FromJsonOverwrite(manifest.galaxy,settings.galaxy);}
            var home=ScriptableObject.CreateInstance<PlanetDefinition>();JsonUtility.FromJsonOverwrite(manifest.home,home);Validate(home);
            var state=new SurvivalUniverseState{worldId=manifest.world,seed=manifest.seed,homeBody=home.bodyId,systemJson=manifest.system,galaxyJson=manifest.galaxy};
            var result=new SurvivalUniverse(state,settings,home,false);foreach(var body in result.Bodies)state.regions.Add(new SurvivalRegionOwnership{body=body.bodyId,instance=manifest.instance});return result;
        }
        internal PlanetDefinition ApplyDefinition(string json){
            if(string.IsNullOrEmpty(json)||json.Length>16384)throw new InvalidDataException("Invalid region definition");var copy=ScriptableObject.CreateInstance<PlanetDefinition>();
            try{JsonUtility.FromJsonOverwrite(json,copy);Validate(copy);var target=Definition(copy.bodyId);if(target==null)throw new InvalidDataException("Unknown region identity");JsonUtility.FromJsonOverwrite(json,target);surfaces.Remove(target.bodyId);if(Galaxy!=null){var node=Galaxy.Nodes[Galaxy.NodeFor(target)];node.mu=target.gravitationalParameter;node.radius=target.radius;}return target;}finally{ArmyLifetime.Destroy(copy);}
        }
        internal PlanetSurface Surface(int body){if(surfaces.TryGetValue(body,out var surface))return surface;var definition=Definition(body);if(definition==null)return null;surface=new PlanetSurface(definition);surfaces.Add(body,surface);return surface;}
        internal bool Owns(int body,string instance){var region=State.regions.Find(r=>r.body==body);return region!=null&&region.instance==instance;}
        internal Vector3 SunDirection(int body){
            if(Galaxy==null)return new Vector3(1,.12f,1.3f).normalized;var definition=Definition(body);if(definition==null)return Vector3.zero;var position=Galaxy.Current[Galaxy.NodeFor(definition)].position;double strongest=-1;Double3 direction=default;
            for(int i=0;i<Galaxy.Nodes.Length;i++){var node=Galaxy.Nodes[i];if(node.kind!=CelestialKind.Star)continue;var delta=Galaxy.Current[i].position-position;double distance=Math.Max(1,delta.Magnitude),flux=node.mu/(distance*distance);if(flux>strongest){strongest=flux;direction=delta*(1/distance);}}
            return direction.Float;
        }
        internal void UpdatePowerEnvironments(IndustrialWorld world,IEnumerable<int> occupied){foreach(int body in occupied){var definition=Definition(body);if(definition==null)continue;var light=SunDirection(body);double wind=definition.hasAtmosphere?Math.Max(0,Math.Min(1,(definition.weather?.windSpeed??0)/25.0)):0;world.SetPowerEnvironment(body,light.x,light.y,light.z,wind);}}
        internal void Advance(double time){if(!SurvivalServerConfig.Finite(time)||time<0)throw new ArgumentOutOfRangeException(nameof(time));Galaxy?.Publish(time,Bodies);}
        internal int ClosestRegion(int current,Double3 local){
            if(Galaxy==null||Definition(current)==null)return current;int reference=Galaxy.NodeFor(Definition(current));var absolute=Galaxy.Current[reference].position+local;int chosen=current;double best=(absolute-Galaxy.Current[reference].position).Magnitude-Definition(current).radius;
            foreach(var body in Bodies){if(body.bodyId==current)continue;double altitude=(absolute-Galaxy.Current[Galaxy.NodeFor(body)].position).Magnitude-body.radius;if(altitude<best&&altitude<=settings.DetailAltitude(body)){best=altitude;chosen=body.bodyId;}}return chosen;
        }
        internal void Reframe(int from,int to,ref Double3 position,ref Double3 velocity){
            if(from==to)return;if(Galaxy==null||Definition(from)==null||Definition(to)==null)throw new InvalidOperationException("Unknown celestial reference frame");
            var a=Galaxy.Current[Galaxy.NodeFor(Definition(from))];var b=Galaxy.Current[Galaxy.NodeFor(Definition(to))];position+=a.position-b.position;velocity+=a.velocity-b.velocity;
        }
        // Warp uses a continuous sphere sweep, including moving celestial sources.
        // Padding by source travel is conservative across the fixed server step.
        internal double SweepCelestials(int current,Double3 from,Double3 to,double dt,out int target){
            target=current;if(Galaxy==null)return 1;int reference=Galaxy.NodeFor(Definition(current));var origin=Galaxy.Current[reference].position;var start=origin+from;var delta=to-from;double distance=delta.Magnitude;if(distance<1e-9)return 1;var direction=delta*(1/distance);double fraction=1;
            Galaxy.Evaluate(Galaxy.Time+dt,sweepStates);
            for(int i=0;i<Galaxy.Nodes.Length;i++){var node=Galaxy.Nodes[i];if(node.radius<=0||node.kind==CelestialKind.Barycentre)continue;var centre=Galaxy.Current[i].position;double radius=node.radius+Math.Max(6000,(sweepStates[i].position-centre).Magnitude*2);if(node.planet>=0){var body=Bodies[node.planet];radius=body.radius+Math.Max(body.ReliefBound+6000,body.hasAtmosphere?body.atmosphereHeight:0)+(sweepStates[i].position-centre).Magnitude*2;}
                var relative=start-centre;if(relative.Magnitude<=radius+2)continue;
                if(PlanetSpacecraft.CruiseSphereEntry(relative,direction,distance,radius,out double entry)){double next=Math.Max(0,(entry-1)/distance);if(next<fraction){fraction=next;if(node.planet>=0)target=Bodies[node.planet].bodyId;}}
            }return fraction;
        }
        public void Dispose(){if(disposed)return;disposed=true;foreach(var body in Bodies)ArmyLifetime.Destroy(body);if(settings.galaxy!=null)ArmyLifetime.Destroy(settings.galaxy);ArmyLifetime.Destroy(settings);surfaces.Clear();definitions.Clear();}
    }
}
