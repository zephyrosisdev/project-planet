using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public enum PlanetCloudSystem { None, Scattered, Broken, Overcast }
    [Serializable] public sealed class PlanetWeather
    {
        public PlanetCloudSystem clouds=PlanetCloudSystem.Scattered;
        [Range(0,1)]public float coverage=.52f;
        [Min(0)]public float cloudAltitude=2200,cloudThickness=1600,windSpeed=25;
        [Range(.1f,3)]public float cloudDensity=1;
        [Range(.05f,2)]public float haze=.45f;
        public Color atmosphereColour=new Color(.18f,.4f,.85f),cloudColour=new Color(.95f,.97f,1);
        public bool HasClouds=>clouds!=PlanetCloudSystem.None&&coverage>0&&cloudThickness>0;
        internal static uint Hash(uint x){unchecked{x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;return x^(x>>16);}}
        static float Value(uint x)=>(Hash(x)&0xffffff)/16777216f;
        public static void Generate(PlanetDefinition body,bool? atmosphere=null,bool? breathable=null,float pressure=1,float oxygen=.21f){
            uint key=unchecked((uint)body.terrainSeed)^0xc3416b3du;
            body.hasAtmosphere=atmosphere??Value(key)>.28f;body.breathable=body.hasAtmosphere&&(breathable??Value(key+1)>.48f);
            body.surfacePressure=body.hasAtmosphere?(atmosphere.HasValue?pressure:Mathf.Lerp(.15f,2,Value(key+2))):0;body.oxygenLevel=body.breathable?oxygen:0;
            var weather=new PlanetWeather();float chance=Value(key+3);
            weather.clouds=!body.hasAtmosphere||chance<.25f?PlanetCloudSystem.None:chance<.6f?PlanetCloudSystem.Scattered:chance<.88f?PlanetCloudSystem.Broken:PlanetCloudSystem.Overcast;
            weather.coverage=weather.clouds==PlanetCloudSystem.None?0:weather.clouds==PlanetCloudSystem.Scattered?.42f:weather.clouds==PlanetCloudSystem.Broken?.62f:.82f;
            weather.cloudAltitude=Mathf.Clamp(body.radius*.018f,600,5000);weather.cloudThickness=Mathf.Clamp(body.radius*.013f,300,3000);weather.windSpeed=Mathf.Lerp(12,55,Value(key+4));
            weather.haze=Mathf.Lerp(.25f,.65f,Value(key+5));weather.cloudDensity=Mathf.Lerp(.65f,1.3f,Value(key+6));
            if(!body.breathable){weather.atmosphereColour=Color.Lerp(new Color(.72f,.35f,.13f),new Color(.33f,.27f,.7f),Value(key+7));weather.cloudColour=Color.Lerp(Color.white,new Color(.85f,.67f,.38f),Value(key+8)*.5f);}
            body.weather=weather;
        }
        internal static float NearDetail(float altitude,float atmosphereHeight)=>1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(atmosphereHeight*1.5f,atmosphereHeight*3,altitude));
        internal void Layer(PlanetDefinition body,out float bottom,out float thickness){bottom=Mathf.Clamp(cloudAltitude,body.atmosphereHeight*.03f,body.atmosphereHeight*.6f);thickness=Mathf.Clamp(cloudThickness,body.atmosphereHeight*.02f,body.atmosphereHeight*.3f);}
    }
}
