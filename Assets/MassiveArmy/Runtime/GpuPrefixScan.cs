using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    public sealed class GpuPrefixScan : IDisposable
    {
        readonly ComputeShader shader;
        readonly GraphicsBuffer sums, offsets;
        readonly GpuPrefixScan parent;
        readonly int count, blocks, scan, add;
        public GpuPrefixScan(int count)
        {
            if (count < 1 || count > 1048576) throw new ArgumentOutOfRangeException(nameof(count));
            this.count = count; blocks = (count + 511) / 512;
            shader = UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PrefixScan"));
            sums = new GraphicsBuffer(GraphicsBuffer.Target.Structured, blocks, 4);
            offsets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, blocks, 4);
            if(blocks>1)parent=new GpuPrefixScan(blocks);
            scan = shader.FindKernel("ScanBlocks"); add = shader.FindKernel("AddSums");
        }
        public void Record(CommandBuffer cmd, GraphicsBuffer counts, GraphicsBuffer output)
        {
            cmd.SetComputeIntParam(shader, "_Count", count);
            cmd.SetComputeIntParam(shader, "_Blocks", blocks);
            cmd.SetComputeBufferParam(shader, scan, "_Counts", counts);
            cmd.SetComputeBufferParam(shader, scan, "_Offsets", output);
            cmd.SetComputeBufferParam(shader, scan, "_BlockSums", sums);
            cmd.DispatchCompute(shader, scan, blocks, 1, 1);
            if(parent==null)return;
            parent.Record(cmd,sums,offsets);
            cmd.SetComputeIntParam(shader,"_Count",count);
            cmd.SetComputeBufferParam(shader, add, "_Offsets", output);
            cmd.SetComputeBufferParam(shader, add, "_BlockOffsets", offsets);
            cmd.DispatchCompute(shader, add, (count + 255) / 256, 1, 1);
        }
        public void Dispose() { parent?.Dispose(); sums.Dispose(); offsets.Dispose(); ArmyLifetime.Destroy(shader); }
    }
}
