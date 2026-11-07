using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.EditorTools {
    public sealed class PlanetDaylightPreviewWindow:EditorWindow {
        PlanetDevelopment target;Slider hour;Label status;Button play;DropdownField duration;bool syncing;
        [MenuItem("Synthetic Front/Planets/Atmosphere time preview")]
        static void Open(){var window=GetWindow<PlanetDaylightPreviewWindow>();window.titleContent=new GUIContent("Atmosphere time");window.minSize=new Vector2(390,290);window.Show();}
        public void CreateGUI(){
            var root=rootVisualElement;root.style.paddingLeft=root.style.paddingRight=root.style.paddingTop=root.style.paddingBottom=12;
            root.Add(new Label("CUSTOM ATMOSPHERE · DAYLIGHT PREVIEW"){style={unityFontStyleAndWeight=FontStyle.Bold}});
            var help=new Label("Run a planet in Play mode. Sweep its sun through a full day to inspect scattering, horizon colour and shadows. Gameplay time stays unchanged.");help.style.whiteSpace=WhiteSpace.Normal;help.style.marginTop=help.style.marginBottom=10;root.Add(help);
            status=new Label();status.style.whiteSpace=WhiteSpace.Normal;root.Add(status);
            hour=new Slider("Local solar hour",0,24){showInputField=true};hour.RegisterValueChangedCallback(e=>{if(!syncing&&FindTarget()){target.DaylightPreviewRunning=false;target.SetDaylightPreviewHour(e.newValue);}});root.Add(hour);
            var presets=new VisualElement();presets.style.flexDirection=FlexDirection.Row;root.Add(presets);
            foreach(var preset in new[]{("Dawn",6f),("Noon",12f),("Dusk",18f),("Night",0f)}){var value=preset.Item2;presets.Add(new Button(()=>{if(FindTarget()){target.DaylightPreviewRunning=false;target.SetDaylightPreviewHour(value);Sync();}}){text=preset.Item1});}
            duration=new DropdownField("Full day takes",new System.Collections.Generic.List<string>{"15 seconds","30 seconds","60 seconds","120 seconds","300 seconds"},2);root.Add(duration);duration.RegisterValueChangedCallback(_=>{if(target!=null)target.DaylightPreviewCycleSeconds=CycleSeconds();});
            var actions=new VisualElement();actions.style.flexDirection=FlexDirection.Row;actions.style.marginTop=12;root.Add(actions);
            play=new Button(()=>{if(!FindTarget()||!target.BeginDaylightPreview())return;target.DaylightPreviewCycleSeconds=CycleSeconds();target.DaylightPreviewRunning=!target.DaylightPreviewRunning;Sync();}){text="PLAY DAY CYCLE"};actions.Add(play);
            actions.Add(new Button(()=>{if(target!=null)target.StopDaylightPreview();Sync();}){text="RESTORE ACTUAL SUN"});
            root.Add(new Button(PlanetLiveWeatherEditor.SelectWeather){text="EDIT ACTIVE ATMOSPHERE SETTINGS"});
            root.schedule.Execute(Sync).Every(100);Sync();
        }
        float CycleSeconds()=>new[]{15,30,60,120,300}[Mathf.Clamp(duration.index,0,4)];
        bool FindTarget(){
            PlanetDevelopment active=null;if(EditorApplication.isPlaying){active=Object.FindFirstObjectByType<PlanetSystemRuntime>()?.ActiveWorld;if(active==null)foreach(var world in Object.FindObjectsByType<PlanetDevelopment>(FindObjectsSortMode.None))if(world.CanPreviewDaylight){active=world;break;}}
            if(active!=target){if(target!=null)target.StopDaylightPreview();target=active;}return target!=null&&target.Ready;
        }
        void Sync(){if(status==null)return;bool ready=FindTarget();hour.SetEnabled(ready);play.SetEnabled(ready);if(!ready){status.text="Waiting for an active planet in Play mode.";return;}syncing=true;hour.SetValueWithoutNotify(target.DaylightPreviewHour);syncing=false;play.text=target.DaylightPreviewRunning?"PAUSE DAY CYCLE":"PLAY DAY CYCLE";var weather=target.definition.weather;status.text=target.definition.displayName+" · "+(weather?.physicalAtmosphere==true?"Rayleigh / aerosol scattering":"Legacy tinted haze")+"\n"+(target.DaylightPreviewActive?$"Preview {target.DaylightPreviewHour:00.00} h · one day / {target.DaylightPreviewCycleSeconds:0} real seconds":"Actual celestial sun · preview disabled");}
        void OnDisable(){if(target!=null)target.StopDaylightPreview();}
    }
}
