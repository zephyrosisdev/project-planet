using System.Collections.Generic;

namespace MassiveArmy.Planetary
{
    /// <summary>Pronounceable seeded names. Independent of the random stream used for planet generation.</summary>
    public static class PlanetNames
    {
        static readonly string[] Starts={"Tar","Bel","Cal","Dar","El","Fal","Gal","Hal","Il","Jar","Kel","Lor","Mar","Nel","Or","Pal","Quar","Ral","Sel","Tal","Ul","Val","Wel","Xan","Yar","Zel","Aer","Bren","Cor","Dre","Eld","Fen","Glen","Har","Is","Jun","Kael","Lum","Mer","Nor","Ost","Per","Ren","Sol","Thal","Tor","Vey","Wyn"};
        static readonly string[] Middles={"a","e","i","o","u","al","an","ar","el","en","er","il","in","or","ra","ri","va","ve","lo","na"};
        static readonly string[] Ends={"lon","don","ton","ron","ria","lia","nia","via","ora","ara","era","ion","ian","ius","is","os","us","eth","oth","en","an","on","or","ar","el","ell","elle","aine","vale","mere","wyn","ward","den","din","dell","len","lin","lis","ris","rin","ven","vin","ver","ves","las","los","mon","nor"};
        static uint Mix(uint value){unchecked{value^=value>>16;value*=0x7feb352d;value^=value>>15;value*=0x846ca68b;return value^(value>>16);}}
        public static string Generate(int seed,int ordinal,int variant=0){
            uint value=Mix(unchecked((uint)seed+(uint)ordinal*0x9e3779b9u+(uint)variant*0x85ebca6bu));
            string first=Starts[value%(uint)Starts.Length];value=Mix(value+0x9e3779b9u);
            string middle=(value&3)==0?Middles[(value>>2)%(uint)Middles.Length]:"";value=Mix(value+0x9e3779b9u);
            return first+middle+Ends[value%(uint)Ends.Length];
        }
        public static string Unique(int seed,int ordinal,HashSet<string> used){for(int variant=0;;variant++){string name=Generate(seed,ordinal,variant);if(used.Add(name))return name;}}
        public static bool Placeholder(string name)=>string.IsNullOrWhiteSpace(name)||name=="Home planet"||name=="Development planet"||name.StartsWith("Planet ");
    }
}
