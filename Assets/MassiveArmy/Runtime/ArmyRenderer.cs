using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    public sealed class ArmyRenderer : IDisposable
    {
        readonly GpuBattle battle;
        readonly Mesh[] meshes;
        readonly Material[] materials;
        readonly Texture2D atlas;
        readonly Material formationMaterial,missileMaterial;
        readonly Mesh rocket;
        readonly CombatPresentation combatPresentation;
        internal CombatPresentation CombatEffects=>combatPresentation;
        readonly SceneryRenderer scenery;
        readonly ArtUnitRenderer modelArt;
        readonly CommandBuffer renderTiming=new CommandBuffer{name="Camera GPU timing"};
        Camera renderingCamera;
        public readonly uint[] IndexCounts;
        public ArmyRenderer(GpuBattle battle)
        {
            this.battle=battle;
            rocket=Resources.Load<Mesh>("MassiveArmy/CombatArt/Rocket");combatPresentation=new CombatPresentation(battle);
            if(battle.Settings.baseBuilding&&RtsModelArt.Load()!=null)modelArt=new ArtUnitRenderer(battle.ZombieType>=0);
            meshes=new[]{modelArt!=null?modelArt.NearMesh:ArmyMeshes.Tank(true),modelArt!=null?modelArt.FarMesh:ArmyMeshes.Tank(false),ArmyMeshes.Quad(),ArmyMeshes.Quad()};
            IndexCounts=new[]{meshes[0].GetIndexCount(0),meshes[1].GetIndexCount(0),meshes[2].GetIndexCount(0),meshes[3].GetIndexCount(0)};
            atlas=ArmyMeshes.Atlas(); var shader=Resources.Load<Shader>("MassiveArmy/Units");
            missileMaterial=new Material(Resources.Load<Shader>("MassiveArmy/StrategicMissiles")){enableInstancing=true};missileMaterial.SetBuffer("_Missiles",battle.MissileBuffer);missileMaterial.SetBuffer("_Fog",battle.FogBuffer);missileMaterial.SetInt("_FogRes",GpuBattle.FogResolution);missileMaterial.SetFloat("_WorldSize",battle.Field.Size);
            formationMaterial=new Material(Resources.Load<Shader>("MassiveArmy/FormationPreview")){enableInstancing=true};
            formationMaterial.SetBuffer("_PreviewSlots",battle.PreviewSlots);formationMaterial.SetBuffer("_Groups",battle.Navigation.Groups);
            formationMaterial.SetTexture("_Terrain",battle.Field.Terrain);formationMaterial.SetFloat("_WorldSize",battle.Field.Size);
            materials=new Material[4];
            for(int i=0;i<4;i++)
            {
                materials[i]=new Material(modelArt!=null&&i<3?Resources.Load<Shader>("MassiveArmy/ArtUnits"):shader){name="GPU tier "+i,enableInstancing=true};
                if(modelArt!=null&&i<3)modelArt.Bind(materials[i],i);
                materials[i].SetBuffer("_Traits",battle.TraitsBuffer);materials[i].SetInt("_BaseMode",battle.Settings.baseBuilding?1:0);
                materials[i].SetInt("_Mode",i);materials[i].SetInt("_Capacity",battle.Settings.capacity);
                materials[i].SetTexture("_WaterMap",battle.Field.WaterMap);materials[i].SetFloat("_WorldSize",battle.Settings.worldSize);materials[i].SetTexture("_Terrain",battle.Field.Terrain);
                materials[i].SetTexture("_Atlas",atlas);materials[i].SetBuffer("_Visible",battle.Visible);
                materials[i].SetBuffer("_VisibleProjectiles",battle.VisibleProjectiles);materials[i].SetBuffer("_Projectiles",battle.Projectiles);
            }
            scenery=new SceneryRenderer(battle.Field);
            RenderPipelineManager.beginCameraRendering+=BeginCamera;
            RenderPipelineManager.endCameraRendering+=EndCamera;
        }
        void BeginCamera(ScriptableRenderContext context,Camera camera)
        {
            if(camera!=renderingCamera)return;
            renderTiming.Clear();renderTiming.BeginSample(GpuSamples.Get("Army.Rendering"));context.ExecuteCommandBuffer(renderTiming);
        }
        void EndCamera(ScriptableRenderContext context,Camera camera)
        {
            if(camera!=renderingCamera)return;
            renderTiming.Clear();renderTiming.EndSample(GpuSamples.Get("Army.Rendering"));context.ExecuteCommandBuffer(renderTiming);context.Submit();
        }
        public void Render(Camera camera)
        {
            combatPresentation.Update(camera);
            renderingCamera=camera;
            var vp=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix;
            missileMaterial.SetInt("_Spectator",battle.Settings.spectatorMode?1:0);missileMaterial.SetVector("_RenderFocus",(WorldTopology.ViewFocus(camera)));formationMaterial.SetVector("_RenderFocus",(WorldTopology.ViewFocus(camera)));
            var bounds=new Bounds((WorldTopology.ViewFocus(camera)),new Vector3(battle.Field.Size*2,3000,battle.Field.Size*2));
            if(battle.PreviewActive)
            {
                formationMaterial.SetMatrix("_BattleVP",vp);
                var preview=new RenderParams(formationMaterial){worldBounds=bounds,camera=camera,shadowCastingMode=ShadowCastingMode.Off};
                Graphics.RenderMeshIndirect(preview,meshes[2],battle.PreviewArguments);
            }
            for(int i=0;i<4;i++)
            {
                var material=materials[i];material.SetBuffer("_Units",battle.Units);material.SetMatrix("_BattleVP",vp);
                material.SetVector("_CameraRight",camera.transform.right);material.SetVector("_CameraUp",camera.transform.up);
                material.SetVector("_CameraPosition",camera.transform.position);material.SetVector("_RenderFocus",(WorldTopology.ViewFocus(camera)));material.SetFloat("_TimeSeconds",Time.time);material.SetFloat("_RenderInterpolation",battle.RenderInterpolation);
                if(modelArt!=null)material.SetFloat("_ZombieTime",(battle.Tick-1f+battle.RenderInterpolation)*BattleSettings.Tick);
                var rp=new RenderParams(material){worldBounds=bounds,camera=camera,shadowCastingMode=battle.Settings.shadows&&i<2?ShadowCastingMode.On:ShadowCastingMode.Off,receiveShadows=battle.Settings.shadows};
                Graphics.RenderMeshIndirect(rp,meshes[i],battle.Arguments,1,i);
            }
            if(battle.Settings.baseBuilding)Graphics.RenderMeshPrimitives(new RenderParams(missileMaterial){worldBounds=bounds,camera=camera,shadowCastingMode=ShadowCastingMode.Off},rocket,0,32);
            if(battle.Settings.trees)scenery.Render(camera,vp,battle.Settings.shadows);
        }
        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering-=BeginCamera;RenderPipelineManager.endCameraRendering-=EndCamera;
            combatPresentation.Dispose();renderTiming.Dispose();scenery.Dispose();
            foreach(var m in materials) ArmyLifetime.Destroy(m);
            ArmyLifetime.Destroy(formationMaterial);ArmyLifetime.Destroy(missileMaterial);
            if(modelArt!=null){modelArt.Dispose();ArmyLifetime.Destroy(meshes[2]);ArmyLifetime.Destroy(meshes[3]);}else foreach(var m in meshes) ArmyLifetime.Destroy(m);
            ArmyLifetime.Destroy(atlas);
        }
    }
}
