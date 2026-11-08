#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.Globalization;
using System.IO;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using UnityEngine;

namespace MassiveArmy.Networking {
    // Real UTP simulator layer below reliable delivery/DTLS. The obsolete NGO
    // DebugSimulator API has no effect in the pinned package and is not used.
    internal sealed class SurvivalNetworkConditions:INetworkStreamDriverConstructor {
        internal static int DelayMs,JitterMs,CreatedDrivers;internal static float LossPercent;
        internal static bool Active=>CreatedDrivers>0&&(DelayMs>0||JitterMs>0||LossPercent>0);
        static string Argument(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        internal static void Configure(){
            string delay=Argument("-survival-probe-delay"),jitter=Argument("-survival-probe-jitter"),loss=Argument("-survival-probe-loss");if(delay==null&&jitter==null&&loss==null)return;
            string path=Argument("-survival-probe-report")??Argument("-survival-load-report");string allowed=Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar;
            if(path==null||!Path.GetFullPath(path).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Network simulation requires an isolated diagnostic Live report");
            if(!int.TryParse(delay??"0",out int d)||d<0||d>500||!int.TryParse(jitter??"0",out int j)||j<0||j>250||!float.TryParse(loss??"0",NumberStyles.Float,CultureInfo.InvariantCulture,out float l)||float.IsNaN(l)||float.IsInfinity(l)||l<0||l>15)throw new ArgumentException("Invalid diagnostic network conditions");
            if(UnityTransport.s_DriverConstructor!=null&&!(UnityTransport.s_DriverConstructor is SurvivalNetworkConditions))throw new InvalidOperationException("Another custom transport constructor is already installed");
            DelayMs=d;JitterMs=j;LossPercent=l;UnityTransport.s_DriverConstructor=new SurvivalNetworkConditions();
        }
        public void CreateDriver(UnityTransport transport,out NetworkDriver driver,out NetworkPipeline unreliableFragmentedPipeline,out NetworkPipeline unreliableSequencedFragmentedPipeline,out NetworkPipeline reliableSequencedPipeline){
            if(transport.UseWebSockets)throw new InvalidOperationException("This diagnostic fixture requires the production UDP/DTLS transport");
            var settings=transport.GetDefaultNetworkSettings();
            try{
                settings.WithNetworkSimulatorParameters(sendPacketLossPercent:LossPercent,sendDelayMS:(uint)DelayMs,sendJitterMS:(uint)JitterMs);
                driver=NetworkDriver.Create(new UDPNetworkInterface(),settings);
                if(!driver.CurrentSettings.TryGet<NetworkSimulatorParameter>(out var actual)||actual.SendDelayMS!=DelayMs||actual.SendJitterMS!=JitterMs||actual.SendPacketLossPercent!=LossPercent){driver.Dispose();throw new InvalidOperationException("UTP did not retain the requested network simulation parameters");}
                transport.GetDefaultPipelineConfigurations(ref driver,out var unreliable,out var sequenced,out var reliable);
                unreliableFragmentedPipeline=driver.CreatePipeline(unreliable);unreliableSequencedFragmentedPipeline=driver.CreatePipeline(sequenced);reliableSequencedPipeline=driver.CreatePipeline(reliable);CreatedDrivers++;
                Debug.Log("Diagnostic UTP send conditions active: delay="+DelayMs+"ms, jitter="+JitterMs+"ms, loss="+LossPercent.ToString(CultureInfo.InvariantCulture)+"%");
            }finally{settings.Dispose();}
        }
    }
}
#endif
