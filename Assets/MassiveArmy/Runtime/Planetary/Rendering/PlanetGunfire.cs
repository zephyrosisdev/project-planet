using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetEncounter {
        const int ShotCapacity=2048;
        GraphicsBuffer shots,shotCounter;Material shotMaterial;
        internal uint ShotCountForValidation {get {var value=new uint[1];shotCounter.GetData(value);return value[0];}}
        void InitializeGunfire(){shots=Buffer(ShotCapacity,48);shots.SetData(new Vector4[ShotCapacity*3]);shotCounter=Buffer(2,4);shotCounter.SetData(new uint[2]);foreach(string kernel in new[]{"Fight","CivicFindTargets","ApplyPersonalStrike"}){int fight=shader.FindKernel(kernel);shader.SetBuffer(fight,"_Shots",shots);shader.SetBuffer(fight,"_ShotCounter",shotCounter);}shotMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetGunfire"));}
        readonly uint[] clearShotBudget={0};
        void BeginGunfireTick(){shotCounter.SetData(clearShotBudget,0,1,1);shader.SetVector("_ShotCameraLocal",world.ObserverPosition.RelativeTo(world.definition.position).Float);}
        public void DrawGunfire(CommandBuffer cmd){
            shotMaterial.SetBuffer("_Shots",shots);shotMaterial.SetFloat("_ShotTick",world.SimulationTicks);shotMaterial.SetFloat("_ShotRadius",world.definition.radius);shotMaterial.SetVector("_ShotCentre",world.PlanetRenderCentre);shotMaterial.SetMatrix("_ShotVP",GL.GetGPUProjectionMatrix(world.View.projectionMatrix,true)*world.View.worldToCameraMatrix);shotMaterial.SetVector("_ShotScreen",new Vector4(world.View.pixelWidth,world.View.pixelHeight,0,0));
            cmd.DrawProcedural(Matrix4x4.identity,shotMaterial,0,MeshTopology.Triangles,ShotCapacity*6);
        }
        void DisposeGunfire(){shots?.Dispose();shotCounter?.Dispose();ArmyLifetime.Destroy(shotMaterial);}
    }
}
