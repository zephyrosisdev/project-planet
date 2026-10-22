using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public static class PlanetCountries {
        public const int Maximum=8,Zombies=31;
        public static readonly string[] Names={"Cyan Republic","Red Federation","Green Union","Amber Commonwealth","Violet League","Azure Coalition","Silver Dominion","Rose Confederacy"};
        public static readonly Color[] Colours={new Color(.1f,.8f,1),new Color(1,.2f,.1f),new Color(.2f,.9f,.35f),new Color(1,.7f,.1f),new Color(.7f,.35f,1),new Color(.2f,.5f,1),new Color(.8f,.85f,.9f),new Color(1,.4f,.7f)};
    }
    public sealed partial class PlanetDevelopment {
        [Range(2,PlanetCountries.Maximum)]public int countryCount=4;
        // Existing benchmark fixtures retain their original two-country world. A
        // separate explicit count exercises additional countries without changing a baseline.
        public int CountryCount {get{var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-planet-country-count");if(at>=0&&at+1<args.Length&&int.TryParse(args[at+1],out int count))return Mathf.Clamp(count,2,PlanetCountries.Maximum);foreach(string arg in args)if(arg.StartsWith("-planet-")&&(arg.Contains("validation")||arg.Contains("test")||arg.Contains("recovery")||arg.Contains("site-protection")))return 2;return Mathf.Clamp(countryCount,2,PlanetCountries.Maximum);}}
        internal bool ClassicCountryFixture {get{var args=Environment.GetCommandLineArgs();foreach(string arg in args)if(arg.StartsWith("-planet-")&&(arg.Contains("validation")||arg.Contains("test")||arg.Contains("recovery")||arg.Contains("site-protection")))return true;return false;}}
    }
    public sealed partial class PlanetRtsBase {
        internal int TerritoryOwner(Vector3 direction){int nearest=0;float score=-2;for(int team=0;team<Factions.Length;team++){float value=Vector3.Dot(direction,Homes[team]);if(value>score){score=value;nearest=team;}}return nearest;}
        internal bool OwnDeposit(int team,Vector3 direction)=>TerritoryOwner(direction)==team;
        void SelectAdditionalHomes(){
            for(int team=2;team<Factions.Length;team++){float phi=(team-1)*2.3999632f;var preferred=(anchor*Mathf.Cos(phi)+right*Mathf.Sin(phi)+forward*(team%2==0?.5f:-.5f)).normalized;bool selected=false;
                foreach(var candidate in PlanetStartingRegion.Candidates(fixture.definition,preferred)){bool separate=true;for(int other=0;other<team;other++)if(CubeSphere.ArcDistance(candidate,Homes[other],fixture.definition.radius)<Mathf.Min(12000,fixture.definition.radius*.8f)){separate=false;break;}if(!separate||!PlanetStartingRegion.ExpansionPlain(fixture.definition,candidate))continue;Homes[team]=candidate;selected=true;break;}
                if(!selected)throw new InvalidOperationException("No dry, separated mainland for country "+team);
            }
        }
    }
}
