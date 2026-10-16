using System.Collections.Generic;
using UnityEngine.Profiling;

namespace MassiveArmy
{
    static class GpuSamples
    {
        static readonly Dictionary<string,CustomSampler> samples=new Dictionary<string,CustomSampler>();
        public static CustomSampler Get(string name)
        {
            if(!samples.TryGetValue(name,out var sampler)) { sampler=CustomSampler.Create(name,true);samples.Add(name,sampler); }
            return sampler;
        }
    }
}
