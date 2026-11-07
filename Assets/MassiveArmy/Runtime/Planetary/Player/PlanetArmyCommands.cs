using System;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    /// <summary>GPU selection and group assignment. CPU reads only 42 aggregate command counters.</summary>
    public sealed class PlanetArmyCommands : IDisposable
    {
        readonly ComputeShader shader;
        readonly GraphicsBuffer counts,pick;
        readonly uint[] totals=new uint[BattleSettings.Groups*2+6],empty=new uint[BattleSettings.Groups*2+6];
        readonly uint[] noPick={uint.MaxValue},zeroPick={0};readonly float[] formationSpacing={12,12,12,12,12};
        readonly int pickKernel,selectKernel,countKernel,orderKernel;int dispatch;
        public int PlayerGroupCount=BattleSettings.Groups;
        public uint SelectedCount {get;private set;}
        public uint MovementRevision {get;private set;}
        public int FormationCount(int movement)=>(int)totals[BattleSettings.Groups*2+1+movement];
        public float FormationSpacing(int movement)=>formationSpacing[movement];
        public string Status {get;private set;}="Select units to issue orders";
        public PlanetArmyCommands(PlanetDefinition definition,GraphicsBuffer units,int count)
        {
            planetRadius=definition.radius;shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetCommands"));
            PlanetDevelopment.BindSurface(shader,definition);shader.SetInt("_PlanetUnitCount",count);dispatch=Mathf.Max(1,(count+127)/128);
            counts=new GraphicsBuffer(GraphicsBuffer.Target.Structured,totals.Length,4);pick=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4);
            pickKernel=shader.FindKernel("Pick");selectKernel=shader.FindKernel("Select");countKernel=shader.FindKernel("CountGroups");orderKernel=shader.FindKernel("Order");
            foreach(int k in new[]{pickKernel,selectKernel,countKernel,orderKernel,shader.FindKernel("RespondToSite"),shader.FindKernel("OrderSingleVehicle"),shader.FindKernel("OrderCompanion")})shader.SetBuffer(k,"_PlanetUnits",units);
            shader.SetBuffer(pickKernel,"_Pick",pick);shader.SetBuffer(selectKernel,"_Pick",pick);shader.SetBuffer(orderKernel,"_Pick",pick);shader.SetBuffer(countKernel,"_CommandCounts",counts);
        }
        public void SetCount(int count){dispatch=Mathf.Max(1,(count+127)/128);shader.SetInt("_PlanetUnitCount",count);}
        internal void EnableVehicles()=>shader.EnableKeyword("PLANET_VEHICLES");
        public void BindCatalog(GraphicsBuffer archetypes,GraphicsBuffer traits,RtsCatalog catalog=null){if(catalog!=null)foreach(var type in catalog.units){int movement=(int)type.movement;formationSpacing[movement]=Mathf.Max(formationSpacing[movement],type.Gpu.radius*2+2);}shader.EnableKeyword("PLANET_RTS_CONTENT");shader.SetBuffer(countKernel,"_Archetypes",archetypes);shader.SetBuffer(orderKernel,"_Archetypes",archetypes);shader.SetBuffer(shader.FindKernel("RespondToSite"),"_Archetypes",archetypes);shader.SetBuffer(pickKernel,"_Traits",traits);shader.SetBuffer(selectKernel,"_Traits",traits);}
        public void Select(Matrix4x4 planetToClip,Vector3 cameraLocal,Vector2 screenSize,Vector2 a,Vector2 b,int operation=0,bool click=false)
        {
            shader.SetMatrix("_SelectionVP",planetToClip);shader.SetVector("_SelectionCamera",cameraLocal);shader.SetVector("_SelectionScreen",screenSize);
            shader.SetVector("_SelectionRect",new Vector4(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y)));
            shader.SetInt("_SelectionOperation",operation);shader.SetInt("_ClickSelection",click?1:0);
            if(click){pick.SetData(noPick);shader.Dispatch(pickKernel,dispatch,1,1);}
            shader.Dispatch(selectKernel,dispatch,1,1);Count();Status=$"{SelectedCount} units selected";
        }
        void Count(){counts.SetData(empty);shader.Dispatch(countKernel,dispatch,1,1);counts.GetData(totals);SelectedCount=totals[BattleSettings.Groups*2];}
        public bool Move(PlanetPathfindingAdapter navigation,Vector3 direction,Func<int,bool> terrainValid=null,bool attackMove=false,Vector3? formationEnd=null)
        {
            shader.SetInt("_AttackMove",attackMove?1:0);Count();if(SelectedCount==0){Status="Select units to issue orders";return false;}
            var groups=new int[5];Array.Fill(groups,-1);var reserved=new bool[navigation.GroupCount];
            for(int m=0;m<5;m++)if(totals[BattleSettings.Groups*2+1+m]>0){
                if(terrainValid!=null&&!terrainValid(m)){Status="Destination is incompatible with the selected units' movement classes";return false;}
                int group=-1;for(int i=0;i<Mathf.Min(PlayerGroupCount,navigation.GroupCount);i++)if(!reserved[i]&&totals[i]>0&&totals[i]==totals[BattleSettings.Groups+i]){group=i;break;}
                if(group<0)for(int i=0;i<Mathf.Min(PlayerGroupCount,navigation.GroupCount);i++)if(!reserved[i]&&totals[i]==0){group=i;break;}
                if(group<0){Status="All command groups are occupied; select a whole group to retarget it";return false;}groups[m]=group;reserved[group]=true;
            }
            bool formation=formationEnd.HasValue;var start=direction;var end=formationEnd??direction;
            formation&=CubeSphere.TryFormationRight(start,end,out var formationRight);
            if(formation)direction=(start+end).normalized;
            shader.SetInt("_FormationEnabled",formation?1:0);shader.SetVector("_FormationRight",formationRight);
            shader.SetFloat("_FormationWidth",formation?CubeSphere.ArcDistance(start,end,planetRadius):0);
            float depth=0;
            for(int m=0;m<5;m++)if(groups[m]>=0){
                int count=FormationCount(m);float spacing=formationSpacing[m],width=formation?CubeSphere.ArcDistance(start,end,planetRadius):0;
                pick.SetData(zeroPick);shader.SetFloat("_FormationSpacing",spacing);shader.SetInt("_FormationCount",count);shader.SetFloat("_FormationDepth",depth);
                navigation.SetGoal(groups[m],direction,(MovementClass)m);navigation.SetFormation(groups[m],formationRight,spacing,width,formation?count:0);
                shader.SetInt("_CommandGroup",groups[m]);shader.SetInt("_CommandMovement",m);shader.SetInt("_Stop",0);shader.SetVector("_CommandGoal",CubeSphere.Encode(direction));shader.Dispatch(orderKernel,dispatch,1,1);
                if(formation){int columns=Mathf.Clamp(Mathf.FloorToInt(width/spacing)+1,1,count);depth+=((count+columns-1)/columns)*spacing+spacing;}
            }
            MovementRevision++;Status=$"Move order: {SelectedCount} units";return true;
        }
        readonly float planetRadius;
        public void RespondToSite(int team,Vector3 direction,float radius,float planetRadius){
            int kernel=shader.FindKernel("RespondToSite");shader.SetInt("_DefenceTeam",team);shader.SetVector("_DefenceSite",direction);shader.SetFloat("_DefenceChord",2*Mathf.Sin(Mathf.Min(Mathf.PI,radius/planetRadius)*.5f));shader.Dispatch(kernel,dispatch,1,1);
        }
        public void Stop(){MovementRevision++;shader.SetInt("_Stop",1);shader.Dispatch(orderKernel,dispatch,1,1);Count();Status=$"Stopped {SelectedCount} units";}
        public bool MoveVehicle(PlanetPathfindingAdapter navigation,uint id,MovementClass movement,Vector3 direction,bool stop=false){
            int kernel=shader.FindKernel("OrderSingleVehicle");int group=-1;
            if(!stop){Count();for(int i=0;i<Mathf.Min(PlayerGroupCount,navigation.GroupCount);i++)if(totals[i]==0){group=i;break;}if(group<0)return false;navigation.SetGoal(group,direction,movement);}
            shader.SetInt("_SingleVehicle",(int)id);shader.SetInt("_Stop",stop?1:0);shader.SetInt("_CommandGroup",Mathf.Max(0,group));shader.SetVector("_CommandGoal",CubeSphere.Encode(direction));shader.Dispatch(kernel,1,1,1);MovementRevision++;return true;
        }
        public void OrderCompanion(uint id,Vector3 direction,bool register){int kernel=shader.FindKernel("OrderCompanion");shader.SetInt("_SingleVehicle",(int)id);shader.SetInt("_RegisterCompanion",register?1:0);shader.SetVector("_CommandGoal",CubeSphere.Encode(direction));shader.Dispatch(kernel,1,1,1);}
        public void Dispose(){counts.Dispose();pick.Dispose();ArmyLifetime.Destroy(shader);}
    }
}
